using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク銃の主制御。
    /// ・照準方向が画面内なら fireInterval 秒間隔で自動発射
    /// ・弾はプリミティブ Sphere で生成（Blenderモデル不要）
    /// ・SphereCollider(isTrigger=true) + Rigidbody(kinematic) を弾に付与
    /// ・実際のペイントは InkBullet.OnTriggerEnter → PaintTarget.Paint() で行う
    /// </summary>
    public class InkGun : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("銃口オブジェクト")]
        [Tooltip("弾が飛び出す銃口 Transform（Ray の始点）")]
        [SerializeField] private Transform muzzlePoint;

        [Header("照準オブジェクト（PoseRotationDriver のターゲット）")]
        [Tooltip("スマホのジャイロで回転する照準オブジェクト")]
        [SerializeField] private Transform aimTransform;

        [Header("発射設定")]
        [Tooltip("何秒おきに1発発射するか")]
        [SerializeField] private float fireInterval = 0.3f;

        [Tooltip("弾の飛翔プレファブ（未設定時はプリミティブ Sphere を自動生成）")]
        [SerializeField] private GameObject inkProjectilePrefab;

        [Header("照準内外判定（度）")]
        [Tooltip("照準がこの角度以内ならば発射する")]
        [SerializeField] private float maxAimAngle = 45f;

        [Header("弾のサイズ")]
        [SerializeField] private float bulletScale = 0.2f;

        [Header("インク塗りブラシ半径（ピクセル単位）")]
        [Tooltip("弾が当たった中心から何ピクセル塗るか（例: 8 → 直径16px）")]
        [SerializeField] [Range(1, 64)] private int brushPixelRadius = 8;

        // ────────────────────────────────────────────────────────────
        // 内部参照・状態
        // ────────────────────────────────────────────────────────────

        private InkColorCycler _colorCycler;
        private Camera          _mainCamera;
        private float           _fireTimer = 0f;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _colorCycler = FindObjectOfType<InkColorCycler>();
            _mainCamera  = Camera.main;
        }

        private void Update()
        {
            // GameStateManager がなければ何もしない
            if (GameStateManager.Instance == null) return;

            // Playing 状態以外は撃てない
            if (GameStateManager.Instance.CurrentState != GameStateManager.GameState.Playing) return;

            // 照準が画面外を向いていれば発射しない
            if (!IsAimingAtScreen()) return;

            // 発射タイマー
            _fireTimer += Time.deltaTime;
            if (_fireTimer >= fireInterval)
            {
                _fireTimer = 0f;
                Fire();
            }
        }

        // ────────────────────────────────────────────────────────────
        // 照準判定
        // ────────────────────────────────────────────────────────────

        private bool IsAimingAtScreen()
        {
            if (aimTransform == null) return true; // 未設定時は常に有効（テスト用）

            Vector3 aimDir      = aimTransform.forward;
            Vector3 screenNormal = _mainCamera != null
                ? _mainCamera.transform.forward
                : Vector3.forward;

            return Vector3.Angle(aimDir, screenNormal) <= maxAimAngle;
        }

        // ────────────────────────────────────────────────────────────
        // 発射処理
        // ────────────────────────────────────────────────────────────

        private void Fire()
        {
            Vector3 origin    = muzzlePoint  != null ? muzzlePoint.position  : transform.position;
            Vector3 direction = aimTransform != null ? aimTransform.forward  : transform.forward;

            SpawnBullet(origin, direction);
        }

        private void SpawnBullet(Vector3 origin, Vector3 direction)
        {
            // ── オブジェクト生成 ──────────────────────────────────
            GameObject bulletGo;

            if (inkProjectilePrefab != null)
            {
                // プレファブが設定されていればそれを使う
                bulletGo = Instantiate(inkProjectilePrefab, origin, Quaternion.LookRotation(direction));
            }
            else
            {
                // 未設定の場合は Sphere プリミティブを動的生成
                bulletGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulletGo.transform.position   = origin;
                bulletGo.transform.rotation   = Quaternion.LookRotation(direction);
                bulletGo.transform.localScale = Vector3.one * bulletScale;

                // CreatePrimitive が付けるデフォルト SphereCollider を isTrigger に変更
                var sc = bulletGo.GetComponent<SphereCollider>();
                if (sc != null) sc.isTrigger = true;
            }

            // ── 色設定 ────────────────────────────────────────────
            Color inkColor = _colorCycler != null ? _colorCycler.CurrentColor : Color.white;
            var rend = bulletGo.GetComponent<Renderer>();
            if (rend != null)
            {
                // インスタンスマテリアルを作成して色を設定
                rend.material = new Material(rend.sharedMaterial);
                rend.material.color = inkColor;
            }

            // ── SphereCollider を Trigger に設定（プレファブ使用時の保険） ──
            var col = bulletGo.GetComponent<SphereCollider>();
            if (col == null) col = bulletGo.AddComponent<SphereCollider>();
            col.isTrigger = true;

            // ── Rigidbody (kinematic) ─────────────────────────────
            var rb = bulletGo.GetComponent<Rigidbody>();
            if (rb == null) rb = bulletGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            // ── InkBullet を付与して発射 ──────────────────────────
            var bullet = bulletGo.GetComponent<InkBullet>();
            if (bullet == null) bullet = bulletGo.AddComponent<InkBullet>();
            bullet.Initialize(direction, inkColor, brushPixelRadius);
        }
    }
}

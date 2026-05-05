using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の飛翔・当たり判定・ペイント処理。
    ///
    /// ▼ UV 取得の優先順位
    ///   1. MeshCollider → hit.textureCoord で正確に取得
    ///   2. BoxCollider/SphereCollider → Physics.Raycast で着弾ワールド座標を取得し UV 近似
    ///   3. すべて失敗 → 弾の現在ワールド座標から UV 近似
    ///
    /// ▼ 側面ヒット防止
    ///   カメラに向いている面のみペイントする。
    ///   着弾面の法線とカメラ→弾ベクトルが概ね逆向きなら「正面ヒット」と判定する。
    /// </summary>
    public class InkBullet : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [SerializeField] private float speed    = 20f;
        [SerializeField] private float lifetime = 3f;

        [Tooltip("着弾点を中心に塗るブラシのピクセル半径（例: 128 → 直径256px ≈ テクスチャ512の半分）")]
        [SerializeField] [Range(1, 256)] private int brushPixelRadius = 128;

        [Tooltip("カメラ正面以外の面へのヒットを無視するか（側面塗り防止）")]
        [SerializeField] private bool ignoreSideHits = true;

        [Tooltip("正面とみなす角度の閾値（度）。この角度以内の法線のみ塗る")]
        [SerializeField] [Range(10f, 90f)] private float frontFaceAngleThreshold = 70f;

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private Vector3 _direction;
        private Color   _color;
        private bool    _initialized = false;
        private bool    _hasHit      = false;

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        public void Initialize(Vector3 direction, Color inkColor, int pixelRadius = 128)
        {
            _direction       = direction.normalized;
            _color           = inkColor;
            brushPixelRadius = pixelRadius;
            _initialized     = true;
            Destroy(gameObject, lifetime);
        }

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_initialized) return;
            transform.Translate(_direction * speed * Time.deltaTime, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasHit) return;

            var paintTarget = other.GetComponent<PaintTarget>();
            if (paintTarget == null) return;

            _hasHit = true;

            // ── 側面ヒット判定 ──────────────────────────────────────
            // Raycast で着弾面の法線を取得し、カメラに向いている面だけ塗る
            Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);

            RaycastHit hitInfo;
            bool gotHit = Physics.Raycast(ray, out hitInfo, 2f) && hitInfo.collider == other;

            if (ignoreSideHits && gotHit)
            {
                // 着弾面の法線とカメラ前方のなす角が閾値を超えていたら側面 → スキップ
                Camera cam = Camera.main;
                Vector3 cameraForward = cam != null ? cam.transform.forward : Vector3.forward;
                float angle = Vector3.Angle(-hitInfo.normal, cameraForward);

                if (angle > frontFaceAngleThreshold)
                {
                    Debug.Log($"[InkBullet] 側面ヒット（angle={angle:F1}°）→ スキップ");
                    Destroy(gameObject);
                    return;
                }
            }

            // ── UV 取得 ──────────────────────────────────────────────
            Vector2 uv = GetHitUV(other, gotHit ? hitInfo : (RaycastHit?)null);
            paintTarget.Paint(uv, _color, brushPixelRadius);

            Debug.Log($"[InkBullet] 命中！UV={uv} color={_color} pixelR={brushPixelRadius}");
            Destroy(gameObject);
        }

        // ────────────────────────────────────────────────────────────
        // 着弾点の UV 取得
        // ────────────────────────────────────────────────────────────

        private Vector2 GetHitUV(Collider col, RaycastHit? cachedHit)
        {
            Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);

            // ① MeshCollider があれば textureCoord で正確に取得
            var meshCol = col as MeshCollider ?? col.GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                if (meshCol.Raycast(ray, out RaycastHit mHit, 2f))
                    return mHit.textureCoord;
            }

            // ② Physics.Raycast 結果をキャッシュから利用（もしあれば）
            if (cachedHit.HasValue)
                return BoundsPointToUV(col, cachedHit.Value.point);

            // ③ 最終フォールバック: 弾の現在位置から UV 近似
            return BoundsPointToUV(col, transform.position);
        }

        /// <summary>
        /// ワールド座標をオブジェクトのローカル空間 (±0.5) に変換し UV に近似する。
        /// X → U, Y → V（プレイヤー正面から見た面に対応）
        /// </summary>
        private static Vector2 BoundsPointToUV(Collider col, Vector3 worldPoint)
        {
            Vector3 local = col.transform.InverseTransformPoint(worldPoint);
            float u = Mathf.Clamp01(local.x + 0.5f);
            float v = Mathf.Clamp01(local.y + 0.5f);
            return new Vector2(u, v);
        }
    }
}

using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の飛翔・当たり判定・ペイント処理。
    ///
    /// ▼ UV 取得の優先順位
    ///   1. MeshCollider → hit.textureCoord で「正確なUV」を取得
    ///   2. BoxCollider/SphereCollider → Physics.Raycast 結果から BoundsPointToUV で「近似UV」を取得
    ///   3. すべて失敗 → 弾の現在ワールド座標から「近似UV」を取得
    ///
    /// ▼ MeshCollider がない場合は Warning を出す（UV精度が落ちる）
    ///
    /// ▼ 側面ヒット防止
    ///   カメラに向いている面のみペイントする。
    ///
    /// ▼ デバッグ情報
    ///   着弾時に弾生成〜着弾の飛行時間、UV(正確/近似)、命中オブジェクトをログ出力する。
    ///
    /// NOTE: PaintTarget に正確な UV を得るには MeshCollider が必要。
    ///       MeshCollider を Convex=false で設定し、Read/Write Enabled なメッシュを使うこと。
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

        /// <summary>弾が生成された時刻（飛行時間計測用）</summary>
        private float _spawnTime;

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        public void Initialize(Vector3 direction, Color inkColor, int pixelRadius = 128)
        {
            _direction       = direction.normalized;
            _color           = inkColor;
            brushPixelRadius = pixelRadius;
            _initialized     = true;
            _spawnTime       = Time.time;
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
            float flightTime = Time.time - _spawnTime;

            // ── 側面ヒット判定 ──────────────────────────────────────
            Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);
            RaycastHit hitInfo;
            // QueryTriggerInteraction.Collide を指定しないと Trigger は無視される
            bool gotHit = Physics.Raycast(ray, out hitInfo, 2f,
                              Physics.DefaultRaycastLayers,
                              QueryTriggerInteraction.Collide)
                          && hitInfo.collider == other;

            if (ignoreSideHits && gotHit)
            {
                Camera cam = Camera.main;
                Vector3 cameraForward = cam != null ? cam.transform.forward : Vector3.forward;
                float angle = Vector3.Angle(-hitInfo.normal, cameraForward);

                if (angle > frontFaceAngleThreshold)
                {
                    Debug.Log($"[InkBullet] 側面ヒット（angle={angle:F1}°）→ スキップ（飛行時間={flightTime*1000f:F0}ms）");
                    Destroy(gameObject);
                    return;
                }
            }

            // ── UV 取得 ──────────────────────────────────────────────
            bool exactUV = false;
            Vector2 uv = GetHitUV(other, gotHit ? hitInfo : (RaycastHit?)null, out exactUV);

            paintTarget.Paint(uv, _color, brushPixelRadius);

            // ── 着弾ログ ────────────────────────────────────────────
            Debug.Log(
                $"[InkBullet] 命中!\n" +
                $"  object      = {other.name}\n" +
                $"  UV          = {uv}  ({(exactUV ? "正確 (MeshCollider.textureCoord)" : "⚠ 近似 (BoundsPointToUV)")})\n" +
                $"  color       = {_color}\n" +
                $"  pixelRadius = {brushPixelRadius}\n" +
                $"  flightTime  = {flightTime * 1000f:F0} ms");

            Destroy(gameObject);
        }

        // ────────────────────────────────────────────────────────────
        // 着弾点の UV 取得
        // ────────────────────────────────────────────────────────────

        private Vector2 GetHitUV(Collider col, RaycastHit? cachedHit, out bool exactUV)
        {
            exactUV = false;
            Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);

            // ① MeshCollider があれば textureCoord で正確に取得
            var meshCol = col as MeshCollider ?? col.GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                if (meshCol.Raycast(ray, out RaycastHit mHit, 3f))
                {
                    exactUV = true;
                    return mHit.textureCoord;
                }
                // MeshCollider はあるが Raycast 失敗（例: 凸包モード）
                Debug.LogWarning(
                    $"[InkBullet] ⚠ MeshCollider.Raycast に失敗しました ({col.name})。\n" +
                    "BoundsPointToUV（近似UV）にフォールバックします。\n" +
                    "Mesh を Read/Write Enabled にし、Convex=false の MeshCollider を設定してください。");
            }
            else
            {
                // MeshCollider がない → 精度が落ちる旨を警告
                Debug.LogWarning(
                    $"[InkBullet] ⚠ {col.name} に MeshCollider がありません。\n" +
                    "UV は BoundsPointToUV（近似）を使います。\n" +
                    "正確な UV 塗りには非凸 MeshCollider（Convex=false, Read/Write Enabled Mesh）が必要です。");
            }

            // ② Physics.Raycast 結果をキャッシュから利用（もしあれば）
            if (cachedHit.HasValue)
            {
                Debug.Log("[InkBullet] 近似UV: BoundsPointToUV（Raycastキャッシュあり）");
                return BoundsPointToUV(col, cachedHit.Value.point);
            }

            // ③ 最終フォールバック: 弾の現在位置から UV 近似
            Debug.Log("[InkBullet] 近似UV: BoundsPointToUV（弾の現在位置から）");
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

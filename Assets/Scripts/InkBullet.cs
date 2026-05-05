using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の飛翔・当たり判定・ペイント処理。
    ///
    /// ▼ UV 取得の優先順位
    ///   1. MeshCollider がある → hit.textureCoord で正確に取得
    ///   2. BoxCollider / SphereCollider など → Physics.Raycast で着弾ワールド座標を取得し
    ///      オブジェクトのローカル空間に変換して UV を近似計算
    ///   3. すべて失敗 → 弾の現在ワールド座標から UV を近似（最終フォールバック）
    ///
    /// これにより「オブジェクトの中心」ではなく「実際の着弾点」にインクが塗られる。
    /// </summary>
    public class InkBullet : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [SerializeField] private float speed    = 20f; // 弾の速さ（Units/秒）
        [SerializeField] private float lifetime = 3f;  // 自動消滅までの秒数

        [Tooltip("着弾点を中心に塗るブラシのピクセル半径（例: 30 → 直径60px）")]
        [SerializeField] [Range(1, 128)] private int brushPixelRadius = 30;

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

        public void Initialize(Vector3 direction, Color inkColor, int pixelRadius = 8)
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

            // 実際の着弾点の UV を計算して塗る
            Vector2 uv = GetHitUV(other);
            paintTarget.Paint(uv, _color, brushPixelRadius);

            Debug.Log($"[InkBullet] 命中！UV={uv} color={_color} pixelR={brushPixelRadius}");
            Destroy(gameObject);
        }

        // ────────────────────────────────────────────────────────────
        // 着弾点の UV 取得（着弾点を正確に取る）
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Collider の種類に関わらず着弾 UV を取得する。
        /// MeshCollider → textureCoord、それ以外 → ローカル座標変換による近似。
        /// </summary>
        private Vector2 GetHitUV(Collider col)
        {
            // 弾の少し後ろから前方へ Raycast（接触直前の位置から撃つ）
            Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);
            float maxDist = 2f;

            // ① MeshCollider がある → textureCoord で正確に取得
            var meshCol = col as MeshCollider ?? col.GetComponent<MeshCollider>();
            if (meshCol != null)
            {
                if (meshCol.Raycast(ray, out RaycastHit meshHit, maxDist))
                {
                    Debug.Log("[InkBullet] MeshCollider UV取得成功");
                    return meshHit.textureCoord;
                }
            }

            // ② Physics.Raycast でワールド着弾点を取得してローカル座標 → UV に変換
            //    ※ BoxCollider / SphereCollider / CapsuleCollider など非メッシュに有効
            if (Physics.Raycast(ray, out RaycastHit worldHit, maxDist) && worldHit.collider == col)
            {
                Debug.Log($"[InkBullet] Physics.Raycast 着弾点={worldHit.point}");
                return WorldPointToUV(col, worldHit.point);
            }

            // ③ 最終フォールバック: 弾の現在ワールド位置から UV を近似
            Debug.LogWarning("[InkBullet] Raycast 失敗 → 弾位置から UV を近似");
            return WorldPointToUV(col, transform.position);
        }

        /// <summary>
        /// ワールド座標をオブジェクトのローカル空間に変換し UV に近似する。
        ///
        /// Unity プリミティブ（Cube, Sphere など）のメッシュ頂点は
        /// ローカル空間で ±0.5 の範囲に収まるため：
        ///   localX ∈ [-0.5, +0.5] → U = localX + 0.5 ∈ [0, 1]
        ///   localY ∈ [-0.5, +0.5] → V = localY + 0.5 ∈ [0, 1]
        /// でマッピングする。
        /// </summary>
        private static Vector2 WorldPointToUV(Collider col, Vector3 worldPoint)
        {
            // InverseTransformPoint は位置・回転・スケールすべて考慮してローカル座標へ変換する
            Vector3 local = col.transform.InverseTransformPoint(worldPoint);

            // X → U, Y → V（正面から当たる想定。横移動オブジェクトに適合）
            float u = Mathf.Clamp01(local.x + 0.5f);
            float v = Mathf.Clamp01(local.y + 0.5f);

            return new Vector2(u, v);
        }
    }
}

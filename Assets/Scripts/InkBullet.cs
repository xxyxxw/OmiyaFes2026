using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の飛翔・当たり判定・ペイント処理。
    /// InkGun から生成後に Initialize() を呼ぶ。
    /// OnTriggerEnter で PaintTarget に命中したら UV 座標を取得してインクを塗る。
    ///
    /// ▼ 設計方針
    ///   - MeshCollider に対して Raycast して hit.textureCoord を取得
    ///   - PaintTarget.Paint(uv, color, pixelRadius) を呼ぶ
    ///   - brushPixelRadius で塗るサイズを直接ピクセル単位で指定（例: 8 = 直径16px）
    /// </summary>
    public class InkBullet : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [SerializeField] private float speed            = 20f; // 弾の速さ（Units/秒）
        [SerializeField] private float lifetime         = 3f;  // 自動消滅までの秒数

        [Tooltip("着弾点を中心に塗るブラシのピクセル半径（例: 8 → 直径16px）")]
        [SerializeField] [Range(1, 64)] private int brushPixelRadius = 8;

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

        /// <summary>
        /// InkGun から Instantiate 直後に呼ぶ初期化メソッド。
        /// </summary>
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

        /// <summary>
        /// Trigger Collider が別の Collider に触れたとき呼ばれる。
        /// PaintTarget を持つオブジェクトなら UV を取得してインクを塗る。
        /// ※ InkBullet 自身は SphereCollider (isTrigger=true) + Rigidbody(kinematic) を持つ。
        /// ※ 対象オブジェクトは MeshCollider(non-convex, non-trigger) を持つ。
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (_hasHit) return;

            var paintTarget = other.GetComponent<PaintTarget>();
            if (paintTarget == null) return;

            _hasHit = true;

            // MeshCollider に Raycast して UV 座標を取得
            var meshCol = other as MeshCollider;
            if (meshCol == null)
                meshCol = other.GetComponent<MeshCollider>();

            if (meshCol != null)
            {
                Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);

                if (meshCol.Raycast(ray, out RaycastHit hit, 2f))
                {
                    // 着弾 UV 座標をそのまま渡す（ピクセル半径は brushPixelRadius）
                    paintTarget.Paint(hit.textureCoord, _color, brushPixelRadius);
                    Debug.Log($"[InkBullet] 命中！UV={hit.textureCoord} color={_color} pixelR={brushPixelRadius}");
                }
                else
                {
                    // Raycast 失敗時は中心にフォールバック
                    paintTarget.Paint(new Vector2(0.5f, 0.5f), _color, brushPixelRadius);
                    Debug.Log("[InkBullet] UV取得失敗 → 中心に塗る（フォールバック）");
                }
            }

            Destroy(gameObject);
        }
    }
}

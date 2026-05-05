using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の飛翔・当たり判定・ペイント処理。
    /// InkGun から生成後に Initialize() を呼ぶ。
    /// OnTriggerEnter で PaintTarget に命中したら UV 座標を取得してインクを塗る。
    /// </summary>
    public class InkBullet : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [SerializeField] private float speed            = 20f;  // 弾の飛ぶ速さ（Units/秒）
        [SerializeField] private float lifetime         = 3f;   // 生成からこの秒数が経つと自動で消える
        [SerializeField] private float brushWorldRadius = 0.5f; // 着弾時のブラシ半径（ワールド空間・メートル単位）

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private Vector3 _direction;          // 飛翔方向（正規化済み）
        private Color   _color;              // この弾のインク色
        private bool    _initialized = false;
        private bool    _hasHit      = false; // 多重ヒット防止フラグ

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// InkGun から Instantiate 直後に呼ぶ初期化メソッド。
        /// </summary>
        public void Initialize(Vector3 direction, Color inkColor, float worldRadius = 0.5f)
        {
            _direction       = direction.normalized;
            _color           = inkColor;
            brushWorldRadius = worldRadius; // InkGun の Inspector 値で上書き
            _initialized     = true;
            Destroy(gameObject, lifetime);
        }

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_initialized) return;

            // ワールド空間で direction 方向へ毎フレーム移動
            transform.Translate(_direction * speed * Time.deltaTime, Space.World);
        }

        /// <summary>
        /// Trigger Collider が別の Collider に触れたとき呼ばれる。
        /// PaintTarget を持つオブジェクトなら UV を取得してインクを塗る。
        /// ※ InkBullet 自身は SphereCollider (isTrigger=true) + Rigidbody(kinematic) を持つ。
        /// ※ 対象オブジェクトは MeshCollider(non-convex, non-trigger) + Rigidbody(kinematic)。
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            // 多重ヒット防止
            if (_hasHit) return;

            // PaintTarget を持つオブジェクトかチェック
            var paintTarget = other.GetComponent<PaintTarget>();
            if (paintTarget == null) return;

            _hasHit = true;

            // MeshCollider に対して Ray を飛ばして UV 座標を取得
            var meshCol = other as MeshCollider;
            if (meshCol == null)
                meshCol = other.GetComponent<MeshCollider>();

            if (meshCol != null)
            {
                // 弾の少し後ろから前方に向けて Ray を飛ばす
                Ray ray = new Ray(transform.position - _direction * 0.5f, _direction);

                if (meshCol.Raycast(ray, out RaycastHit hit, 2f))
                {
                    // ワールド空間の半径を渡す（PaintTarget 内で UV 半径に変換される）
                    paintTarget.Paint(hit.textureCoord, _color, brushWorldRadius);
                    Debug.Log($"[InkBullet] 命中！UV={hit.textureCoord} color={_color} worldR={brushWorldRadius}");
                }
                else
                {
                    // Ray が当たらなかった場合は中心 UV にフォールバック
                    paintTarget.Paint(new Vector2(0.5f, 0.5f), _color, brushWorldRadius);
                    Debug.Log("[InkBullet] UV取得失敗 → 中心に塗る（フォールバック）");
                }
            }

            // 弾を即削除
            Destroy(gameObject);
        }
    }
}

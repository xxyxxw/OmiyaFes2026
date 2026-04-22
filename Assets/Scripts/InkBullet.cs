using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の直進移動・寿命管理。
    /// InkGun.cs から Instantiate 後に Initialize() を呼ぶ。
    /// </summary>
    public class InkBullet : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [SerializeField] private float speed    = 20f;  // 弾の飛ぶ速さ（Units/秒）
        [SerializeField] private float lifetime = 2f;   // 生成からこの秒数が経つと自動で消える

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private Vector3 _direction;          // 飛翔方向（正規化済み）
        private bool _initialized = false;   // Initialize() が呼ばれたかのフラグ

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// InkGun から Instantiate 直後に呼ぶ初期化メソッド。
        /// direction: 発射方向ベクトル（自動的に正規化される）
        /// </summary>
        public void Initialize(Vector3 direction)
        {
            _direction   = direction.normalized; // 方向ベクトルを長さ1に正規化
            _initialized = true;
            Destroy(gameObject, lifetime);       // lifetime 秒後に自動で GameObject を削除
        }

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            // Initialize() が呼ばれるまでは動かない（生成直後の1フレームズレを防ぐ）
            if (!_initialized) return;

            // ワールド空間で direction 方向へ毎フレーム移動
            // Time.deltaTime を掛けることでフレームレートに依存しない速度になる
            transform.Translate(_direction * speed * Time.deltaTime, Space.World);
        }
    }
}

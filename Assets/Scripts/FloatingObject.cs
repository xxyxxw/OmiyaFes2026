using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 右から左へ水平移動しながら自転するオブジェクト。
    /// ObjectSpawner が生成後に Initialize() を呼ぶ。
    /// </summary>
    public class FloatingObject : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        private float   _moveSpeed  = 3f;    // 移動速度（Units/秒）
        private float   _destroyX   = -12f;  // この X 座標を下回ったら自動削除

        private Vector3 _rotationAxis  = Vector3.up;   // 自転軸
        private float   _rotationSpeed = 60f;           // 自転速度（度/秒）

        // ────────────────────────────────────────────────────────────
        // 初期化
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// ObjectSpawner から Instantiate 直後に呼ぶ。
        /// rotationAxis / rotationSpeed を省略するとランダムに設定される。
        /// </summary>
        public void Initialize(
            float   speed,
            float   destroyX       = -12f,
            Vector3? rotationAxis  = null,
            float   rotationSpeed  = -1f)
        {
            _moveSpeed = speed;
            _destroyX  = destroyX;

            // 自転軸: 引数があればそれを使い、なければランダム
            _rotationAxis = rotationAxis.HasValue
                ? rotationAxis.Value.normalized
                : Random.onUnitSphere;

            // 自転速度: 引数 < 0 ならランダム（30〜90度/秒）
            _rotationSpeed = rotationSpeed < 0f
                ? Random.Range(30f, 90f)
                : rotationSpeed;
        }

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            float dt = Time.deltaTime;

            // 左へ移動
            transform.Translate(Vector3.left * _moveSpeed * dt, Space.World);

            // 自転
            transform.Rotate(_rotationAxis, _rotationSpeed * dt, Space.Self);

            // 左端で削除
            if (transform.position.x < _destroyX)
                Destroy(gameObject);
        }
    }
}

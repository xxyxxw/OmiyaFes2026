using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 右から左へ水平移動し、左端に達したら自動削除する。
    /// ObjectSpawner が生成後に Initialize() を呼ぶ。
    /// </summary>
    public class FloatingObject : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        private float _moveSpeed  = 3f;   // 移動速度（Units/秒）
        private float _destroyX   = -12f; // この X 座標を下回ったら自動削除

        // ────────────────────────────────────────────────────────────
        // 初期化
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// ObjectSpawner から Instantiate 直後に呼ぶ。
        /// </summary>
        public void Initialize(float speed, float destroyX = -12f)
        {
            _moveSpeed = speed;
            _destroyX  = destroyX;
        }

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            // 毎フレーム左方向へ移動（ワールド空間）
            transform.Translate(Vector3.left * _moveSpeed * Time.deltaTime, Space.World);

            // 左端を超えたら削除
            if (transform.position.x < _destroyX)
            {
                Destroy(gameObject);
            }
        }
    }
}

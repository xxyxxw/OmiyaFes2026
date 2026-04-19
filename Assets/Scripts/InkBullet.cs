using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク弾の直進移動・寿命管理。
    /// InkGun.cs から Instantiate 後に Initialize() を呼ぶ。
    /// </summary>
    public class InkBullet : MonoBehaviour
    {
        [SerializeField] private float speed    = 20f;
        [SerializeField] private float lifetime = 2f;

        private Vector3 _direction;
        private bool _initialized = false;

        public void Initialize(Vector3 direction)
        {
            _direction   = direction.normalized;
            _initialized = true;
            Destroy(gameObject, lifetime);
        }

        private void Update()
        {
            if (!_initialized) return;
            transform.Translate(_direction * speed * Time.deltaTime, Space.World);
        }
    }
}

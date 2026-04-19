using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク色を時間経過で自動サイクルさせる。
    /// InkGun.cs からこのコンポーネントで現在色を参照する。
    /// </summary>
    public class InkColorCycler : MonoBehaviour
    {
        [SerializeField] private Color[] colors = new Color[]
        {
            new Color(1f,   0.2f, 0.2f),  // 赤
            new Color(1f,   0.4f, 0.7f),  // ピンク
            new Color(1f,   0.6f, 0.1f),  // オレンジ
            new Color(1f,   0.9f, 0.1f),  // 黄色
            new Color(0.2f, 0.8f, 1f),    // 水色
            new Color(0.2f, 0.9f, 0.3f),  // 緑
            new Color(0.7f, 0.2f, 1f),    // 紫
        };

        [SerializeField] private float intervalSeconds = 2.5f;

        public Color CurrentColor { get; private set; }

        private int _index = 0;
        private float _timer = 0f;

        private void Awake()
        {
            CurrentColor = colors.Length > 0 ? colors[0] : Color.white;
        }

        private void Update()
        {
            if (colors.Length == 0) return;

            _timer += Time.deltaTime;
            if (_timer >= intervalSeconds)
            {
                _timer = 0f;
                _index = (_index + 1) % colors.Length;
                CurrentColor = colors[_index];
            }
        }
    }
}

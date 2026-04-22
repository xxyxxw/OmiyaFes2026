using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク色を時間経過で自動サイクルさせる。
    /// InkGun.cs からこのコンポーネントで現在色を参照する。
    /// </summary>
    public class InkColorCycler : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        // サイクルで順番に切り替わる色のリスト（Inspectorから自由に追加・変更可能）
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

        // 何秒ごとに色を次に切り替えるか
        [SerializeField] private float intervalSeconds = 2.5f;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>現在選択中のインク色（InkGun・GameUI から参照される）</summary>
        public Color CurrentColor { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private int   _index = 0;  // 現在何番目の色を表示しているか
        private float _timer = 0f; // 前回の切り替えからの経過時間

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // 最初の色を設定する（colors が空なら白にフォールバック）
            CurrentColor = colors.Length > 0 ? colors[0] : Color.white;
        }

        private void Update()
        {
            if (colors.Length == 0) return; // 色が未設定なら何もしない

            _timer += Time.deltaTime; // 経過時間を積算

            // intervalSeconds 秒経過したら次の色へ切り替え
            if (_timer >= intervalSeconds)
            {
                _timer = 0f;

                // % 演算子で配列の末尾まで行ったら先頭に戻る（ループ）
                _index = (_index + 1) % colors.Length;

                CurrentColor = colors[_index];
            }
        }
    }
}

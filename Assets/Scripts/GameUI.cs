using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OmiyaFes2026
{
    /// <summary>
    /// HUD 全体を管理する。
    /// - 残り時間（右上）
    /// - 現在色インジケーター（左下）
    /// - 待機中メッセージ
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("残り時間")]
        // 残り時間を表示する TextMeshPro テキスト（右上に配置するUI）
        [SerializeField] private TextMeshProUGUI timerText;

        [Header("色インジケーター")]
        // 現在のインク色を示す UI Image（左下のアイコン等）
        [SerializeField] private Image colorIndicator;

        [Header("待機中パネル")]
        // 「ZIG SIM を送信してください」等を表示するパネル GameObject
        [SerializeField] private GameObject waitingPanel;
        // 待機中パネルに表示するメッセージテキスト
        [SerializeField] private TextMeshProUGUI waitingMessageText;

        // ────────────────────────────────────────────────────────────
        // 内部参照
        // ────────────────────────────────────────────────────────────

        private InkColorCycler _colorCycler; // 現在の色を取得するために参照

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // シーン内の InkColorCycler コンポーネントを自動検索して取得
            _colorCycler = FindObjectOfType<InkColorCycler>();
        }

        private void Start()
        {
            // 待機中パネルのメッセージを設定
            if (waitingMessageText != null)
                waitingMessageText.text = "スマホを傾けてインクを塗ろう！\nZIG SIM でジャイロを送信してください";

            // GameStateManager のイベントを購読してゲーム状態の変化に合わせてパネルを切替える
            // += でラムダ式を登録することで、状態変化時に自動呼び出しされる
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.OnGameStart += () => SetWaitingPanelVisible(false); // ゲーム開始→待機パネル非表示
                GameStateManager.Instance.OnReset     += () => SetWaitingPanelVisible(true);  // リセット→待機パネル表示
                GameStateManager.Instance.OnGameEnd   += () => SetWaitingPanelVisible(false); // ゲーム終了→待機パネル非表示
            }

            // 起動直後は待機状態なのでパネルを表示
            SetWaitingPanelVisible(true);
        }

        private void Update()
        {
            // 毎フレームタイマーと色インジケーターを更新
            UpdateTimer();
            UpdateColorIndicator();
        }

        // ────────────────────────────────────────────────────────────
        // 内部更新メソッド
        // ────────────────────────────────────────────────────────────

        private void UpdateTimer()
        {
            if (timerText == null) return;
            if (GameStateManager.Instance == null) return;

            // GameStateManager から残り時間を取得
            float t = GameStateManager.Instance.RemainingTime;

            // 秒数を「分:秒」の表示形式に変換（例: 90秒 → "01:30"）
            int minutes = Mathf.FloorToInt(t / 60f);
            int seconds = Mathf.FloorToInt(t % 60f);
            timerText.text = $"{minutes:00}:{seconds:00}";

            // 残り10秒以下になったらテキストを赤に変えて緊迫感を演出
            timerText.color = t <= 10f ? Color.red : Color.white;
        }

        private void UpdateColorIndicator()
        {
            // 参照がなければ何もしない（null安全チェック）
            if (colorIndicator == null) return;
            if (_colorCycler == null) return;

            // InkColorCycler の現在色を UI のカラーとして反映
            colorIndicator.color = _colorCycler.CurrentColor;
        }

        // ────────────────────────────────────────────────────────────
        // ヘルパー
        // ────────────────────────────────────────────────────────────

        /// <summary>待機中パネルの表示・非表示を切り替える</summary>
        private void SetWaitingPanelVisible(bool visible)
        {
            if (waitingPanel != null)
                waitingPanel.SetActive(visible);
        }
    }
}

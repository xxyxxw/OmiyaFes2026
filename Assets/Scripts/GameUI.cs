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
        [Header("残り時間")]
        [SerializeField] private TextMeshProUGUI timerText;

        [Header("色インジケーター")]
        [SerializeField] private Image colorIndicator;

        [Header("待機中パネル")]
        [SerializeField] private GameObject waitingPanel;
        [SerializeField] private TextMeshProUGUI waitingMessageText;

        private InkColorCycler _colorCycler;

        private void Awake()
        {
            _colorCycler = FindObjectOfType<InkColorCycler>();
        }

        private void Start()
        {
            if (waitingMessageText != null)
                waitingMessageText.text = "スマホを傾けてインクを塗ろう！\nZIG SIM でジャイロを送信してください";

            // GameStateManager のイベント購読
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.OnGameStart += () => SetWaitingPanelVisible(false);
                GameStateManager.Instance.OnReset     += () => SetWaitingPanelVisible(true);
                GameStateManager.Instance.OnGameEnd   += () => SetWaitingPanelVisible(false);
            }

            SetWaitingPanelVisible(true);
        }

        private void Update()
        {
            UpdateTimer();
            UpdateColorIndicator();
        }

        private void UpdateTimer()
        {
            if (timerText == null) return;
            if (GameStateManager.Instance == null) return;

            float t = GameStateManager.Instance.RemainingTime;
            int minutes = Mathf.FloorToInt(t / 60f);
            int seconds = Mathf.FloorToInt(t % 60f);
            timerText.text = $"{minutes:00}:{seconds:00}";

            // 残り10秒以下で赤く
            timerText.color = t <= 10f ? Color.red : Color.white;
        }

        private void UpdateColorIndicator()
        {
            if (colorIndicator == null) return;
            if (_colorCycler == null) return;
            colorIndicator.color = _colorCycler.CurrentColor;
        }

        private void SetWaitingPanelVisible(bool visible)
        {
            if (waitingPanel != null)
                waitingPanel.SetActive(visible);
        }
    }
}

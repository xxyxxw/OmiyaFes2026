using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// ゲーム全体の状態を管理する。
    /// Waiting → Playing → Ending → Waiting のサイクル。
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        public enum GameState { Waiting, Playing, Ending }

        [Header("設定")]
        [SerializeField] private float gameDuration = 45f;        // ゲームプレイ時間（秒）
        [SerializeField] private float inactivityTimeout = 10f;   // 非操作タイムアウト（秒）

        public static GameStateManager Instance { get; private set; }

        public GameState CurrentState { get; private set; } = GameState.Waiting;

        /// <summary>残り時間（秒）</summary>
        public float RemainingTime { get; private set; }

        private float _inactivityTimer = 0f;

        // イベント
        public event System.Action OnGameStart;
        public event System.Action OnGameEnd;
        public event System.Action OnReset;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case GameState.Waiting:
                    HandleWaiting();
                    break;
                case GameState.Playing:
                    HandlePlaying();
                    break;
                case GameState.Ending:
                    // 現在は結果表示なし仕様なので即 Waiting へ
                    ResetGame();
                    break;
            }

            // スタッフ用 強制リセット（Rキー）
            if (Input.GetKeyDown(KeyCode.R))
                ResetGame();

            // フルスクリーン切替（F11）
            if (Input.GetKeyDown(KeyCode.F11))
                Screen.fullScreen = !Screen.fullScreen;
        }

        // ─────────────────────────────────────────
        // 状態ハンドラ
        // ─────────────────────────────────────────

        private void HandleWaiting()
        {
            // ZIG SIM からパケット受信を検知したらゲーム開始
            // UdpQuaternionReceiver を参照して判断する
            var receiver = FindObjectOfType<Pose.UdpQuaternionReceiver>();
            if (receiver != null && receiver.IsReceiving)
            {
                StartGame();
            }
        }

        private void HandlePlaying()
        {
            RemainingTime -= Time.deltaTime;

            // 非操作タイマー
            var receiver = FindObjectOfType<Pose.UdpQuaternionReceiver>();
            bool isActive = receiver != null && receiver.IsReceiving;

            if (isActive)
            {
                _inactivityTimer = 0f;
            }
            else
            {
                _inactivityTimer += Time.deltaTime;
                if (_inactivityTimer >= inactivityTimeout)
                {
                    Debug.Log("[GameStateManager] 非操作タイムアウト → リセット");
                    ResetGame();
                    return;
                }
            }

            // 時間切れ
            if (RemainingTime <= 0f)
            {
                RemainingTime = 0f;
                EndGame();
            }
        }

        // ─────────────────────────────────────────
        // 公開メソッド
        // ─────────────────────────────────────────

        public void StartGame()
        {
            CurrentState = GameState.Playing;
            RemainingTime = gameDuration;
            _inactivityTimer = 0f;
            OnGameStart?.Invoke();
            Debug.Log("[GameStateManager] ゲーム開始");
        }

        public void EndGame()
        {
            CurrentState = GameState.Ending;
            OnGameEnd?.Invoke();
            Debug.Log("[GameStateManager] ゲーム終了");
        }

        public void ResetGame()
        {
            CurrentState = GameState.Waiting;
            RemainingTime = gameDuration;
            _inactivityTimer = 0f;
            OnReset?.Invoke();
            Debug.Log("[GameStateManager] リセット → 待機状態");
        }
    }
}

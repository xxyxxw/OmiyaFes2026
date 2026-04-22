using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// ゲーム全体の状態を管理する。
    /// Waiting → Playing → Ending → Waiting のサイクル。
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        // ゲームの「状態」を 3 種類で定義する列挙型
        // Waiting = 待機中（スマホ未接続・結果表示後など）
        // Playing = ゲームプレイ中（タイマー稼働中）
        // Ending  = ゲーム終了処理中（現仕様では即 Waiting に戻る）
        public enum GameState { Waiting, Playing, Ending }

        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("設定")]
        [SerializeField] private float gameDuration     = 45f;  // ゲームプレイ時間（秒）
        [SerializeField] private float inactivityTimeout = 10f; // 操作がない場合のタイムアウト（秒）

        // ────────────────────────────────────────────────────────────
        // シングルトン
        // ────────────────────────────────────────────────────────────

        // シングルトンパターン：シーン内に1つだけ存在させる
        // 他スクリプトから GameStateManager.Instance でアクセス可能
        public static GameStateManager Instance { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>現在のゲーム状態（外部からの読み取り専用）</summary>
        public GameState CurrentState { get; private set; } = GameState.Waiting;

        /// <summary>残り時間（秒）。GameUI がこれを読んでタイマー表示する</summary>
        public float RemainingTime { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private float _inactivityTimer = 0f; // 最後に操作があってからの経過時間

        // ────────────────────────────────────────────────────────────
        // イベント（購読すると状態変化を受け取れる）
        // GameUI など他のクラスはこれを += で購読して UI を更新する
        // ────────────────────────────────────────────────────────────

        public event System.Action OnGameStart; // ゲーム開始時に発火
        public event System.Action OnGameEnd;   // ゲーム終了時に発火
        public event System.Action OnReset;     // リセット時に発火

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // シングルトンの初期化：すでに Instance があれば自分を削除
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            // 現在の状態に応じた処理を実行
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

        // ────────────────────────────────────────────────────────────
        // 状態ハンドラ（Update から毎フレーム呼ばれる）
        // ────────────────────────────────────────────────────────────

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
            // 残り時間を毎フレーム減らす
            RemainingTime -= Time.deltaTime;

            // ── 非操作タイムアウト判定 ──────────────────────────
            var receiver = FindObjectOfType<Pose.UdpQuaternionReceiver>();
            bool isActive = receiver != null && receiver.IsReceiving;

            if (isActive)
            {
                // スマホから受信中はタイマーをリセット（操作あり）
                _inactivityTimer = 0f;
            }
            else
            {
                // 受信が止まったら非操作タイマーを積算
                _inactivityTimer += Time.deltaTime;
                if (_inactivityTimer >= inactivityTimeout)
                {
                    // タイムアウトしたら強制リセット
                    Debug.Log("[GameStateManager] 非操作タイムアウト → リセット");
                    ResetGame();
                    return;
                }
            }

            // ── 時間切れ判定 ──────────────────────────
            if (RemainingTime <= 0f)
            {
                RemainingTime = 0f;
                EndGame();
            }
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド（外部から状態遷移をトリガーできる）
        // ────────────────────────────────────────────────────────────

        /// <summary>ゲーム開始。Playing 状態に移行してタイマーをスタートする</summary>
        public void StartGame()
        {
            CurrentState   = GameState.Playing;
            RemainingTime  = gameDuration;  // タイマーを設定秒数にリセット
            _inactivityTimer = 0f;
            OnGameStart?.Invoke();          // 登録されたリスナーに通知（?. でnull安全）
            Debug.Log("[GameStateManager] ゲーム開始");
        }

        /// <summary>ゲーム終了。Ending 状態に移行してリスナーに通知する</summary>
        public void EndGame()
        {
            CurrentState = GameState.Ending;
            OnGameEnd?.Invoke();
            Debug.Log("[GameStateManager] ゲーム終了");
        }

        /// <summary>初期化。Waiting 状態に戻してタイマーとタイムアウトをリセットする</summary>
        public void ResetGame()
        {
            CurrentState     = GameState.Waiting;
            RemainingTime    = gameDuration;
            _inactivityTimer = 0f;
            OnReset?.Invoke();
            Debug.Log("[GameStateManager] リセット → 待機状態");
        }
    }
}

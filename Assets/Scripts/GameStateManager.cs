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
        [SerializeField] private float gameDuration      = 45f;  // ゲームプレイ時間（秒）
        [SerializeField] private float inactivityTimeout = 10f;  // 操作がない場合のタイムアウト（秒）

        [Header("デバッグ（テスト用）")]
        [Tooltip("チェックを入れるとスペースキーでゲーム開始できる（ZIG SIM不要）")]
        [SerializeField] private bool enableSpaceKeyStart = true;

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
        private Pose.UdpQuaternionReceiver _receiver; // キャッシュする（毎フレームFindしない）
        private int _lastPacketCount = 0;             // 前フレームの受信カウント（非操作判定用）

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

            // receiver をシーンから一度だけ検索してキャッシュ（毎フレームの重いFindを避ける）
            _receiver = FindObjectOfType<Pose.UdpQuaternionReceiver>();
            if (_receiver == null)
                Debug.LogWarning("[GameStateManager] UdpQuaternionReceiverがシーンに見つかりません。" +
                                 "Hierarchyの GameObject に UdpQuaternionReceiver をアタッチしてください。");
            else
                Debug.Log($"[GameStateManager] receiver 検出: {_receiver.gameObject.name}");
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
            // ── テスト用：スペースキーでゲーム開始 ────────────────
            if (enableSpaceKeyStart && Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("[GameStateManager] スペースキーでゲーム開始（デバッグ）");
                StartGame();
                return;
            }

            // ── receiver が見つからない場合は再検索する ────────────
            if (_receiver == null)
            {
                _receiver = FindObjectOfType<Pose.UdpQuaternionReceiver>();
                if (_receiver == null)
                {
                    if (Time.frameCount % 300 == 0)
                        Debug.LogWarning("[GameStateManager] UdpQuaternionReceiver が見つかりません");
                    return;
                }
                Debug.Log($"[GameStateManager] receiver 再検出: {_receiver.gameObject.name}");
            }

            // ── ZIG SIM からパケット受信を検知したらゲーム開始 ───
            if (_receiver.IsReceiving)
            {
                Debug.Log($"[GameStateManager] ZIG SIM 受信検知 → ゲーム開始");
                StartGame();
            }
        }

        private void HandlePlaying()
        {
            // 残り時間を毎フレーム減らす
            RemainingTime -= Time.deltaTime;

            // ── 非操作タイムアウト判定（ReceivedPacketCountの増分で判断） ──
            // IsReceivingは0.5秒スケールなので、パケット数の増分が0の場合にのみタイマー秒積す
            if (_receiver != null)
            {
                int currentCount = _receiver.ReceivedPacketCount;
                bool isActive = currentCount != _lastPacketCount;
                _lastPacketCount = currentCount;

                if (isActive)
                    _inactivityTimer = 0f;
                else
                    _inactivityTimer += Time.deltaTime;
            }
            else
            {
                _inactivityTimer += Time.deltaTime;
            }

            if (_inactivityTimer >= inactivityTimeout)
            {
                Debug.Log("[GameStateManager] 非操作タイムアウト → リセット");
                ResetGame();
                return;
            }

            // ── 時間切れ判定 ──────────────────
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

            // ── シーン内の全 PaintTarget のインクをリセット ──────────
            // 壁・浮遊オブジェクト・どんなオブジェクトでも一括クリア
            PaintTarget[] allTargets = FindObjectsOfType<PaintTarget>();
            foreach (var pt in allTargets)
                pt.ClearPaint();

            if (allTargets.Length > 0)
                Debug.Log($"[GameStateManager] インクリセット: {allTargets.Length} 個の PaintTarget をクリアしました");

            Debug.Log("[GameStateManager] リセット → 待機状態");
        }
    }
}

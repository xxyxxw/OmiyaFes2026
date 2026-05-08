using UnityEngine;
using OmiyaFes2026.Pose;

namespace OmiyaFes2026
{
    /// <summary>
    /// ZIG SIM → AimRoot → InkGun の連携診断スクリプト。
    /// 2 秒ごとに全コンポーネントの状態をコンソールに出力する。
    ///
    /// ▼ 使い方
    ///   1. シーン内の任意の GameObject（例: GameManager）にアタッチ
    ///   2. Play Mode で Console を確認
    ///   3. 問題が特定できたら外す（or 削除）
    /// </summary>
    public class ZigSimDiagnostic : MonoBehaviour
    {
        [Header("診断設定")]
        [Tooltip("状態を出力する間隔（秒）")]
        [SerializeField] private float reportInterval = 2f;

        private float _timer = 0f;

        // ── 対象コンポーネント（Start で自動検索）──────────────────────
        private UdpQuaternionReceiver _receiver;
        private PoseRotationDriver    _poseDriver;
        private InkGun                _inkGun;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Start()
        {
            _receiver   = FindObjectOfType<UdpQuaternionReceiver>();
            _poseDriver = FindObjectOfType<PoseRotationDriver>();
            _inkGun     = FindObjectOfType<InkGun>();

            // 起動時に1回フル診断
            RunDiagnostic(fullMode: true);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= reportInterval)
            {
                _timer = 0f;
                RunDiagnostic(fullMode: false);
            }
        }

        // ────────────────────────────────────────────────────────────
        // 診断本体
        // ────────────────────────────────────────────────────────────

        private void RunDiagnostic(bool fullMode)
        {
            // ── ① GameStateManager ──────────────────────────────────
            string gameState = GameStateManager.Instance != null
                ? GameStateManager.Instance.CurrentState.ToString()
                : "❌ Instance が null！";

            // ── ② UdpQuaternionReceiver（ZIGSIM受信） ───────────────
            string receiverInfo;
            if (_receiver == null)
            {
                receiverInfo = "❌ UdpQuaternionReceiver が見つからない！\n" +
                               "   → ZigSimReceiver オブジェクトに UdpQuaternionReceiver がアタッチされているか確認";
            }
            else
            {
                receiverInfo =
                    $"IsReceiving     = {(_receiver.IsReceiving ? "✅ 受信中" : "❌ 未受信（ZIG SIMがパケット送れていない）")}\n" +
                    $"   ReceivedCount = {_receiver.ReceivedPacketCount}\n" +
                    $"   LatestQuat    = {_receiver.LatestQuaternion}\n" +
                    $"   LastSender    = {_receiver.LastSender}";
            }

            // ── ③ PoseRotationDriver（ZIGSIM→AimRoot の変換器） ─────
            string poseInfo;
            if (_poseDriver == null)
            {
                poseInfo = "❌ PoseRotationDriver が見つからない！\n" +
                           "   → ZigSimReceiver にアタッチされているか確認";
            }
            else
            {
                var aimTarget = _poseDriver.AimTarget;
                string aimName = aimTarget != null ? aimTarget.name : "null（⚠ 未設定）";
                string aimRot  = aimTarget != null
                    ? $"{aimTarget.rotation.eulerAngles:F1}"
                    : "N/A";

                // DebugRelativeEuler = PoseRotationDriver が最後に計算した「スマホの相対回転」
                // これが 0,0,0 のまま → ZIGSIM データが基準姿勢から動いていない
                Vector3 relEuler = _poseDriver.DebugRelativeEuler;
                string rotStatus = (Mathf.Abs(relEuler.x) < 0.5f &&
                                    Mathf.Abs(relEuler.y) < 0.5f &&
                                    Mathf.Abs(relEuler.z) < 0.5f)
                    ? "⚠ ≈ 0,0,0（スマホが動いていない or キャリブ直後）"
                    : "✅ 動いている";

                poseInfo =
                    $"AimTarget       = {aimName}\n" +
                    $"   AimTarget.rotation = {aimRot}\n" +
                    $"   RelativeEuler      = pitch={relEuler.x:F1}° yaw={relEuler.y:F1}° roll={relEuler.z:F1}°  {rotStatus}\n" +
                    $"   CurrentYaw/Pitch   = yaw={_poseDriver.CurrentYawDeg:F1}° pitch={_poseDriver.CurrentPitchDeg:F1}°";
            }


            // ── ④ AimRoot GameObject（シーン上の存在確認） ──────────
            var aimRootGo = GameObject.Find("AimRoot");
            string aimRootInfo = aimRootGo != null
                ? $"✅ AimRoot が存在 | rotation = {aimRootGo.transform.rotation.eulerAngles}"
                : "❌ AimRoot がシーン上にない！ AimRootSetup.Awake() が動いていない可能性";

            // ── ⑤ InkGun ────────────────────────────────────────────
            string inkGunInfo;
            if (_inkGun == null)
            {
                inkGunInfo = "❌ InkGun が見つからない！";
            }
            else
            {
                // aimTransform はプライベートなので transform で代用
                // AimRootSetup が SetAimTransform を呼んでいれば AimRoot.forward が反映されている
                inkGunInfo = $"✅ InkGun 存在 | transform.forward = {_inkGun.transform.forward}";
            }

            // ── ⑥ BackgroundSetup / BackgroundPaintTarget ───────────
            var bgPaint = FindObjectOfType<BackgroundPaintTarget>();
            string bgInfo = bgPaint != null
                ? $"✅ BackgroundPaintTarget 存在 | {bgPaint.gameObject.name}"
                : "❌ BackgroundPaintTarget が見つからない → インク当たり判定なし";

            // ── まとめてログ ─────────────────────────────────────────
            string separator = fullMode
                ? "══════════════════════════════════════════"
                : "──────────────────────────────────────────";

            Debug.Log(
                $"\n{separator}\n" +
                $"[ZigSimDiagnostic] 診断レポート\n" +
                $"{separator}\n" +
                $"① GameState      : {gameState}\n" +
                $"② UDP受信        : {receiverInfo}\n" +
                $"③ PoseDriver     : {poseInfo}\n" +
                $"④ AimRoot        : {aimRootInfo}\n" +
                $"⑤ InkGun         : {inkGunInfo}\n" +
                $"⑥ 背景ペイント   : {bgInfo}\n" +
                $"{separator}"
            );

            // fullMode 時は接続フローをわかりやすく説明
            if (fullMode)
            {
                Debug.Log(
                    "[ZigSimDiagnostic] 期待する連携フロー:\n" +
                    "  ZIG SIM App\n" +
                    "    ↓ UDP パケット\n" +
                    "  UdpQuaternionReceiver（IsReceiving=true, ReceivedCount が増加）\n" +
                    "    ↓ ConsumeLatestRotation()\n" +
                    "  PoseRotationDriver（AimTarget に回転を適用）\n" +
                    "    ↓ AimRoot.rotation が変化\n" +
                    "  InkGun（aimTransform.forward の方向に発射）\n" +
                    "\n" +
                    "  ★ どこで「❌」が出るかを確認してください\n" +
                    "  ★ すべて「✅」でも AimRoot.rotation が動かなければ ZIGSIM の IP/Port 確認"
                );
            }
        }

        // ────────────────────────────────────────────────────────────
        // 手動トリガー（D キーで即時診断）
        // ────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.D))
            {
                Debug.Log("[ZigSimDiagnostic] ★ Dキー診断トリガー");
                RunDiagnostic(fullMode: true);
            }
        }
    }
}

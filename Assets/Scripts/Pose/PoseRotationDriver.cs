using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// スマホジャイロ（ZIG SIM）で照準を制御するドライバー。
    ///
    /// ▼ 精度改善ポイント（v2）
    ///   1. キャリブレーション平均化: 初回パケット1フレームではなく calibAverageFrames フレーム分を
    ///      Slerp 積算して基準姿勢を安定させる（小刻みな動きの影響を排除）
    ///   2. 入力クォータニオンのローパスフィルタ: 受信直後にノイズ除去 Slerp をかけ、
    ///      高周波ジッターを除去しつつ追従性を保つ
    ///   3. 出力スムージング: aimTarget への適用前にもう一段 Slerp をかけ
    ///      ガクつきを最終段でも除去する
    ///
    /// ▼ 左右が逆 → Inspector で invertLeftRight を ON
    /// ▼ 上下が逆 → Inspector で invertUpDown を ON
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    [AddComponentMenu("OmiyaFes/Pose Rotation Driver")]
    public class PoseRotationDriver : MonoBehaviour
    {
        public enum ControlMode
        {
            RotateGun,
            MoveCrosshair,
        }

        // ────────────────────────────────────────────────────────────
        // Inspector 設定
        // ────────────────────────────────────────────────────────────

        [Header("制御モード")]
        [SerializeField] private ControlMode controlMode = ControlMode.RotateGun;

        [Header("照準対象 Transform")]
        [SerializeField] private Transform aimTarget;

        [Header("キャリブレーション設定")]
        [Tooltip("最初のパケット受信時に自動キャリブレーションするか")]
        [SerializeField] private bool autoCalibrateOnFirstPacket = true;

        [Tooltip("キャリブレーション基準姿勢を何フレーム分で平均化するか（大きいほど安定。推奨: 10〜30）")]
        [SerializeField] [Range(1, 60)] private int calibAverageFrames = 20;

        [Header("入力ローパスフィルタ（ジッター除去）")]
        [Tooltip("受信クォータニオンに掛けるSLERPローパスフィルタ係数（0=フィルタなし、1に近いほど強い平滑化）")]
        [SerializeField] [Range(0f, 0.99f)] private float inputLowPassAlpha = 0.15f;

        [Header("出力スムージング")]
        [Tooltip("aimTargetへの適用時のスムージング量（0=即時、0.95で非常に滑らか）")]
        [SerializeField] [Range(0f, 0.99f)] private float rotationSmoothing = 0f;

        [Header("モデル補正")]
        [SerializeField] private Vector3 modelEulerOffset = Vector3.zero;

        [Header("向き反転補正")]
        [Tooltip("左右が反転しているときにON")]
        [SerializeField] private bool invertLeftRight = false;
        [Tooltip("上下が反転しているときにON")]
        [SerializeField] private bool invertUpDown = false;

        [Header("感度（MoveCrosshair モード用）")]
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityH = 3.0f;
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityV = 3.0f;
        [SerializeField] [Range(0f, 0.99f)] private float smoothing    = 0.1f;

        [Header("移動範囲（MoveCrosshair モード用）")]
        [SerializeField] private float maxYawDeg   = 40f;
        [SerializeField] private float maxPitchDeg = 30f;
        [SerializeField] private float screenDepth = 10f;

        [Header("デバッグ")]
        [SerializeField] private bool showDebugGizmos = true;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        public Transform AimTarget => aimTarget;
        public float CurrentYawDeg   { get; private set; }
        public float CurrentPitchDeg { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private UdpQuaternionReceiver _receiver;
        private Camera _mainCamera;

        // キャリブレーション
        private Quaternion _referenceSensorRotation = Quaternion.identity;
        private Quaternion _calibAccumulator         = Quaternion.identity; // 平均化用
        private int        _calibFrameCount          = 0;
        private bool       _hasCalibration           = false;
        private bool       _calibrating              = false;

        // ローパスフィルタ状態
        private Quaternion _filteredInput  = Quaternion.identity; // 低域フィルタ済みQuat
        private bool       _hasFilteredInput = false;

        // 出力
        private Quaternion _initialLocalRotation = Quaternion.identity;
        private Quaternion _targetLocalRotation  = Quaternion.identity;

        // MoveCrosshair 用
        private Vector3 _smoothedPosition;
        private bool    _positionInitialized;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _receiver   = GetComponent<UdpQuaternionReceiver>();
            _mainCamera = Camera.main;

            if (aimTarget == null) aimTarget = transform;
            _initialLocalRotation = aimTarget.localRotation;
            _targetLocalRotation  = _initialLocalRotation;
        }

        private void Update()
        {
            if (aimTarget == null || _receiver == null) return;

            Quaternion nextRaw;
            if (!_receiver.ConsumeLatestRotation(out nextRaw))
            {
                // 新パケットなし → スムージングのみ適用
                ApplySmoothingToTarget();
                return;
            }

            // ── ① 入力ローパスフィルタ ──────────────────────────────
            // 高周波ジッターをSLERPで抑制。inputLowPassAlpha=0ならフィルタなし。
            Quaternion filtered = ApplyInputLowPass(nextRaw);

            // ── ② キャリブレーション平均化 ──────────────────────────
            if (autoCalibrateOnFirstPacket && !_hasCalibration)
            {
                AccumulateCalibration(filtered);
                ApplySmoothingToTarget();
                return; // キャリブレーション中は照準を動かさない
            }

            // ── ③ 反転補正（1回だけ Euler で適用）────────────────────
            Quaternion relativeRotation = _hasCalibration
                ? QuaternionCalibrationUtility.CalculateRelativeRotation(_referenceSensorRotation, filtered)
                : filtered;

            if (invertLeftRight || invertUpDown)
            {
                Vector3 eu  = relativeRotation.eulerAngles;
                float pitch = Mathf.DeltaAngle(0f, eu.x);
                float yaw   = Mathf.DeltaAngle(0f, eu.y);
                float roll  = Mathf.DeltaAngle(0f, eu.z);
                relativeRotation = Quaternion.Euler(
                    invertUpDown    ? -pitch : pitch,
                    invertLeftRight ? -yaw   : yaw,
                    roll);
            }

            // ── ④ 公開プロパティ更新 ──────────────────────────────────
            Vector3 e       = relativeRotation.eulerAngles;
            CurrentYawDeg   = Mathf.DeltaAngle(0f, e.y);
            CurrentPitchDeg = Mathf.DeltaAngle(0f, e.x);

            // ── ⑤ モード別処理 ──────────────────────────────────────
            switch (controlMode)
            {
                case ControlMode.RotateGun:
                    ApplyRotateGun(relativeRotation);
                    break;
                case ControlMode.MoveCrosshair:
                    ApplyMoveCrosshair(relativeRotation);
                    break;
            }
        }

        // ────────────────────────────────────────────────────────────
        // ① 入力ローパスフィルタ
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// 指数移動平均的なSLERP ローパスフィルタ。
        /// alpha が大きいほど平滑化が強く（追従が遅い）、
        /// 0 なら即値（フィルタなし）。
        /// </summary>
        private Quaternion ApplyInputLowPass(Quaternion raw)
        {
            if (inputLowPassAlpha <= 0f || !_hasFilteredInput)
            {
                _filteredInput    = raw;
                _hasFilteredInput = true;
                return raw;
            }

            // Slerp(raw, filtered, alpha) ≒ 低域通過フィルタ
            // alpha=0.15 → 約85%を今回の値にすることで素早く追従しつつノイズを抑制
            _filteredInput = Quaternion.Slerp(raw, _filteredInput, inputLowPassAlpha);
            return _filteredInput;
        }

        // ────────────────────────────────────────────────────────────
        // ② キャリブレーション平均化
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// calibAverageFrames フレーム分のクォータニオンを SLERP 積算して
        /// 安定したキャリブレーション基準姿勢を求める。
        /// </summary>
        private void AccumulateCalibration(Quaternion q)
        {
            if (!_calibrating)
            {
                // 初回 → 積算開始
                _calibAccumulator = q;
                _calibFrameCount  = 1;
                _calibrating      = true;
                Debug.Log("[PoseRotationDriver] 📐 キャリブレーション開始（平均化中...）");
            }
            else
            {
                // Slerp で重み付き平均: 均等ウェイト = 1/(n+1)
                float t = 1f / (_calibFrameCount + 1);
                _calibAccumulator = Quaternion.Slerp(_calibAccumulator, q, t);
                _calibFrameCount++;
            }

            if (_calibFrameCount >= calibAverageFrames)
            {
                _referenceSensorRotation = _calibAccumulator;
                _hasCalibration          = true;
                _calibrating             = false;
                Debug.Log($"[PoseRotationDriver] ✅ キャリブレーション完了（{calibAverageFrames}フレーム平均）");
            }
        }

        // ────────────────────────────────────────────────────────────
        // MODE A: 銃の向きを直接制御（ARD 方式）
        // ────────────────────────────────────────────────────────────

        private void ApplyRotateGun(Quaternion relativeRotation)
        {
            Quaternion modelOffsetRot = Quaternion.Euler(modelEulerOffset);
            _targetLocalRotation = _initialLocalRotation * relativeRotation * modelOffsetRot;
            ApplySmoothingToTarget();
        }

        private void ApplySmoothingToTarget()
        {
            if (aimTarget == null) return;
            if (rotationSmoothing > 0f)
                aimTarget.localRotation = Quaternion.Slerp(
                    aimTarget.localRotation, _targetLocalRotation, 1f - rotationSmoothing);
            else
                aimTarget.localRotation = _targetLocalRotation;
        }

        // ────────────────────────────────────────────────────────────
        // MODE B: 傾きを画面上の位置に変換して移動
        // ────────────────────────────────────────────────────────────

        private void ApplyMoveCrosshair(Quaternion relativeRotation)
        {
            float pitch = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.x);
            float yaw   = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.y);

            // normV の -1 はスクリーン座標系の Y 軸反転補正（常に必要）
            float normH =  Mathf.Clamp(yaw   / maxYawDeg,   -1f, 1f);
            float normV = -Mathf.Clamp(pitch  / maxPitchDeg, -1f, 1f);

            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return;

            float halfW = Screen.width  * 0.5f;
            float halfH = Screen.height * 0.5f;

            float screenX = halfW + normH * halfW * sensitivityH;
            float screenY = halfH + normV * halfH * sensitivityV;

            Vector3 screenPos = new Vector3(
                Mathf.Clamp(screenX, 0f, Screen.width),
                Mathf.Clamp(screenY, 0f, Screen.height),
                screenDepth);
            Vector3 targetWorldPos = cam.ScreenToWorldPoint(screenPos);

            if (!_positionInitialized)
            {
                _smoothedPosition    = targetWorldPos;
                _positionInitialized = true;
            }
            else
            {
                _smoothedPosition = Vector3.Lerp(targetWorldPos, _smoothedPosition, smoothing);
            }

            aimTarget.position = _smoothedPosition;
            Vector3 dir = _smoothedPosition - cam.transform.position;
            if (dir.sqrMagnitude > 0.001f)
                aimTarget.rotation = Quaternion.LookRotation(dir.normalized);
        }

        // ────────────────────────────────────────────────────────────
        // キャリブレーション
        // ────────────────────────────────────────────────────────────

        public void ResetCalibration()
        {
            _hasCalibration      = false;
            _calibrating         = false;
            _calibFrameCount     = 0;
            _hasFilteredInput    = false; // フィルタ状態もリセット
            _positionInitialized = false;
            _targetLocalRotation = _initialLocalRotation;

            if (aimTarget != null)
                aimTarget.localRotation = _initialLocalRotation;

            if (_receiver != null)
                _receiver.ClearPendingRotation();

            Debug.Log("[PoseRotationDriver] 🔄 キャリブレーションリセット → 次のパケットから平均化開始");
        }

        public void Calibrate() => ResetCalibration();

        // ────────────────────────────────────────────────────────────
        // Gizmos
        // ────────────────────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos || aimTarget == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawRay(aimTarget.position, aimTarget.forward * 5f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(aimTarget.position, 0.15f);
        }
    }
}
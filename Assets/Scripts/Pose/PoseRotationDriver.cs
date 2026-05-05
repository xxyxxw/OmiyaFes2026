using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// スマホジャイロ（ZIG SIM）で照準を制御するドライバー。
    ///
    /// ▼ 動作フロー
    ///   1. UdpQuaternionReceiver.ConsumeLatestRotation() で変換済みクォータニオンを取得
    ///   2. 初回パケット or Cキー で referenceSensorRotation を保存（キャリブレーション）
    ///   3. QuaternionCalibrationUtility.CalculateRelativeRotation() で相対回転を算出
    ///   4. Update() 内で invertLeftRight / invertUpDown を 1回だけ適用
    ///   5. RotateGun / MoveCrosshair それぞれに渡す
    ///
    /// ▼ 左右が逆 → Inspector で invertLeftRight を ON
    /// ▼ 上下が逆 → Inspector で invertUpDown を ON
    /// ▼ 両方逆   → 両方 ON
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    [AddComponentMenu("OmiyaFes/Pose Rotation Driver")]
    public class PoseRotationDriver : MonoBehaviour
    {
        public enum ControlMode
        {
            /// <summary>スマホの向きで aimTarget の Rotation を直接制御する（ARD 方式）</summary>
            RotateGun,
            /// <summary>スマホの傾きを画面上の 2D 位置に変換して aimTarget を動かす</summary>
            MoveCrosshair,
        }

        // ────────────────────────────────────────────────────────────
        // Inspector 設定
        // ────────────────────────────────────────────────────────────

        [Header("制御モード")]
        [Tooltip("RotateGun: スマホ向き＝銃の向き（推奨） / MoveCrosshair: スマホ傾き＝銃の位置")]
        [SerializeField] private ControlMode controlMode = ControlMode.RotateGun;

        [Header("照準対象 Transform")]
        [Tooltip("向きまたは位置を制御するオブジェクト（銃、照準カーソルなど）")]
        [SerializeField] private Transform aimTarget;

        [Header("キャリブレーション設定")]
        [Tooltip("最初のパケット受信時に自動キャリブレーションするか")]
        [SerializeField] private bool autoCalibrateOnFirstPacket = true;

        [Tooltip("スムージング量（0=即時適用、0より大きいと Slerp で滑らか）")]
        [SerializeField] [Range(0f, 0.95f)] private float rotationSmoothing = 0f;

        [Tooltip("モデルの初期向き補正（Euler 角で指定）")]
        [SerializeField] private Vector3 modelEulerOffset = Vector3.zero;

        [Header("向き反転補正（左右が逆ならON / 上下が逆ならON）")]
        [Tooltip("左右が反転しているときにON")]
        [SerializeField] private bool invertLeftRight = false;

        [Tooltip("上下が反転しているときにON")]
        [SerializeField] private bool invertUpDown = false;

        [Header("感度（MoveCrosshair モード用）")]
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityH = 3.0f;
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityV = 3.0f;
        [SerializeField] [Range(0f, 0.95f)] private float smoothing    = 0.1f;

        [Header("移動範囲（MoveCrosshair モード用）")]
        [SerializeField] private float maxYawDeg   = 40f;
        [SerializeField] private float maxPitchDeg = 30f;
        [SerializeField] private float screenDepth = 10f;

        [Header("デバッグ")]
        [SerializeField] private bool showDebugGizmos = true;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>外部スクリプト（InkGun など）から参照できる照準 Transform</summary>
        public Transform AimTarget => aimTarget;

        /// <summary>現在のキャリブレーション後の Yaw 角度（度）</summary>
        public float CurrentYawDeg { get; private set; }

        /// <summary>現在のキャリブレーション後の Pitch 角度（度）</summary>
        public float CurrentPitchDeg { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private UdpQuaternionReceiver _receiver;
        private Camera _mainCamera;

        private Quaternion _referenceSensorRotation = Quaternion.identity;
        private Quaternion _initialLocalRotation    = Quaternion.identity;
        private Quaternion _targetLocalRotation     = Quaternion.identity;
        private bool _hasCalibration;

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

            // 新しいパケットがなければスムージングだけ適用して終了
            Quaternion nextRotation;
            if (!_receiver.ConsumeLatestRotation(out nextRotation))
            {
                ApplySmoothingToTarget();
                return;
            }

            // 初回パケット → 自動キャリブレーション
            if (autoCalibrateOnFirstPacket && !_hasCalibration)
            {
                _referenceSensorRotation = nextRotation;
                _hasCalibration = true;
                Debug.Log("[PoseRotationDriver] ✅ 自動キャリブレーション（初回パケット）");
            }

            // 基準からの相対回転を算出
            Quaternion relativeRotation = _hasCalibration
                ? QuaternionCalibrationUtility.CalculateRelativeRotation(_referenceSensorRotation, nextRotation)
                : nextRotation;

            // ── 反転補正（Update()内で1回だけ適用） ─────────────────────────
            // RotateGun / MoveCrosshair 両方に同じ補正が反映される。
            // 左右が逆 → Inspector で invertLeftRight を ON
            // 上下が逆 → Inspector で invertUpDown を ON
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
            // ─────────────────────────────────────────────────────────────────

            // 公開プロパティ更新
            Vector3 e       = relativeRotation.eulerAngles;
            CurrentYawDeg   = Mathf.DeltaAngle(0f, e.y);
            CurrentPitchDeg = Mathf.DeltaAngle(0f, e.x);

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
            // invertLeftRight / invertUpDown は Update() で既に適用済み。
            // ここでは二重反転を避けるため符号を改めて掛けない。
            // signV = -1 はスクリーン座標系の Y 軸反転補正（常に必要）。
            float pitch = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.x);
            float yaw   = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.y);

            float normH =  Mathf.Clamp(yaw   / maxYawDeg,   -1f, 1f);
            float normV = -Mathf.Clamp(pitch  / maxPitchDeg, -1f, 1f); // 画面Y座標補正

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

        /// <summary>
        /// キャリブレーションリセット。次の受信パケットを基準として保存する。
        /// PoseCalibrationCoordinator から C キーで呼ばれる。
        /// </summary>
        public void ResetCalibration()
        {
            _hasCalibration      = false;
            _positionInitialized = false;
            _targetLocalRotation = _initialLocalRotation;
            if (aimTarget != null)
                aimTarget.localRotation = _initialLocalRotation;

            if (_receiver != null)
                _receiver.ClearPendingRotation();

            Debug.Log("[PoseRotationDriver] 🔄 キャリブレーションリセット → 次のパケットで自動キャリブレーション");
        }

        /// <summary>後方互換ラッパー。旧来の Calibrate() 呼び出し元が壊れないよう残す。</summary>
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
using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ARD (uni-bit/yugo-ShibaLab-ARD) 方式に寄せた姿勢制御ドライバー。
    ///
    /// ▼ 動作フロー
    ///   1. UdpQuaternionReceiver.ConsumeLatestRotation() で変換済みクォータニオンを取得
    ///   2. 初回パケット or Cキー で referenceSensorRotation を保存（キャリブレーション）
    ///   3. QuaternionCalibrationUtility.CalculateRelativeRotation() で相対回転を算出
    ///   4. invertLeftRight / invertUpDown で向き補正
    ///   5. initialLocalRotation * relativeRotation * modelEulerOffset を aimTarget.localRotation に適用
    ///
    /// ▼ 左右・上下が逆なら Inspector の invertLeftRight / invertUpDown を ON にする
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    [AddComponentMenu("OmiyaFes/Pose Rotation Driver")]
    public class PoseRotationDriver : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // 列挙型
        // ────────────────────────────────────────────────────────────

        public enum ControlMode
        {
            /// <summary>スマホの向きで aimTarget の Rotation を直接制御する（ARD 方式）</summary>
            RotateGun,
            /// <summary>スマホの傾きを画面上の 2D 位置に変換して aimTarget を動かす</summary>
            MoveCrosshair,
        }

        // ────────────────────────────────────────────────────────────
        // インスペクター設定
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

        [Header("向き反転補正（動作がおかしい場合にONにする）")]
        [Tooltip("左右が反転しているときにON")]
        [SerializeField] private bool invertLeftRight = true;

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
        // 公開プロパティ（後方互換維持）
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

        // ARD 方式キャリブレーション
        private Quaternion _referenceSensorRotation = Quaternion.identity;
        private Quaternion _initialLocalRotation    = Quaternion.identity;
        private Quaternion _targetLocalRotation     = Quaternion.identity;
        private bool _hasCalibration;

        // MoveCrosshair 用スムージングバッファ
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
            if (aimTarget == null) return;
            if (_receiver == null) return;

            // 新しいパケットがあれば取得（変換済み・半球安定化済み）
            Quaternion nextRotation;
            if (!_receiver.ConsumeLatestRotation(out nextRotation))
            {
                ApplySmoothingToTarget();
                return;
            }

            // 初回パケット 自動キャリブレーション
            if (autoCalibrateOnFirstPacket && !_hasCalibration)
            {
                _referenceSensorRotation = nextRotation;
                _hasCalibration = true;
                Debug.Log("[PoseRotationDriver] ✅ 自動キャリブレーション（初回パケット）");
            }

            // 相対回転を算出（基準からの差分）
            Quaternion relativeRotation = _hasCalibration
                ? QuaternionCalibrationUtility.CalculateRelativeRotation(_referenceSensorRotation, nextRotation)
                : nextRotation;

            // ────────────────────────────────────────────────────────
            // 向き反転補正
            // invertLeftRight / invertUpDown を Inspector で ON/OFF して調整する。
            // 既存シリアライズ値に依存しない新規 bool なので必ずコードデフォルトが使われる。
            // ────────────────────────────────────────────────────────
            if (invertLeftRight || invertUpDown)
            {
                Vector3 eu    = relativeRotation.eulerAngles;
                float pitch   = Mathf.DeltaAngle(0f, eu.x);
                float yaw     = Mathf.DeltaAngle(0f, eu.y);
                float roll    = Mathf.DeltaAngle(0f, eu.z);
                relativeRotation = Quaternion.Euler(
                    invertUpDown    ? -pitch : pitch,
                    invertLeftRight ? -yaw   : yaw,
                    roll);
            }

            // Yaw/Pitch を更新（UI / AimingController 用）
            Vector3 euler = relativeRotation.eulerAngles;
            CurrentYawDeg   = Mathf.DeltaAngle(0f, euler.y);
            CurrentPitchDeg = Mathf.DeltaAngle(0f, euler.x);

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
                aimTarget.localRotation = Quaternion.Slerp(aimTarget.localRotation, _targetLocalRotation, 1f - rotationSmoothing);
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
        // 公開メソッド（キャリブレーション）
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// ARD 方式キャリブレーションリセット。
        /// PoseCalibrationCoordinator から C キーで呼ばれる。
        /// 次の受信パケットをリファレンスとして保存し直す。
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

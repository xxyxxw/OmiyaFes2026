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
    ///   4. initialLocalRotation * relativeRotation * modelEulerOffset を作る
    ///   5. 必要ならカメラ基準で光線方向を左右/上下反転する
    ///   6. aimTarget.localRotation に適用
    ///
    /// ▼ 左右・上下が逆なら Inspector の invertLeftRight / invertUpDown を ON にする
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    [AddComponentMenu("OmiyaFes/Pose Rotation Driver")]
    public class PoseRotationDriver : MonoBehaviour
    {
        public enum ControlMode
        {
            /// <summary>スマホの向きで aimTarget の Rotation を直接制御する</summary>
            RotateGun,

            /// <summary>スマホの傾きを画面上の 2D 位置に変換して aimTarget を動かす</summary>
            MoveCrosshair,
        }

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

        [Header("向き反転補正")]
        [Tooltip("左右が反転しているときにON")]
        [SerializeField] private bool invertLeftRight = true;

        [Tooltip("上下が反転しているときにON")]
        [SerializeField] private bool invertUpDown = true;

        [Tooltip("反転補正の基準。未設定なら Main Camera を使う")]
        [SerializeField] private Transform correctionReference;

        [Header("感度（MoveCrosshair モード用）")]
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityH = 3.0f;
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityV = 3.0f;
        [SerializeField] [Range(0f, 0.95f)] private float smoothing = 0.1f;

        [Header("移動範囲（MoveCrosshair モード用）")]
        [SerializeField] private float maxYawDeg = 40f;
        [SerializeField] private float maxPitchDeg = 30f;
        [SerializeField] private float screenDepth = 10f;

        [Header("デバッグ")]
        [SerializeField] private bool showDebugGizmos = true;

        /// <summary>外部スクリプト（InkGun など）から参照できる照準 Transform</summary>
        public Transform AimTarget => aimTarget;

        /// <summary>現在のキャリブレーション後の Yaw 角度（度）</summary>
        public float CurrentYawDeg { get; private set; }

        /// <summary>現在のキャリブレーション後の Pitch 角度（度）</summary>
        public float CurrentPitchDeg { get; private set; }

        private UdpQuaternionReceiver _receiver;
        private Camera _mainCamera;

        private Quaternion _referenceSensorRotation = Quaternion.identity;
        private Quaternion _initialLocalRotation = Quaternion.identity;
        private Quaternion _targetLocalRotation = Quaternion.identity;
        private bool _hasCalibration;

        private Vector3 _smoothedPosition;
        private bool _positionInitialized;

        private void Awake()
        {
            _receiver = GetComponent<UdpQuaternionReceiver>();
            _mainCamera = Camera.main;

            if (aimTarget == null)
            {
                aimTarget = transform;
            }

            if (correctionReference == null && _mainCamera != null)
            {
                correctionReference = _mainCamera.transform;
            }

            _initialLocalRotation = aimTarget.localRotation;
            _targetLocalRotation = _initialLocalRotation;
        }

        private void Update()
        {
            if (aimTarget == null)
            {
                return;
            }

            if (_receiver == null)
            {
                return;
            }

            Quaternion nextRotation;
            if (!_receiver.ConsumeLatestRotation(out nextRotation))
            {
                ApplySmoothingToTarget();
                return;
            }

            if (autoCalibrateOnFirstPacket && !_hasCalibration)
            {
                _referenceSensorRotation = nextRotation;
                _hasCalibration = true;
                Debug.Log("[PoseRotationDriver] ✅ 自動キャリブレーション（初回パケット）");
            }

            Quaternion relativeRotation = _hasCalibration
                ? QuaternionCalibrationUtility.CalculateRelativeRotation(_referenceSensorRotation, nextRotation)
                : nextRotation;

            Vector3 euler = relativeRotation.eulerAngles;
            CurrentYawDeg = Mathf.DeltaAngle(0f, euler.y);
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

        private void ApplyRotateGun(Quaternion relativeRotation)
        {
            Quaternion modelOffsetRot = Quaternion.Euler(modelEulerOffset);
            Quaternion rawTargetLocalRotation = _initialLocalRotation * relativeRotation * modelOffsetRot;

            _targetLocalRotation = ApplyScreenSpaceDirectionInversion(rawTargetLocalRotation);
            ApplySmoothingToTarget();
        }

        /// <summary>
        /// 最終的な aimTarget.forward をカメラ/基準Transform空間で左右・上下反転する。
        /// Euler角ではなく、実際に出る光線方向を補正するため、銃口の光線確認に強い。
        /// </summary>
        private Quaternion ApplyScreenSpaceDirectionInversion(Quaternion targetLocalRotation)
        {
            if (!invertLeftRight && !invertUpDown)
            {
                return targetLocalRotation;
            }

            Transform reference = ResolveCorrectionReference();
            if (reference == null)
            {
                return targetLocalRotation;
            }

            Quaternion parentWorldRotation = aimTarget.parent != null
                ? aimTarget.parent.rotation
                : Quaternion.identity;

            Quaternion targetWorldRotation = parentWorldRotation * targetLocalRotation;
            Vector3 worldForward = targetWorldRotation * Vector3.forward;

            if (worldForward.sqrMagnitude <= 0.000001f)
            {
                return targetLocalRotation;
            }

            Vector3 referenceLocalForward = reference.InverseTransformDirection(worldForward.normalized);

            if (invertLeftRight)
            {
                referenceLocalForward.x = -referenceLocalForward.x;
            }

            if (invertUpDown)
            {
                referenceLocalForward.y = -referenceLocalForward.y;
            }

            Vector3 correctedWorldForward = reference.TransformDirection(referenceLocalForward.normalized);

            if (correctedWorldForward.sqrMagnitude <= 0.000001f)
            {
                return targetLocalRotation;
            }

            Quaternion correctedWorldRotation = Quaternion.LookRotation(
                correctedWorldForward.normalized,
                reference.up
            );

            Quaternion correctedLocalRotation = aimTarget.parent != null
                ? Quaternion.Inverse(aimTarget.parent.rotation) * correctedWorldRotation
                : correctedWorldRotation;

            return correctedLocalRotation;
        }

        private Transform ResolveCorrectionReference()
        {
            if (correctionReference != null)
            {
                return correctionReference;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            return _mainCamera != null ? _mainCamera.transform : null;
        }

        private void ApplySmoothingToTarget()
        {
            if (aimTarget == null)
            {
                return;
            }

            if (rotationSmoothing > 0f)
            {
                aimTarget.localRotation = Quaternion.Slerp(
                    aimTarget.localRotation,
                    _targetLocalRotation,
                    1f - rotationSmoothing
                );
            }
            else
            {
                aimTarget.localRotation = _targetLocalRotation;
            }
        }

        private void ApplyMoveCrosshair(Quaternion relativeRotation)
        {
            float pitch = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.x);
            float yaw   = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.y);

            // ✅ invertLeftRight / invertUpDown の意味を統一（1回だけ反転）
            // normV の "-" を signV に吸収して二重反転を排除する
            float signH = invertLeftRight ? -1f : 1f;
            float signV = invertUpDown    ?  1f : -1f;  // normV の "-" を吸収

            float normH = Mathf.Clamp(yaw   / maxYawDeg,   -1f, 1f) * signH;
            float normV = Mathf.Clamp(pitch  / maxPitchDeg, -1f, 1f) * signV;


            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            float halfW = Screen.width * 0.5f;
            float halfH = Screen.height * 0.5f;

            float screenX = halfW + normH * halfW * sensitivityH;
            float screenY = halfH + normV * halfH * sensitivityV;

            Vector3 screenPos = new Vector3(
                Mathf.Clamp(screenX, 0f, Screen.width),
                Mathf.Clamp(screenY, 0f, Screen.height),
                screenDepth
            );

            Vector3 targetWorldPos = cam.ScreenToWorldPoint(screenPos);

            if (!_positionInitialized)
            {
                _smoothedPosition = targetWorldPos;
                _positionInitialized = true;
            }
            else
            {
                _smoothedPosition = Vector3.Lerp(targetWorldPos, _smoothedPosition, smoothing);
            }

            aimTarget.position = _smoothedPosition;

            Vector3 dir = _smoothedPosition - cam.transform.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                aimTarget.rotation = Quaternion.LookRotation(dir.normalized);
            }
        }

        public void ResetCalibration()
        {
            _hasCalibration = false;
            _positionInitialized = false;
            _targetLocalRotation = _initialLocalRotation;

            if (aimTarget != null)
            {
                aimTarget.localRotation = _initialLocalRotation;
            }

            if (_receiver != null)
            {
                _receiver.ClearPendingRotation();
            }

            Debug.Log("[PoseRotationDriver] 🔄 キャリブレーションリセット → 次のパケットで自動キャリブレーション");
        }

        public void Calibrate()
        {
            ResetCalibration();
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos || aimTarget == null)
            {
                return;
            }

            Gizmos.color = Color.red;
            Gizmos.DrawRay(aimTarget.position, aimTarget.forward * 5f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(aimTarget.position, 0.15f);
        }
    }
}
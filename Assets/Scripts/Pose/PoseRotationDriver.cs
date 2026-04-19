using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ZIG SIM のジャイロ値でスポットライト（照準）の向きを制御する。
    /// UdpQuaternionReceiver → このコンポーネント の順で Update される前提。
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    public class PoseRotationDriver : MonoBehaviour
    {
        [Header("照準対象")]
        [Tooltip("向きを制御するスポットライト or 照準オブジェクト")]
        [SerializeField] private Transform aimTarget;
        public Transform AimTarget => aimTarget;

        [Header("感度")]
        [SerializeField] private float sensitivityH = 1.0f;
        [SerializeField] private float sensitivityV = 1.0f;

        private UdpQuaternionReceiver _receiver;
        private Quaternion _calibrationOffset = Quaternion.identity;

        private void Awake()
        {
            _receiver = GetComponent<UdpQuaternionReceiver>();
        }

        private void Update()
        {
            if (!_receiver.IsReceiving) return;
            if (aimTarget == null) return;

            Quaternion rawIos = _receiver.LatestQuaternion;
            Quaternion unity  = QuaternionCoordinateConverter.IosToUnity(rawIos);
            Quaternion calibrated = Quaternion.Inverse(_calibrationOffset) * unity;

            // 感度を加味した回転を適用
            Vector3 euler = calibrated.eulerAngles;
            euler.x *= sensitivityV;
            euler.y *= sensitivityH;
            aimTarget.rotation = Quaternion.Euler(euler);
        }

        /// <summary>現在のスマホの向きをキャリブレーション基準点として保存する</summary>
        public void Calibrate()
        {
            if (!_receiver.IsReceiving)
            {
                Debug.LogWarning("[PoseRotationDriver] ZIG SIM 未受信中のためキャリブレーション不可");
                return;
            }
            _calibrationOffset = QuaternionCoordinateConverter.IosToUnity(_receiver.LatestQuaternion);
            Debug.Log("[PoseRotationDriver] キャリブレーション完了");
        }
    }
}

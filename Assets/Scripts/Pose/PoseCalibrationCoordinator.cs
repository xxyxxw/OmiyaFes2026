using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ARD 方式のキャリブレーション調整役。
    /// - C キーで ResetCalibration() を呼ぶ
    /// - スマホタッチによる ConsumePendingRecenterRequest() にも対応
    /// </summary>
    [AddComponentMenu("OmiyaFes/Pose Calibration Coordinator")]
    public class PoseCalibrationCoordinator : MonoBehaviour
    {
        [Tooltip("キャリブレーションをトリガーするキー（デフォルト: C）")]
        [SerializeField] private KeyCode recenterKey = KeyCode.C;

        // 参照は自動解決 + Inspector 手動指定の両方に対応
        [SerializeField] private UdpQuaternionReceiver receiver;
        [SerializeField] private PoseRotationDriver    driver;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        private void Update()
        {
            // C キーでリキャリブレーション
            if (Input.GetKeyDown(recenterKey))
            {
                ResetAllCalibration();
            }

            // スマホタッチによるリセンター要求（ConsumePendingRecenterRequest）
            if (receiver != null && receiver.ConsumePendingRecenterRequest())
            {
                ResetAllCalibration();
            }
        }

        /// <summary>全コンポーネントのキャリブレーションをリセットする。</summary>
        public void ResetAllCalibration()
        {
            if (driver != null)
                driver.ResetCalibration();

            Debug.Log("[PoseCalibrationCoordinator] ✅ キャリブレーションリセット完了");
        }

        /// <summary>後方互換: 旧 Calibrate() 呼び出しのラッパー</summary>
        public void Calibrate() => ResetAllCalibration();

        private void ResolveReferences()
        {
            if (receiver == null) receiver = GetComponent<UdpQuaternionReceiver>();
            if (driver   == null) driver   = GetComponent<PoseRotationDriver>();
        }
    }
}

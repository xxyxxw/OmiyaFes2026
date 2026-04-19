using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// Cキーでジャイロのキャリブレーションを実行する。
    /// </summary>
    [RequireComponent(typeof(PoseRotationDriver))]
    public class PoseCalibrationCoordinator : MonoBehaviour
    {
        [SerializeField] private KeyCode calibrationKey = KeyCode.C;

        private PoseRotationDriver _driver;

        private void Awake()
        {
            _driver = GetComponent<PoseRotationDriver>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(calibrationKey))
            {
                _driver.Calibrate();
            }
        }
    }
}

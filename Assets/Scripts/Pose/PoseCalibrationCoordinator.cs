using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// Cキーでジャイロのキャリブレーションを実行する。
    /// </summary>
    [RequireComponent(typeof(PoseRotationDriver))] // 同 GameObject に PoseRotationDriver が必須
    public class PoseCalibrationCoordinator : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        // キャリブレーションをトリガーするキー（デフォルト: C）
        [SerializeField] private KeyCode calibrationKey = KeyCode.C;

        // ────────────────────────────────────────────────────────────
        // 内部参照
        // ────────────────────────────────────────────────────────────

        private PoseRotationDriver _driver; // 同 GameObject の PoseRotationDriver

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _driver = GetComponent<PoseRotationDriver>();
        }

        private void Update()
        {
            // 設定したキーが押された瞬間にキャリブレーションを実行
            if (Input.GetKeyDown(calibrationKey))
            {
                _driver.Calibrate();
            }
        }
    }
}

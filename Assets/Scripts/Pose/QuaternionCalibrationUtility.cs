using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// クォータニオンのキャリブレーション計算ユーティリティ。
    /// ARD (uni-bit/yugo-ShibaLab-ARD) と同一ロジックを OmiyaFes 名前空間に移植。
    /// </summary>
    public static class QuaternionCalibrationUtility
    {
        /// <summary>
        /// 基準回転と現在回転から「基準からの相対回転」を計算する。
        /// Calibrate() 時の回転を reference として渡すと、以後の回転が正面基準になる。
        /// </summary>
        public static Quaternion CalculateRelativeRotation(Quaternion referenceRotation, Quaternion currentRotation)
        {
            return Quaternion.Inverse(referenceRotation) * currentRotation;
        }
    }
}

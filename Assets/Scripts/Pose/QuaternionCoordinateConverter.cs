using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// iOS（ZIG SIM）のクォータニオン座標系を Unity 座標系に変換する。
    /// iOS: 右手系（Y上、Z奥） → Unity: 左手系（Y上、Z手前）
    /// </summary>
    public static class QuaternionCoordinateConverter
    {
        /// <summary>
        /// iOS クォータニオン → Unity クォータニオン変換。
        /// ZIG SIM の QUATERNION データはこの変換を通すこと。
        /// </summary>
        public static Quaternion IosToUnity(Quaternion ios)
        {
            // X軸・W軸を反転することで右手系→左手系へ変換
            return new Quaternion(-ios.x, ios.y, ios.z, -ios.w);
        }
    }
}

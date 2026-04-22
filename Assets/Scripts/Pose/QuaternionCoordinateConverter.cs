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
            // ── 座標系変換の考え方 ──────────────────────────────
            // 右手系から左手系への変換は「Z軸を反転」することで行う。
            // クォータニオン (x, y, z, w) で Z 軸を反転するには
            // X 成分と W 成分を反転する（数学的等価変換）。
            // 結果: (-x, y, z, -w)
            return new Quaternion(-ios.x, ios.y, ios.z, -ios.w);
        }
    }
}

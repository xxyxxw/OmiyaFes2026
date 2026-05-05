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
            // iOS(右手系, Y上, Z手前) → Unity(左手系, Y上, Z奥) への変換:
            //
            //  Unity.x = -iOS.x  (Pitch: 上下方向の回転)
            //  Unity.y = -iOS.y  (Yaw:   左右方向の回転) ← ここを -y にしないと横方向が動かない
            //  Unity.z =  iOS.z  (Roll:  傾き)
            //  Unity.w =  iOS.w  (スカラー部は符号そのまま)
            //
            // 旧式の (-x, y, z, w) は Yaw(Y軸)が反転されておらず
            // 左右方向への動きが Unity に正しく伝わらなかった。
            return new Quaternion(-ios.x, -ios.y, ios.z, ios.w);
        }
    }
}

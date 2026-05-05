using UnityEngine;

namespace OmiyaFes2026.Pose
{
    // ────────────────────────────────────────────────────────────────────────────
    // ARD (uni-bit/yugo-ShibaLab-ARD) の QuaternionCoordinateConverter を
    // OmiyaFes2026.Pose 名前空間に完全移植。
    // IosToUnity() は後方互換メソッドとして残す。
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>センサーのクォータニオン座標系プリセット</summary>
    public enum QuaternionCoordinatePreset
    {
        /// <summary>iPhone CoreMotion（ZIG SIM デフォルト）</summary>
        IPhoneCoreMotion = 0,
        /// <summary>Android RotationVector / SensorEvent.TYPE_ROTATION_VECTOR</summary>
        AndroidRotationVector = 1,
    }

    /// <summary>クォータニオン成分のソース指定</summary>
    public enum QuaternionComponentSource
    {
        X = 0,
        Y = 1,
        Z = 2,
        W = 3,
    }

    /// <summary>
    /// センサー座標系（iOS/Android）→ Unity 座標系への変換。
    /// </summary>
    public static class QuaternionCoordinateConverter
    {
        // ────────────────────────────────────────────────────────────
        // 公開 API
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// センサークォータニオンを Unity 左手系に変換する（ARD 完全移植版）。
        /// </summary>
        /// <param name="sensorQuaternion">センサーから来た生クォータニオン</param>
        /// <param name="coordinatePreset">センサーの種類</param>
        /// <param name="eulerOffset">追加の Euler オフセット（通常 Vector3.zero）</param>
        /// <param name="convertHandedness">座標系変換を行うか</param>
        /// <param name="screenFaceDown">スマホ画面を下向きに持つか（face-down）</param>
        public static Quaternion ConvertToUnity(
            Quaternion sensorQuaternion,
            QuaternionCoordinatePreset coordinatePreset,
            Vector3 eulerOffset,
            bool convertHandedness = true,
            bool screenFaceDown    = false)
        {
            // ① 正規化
            Quaternion normalizedSensor = NormalizeQuaternion(sensorQuaternion);

            // ② 半球統一（w < 0 なら全符号反転してガクつきを防ぐ）
            if (normalizedSensor.w < 0f)
            {
                normalizedSensor = new Quaternion(
                    -normalizedSensor.x,
                    -normalizedSensor.y,
                    -normalizedSensor.z,
                    -normalizedSensor.w);
            }

            // ③ 座標系変換
            Quaternion unityRotation = coordinatePreset == QuaternionCoordinatePreset.IPhoneCoreMotion
                ? ConvertIPhoneCoreMotion(normalizedSensor, convertHandedness, screenFaceDown)
                : (convertHandedness ? ConvertHandedness(normalizedSensor, coordinatePreset, screenFaceDown) : normalizedSensor);

            // ④ ゼロガード
            if (unityRotation.x == 0f && unityRotation.y == 0f
                && unityRotation.z == 0f && unityRotation.w == 0f)
            {
                return Quaternion.identity;
            }

            // ⑤ Euler オフセット適用
            Quaternion offsetRotation = Quaternion.Euler(eulerOffset);
            return Quaternion.Normalize(offsetRotation * unityRotation);
        }

        /// <summary>
        /// 成分を自由に入れ替えて符号を掛けることで任意の軸リマップを行う。
        /// </summary>
        public static Quaternion RemapRawQuaternion(
            Quaternion sensorQuaternion,
            QuaternionComponentSource xSource,
            QuaternionComponentSource ySource,
            QuaternionComponentSource zSource,
            QuaternionComponentSource wSource,
            Vector4 signMultiplier)
        {
            return new Quaternion(
                ReadComponent(sensorQuaternion, xSource) * signMultiplier.x,
                ReadComponent(sensorQuaternion, ySource) * signMultiplier.y,
                ReadComponent(sensorQuaternion, zSource) * signMultiplier.z,
                ReadComponent(sensorQuaternion, wSource) * signMultiplier.w);
        }

        /// <summary>
        /// キャリブレーション後の相対回転にプリセット軸符号補正を適用する。
        /// iPhoneAxisSigns / androidAxisSigns でどの軸を反転するか指定する。
        /// </summary>
        public static Quaternion ApplyRelativeAxisPreset(
            Quaternion relativeRotation,
            QuaternionCoordinatePreset coordinatePreset,
            Vector3 iPhoneAxisSigns,
            Vector3 androidAxisSigns)
        {
            Vector3 euler    = ToSignedEuler(relativeRotation);
            Vector3 axisSigns = coordinatePreset == QuaternionCoordinatePreset.AndroidRotationVector
                ? androidAxisSigns
                : iPhoneAxisSigns;

            euler = new Vector3(
                euler.x * Mathf.Sign(Mathf.Approximately(axisSigns.x, 0f) ? 1f : axisSigns.x),
                euler.y * Mathf.Sign(Mathf.Approximately(axisSigns.y, 0f) ? 1f : axisSigns.y),
                euler.z * Mathf.Sign(Mathf.Approximately(axisSigns.z, 0f) ? 1f : axisSigns.z));

            return Quaternion.Euler(euler);
        }

        /// <summary>右手系 Y-Up（iPhone CoreMotion）→ Unity 変換のショートカット</summary>
        public static Quaternion ConvertRightHandedYUpToUnity(
            Quaternion sensorQuaternion,
            Vector3 eulerOffset,
            bool convertHandedness = true)
        {
            return ConvertToUnity(
                sensorQuaternion,
                QuaternionCoordinatePreset.IPhoneCoreMotion,
                eulerOffset,
                convertHandedness);
        }

        /// <summary>
        /// 後方互換メソッド。既存コードが IosToUnity() を呼んでいても壊れないよう残す。
        /// 内部では ConvertToUnity(IPhoneCoreMotion, screenFaceDown=false) を使う。
        /// </summary>
        public static Quaternion IosToUnity(Quaternion ios)
        {
            return ConvertToUnity(
                ios,
                QuaternionCoordinatePreset.IPhoneCoreMotion,
                Vector3.zero,
                convertHandedness: true,
                screenFaceDown: false);
        }

        // ────────────────────────────────────────────────────────────
        // 正規化
        // ────────────────────────────────────────────────────────────

        public static Quaternion NormalizeQuaternion(Quaternion quaternion)
        {
            float mag = Mathf.Sqrt(
                quaternion.x * quaternion.x +
                quaternion.y * quaternion.y +
                quaternion.z * quaternion.z +
                quaternion.w * quaternion.w);

            if (mag <= 0.000001f) return Quaternion.identity;

            float invMag = 1f / mag;
            return new Quaternion(
                quaternion.x * invMag,
                quaternion.y * invMag,
                quaternion.z * invMag,
                quaternion.w * invMag);
        }

        // ────────────────────────────────────────────────────────────
        // 内部変換
        // ────────────────────────────────────────────────────────────

        /// <summary>iPhone CoreMotion → Unity 変換（LookRotation ベース・ARD オリジナル）</summary>
        private static Quaternion ConvertIPhoneCoreMotion(
            Quaternion sensorQuaternion,
            bool convertHandedness,
            bool screenFaceDown = false)
        {
            if (!convertHandedness) return sensorQuaternion;

            // CoreMotion デバイス軸:
            //   +X = 画面右, +Y = 画面上端方向（top）, +Z = 画面外向き（ユーザー側）
            //
            // スマホを「銃のように前方に向けて横持ち（top が前方）」する想定。
            // deviceTop をポインタの forward, -deviceScreenOut をポインタの up にマップ。
            Vector3 deviceTop       = RotateVector(sensorQuaternion, Vector3.up);
            Vector3 deviceScreenOut = RotateVector(sensorQuaternion, Vector3.forward);

            if (deviceTop.sqrMagnitude <= 0.000001f || deviceScreenOut.sqrMagnitude <= 0.000001f)
                return Quaternion.identity;

            if (screenFaceDown)
            {
                // Face-down（画面下向き）: 上下が反転するため補正
                return Quaternion.LookRotation(deviceTop.normalized, deviceScreenOut.normalized);
            }

            // 通常（face-up / 横持ち）
            return Quaternion.LookRotation(deviceTop.normalized, -deviceScreenOut.normalized);
        }



        /// <summary>Android RotationVector → Unity 左手系変換</summary>
        private static Quaternion ConvertHandedness(
            Quaternion sensorQuaternion,
            QuaternionCoordinatePreset coordinatePreset,
            bool screenFaceDown = false)
        {
            switch (coordinatePreset)
            {
                case QuaternionCoordinatePreset.AndroidRotationVector:
                    if (screenFaceDown)
                        return new Quaternion(-sensorQuaternion.x,  sensorQuaternion.y, -sensorQuaternion.z, -sensorQuaternion.w);
                    return     new Quaternion( sensorQuaternion.x,  sensorQuaternion.y, -sensorQuaternion.z, -sensorQuaternion.w);

                case QuaternionCoordinatePreset.IPhoneCoreMotion:
                default:
                    return     new Quaternion( sensorQuaternion.x,  sensorQuaternion.y, -sensorQuaternion.z, -sensorQuaternion.w);
            }
        }

        // ────────────────────────────────────────────────────────────
        // ユーティリティ
        // ────────────────────────────────────────────────────────────

        private static Vector3 ToSignedEuler(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            return new Vector3(
                Mathf.DeltaAngle(0f, euler.x),
                Mathf.DeltaAngle(0f, euler.y),
                Mathf.DeltaAngle(0f, euler.z));
        }

        private static float ReadComponent(Quaternion q, QuaternionComponentSource src)
        {
            switch (src)
            {
                case QuaternionComponentSource.X: return q.x;
                case QuaternionComponentSource.Y: return q.y;
                case QuaternionComponentSource.Z: return q.z;
                case QuaternionComponentSource.W: return q.w;
                default: return 0f;
            }
        }

        /// <summary>クォータニオンによるベクトル回転（行列展開版・GC ゼロ）</summary>
        private static Vector3 RotateVector(Quaternion q, Vector3 v)
        {
            float xx = q.x + q.x; float yy = q.y + q.y; float zz = q.z + q.z;
            float wx = q.w * xx;  float wy = q.w * yy;  float wz = q.w * zz;
            float xx2 = q.x * xx; float xy2 = q.x * yy; float xz2 = q.x * zz;
            float yy2 = q.y * yy; float yz2 = q.y * zz; float zz2 = q.z * zz;

            return new Vector3(
                (1f - (yy2 + zz2)) * v.x + (xy2 - wz) * v.y + (xz2 + wy) * v.z,
                (xy2 + wz) * v.x + (1f - (xx2 + zz2)) * v.y + (yz2 - wx) * v.z,
                (xz2 - wy) * v.x + (yz2 + wx) * v.y + (1f - (xx2 + yy2)) * v.z);
        }
    }
}

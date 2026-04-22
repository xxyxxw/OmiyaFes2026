using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ZIG SIM のジャイロ値でスポットライト（照準）の向きを制御する。
    /// UdpQuaternionReceiver → このコンポーネント の順で Update される前提。
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))] // 同 GameObject に必ず UdpQuaternionReceiver が必要
    public class PoseRotationDriver : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("照準対象")]
        [Tooltip("向きを制御するスポットライト or 照準オブジェクト")]
        // このオブジェクトの Rotation をジャイロ値で書き換える
        [SerializeField] private Transform aimTarget;

        // 外部スクリプト（AimingController など）から参照できるよう公開
        public Transform AimTarget => aimTarget;

        [Header("感度")]
        [SerializeField] private float sensitivityH = 1.0f; // 横方向の感度倍率
        [SerializeField] private float sensitivityV = 1.0f; // 縦方向の感度倍率

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private UdpQuaternionReceiver _receiver;              // 同 GameObject の受信コンポーネント
        private Quaternion _calibrationOffset = Quaternion.identity; // キャリブレーション基準回転

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // RequireComponent で保証されているので null にはならない
            _receiver = GetComponent<UdpQuaternionReceiver>();
        }

        private void Update()
        {
            // 受信がなければ何もしない（スマホ未接続時等）
            if (!_receiver.IsReceiving) return;
            if (aimTarget == null) return;

            // ① iOS座標系のクォータニオンを Unity 座標系に変換
            Quaternion rawIos   = _receiver.LatestQuaternion;
            Quaternion unity    = QuaternionCoordinateConverter.IosToUnity(rawIos);

            // ② キャリブレーションオフセットを差し引いて「基準からの相対回転」を求める
            //    Quaternion.Inverse(offset) * rotation = offset を原点とした回転
            Quaternion calibrated = Quaternion.Inverse(_calibrationOffset) * unity;

            // ③ 感度を掛け合わせて照準オブジェクトに適用
            Vector3 euler = calibrated.eulerAngles;
            euler.x *= sensitivityV; // 縦（ピッチ）感度
            euler.y *= sensitivityH; // 横（ヨー）感度
            aimTarget.rotation = Quaternion.Euler(euler);
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// 現在のスマホの向きをキャリブレーション基準点として保存する。
        /// 呼び出した瞬間の向きが「正面」となる。
        /// PoseCalibrationCoordinator から C キーで呼ばれる。
        /// </summary>
        public void Calibrate()
        {
            if (!_receiver.IsReceiving)
            {
                Debug.LogWarning("[PoseRotationDriver] ZIG SIM 未受信中のためキャリブレーション不可");
                return;
            }

            // 現在の Unity 座標系での向きをオフセットとして保存
            _calibrationOffset = QuaternionCoordinateConverter.IosToUnity(_receiver.LatestQuaternion);
            Debug.Log("[PoseRotationDriver] キャリブレーション完了");
        }
    }
}

using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// ZIG SIM のジャイロ値でスマホの姿勢を Unity 内に反映する。
    ///
    /// ▼ 制御モードは2種類（Inspector で切替）
    ///
    ///   [MODE A] RotateGun  （デフォルト）
    ///     スマホを向けた方向に「銃オブジェクトの向き（Rotation）」を変える。
    ///     銃の transform.forward が照準方向になる。
    ///     → GunAimVisualizer と組み合わせてレイを可視化する。
    ///
    ///   [MODE B] MoveCrosshair
    ///     スマホの傾きを「画面上のカーソル位置（スクリーン座標）」に変換し、
    ///     カーソルオブジェクト（WorldSpace Canvas 上の UI など）を移動させる。
    ///     スマホを右に向けると銃が右に動く、上に向けると上に動く。
    ///
    /// ▼ キャリブレーション（C キー）
    ///   Calibrate() を呼ぶと、その瞬間の向きが「正面（中央）」として記録される。
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    [AddComponentMenu("OmiyaFes/Pose Rotation Driver")]
    public class PoseRotationDriver : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // 列挙型
        // ────────────────────────────────────────────────────────────

        public enum ControlMode
        {
            /// <summary>スマホの向きで銃の Rotation を直接制御する（姿勢反映）</summary>
            RotateGun,
            /// <summary>スマホの傾きを画面上の 2D 位置に変換して銃/カーソルを動かす</summary>
            MoveCrosshair,
        }

        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Header("制御モード")]
        [Tooltip("RotateGun: スマホ向き＝銃の向き / MoveCrosshair: スマホ傾き＝銃の位置")]
        [SerializeField] private ControlMode controlMode = ControlMode.MoveCrosshair;

        [Header("照準対象 Transform")]
        [Tooltip("向きまたは位置を制御するオブジェクト（銃、照準カーソルなど）")]
        [SerializeField] private Transform aimTarget;

        [Header("感度（MoveCrosshair モード用）")]
        [Tooltip("横方向の感度（スマホを左右に傾けたときの動く量）")]
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityH = 3.0f;

        [Tooltip("縦方向の感度（スマホを上下に傾けたときの動く量）")]
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityV = 3.0f;

        [Tooltip("スムージング量（0=即時、1=ほぼ動かない）。0.05〜0.2 が自然")]
        [SerializeField] [Range(0f, 0.95f)] private float smoothing = 0.1f;

        [Header("移動範囲（MoveCrosshair モード用）")]
        [Tooltip("画面端からのオフセット（度）。スマホをどこまで傾けたら端に到達するか")]
        [SerializeField] private float maxYawDeg  = 40f; // 左右の最大角度
        [SerializeField] private float maxPitchDeg = 30f; // 上下の最大角度

        [Tooltip("移動させる平面の距離（カメラから何m先でスクリーン座標→ワールド座標に変換するか）")]
        [SerializeField] private float screenDepth = 10f;

        [Header("デバッグ")]
        [SerializeField] private bool showDebugGizmos = true;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>外部スクリプト（InkGun など）から参照できる照準 Transform</summary>
        public Transform AimTarget => aimTarget;

        /// <summary>現在のキャリブレーション後の Yaw 角度（度, 左右 -180〜180）</summary>
        public float CurrentYawDeg { get; private set; }

        /// <summary>現在のキャリブレーション後の Pitch 角度（度, 上下 -180〜180）</summary>
        public float CurrentPitchDeg { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private UdpQuaternionReceiver _receiver;
        private Quaternion _calibrationOffset = Quaternion.identity;
        private Camera     _mainCamera;

        // MoveCrosshair 用スムージングバッファ
        private Vector3 _smoothedPosition;
        private bool    _positionInitialized;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _receiver   = GetComponent<UdpQuaternionReceiver>();
            _mainCamera = Camera.main;
        }

        private void Update()
        {
            if (!_receiver.IsReceiving) return;
            if (aimTarget == null) return;

            // ① iOS → Unity 座標系変換
            Quaternion rawIos   = _receiver.LatestQuaternion;
            Quaternion unityRot = QuaternionCoordinateConverter.IosToUnity(rawIos);

            // ② キャリブレーションオフセットを差し引いて「基準からの相対回転」
            Quaternion calibrated = Quaternion.Inverse(_calibrationOffset) * unityRot;

            switch (controlMode)
            {
                case ControlMode.RotateGun:
                    ApplyRotateGun(calibrated);
                    break;

                case ControlMode.MoveCrosshair:
                    ApplyMoveCrosshair(calibrated);
                    break;
            }
        }

        // ────────────────────────────────────────────────────────────
        // MODE A: 銃の向きを直接制御
        // ────────────────────────────────────────────────────────────

        private void ApplyRotateGun(Quaternion calibrated)
        {
            // スマホの姿勢をそのまま銃の Rotation に反映
            aimTarget.rotation = calibrated;
        }

        // ────────────────────────────────────────────────────────────
        // MODE B: 傾きを画面上の位置に変換して移動
        // ────────────────────────────────────────────────────────────

        private void ApplyMoveCrosshair(Quaternion calibrated)
        {
            // キャリブレーション済みクォータニオンから Euler 角を取り出す
            // Unity の Euler は (-180, 180] の範囲
            Vector3 euler = calibrated.eulerAngles;

            // Unity の eulerAngles は 0〜360 で返ってくるので -180〜180 に正規化
            float pitch = NormalizeAngle(euler.x); // 上下（ピッチ）
            float yaw   = NormalizeAngle(euler.y); // 左右（ヨー）

            CurrentPitchDeg = pitch;
            CurrentYawDeg   = yaw;

            // -1〜1 に正規化（clamp）
            float normH =  Mathf.Clamp(yaw   / maxYawDeg,   -1f, 1f);
            float normV = -Mathf.Clamp(pitch  / maxPitchDeg, -1f, 1f); // 上向きが+になるよう反転

            // カメラの視野からワールド座標へ変換
            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return;

            // スクリーン中央 + 正規化オフセット → ワールド座標
            float halfW = Screen.width  * 0.5f;
            float halfH = Screen.height * 0.5f;

            float screenX = halfW + normH * halfW * sensitivityH;
            float screenY = halfH + normV * halfH * sensitivityV;

            // スクリーン座標→ワールド座標（screenDepth m 先の平面）
            Vector3 screenPos = new Vector3(
                Mathf.Clamp(screenX, 0, Screen.width),
                Mathf.Clamp(screenY, 0, Screen.height),
                screenDepth
            );
            Vector3 targetWorldPos = cam.ScreenToWorldPoint(screenPos);

            // スムージング
            if (!_positionInitialized)
            {
                _smoothedPosition    = targetWorldPos;
                _positionInitialized = true;
            }
            else
            {
                _smoothedPosition = Vector3.Lerp(targetWorldPos, _smoothedPosition, smoothing);
            }

            // aimTarget の位置を更新
            aimTarget.position = _smoothedPosition;

            // 銃口はカメラ方向を向かせる（レイが前方に飛ぶように）
            Vector3 dir = _smoothedPosition - cam.transform.position;
            if (dir.sqrMagnitude > 0.001f)
                aimTarget.rotation = Quaternion.LookRotation(dir.normalized);
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// 現在のスマホの向きをキャリブレーション基準点として保存する。
        /// 呼び出した瞬間の向きが「正面（中央）」となる。
        /// PoseCalibrationCoordinator から C キーで呼ばれる。
        /// </summary>
        public void Calibrate()
        {
            if (!_receiver.IsReceiving)
            {
                Debug.LogWarning("[PoseRotationDriver] ZIG SIM 未受信中のためキャリブレーション不可");
                return;
            }

            _calibrationOffset   = QuaternionCoordinateConverter.IosToUnity(_receiver.LatestQuaternion);
            _positionInitialized = false; // スムージングもリセット
            Debug.Log("[PoseRotationDriver] ✅ キャリブレーション完了");
        }

        // ────────────────────────────────────────────────────────────
        // ユーティリティ
        // ────────────────────────────────────────────────────────────

        /// <summary>0〜360 の角度を -180〜180 に変換する</summary>
        private static float NormalizeAngle(float angle)
        {
            while (angle >  180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos || aimTarget == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawRay(aimTarget.position, aimTarget.forward * 5f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(aimTarget.position, 0.15f);
        }
    }
}

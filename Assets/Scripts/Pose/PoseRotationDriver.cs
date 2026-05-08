using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// スマホジャイロ（ZIG SIM）で照準を制御するドライバー。
    ///
    /// ▼ ARD互換モード（useArdCompatibleMode = true）
    ///   - 入力ローパスなし（inputLowPassAlpha = 0 相当）
    ///   - 出力スムージングなし（rotationSmoothing = 0 相当）
    ///   - 最初のパケット 1 フレームで即キャリブ完了（ARD と同じ）
    ///   - screenFaceDown = true と組み合わせて使うこと
    ///
    /// ▼ 精度改善ポイント（通常モード）
    ///   1. キャリブレーション平均化: calibAverageFrames フレーム分を Slerp 積算
    ///   2. 入力クォータニオンのローパスフィルタ: 高周波ジッター除去
    ///   3. 出力スムージング: aimTarget 適用前に一段 Slerp
    ///
    /// ▼ 左右が逆 → Inspector で invertLeftRight を ON
    /// ▼ 上下が逆 → Inspector で invertUpDown を ON
    /// </summary>
    [RequireComponent(typeof(UdpQuaternionReceiver))]
    [AddComponentMenu("OmiyaFes/Pose Rotation Driver")]
    public class PoseRotationDriver : MonoBehaviour
    {
        public enum ControlMode
        {
            /// <summary>スマホの向き = 銃の向き（1:1直接マッピング。推奨）</summary>
            DirectMapping,
            RotateGun,
            MoveCrosshair,
            RotateAndMove,
        }

        // ────────────────────────────────────────────────────────────
        // Inspector 設定
        // ────────────────────────────────────────────────────────────

        [Header("ARD互換モード（推奨: ON）")]
        [Tooltip("true にすると ARD と同じ挙動（ローパス無効・スムージング0・即時キャリブ）になる。\n" +
                 "UdpQuaternionReceiver の screenFaceDown = true と合わせて使うこと。")]
        [SerializeField] private bool useArdCompatibleMode = true;

        [Header("制御モード")]
        [Tooltip(
            "DirectMapping = スマホの向き=銃の向き 1:1直接（推奨）\n" +
            "RotateGun    = 回転のみ\n" +
            "MoveCrosshair = 位置のみ\n" +
            "RotateAndMove = 回転+位置")]
        [SerializeField] private ControlMode controlMode = ControlMode.DirectMapping;

        [Header("照準対象 Transform")]
        [Tooltip("スマホ姿勢で回転させる照準 Transform（AimRoot など）。null なら自動で this.transform を使うが Warning が出る。")]
        [SerializeField] private Transform aimTarget;

        [Header("キャリブレーション設定")]
        [Tooltip("最初のパケット受信時に自動キャリブレーションするか")]
        [SerializeField] private bool autoCalibrateOnFirstPacket = true;

        [Tooltip("キャリブレーション基準姿勢を何フレーム分で平均化するか（ARD互換モードでは無視・常に1）")]
        [SerializeField] [Range(1, 60)] private int calibAverageFrames = 20;

        [Header("入力ローパスフィルタ（通常モードのみ有効）")]
        [Tooltip("受信クォータニオンのSLERPローパス係数（0=フィルタなし、ARD互換モードでは無視）")]
        [SerializeField] [Range(0f, 0.99f)] private float inputLowPassAlpha = 0.15f;

        [Header("出力スムージング（通常モードのみ有効）")]
        [Tooltip("aimTargetへの適用時スムージング量（0=即時、ARD互換モードでは無視）")]
        [SerializeField] [Range(0f, 0.99f)] private float rotationSmoothing = 0f;

        [Header("モデル補正")]
        [SerializeField] private Vector3 modelEulerOffset = Vector3.zero;

        [Header("向き反転補正")]
        [Tooltip("左右が反転しているときにON")]
        [SerializeField] private bool invertLeftRight = false;
        [Tooltip("上下が反転しているときにON")]
        [SerializeField] private bool invertUpDown = false;

        [Header("感度（DirectMapping モード用）")]
        [Tooltip("スマホの傾き角度に掛ける倍率。1.0=1:1、2.0=2倍感度。スマホを少し動かしても大きく動かしたい場合は大きくする。")]
        [SerializeField] [Range(0.1f, 5f)] private float sensitivityScale = 1.0f;

        [Header("感度（MoveCrosshair モード用）")]
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityH = 3.0f;
        [SerializeField] [Range(0.1f, 10f)] private float sensitivityV = 3.0f;
        [SerializeField] [Range(0f, 0.99f)] private float smoothing    = 0.1f;

        [Header("移動範囲（MoveCrosshair モード用）")]
        [SerializeField] private float maxYawDeg   = 40f;
        [SerializeField] private float maxPitchDeg = 30f;
        [SerializeField] private float screenDepth = 10f;

        [Header("デバッグ")]
        [SerializeField] private bool showDebugGizmos = true;
        [Tooltip("ON にすると毎フレーム詳細ログを Console に出力する（重いので確認後は OFF に）")]
        [SerializeField] private bool verboseDebugLog = false;
        [Tooltip("詳細ログを何フレームおきに出力するか（verboseDebugLog=true のとき有効）")]
        [SerializeField] [Range(1, 300)] private int verboseLogInterval = 30;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        public Transform AimTarget => aimTarget;
        public float CurrentYawDeg   { get; private set; }
        public float CurrentPitchDeg { get; private set; }
        /// <summary>最後に適用した targetLocalRotation の Euler（デバッグ用）</summary>
        public Vector3 DebugRelativeEuler { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private UdpQuaternionReceiver _receiver;
        private Camera _mainCamera;

        // キャリブレーション
        private Quaternion _referenceSensorRotation = Quaternion.identity;
        private Quaternion _calibAccumulator         = Quaternion.identity;
        private int        _calibFrameCount          = 0;
        private bool       _hasCalibration           = false;
        private bool       _calibrating              = false;

        // ローパスフィルタ状態
        private Quaternion _filteredInput    = Quaternion.identity;
        private bool       _hasFilteredInput = false;

        // 出力
        private Quaternion _initialLocalRotation = Quaternion.identity;
        private Quaternion _targetLocalRotation  = Quaternion.identity;

        // MoveCrosshair 用
        private Vector3 _smoothedPosition;
        private bool    _positionInitialized;

        // デバッグ用
        private int _frameCount = 0;
        private bool _aimTargetWarningEmitted = false;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _receiver   = GetComponent<UdpQuaternionReceiver>();
            _mainCamera = Camera.main;

            // aimTarget 未設定の場合は this.transform にフォールバックし、Warning を出す
            if (aimTarget == null)
            {
                Debug.LogWarning(
                    "[PoseRotationDriver] ⚠ aimTarget が未設定です。this.transform にフォールバックします。\n" +
                    "Inspector で AimRoot などの専用 Transform を設定することを強く推奨します。", this);
                aimTarget = transform;
                _aimTargetWarningEmitted = true;
            }

            _initialLocalRotation = aimTarget.localRotation;
            _targetLocalRotation  = _initialLocalRotation;

            // 起動時の設定を出力
            Debug.Log(
                $"[PoseRotationDriver] 起動設定\n" +
                $"  useArdCompatibleMode   = {useArdCompatibleMode}\n" +
                $"  controlMode            = {controlMode}\n" +
                $"  autoCalibrateOnFirst   = {autoCalibrateOnFirstPacket}\n" +
                $"  calibAverageFrames     = {(useArdCompatibleMode ? 1 : calibAverageFrames)} " +
                    $"{(useArdCompatibleMode ? "(ARD互換モードで上書き)" : "")}\n" +
                $"  inputLowPassAlpha      = {(useArdCompatibleMode ? 0f : inputLowPassAlpha)} " +
                    $"{(useArdCompatibleMode ? "(ARD互換モードで無効化)" : "")}\n" +
                $"  rotationSmoothing      = {(useArdCompatibleMode ? 0f : rotationSmoothing)} " +
                    $"{(useArdCompatibleMode ? "(ARD互換モードで無効化)" : "")}\n" +
                $"  aimTarget              = {aimTarget.name}");
        }

        private void Update()
        {
            _frameCount++;

            // aimTarget null チェック（実行中に消えた場合）
            if (aimTarget == null)
            {
                if (!_aimTargetWarningEmitted)
                {
                    Debug.LogWarning("[PoseRotationDriver] ⚠ aimTarget が null になりました。", this);
                    _aimTargetWarningEmitted = true;
                }
                return;
            }

            if (_receiver == null) return;

            // ── キャリブレーションキー入力 ────────────────────────────
            // C キーで現在姿勢を基準にリキャリブ（Space はゲーム開始に使用済みのため除外）
            if (Input.GetKeyDown(KeyCode.C))
            {
                Debug.Log("[PoseRotationDriver] 🎯 手動キャリブレーション実行 (C キー)");
                ResetCalibration();
            }

            // ── ARD互換モード: 有効フィルタ係数の上書き ─────────────
            float effectiveLowPass  = useArdCompatibleMode ? 0f : inputLowPassAlpha;
            float effectiveSmooth   = useArdCompatibleMode ? 0f : rotationSmoothing;
            int   effectiveCalibFrames = useArdCompatibleMode ? 1 : calibAverageFrames;

            Quaternion nextRaw;
            if (!_receiver.ConsumeLatestRotation(out nextRaw))
            {
                // 新パケットなし → スムージングのみ
                ApplySmoothingToTarget(effectiveSmooth);
                return;
            }

            // ── ① 入力ローパスフィルタ ──────────────────────────────
            Quaternion filtered = ApplyInputLowPass(nextRaw, effectiveLowPass);

            // ── ② キャリブレーション平均化 ──────────────────────────
            if (autoCalibrateOnFirstPacket && !_hasCalibration)
            {
                AccumulateCalibration(filtered, effectiveCalibFrames);
                ApplySmoothingToTarget(effectiveSmooth);
                return;
            }

            // ── ③ 相対回転の計算 ────────────────────────────────────
            Quaternion relativeRotation = _hasCalibration
                ? QuaternionCalibrationUtility.CalculateRelativeRotation(_referenceSensorRotation, filtered)
                : filtered;

            // ── ④ 反転補正 ──────────────────────────────────────────
            if (invertLeftRight || invertUpDown)
            {
                Vector3 eu    = relativeRotation.eulerAngles;
                float   pitch = Mathf.DeltaAngle(0f, eu.x);
                float   yaw   = Mathf.DeltaAngle(0f, eu.y);
                float   roll  = Mathf.DeltaAngle(0f, eu.z);
                relativeRotation = Quaternion.Euler(
                    invertUpDown    ? -pitch : pitch,
                    invertLeftRight ? -yaw   : yaw,
                    roll);
            }

            // ── ⑤ 公開プロパティ更新 ────────────────────────────────
            Vector3 eu2       = relativeRotation.eulerAngles;
            CurrentYawDeg     = Mathf.DeltaAngle(0f, eu2.y);
            CurrentPitchDeg   = Mathf.DeltaAngle(0f, eu2.x);
            DebugRelativeEuler = new Vector3(
                Mathf.DeltaAngle(0f, eu2.x),
                Mathf.DeltaAngle(0f, eu2.y),
                Mathf.DeltaAngle(0f, eu2.z));

            // ── ⑥ モード別処理 ──────────────────────────────────────
            switch (controlMode)
            {
                case ControlMode.DirectMapping:
                    ApplyDirectMapping(relativeRotation);
                    break;
                case ControlMode.RotateGun:
                    ApplyRotateGun(relativeRotation, effectiveSmooth);
                    break;
                case ControlMode.MoveCrosshair:
                    ApplyMoveCrosshair(relativeRotation);
                    break;
                case ControlMode.RotateAndMove:
                    ApplyRotateAndMove(relativeRotation, effectiveSmooth);
                    break;
            }

            // ── ⑦ 詳細デバッグログ ──────────────────────────────────
            if (verboseDebugLog && (_frameCount % verboseLogInterval == 0))
            {
                EmitVerboseDebugLog(nextRaw, filtered, relativeRotation);
            }
        }

        // ────────────────────────────────────────────────────────────
        // ① 入力ローパスフィルタ
        // ────────────────────────────────────────────────────────────

        private Quaternion ApplyInputLowPass(Quaternion raw, float alpha)
        {
            if (alpha <= 0f || !_hasFilteredInput)
            {
                _filteredInput    = raw;
                _hasFilteredInput = true;
                return raw;
            }
            _filteredInput = Quaternion.Slerp(raw, _filteredInput, alpha);
            return _filteredInput;
        }

        // ────────────────────────────────────────────────────────────
        // ② キャリブレーション平均化
        // ────────────────────────────────────────────────────────────

        private void AccumulateCalibration(Quaternion q, int targetFrames)
        {
            if (!_calibrating)
            {
                _calibAccumulator = q;
                _calibFrameCount  = 1;
                _calibrating      = true;
                Debug.Log("[PoseRotationDriver] 📐 キャリブレーション開始（平均化中...）");
            }
            else
            {
                float t = 1f / (_calibFrameCount + 1);
                _calibAccumulator = Quaternion.Slerp(_calibAccumulator, q, t);
                _calibFrameCount++;
            }

            if (_calibFrameCount >= targetFrames)
            {
                _referenceSensorRotation = _calibAccumulator;
                _hasCalibration          = true;
                _calibrating             = false;
                string mode = targetFrames <= 1 ? "ARD互換・即時" : $"{targetFrames}フレーム平均";
                Debug.Log(
                    $"[PoseRotationDriver] ✅ キャリブレーション完了（{mode}）\n" +
                    $"  referenceSensorRotation = {_referenceSensorRotation.eulerAngles}");
            }
        }

        // ────────────────────────────────────────────────────────────
        // MODE DirectMapping: スマホの向き = 銃の向き（1:1直接）
        // ────────────────────────────────────────────────────────────
        // ・position は一切変えない（銃は固定位置）
        // ・rotation だけをスマホの姿勢に直接対応させる
        // ・スムージングなし → 遅延ゼロで最もダイレクトな操作感
        // ─────────────────────────────────────────────────────────────

        private void ApplyDirectMapping(Quaternion relativeRotation)
        {
            // ── 感度スケール適用 ────────────────────────────────────────
            // sensitivityScale = 1.0 のとき 1:1（そのまま）
            // sensitivityScale = 2.0 のときスマホを30度傾けると銃が60度動く
            Quaternion scaledRotation;
            if (Mathf.Approximately(sensitivityScale, 1f))
            {
                scaledRotation = relativeRotation;
            }
            else
            {
                // Euler角でYaw/Pitchに倍率を掛けてからQuaternionに戻す
                Vector3 eu    = relativeRotation.eulerAngles;
                float pitch   = Mathf.DeltaAngle(0f, eu.x) * sensitivityScale;
                float yaw     = Mathf.DeltaAngle(0f, eu.y) * sensitivityScale;
                float roll    = Mathf.DeltaAngle(0f, eu.z); // Rollは倍率なし
                scaledRotation = Quaternion.Euler(pitch, yaw, roll);
            }

            Quaternion modelOffsetRot = Quaternion.Euler(modelEulerOffset);
            // 初期姿勢 × スケール済み相対回転 × モデル補正 → localRotation に直接適用
            aimTarget.localRotation = _initialLocalRotation * scaledRotation * modelOffsetRot;
            // _targetLocalRotation も更新（ResetCalibration で参照される）
            _targetLocalRotation = aimTarget.localRotation;
        }

        // ────────────────────────────────────────────────────────────
        // MODE A: 銃の向きを直接制御（ARD 方式）
        // ────────────────────────────────────────────────────────────

        private void ApplyRotateGun(Quaternion relativeRotation, float smooth)
        {
            Quaternion modelOffsetRot  = Quaternion.Euler(modelEulerOffset);
            _targetLocalRotation = _initialLocalRotation * relativeRotation * modelOffsetRot;
            ApplySmoothingToTarget(smooth);
        }

        private void ApplySmoothingToTarget(float smooth)
        {
            if (aimTarget == null) return;
            if (smooth > 0f)
                aimTarget.localRotation = Quaternion.Slerp(
                    aimTarget.localRotation, _targetLocalRotation, 1f - smooth);
            else
                aimTarget.localRotation = _targetLocalRotation;
        }

        // ────────────────────────────────────────────────────────────────
        // MODE C: 回転 + 位置両方（推奨）
        // ────────────────────────────────────────────────────────────────

        private void ApplyRotateAndMove(Quaternion relativeRotation, float smooth)
        {
            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null)
            {
                ApplyRotateGun(relativeRotation, smooth);
                return;
            }

            // ── Euler角ではなく forward ベクトルの X/Y で偏差を計算 ──────────
            // Euler角(eulerAngles)は gimbal lock の影響で「右へ動かして戻す」と
            // 元の値に戻らないことがある。forward ベクトルを使えばこの問題が消える。
            //
            // relativeRotation = Inverse(基準) * 現在
            // → 基準姿勢のときは Identity → forward = (0, 0, 1)
            // → 右に傾けると forward.x > 0、戻せば forward.x = 0 に必ず戻る
            Vector3 fwd = relativeRotation * Vector3.forward;

            // Atan2 で角度を求める（sin近似より正確、大きい角でも安定）
            float yawRad   =  Mathf.Atan2(fwd.x, fwd.z);   // 右 = 正
            float pitchRad =  Mathf.Atan2(fwd.y, fwd.z);   // 上 = 正（マイナス除去）

            // maxYawDeg / maxPitchDeg を基準に -1〜1 に正規化
            float normH = Mathf.Clamp(yawRad   / (maxYawDeg   * Mathf.Deg2Rad), -1f, 1f);
            float normV = Mathf.Clamp(pitchRad / (maxPitchDeg * Mathf.Deg2Rad), -1f, 1f);

            // ── クロスヘア位置: cam.right/up のみ使用 → Z方向移動なし ────────
            float fovRad     = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHeight = screenDepth * Mathf.Tan(fovRad);
            float halfWidth  = halfHeight * cam.aspect;

            Vector3 centerPoint = cam.transform.position
                                + cam.transform.forward * screenDepth;

            Vector3 targetWorldPos = centerPoint
                + cam.transform.right * (normH * halfWidth  * sensitivityH)
                + cam.transform.up    * (normV * halfHeight * sensitivityV);

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

            // 照準オブジェクトの位置を更新（上下左右のみ、前後なし）
            aimTarget.position = _smoothedPosition;

            // 照準の向きをカメラ→クロスヘア方向に合わせる
            Vector3 shootDir = _smoothedPosition - cam.transform.position;
            if (shootDir.sqrMagnitude > 0.001f)
                aimTarget.rotation = Quaternion.LookRotation(shootDir.normalized);
        }

        // ────────────────────────────────────────────────────────────
        // MODE B: 傾きを画面上の位置に変換して移動
        // ────────────────────────────────────────────────────────────

        private void ApplyMoveCrosshair(Quaternion relativeRotation)
        {
            float pitch = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.x);
            float yaw   = Mathf.DeltaAngle(0f, relativeRotation.eulerAngles.y);

            float normH =  Mathf.Clamp(yaw   / maxYawDeg,   -1f, 1f);
            float normV = -Mathf.Clamp(pitch  / maxPitchDeg, -1f, 1f);

            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return;

            float halfW = Screen.width  * 0.5f;
            float halfH = Screen.height * 0.5f;

            float screenX = halfW + normH * halfW * sensitivityH;
            float screenY = halfH + normV * halfH * sensitivityV;

            Vector3 screenPos = new Vector3(
                Mathf.Clamp(screenX, 0f, Screen.width),
                Mathf.Clamp(screenY, 0f, Screen.height),
                screenDepth);
            Vector3 targetWorldPos = cam.ScreenToWorldPoint(screenPos);

            if (!_positionInitialized)
            {
                _smoothedPosition    = targetWorldPos;
                _positionInitialized = true;
            }
            else
            {
                _smoothedPosition = Vector3.Lerp(targetWorldPos, _smoothedPosition, smoothing);
            }

            aimTarget.position = _smoothedPosition;
            Vector3 dir = _smoothedPosition - cam.transform.position;
            if (dir.sqrMagnitude > 0.001f)
                aimTarget.rotation = Quaternion.LookRotation(dir.normalized);
        }

        // ────────────────────────────────────────────────────────────
        // 詳細デバッグログ
        // ────────────────────────────────────────────────────────────

        private void EmitVerboseDebugLog(Quaternion rawQuat, Quaternion convertedQuat, Quaternion relRotation)
        {
            int pktCount = _receiver != null ? _receiver.ReceivedPacketCount : 0;
            Vector3 relEuler = DebugRelativeEuler;
            Vector3 fwd = aimTarget != null ? aimTarget.forward : Vector3.forward;
            Quaternion localRot = aimTarget != null ? aimTarget.localRotation : Quaternion.identity;

            Debug.Log(
                $"[PoseRotationDriver] 🔍 詳細デバッグ frame={_frameCount}\n" +
                $"  ReceivedPacketCount    = {pktCount}\n" +
                $"  screenFaceDown         = {(_receiver != null ? _receiver.ScreenFaceDown.ToString() : "N/A")}\n" +
                $"  useArdCompatibleMode   = {useArdCompatibleMode}\n" +
                $"  rawQuat                = ({rawQuat.x:F3}, {rawQuat.y:F3}, {rawQuat.z:F3}, {rawQuat.w:F3})\n" +
                $"  convertedQuat          = ({convertedQuat.x:F3}, {convertedQuat.y:F3}, {convertedQuat.z:F3}, {convertedQuat.w:F3})\n" +
                $"  hasCalibration         = {_hasCalibration}\n" +
                $"  referenceSensorRot     = {_referenceSensorRotation.eulerAngles}\n" +
                $"  relativeRotation Euler = pitch={relEuler.x:F1}° yaw={relEuler.y:F1}° roll={relEuler.z:F1}°\n" +
                $"  aimTarget.localRot     = {localRot.eulerAngles}\n" +
                $"  aimTarget.forward      = {fwd:F3}");
        }

        // ────────────────────────────────────────────────────────────
        // キャリブレーション
        // ────────────────────────────────────────────────────────────

        public void ResetCalibration()
        {
            _hasCalibration      = false;
            _calibrating         = false;
            _calibFrameCount     = 0;
            _hasFilteredInput    = false;
            _positionInitialized = false;
            _targetLocalRotation = _initialLocalRotation;

            if (aimTarget != null)
                aimTarget.localRotation = _initialLocalRotation;

            if (_receiver != null)
                _receiver.ClearPendingRotation();

            Debug.Log("[PoseRotationDriver] 🔄 キャリブレーションリセット → 次のパケットから基準姿勢を設定");
        }

        public void Calibrate() => ResetCalibration();

        /// <summary>
        /// 実行時に外部から aimTarget を設定する（AimRootSetup などから使用）。
        /// </summary>
        public void SetAimTarget(Transform target)
        {
            aimTarget = target;
            _initialLocalRotation = aimTarget != null ? aimTarget.localRotation : Quaternion.identity;
            _targetLocalRotation  = _initialLocalRotation;
            _aimTargetWarningEmitted = false;
            Debug.Log($"[PoseRotationDriver] aimTarget を {(target != null ? target.name : "null")} に設定しました。");
        }

        // ────────────────────────────────────────────────────────────
        // Gizmos
        // ────────────────────────────────────────────────────────────

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
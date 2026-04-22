using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 照準方向が画面正面に対して一定角度内かを判定する。
    /// AimingController = PoseRotationDriver のターゲット Transform を見て判断。
    /// </summary>
    public class AimingController : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("照準 Transform（PoseRotationDriver のターゲット）")]
        // スマホのジャイロで動く照準オブジェクトのTransform
        [SerializeField] private Transform aimTransform;

        [Header("スクリーン正面（通常はメインカメラの向き）")]
        // 「画面の正面方向」を表す Transform（通常はメインカメラを設定）
        [SerializeField] private Transform screenNormalTransform;

        [Header("有効角度（度）")]
        // 照準がここ以内の角度に入っていれば「画面を向いている」と判定する
        [SerializeField] private float maxAngle = 45f;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>照準が画面を向いているか（InkGun がこれを参照して発射判定する）</summary>
        public bool IsAimingAtScreen { get; private set; } = true;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            // どちらかの Transform が未設定の場合は「向いている」として扱う（テスト用フォールバック）
            if (aimTransform == null || screenNormalTransform == null)
            {
                IsAimingAtScreen = true;
                return;
            }

            // 照準方向とスクリーン正面方向のなす角度を計算
            // Vector3.Angle は 0〜180 の値を返す
            float angle = Vector3.Angle(aimTransform.forward, screenNormalTransform.forward);

            // maxAngle 以内なら「スクリーンを向いている」と判定
            IsAimingAtScreen = angle <= maxAngle;
        }
    }
}

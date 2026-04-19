using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 照準方向が画面正面に対して一定角度内かを判定する。
    /// AimingController = PoseRotationDriver のターゲット Transform を見て判断。
    /// </summary>
    public class AimingController : MonoBehaviour
    {
        [Header("照準 Transform（PoseRotationDriver のターゲット）")]
        [SerializeField] private Transform aimTransform;

        [Header("スクリーン正面（通常はメインカメラの向き）")]
        [SerializeField] private Transform screenNormalTransform;

        [Header("有効角度（度）")]
        [SerializeField] private float maxAngle = 45f;

        /// <summary>照準が画面を向いているか</summary>
        public bool IsAimingAtScreen { get; private set; } = true;

        private void Update()
        {
            if (aimTransform == null || screenNormalTransform == null)
            {
                IsAimingAtScreen = true;
                return;
            }

            float angle = Vector3.Angle(aimTransform.forward, screenNormalTransform.forward);
            IsAimingAtScreen = angle <= maxAngle;
        }
    }
}

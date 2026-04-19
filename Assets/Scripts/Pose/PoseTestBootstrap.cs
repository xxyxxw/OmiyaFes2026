using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// Play Mode 開始時に Pose システムに必要な GameObject を自動生成・接続する。
    /// 開発初期のテスト用。本番シーンでは手動で組んでこのスクリプトを外しても良い。
    /// </summary>
    public class PoseTestBootstrap : MonoBehaviour
    {
        [Header("テスト用ターゲット（未設定時は自動生成）")]
        [SerializeField] private Transform aimTarget;

        private void Awake()
        {
            if (aimTarget == null)
            {
                // テスト用ライトを自動生成
                GameObject lightGo = new GameObject("AimSpotlight_Auto");
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.intensity = 2f;
                l.spotAngle = 30f;
                lightGo.transform.position = new Vector3(0, 5, 0);
                lightGo.transform.rotation = Quaternion.Euler(90, 0, 0);
                aimTarget = lightGo.transform;
                Debug.Log("[PoseTestBootstrap] AimSpotlight_Auto を自動生成しました");
            }

            // 同 GameObject に必要コンポーネントを追加（なければ）
            var receiver = GetOrAdd<UdpQuaternionReceiver>(gameObject);
            var driver   = GetOrAdd<PoseRotationDriver>(gameObject);
            GetOrAdd<PoseCalibrationCoordinator>(gameObject);
            GetOrAdd<PoseDebugOverlay>(gameObject);

            // リフレクション不要で直接設定できるフィールドはインスペクタで設定してください。
            // aimTarget は PoseRotationDriver の SerializeField なので
            // テスト用途では手動で Inspector から設定してください。
            Debug.Log("[PoseTestBootstrap] Pose コンポーネントのセットアップ完了");
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.GetComponent<T>() ?? go.AddComponent<T>();
        }
    }
}

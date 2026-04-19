using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// デバッグオーバーレイを画面に表示する（Dキーで表示切替）。
    /// ZIG SIM からの受信状態・現在の照準クォータニオンを可視化。
    /// </summary>
    public class PoseDebugOverlay : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.D;
        [SerializeField] private UdpQuaternionReceiver receiver;
        [SerializeField] private PoseRotationDriver driver;

        private bool _visible = false;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 200), GUI.skin.box);
            GUILayout.Label("=== Pose Debug Overlay ===");

            if (receiver != null)
            {
                GUILayout.Label($"IsReceiving : {receiver.IsReceiving}");
                Quaternion q = receiver.LatestQuaternion;
                GUILayout.Label($"Raw (iOS)   : ({q.x:F3}, {q.y:F3}, {q.z:F3}, {q.w:F3})");
            }
            else
            {
                GUILayout.Label("receiver が未設定");
            }

            if (driver != null && driver.AimTarget != null)
            {
                Vector3 e = driver.AimTarget.eulerAngles;
                GUILayout.Label($"AimEuler    : ({e.x:F1}, {e.y:F1}, {e.z:F1})");
            }

            GUILayout.Label("[D] オーバーレイOFF");
            GUILayout.EndArea();
        }
    }
}

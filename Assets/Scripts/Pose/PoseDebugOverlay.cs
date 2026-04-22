using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// デバッグオーバーレイを画面に表示する（Dキーで表示切替）。
    /// ZIG SIM からの受信状態・現在の照準クォータニオンを可視化。
    /// </summary>
    public class PoseDebugOverlay : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        // オーバーレイの表示/非表示をトグルするキー（デフォルト: D）
        [SerializeField] private KeyCode toggleKey = KeyCode.D;

        // デバッグ情報の参照元コンポーネント
        [SerializeField] private UdpQuaternionReceiver receiver; // 受信状態を確認
        [SerializeField] private PoseRotationDriver driver;      // 照準の向きを確認

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private bool _visible = false; // オーバーレイ表示中かどうか

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Update()
        {
            // toggleKey が押されるたびに表示/非表示を切り替える
            if (Input.GetKeyDown(toggleKey))
                _visible = !_visible;
        }

        // OnGUI はフレームごとに GUI を描画するための Unity の特殊メソッド
        private void OnGUI()
        {
            if (!_visible) return; // 非表示なら何も描画しない

            // 左上に 320x200 の GUI ボックスを描画
            GUILayout.BeginArea(new Rect(10, 10, 320, 200), GUI.skin.box);
            GUILayout.Label("=== Pose Debug Overlay ===");

            if (receiver != null)
            {
                // ZIG SIM からの受信状態（true/false）を表示
                GUILayout.Label($"IsReceiving : {receiver.IsReceiving}");

                // 受信している iOS 座標系のクォータニオン生値を表示
                Quaternion q = receiver.LatestQuaternion;
                GUILayout.Label($"Raw (iOS)   : ({q.x:F3}, {q.y:F3}, {q.z:F3}, {q.w:F3})");
            }
            else
            {
                GUILayout.Label("receiver が未設定");
            }

            if (driver != null && driver.AimTarget != null)
            {
                // 照準オブジェクトの現在のオイラー角を表示（Unity 座標系・感度適用後）
                Vector3 e = driver.AimTarget.eulerAngles;
                GUILayout.Label($"AimEuler    : ({e.x:F1}, {e.y:F1}, {e.z:F1})");
            }

            GUILayout.Label("[D] オーバーレイOFF");
            GUILayout.EndArea();
        }
    }
}

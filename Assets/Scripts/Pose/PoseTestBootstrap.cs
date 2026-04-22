using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// Play Mode 開始時に Pose システムに必要な GameObject を自動生成・接続する。
    /// 開発初期のテスト用。本番シーンでは手動で組んでこのスクリプトを外しても良い。
    /// </summary>
    public class PoseTestBootstrap : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("テスト用ターゲット（未設定時は自動生成）")]
        // 照準させるオブジェクトの Transform。
        // 未設定なら Awake 内でスポットライトが自動生成される。
        [SerializeField] private Transform aimTarget;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // ── 照準ターゲットの自動生成 ──────────────────────────
            if (aimTarget == null)
            {
                // Inspector で照準オブジェクトが指定されていない場合にスポットライトを自動生成
                GameObject lightGo = new GameObject("AimSpotlight_Auto");
                Light l = lightGo.AddComponent<Light>();
                l.type      = LightType.Spot;
                l.intensity = 2f;
                l.spotAngle = 30f;
                lightGo.transform.position = new Vector3(0, 5, 0);
                lightGo.transform.rotation = Quaternion.Euler(90, 0, 0); // 真下向き
                aimTarget = lightGo.transform;
                Debug.Log("[PoseTestBootstrap] AimSpotlight_Auto を自動生成しました");
            }

            // ── Pose に必要なコンポーネントを同 GameObject に追加 ──
            // GetOrAdd<T> は既にあれば取得、なければ AddComponent する
            var receiver = GetOrAdd<UdpQuaternionReceiver>(gameObject);
            var driver   = GetOrAdd<PoseRotationDriver>(gameObject);
            GetOrAdd<PoseCalibrationCoordinator>(gameObject);
            GetOrAdd<PoseDebugOverlay>(gameObject);

            // ⚠️ aimTarget は PoseRotationDriver の SerializeField のため
            // テスト用途では手動で Inspector から設定してください。
            // リフレクションを使えば自動設定も可能だが、可読性のため省略。
            Debug.Log("[PoseTestBootstrap] Pose コンポーネントのセットアップ完了");
        }

        // ────────────────────────────────────────────────────────────
        // ユーティリティ
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// コンポーネントを取得する。存在しなければ追加して返す。
        /// ?? 演算子の null 合体と同じ考え方。
        /// </summary>
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.GetComponent<T>() ?? go.AddComponent<T>();
        }
    }
}

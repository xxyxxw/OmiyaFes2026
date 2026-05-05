using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// 銃口から飛ぶ「照準レイ」を LineRenderer で可視化する。
    /// PoseRotationDriver と同じ GameObject か子 GameObject に追加して使う。
    ///
    /// ▼ 仕組み
    ///   ・LineRenderer で銃口→ヒット点（または最大距離）の線を描画
    ///   ・ヒット時はヒット点にスフィアマーカーを表示
    ///   ・Debug.DrawRay は SceneView 専用で GameView では見えないため LineRenderer を使用
    /// </summary>
    [AddComponentMenu("OmiyaFes/Gun Aim Visualizer")]
    public class GunAimVisualizer : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Header("照準元（銃口）")]
        [Tooltip("レイの始点。未設定なら自身の Transform を使う")]
        [SerializeField] private Transform gunMuzzle;

        [Header("レイ設定")]
        [Tooltip("レイの最大距離（m）")]
        [SerializeField] private float rayLength = 30f;

        [Tooltip("レイの色")]
        [SerializeField] private Color rayColor = new Color(1f, 0.2f, 0.2f, 0.8f);

        [Tooltip("レイの太さ（m）")]
        [SerializeField] [Range(0.001f, 0.1f)] private float rayWidth = 0.02f;

        [Header("ヒットマーカー")]
        [Tooltip("ヒット点に表示するマーカーの大きさ（m）")]
        [SerializeField] private float hitMarkerSize = 0.3f;

        [Tooltip("ヒットマーカーの色")]
        [SerializeField] private Color hitColor = Color.yellow;

        [Header("レイヤーマスク")]
        [Tooltip("レイキャストで当たり判定するレイヤー。All で全て")]
        [SerializeField] private LayerMask raycastLayers = ~0; // All

        // ────────────────────────────────────────────────────────────
        // 内部状態
        // ────────────────────────────────────────────────────────────

        private LineRenderer _lineRenderer;
        private GameObject   _hitMarker;
        private Transform    _muzzle;        // 実際に使う銃口 Transform

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _muzzle = gunMuzzle != null ? gunMuzzle : transform;

            // ── LineRenderer のセットアップ ──────────────────────────
            _lineRenderer = gameObject.GetComponent<LineRenderer>();
            if (_lineRenderer == null)
                _lineRenderer = gameObject.AddComponent<LineRenderer>();

            _lineRenderer.positionCount  = 2;
            _lineRenderer.startWidth     = rayWidth;
            _lineRenderer.endWidth       = rayWidth * 0.3f; // 先細り
            _lineRenderer.useWorldSpace  = true;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;

            // マテリアル（Unlit/Color でシンプルに）
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = rayColor;
            _lineRenderer.material = mat;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(rayColor, 0f), new GradientColorKey(rayColor, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f),    new GradientAlphaKey(0.0f, 1f) }
            );
            _lineRenderer.colorGradient = gradient;

            // ── ヒットマーカー（スフィア）のセットアップ ──────────────
            _hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _hitMarker.name = "AimHitMarker";
            _hitMarker.transform.localScale = Vector3.one * hitMarkerSize;
            _hitMarker.SetActive(false);

            // コライダー削除（判定に干渉させない）
            var col = _hitMarker.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // マテリアル設定
            var hitMat = new Material(Shader.Find("Sprites/Default"));
            hitMat.color = hitColor;
            _hitMarker.GetComponent<Renderer>().material = hitMat;
        }

        private void Update()
        {
            Vector3 origin    = _muzzle.position;
            Vector3 direction = _muzzle.forward;

            // ── レイキャスト ─────────────────────────────────────────
            Vector3 endPoint;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, rayLength, raycastLayers))
            {
                endPoint = hit.point;
                _hitMarker.transform.position = hit.point;
                _hitMarker.SetActive(true);
            }
            else
            {
                endPoint = origin + direction * rayLength;
                _hitMarker.SetActive(false);
            }

            // ── LineRenderer 更新 ─────────────────────────────────────
            _lineRenderer.SetPosition(0, origin);
            _lineRenderer.SetPosition(1, endPoint);

            // ── SceneView にも念のため描画（エディタ確認用）─────────────
            Debug.DrawRay(origin, direction * rayLength, rayColor);
        }

        private void OnDestroy()
        {
            if (_hitMarker != null) Destroy(_hitMarker);
        }

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>現在のレイが当たった点（当たっていなければ最大距離の点）</summary>
        public Vector3 AimWorldPoint { get; private set; }
    }
}

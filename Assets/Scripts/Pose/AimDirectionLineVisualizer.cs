using UnityEngine;

namespace OmiyaFes2026.Pose
{
    /// <summary>
    /// aimTransform.forward の向きを Game 画面上に LineRenderer で可視化する。
    /// スマホ操作で照準がどちらを向いているか確認するためのデバッグ用。
    /// </summary>
    [AddComponentMenu("OmiyaFes/Pose/Aim Direction Line Visualizer")]
    public class AimDirectionLineVisualizer : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("向きを見る Transform。PoseRotationDriver / InkGun の aimTransform と同じものを指定する")]
        [SerializeField] private Transform directionTransform;

        [Tooltip("線の開始位置。未設定なら directionTransform の位置を使う。銃口があるなら muzzlePoint を指定")]
        [SerializeField] private Transform originTransform;

        [Header("表示設定")]
        [Tooltip("線の長さ")]
        [SerializeField] private float lineLength = 10f;

        [Tooltip("線の太さ")]
        [SerializeField] private float lineWidth = 0.04f;

        [Tooltip("線の色")]
        [SerializeField] private Color lineColor = Color.cyan;

        [Tooltip("実行開始時に LineRenderer を自動生成する")]
        [SerializeField] private bool createLineRendererIfMissing = true;

        private LineRenderer lineRenderer;
        private Material runtimeMaterial;

        private void Awake()
        {
            // directionTransform 未設定なら自分の Transform にフォールバック
            if (directionTransform == null)
            {
                directionTransform = transform;
            }

            lineRenderer = GetComponent<LineRenderer>();

            if (lineRenderer == null && createLineRendererIfMissing)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            SetupLineRenderer();
        }

        private void OnValidate()
        {
            if (lineLength < 0f)
            {
                lineLength = 0f;
            }

            if (lineWidth < 0.001f)
            {
                lineWidth = 0.001f;
            }

            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            SetupLineRenderer();
        }

        private void Update()
        {
            if (lineRenderer == null || directionTransform == null)
            {
                return;
            }

            Vector3 startPosition = originTransform != null
                ? originTransform.position
                : directionTransform.position;

            Vector3 endPosition = startPosition + directionTransform.forward * lineLength;

            lineRenderer.SetPosition(0, startPosition);
            lineRenderer.SetPosition(1, endPosition);
        }

        private void SetupLineRenderer()
        {
            if (lineRenderer == null)
            {
                return;
            }

            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor = lineColor;

            if (lineRenderer.sharedMaterial == null)
            {
                Shader shader =
                    Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("Sprites/Default") ??
                    Shader.Find("Unlit/Color");

                if (shader != null)
                {
                    runtimeMaterial = new Material(shader);
                    runtimeMaterial.name = "Aim Direction Line Material";
                    lineRenderer.sharedMaterial = runtimeMaterial;
                }
            }

            if (lineRenderer.sharedMaterial != null)
            {
                if (lineRenderer.sharedMaterial.HasProperty("_BaseColor"))
                {
                    lineRenderer.sharedMaterial.SetColor("_BaseColor", lineColor);
                }

                if (lineRenderer.sharedMaterial.HasProperty("_Color"))
                {
                    lineRenderer.sharedMaterial.SetColor("_Color", lineColor);
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        // 実行時セッター（AimRootSetup などから使用）
        // ────────────────────────────────────────────────────────────

        /// <summary>実行時に外部から directionTransform を変更する。</summary>
        public void SetDirectionTransform(Transform t)
        {
            directionTransform = t;
            Debug.Log($"[AimDirectionLineVisualizer] directionTransform = {(t != null ? t.name : "null")}");
        }
    }
}
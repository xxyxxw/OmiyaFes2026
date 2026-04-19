using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// インク銃の主制御。
    /// ・照準方向が画面内なら 0.5 秒間隔で自動発射
    /// ・Raycast でペイントターゲットに命中 → PaintTextureManager で描画
    /// ・命中時に InkSplash.prefab を生成
    /// </summary>
    public class InkGun : MonoBehaviour
    {
        [Header("銃口オブジェクト")]
        [SerializeField] private Transform muzzlePoint;

        [Header("照準オブジェクト（PoseRotationDriver のターゲット）")]
        [SerializeField] private Transform aimTransform;

        [Header("発射設定")]
        [SerializeField] private float fireInterval = 0.5f;
        [SerializeField] private GameObject inkProjectilePrefab;  // ⚠️ Blender モデル必要
        [SerializeField] private GameObject inkSplashPrefab;      // ⚠️ Blender モデル必要

        [Header("照準内外判定（度）")]
        [SerializeField] private float maxAimAngle = 45f;

        [Header("Raycast")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private LayerMask paintTargetLayer;

        [Header("ブラシ")]
        [Tooltip("円形アルファテクスチャ。未設定時は単純な円を描画する")]
        [SerializeField] private Texture2D brushTexture;
        [SerializeField] private float brushRadius = 0.05f; // UV 空間上の半径

        private PaintTextureManager _paintManager;
        private InkColorCycler _colorCycler;
        private float _fireTimer = 0f;

        private void Awake()
        {
            _paintManager  = FindObjectOfType<PaintTextureManager>();
            _colorCycler   = FindObjectOfType<InkColorCycler>();

            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        private void Update()
        {
            // ゲームプレイ中のみ発射
            if (GameStateManager.Instance == null) return;
            if (GameStateManager.Instance.CurrentState != GameStateManager.GameState.Playing) return;

            // 照準が画面内かチェック
            if (!IsAimingAtScreen()) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer >= fireInterval)
            {
                _fireTimer = 0f;
                Fire();
            }
        }

        private bool IsAimingAtScreen()
        {
            if (aimTransform == null) return true; // テスト時は常に有効

            // 照準方向と画面正面方向のなす角を計算
            Vector3 aimDir = aimTransform.forward;
            Vector3 screenNormal = mainCamera != null ? mainCamera.transform.forward : Vector3.forward;
            float angle = Vector3.Angle(aimDir, screenNormal);
            return angle <= maxAimAngle;
        }

        private void Fire()
        {
            // ── Raycast ──────────────────────────────
            Ray ray = new Ray(
                muzzlePoint != null ? muzzlePoint.position : transform.position,
                aimTransform != null ? aimTransform.forward : transform.forward
            );

            if (Physics.Raycast(ray, out RaycastHit hit, 100f, paintTargetLayer))
            {
                // ペイント処理
                PaintAt(hit);

                // エフェクト生成
                SpawnSplash(hit.point, hit.normal);
            }

            // 飛翔弾の視覚演出
            SpawnProjectile(ray.origin, ray.direction);
        }

        // ─────────────────────────────────────────
        // ペイント
        // ─────────────────────────────────────────

        private void PaintAt(RaycastHit hit)
        {
            if (_paintManager == null) return;

            // ─── ⚠️ 問題ポイント ───
            // hit.textureCoord は MeshCollider(convex=false) かつ UV 展開済みの
            // メッシュでないと (0,0) になる。Blender モデル未着時は Sphere 等で代替テスト。
            Vector2 uv = hit.textureCoord;
            DrawBrush(_paintManager.PaintTexture, uv, _colorCycler?.CurrentColor ?? Color.white);
        }

        private void DrawBrush(RenderTexture target, Vector2 uv, Color color)
        {
            // GPU ブラシスタンプ（Graphics.Blit で UV 位置に円を描く）
            // 専用シェーダー InkPaintShader が必要。ない場合は簡易 CPU 描画にフォールバック。
            // TODO: InkPaintShader.shader 完成後にここを差し替える
            SimpleCpuBrush(target, uv, color);
        }

        /// <summary>シェーダー未完成時の CPU フォールバック描画（重い・精度低い）</summary>
        private void SimpleCpuBrush(RenderTexture rt, Vector2 uv, Color color)
        {
            Texture2D tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;

            int cx = Mathf.RoundToInt(uv.x * rt.width);
            int cy = Mathf.RoundToInt(uv.y * rt.height);
            int r  = Mathf.RoundToInt(brushRadius * rt.width);

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > r) continue;
                    float alpha = Mathf.Lerp(1f, 0f, dist / r);
                    Color c = new Color(color.r, color.g, color.b, alpha);
                    tmp.SetPixel(cx + dx, cy + dy, c);
                }
            }

            tmp.Apply();
            Graphics.Blit(tmp, rt);
            Destroy(tmp);
        }

        // ─────────────────────────────────────────
        // エフェクト生成
        // ─────────────────────────────────────────

        private void SpawnSplash(Vector3 position, Vector3 normal)
        {
            if (inkSplashPrefab == null)
            {
                // ⚠️ Blender モデル未完成時はスキップ（警告のみ）
                return;
            }
            GameObject splash = Instantiate(inkSplashPrefab, position,
                                            Quaternion.LookRotation(normal));
            Destroy(splash, 1.5f);
        }

        private void SpawnProjectile(Vector3 origin, Vector3 direction)
        {
            if (inkProjectilePrefab == null)
            {
                // ⚠️ Blender モデル未完成時はスキップ（警告のみ）
                return;
            }
            GameObject proj = Instantiate(inkProjectilePrefab, origin,
                                          Quaternion.LookRotation(direction));
            var bullet = proj.AddComponent<InkBullet>();
            bullet.Initialize(direction);
        }
    }
}

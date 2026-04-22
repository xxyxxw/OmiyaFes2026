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
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("銃口オブジェクト")]
        // 弾が飛び出す「銃口」位置の Transform（Ray の始点になる）
        [SerializeField] private Transform muzzlePoint;

        [Header("照準オブジェクト（PoseRotationDriver のターゲット）")]
        // スマホのジャイロで回転する照準オブジェクト（発射方向の基準）
        [SerializeField] private Transform aimTransform;

        [Header("発射設定")]
        // 何秒おきに1発発射するか
        [SerializeField] private float fireInterval = 0.5f;
        [SerializeField] private GameObject inkProjectilePrefab;  // ⚠️ 飛翔弾プレファブ（Blender モデル必要）
        [SerializeField] private GameObject inkSplashPrefab;      // ⚠️ 着弾エフェクトプレファブ（Blender モデル必要）

        [Header("照準内外判定（度）")]
        // 照準がこの角度以内にスクリーンを向いているときだけ発射する
        [SerializeField] private float maxAimAngle = 45f;

        [Header("Raycast")]
        // ペイント用の Ray を飛ばすカメラ
        [SerializeField] private Camera mainCamera;
        // ペイント対象のレイヤー（LayerMask で特定のオブジェクトだけ Hit させる）
        [SerializeField] private LayerMask paintTargetLayer;

        [Header("ブラシ")]
        [Tooltip("円形アルファテクスチャ。未設定時は単純な円を描画する")]
        // ブラシの形状を決めるアルファテクスチャ（円形グラデーション等）
        [SerializeField] private Texture2D brushTexture;
        // UV 空間上でのブラシ半径（0〜1 の範囲。小さいほど細い）
        [SerializeField] private float brushRadius = 0.05f;

        // ────────────────────────────────────────────────────────────
        // 内部参照・状態
        // ────────────────────────────────────────────────────────────

        private PaintTextureManager _paintManager; // テクスチャへの描画を担当するコンポーネント
        private InkColorCycler _colorCycler;       // 現在のインク色を提供するコンポーネント
        private float _fireTimer = 0f;             // 前回の発射からの経過時間

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // シーン内から必要なコンポーネントを自動検索して取得
            _paintManager  = FindObjectOfType<PaintTextureManager>();
            _colorCycler   = FindObjectOfType<InkColorCycler>();

            // mainCamera が未設定ならメインカメラを自動取得
            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        private void Update()
        {
            // ── 発射条件チェック ──────────────────────────────────

            // GameStateManager のシングルトンがなければ何もしない
            if (GameStateManager.Instance == null) return;

            // Playing 状態でなければ発射しない（Waiting・Ending 中は撃てない）
            if (GameStateManager.Instance.CurrentState != GameStateManager.GameState.Playing) return;

            // 照準がスクリーンの方向を向いていなければ発射しない
            if (!IsAimingAtScreen()) return;

            // ── 発射タイマー ──────────────────────────────────────
            _fireTimer += Time.deltaTime;
            if (_fireTimer >= fireInterval)
            {
                _fireTimer = 0f; // タイマーをリセット
                Fire();          // 発射！
            }
        }

        // ────────────────────────────────────────────────────────────
        // 照準判定
        // ────────────────────────────────────────────────────────────

        private bool IsAimingAtScreen()
        {
            if (aimTransform == null) return true; // テスト時は常に有効

            // 照準方向と画面正面方向のなす角を計算
            Vector3 aimDir      = aimTransform.forward;
            Vector3 screenNormal = mainCamera != null ? mainCamera.transform.forward : Vector3.forward;
            float angle = Vector3.Angle(aimDir, screenNormal);

            // maxAimAngle 以内なら「スクリーンを向いている」と判定
            return angle <= maxAimAngle;
        }

        // ────────────────────────────────────────────────────────────
        // 発射処理
        // ────────────────────────────────────────────────────────────

        private void Fire()
        {
            // ── Raycast ──────────────────────────────────────────────
            // 銃口位置から照準方向に Ray を飛ばす
            Ray ray = new Ray(
                muzzlePoint  != null ? muzzlePoint.position  : transform.position,
                aimTransform != null ? aimTransform.forward  : transform.forward
            );

            // paintTargetLayer に属するオブジェクトに最大 100m 先まで当たり判定
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, paintTargetLayer))
            {
                // 命中した UV 座標にインクを塗る
                PaintAt(hit);

                // 着弾エフェクト（スプラッシュ）を生成
                SpawnSplash(hit.point, hit.normal);
            }

            // 飛翔弾の見た目エフェクトを生成（Raycast の結果に関わらず常に出す）
            SpawnProjectile(ray.origin, ray.direction);
        }

        // ────────────────────────────────────────────────────────────
        // ペイント処理
        // ────────────────────────────────────────────────────────────

        private void PaintAt(RaycastHit hit)
        {
            if (_paintManager == null) return;

            // ─── ⚠️ 問題ポイント ───
            // hit.textureCoord は MeshCollider(convex=false) かつ UV 展開済みの
            // メッシュでないと (0,0) になる。Blender モデル未着時は Sphere 等で代替テスト。
            Vector2 uv = hit.textureCoord;

            // 現在のインク色を取得（colorCycler が null なら白）
            DrawBrush(_paintManager.PaintTexture, uv, _colorCycler?.CurrentColor ?? Color.white);
        }

        private void DrawBrush(RenderTexture target, Vector2 uv, Color color)
        {
            // GPU ブラシスタンプ（Graphics.Blit で UV 位置に円を描く）
            // 専用シェーダー InkPaintShader が必要。ない場合は簡易 CPU 描画にフォールバック。
            // TODO: InkPaintShader.shader 完成後にここを差し替える
            SimpleCpuBrush(target, uv, color);
        }

        /// <summary>
        /// シェーダー未完成時の CPU フォールバック描画（重い・精度低い）。
        /// GPU 描画（Graphics.Blit + Shader）に切り替えたら削除してよい。
        /// </summary>
        private void SimpleCpuBrush(RenderTexture rt, Vector2 uv, Color color)
        {
            // ① RenderTexture の現在の内容を Texture2D に読み出す
            Texture2D tmp  = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;

            // ② UV 座標をピクセル座標に変換（uv.x は横, uv.y は縦）
            int cx = Mathf.RoundToInt(uv.x * rt.width);
            int cy = Mathf.RoundToInt(uv.y * rt.height);
            int r  = Mathf.RoundToInt(brushRadius * rt.width); // UV半径をピクセル半径に変換

            // ③ ブラシ半径内のピクセルを1つずつ走査して色を塗る（= CPU ブラシ）
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy); // 中心からの距離
                    if (dist > r) continue;                      // 円の外は無視

                    // 外周に近いほど透明になるグラデーション（にじみ表現）
                    float alpha = Mathf.Lerp(1f, 0f, dist / r);
                    Color c = new Color(color.r, color.g, color.b, alpha);
                    tmp.SetPixel(cx + dx, cy + dy, c);
                }
            }

            // ④ 変更を GPU に適用して RenderTexture に書き戻す
            tmp.Apply();
            Graphics.Blit(tmp, rt);
            Destroy(tmp); // 一時的な Texture2D を解放（メモリリーク防止）
        }

        // ────────────────────────────────────────────────────────────
        // エフェクト生成
        // ────────────────────────────────────────────────────────────

        /// <summary>着弾点にスプラッシュエフェクトを生成する</summary>
        private void SpawnSplash(Vector3 position, Vector3 normal)
        {
            if (inkSplashPrefab == null)
            {
                // ⚠️ Blender モデル未完成時はスキップ（警告のみ）
                return;
            }

            // 法線方向を向いてエフェクトを配置（壁面に対して垂直に貼りつく）
            GameObject splash = Instantiate(inkSplashPrefab, position,
                                            Quaternion.LookRotation(normal));
            Destroy(splash, 1.5f); // 1.5 秒後に自動削除
        }

        /// <summary>銃口から飛翔弾の見た目オブジェクトを生成する</summary>
        private void SpawnProjectile(Vector3 origin, Vector3 direction)
        {
            if (inkProjectilePrefab == null)
            {
                // ⚠️ Blender モデル未完成時はスキップ（警告のみ）
                return;
            }

            // 発射方向を向いたプレファブを生成して InkBullet で飛ばす
            GameObject proj = Instantiate(inkProjectilePrefab, origin,
                                          Quaternion.LookRotation(direction));

            // AddComponent で InkBullet を動的にアタッチして初期化
            var bullet = proj.AddComponent<InkBullet>();
            bullet.Initialize(direction);
        }
    }
}

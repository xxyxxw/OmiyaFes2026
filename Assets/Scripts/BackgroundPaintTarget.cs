using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 背景画像の上にインクを塗り重ねる専用 PaintTarget。
    ///
    /// ▼ 通常の PaintTarget との違い
    ///   - Awake で RenderTexture を「透明」ではなく「背景画像のコピー」で初期化する
    ///   - ClearPaint() が呼ばれた場合も、透明ではなく背景画像の状態に戻す
    ///   - インクが当たるたびに背景の上にインクが重なっていく
    ///
    /// ▼ 使い方
    ///   BackgroundSetup が自動でアタッチする。手動アタッチも可。
    ///   Inspector の baseTexture に background.png をドラッグすること。
    ///
    /// ▼ 仕組み（重要）
    ///   RenderTexture (paintRT) の初期状態 = background.png の内容
    ///   → インク着弾時に PaintPixels() でそこに色を描き込む
    ///   → リセット時は Graphics.Blit(baseTexture, paintRT) で元に戻す
    /// </summary>
    public class BackgroundPaintTarget : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Header("背景画像")]
        [Tooltip("ベースとなる背景テクスチャ（background.png）をここにドラッグ")]
        [SerializeField] public Texture2D baseTexture;

        [Header("ペイント設定")]
        [Tooltip("ペイントテクスチャの解像度（例: 512）")]
        [SerializeField] private int textureSize = 512;

        [Tooltip("着弾点を中心に塗るブラシのピクセル半径（例: 80）")]
        [SerializeField] [Range(1, 256)] private int brushPixelRadius = 80;

        [Tooltip("0 より大きい値を設定すると、InkBullet 側の brushPixelRadius を強制上書きする。\n0 = 上書きなし")]
        [SerializeField] [Range(0, 512)] private int forceBrushPixelRadius = 0;

        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        private RenderTexture _paintRT;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _paintRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
            _paintRT.name = $"BgPaintTex_{gameObject.GetInstanceID()}";
            _paintRT.Create();

            // ── 背景画像をベースとして書き込む ──────────────────────
            RestoreBaseTexture();

            // ── マテリアルの mainTexture を RenderTexture に差し替える ─
            var r = GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(r.sharedMaterial);
                r.material.mainTexture = _paintRT;
            }
        }

        private void OnDestroy()
        {
            if (_paintRT != null)
                _paintRT.Release();
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド（PaintTarget と同じシグネチャ）
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// UV 座標にインクを塗る。InkBullet.OnTriggerEnter から呼ばれる。
        /// </summary>
        /// <param name="uv">着弾 UV 座標（0〜1）</param>
        /// <param name="color">インク色</param>
        /// <param name="pixelRadiusOverride">0以下なら Inspector の brushPixelRadius を使用</param>
        public void Paint(Vector2 uv, Color color, int pixelRadiusOverride = 0)
        {
            int radius = forceBrushPixelRadius > 0
                ? forceBrushPixelRadius
                : (pixelRadiusOverride > 0 ? pixelRadiusOverride : brushPixelRadius);

            PaintPixels(_paintRT, uv, color, radius);
        }

        /// <summary>
        /// インクを消去して背景画像の状態に戻す。
        /// GameStateManager.OnReset から呼ばれる。
        /// </summary>
        public void ClearPaint()
        {
            if (_paintRT != null)
                RestoreBaseTexture();
        }

        // ────────────────────────────────────────────────────────────
        // 内部：背景画像を RenderTexture に書き戻す
        // ────────────────────────────────────────────────────────────

        private void RestoreBaseTexture()
        {
            if (baseTexture != null)
            {
                // Graphics.Blit で背景画像をそのまま RenderTexture に書き込む
                Graphics.Blit(baseTexture, _paintRT);
            }
            else
            {
                // 背景テクスチャが未設定の場合は黒でクリア
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = _paintRT;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = prev;
                Debug.LogWarning("[BackgroundPaintTarget] baseTexture が未設定です。黒で初期化します。");
            }
        }

        // ────────────────────────────────────────────────────────────
        // 内部：CPU ブラシ描画（PaintTarget と同じロジック）
        // ────────────────────────────────────────────────────────────

        private static void PaintPixels(RenderTexture rt, Vector2 uv, Color color, int pixelRadius)
        {
            // ① RenderTexture → Texture2D へ読み出す
            Texture2D tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;

            // ② UV → ピクセル座標
            int cx = Mathf.RoundToInt(uv.x * (rt.width  - 1));
            int cy = Mathf.RoundToInt(uv.y * (rt.height - 1));
            int pr = pixelRadius;
            float prF = (float)pr;

            // ③ ブラシ円内を走査してインクを重ねる
            for (int dx = -pr; dx <= pr; dx++)
            {
                for (int dy = -pr; dy <= pr; dy++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > prF) continue;

                    int px = cx + dx;
                    int py = cy + dy;
                    if (px < 0 || px >= rt.width || py < 0 || py >= rt.height) continue;

                    // 外周に向かって透明になるグラデーション（背景と自然にブレンド）
                    float alpha = Mathf.Lerp(1f, 0f, dist / prF);

                    // 背景ピクセルにインク色をアルファブレンドで重ねる
                    // α=1 の中心は完全にインク色、外周は背景が透けて見える
                    Color existing = tmp.GetPixel(px, py);
                    Color blended  = Color.Lerp(existing, color, alpha * 0.9f);
                    blended.a = 1f; // 背景は常に不透明
                    tmp.SetPixel(px, py, blended);
                }
            }

            // ④ GPU へ書き戻す
            tmp.Apply();
            Graphics.Blit(tmp, rt);
            Destroy(tmp);
        }
    }
}

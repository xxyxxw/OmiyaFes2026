using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 流れてくるオブジェクト1個に付属するペイントコンポーネント。
    /// Awake で自分専用の RenderTexture を生成し、Paint() でインクを描く。
    /// InkBullet から直接呼ばれる。
    ///
    /// ▼ 設計方針（着弾地点から数ピクセル塗る）
    ///   - brushPixelRadius: テクスチャ上のピクセル半径で直接指定（ワールドサイズ依存しない）
    ///   - InkBullet は hit.textureCoord を取得して Paint(uv, color) を呼ぶだけ
    /// </summary>
    public class PaintTarget : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Tooltip("ペイントテクスチャの解像度（例: 512）")]
        [SerializeField] private int textureSize = 512;

        [Tooltip("着弾点を中心に塗るブラシのピクセル半径（例: 30 なら直径60px）")]
        [SerializeField] [Range(1, 128)] private int brushPixelRadius = 30;

        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        private RenderTexture _paintTexture;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            _paintTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
            _paintTexture.name = $"PaintTex_{gameObject.GetInstanceID()}";
            _paintTexture.Create();

            ClearTexture();

            var r = GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(r.sharedMaterial);
                r.material.mainTexture = _paintTexture;
            }
        }

        private void OnDestroy()
        {
            if (_paintTexture != null)
                _paintTexture.Release();
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// UV 座標にインクを塗る。InkBullet.OnTriggerEnter から呼ばれる。
        /// </summary>
        /// <param name="uv">着弾 UV 座標（0〜1）</param>
        /// <param name="color">インク色</param>
        /// <param name="pixelRadiusOverride">0以下なら Inspector の brushPixelRadius を使用</param>
        public void Paint(Vector2 uv, Color color, int pixelRadiusOverride = 0)
        {
            int radius = pixelRadiusOverride > 0 ? pixelRadiusOverride : brushPixelRadius;
            PaintPixels(_paintTexture, uv, color, radius);
        }

        // ────────────────────────────────────────────────────────────
        // 内部：CPU ブラシ描画（ピクセル半径指定）
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

            // ③ ブラシ円内を走査
            for (int dx = -pr; dx <= pr; dx++)
            {
                for (int dy = -pr; dy <= pr; dy++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > prF) continue;

                    int px = cx + dx;
                    int py = cy + dy;
                    if (px < 0 || px >= rt.width || py < 0 || py >= rt.height) continue;

                    // 外周に向かって透明になるグラデーション
                    float alpha = Mathf.Lerp(1f, 0f, dist / prF);
                    Color existing = tmp.GetPixel(px, py);
                    Color blended  = Color.Lerp(existing, color, alpha);
                    blended.a = Mathf.Max(existing.a, alpha);
                    tmp.SetPixel(px, py, blended);
                }
            }

            // ④ GPU へ書き戻す
            tmp.Apply();
            Graphics.Blit(tmp, rt);
            Destroy(tmp);
        }

        private void ClearTexture()
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _paintTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }
    }
}

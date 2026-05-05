using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// 流れてくるオブジェクト1個に付属するペイントコンポーネント。
    /// Awake で自分専用の RenderTexture を生成し、Paint() でインクを描く。
    /// InkBullet から直接呼ばれる。
    /// </summary>
    public class PaintTarget : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [SerializeField] private int   textureSize       = 512;  // テクスチャ解像度
        [SerializeField] private float brushWorldRadius = 0.5f; // デフォルトブラシ半径（ワールド空間・メートル単位）

        // ────────────────────────────────────────────────────────────
        // 内部フィールド
        // ────────────────────────────────────────────────────────────

        private RenderTexture _paintTexture;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // RenderTexture を生成
            _paintTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
            _paintTexture.name = $"PaintTex_{gameObject.GetInstanceID()}";
            _paintTexture.Create();

            // 全透明でリセット
            ClearTexture();

            // このオブジェクトの Renderer マテリアルに適用
            var r = GetComponent<Renderer>();
            if (r != null)
            {
                // sharedMaterial を変えないようインスタンスを複製してから上書き
                r.material = new Material(r.sharedMaterial);
                r.material.mainTexture = _paintTexture;
            }
        }

        private void OnDestroy()
        {
            // GPU リソース解放
            if (_paintTexture != null)
                _paintTexture.Release();
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// UV 座標にインクを塗る。InkBullet.OnTriggerEnter から呼ばれる。
        /// worldRadius：ワールド空間での塗り半径（メートル）。0以下はデフォルト値を使用。
        /// 内部で Renderer.bounds を参照してUV空間の半径に自動変換する。
        /// </summary>
        public void Paint(Vector2 uv, Color color, float worldRadius = 0f)
        {
            float wr = worldRadius > 0f ? worldRadius : brushWorldRadius;

            // ── ワールド半径 → UV 半径 変換 ──────────────────────
            // オブジェクトの XZ 方向の実寸（ワールド空間）を取得し、
            // worldRadius ÷ objectSize = uvRadius と近似する。
            // 例: 1m サイズのオブジェクトで worldRadius=0.5 → uvRadius=0.5 (テクスチャの半分)
            float uvRadius = 0.1f; // フォールバック値
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                // bounds.size はワールドスケール込みのサイズ
                // X と Z の大きい方を基準（横・奥行き方向のスケールに合わせる）
                float boundsSize = Mathf.Max(rend.bounds.size.x, rend.bounds.size.z);
                if (boundsSize > 0f)
                    uvRadius = wr / boundsSize;
            }

            // UV 半径を安全な範囲にクランプ（0.01〜0.5）
            uvRadius = Mathf.Clamp(uvRadius, 0.01f, 0.5f);

            SimpleCpuBrush(_paintTexture, uv, color, uvRadius);
        }


        // ────────────────────────────────────────────────────────────
        // 内部：CPU ブラシ描画
        // ────────────────────────────────────────────────────────────

        private void SimpleCpuBrush(RenderTexture rt, Vector2 uv, Color color, float radius)
        {
            // ① RenderTexture の現在内容を Texture2D に読み出す
            Texture2D tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;

            // ② UV → ピクセル座標に変換
            int cx = Mathf.RoundToInt(uv.x * rt.width);
            int cy = Mathf.RoundToInt(uv.y * rt.height);
            int pr = Mathf.RoundToInt(radius * rt.width);

            // ③ ブラシ半径内を走査して色を塗る
            for (int dx = -pr; dx <= pr; dx++)
            {
                for (int dy = -pr; dy <= pr; dy++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > pr) continue;

                    int px = cx + dx;
                    int py = cy + dy;
                    // テクスチャ範囲外はスキップ
                    if (px < 0 || px >= rt.width || py < 0 || py >= rt.height) continue;

                    // 外周ほど透明になるグラデーション
                    float alpha = Mathf.Lerp(1f, 0f, dist / pr);
                    Color existing = tmp.GetPixel(px, py);
                    // 既存の色とブレンド（上書きではなく重ね塗り）
                    Color blended = Color.Lerp(existing, color, alpha);
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

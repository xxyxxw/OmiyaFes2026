using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// ペイント用 RenderTexture の初期化・リセットを管理する。
    /// キャラクターマテリアルの MainTexture として使用される。
    /// </summary>
    public class PaintTextureManager : MonoBehaviour
    {
        [Header("塗りターゲット")]
        [Tooltip("ペイント対象のキャラクターRendererを設定。Blenderモデルが来るまではダミーで動作確認可")]
        [SerializeField] private Renderer targetRenderer;

        [Header("RenderTexture設定")]
        [SerializeField] private int textureWidth  = 1024;
        [SerializeField] private int textureHeight = 1024;

        public RenderTexture PaintTexture { get; private set; }

        // ペイントされた有効ピクセル数（面積計算用）
        private Texture2D _readbackTexture;

        private void Awake()
        {
            InitializeTexture();
        }

        private void InitializeTexture()
        {
            PaintTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32);
            PaintTexture.name = "PaintTexture";
            PaintTexture.Create();
            ClearTexture();

            // ターゲットにテクスチャをセット
            ApplyToRenderer();

            _readbackTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        }

        private void ApplyToRenderer()
        {
            if (targetRenderer == null)
            {
                // ─── ⚠️ 問題ポイント ───
                // Blender モデルが未完成の場合、ここで null になりテクスチャが適用されない。
                // テスト用にプリミティブ（Cube/Sphere）を代替として使用すること。
                Debug.LogWarning("[PaintTextureManager] targetRenderer が未設定です。" +
                                 "Blenderモデルがインポートされたらアサインしてください。");
                return;
            }

            var mat = targetRenderer.material;
            mat.mainTexture = PaintTexture;
        }

        /// <summary>RenderTexture を透明色でリセット（インクをすべて消す）</summary>
        public void ResetTexture()
        {
            ClearTexture();
            Debug.Log("[PaintTextureManager] ペイントリセット完了");
        }

        private void ClearTexture()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = PaintTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;
        }

        /// <summary>公開: RendererのアサインをBlenderモデルインポート後に外部から設定できる</summary>
        public void SetTargetRenderer(Renderer r)
        {
            targetRenderer = r;
            ApplyToRenderer();
        }

        private void OnDestroy()
        {
            if (PaintTexture != null)
                PaintTexture.Release();
        }
    }
}

using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// ペイント用 RenderTexture の初期化・リセットを管理する。
    /// キャラクターマテリアルの MainTexture として使用される。
    /// </summary>
    public class PaintTextureManager : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定フィールド
        // ────────────────────────────────────────────────────────────

        [Header("塗りターゲット")]
        [Tooltip("ペイント対象のキャラクターRendererを設定。Blenderモデルが来るまではダミーで動作確認可")]
        // インクを塗るキャラクターの MeshRenderer / SkinnedMeshRenderer
        [SerializeField] private Renderer targetRenderer;

        [Header("RenderTexture設定")]
        // 生成する RenderTexture の解像度（大きいほど高精細・重い）
        [SerializeField] private int textureWidth  = 1024;
        [SerializeField] private int textureHeight = 1024;

        // ────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// ペイント用 RenderTexture（InkGun が直接書き込む対象）。
        /// 外部からは読み取りのみ（private set）。
        /// </summary>
        public RenderTexture PaintTexture { get; private set; }

        // ────────────────────────────────────────────────────────────
        // 内部参照
        // ────────────────────────────────────────────────────────────

        // ペイントされた有効ピクセル数（面積計算用）
        // ※ 面積スコア機能を実装する場合に使用予定
        private Texture2D _readbackTexture;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            InitializeTexture();
        }

        // ────────────────────────────────────────────────────────────
        // 初期化処理
        // ────────────────────────────────────────────────────────────

        private void InitializeTexture()
        {
            // ARGB32 フォーマットで RenderTexture を生成（アルファチャンネルあり）
            PaintTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32);
            PaintTexture.name = "PaintTexture";
            PaintTexture.Create(); // GPU 上にリソースを確保

            ClearTexture(); // 最初は全透明にリセット

            // ターゲットの Renderer のマテリアルにテクスチャをセット
            ApplyToRenderer();

            // CPU 側へのピクセル読み戻し用一時バッファ（面積計測などに使用）
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

            // Renderer のマテリアルに PaintTexture を MainTexture として適用
            // これにより、テクスチャに書き込んだ内容がキャラクターの表面に反映される
            var mat = targetRenderer.material;
            mat.mainTexture = PaintTexture;
        }

        // ────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────

        /// <summary>RenderTexture を透明色でリセット（インクをすべて消す）</summary>
        public void ResetTexture()
        {
            ClearTexture();
            Debug.Log("[PaintTextureManager] ペイントリセット完了");
        }

        /// <summary>
        /// Blenderモデルインポート後、外部から Renderer をセットするためのメソッド。
        /// Awake では null だった targetRenderer を後から登録できる。
        /// </summary>
        public void SetTargetRenderer(Renderer r)
        {
            targetRenderer = r;
            ApplyToRenderer(); // セット後すぐにテクスチャを適用
        }

        // ────────────────────────────────────────────────────────────
        // 内部ユーティリティ
        // ────────────────────────────────────────────────────────────

        private void ClearTexture()
        {
            // 現在アクティブな RenderTexture を退避させてから PaintTexture に切り替え
            var prev = RenderTexture.active;
            RenderTexture.active = PaintTexture;

            // GL.Clear で全ピクセルを透明（Color.clear = RGBA 0,0,0,0）にする
            GL.Clear(true, true, Color.clear);

            // 元の RenderTexture に戻す（他の描画処理に影響しないよう必ず復元）
            RenderTexture.active = prev;
        }

        // ────────────────────────────────────────────────────────────
        // 後片付け
        // ────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            // シーン破棄時に GPU リソースを解放してメモリリークを防ぐ
            if (PaintTexture != null)
                PaintTexture.Release();
        }
    }
}

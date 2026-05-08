using UnityEngine;

namespace OmiyaFes2026
{
    /// <summary>
    /// シーンに配置済みの Quad に対して、背景テクスチャとインク塗り機能を設定する。
    ///
    /// ▼ 使い方（新方式）
    ///   1. シーンに Quad を手動配置し、位置・スケールを好きに設定しておく
    ///   2. 空の GameObject（例: "BackgroundManager"）に このスクリプトをアタッチ
    ///   3. Inspector の「背景Quad」に、シーン上の Quad をドラッグ
    ///   4. Inspector の「背景テクスチャ」に background.png をドラッグ
    ///
    /// ▼ 変更点（旧方式との違い）
    ///   旧: Start() で Quad を動的生成していた → 余計な板が1枚増えてしまっていた
    ///   新: 既存の Quad を参照し、テクスチャ・インク設定だけを追加する
    ///
    /// ▼ インク塗り対応
    ///   MeshCollider を BoxCollider(Trigger) に差し替えて BackgroundPaintTarget を付与する。
    ///   InkBullet が当たると背景画像の上にインクが重なっていく。
    /// </summary>
    public class BackgroundSetup : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        // インスペクター設定
        // ────────────────────────────────────────────────────────────

        [Header("既存の背景 Quad")]
        [Tooltip("シーンに配置済みの Quad をここにドラッグしてください（自動生成はしません）")]
        [SerializeField] private GameObject backgroundQuad;

        [Header("背景テクスチャ")]
        [Tooltip("Assets フォルダに入れた背景画像をここにドラッグ")]
        [SerializeField] private Texture2D backgroundTexture;

        [Header("インク設定")]
        [Tooltip("インクが当たるようにするか（true = BackgroundPaintTarget を自動アタッチ）")]
        [SerializeField] private bool enableInkPainting = true;

        [Tooltip("ペイントテクスチャの解像度（高いほど精細だが重い）")]
        [SerializeField] private int paintTextureSize = 512;

        [Tooltip("背景に当たったときのブラシ半径（ピクセル）")]
        [SerializeField] [Range(1, 512)] private int brushPixelRadius = 80;

        // ────────────────────────────────────────────────────────────
        // Unity ライフサイクル
        // ────────────────────────────────────────────────────────────

        private void Start()
        {
            SetupBackground();
        }

        // ────────────────────────────────────────────────────────────
        // 背景セットアップ
        // ────────────────────────────────────────────────────────────

        private void SetupBackground()
        {
            // ── 参照チェック（未設定なら名前で自動検索） ──────────────
            if (backgroundQuad == null)
            {
                // "Quad" という名前の GameObject をシーン全体から検索
                backgroundQuad = GameObject.Find("Quad");

                if (backgroundQuad != null)
                {
                    Debug.Log($"[BackgroundSetup] 「背景Quad」が未設定のため、シーン内の「{backgroundQuad.name}」を自動検出しました。");
                }
                else
                {
                    Debug.LogError(
                        "[BackgroundSetup] 「背景Quad」が見つかりません。\n" +
                        "① Inspector の「背景Quad」フィールドにシーンの Quad をドラッグ、または\n" +
                        "② シーン上の Quad の名前が「Quad」であることを確認してください。");
                    return;
                }
            }

            // ── マテリアル設定 ──────────────────────────────────
            var rend = backgroundQuad.GetComponent<Renderer>();
            if (rend == null)
            {
                Debug.LogError("[BackgroundSetup] 背景Quad に Renderer がありません。");
                return;
            }

            // URP / Built-in 両対応のシェーダーを優先順で探す
            Shader texShader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Texture")
                            ?? Shader.Find("Sprites/Default");

            if (texShader == null)
            {
                Debug.LogWarning("[BackgroundSetup] 使用可能なシェーダーが見つかりませんでした。デフォルトマテリアルを使用します。");
            }

            Material mat = texShader != null ? new Material(texShader) : new Material(rend.sharedMaterial ?? rend.material);
            if (backgroundTexture != null)
            {
                mat.mainTexture = backgroundTexture;
                // URP の場合は _BaseMap にもセット
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", backgroundTexture);
            }
            else
            {
                mat.color = Color.gray;
                Debug.LogWarning("[BackgroundSetup] backgroundTexture が未設定です。グレーで表示します。");
            }
            rend.material = mat;

            // ── インク塗り設定 ──────────────────────────────────
            if (enableInkPainting)
            {
                SetupInkPainting(backgroundQuad);
            }
            else
            {
                // インク不要ならコライダーをすべて削除（弾が貫通するだけの純粋な背景板）
                foreach (var c in backgroundQuad.GetComponents<Collider>())
                    Destroy(c);
            }

            Debug.Log(
                $"[BackgroundSetup] 背景セットアップ完了\n" +
                $"  Quad     = {backgroundQuad.name}\n" +
                $"  Position = {backgroundQuad.transform.position}\n" +
                $"  Scale    = {backgroundQuad.transform.localScale}\n" +
                $"  Texture  = {(backgroundTexture != null ? backgroundTexture.name : "未設定")}");
        }

        // ────────────────────────────────────────────────────────────
        // インク塗りセットアップ
        // ────────────────────────────────────────────────────────────

        private void SetupInkPainting(GameObject quad)
        {
            // ── 既存コライダーを全削除 ──────────────────────────
            // MeshCollider（non-convex）は isTrigger=true にできないため
            // BoxCollider に差し替える
            foreach (var c in quad.GetComponents<Collider>())
                Destroy(c);

            // ── BoxCollider（Trigger）を付与 ────────────────────
            // BoxCollider は isTrigger=true に対応 → FloatingObject が貫通できる
            // 厚みを 0.5f にして弾が抜けにくくする（Quad はほぼ平面）
            var boxCol      = quad.AddComponent<BoxCollider>();
            boxCol.size     = new Vector3(1f, 1f, 0.5f);
            boxCol.center   = Vector3.zero;
            boxCol.isTrigger = true;

            // ── レイヤーを Ignore Raycast に設定 ────────────────
            // FloatingObject（Layer 8）との物理干渉を防ぐ
            // InkBullet の OnTriggerEnter は Layer 問わず動作するので問題なし
            int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
            quad.layer = ignoreLayer >= 0 ? ignoreLayer : 0;

            // ── BackgroundPaintTarget をアタッチ ────────────────
            // 既にアタッチ済みの場合は重複しないよう既存を削除してから追加
            var existing = quad.GetComponent<BackgroundPaintTarget>();
            if (existing != null) Destroy(existing);

            var bgPaintTarget      = quad.AddComponent<BackgroundPaintTarget>();
            bgPaintTarget.baseTexture = backgroundTexture;

            // privateフィールドは Reflection で設定
            var type  = typeof(BackgroundPaintTarget);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var texField   = type.GetField("textureSize",     flags);
            var brushField = type.GetField("brushPixelRadius", flags);
            if (texField   != null) texField.SetValue(bgPaintTarget,   paintTextureSize);
            if (brushField != null) brushField.SetValue(bgPaintTarget, brushPixelRadius);

            Debug.Log(
                $"[BackgroundSetup] インク塗り設定完了\n" +
                $"  BoxCollider(Trigger) + BackgroundPaintTarget をアタッチしました\n" +
                $"  Layer = Ignore Raycast（FloatingObject と非干渉）");
        }
    }
}

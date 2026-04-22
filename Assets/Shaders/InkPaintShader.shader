// ========================================================================
// InkPaintShader
// 役割: キャラクター表面へのペイント描画 + ブラシスタンプ書き込みの2パス構成。
//
// Pass 1 (UniversalForward): RenderTexture の内容を URP の Forward レンダリングで表示。
//                            インクが塗られた部分に「濡れ感」の光沢ハイライトを加算する。
// Pass 2 (BrushStamp)      : Graphics.Blit 用パス。RenderTexture にブラシの色・形を書き込む。
//                            アルファブレンドでやわらかいにじみ感を表現。
// ========================================================================
Shader "OmiyaFes2026/InkPaintShader"
{
    Properties
    {
        // ── テクスチャ ─────────────────────────────────────────────
        _MainTex    ("Base (Paint) Texture",   2D) = "white" {} // ペイント済み RenderTexture
        _BrushTex   ("Brush Alpha Texture",    2D) = "white" {} // ブラシの形状を決めるアルファマップ

        // ── ブラシパラメータ ───────────────────────────────────────
        _BrushColor ("Brush Color",         Color) = (1,0,0,1)      // 塗るインクの色
        _BrushUV    ("Brush Center UV",    Vector) = (0.5, 0.5, 0, 0) // ブラシ中心の UV 座標
        _BrushRadius("Brush Radius (UV)",   Float) = 0.05            // ブラシの半径（UV空間 0〜1）

        // ── 光沢演出 ───────────────────────────────────────────────
        _Glossiness ("Smoothness",          Float) = 0.8 // 光沢の強さ（1 = 最大）
        _GlossFade  ("Gloss Fade (0=shiny)",Float) = 0.0 // フェード（0 = 光沢全開, 1 = 光沢なし）
    }

    SubShader
    {
        // URP の Forward レンダリングパイプラインを対象とする
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ════════════════════════════════════════════════════════════
        // Pass 1: URP Forward — ペイントテクスチャ表示 + 光沢演出
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" } // URP の Forward ライティングパスとして登録

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            // URP の共通関数・マクロをインクルード
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── 頂点シェーダーの入力構造体 ────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;  // オブジェクト空間の頂点座標
                float2 uv         : TEXCOORD0; // UV 座標（テクスチャ貼り付けに使用）
            };

            // ── フラグメントシェーダーへの受け渡し構造体 ─────────
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // クリップ空間の頂点座標（GPU が使用）
                float2 uv          : TEXCOORD0;   // UV 座標（フラグメントシェーダーに渡す）
            };

            // テクスチャとサンプラーの宣言（URP マクロを使用）
            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_BrushTex);  SAMPLER(sampler_BrushTex);

            // Properties で宣言した変数をここで受け取る
            float4 _BrushColor;
            float4 _BrushUV;
            float  _BrushRadius;
            float  _Glossiness;
            float  _GlossFade;

            // ── 頂点シェーダー ─────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // TransformObjectToHClip: オブジェクト空間 → クリップ空間への変換（URP ユーティリティ）
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv; // UV をそのままフラグメントシェーダーへ
                return OUT;
            }

            // ── フラグメントシェーダー ─────────────────────────────
            // ピクセル単位で呼ばれ、最終的な色を返す
            half4 frag(Varyings IN) : SV_Target
            {
                // ① ペイント済み RenderTexture から現在ピクセルの色を取得
                half4 baseTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // ② 光沢（濡れ感）の計算
                // ※ Forward Pass 内で Smoothness を直接使うのは本来 PBR 計算が必要だが、
                //   ここでは簡易的に明るさブーストで「濡れ感」を表現する
                half gloss = _Glossiness * (1.0 - _GlossFade);
                half3 wetHighlight = baseTex.rgb * (1.0 + gloss * 0.5); // 50%明るさを加算

                // ③ インクが塗られている部分（alpha > 0）だけ光沢を反映
                //    lerp(a, b, t) = a と b を t の割合で混合
                half3 finalColor = lerp(baseTex.rgb, wetHighlight, baseTex.a);

                return half4(finalColor, 1.0); // アルファは常に不透明
            }
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        // Pass 2: BrushStamp — RenderTexture へのブラシ書き込み専用
        // InkGun.cs の DrawBrush から Graphics.Blit でこのパスを呼ぶ
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "BrushStamp"
            // アルファブレンド設定: src.a と (1-src.a) の標準アルファブレンド
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off  // 深度バッファへの書き込みをしない（2D合成なので不要）
            Cull Off    // 裏面カリング無効（Blit の全画面クワッドは両面必要）

            HLSLPROGRAM
            #pragma vertex   vertBrush
            #pragma fragment fragBrush
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Blit 用の軽量な入出力構造体
            struct AttrBrush { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct VaryBrush { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BrushTex); SAMPLER(sampler_BrushTex);
            float4 _BrushColor;   // 塗るインクの色（C# から Material.SetColor で渡す）

            // 頂点シェーダー（単純なクリップ空間変換のみ）
            VaryBrush vertBrush(AttrBrush IN)
            {
                VaryBrush OUT;
                OUT.pos = TransformObjectToHClip(IN.pos.xyz);
                OUT.uv  = IN.uv;
                return OUT;
            }

            // フラグメントシェーダー（ブラシのにじみ効果を計算）
            half4 fragBrush(VaryBrush IN) : SV_Target
            {
                // ブラシテクスチャのアルファチャンネルを取得（円形グラデーション想定）
                half alpha = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, IN.uv).a;

                // alpha を二乗することで外周をより強く透明にする（ガンマ的ににじみを強調）
                // 例: alpha=0.5 → 0.5*0.5 = 0.25 でエッジがより柔らかくなる
                alpha = alpha * alpha;

                // ブラシ色 × 計算したアルファで最終色を返す
                return half4(_BrushColor.rgb, alpha * _BrushColor.a);
            }
            ENDHLSL
        }
    }

    // このシェーダーが使えない環境では URP 標準の Lit シェーダーにフォールバック
    FallBack "Universal Render Pipeline/Lit"
}

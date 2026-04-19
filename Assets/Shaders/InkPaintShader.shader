Shader "OmiyaFes2026/InkPaintShader"
{
    Properties
    {
        _MainTex    ("Base (Paint) Texture",   2D) = "white" {}
        _BrushTex   ("Brush Alpha Texture",    2D) = "white" {}
        _BrushColor ("Brush Color",         Color) = (1,0,0,1)
        _BrushUV    ("Brush Center UV",    Vector) = (0.5, 0.5, 0, 0)
        _BrushRadius("Brush Radius (UV)",   Float) = 0.05
        // 光沢演出
        _Glossiness ("Smoothness",          Float) = 0.8
        _GlossFade  ("Gloss Fade (0=shiny)",Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ── Pass 1: URP Lit をベースにペイントを合成 ──────
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_BrushTex);  SAMPLER(sampler_BrushTex);

            float4 _BrushColor;
            float4 _BrushUV;
            float  _BrushRadius;
            float  _Glossiness;
            float  _GlossFade;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ベース（ペイント済み RenderTexture）
                half4 baseTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // 光沢フェードを Smoothness に反映
                // ※ Forward Pass 内で Smoothness を直接使うのは本来 PBR 計算が必要だが、
                //   ここでは簡易的に明るさブーストで"濡れ感"を表現する
                half gloss = _Glossiness * (1.0 - _GlossFade);
                half3 wetHighlight = baseTex.rgb * (1.0 + gloss * 0.5);

                // 塗りアルファがある部分だけ光沢を加算
                half3 finalColor = lerp(baseTex.rgb, wetHighlight, baseTex.a);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ── Pass 2: ブラシスタンプ（Blit 用）─────────────
        Pass
        {
            Name "BrushStamp"
            // Graphics.Blit で RenderTexture に書き込む専用 Pass
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vertBrush
            #pragma fragment fragBrush
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttrBrush { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct VaryBrush { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BrushTex); SAMPLER(sampler_BrushTex);
            float4 _BrushColor;

            VaryBrush vertBrush(AttrBrush IN)
            {
                VaryBrush OUT;
                OUT.pos = TransformObjectToHClip(IN.pos.xyz);
                OUT.uv  = IN.uv;
                return OUT;
            }

            half4 fragBrush(VaryBrush IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, IN.uv).a;
                // 外周ほど薄くするにじみ感
                alpha = alpha * alpha; // ガンマ補正的にじわり
                return half4(_BrushColor.rgb, alpha * _BrushColor.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}

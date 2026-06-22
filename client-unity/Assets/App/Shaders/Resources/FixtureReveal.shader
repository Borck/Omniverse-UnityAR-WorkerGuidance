Shader "Guidance/FixtureReveal"
{
    Properties
    {
        _BaseColor      ("Base Color",       Color)         = (0.75, 0.78, 0.82, 1.0)
        _GlowColor      ("Glow Color",       Color)         = (0.4, 1.0, 1.0, 1.0)
        _RevealY        ("Reveal Y (world)", Float)         = -1000
        _GlowBandWidth  ("Glow Band Width",  Range(0, 1))   = 0.04
        _BodyAlpha      ("Body Alpha",       Range(0, 1))   = 0.85
        _GlowIntensity  ("Glow Intensity",   Range(0, 5))   = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FixtureRevealForward"
            // UniversalForward so URP renders this pass. ForwardBase (Built-in)
            // is skipped by URP, which made the overlay invisible.
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float3 worldNormal: TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GlowColor;
                float  _RevealY;
                float  _GlowBandWidth;
                float  _BodyAlpha;
                float  _GlowIntensity;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos         = TransformObjectToHClip(v.vertex.xyz);
                o.worldPos    = TransformObjectToWorld(v.vertex.xyz);
                o.worldNormal = TransformObjectToWorldNormal(v.normal);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // Above the slice plane: clip pixels (they are not yet revealed).
                if (i.worldPos.y > _RevealY)
                {
                    discard;
                }

                // Distance below the slice plane.
                float dist = _RevealY - i.worldPos.y;

                // Within the glow band: blend base toward bright glow.
                if (dist < _GlowBandWidth)
                {
                    float t = saturate(1.0 - (dist / _GlowBandWidth));
                    half3 col = lerp(_BaseColor.rgb, _GlowColor.rgb * _GlowIntensity, t);
                    half alpha = lerp(_BodyAlpha, 1.0, t);
                    return half4(col, alpha);
                }

                // Below the band: solid body at body alpha.
                return half4(_BaseColor.rgb, _BodyAlpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

Shader "Guidance/Hologram"
{
    Properties
    {
        _BaseColor          ("Base Color",          Color) = (0.2, 0.8, 1.0, 1.0)
        _RimColor           ("Rim Color",           Color) = (0.4, 1.0, 1.0, 1.0)
        _RimPower           ("Rim Power",           Range(0.5, 8)) = 2.5
        _RimIntensity       ("Rim Intensity",       Range(0, 5))   = 2.0
        _Alpha              ("Body Alpha",          Range(0, 1))   = 0.18
        _ScanlineSpeed      ("Scanline Speed",      Range(0, 5))   = 0.6
        _ScanlineDensity    ("Scanline Density",    Range(1, 200)) = 60
        _ScanlineIntensity  ("Scanline Intensity",  Range(0, 1))   = 0.4
        _PulseSpeed         ("Pulse Speed",         Range(0, 5))   = 0.5
        _PulseAmount        ("Pulse Amount",        Range(0, 1))   = 0.75
    }

    SubShader
    {
        // RenderPipeline tag tells URP this SubShader is for it.
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HologramForward"
            // UniversalForward is the LightMode URP renders. ForwardBase (the
            // old Built-in tag) is silently skipped by URP -> invisible model.
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
                float4 pos         : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            // SRP Batcher compatibility: all material properties in one CBUFFER.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float  _RimPower;
                float  _RimIntensity;
                float  _Alpha;
                float  _ScanlineSpeed;
                float  _ScanlineDensity;
                float  _ScanlineIntensity;
                float  _PulseSpeed;
                float  _PulseAmount;
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
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float  ndotv   = saturate(dot(normalize(i.worldNormal), viewDir));
                float  fresnel = pow(1.0 - ndotv, _RimPower) * _RimIntensity;

                // Double-tap heartbeat: two close peaks (lub-dub), then a rest,
                // repeating once per (1/_PulseSpeed) seconds.
                float phase = frac(_Time.y * _PulseSpeed);
                float peak1 = exp(-pow((phase - 0.08) * 14.0, 2.0));
                float peak2 = exp(-pow((phase - 0.24) * 14.0, 2.0));
                float beat  = saturate(peak1 + peak2);
                float pulse = lerp(1.0 - _PulseAmount, 1.0 + _PulseAmount, beat);

                float scan     = sin((i.worldPos.y * _ScanlineDensity) - (_Time.y * _ScanlineSpeed * 6.2831853)) * 0.5 + 0.5;
                float scanMask = lerp(1.0 - _ScanlineIntensity, 1.0, scan);

                half3 body = _BaseColor.rgb * scanMask * pulse;
                half3 rim  = _RimColor.rgb  * fresnel;
                half3 col  = body + rim;

                half alpha = saturate((_Alpha * scanMask + fresnel) * pulse);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

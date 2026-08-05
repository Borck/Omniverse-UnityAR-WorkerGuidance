Shader "Guidance/Hologram"
{
    // Optical-see-through waveguide hologram, tuned for the Vuzix M4000 under
    // bright workbench lighting. Waveguides are additive (display only ADDS
    // light, never subtracts), so:
    //   - Dark/dim fragments are washed out by ambient -> we keep a brightness
    //     floor everywhere and avoid any time-varying darkening.
    //   - Additive blend matches the optics: overlapping fragments accumulate,
    //     never overwrite each other dimmer.
    //   - Fresnel rim emphasises the silhouette so the part shape reads even
    //     when the interior is washed out.
    Properties
    {
        [HDR] _BaseColor    ("Base Color (HDR)",       Color) = (0, 2, 2, 1)
        [HDR] _RimColor     ("Rim Color (HDR)",        Color) = (0.8, 2.5, 2.5, 1)
        _RimPower           ("Rim Power",              Range(0.5, 8))  = 1.8
        _RimIntensity       ("Rim Intensity",          Range(0, 15))   = 6.0
        _BodyAlpha          ("Body Alpha",             Range(0, 1))    = 0.55
        _ShapeContrast      ("Body Shape Contrast",    Range(0, 1))    = 0.25
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
            Name "HologramForward"
            // UniversalForward is the LightMode URP renders. ForwardBase (the
            // old Built-in tag) is silently skipped by URP -> invisible model.
            Tags { "LightMode" = "UniversalForward" }

            // Pure additive: no source-alpha attenuation, so every fragment
            // contributes its full HDR colour to the framebuffer. Maximum
            // possible punch on the waveguide.
            Blend One One
            ZWrite Off
            // Never depth-occluded: punch through fixture overlay, near-clip,
            // anything else in the scene.
            ZTest Always
            Cull Back

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
                float  _BodyAlpha;
                float  _ShapeContrast;
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

                // Fresnel: dim at facing, bright at grazing -> silhouette glows.
                float fresnel = pow(1.0 - ndotv, _RimPower) * _RimIntensity;

                // Body brightness has a configurable floor so back-facing /
                // shadowed fragments don't drop below the waveguide's
                // washout threshold under bright workbench light.
                // _ShapeContrast = 0 -> flat body (max visibility, no shape cue)
                // _ShapeContrast = 1 -> full N.V shading (best shape cue)
                float bodyShade = lerp(1.0, ndotv, _ShapeContrast);

                half3 body = _BaseColor.rgb * bodyShade * _BodyAlpha;
                half3 rim  = _RimColor.rgb  * fresnel;
                // Pure additive (Blend One One): the final colour we return is
                // exactly what gets added to the framebuffer. Alpha is unused
                // by the blend equation but kept = 1 so debugging in editor
                // shows the same intensity it ends up at on glass.
                return half4(body + rim, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

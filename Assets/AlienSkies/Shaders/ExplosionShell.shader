// Expanding energy shell: additive fresnel rim that fades with _Progress.
// The explosion is this + the core flash + debris streaks - all code, no packs.
Shader "AlienSkies/ExplosionShell"
{
    Properties
    {
        _Color ("HDR Color", Color) = (4, 1.4, 5.2, 1)
        _RimPower ("Rim power", Float) = 2.2
        _Progress ("Progress 0..1", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            Cull Back
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color;
            float _RimPower;
            float _Progress;

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            V vert(A a)
            {
                V o;
                o.wp = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.wn = TransformObjectToWorldNormal(a.n);
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float rim = pow(1.0 - saturate(dot(normalize(i.wn), v)), _RimPower);
                float fade = saturate(1.0 - _Progress);
                return float4(_Color.rgb, rim * fade * fade);
            }
            ENDHLSL
        }
    }
}

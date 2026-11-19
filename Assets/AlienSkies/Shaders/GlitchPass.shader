// Signal-corruption overlay for hard resets (death, void). Same contract as
// ImpactFrame: camera-parented quad, ZTest Always so it draws over
// everything, dead after its life (driven by ImpactFrameFade via _Progress).
Shader "AlienSkies/GlitchPass"
{
    Properties
    {
        _Progress ("Progress 0..1", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Progress;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A a)
            {
                V o;
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.uv = a.uv;
                return o;
            }

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float4 frag(V i) : SV_Target
            {
                // Signal-corruption overlay: horizontal bands slide and flash,
                // RGB-split edges, block noise, scanlines. Fades with _Progress.
                float t = _Progress;
                float band = floor(i.uv.y * 24.0);
                float bandJit = hash(float2(band, floor(t * 20.0)));
                float slide = step(0.6, bandJit) * (bandJit - 0.5) * 0.4;

                float block = step(0.75, hash(float2(floor((i.uv.x + slide) * 16.0), band + floor(t * 12.0))));
                float scan = 0.5 + 0.5 * sin(i.uv.y * 900.0);

                float3 col = float3(0.0, 0.0, 0.0);
                col.r = block * hash(float2(band, 1.0));
                col.g = block * hash(float2(band, 2.0)) * 0.9;
                col.b = block * hash(float2(band, 3.0)) * 1.2;
                col += float3(1.0, 0.25, 0.75) * step(0.92, bandJit) * 0.8; // magenta tears

                float alpha = (block * 0.55 + 0.12 * scan) * (1.0 - t) * (1.0 - t);
                return float4(col * 2.0, alpha);
            }
            ENDHLSL
        }
    }
}

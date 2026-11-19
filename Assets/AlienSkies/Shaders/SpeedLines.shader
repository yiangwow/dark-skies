// Radial speed-line overlay: hashed angular wedges masked to an annulus
// between the screen center and its rim, additive so it reads as bright
// streaks over whatever's behind. Camera-parented quad, ZTest Always so it
// draws over everything (ImpactFrame.shader scaffolding).
Shader "AlienSkies/SpeedLines"
{
    Properties
    {
        _Intensity ("Intensity 0..1", Range(0, 1)) = 0
        // Default must match the mask's old hardcoded inner edge (0.25) so
        // an unset property reproduces today's annulus exactly (guardrail 3:
        // accel shrinks this toward the outer edge, braking never does).
        _InnerRadius ("Inner radius", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Intensity;
            float _InnerRadius;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A a)
            {
                V o;
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.uv = a.uv;
                return o;
            }

            // Not an HLSL builtin - the ImpactFrame scaffolding this is copied
            // from has none, so it's defined here.
            float hash(float n) { return frac(sin(n * 127.1) * 43758.5453); }

            float4 frag(V i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float ang = atan2(c.y, c.x);
                float r = length(c) * 2.0;

                // One hashed on/off value per angular wedge; the wedge index is
                // re-rolled every time step so streaks pop in and out as they
                // scroll outward. Full radial span within the mask band (below)
                // is what reads as a streak rather than a dash.
                float streaks = step(0.985, hash(floor(ang * 90.0) + floor(_Time.y * 8.0)));

                // Edges only - fades in away from the screen center. Inner
                // edge is a property (SpeedFeelCore.InnerRadiusFor) so it can
                // shrink under acceleration; outer edge stays fixed.
                float mask = smoothstep(_InnerRadius, 0.7, r);

                float alpha = streaks * mask * _Intensity;
                return float4((1.2).xxx, alpha);
            }
            ENDHLSL
        }
    }
}

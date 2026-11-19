// Kill flash = SIGNAL SPIKE: a 0.28s stutter in the Machine's feed — thin
// palette-fringed band tears + one scanline pop at the kill instant, fully
// transparent everywhere else (it corrupts the world, never covers it).
// Small sibling of GlitchPass (death = full signal breakdown): one glitch
// family, scaled to the size of the moment.
// Playtest round 5, 2026-08-29 ("kill flash needs to be redesigned"): the
// anime impact frame was borrowed language — pop-art FILL in a dark neon
// world — and no recolor could fix that; this is built from the game's own
// glitch DNA instead. Same contract as ever: camera-parented quad, ZTest
// Always, driven by ImpactFrameFade via _Progress.
Shader "AlienSkies/ImpactFrame"
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
                float t = _Progress;
                float fade = (1.0 - t) * (1.0 - t); // GlitchPass falloff family

                // 4 glitch frames over the life: the tear set re-rolls each
                // beat (~70ms) — reads as frames, not fizz.
                float seed = floor(t * 4.0);
                float band = floor(i.uv.y * 36.0);
                float pick = hash(float2(band, seed));
                float tear = step(0.86, pick); // ~5 of 36 bands live per frame

                // Tears span a random ~55% of the width, not the full screen.
                float xoff = hash(float2(band, seed + 3.0)) * 0.5;
                tear *= step(xoff, i.uv.x) * step(i.uv.x, xoff + 0.55);
                // Guardrail 4: the reticle zone is sacred — no tear within
                // +-6% of center height (the hitmarker lives there, and the
                // player is scanning for the NEXT threat at kill time).
                tear *= step(0.06, abs(i.uv.y - 0.5));

                // Each tear fringes in one of the signature pair.
                const float3 magenta = float3(1.00, 0.18, 0.53);
                const float3 cyan    = float3(0.22, 0.88, 1.00);
                float3 col = lerp(magenta, cyan, step(0.5, hash(float2(band, seed + 7.0))));

                // The spike: one hard scanline in the first beat only — the
                // audible click of the kill, then gone. Off-center (guardrail
                // 4): it must never sit over the reticle/hitmarker zone.
                float spike = step(abs(i.uv.y - 0.62), 0.004) * step(t, 0.22);
                col = lerp(col, magenta, spike);

                // saturate: band 17 tears deterministically at seed 0 and
                // overlaps the scanline — unclamped 1.3 alpha goes NEGATIVE
                // through OneMinusSrcAlpha on the HDR target (subtracts scene).
                float alpha = saturate((tear * 0.45 + spike * 0.85) * fade);
                return float4(col * 2.2, alpha);
            }
            ENDHLSL
        }
    }
}

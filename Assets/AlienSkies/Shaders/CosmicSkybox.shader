Shader "AlienSkies/CosmicSkybox"
{
    // Procedural TTGL-flavored sky: deep violet gradient, two-hue nebula fbm
    // concentrated near the horizon band, sparse HDR stars (bloom picks them up).
    Properties
    {
        _TopColor ("Top Color", Color) = (0.045, 0.02, 0.11, 1)
        _HorizonColor ("Horizon Color", Color) = (0.13, 0.045, 0.20, 1)
        _BottomColor ("Bottom Color", Color) = (0.008, 0.004, 0.03, 1)
        _NebulaA ("Nebula A (magenta)", Color) = (0.85, 0.16, 0.50, 1)
        _NebulaB ("Nebula B (cyan)", Color) = (0.18, 0.62, 0.90, 1)
        _NebulaIntensity ("Nebula Intensity", Range(0, 2)) = 0.55
        _StarDensity ("Star Density", Range(0, 1)) = 0.55
        _StarBrightness ("Star Brightness", Range(0, 6)) = 2.6
        _BandColor ("Galactic Band Color", Color) = (1.0, 0.55, 0.22, 1)
        _BandIntensity ("Galactic Band Intensity", Range(0, 2)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor, _HorizonColor, _BottomColor, _NebulaA, _NebulaB, _BandColor;
            float _NebulaIntensity, _StarDensity, _StarBrightness, _BandIntensity;
            // Global (NOT a material Properties entry, or per-material values would
            // shadow it) — set every frame by MusicManager from combat heat.
            float _AlienHeat;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i);
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                return 0.5333 * vnoise(p) + 0.2667 * vnoise(p * 2.03) + 0.1333 * vnoise(p * 4.09);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // vertical gradient
                float up = saturate(dir.y);
                float down = saturate(-dir.y);
                float3 col = _HorizonColor.rgb;
                col = lerp(col, _TopColor.rgb, smoothstep(0.0, 0.55, up));
                col = lerp(col, _BottomColor.rgb, smoothstep(0.0, 0.45, down));

                // nebula: two-hue fbm, concentrated in a horizon band
                float band = 1.0 - smoothstep(0.05, 0.6, abs(dir.y));
                float n = fbm(dir * 3.1);
                float mask = smoothstep(0.45, 0.78, n) * (0.35 + 0.65 * band) * _NebulaIntensity * lerp(1.0, 1.15, _AlienHeat);
                float hue = fbm(dir * 1.7 + 13.7);
                col += lerp(_NebulaA.rgb, _NebulaB.rgb, hue) * mask;

                // warm edge-on galactic band hugging the horizon (TTGL composition)
                float bandGlow = pow(saturate(1.0 - abs(dir.y)), 7.0) * _BandIntensity
                                 * (0.55 + 0.45 * fbm(dir * 2.4 + 7.7));
                col += _BandColor.rgb * bandGlow;

                // stars: one candidate per cell, sparse threshold, HDR bright
                float3 sp = dir * 70.0;
                float3 cell = floor(sp);
                float3 f = frac(sp);
                float h = hash13(cell);
                float3 starPos = float3(hash13(cell + 17.1), hash13(cell + 31.7), hash13(cell + 47.3));
                float d = length(f - starPos);
                float gate = step(1.0 - _StarDensity * 0.06, h);
                float star = smoothstep(0.10, 0.0, d) * gate;
                float tint = hash13(cell + 71.3);
                float3 starCol = lerp(float3(0.85, 0.9, 1.0), float3(1.0, 0.85, 0.75), tint);
                col += starCol * star * _StarBrightness * (0.4 + 0.6 * hash13(cell + 5.5));

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}

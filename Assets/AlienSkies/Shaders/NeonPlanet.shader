Shader "AlienSkies/NeonPlanet"
{
    // Stylized procedural planet in the neon palette: fbm continents (or gas
    // bands via _Banding) in two tones of one hue, glowing coastlines, polar
    // caps, day/night terminator, HDR atmosphere rim, slow spin. Seeded from
    // world position so every instance is a different world with one shader.
    Properties
    {
        _Color ("Hue", Color) = (0.6, 0.36, 1, 1)
        _SurfaceIntensity ("Surface Intensity", Range(0, 3)) = 1.1
        _RimIntensity ("Atmosphere Rim Intensity", Range(0, 6)) = 2.2
        _NoiseScale ("Noise Scale", Range(0.5, 10)) = 3.0
        _Banding ("Gas Banding (0 rocky, 1 giant)", Range(0, 1)) = 0.0
        _SpinSpeed ("Spin Speed", Range(0, 0.2)) = 0.02
        _Swirl ("Spiral Swirl", Range(0, 3)) = 1.4
        _SunDir ("Sun Direction", Vector) = (-0.35, 0.71, -0.61, 0)
        _NightSide ("Night Side Brightness", Range(0, 1)) = 0.16
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _SunDir;
            float _SurfaceIntensity, _RimIntensity, _NoiseScale, _Banding,
                  _SpinSpeed, _Swirl, _NightSide;
            // Global (NOT a material Properties entry, or per-material values would
            // shadow it) — set every frame by MusicManager from combat heat.
            float _AlienHeat;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 opos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float3 seed : TEXCOORD3;
            };

            v2f vert(appdata_base i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.opos = i.vertex.xyz;
                o.normal = UnityObjectToWorldNormal(i.normal);
                o.viewDir = WorldSpaceViewDir(i.vertex);
                o.seed = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13,
                                unity_ObjectToWorld._m23) * 0.137;
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
                // slow spin around the pole keeps worlds alive without motion sickness
                float a = _Time.y * _SpinSpeed;
                float s = sin(a), c = cos(a);
                float3 pn = normalize(i.opos);
                pn = float3(pn.x * c - pn.z * s, pn.y, pn.x * s + pn.z * c);

                // Gurren Lagann swirl: shear the sampling domain around the
                // pole by latitude so continents smear into spiral arms
                // instead of sitting still.
                float tw = _Swirl * pn.y;
                float sw = sin(tw), cw = cos(tw);
                float3 ps = float3(pn.x * cw - pn.z * sw, pn.y, pn.x * sw + pn.z * cw);

                // rocky: fbm blotch continents; giant: latitude bands warped by
                // fbm AND wound twice around the sphere (integer winds keep the
                // longitude seam continuous) — drill-spiral bands, not stripes.
                float rocky = fbm(ps * _NoiseScale + i.seed);
                float turb = fbm(ps * _NoiseScale * 1.4 + i.seed * 1.7);
                float ang = atan2(pn.z, pn.x);
                float giant = sin(pn.y * 7.0 + turb * 5.0 + ang * 2.0) * 0.5 + 0.5;
                float landRaw = lerp(rocky, giant, _Banding);

                float land = smoothstep(0.42, 0.58, landRaw);
                // glowing coastline: thin bright band where land meets ocean
                float coast = smoothstep(0.38, 0.5, landRaw) * (1.0 - smoothstep(0.5, 0.62, landRaw));

                float3 ocean = _Color.rgb * 0.11;
                float3 landCol = _Color.rgb * 0.95;
                float3 surface = lerp(ocean, landCol, land);
                surface += _Color.rgb * coast * 1.3 * lerp(1.0, 1.3, _AlienHeat); // coasts burn toward the hue, hotter with the music

                // polar caps: near-white, hue-kissed
                float caps = smoothstep(0.78, 0.9, abs(pn.y));
                surface = lerp(surface, lerp(float3(1, 1, 1), _Color.rgb, 0.25), caps * 0.9);

                surface *= _SurfaceIntensity;

                // day/night terminator; faint self-light on the night side
                float3 n = normalize(i.normal);
                float day = saturate(dot(n, normalize(_SunDir.xyz)) * 0.6 + 0.4);
                surface *= lerp(_NightSide, 1.0, day);

                // HDR atmosphere rim after shading so it glows on the dark limb
                float ndv = saturate(dot(n, normalize(i.viewDir)));
                float rim = pow(1.0 - ndv, 2.6);
                float3 col = surface + _Color.rgb * rim * _RimIntensity * lerp(1.0, 1.3, _AlienHeat);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}

Shader "AlienSkies/GlowOrb"
{
    // Stylized glowing planet: fbm-distorted gas bands in dark/bright tones of one
    // hue, white-hot storm cells, and an HDR atmosphere rim. Noise is seeded from
    // the object's world position, so every instance looks different with one
    // shared material. Slow swirl keeps surfaces alive. Unlit, ignores fog.
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _SurfaceIntensity ("Surface Intensity", Range(0, 4)) = 1.6
        _RimIntensity ("Atmosphere Rim Intensity", Range(0, 6)) = 1.4
        _BandFreq ("Band Frequency", Range(0, 20)) = 7.0
        _Turbulence ("Turbulence", Range(0, 3)) = 1.1
        _NoiseScale ("Noise Scale", Range(0.5, 10)) = 3.5
        _StormIntensity ("Storm Intensity", Range(0, 4)) = 1.6
        _SwirlSpeed ("Swirl Speed", Range(0, 0.5)) = 0.03
        _SunDir ("Sun Direction", Vector) = (-0.35, 0.71, -0.61, 0)
        _NightSide ("Night Side Brightness", Range(0, 1)) = 0.22
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
            float _SurfaceIntensity, _RimIntensity, _BandFreq, _Turbulence,
                  _NoiseScale, _StormIntensity, _SwirlSpeed, _NightSide;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 opos : TEXCOORD0;    // object-space position (seamless sphere coords)
                float3 normal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float3 seed : TEXCOORD3;    // per-instance world offset
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
                float3 pn = normalize(i.opos);
                float t = _Time.y * _SwirlSpeed;

                // turbulent gas bands: latitude stripes warped by fbm, drifting slowly
                float turb = fbm(pn * _NoiseScale + i.seed + float3(t, 0.0, -t));
                float bands = sin(pn.y * _BandFreq + turb * _Turbulence * 6.2832) * 0.5 + 0.5;
                // second detail octave breaks up the stripes
                float detail = fbm(pn * _NoiseScale * 2.2 + i.seed * 1.7 - float3(0.0, t, t));
                bands = saturate(bands * (0.65 + 0.55 * detail));
                float3 surface = lerp(_Color.rgb * 0.16, _Color.rgb, bands) * _SurfaceIntensity;

                // storm cells: rare hot spots that blow toward white and bloom
                float storm = smoothstep(0.68, 0.95, fbm(pn * _NoiseScale * 3.1 + i.seed * 2.3 + t));
                surface += lerp(_Color.rgb, float3(1, 1, 1), 0.55) * storm * _StormIntensity;

                // day/night shading gives the sphere 3D form; the night side stays
                // faintly self-lit because these are cosmic bodies, not rocks
                float3 n = normalize(i.normal);
                float day = saturate(dot(n, normalize(_SunDir.xyz)) * 0.6 + 0.4);
                surface *= lerp(_NightSide, 1.0, day);

                // HDR atmosphere rim added after shading so it glows on the dark limb
                float ndv = saturate(dot(n, normalize(i.viewDir)));
                float rim = pow(1.0 - ndv, 2.4);
                float3 col = surface + _Color.rgb * rim * _RimIntensity;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}

Shader "AlienSkies/NeonRim"
{
    // Flat stylized surface: dark base, HDR fresnel rim in a palette color, and
    // a soft sun-side lambert term so shapes still read as volumes. The whole
    // neon-vector art direction hangs off this one shader.
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.045, 0.045, 0.10, 1)
        _RimColor ("Rim Color", Color) = (0.6, 0.36, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimIntensity ("Rim Intensity (HDR)", Range(0, 8)) = 2.4
        _SunDir ("Sun Direction", Vector) = (-0.35, 0.71, -0.61, 0)
        _Shading ("Sun Shading Amount", Range(0, 1)) = 0.45
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

            fixed4 _BaseColor, _RimColor;
            float4 _SunDir;
            float _RimPower, _RimIntensity, _Shading;
            // Global (NOT a material Properties entry, or per-material values would
            // shadow it) — set every frame by MusicManager from combat heat.
            float _AlienHeat;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - wpos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 vd = normalize(i.viewDir);
                float ndl = saturate(dot(n, normalize(_SunDir.xyz)));
                float shade = lerp(1.0, 0.35 + 0.65 * ndl, _Shading);
                float fres = pow(1.0 - saturate(dot(n, vd)), _RimPower);
                float3 col = _BaseColor.rgb * shade + _RimColor.rgb * (fres * _RimIntensity * lerp(1.0, 1.35, _AlienHeat));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}

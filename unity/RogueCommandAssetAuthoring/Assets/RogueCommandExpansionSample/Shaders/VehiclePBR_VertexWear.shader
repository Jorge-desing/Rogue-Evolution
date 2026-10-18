Shader "RogueCommand/VehiclePBR_VertexWear"
{
    Properties
    {
        _Color ("Main Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        
        [Toggle(_METALLICGLOSSMAP)] _EnableMetallicGloss ("Enable Metallic Gloss Map", Float) = 1.0
        _MetallicGlossMap ("Metallic (R) Gloss (A)", 2D) = "white" {}
        _GlossMapScale ("Smoothness Scale", Range(0.0, 1.0)) = 1.0
        _Metallic ("Metallic Fallback", Range(0.0, 1.0)) = 0.0
        _Glossiness ("Smoothness Fallback", Range(0.0, 1.0)) = 0.5
        
        [Toggle(_NORMALMAP)] _EnableNormalMap ("Enable Normal Map", Float) = 0.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        
        [Toggle(_EMISSION)] _EnableEmission ("Enable Emission", Float) = 0.0
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Header(Vertex Wear Multipliers)]
        _AoStrength ("Cavity AO Strength (Red)", Range(0.0, 2.0)) = 1.0
        _WearStrength ("Edge Wear Strength (Green)", Range(0.0, 2.0)) = 1.0
        _SootStrength ("Thermal Soot Strength (Blue)", Range(0.0, 2.0)) = 1.0
        _TeamColor ("Team Color Tint (Alpha Mask)", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #pragma multi_compile_instancing

        #pragma shader_feature _METALLICGLOSSMAP
        #pragma shader_feature _NORMALMAP
        #pragma shader_feature _EMISSION

        #include "UnityCG.cginc"

        sampler2D _MainTex;
        #ifdef _METALLICGLOSSMAP
        sampler2D _MetallicGlossMap;
        #endif
        #ifdef _NORMALMAP
        sampler2D _BumpMap;
        #endif
        #ifdef _EMISSION
        sampler2D _EmissionMap;
        #endif

        struct Input
        {
            float2 uv_MainTex;
            fixed4 vertColor;
        };

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _TeamColor)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _EmissionColor)
            UNITY_DEFINE_INSTANCED_PROP(half, _GlossMapScale)
            UNITY_DEFINE_INSTANCED_PROP(half, _Metallic)
            UNITY_DEFINE_INSTANCED_PROP(half, _Glossiness)
            UNITY_DEFINE_INSTANCED_PROP(half, _BumpScale)
            UNITY_DEFINE_INSTANCED_PROP(half, _AoStrength)
            UNITY_DEFINE_INSTANCED_PROP(half, _WearStrength)
            UNITY_DEFINE_INSTANCED_PROP(half, _SootStrength)
        UNITY_INSTANCING_BUFFER_END(Props)

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * UNITY_ACCESS_INSTANCED_PROP(Props, _Color);

            // Decode 4-Channel Vertex Color:
            // R: Ambient Occlusion / Cavity Dirt (darkens crevices)
            // G: Curvature / Edge Wear (metallic edge scratches)
            // B: Thermal Soot (exhaust and muzzle darkening)
            // A: Team Color Mask (1.0 = tintable armor, 0.0 = fixed mechanics)
            half ao = saturate(lerp(1.0, IN.vertColor.r, UNITY_ACCESS_INSTANCED_PROP(Props, _AoStrength)));
            half wear = saturate(IN.vertColor.g * UNITY_ACCESS_INSTANCED_PROP(Props, _WearStrength));
            half soot = saturate(IN.vertColor.b * UNITY_ACCESS_INSTANCED_PROP(Props, _SootStrength));
            half teamMask = IN.vertColor.a;

            // Apply Team Color modulation where masked
            fixed3 teamTint = UNITY_ACCESS_INSTANCED_PROP(Props, _TeamColor).rgb;
            fixed3 albedo = lerp(c.rgb, c.rgb * teamTint, teamMask);

            // Apply Cavity AO and Thermal Soot
            albedo *= ao * (1.0 - soot * 0.75);

            // Bare chipped steel highlight on worn edges
            fixed3 bareMetal = fixed3(0.72, 0.74, 0.76);
            albedo = lerp(albedo, bareMetal, wear * 0.85);
            o.Albedo = albedo;

            // Metallic and Smoothness
            #ifdef _METALLICGLOSSMAP
            half4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            half metallic = mg.r;
            half smoothness = mg.a * UNITY_ACCESS_INSTANCED_PROP(Props, _GlossMapScale);
            #else
            half metallic = UNITY_ACCESS_INSTANCED_PROP(Props, _Metallic);
            half smoothness = UNITY_ACCESS_INSTANCED_PROP(Props, _Glossiness);
            #endif

            // Worn chipped edges reveal high-metallic, high-specular steel
            metallic = saturate(metallic + wear * 0.6);
            smoothness = saturate(lerp(smoothness, 0.82, wear * 0.75));

            o.Metallic = metallic;
            o.Smoothness = smoothness;

            // Normal Map
            #ifdef _NORMALMAP
            half bumpScale = UNITY_ACCESS_INSTANCED_PROP(Props, _BumpScale);
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), bumpScale);
            #endif

            // Emission
            #ifdef _EMISSION
            fixed4 emit = tex2D(_EmissionMap, IN.uv_MainTex) * UNITY_ACCESS_INSTANCED_PROP(Props, _EmissionColor);
            o.Emission = emit.rgb;
            #endif

            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Standard"
}

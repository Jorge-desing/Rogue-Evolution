Shader "RogueCommand/VehiclePBR_VertexWear"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        [MainColor] _BaseColor ("Main Tint", Color) = (1,1,1,1)

        [NoScaleOffset] _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _GlossMapScale ("Smoothness Scale", Range(0.0, 1.0)) = 1.0
        _Metallic ("Metallic Fallback", Range(0.0, 1.0)) = 0.0
        _Smoothness ("Smoothness Fallback", Range(0.0, 1.0)) = 0.5

        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}

        [Header(Vertex Wear Multipliers)]
        _AoStrength ("Cavity AO Strength (Red)", Range(0.0, 2.0)) = 1.0
        _WearStrength ("Edge Wear Strength (Green)", Range(0.0, 2.0)) = 1.0
        _SootStrength ("Thermal Soot Strength (Blue)", Range(0.0, 2.0)) = 1.0
        _TeamColor ("Team Color Tint (Alpha Mask)", Color) = (1,1,1,1)

        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _AlphaClip ("Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "UniversalMaterialType"="Lit"
            "IgnoreProjector"="True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VehicleVert
            #pragma fragment VehicleFrag

            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half4 _TeamColor;
                half _Metallic;
                half _Smoothness;
                half _GlossMapScale;
                half _BumpScale;
                half _AoStrength;
                half _WearStrength;
                half _SootStrength;
                half _Cutoff;
                half _Surface;
            CBUFFER_END

            struct VehicleAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VehicleVaryings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 vertexColor : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VehicleVaryings VehicleVert(VehicleAttributes input)
            {
                VehicleVaryings output = (VehicleVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS,
                    input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.vertexColor = input.color;
                output.vertexLighting = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);
                return output;
            }

            half4 VehicleFrag(VehicleVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
                surfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

                #if defined(_METALLICSPECGLOSSMAP)
                    half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                    surfaceData.metallic = metallicGloss.r;
                    surfaceData.smoothness = metallicGloss.a * _GlossMapScale;
                #else
                    surfaceData.metallic = _Metallic;
                    surfaceData.smoothness = _Smoothness;
                #endif

                // If the mesh has no authored vertex colors (Unity supplies all zeros), default to clean unweathered baseline (R=1 cavity AO, G=0 wear, B=0 soot, A=0 team mask)
                half4 vColor = input.vertexColor;
                if (dot(vColor, vColor) < 0.0001h)
                {
                    vColor = half4(1.0h, 0.0h, 0.0h, 0.0h);
                }

                half ao = saturate(lerp(1.0h, vColor.r, _AoStrength));
                half wear = saturate(vColor.g * _WearStrength);
                half soot = saturate(vColor.b * _SootStrength);

                // Team color modulation
                surfaceData.albedo = lerp(surfaceData.albedo,
                    surfaceData.albedo * _TeamColor.rgb, vColor.a);

                // Cavity AO and Thermal Soot darkening
                surfaceData.albedo *= ao * (1.0h - soot * 0.75h);

                // Two-tier edge wear: oxidized dark primer border (#2c2724) + bare bright alloy ridge (#c7ccd6)
                half primerMask = smoothstep(0.12h, 0.45h, wear) * (1.0h - smoothstep(0.50h, 0.85h, wear));
                half steelMask = smoothstep(0.42h, 0.90h, wear);
                surfaceData.albedo = lerp(surfaceData.albedo, half3(0.17h, 0.15h, 0.14h), primerMask * 0.85h);
                surfaceData.albedo = lerp(surfaceData.albedo, half3(0.78h, 0.80h, 0.84h), steelMask * 0.95h);

                // Physical response: Bare steel boosts metallic/smoothness; primer dulls; carbon soot extinguishes specularity
                surfaceData.metallic = saturate(surfaceData.metallic * (1.0h - primerMask * 0.5h) + steelMask * 0.85h);
                surfaceData.metallic = saturate(surfaceData.metallic - soot * 0.40h);

                surfaceData.smoothness = saturate(lerp(surfaceData.smoothness, 0.85h, steelMask * 0.8h) * (1.0h - primerMask * 0.4h));
                surfaceData.smoothness *= (1.0h - soot * 0.65h);

                surfaceData.normalTS = SampleNormal(input.uv,
                    TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
                surfaceData.emission = SampleEmission(input.uv, _EmissionColor.rgb,
                    TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));
                surfaceData.occlusion = 1.0h;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(
                    TransformTangentToWorld(surfaceData.normalTS, tangentToWorld));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = ComputeFogFactor(input.positionCS.z);
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = OutputAlpha(color.a, _Surface);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Simple Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Simple Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Simple Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Simple Lit"
}

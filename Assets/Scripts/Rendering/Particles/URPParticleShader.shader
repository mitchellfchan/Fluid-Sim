Shader "Universal Render Pipeline/Fluid/Particle Spheres"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ParticleScale ("Particle Scale", Float) = 1.0
        
        // Required buffer properties (hidden, set by renderer feature)
        [HideInInspector] _Positions ("Positions Buffer", Float) = 0
        [HideInInspector] _LocalToWorld ("Local To World Matrix", Float) = 0
        [HideInInspector] _ParticleCount ("Particle Count", Int) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
            "ShaderModel" = "4.5"
        }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // Render State
            Cull Back
            Blend One Zero
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma exclude_renderers gles gles3 glcore
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment

            // Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                uint instanceID     : SV_InstanceID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv        : TEXCOORD0;
                float4 vertex    : SV_POSITION;
                float4 color     : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Properties
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _ParticleScale;
            CBUFFER_END

            // Particle data buffers (set by CopilotParticleDisplay)
            StructuredBuffer<float3> _Positions;
            float4x4 _LocalToWorld;
            int _ParticleCount;

            Varyings UnlitPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Get the instance ID (which particle we're rendering)
                uint instanceID = input.instanceID;
                
                // Get particle position from buffer (already in world space!)
                float3 particleWorldPos = float3(0, 0, 0);
                if (instanceID < (uint)_ParticleCount)
                {
                    particleWorldPos = _Positions[instanceID];
                }
                
                // Scale the vertex position by particle scale and transform to world space
                float3 scaledVertexPos = input.positionOS.xyz * _ParticleScale;
                float3 vertexWorldOffset = mul((float3x3)_LocalToWorld, scaledVertexPos);
                
                // Final world position = particle world position + vertex offset
                float3 worldPos = particleWorldPos + vertexWorldOffset;
                
                // Transform to clip space
                output.vertex = TransformWorldToHClip(worldPos);
                
                // Pass through UV
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
                // Simple per-instance coloring for debugging
                float colorVariation = (float)(instanceID % 16) / 15.0;
                output.color = lerp(_BaseColor, _BaseColor * float4(1.0, colorVariation, 1.0 - colorVariation, 1.0), 0.3);
                
                return output;
            }

            half4 UnlitPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 color = texColor * input.color;
                
                return color;
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
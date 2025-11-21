Shader "Universal Render Pipeline/Fluid/Particle Hybrid"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ParticleScale ("Particle Scale", Float) = 1.0
        _SphereDistance ("Sphere Render Distance", Float) = 20.0
        _BillboardTexture ("Billboard Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ParticleHybrid"
            
            HLSLPROGRAM
            #pragma vertex HybridVertex
            #pragma fragment HybridFragment
            #pragma target 4.5
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            StructuredBuffer<float3> _Positions;
            float _ParticleScale;
            float _SphereDistance;
            float3 _CameraPosition;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float isBillboard : TEXCOORD1;
            };

            Varyings HybridVertex(Attributes input)
            {
                Varyings output;
                
                uint instanceID = input.instanceID;
                float3 particleWorldPos = _Positions[instanceID];
                float distanceToCamera = length(particleWorldPos - _CameraPosition);
                
                // Use billboard for distant particles
                if (distanceToCamera > _SphereDistance)
                {
                    // Billboard rendering - face camera
                    float3 cameraRight = normalize(cross(float3(0,1,0), normalize(_CameraPosition - particleWorldPos)));
                    float3 cameraUp = normalize(cross(normalize(_CameraPosition - particleWorldPos), cameraRight));
                    
                    float3 worldPos = particleWorldPos + 
                                      (input.positionOS.x * cameraRight + input.positionOS.y * cameraUp) * _ParticleScale;
                    
                    output.positionCS = TransformWorldToHClip(worldPos);
                    output.isBillboard = 1.0;
                }
                else
                {
                    // Full 3D sphere rendering
                    float3 scaledVertexPos = input.positionOS.xyz * _ParticleScale;
                    float3 worldPos = particleWorldPos + scaledVertexPos;
                    output.positionCS = TransformWorldToHClip(worldPos);
                    output.isBillboard = 0.0;
                }
                
                output.uv = input.uv;
                return output;
            }

            half4 HybridFragment(Varyings input) : SV_Target
            {
                // Different shading for billboard vs sphere
                if (input.isBillboard > 0.5)
                {
                    // Cheap billboard shading
                    float2 centered = input.uv * 2.0 - 1.0;
                    float dist = length(centered);
                    if (dist > 1.0) discard; // Circular billboard
                    
                    return half4(0.5, 0.7, 1.0, 1.0); // Simple blue
                }
                else
                {
                    // Full sphere shading with lighting
                    return half4(0.3, 0.6, 1.0, 1.0); // Darker blue for spheres
                }
            }
            
            ENDHLSL
        }
    }
}
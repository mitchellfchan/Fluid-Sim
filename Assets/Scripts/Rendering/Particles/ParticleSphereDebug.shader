Shader "Fluid/ParticleSphereDebug"
{
    Properties
    {
        [Header(Debug Properties)]
        _BaseColor ("Base Color", Color) = (1, 0, 0, 1)
        _ParticleScale ("Particle Scale", Float) = 1.0
        
        // Hidden properties set by CopilotParticleDisplay
        [HideInInspector] _Positions ("Positions Buffer", Float) = 0
        [HideInInspector] _LocalToWorld ("Local To World Matrix", Float) = 0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline"="UniversalRenderPipeline"
            "Queue"="Geometry"
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Cull Back
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            
            // Properties
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _ParticleScale;
            CBUFFER_END
            
            // Buffers set by CopilotParticleDisplay
            StructuredBuffer<float3> _Positions;
            float4x4 _LocalToWorld;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                // Get instance ID
                uint instanceID = unity_InstanceID;
                
                // Simple debug: use a bright color based on instance ID
                float t = (float)(instanceID % 100) / 99.0;
                output.color = float4(t, 1.0 - t, 0.5, 1.0);
                
                // Get particle position from buffer
                float3 particlePos = float3(0, 0, 0);
                if (instanceID < (uint)_Positions.Length)
                {
                    particlePos = _Positions[instanceID];
                }
                else
                {
                    // Debug: If we're outside buffer bounds, render at origin with red color
                    particlePos = float3(0, 0, 0);
                    output.color = float4(1, 0, 0, 1); // Red for invalid particles
                }
                
                // Scale the vertex position
                float3 scaledPosition = input.positionOS.xyz * _ParticleScale;
                
                // Position in world space: particle position + scaled vertex offset
                float3 worldPos = particlePos + scaledPosition;
                
                // Apply the fluid simulation's transform
                worldPos = mul(_LocalToWorld, float4(worldPos, 1.0)).xyz;
                
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                
                // Transform normal
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                return output;
            }
            
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Simple lighting
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 lighting = mainLight.color * NdotL * 0.8 + 0.2; // Add some ambient
                
                // Combine base color, instance color, and lighting
                float4 finalColor = _BaseColor * input.color;
                finalColor.rgb *= lighting;
                
                return finalColor;
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
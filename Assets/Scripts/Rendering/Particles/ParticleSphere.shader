Shader "Fluid/ParticleSphere"
{
    Properties
    {
        [Header(Particle Properties)]
        _BaseColor ("Base Color", Color) = (0.3, 0.6, 1.0, 0.8)
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.9
        
        [Header(Velocity Coloring)]
        _VelocityColorStrength ("Velocity Color Strength", Range(0, 2)) = 1.0
        _MaxVelocity ("Max Velocity for Color", Float) = 10.0
        _VelocityColorRamp ("Velocity Color Ramp", 2D) = "white" {}
        
        [Header(Density Effects)]
        _DensityColorStrength ("Density Color Strength", Range(0, 2)) = 0.5
        _MaxDensity ("Max Density for Color", Float) = 2.0
        
        [Header(Rendering)]
        _ParticleScale ("Particle Scale", Float) = 1.0
        
        // Hidden properties set by CopilotParticleDisplay
        [HideInInspector] _Positions ("Positions Buffer", Float) = 0
        [HideInInspector] _Velocities ("Velocities Buffer", Float) = 0
        [HideInInspector] _Densities ("Densities Buffer", Float) = 0
        [HideInInspector] _LocalToWorld ("Local To World Matrix", Float) = 0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "RenderPipeline"="UniversalRenderPipeline"
            "Queue"="Transparent"
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // URP and Unity includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            
            // Our custom particle data functions
            #include "ParticleInstanceData.hlsl"
            
            // Properties
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _VelocityColorStrength;
                float _MaxVelocity;
                float _DensityColorStrength;
                float _MaxDensity;
            CBUFFER_END
            
            TEXTURE2D(_VelocityColorRamp);
            SAMPLER(sampler_VelocityColorRamp);
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 velocity : TEXCOORD3;
                float density : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                // Transform particle vertex using our custom function
                float3 worldPos;
                TransformParticleVertex_float(input.positionOS.xyz, worldPos);
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                
                // Transform normal to world space
                float3 particleVel;
                float speed;
                GetParticleVelocity_float(particleVel, speed);
                
                // For now, use standard normal transformation
                // You could modify this to orient particles based on velocity
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // Pass through UV coordinates
                output.uv = input.uv;
                
                // Get particle data for fragment shader
                output.velocity = particleVel;
                GetParticleDensity_float(output.density);
                
                return output;
            }
            
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Base color
                float4 color = _BaseColor;
                
                // Velocity-based coloring
                float speed = length(input.velocity);
                float normalizedSpeed = saturate(speed / _MaxVelocity);
                float3 velocityColor = SAMPLE_TEXTURE2D(_VelocityColorRamp, sampler_VelocityColorRamp, float2(normalizedSpeed, 0.5)).rgb;
                color.rgb = lerp(color.rgb, velocityColor, _VelocityColorStrength * normalizedSpeed);
                
                // Density-based effects
                float normalizedDensity = saturate(input.density / _MaxDensity);
                color.rgb = lerp(color.rgb, color.rgb * 1.5, _DensityColorStrength * normalizedDensity);
                
                // Simple lighting (you can expand this)
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 lighting = mainLight.color * NdotL + unity_AmbientSky.rgb;
                
                color.rgb *= lighting;
                
                // Apply transparency based on density (optional)
                color.a *= saturate(normalizedDensity * 0.5 + 0.5);
                
                return color;
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
Shader "Fluid/ParticleCircleBillboard" {
	Properties {
		_ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.9
		_ShadowColor ("Shadow Color", Color) = (0.01, 0.02, 0.05, 1)
	}
	SubShader {

		Tags {"Queue"="Geometry" }

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"
			
			// Manually declare the main light direction (we'll set this from C#)
			float3 _MainLightDirection;
			
			StructuredBuffer<float3> Positions;
			StructuredBuffer<float3> Velocities;
			StructuredBuffer<uint> ParticleIDs;
			Texture2D<float4> ColourMap;
			Texture2D<float4> ParticleColorMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;
			int _UseParticleColors;
			int _ColorGridSize;
			int _TotalParticles; // Total number of particles for normalization

			float scale;
			float3 colour;

			float4x4 localToWorld;
			
			// Shadow properties
			float _ShadowIntensity;
			float4 _ShadowColor;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
				float3 normal : NORMAL;
				float3 viewLightDir : TEXCOORD2; // Light direction in view space
			};

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				v2f o;
				o.uv = v.texcoord;
				o.normal = v.normal;
				
				float3 centreWorld = Positions[instanceID];
				float3 objectVertPos = v.vertex * scale * 2;
				float4 viewPos = mul(UNITY_MATRIX_V, float4(centreWorld, 1)) + float4(objectVertPos, 0);
				o.pos = mul(UNITY_MATRIX_P, viewPos);

				// Use manually set light direction, transform to view space
				o.viewLightDir = normalize(mul((float3x3)UNITY_MATRIX_V, _MainLightDirection));

				// Choose color based on mode
				if (_UseParticleColors > 0)
				{
					// ColorMap mode: Map particle ID uniformly across entire texture
					uint particleID = ParticleIDs[instanceID];
					
					// Normalize particle ID to 0-1 range based on total particles
					float t = (float)particleID / (float)max(_TotalParticles - 1, 1);
					
					// Convert linear t to 2D grid coordinates that cover the whole texture
					float gridSize = (float)_ColorGridSize;
					float totalCells = gridSize * gridSize;
					
					// Map to grid cell index
					float cellIndex = t * totalCells;
					uint gridX = (uint)cellIndex % _ColorGridSize;
					uint gridY = (uint)cellIndex / _ColorGridSize;
					
					// Sample from center of grid cell
					float gridCellSize = 1.0 / gridSize;
					float halfCell = gridCellSize * 0.5;
					
					float2 uv;
					uv.x = (float)gridX * gridCellSize + halfCell;
					uv.y = (float)gridY * gridCellSize + halfCell;
					
					o.colour = ParticleColorMap.SampleLevel(linear_clamp_sampler, uv, 0).rgb;
				}
				else
				{
					// Velocity-based coloring (original behavior)
					float speed = length(Velocities[instanceID]);
					float speedT = saturate(speed / velocityMax);
					float colT = speedT;
					o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0).rgb;
				}

				return o;
			}

			float4 frag (v2f i) : SV_Target
			{
				// Convert UV from [0,1] to centered coordinates [-1,1]
				float2 centeredUV = i.uv * 2.0 - 1.0;
				
				// Calculate distance from center
				float distanceFromCenter = length(centeredUV);
				
				// Discard pixels outside the circle (radius = 1.0)
				if (distanceFromCenter > 1.0)
				{
					discard;
				}
				
				// Optional: Add soft edge falloff for smoother circles
				float alpha = 1.0 - smoothstep(0.9, 1.0, distanceFromCenter);
				
				// Calculate sphere normal at this pixel (points out of screen in view space)
				// For a billboard facing the camera, the normal in view space is:
				// (centeredUV.x, centeredUV.y, sqrt(1 - r²))
				float z = sqrt(1.0 - distanceFromCenter * distanceFromCenter);
				float3 sphereNormal = normalize(float3(centeredUV.x, centeredUV.y, z));
				
				// Simple N·L lighting with the main light
				float NdotL = dot(sphereNormal, i.viewLightDir);
				
				// Remap from [-1, 1] to a nicer lighting range
				// This creates the crescent shadow effect!
				float lightingFactor = NdotL * 0.5 + 0.5; // Map to [0, 1]
				
				// Apply shadow intensity and color
				// When lightingFactor is low (shadows), blend toward shadow color
				// When lightingFactor is high (highlights), use full color
				float shadowAmount = (1.0 - lightingFactor) * _ShadowIntensity;
				float3 litColor = lerp(i.colour, i.colour * _ShadowColor.rgb, shadowAmount);
				
				return float4(litColor, alpha);
			}

			ENDCG
		}
	}
}
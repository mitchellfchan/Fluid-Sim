using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid.Simulation
{

	public class Spawner3D : MonoBehaviour
	{
		public int particleSpawnDensity = 600;
		public float3 initialVel;
		public float jitterStrength;
		public bool showSpawnBounds;
		public SpawnRegion[] spawnRegions;

		[Header("Debug Info")] public int debug_num_particles;
		public float debug_spawn_volume;
		
		[Header("Particle Colors")]
		public bool useRandomColors = true;
		public Color[] colorPalette = new Color[]
		{
			new Color(0.2f, 0.6f, 1.0f, 1.0f), // Blue
			new Color(0.0f, 0.8f, 0.4f, 1.0f), // Green
			new Color(1.0f, 0.4f, 0.2f, 1.0f), // Orange
			new Color(0.8f, 0.2f, 0.8f, 1.0f), // Purple
			new Color(1.0f, 0.8f, 0.2f, 1.0f), // Yellow
			new Color(1.0f, 0.2f, 0.2f, 1.0f)  // Red
		};


		public SpawnData GetSpawnData()
		{
			List<float3> allPoints = new();
			List<float3> allVelocities = new();
			List<float4> allColors = new();

			foreach (SpawnRegion region in spawnRegions)
			{
				int particlesPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
				// Transform local centre to world space for spawning
				Vector3 worldCentre = transform.TransformPoint(region.centre);
				(float3[] points, float3[] velocities, float4[] colors) = SpawnCube(particlesPerAxis, worldCentre, Vector3.one * region.size);
				allPoints.AddRange(points);
				allVelocities.AddRange(velocities);
				allColors.AddRange(colors);
			}

			return new SpawnData() { points = allPoints.ToArray(), velocities = allVelocities.ToArray(), colors = allColors.ToArray() };
		}

		(float3[] p, float3[] v, float4[] c) SpawnCube(int numPerAxis, Vector3 centre, Vector3 size)
		{
			int numPoints = numPerAxis * numPerAxis * numPerAxis;
			float3[] points = new float3[numPoints];
			float3[] velocities = new float3[numPoints];
			float4[] colors = new float4[numPoints];

			// Use seeded random for consistent colors
			System.Random seededRandom = new System.Random(42); // Fixed seed for consistent colors
			int i = 0;

			for (int x = 0; x < numPerAxis; x++)
			{
				for (int y = 0; y < numPerAxis; y++)
				{
					for (int z = 0; z < numPerAxis; z++)
					{
						float tx = x / (numPerAxis - 1f);
						float ty = y / (numPerAxis - 1f);
						float tz = z / (numPerAxis - 1f);

						float px = (tx - 0.5f) * size.x + centre.x;
						float py = (ty - 0.5f) * size.y + centre.y;
						float pz = (tz - 0.5f) * size.z + centre.z;
						float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
						
						points[i] = new float3(px, py, pz) + jitter;
						velocities[i] = initialVel;
						
						// Generate random color for this particle
						if (useRandomColors && colorPalette.Length > 0)
						{
							Color randomColor = colorPalette[seededRandom.Next(0, colorPalette.Length)];
							colors[i] = new float4(randomColor.r, randomColor.g, randomColor.b, randomColor.a);
						}
						else
						{
							colors[i] = new float4(1, 1, 1, 1); // Default white color
						}
						
						i++;
					}
				}
			}

			return (points, velocities, colors);
		}



		void OnValidate()
		{
			debug_spawn_volume = 0;
			debug_num_particles = 0;

			if (spawnRegions != null)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					debug_spawn_volume += region.Volume;
					int numPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
					debug_num_particles += numPerAxis * numPerAxis * numPerAxis;
				}
			}
		}

		void OnDrawGizmos()
		{
			if (showSpawnBounds && !Application.isPlaying)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					Gizmos.color = region.debugDisplayCol;
					// Transform local centre to world space for gizmo drawing
					Vector3 worldCentre = transform.TransformPoint(region.centre);
					Gizmos.DrawWireCube(worldCentre, Vector3.one * region.size);
				}
			}
		}

		[System.Serializable]
		public struct SpawnRegion
		{
			public Vector3 centre;
			public float size;
			public Color debugDisplayCol;

			public float Volume => size * size * size;

			public int CalculateParticleCountPerAxis(int particleDensity)
			{
				int targetParticleCount = (int)(Volume * particleDensity);
				int particlesPerAxis = (int)Math.Cbrt(targetParticleCount);
				return particlesPerAxis;
			}
		}

		public struct SpawnData
		{
			public float3[] points;
			public float3[] velocities;
			public float4[] colors;
		}
	}
}
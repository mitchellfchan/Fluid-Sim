using System;
using UnityEngine;
 using UnityEngine.InputSystem;
using Seb.GPUSorting;
using Unity.Mathematics;
using System.Collections.Generic;
using Seb.Helpers;
using static Seb.Helpers.ComputeHelper;


namespace Seb.Fluid.Simulation
{
	public class MFCFluidSim : MonoBehaviour, IFluidSimulation
	{
		public event Action<MFCFluidSim> SimulationInitCompleted;

		[Header("Time Step")] public float normalTimeScale = 1;
		public float slowTimeScale = 0.1f;
		public float maxTimestepFPS = 60; // if time-step dips lower than this fps, simulation will run slower (set to 0 to disable)
		public int iterationsPerFrame = 3;

		[Header("Simulation Settings")] public float gravity = -10;
		public float smoothingRadius = 0.2f;
		public float targetDensity = 630;
		public float pressureMultiplier = 288;
		public float nearPressureMultiplier = 2.15f;
		public float viscosityStrength = 0;
		[Range(0, 1)] public float collisionDamping = 0.95f;

	[Header("Force Zone System")]
	[Range(1, 32)] public int maxForceZones = 8;
	[SerializeField] private List<ForceZone> forceZones = new List<ForceZone>();

	// Private force zone system data
	private ComputeBuffer forceZoneBuffer;
	private GPUForceZone[] gpuForceZones;
	private Vector3[] forceZoneLastPositions;
	private Vector3[] forceZoneVelocities;
	private Vector3[] forceZoneLastRotations;
	private Vector3[] forceZoneAngularVelocities;
	private bool forceZoneFirstFrame = true;

			[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("References")] public ComputeShader compute;
		public Spawner3D spawner;

		[HideInInspector] public RenderTexture DensityMap;
		public Vector3 Scale => transform.localScale;

		// Buffers
	
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer predictedPositionsBuffer;
		public ComputeBuffer debugBuffer { get; private set; }
		public ComputeBuffer particleIDBuffer { get; private set; }

		ComputeBuffer sortTarget_positionBuffer;
		ComputeBuffer sortTarget_velocityBuffer;
		ComputeBuffer sortTarget_predictedPositionsBuffer;
		ComputeBuffer sortTarget_particleIDBuffer;

		// Kernel IDs
		const int externalForcesKernel = 0;
		const int spatialHashKernel = 1;
		const int reorderKernel = 2;
		const int reorderCopybackKernel = 3;
		const int densityKernel = 4;
		const int pressureKernel = 5;
		const int viscosityKernel = 6;
		const int updatePositionsKernel = 7;
		const int renderKernel = 8;
		const int foamUpdateKernel = 9;
		const int foamReorderCopyBackKernel = 10;

		SpatialHash spatialHash;

		// State
		bool isPaused;
		bool pauseNextFrame;
		float smoothRadiusOld;
		float simTimer;
		bool inSlowMode;
		Spawner3D.SpawnData spawnData;
		Dictionary<ComputeBuffer, string> bufferNameLookup;

		void Start()
		{
		isPaused = false;

		Initialize();
	}		void Initialize()
		{
			spawnData = spawner.GetSpawnData();
			int numParticles = spawnData.points.Length;

			spatialHash = new SpatialHash(numParticles);
			
			// Create buffers
			positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			densityBuffer = CreateStructuredBuffer<float2>(numParticles);
			particleIDBuffer = CreateStructuredBuffer<uint>(numParticles);

			debugBuffer = CreateStructuredBuffer<float3>(numParticles);

			sortTarget_positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_particleIDBuffer = CreateStructuredBuffer<uint>(numParticles);

			bufferNameLookup = new Dictionary<ComputeBuffer, string>
			{
				{ positionBuffer, "Positions" },
				{ predictedPositionsBuffer, "PredictedPositions" },
				{ velocityBuffer, "Velocities" },
				{ densityBuffer, "Densities" },
				{ particleIDBuffer, "ParticleIDs" },
				{ spatialHash.SpatialKeys, "SpatialKeys" },
				{ spatialHash.SpatialOffsets, "SpatialOffsets" },
				{ spatialHash.SpatialIndices, "SortedIndices" },
				{ sortTarget_positionBuffer, "SortTarget_Positions" },
				{ sortTarget_predictedPositionsBuffer, "SortTarget_PredictedPositions" },
				{ sortTarget_velocityBuffer, "SortTarget_Velocities" },
				{ sortTarget_particleIDBuffer, "SortTarget_ParticleIDs" },
				{ debugBuffer, "Debug" }
			};

			// Set buffer data
			SetInitialBufferData(spawnData);

			// External forces kernel
			SetBuffers(compute, externalForcesKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				predictedPositionsBuffer,
				velocityBuffer
			});

			// Spatial hash kernel
			SetBuffers(compute, spatialHashKernel, bufferNameLookup, new ComputeBuffer[]
			{
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				predictedPositionsBuffer,
				spatialHash.SpatialIndices
			});

			// Reorder kernel
			SetBuffers(compute, reorderKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				particleIDBuffer,
				sortTarget_particleIDBuffer,
				spatialHash.SpatialIndices
			});

			// Reorder copyback kernel
			SetBuffers(compute, reorderCopybackKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				particleIDBuffer,
				sortTarget_particleIDBuffer,
				spatialHash.SpatialIndices
			});

			// Density kernel
			SetBuffers(compute, densityKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets
			});

			// Pressure kernel
			SetBuffers(compute, pressureKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				debugBuffer
			});

			// Viscosity kernel
			SetBuffers(compute, viscosityKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets
			});

			// Update positions kernel
			SetBuffers(compute, updatePositionsKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				velocityBuffer
			});

			// Render to 3d tex kernel
			SetBuffers(compute, renderKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
			});

			




			compute.SetInt("numParticles", positionBuffer.count);
	

			UpdateSmoothingConstants();

			// Run single frame of sim with deltaTime = 0 to initialize density texture
			// (so that display can work even if paused at start)
			if (renderToTex3D)
			{
				RunSimulationFrame(0);
			}

			SimulationInitCompleted?.Invoke(this);
		}

		
		void Update()
		{
			// Run simulation
			if (!isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity; // If framerate dips too low, run the simulation slower than real-time
				float dt = Mathf.Min(Time.deltaTime * ActiveTimeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (pauseNextFrame)
			{
				isPaused = true;
				pauseNextFrame = false;
			}

			HandleInput();
		}

		void RunSimulationFrame(float frameDeltaTime)
		{
			float subStepDeltaTime = frameDeltaTime / iterationsPerFrame;
			UpdateSettings(subStepDeltaTime, frameDeltaTime);

			// Simulation sub-steps
			for (int i = 0; i < iterationsPerFrame; i++)
			{
				simTimer += subStepDeltaTime;
				RunSimulationStep();
			}



			// 3D density map
			if (renderToTex3D)
			{
				UpdateDensityMap();
			}
		}

		void UpdateDensityMap()
		{
			float maxAxis = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
			int w = Mathf.RoundToInt(transform.localScale.x / maxAxis * densityTextureRes);
			int h = Mathf.RoundToInt(transform.localScale.y / maxAxis * densityTextureRes);
			int d = Mathf.RoundToInt(transform.localScale.z / maxAxis * densityTextureRes);
			CreateRenderTexture3D(ref DensityMap, w, h, d, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat, TextureWrapMode.Clamp);
			//Debug.Log(w + " " + h + "  " + d);
			compute.SetTexture(renderKernel, "DensityMap", DensityMap);
			compute.SetInts("densityMapSize", DensityMap.width, DensityMap.height, DensityMap.volumeDepth);
			Dispatch(compute, DensityMap.width, DensityMap.height, DensityMap.volumeDepth, renderKernel);
		}

		void RunSimulationStep()
		{
			Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
			spatialHash.Run();
			
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderCopybackKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
			if (viscosityStrength != 0) Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);
		}

		void UpdateSmoothingConstants()
		{
			float r = smoothingRadius;
			float spikyPow2 = 15 / (2 * Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3 = 15 / (Mathf.PI * Mathf.Pow(r, 6));
			float spikyPow2Grad = 15 / (Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3Grad = 45 / (Mathf.PI * Mathf.Pow(r, 6));

			compute.SetFloat("K_SpikyPow2", spikyPow2);
			compute.SetFloat("K_SpikyPow3", spikyPow3);
			compute.SetFloat("K_SpikyPow2Grad", spikyPow2Grad);
			compute.SetFloat("K_SpikyPow3Grad", spikyPow3Grad);
		}

		void UpdateSettings(float stepDeltaTime, float frameDeltaTime)
		{


			if (smoothingRadius != smoothRadiusOld)
			{
				smoothRadiusOld = smoothingRadius;
				UpdateSmoothingConstants();
			}

			Vector3 simBoundsSize = transform.localScale;
			Vector3 simBoundsCentre = transform.position;

			compute.SetFloat("deltaTime", stepDeltaTime);
			compute.SetFloat("whiteParticleDeltaTime", frameDeltaTime);
			compute.SetFloat("simTime", simTimer);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("collisionDamping", collisionDamping);
			compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetVector("boundsSize", simBoundsSize);
			compute.SetVector("centre", simBoundsCentre);

		compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
		compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

		UpdateForceZones();
		UploadForceZonesToGPU();
	}
	private void UpdateForceZones()
	{
		// Initialize tracking arrays on first call
		if (forceZoneLastPositions == null || forceZoneLastPositions.Length != maxForceZones)
		{
			forceZoneLastPositions = new Vector3[maxForceZones];
			forceZoneVelocities = new Vector3[maxForceZones];
			forceZoneLastRotations = new Vector3[maxForceZones];
			forceZoneAngularVelocities = new Vector3[maxForceZones];
		}
		
		float deltaTime = Time.deltaTime;
		
		for (int i = 0; i < forceZones.Count; i++)
		{
			var zone = forceZones[i];
			
	// Update position and rotation from transform
	if (zone.transform != null)
	{
		Vector3 currentPosition = zone.transform.position;
		zone.position = currentPosition;
		zone.rotation = zone.transform.rotation;
		
		// ===== CALCULATE VELOCITY =====
		if (!forceZoneFirstFrame && deltaTime > 0)
		{
			forceZoneVelocities[i] = (currentPosition - forceZoneLastPositions[i]) / deltaTime;
		}
		else
		{
			forceZoneVelocities[i] = Vector3.zero;
		}
		zone.velocity = forceZoneVelocities[i];
		forceZoneLastPositions[i] = currentPosition;
		
		// ===== CALCULATE ANGULAR VELOCITY =====
		Vector3 currentRotation = zone.transform.eulerAngles;
		if (!forceZoneFirstFrame && deltaTime > 0)
		{
			Vector3 rotationDiff = currentRotation - forceZoneLastRotations[i];
			
			// Handle angle wrapping
			if (rotationDiff.x > 180f) rotationDiff.x -= 360f;
			if (rotationDiff.x < -180f) rotationDiff.x += 360f;
			if (rotationDiff.y > 180f) rotationDiff.y -= 360f;
			if (rotationDiff.y < -180f) rotationDiff.y += 360f;
			if (rotationDiff.z > 180f) rotationDiff.z -= 360f;
			if (rotationDiff.z < -180f) rotationDiff.z += 360f;
			
			forceZoneAngularVelocities[i] = rotationDiff * Mathf.Deg2Rad / deltaTime;
		}
		else
		{
			forceZoneAngularVelocities[i] = Vector3.zero;
		}
		zone.angularVelocity = forceZoneAngularVelocities[i];
		zone.rotationCenter = currentPosition; // Use transform position as rotation center
		forceZoneLastRotations[i] = currentRotation;
		
		// ===== READ FROM SETTINGS COMPONENT IF AVAILABLE =====
		if (zone.settings != null)
		{
			// Update force properties from settings component (allows runtime editing)
			zone.forceMode = zone.settings.forceMode;
			zone.forceStrength = zone.settings.forceStrength;
			zone.vortexTwist = zone.settings.vortexTwist;
			zone.turbulenceFrequency = zone.settings.turbulenceFrequency;
			zone.turbulenceOctaves = zone.settings.turbulenceOctaves;
			zone.falloffCurve = zone.settings.falloffCurve;
			
			// Read rigid collision properties from settings
			zone.mass = zone.settings.mass;
			zone.bounciness = zone.settings.bounciness;
			zone.friction = zone.settings.friction;
			
			// Get local-space directions from settings
			zone.localForceDirection = zone.settings.forceDirection.normalized;
			zone.localVortexAxis = zone.settings.vortexAxis.normalized;
		}
		
		// ===== AUTOMATIC SCALE DETECTION =====
		Vector3 scale = zone.transform.localScale;				if (zone.shapeType == ForceZoneShape.Sphere)
				{
					// For spheres, use the largest scale component as radius multiplier
					float maxScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
					zone.radius = zone.baseRadius * maxScale;
				}
				else if (zone.shapeType == ForceZoneShape.Box)
				{
					// For boxes, apply scale directly to each dimension
					zone.size = Vector3.Scale(zone.baseSize, scale);
				}
				else if (zone.shapeType == ForceZoneShape.Cylinder || zone.shapeType == ForceZoneShape.Capsule)
				{
					// For cylinders/capsules, scale height with Y and radius with max of X/Z
					float radiusScale = Mathf.Max(scale.x, scale.z);
					zone.radius = zone.baseRadius * radiusScale;
					zone.size = new Vector3(zone.baseSize.x * scale.y, 0, 0); // Height in size.x
			}
			
		// Transform force direction and vortex axis to world space FROM LOCAL SPACE
		zone.forceDirection = zone.transform.TransformDirection(zone.localForceDirection).normalized;
		zone.vortexAxis = zone.transform.TransformDirection(zone.localVortexAxis).normalized;
	}
	
	// Update the zone in the list
	forceZones[i] = zone;
		}
		
		forceZoneFirstFrame = false;
	}

	private void UploadForceZonesToGPU()
	{
		// Safety check - ensure force zone system is initialized
		if (gpuForceZones == null)
		{
			gpuForceZones = new GPUForceZone[maxForceZones];
		}
		
		if (forceZoneBuffer == null)
		{
			forceZoneBuffer = new ComputeBuffer(maxForceZones, System.Runtime.InteropServices.Marshal.SizeOf<GPUForceZone>());
		}
		
		// Clear GPU array
		System.Array.Clear(gpuForceZones, 0, maxForceZones);
		
		// Convert active force zones to GPU format
		for (int i = 0; i < forceZones.Count; i++)
		{
			gpuForceZones[i] = GPUForceZone.FromForceZone(forceZones[i]);
		}
		
	// Upload to GPU
	forceZoneBuffer.SetData(gpuForceZones);
	compute.SetBuffer(externalForcesKernel, "ForceZones", forceZoneBuffer);
	compute.SetBuffer(updatePositionsKernel, "ForceZones", forceZoneBuffer); // Also bind to UpdatePositions for rigid collisions
	compute.SetInt("numForceZones", forceZones.Count);
	compute.SetInt("maxForceZones", maxForceZones);
	
	// Debug: Log rigid force zones
	if (Time.frameCount % 120 == 0) // Every 2 seconds at 60fps
	{
		for (int i = 0; i < forceZones.Count; i++)
		{
			var zone = forceZones[i];
			if (zone.forceMode == ForceZoneMode.RigidStatic || zone.forceMode == ForceZoneMode.RigidDynamic)
			{
				// Rigid zone detected (debug logging removed)
			}
		}
	}
}		void SetInitialBufferData(Spawner3D.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);

			// Initialize particle IDs with their original indices
			uint[] particleIDs = new uint[spawnData.points.Length];
			for (uint i = 0; i < particleIDs.Length; i++)
			{
				particleIDs[i] = i;
			}
			particleIDBuffer.SetData(particleIDs);



			debugBuffer.SetData(new float3[debugBuffer.count]);

			simTimer = 0;
		}

			void HandleInput()
	{
		if (Keyboard.current != null)
		{
			if (Keyboard.current.spaceKey.wasPressedThisFrame)
			{
				isPaused = !isPaused;
			}

			if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
			{
				isPaused = false;
				pauseNextFrame = true;
			}

			if (Keyboard.current.rKey.wasPressedThisFrame)
			{
				pauseNextFrame = true;
				SetInitialBufferData(spawnData);
				// Run single frame of sim with deltaTime = 0 to initialize density texture
				// (so that display can work even if paused at start)
				if (renderToTex3D)
				{
					RunSimulationFrame(0);
				}
			}

			if (Keyboard.current.qKey.wasPressedThisFrame)
			{
				inSlowMode = !inSlowMode;
			}
		}
	}

		private float ActiveTimeScale => inSlowMode ? slowTimeScale : normalTimeScale;

		void OnDestroy()
		{
			foreach (var kvp in bufferNameLookup)
			{
				Release(kvp.Key);
			}

	spatialHash.Release();
	forceZoneBuffer?.Release();
}

	#region Force Zone Management
		public bool AddForceZone(ForceZone zone)
		{
			if (forceZones.Count >= maxForceZones)
			{
				Debug.LogWarning($"Cannot add force zone: Maximum {maxForceZones} zones reached!");
				return false;
			}
			
			forceZones.Add(zone);
			return true;
		}

		public bool RemoveForceZone(ForceZone zone)
		{
			return forceZones.Remove(zone);
		}

		public bool RemoveForceZoneAt(int index)
		{
			if (index >= 0 && index < forceZones.Count)
			{
				forceZones.RemoveAt(index);
				return true;
			}
			return false;
		}

		public void ClearAllForceZones()
		{
			forceZones.Clear();
		}

		public ForceZone GetForceZone(int index)
		{
			if (index >= 0 && index < forceZones.Count)
				return forceZones[index];
			return default;
		}

		public int GetForceZoneCount() => forceZones.Count;
		public int GetMaxForceZones() => maxForceZones;

	public void UpdateForceZoneAt(int index, ForceZone newZone)
	{
		if (index >= 0 && index < forceZones.Count)
		{
			forceZones[index] = newZone;
		}
	}
	
	/// <summary>
	/// Refresh settings for a specific ForceZoneSettings component.
	/// Called when settings are changed in the Inspector.
	/// </summary>
	public void RefreshForceZoneSettings(ForceZoneSettings settings)
	{
		// Find any force zones that reference this settings component and mark for update
		for (int i = 0; i < forceZones.Count; i++)
		{
			var zone = forceZones[i];
			if (zone.settings == settings)
			{
				// The zone will automatically read from settings in next UpdateForceZones call
			}
		}
	}

	#endregion
		public struct FoamParticle
		{
			public float3 position;
			public float3 velocity;
			public float lifetime;
			public float scale;
		}

		void OnDrawGizmos()
		{
			// Draw Bounds
			var m = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.color = new Color(0, 1, 0, 0.5f);
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
			Gizmos.matrix = m;
		}
	}
}
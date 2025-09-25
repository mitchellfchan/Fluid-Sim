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
	public class FluidSim : MonoBehaviour
	{
		public event Action<FluidSim> SimulationInitCompleted;

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

		[Header("Collision Objects System")]
		[Range(1, 64)] public int maxCollisionObjects = 16;
		[SerializeField] private List<CollisionObject> collisionObjects = new List<CollisionObject>();

		// Private collision system data
		private ComputeBuffer collisionObjectBuffer;
		private GPUCollisionObject[] gpuCollisionObjects;
		private Vector3[] lastPositions;
		private Vector3[] velocities;
		private Vector3[] lastRotations;     // Track rotation changes
		private Vector3[] angularVelocities; // Calculated angular velocities
		private bool isFirstFrame = true;

		[Header("Foam Settings")] public bool foamActive;
		public int maxFoamParticleCount = 1000;
		public float trappedAirSpawnRate = 70;
		public float spawnRateFadeInTime = 0.5f;
		public float spawnRateFadeStartTime = 0;
		public Vector2 trappedAirVelocityMinMax = new(5, 25);
		public Vector2 foamKineticEnergyMinMax = new(15, 80);
		public float bubbleBuoyancy = 1.5f;
		public int sprayClassifyMaxNeighbours = 5;
		public int bubbleClassifyMinNeighbours = 15;
		public float bubbleScale = 0.5f;
		public float bubbleChangeScaleSpeed = 7;

		[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("References")] public ComputeShader compute;
		public Spawner3D spawner;

		[HideInInspector] public RenderTexture DensityMap;
		public Vector3 Scale => transform.localScale;

		// Buffers
		public ComputeBuffer foamBuffer { get; private set; }
		public ComputeBuffer foamSortTargetBuffer { get; private set; }
		public ComputeBuffer foamCountBuffer { get; private set; }
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer predictedPositionsBuffer;
		public ComputeBuffer debugBuffer { get; private set; }

		ComputeBuffer sortTarget_positionBuffer;
		ComputeBuffer sortTarget_velocityBuffer;
		ComputeBuffer sortTarget_predictedPositionsBuffer;

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
			Debug.Log("Controls: Space = Play/Pause, Q = SlowMode, R = Reset");
			isPaused = false;

			Initialize();
			InitializeCollisionSystem();
		}

		void Initialize()
		{
			spawnData = spawner.GetSpawnData();
			int numParticles = spawnData.points.Length;

			spatialHash = new SpatialHash(numParticles);
			
			// Create buffers
			positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			densityBuffer = CreateStructuredBuffer<float2>(numParticles);
			foamBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamSortTargetBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamCountBuffer = CreateStructuredBuffer<uint>(4096);
			debugBuffer = CreateStructuredBuffer<float3>(numParticles);

			sortTarget_positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer = CreateStructuredBuffer<float3>(numParticles);

			bufferNameLookup = new Dictionary<ComputeBuffer, string>
			{
				{ positionBuffer, "Positions" },
				{ predictedPositionsBuffer, "PredictedPositions" },
				{ velocityBuffer, "Velocities" },
				{ densityBuffer, "Densities" },
				{ spatialHash.SpatialKeys, "SpatialKeys" },
				{ spatialHash.SpatialOffsets, "SpatialOffsets" },
				{ spatialHash.SpatialIndices, "SortedIndices" },
				{ sortTarget_positionBuffer, "SortTarget_Positions" },
				{ sortTarget_predictedPositionsBuffer, "SortTarget_PredictedPositions" },
				{ sortTarget_velocityBuffer, "SortTarget_Velocities" },
				{ foamCountBuffer, "WhiteParticleCounters" },
				{ foamBuffer, "WhiteParticles" },
				{ foamSortTargetBuffer, "WhiteParticlesCompacted" },
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
				foamBuffer,
				foamCountBuffer,
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

			// Foam update kernel
			SetBuffers(compute, foamUpdateKernel, bufferNameLookup, new ComputeBuffer[]
			{
				foamBuffer,
				foamCountBuffer,
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				foamSortTargetBuffer,
				//debugBuffer
			});


			// Foam reorder copyback kernel
			SetBuffers(compute, foamReorderCopyBackKernel, bufferNameLookup, new ComputeBuffer[]
			{
				foamBuffer,
				foamSortTargetBuffer,
				foamCountBuffer,
			});

			compute.SetInt("numParticles", positionBuffer.count);
			compute.SetInt("MaxWhiteParticleCount", maxFoamParticleCount);

			UpdateSmoothingConstants();

			// Run single frame of sim with deltaTime = 0 to initialize density texture
			// (so that display can work even if paused at start)
			if (renderToTex3D)
			{
				RunSimulationFrame(0);
			}

			SimulationInitCompleted?.Invoke(this);
		}

		private void InitializeCollisionSystem()
		{
			// Create collision object buffer
			gpuCollisionObjects = new GPUCollisionObject[maxCollisionObjects];
			collisionObjectBuffer = new ComputeBuffer(maxCollisionObjects, System.Runtime.InteropServices.Marshal.SizeOf<GPUCollisionObject>());
			
			// Initialize tracking arrays
			InitializeCollisionObjectTracking();
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

			// Foam and spray particles
			if (foamActive)
			{
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamUpdateKernel);
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamReorderCopyBackKernel);
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

			// Foam settings
			float fadeInT = (spawnRateFadeInTime <= 0) ? 1 : Mathf.Clamp01((simTimer - spawnRateFadeStartTime) / spawnRateFadeInTime);
			compute.SetVector("trappedAirParams", new Vector3(trappedAirSpawnRate * fadeInT * fadeInT, trappedAirVelocityMinMax.x, trappedAirVelocityMinMax.y));
			compute.SetVector("kineticEnergyParams", foamKineticEnergyMinMax);
			compute.SetFloat("bubbleBuoyancy", bubbleBuoyancy);
			compute.SetInt("sprayClassifyMaxNeighbours", sprayClassifyMaxNeighbours);
			compute.SetInt("bubbleClassifyMinNeighbours", bubbleClassifyMinNeighbours);
			compute.SetFloat("bubbleScaleChangeSpeed", bubbleChangeScaleSpeed);
			compute.SetFloat("bubbleScale", bubbleScale);



			UpdateCollisionObjects();
			UploadCollisionObjectsToGPU();
		}

		
	private void UpdateCollisionObjects()
	{
		float deltaTime = Time.deltaTime;
		
		for (int i = 0; i < collisionObjects.Count; i++)
		{
			var obj = collisionObjects[i];
			
			// Update position from transform
			if (obj.transform != null)
			{
				Vector3 currentPosition = obj.transform.position;
				obj.position = currentPosition;
				
				// Calculate velocity
				if (!isFirstFrame && deltaTime > 0)
				{
					velocities[i] = (currentPosition - lastPositions[i]) / deltaTime;
				}
				else
				{
					velocities[i] = Vector3.zero;
				}
				
				obj.velocity = velocities[i];
				lastPositions[i] = currentPosition;
				
				// Calculate angular velocity from rotation changes
				Vector3 currentRotation = obj.transform.eulerAngles;
				obj.rotation = currentRotation;
				
				if (!isFirstFrame && deltaTime > 0)
				{
					// Calculate rotation difference (handling angle wrapping)
					Vector3 rotationDiff = currentRotation - lastRotations[i];
					
					// Handle angle wrapping (e.g., 350° to 10° = 20° change, not -340°)
					if (rotationDiff.x > 180f) rotationDiff.x -= 360f;
					if (rotationDiff.x < -180f) rotationDiff.x += 360f;
					if (rotationDiff.y > 180f) rotationDiff.y -= 360f;
					if (rotationDiff.y < -180f) rotationDiff.y += 360f;
					if (rotationDiff.z > 180f) rotationDiff.z -= 360f;
					if (rotationDiff.z < -180f) rotationDiff.z += 360f;
					
					// Convert to radians/second
					angularVelocities[i] = rotationDiff * Mathf.Deg2Rad / deltaTime;
				}
				else
				{
					angularVelocities[i] = Vector3.zero;
				}
				
				obj.angularVelocity = angularVelocities[i];
				lastRotations[i] = currentRotation;
				
				// ===== AUTOMATIC SCALE DETECTION =====
				Vector3 scale = obj.transform.localScale;
				
				if (obj.shapeType == CollisionShape.Sphere)
				{
					// For spheres, use the largest scale component as radius multiplier
					float maxScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
					obj.radius = obj.baseRadius * maxScale;
				}
				else if (obj.shapeType == CollisionShape.Box)
				{
					// For boxes, apply scale directly to each dimension
					obj.size = Vector3.Scale(obj.baseSize, scale);
					
					// Update rotation for boxes
					obj.rotation = obj.transform.eulerAngles;
				}
				else if (obj.shapeType == CollisionShape.Cylinder)
				{
					// For cylinders, scale height with Y and radius with max of X/Z
					float radiusScale = Mathf.Max(scale.x, scale.z);
					obj.radius = obj.baseRadius * radiusScale;
					obj.size = new Vector3(obj.baseSize.x * scale.y, 0, 0); // Height in size.x
					
					// Update rotation for cylinders
					obj.rotation = obj.transform.eulerAngles;
				}
				else if (obj.shapeType == CollisionShape.Capsule)
				{
					// For capsules, scale height with Y and radius with max of X/Z
					float radiusScale = Mathf.Max(scale.x, scale.z);
					obj.radius = obj.baseRadius * radiusScale;
					obj.size = new Vector3(obj.baseSize.x * scale.y, 0, 0); // Height in size.x
					
					// Update rotation for capsules (important for pinball paddles!)
					obj.rotation = obj.transform.eulerAngles;
				}
			}
			
			// Update the object in the list
			collisionObjects[i] = obj;
		}
		
		isFirstFrame = false;
	}

	private void UploadCollisionObjectsToGPU()
	{
		// Clear GPU array
		System.Array.Clear(gpuCollisionObjects, 0, maxCollisionObjects);
		
		// Convert active collision objects to GPU format
		for (int i = 0; i < collisionObjects.Count; i++)
		{
			gpuCollisionObjects[i] = GPUCollisionObject.FromCollisionObject(collisionObjects[i]);
		}
		
		// Upload to GPU
		collisionObjectBuffer.SetData(gpuCollisionObjects);
		compute.SetBuffer(externalForcesKernel, "CollisionObjects", collisionObjectBuffer);
		compute.SetBuffer(updatePositionsKernel, "CollisionObjects", collisionObjectBuffer);
		compute.SetInt("numCollisionObjects", collisionObjects.Count);
		compute.SetInt("maxCollisionObjects", maxCollisionObjects);
	}

		void SetInitialBufferData(Spawner3D.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);

			foamBuffer.SetData(new FoamParticle[foamBuffer.count]);

			debugBuffer.SetData(new float3[debugBuffer.count]);
			foamCountBuffer.SetData(new uint[foamCountBuffer.count]);
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
			collisionObjectBuffer?.Release();
		}

		#region Collision Object Management
		public bool AddCollisionObject(CollisionObject obj)
		{
			if (collisionObjects.Count >= maxCollisionObjects)
			{
				Debug.LogWarning($"Cannot add collision object: Maximum {maxCollisionObjects} objects reached!");
				return false;
			}
			
			collisionObjects.Add(obj);
			InitializeCollisionObjectTracking();
			return true;
		}

		public bool RemoveCollisionObject(CollisionObject obj)
		{
			bool removed = collisionObjects.Remove(obj);
			if (removed)
			{
				InitializeCollisionObjectTracking();
			}
			return removed;
		}

		public bool RemoveCollisionObjectAt(int index)
		{
			if (index >= 0 && index < collisionObjects.Count)
			{
				collisionObjects.RemoveAt(index);
				InitializeCollisionObjectTracking();
				return true;
			}
			return false;
		}

		public void ClearAllCollisionObjects()
		{
			collisionObjects.Clear();
			InitializeCollisionObjectTracking();
		}

		public CollisionObject GetCollisionObject(int index)
		{
			if (index >= 0 && index < collisionObjects.Count)
				return collisionObjects[index];
			return default;
		}

		public int GetCollisionObjectCount() => collisionObjects.Count;
		public int GetMaxCollisionObjects() => maxCollisionObjects;
		
		public void UpdateCollisionObjectAt(int index, CollisionObject newObj)
		{
			if (index >= 0 && index < collisionObjects.Count)
			{
				collisionObjects[index] = newObj;
			}
		}

		private void InitializeCollisionObjectTracking()
		{
			// Resize tracking arrays if needed
			if (lastPositions == null || lastPositions.Length != maxCollisionObjects)
			{
				lastPositions = new Vector3[maxCollisionObjects];
				velocities = new Vector3[maxCollisionObjects];
				lastRotations = new Vector3[maxCollisionObjects];
				angularVelocities = new Vector3[maxCollisionObjects];
			}
			
			// Initialize positions and rotations for new objects
			for (int i = 0; i < collisionObjects.Count; i++)
			{
				if (collisionObjects[i].transform != null)
				{
					lastPositions[i] = collisionObjects[i].transform.position;
					velocities[i] = Vector3.zero;
					lastRotations[i] = collisionObjects[i].transform.eulerAngles;
					angularVelocities[i] = Vector3.zero;
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
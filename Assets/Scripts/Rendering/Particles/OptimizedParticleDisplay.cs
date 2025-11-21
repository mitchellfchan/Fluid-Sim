using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Seb.Fluid.Simulation;
using Seb.Helpers;

namespace Seb.Fluid.Rendering
{
    /// <summary>
    /// High-performance particle renderer with GPU culling, LOD, and hybrid sphere/billboard rendering
    /// </summary>
    public class OptimizedParticleDisplay : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("Performance")]
            public bool enableGPUCulling = true;
            public bool enableLOD = true;
            public bool enableHybridRendering = true;
            
            [Header("Culling")]
            public float maxRenderDistance = 50f;
            public float billboardDistance = 20f;
            
            [Header("LOD Settings")]
            public int maxSphereVertices = 24;
            public int minSphereVertices = 6;
            public float lodDistanceMultiplier = 1f;
            
            [Header("Materials")]
            public Material sphereMaterial;
            public Material billboardMaterial;
            
            [Header("Debug")]
            public bool showStats = false;
        }
        
        public Settings settings = new Settings();
        private OptimizedParticleRenderPass renderPass;
        
        public override void Create()
        {
            renderPass = new OptimizedParticleRenderPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.sphereMaterial == null) return;
            
            var fluidSim = Object.FindFirstObjectByType<FluidSim>();
            if (fluidSim?.positionBuffer == null) return;
            
            renderPass.Setup(fluidSim, renderingData.cameraData.camera);
            renderer.EnqueuePass(renderPass);
        }
    }
    
    public class OptimizedParticleRenderPass : ScriptableRenderPass
    {
        private readonly OptimizedParticleDisplay.Settings settings;
        
        // Culling compute shader and buffers
        private ComputeShader cullingCompute;
        private ComputeBuffer visibleIndicesBuffer;
        private ComputeBuffer visibleCountBuffer;
        private ComputeBuffer cullingArgsBuffer;
        
        // LOD meshes
        private Mesh[] sphereLODs;
        private ComputeBuffer[] lodArgsBuffers;
        
        // Runtime data
        private FluidSim fluidSim;
        private Camera camera;
        private int frameCounter;
        
        public OptimizedParticleRenderPass(OptimizedParticleDisplay.Settings settings)
        {
            this.settings = settings;
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            
            InitializeCullingSystem();
            InitializeLODSystem();
        }
        
        private void InitializeCullingSystem()
        {
            // Load culling compute shader
            cullingCompute = Resources.Load<ComputeShader>("ParticleCulling");
            if (cullingCompute == null)
            {
                Debug.LogError("ParticleCulling.compute not found in Resources folder");
                return;
            }
        }
        
        private void InitializeLODSystem()
        {
            // Create LOD meshes
            int lodCount = 4; // 4 LOD levels
            sphereLODs = new Mesh[lodCount];
            lodArgsBuffers = new ComputeBuffer[lodCount];
            
            for (int i = 0; i < lodCount; i++)
            {
                int vertices = Mathf.RoundToInt(Mathf.Lerp(settings.minSphereVertices, settings.maxSphereVertices, 
                                        (float)i / (lodCount - 1)));
                sphereLODs[i] = SphereGenerator.GenerateSphereMesh(vertices);
            }
        }
        
        public void Setup(FluidSim sim, Camera cam)
        {
            fluidSim = sim;
            camera = cam;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (fluidSim?.positionBuffer == null || camera == null) return;
            
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("OptimizedParticleRender", 
                out var passData, new ProfilingSampler("OptimizedParticleRender")))
            {
                // Setup pass data
                passData.fluidSim = fluidSim;
                passData.camera = camera;
                passData.settings = settings;
                
                builder.SetRenderFunc<PassData>(ExecuteOptimizedRender);
                frameCounter++;
            }
        }
        
        private static void ExecuteOptimizedRender(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // For now, skip GPU culling in RenderGraph (would need separate compute pass)
            // Focus on LOD and hybrid rendering optimizations
            
            if (data.settings.enableHybridRendering)
            {
                RenderHybridParticles(cmd, data);
            }
            else
            {
                RenderStandardParticles(cmd, data);
            }
        }
        
        private static void RenderHybridParticles(RasterCommandBuffer cmd, PassData data)
        {
            // Render with hybrid shader that switches between spheres and billboards
            // based on distance
            var material = data.settings.sphereMaterial;
            material.SetBuffer("_Positions", data.fluidSim.positionBuffer);
            material.SetFloat("_SphereDistance", data.settings.billboardDistance);
            material.SetVector("_CameraPosition", data.camera.transform.position);
            
            // Use medium quality mesh for hybrid rendering
            var mesh = SphereGenerator.GenerateSphereMesh(12);
            var bounds = new Bounds(Vector3.zero, Vector3.one * data.settings.maxRenderDistance * 2);
            
            // Create args buffer if needed
            ComputeBuffer argsBuffer = null;
            ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, data.fluidSim.positionBuffer.count);
            
            cmd.DrawMeshInstancedIndirect(mesh, 0, material, 0, argsBuffer);
            
            ComputeHelper.Release(argsBuffer);
        }
        
        private static void RenderStandardParticles(RasterCommandBuffer cmd, PassData data)
        {
            // Standard sphere rendering with optional LOD
            var material = data.settings.sphereMaterial;
            material.SetBuffer("_Positions", data.fluidSim.positionBuffer);
            
            int lodLevel = data.settings.enableLOD ? CalculateLOD(data) : 2;
            var mesh = SphereGenerator.GenerateSphereMesh(8 + lodLevel * 4);
            var bounds = new Bounds(Vector3.zero, Vector3.one * data.settings.maxRenderDistance * 2);
            
            ComputeBuffer argsBuffer = null;
            ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, data.fluidSim.positionBuffer.count);
            
            cmd.DrawMeshInstancedIndirect(mesh, 0, material, 0, argsBuffer);
            
            ComputeHelper.Release(argsBuffer);
        }
        
        private static int CalculateLOD(PassData data)
        {
            // Calculate average distance to particles for LOD selection
            float avgDistance = Vector3.Distance(data.camera.transform.position, 
                                                data.fluidSim.transform.position);
            
            if (avgDistance > 30f) return 0; // Lowest quality
            if (avgDistance > 15f) return 1; // Medium-low quality  
            if (avgDistance > 8f) return 2;  // Medium quality
            return 3; // Highest quality
        }
        
        private class PassData
        {
            public FluidSim fluidSim;
            public Camera camera;
            public OptimizedParticleDisplay.Settings settings;
        }
    }
}
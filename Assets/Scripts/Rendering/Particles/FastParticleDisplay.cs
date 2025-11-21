using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Seb.Fluid.Simulation;
using Seb.Helpers;

namespace Seb.Fluid.Rendering
{
    /// <summary>
    /// Performance-optimized particle renderer with distance-based LOD and hybrid rendering
    /// </summary>
    public class FastParticleDisplay : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Header("Performance")]
            public bool enableDistanceCulling = true;
            public bool enableLOD = true;
            public bool enableHybridRendering = false; // TODO: Implement hybrid shader
            
            [Header("Distance Settings")]
            public float maxRenderDistance = 50f;
            public float billboardDistance = 20f;
            
            [Header("LOD Settings")]
            [Range(6, 32)] public int highQualityVertices = 24;
            [Range(6, 32)] public int mediumQualityVertices = 16;
            [Range(6, 32)] public int lowQualityVertices = 8;
            public float highQualityDistance = 10f;
            public float mediumQualityDistance = 25f;
            
            [Header("Materials & Objects")]
            public Material particleMaterial;
            public bool autoFindFluidSim = true;
            public FluidSim manualFluidSim;
            
            [Header("Rendering")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            public float particleScale = 1.0f;
            
            [Header("Debug")]
            public bool showStats = false;
        }
        
        public Settings settings = new Settings();
        private FastParticleRenderPass renderPass;
        private FluidSim cachedFluidSim;
        
        public override void Create()
        {
            renderPass = new FastParticleRenderPass(settings);
            renderPass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.particleMaterial == null) return;
            
            // Find FluidSim
            FluidSim fluidSim = null;
            if (settings.autoFindFluidSim)
            {
                if (cachedFluidSim == null || !cachedFluidSim.isActiveAndEnabled)
                {
                    cachedFluidSim = Object.FindFirstObjectByType<FluidSim>();
                }
                fluidSim = cachedFluidSim;
            }
            else
            {
                fluidSim = settings.manualFluidSim;
            }
            
            if (fluidSim?.positionBuffer == null) return;
            
            renderPass.Setup(fluidSim, renderingData.cameraData.camera);
            renderer.EnqueuePass(renderPass);
        }
    }
    
    public class FastParticleRenderPass : ScriptableRenderPass
    {
        private readonly FastParticleDisplay.Settings settings;
        
        // LOD meshes - cached for performance
        private Mesh highQualityMesh;
        private Mesh mediumQualityMesh;
        private Mesh lowQualityMesh;
        
        // Args buffers for each LOD level
        private ComputeBuffer highQualityArgs;
        private ComputeBuffer mediumQualityArgs;
        private ComputeBuffer lowQualityArgs;
        
        private FluidSim fluidSim;
        private Camera camera;
        private int lastParticleCount = -1;
        
        public FastParticleRenderPass(FastParticleDisplay.Settings settings)
        {
            this.settings = settings;
            InitializeLODMeshes();
        }
        
        private void InitializeLODMeshes()
        {
            highQualityMesh = SphereGenerator.GenerateSphereMesh(settings.highQualityVertices);
            mediumQualityMesh = SphereGenerator.GenerateSphereMesh(settings.mediumQualityVertices);
            lowQualityMesh = SphereGenerator.GenerateSphereMesh(settings.lowQualityVertices);
        }
        
        public void Setup(FluidSim sim, Camera cam)
        {
            fluidSim = sim;
            camera = cam;
            
            // Update args buffers if particle count changed
            if (sim.positionBuffer.count != lastParticleCount)
            {
                UpdateArgsBuffers(sim.positionBuffer.count);
                lastParticleCount = sim.positionBuffer.count;
            }
        }
        
        private void UpdateArgsBuffers(int particleCount)
        {
            // Release old buffers
            ComputeHelper.Release(highQualityArgs);
            ComputeHelper.Release(mediumQualityArgs);
            ComputeHelper.Release(lowQualityArgs);
            
            // Create new args buffers
            ComputeHelper.CreateArgsBuffer(ref highQualityArgs, highQualityMesh, particleCount);
            ComputeHelper.CreateArgsBuffer(ref mediumQualityArgs, mediumQualityMesh, particleCount);
            ComputeHelper.CreateArgsBuffer(ref lowQualityArgs, lowQualityMesh, particleCount);
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (fluidSim?.positionBuffer == null || camera == null) return;
            
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("FastParticleRender", 
                out var passData, new ProfilingSampler("FastParticleRender")))
            {
                passData.fluidSim = fluidSim;
                passData.camera = camera;
                passData.settings = settings;
                passData.renderPass = this;
                
                builder.SetRenderFunc<PassData>(ExecuteFastRender);
            }
        }
        
        private static void ExecuteFastRender(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            var pass = data.renderPass;
            
            // Calculate distance from camera to fluid simulation center
            float distanceToFluid = Vector3.Distance(data.camera.transform.position, 
                                                    data.fluidSim.transform.position);
            
            // Skip rendering if too far
            if (data.settings.enableDistanceCulling && distanceToFluid > data.settings.maxRenderDistance)
            {
                return;
            }
            
            // Setup material properties
            var material = data.settings.particleMaterial;
            material.SetBuffer("_Positions", data.fluidSim.positionBuffer);
            material.SetFloat("_ParticleScale", data.settings.particleScale);
            material.SetInt("_ParticleCount", data.fluidSim.positionBuffer.count);
            
            // Set transform matrix (remove scale like the working version)
            Vector3 originalScale = data.fluidSim.transform.localScale;
            data.fluidSim.transform.localScale = Vector3.one;
            Matrix4x4 localToWorld = data.fluidSim.transform.localToWorldMatrix;
            data.fluidSim.transform.localScale = originalScale;
            material.SetMatrix("_LocalToWorld", localToWorld);
            
            // Choose LOD based on distance
            Mesh meshToRender;
            ComputeBuffer argsToUse;
            
            if (data.settings.enableLOD)
            {
                if (distanceToFluid <= data.settings.highQualityDistance)
                {
                    // Close - high quality
                    meshToRender = pass.highQualityMesh;
                    argsToUse = pass.highQualityArgs;
                }
                else if (distanceToFluid <= data.settings.mediumQualityDistance)
                {
                    // Medium distance - medium quality
                    meshToRender = pass.mediumQualityMesh;
                    argsToUse = pass.mediumQualityArgs;
                }
                else
                {
                    // Far - low quality
                    meshToRender = pass.lowQualityMesh;
                    argsToUse = pass.lowQualityArgs;
                }
            }
            else
            {
                // No LOD - use medium quality
                meshToRender = pass.mediumQualityMesh;
                argsToUse = pass.mediumQualityArgs;
            }
            
            // Render the particles
            if (meshToRender != null && argsToUse != null)
            {
                cmd.DrawMeshInstancedIndirect(meshToRender, 0, material, 0, argsToUse);
                
                // Debug stats
                if (data.settings.showStats)
                {
                    int triangles = meshToRender.triangles.Length / 3;
                    Debug.Log($"FastParticleDisplay: Rendering {data.fluidSim.positionBuffer.count} particles " +
                             $"at distance {distanceToFluid:F1}m with {triangles} triangles each " +
                             $"({data.fluidSim.positionBuffer.count * triangles} total triangles)");
                }
            }
        }
        
        public void Cleanup()
        {
            ComputeHelper.Release(highQualityArgs);
            ComputeHelper.Release(mediumQualityArgs);
            ComputeHelper.Release(lowQualityArgs);
        }
        
        private class PassData
        {
            public FluidSim fluidSim;
            public Camera camera;
            public FastParticleDisplay.Settings settings;
            public FastParticleRenderPass renderPass;
        }
    }
}
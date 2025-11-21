using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Seb.Fluid.Simulation;
using Seb.Helpers;

/// <summary>
/// URP Renderer Feature for rendering fluid particles as spheres using Shadergraph materials.
/// Uses the latest URP RenderGraph API (2025) with native render pass compiler support.
/// Compatible with AddRasterRenderPass for optimal performance and integration.
/// Automatically finds FluidSim components in the scene and renders particles with custom materials.
/// </summary>
namespace Seb.Fluid.Rendering
{
    public class CopilotParticleRenderPass : ScriptableRenderPass
    {
        private const string k_PassName = "CopilotParticleRenderPass";
        private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(k_PassName);
        
        private FluidSim fluidSim;
        private Material sphereMaterial;
        private Mesh sphereMesh;
        private ComputeBuffer argsBuffer;
        private float particleScale;
        
        public void Setup(FluidSim sim, Material material, float scale)
        {
            fluidSim = sim;
            sphereMaterial = material;
            particleScale = scale;
            
            // Create sphere mesh if needed
            if (sphereMesh == null)
            {
                sphereMesh = SphereGenerator.GenerateSphereMesh(16); // Medium quality sphere
            }
            
            // Create args buffer for indirect rendering
            if (sim != null && sim.positionBuffer != null && sphereMesh != null)
            {
                // Always recreate the args buffer to ensure it matches current particle count
                ComputeHelper.Release(argsBuffer);
                ComputeHelper.CreateArgsBuffer(ref argsBuffer, sphereMesh, sim.positionBuffer.count);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (fluidSim == null || sphereMaterial == null || argsBuffer == null) return;
            
            // Get camera and universal resource data
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            
            // Only render in game view and scene view
            if (cameraData.cameraType != CameraType.Game && 
                cameraData.cameraType != CameraType.SceneView)
                return;

            // Use AddRasterRenderPass for the native render pass compiler
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(k_PassName, out var passData, m_ProfilingSampler))
            {
                // Set pass data properties directly on the out parameter
                passData.fluidSim = fluidSim;
                passData.material = sphereMaterial;
                passData.mesh = sphereMesh;
                passData.argsBuffer = argsBuffer;
                passData.scale = particleScale;
                
                // Set render targets - preserve existing content with ReadWrite access
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                
                builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                {
                    ExecuteParticleRendering(passData, context);
                });
            }
        }

        private void ExecuteParticleRendering(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // Set up material properties for particle rendering
            if (data.material != null && data.fluidSim != null)
            {
                // Ensure we have valid buffers before rendering
                if (data.fluidSim.positionBuffer == null || data.fluidSim.positionBuffer.count == 0)
                    return;
                
                // Validate that we have a proper mesh
                if (data.mesh == null)
                    return;
                
                // Ensure args buffer exists and has correct particle count
                if (data.argsBuffer == null)
                {
                    Debug.LogError("CopilotParticleDisplay: Args buffer is null - particles will not render");
                    return;
                }
                
                // Set particle data buffers
                data.material.SetBuffer("_Positions", data.fluidSim.positionBuffer);
                if (data.fluidSim.velocityBuffer != null)
                    data.material.SetBuffer("_Velocities", data.fluidSim.velocityBuffer);
                if (data.fluidSim.densityBuffer != null)
                    data.material.SetBuffer("_Densities", data.fluidSim.densityBuffer);
                
                // Set scale and other properties
                data.material.SetFloat("_ParticleScale", data.scale);
                
                // Set transform matrix (remove scale component like ParticleDisplay3D does)
                Vector3 originalScale = data.fluidSim.transform.localScale;
                data.fluidSim.transform.localScale = Vector3.one;
                Matrix4x4 localToWorld = data.fluidSim.transform.localToWorldMatrix;
                data.fluidSim.transform.localScale = originalScale;
                data.material.SetMatrix("_LocalToWorld", localToWorld);
                
                // Set particle count for bounds checking in shader
                data.material.SetInt("_ParticleCount", data.fluidSim.positionBuffer.count);
                
                // Render particles using instanced indirect rendering via command buffer
                cmd.DrawMeshInstancedIndirect(
                    data.mesh, 
                    0, 
                    data.material, 
                    0,  // submesh index
                    data.argsBuffer
                );
            }
        }
        
        public void Cleanup()
        {
            ComputeHelper.Release(argsBuffer);
        }
        
        private class PassData
        {
            public FluidSim fluidSim;
            public Material material;
            public Mesh mesh;
            public ComputeBuffer argsBuffer;
            public float scale;
        }
    }

    [System.Serializable]
    public class CopilotParticleSettings
    {
        [Header("Particle Appearance")]
        [Tooltip("Material to use for rendering particles (should be a Shadergraph material)")]
        public Material particleMaterial;
        
        [Tooltip("Scale multiplier for particle spheres")]
        [Range(0.1f, 5.0f)]
        public float particleScale = 1.0f;
        
        [Tooltip("Sphere mesh resolution (higher = better quality, lower performance)")]
        [Range(8, 32)]
        public int sphereResolution = 16;
        
        [Header("Rendering")]
        [Tooltip("When to render particles in the pipeline")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        
        [Tooltip("Auto-find FluidSim in scene")]
        public bool autoFindFluidSim = true;
        
        [Tooltip("Manual FluidSim assignment (only used if auto-find is disabled)")]
        public FluidSim manualFluidSim;
    }

    public class CopilotParticleDisplay : ScriptableRendererFeature
    {
        [SerializeField] private CopilotParticleSettings settings = new CopilotParticleSettings();
        
        private CopilotParticleRenderPass particlePass;
        private FluidSim cachedFluidSim;
        private Mesh sphereMesh;

        public override void Create()
        {
            particlePass = new CopilotParticleRenderPass();
            particlePass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Skip if no material assigned
            if (settings.particleMaterial == null)
            {
                return;
            }

            FluidSim fluidSim = null;

            // Determine which FluidSim to use
            if (settings.autoFindFluidSim)
            {
                // Auto-find FluidSim in the scene
                if (cachedFluidSim == null || !cachedFluidSim.isActiveAndEnabled)
                {
                    cachedFluidSim = Object.FindFirstObjectByType<FluidSim>();
                }
                fluidSim = cachedFluidSim;
            }
            else
            {
                // Use manually assigned FluidSim
                fluidSim = settings.manualFluidSim;
            }

            // Early exit if no FluidSim found/assigned or not ready
            if (fluidSim == null || !fluidSim.isActiveAndEnabled || fluidSim.positionBuffer == null)
            {
                return;
            }

            // Setup and enqueue the render pass
            particlePass.Setup(fluidSim, settings.particleMaterial, settings.particleScale);
            renderer.EnqueuePass(particlePass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            // Update sphere mesh resolution if changed
            if (sphereMesh == null)
            {
                sphereMesh = SphereGenerator.GenerateSphereMesh(settings.sphereResolution);
            }
        }

        protected override void Dispose(bool disposing)
        {
            particlePass?.Cleanup();
            base.Dispose(disposing);
        }
    }
}
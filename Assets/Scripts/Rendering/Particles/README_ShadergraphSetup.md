# Fluid Particle Shadergraph Setup Guide

## Overview
This guide shows how to create a Shadergraph material that works with the `CopilotParticleDisplay` renderer feature.

## Method 1: Use the Pre-built Shader (Recommended)
1. **Use the provided shader**: `ParticleSphere.shader` is ready to use
2. **Assign the material**: Use `FluidParticleMaterial.mat` in your CopilotParticleDisplay settings
3. **Customize properties**: Adjust colors, velocity effects, and density in the material inspector

## Method 2: Create Your Own Shadergraph

### Step 1: Create New Shadergraph
1. Create a new **Lit Shader Graph** (not Unlit)
2. Set **Surface** to `Transparent`
3. Set **Blend Mode** to `Alpha`
4. Enable **Support VFX Graph** (for instancing support)

### Step 2: Add Custom Function Nodes

#### A. Particle Position (Vertex Shader)
1. Add **Custom Function** node
2. Set **Name**: `TransformParticleVertex`
3. Set **File**: `ParticleInstanceData.hlsl`
4. **Inputs**: 
   - `ObjectPosition` (Vector3) - Connect from **Object Position** node
5. **Outputs**:
   - `WorldPosition` (Vector3) - Connect to **Vertex Position**

#### B. Particle Velocity (Fragment Shader)
1. Add **Custom Function** node  
2. Set **Name**: `GetParticleVelocity`
3. Set **File**: `ParticleInstanceData.hlsl`
4. **Outputs**:
   - `Velocity` (Vector3)
   - `Speed` (Float) - Use for coloring effects

#### C. Particle Density (Fragment Shader)
1. Add **Custom Function** node
2. Set **Name**: `GetParticleDensity` 
3. Set **File**: `ParticleInstanceData.hlsl`
4. **Outputs**:
   - `Density` (Float) - Use for transparency/color effects

### Step 3: Setup Material Properties
Add these properties to your Shadergraph:

```hlsl
// Required by CopilotParticleDisplay (will be set automatically)
_Positions          // StructuredBuffer<float3> 
_Velocities         // StructuredBuffer<float3>
_Densities          // StructuredBuffer<float>
_ParticleScale      // Float
_LocalToWorld       // Matrix4x4

// Optional customization properties
_BaseColor          // Color
_VelocityColorStrength  // Float
_MaxVelocity        // Float  
_DensityColorStrength   // Float
_MaxDensity         // Float
```

### Step 4: Connect the Nodes

#### Vertex Shader:
- **Object Position** → `TransformParticleVertex` → **Position**

#### Fragment Shader:
- `GetParticleVelocity.Speed` → Use for velocity-based coloring
- `GetParticleDensity.Density` → Use for density-based effects
- Connect colors and effects to **Base Color** and **Alpha**

### Step 5: Example Color Mixing
```
Speed = GetParticleVelocity.Speed
NormalizedSpeed = Speed / MaxVelocity
VelocityColor = Sample Texture 2D (using NormalizedSpeed as UV.x)
FinalColor = Lerp(BaseColor, VelocityColor, VelocityColorStrength)
```

## Required Files Structure
```
/Particles/
├── ParticleInstanceData.hlsl      # Custom HLSL functions
├── ParticleSphere.shader          # Pre-built shader (optional)
├── FluidParticleMaterial.mat      # Pre-built material (optional)
├── VelocityColorRamp.bytes        # Velocity color gradient texture
└── YourCustomShader.shadergraph   # Your Shadergraph (if creating custom)
```

## Usage with CopilotParticleDisplay
1. Add **CopilotParticleDisplay** to your URP Renderer Asset
2. Assign your material to **Particle Material** 
3. Adjust **Particle Scale** as needed
4. The renderer feature will automatically:
   - Find FluidSim components
   - Set buffer properties (`_Positions`, `_Velocities`, `_Densities`)
   - Set transform matrix (`_LocalToWorld`)
   - Set particle scale (`_ParticleScale`)

## Tips
- Use **Speed** from velocity for dynamic coloring
- Use **Density** for transparency effects (higher density = more opaque)
- The particle scale affects the mesh size, not the shader scale
- Enable GPU Instancing for best performance
- Use transparent rendering queue for realistic fluid appearance
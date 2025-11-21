# URP Billboard Particle Renderer

A custom URP-compatible particle renderer for the Fluid Simulation project that renders each particle as a flat circular billboard that always faces the camera.

## Features

- **URP Compatible**: Built specifically for Unity's Universal Render Pipeline
- **Billboard Rendering**: Particles always face the camera for optimal performance
- **Circular Masking**: Particles are rendered as perfect circles with configurable radius
- **Random Colors**: Each particle gets a random color from a configurable palette
- **High Performance**: Uses GPU instancing for efficient rendering
- **Easy Setup**: Helper script for automatic configuration

## Files Created

1. **URPBillboardParticle.shader** - URP shader for billboard particles
2. **URPBillboardParticleRenderer.cs** - URP ScriptableRendererFeature
3. **URPBillboardParticleMaterial.mat** - Default material
4. **BillboardParticleSetup.cs** - Helper script for easy setup

## Setup Instructions

### Method 1: Automatic Setup (Recommended)

1. Add the `BillboardParticleSetup` component to any GameObject in your scene
2. The component will automatically:
   - Find your URP Asset
   - Locate the Billboard Particle Renderer
   - Create a material if needed
   - Configure all settings

### Method 2: Manual Setup

1. **Add Renderer Feature to URP Asset**:
   - Open your URP Asset (usually in Project Settings > Graphics)
   - Go to the Renderer Data
   - Add Renderer Feature > URP Billboard Particle Renderer

2. **Configure the Renderer**:
   - Set the Billboard Material (use the provided material or create your own)
   - Adjust particle scale and circle radius
   - Configure color palette for random colors

3. **Create Material** (if needed):
   - Create new Material
   - Set Shader to "Universal Render Pipeline/Fluid/Billboard Particles"
   - Configure properties as needed

## Configuration Options

### BillboardParticleSettings

- **Particle Scale**: Size multiplier for billboard particles (0.1 - 5.0)
- **Circle Radius**: Radius of the circular billboard (0.1 = small circle, 1.0 = full quad)
- **Use Random Colors**: Enable/disable random color assignment
- **Color Palette**: Array of colors to randomly assign to particles
- **Render Pass Event**: When to render particles in the pipeline

### Shader Properties

- **_BaseMap**: Texture for the billboard (optional)
- **_BaseColor**: Base color tint
- **_ParticleScale**: Scale multiplier
- **_CircleRadius**: Circle radius for masking

## How It Works

1. **Billboard Calculation**: Each particle is rendered as a quad that always faces the camera
2. **Circular Masking**: The shader uses UV coordinates to create a perfect circle mask
3. **Color Assignment**: Random colors are assigned at initialization time and stored in a compute buffer
4. **GPU Instancing**: Uses `DrawMeshInstancedIndirect` for efficient rendering of thousands of particles

## Performance Notes

- **Transparent Rendering**: Particles are rendered in the transparent queue
- **No Depth Write**: Disabled for proper transparency blending
- **Billboard Efficiency**: Always faces camera, no complex geometry
- **GPU Instancing**: Efficient rendering of many particles

## Troubleshooting

### Particles Not Visible
- Check that the URP Billboard Particle Renderer is added to your URP Asset
- Verify the material is assigned and uses the correct shader
- Ensure the FluidSim component is active and has particles

### Colors Not Working
- Make sure "Use Random Colors" is enabled in the renderer settings
- Check that the color palette has colors assigned
- Verify the color buffer is being created properly

### Performance Issues
- Reduce particle scale for better performance
- Lower the circle radius to reduce overdraw
- Consider using fewer particles or LOD systems

## Integration with Fluid Simulation

The billboard renderer automatically:
- Finds FluidSim components in the scene
- Uses the position buffer from the simulation
- Updates particle colors when the simulation resets
- Handles particle count changes dynamically

## Customization

### Custom Colors
You can modify the color palette in the renderer settings or create a custom color assignment system by modifying the `InitializeRandomColors` method.

### Custom Shaders
The shader can be extended to support:
- Texture-based particles
- Animated colors
- Size variation
- Additional visual effects

### Performance Optimization
For better performance with many particles:
- Use distance-based culling
- Implement LOD systems
- Use GPU culling for off-screen particles
- Consider using the existing `FastParticleDisplay` or `OptimizedParticleDisplay` for more advanced features

## Example Usage

```csharp
// Get the billboard renderer
var billboardRenderer = FindObjectOfType<URPBillboardParticleRenderer>();

// Configure settings
billboardRenderer.settings.particleScale = 2.0f;
billboardRenderer.settings.circleRadius = 0.3f;
billboardRenderer.settings.useRandomColors = true;

// Custom color palette
billboardRenderer.settings.colorPalette = new Color[]
{
    Color.blue,
    Color.green,
    Color.red
};
```

This system provides a simple, efficient way to render fluid particles as billboards in URP, with the flexibility to customize appearance and performance as needed.

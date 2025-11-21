namespace Seb.Fluid.Simulation
{
    /// <summary>
    /// Defines how force is applied to fluid particles within a force zone.
    /// </summary>
    public enum ForceZoneMode
    {
        /// <summary>
        /// No force applied
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Constant force in a specific direction (like a current or wind)
        /// </summary>
        Directional = 1,
        
        /// <summary>
        /// Force pushes away from or pulls toward the zone center (like gravity or explosion)
        /// </summary>
        Radial = 2,
        
        /// <summary>
        /// Force creates a spinning motion around an axis (like a whirlpool or tornado)
        /// </summary>
        Vortex = 3,
        
        /// <summary>
        /// Random noise-based forces for chaotic fluid motion
        /// </summary>
        Turbulence = 4,
        
        /// <summary>
        /// Combine directional with radial falloff (directional that weakens with distance)
        /// </summary>
        DirectionalWithFalloff = 5,
        
        /// <summary>
        /// Static rigid collision - simple bounce with no momentum transfer (like a static wall)
        /// </summary>
        RigidStatic = 6,
        
        /// <summary>
        /// Dynamic rigid collision - full physics with momentum transfer, rotation, and angular velocity (like a moving paddle)
        /// </summary>
        RigidDynamic = 7
    }
}

using UnityEngine;

namespace Seb.Fluid.Simulation
{
    /// <summary>
    /// Defines a force zone that applies forces to fluid particles.
    /// This is a data struct that holds the configuration and runtime state.
    /// Use ForceZoneSettings MonoBehaviour to attach to GameObjects.
    /// </summary>
    [System.Serializable]
    public struct ForceZone
    {
        [Header("Object Reference")]
        public Transform transform;           // Unity GameObject reference
        public ForceZoneSettings settings;    // Reference to settings component (if any)
        public bool isActive;                // Enable/disable this force zone
        
        [Header("Shape Properties")]
        public ForceZoneShape shapeType;     // What shape defines this zone
        public Vector3 size;                 // For Box: width, height, depth
        public float radius;                 // For Sphere/Cylinder/Capsule
        public float height;                 // For Cylinder/Capsule
        
        [Header("Base Shape (Pre-Scale)")]
        public Vector3 baseSize;             // Original size before scale is applied
        public float baseRadius;             // Original radius before scale is applied
        
    [Header("Force Properties")]
    public ForceZoneMode forceMode;      // Type of force to apply
    public Vector3 forceDirection;       // Direction for directional/vortex forces (WORLD SPACE - calculated at runtime)
    public float forceStrength;          // Magnitude of force
    public AnimationCurve falloffCurve;  // How force strength changes with distance (0=center, 1=edge)
    
    [Header("Vortex Properties")]
    public Vector3 vortexAxis;           // Axis of rotation for vortex mode (WORLD SPACE - calculated at runtime)
    public float vortexTwist;            // Additional radial force component (-1 to 1)
    
    [Header("Turbulence Properties")]
    public float turbulenceFrequency;    // Noise frequency for turbulence
    public float turbulenceOctaves;      // Number of noise layers
    
    [Header("Local Space Reference (Internal)")]
    public Vector3 localForceDirection;  // Force direction in local space
    public Vector3 localVortexAxis;      // Vortex axis in local space
    
    [Header("Runtime Data - Auto-Calculated")]
    public Vector3 position;             // Current world position
    public Quaternion rotation;          // Current rotation
    
    [Header("Rigid Collision Properties (for RigidStatic/RigidDynamic modes)")]
    public Vector3 velocity;             // Linear velocity (auto-calculated)
    public Vector3 angularVelocity;      // Angular velocity (auto-calculated)
    public Vector3 rotationCenter;       // Pivot point for rotation (usually same as position)
    public float mass;                   // Mass for momentum transfer
    public float bounciness;             // Restitution coefficient (0-1)
    public float friction;               // Friction coefficient (0-1)
    
        // Helper properties
        public Vector4 GetPositionAndRadius() => new Vector4(position.x, position.y, position.z, radius);
        public Vector4 GetSizeAndStrength() => new Vector4(size.x, size.y, size.z, forceStrength);
    }

    /// <summary>
    /// GPU-optimized structure for compute shader.
    /// Must match the layout in FluidSim.compute
    /// </summary>
    [System.Serializable]
    public struct GPUForceZone
    {
        public Vector3 position;             // 12 bytes
        public float radius;                 // 4 bytes
        
        public Vector3 forceDirection;       // 12 bytes
        public int shapeType;                // 4 bytes (ForceZoneShape as int)
        
        public Vector3 size;                 // 12 bytes
        public int forceMode;                // 4 bytes (ForceZoneMode as int)
        
        public float forceStrength;          // 4 bytes
        public float isActive;               // 4 bytes (bool as float)
        public float vortexTwist;            // 4 bytes
        public float turbulenceFrequency;    // 4 bytes
        
        // Rotation support
        public Matrix4x4 rotationMatrix;    // 64 bytes - full transform matrix
        public Vector3 vortexAxis;           // 12 bytes
        public float turbulenceOctaves;      // 4 bytes
        
        // Falloff curve sampling (pack 8 samples)
        public Vector4 falloffSamples0;      // 16 bytes (samples 0-3)
        public Vector4 falloffSamples1;      // 16 bytes (samples 4-7)
        
        // Rigid collision properties (for RigidStatic and RigidDynamic modes)
        public Vector3 velocity;             // 12 bytes - linear velocity
        public float mass;                   // 4 bytes - for momentum transfer
        
        public float bounciness;             // 4 bytes - restitution coefficient
        public float friction;               // 4 bytes - friction coefficient
        public Vector3 angularVelocity;      // 12 bytes - rotation speed (radians/second)
        
        public Vector3 rotationCenter;       // 12 bytes - true pivot point for rotation
        public float padding;                // 4 bytes - keep 16-byte alignment
        
        // Total: 232 bytes (16-byte aligned)
        
        /// <summary>
        /// Convert from ForceZone to GPU format
        /// </summary>
        public static GPUForceZone FromForceZone(ForceZone zone)
        {
            // Create pure rotation matrix
            Matrix4x4 rotMatrix = Matrix4x4.identity;
            if (zone.transform != null)
            {
                rotMatrix = Matrix4x4.Rotate(zone.transform.rotation);
            }
            
            // Sample falloff curve at 8 points (0.0 to 1.0)
            Vector4 falloffSamples0 = Vector4.zero;
            Vector4 falloffSamples1 = Vector4.zero;
            if (zone.falloffCurve != null && zone.falloffCurve.keys.Length > 0)
            {
                falloffSamples0.x = zone.falloffCurve.Evaluate(0.0f / 7f);
                falloffSamples0.y = zone.falloffCurve.Evaluate(1.0f / 7f);
                falloffSamples0.z = zone.falloffCurve.Evaluate(2.0f / 7f);
                falloffSamples0.w = zone.falloffCurve.Evaluate(3.0f / 7f);
                falloffSamples1.x = zone.falloffCurve.Evaluate(4.0f / 7f);
                falloffSamples1.y = zone.falloffCurve.Evaluate(5.0f / 7f);
                falloffSamples1.z = zone.falloffCurve.Evaluate(6.0f / 7f);
                falloffSamples1.w = zone.falloffCurve.Evaluate(7.0f / 7f);
            }
            else
            {
                // Default: constant strength
                falloffSamples0 = Vector4.one;
                falloffSamples1 = Vector4.one;
            }
            
            return new GPUForceZone
            {
                position = zone.position,
                radius = zone.radius,
                forceDirection = zone.forceDirection.normalized,
                shapeType = (int)zone.shapeType,
                size = zone.size,
                forceMode = (int)zone.forceMode,
                forceStrength = zone.forceStrength,
                isActive = zone.isActive ? 1f : 0f,
                vortexTwist = zone.vortexTwist,
                turbulenceFrequency = zone.turbulenceFrequency,
                rotationMatrix = rotMatrix,
                vortexAxis = zone.vortexAxis.normalized,
                turbulenceOctaves = zone.turbulenceOctaves,
                falloffSamples0 = falloffSamples0,
                falloffSamples1 = falloffSamples1,
                // Rigid collision properties (for RigidStatic and RigidDynamic modes)
                velocity = zone.velocity,
                mass = zone.mass,
                bounciness = zone.bounciness,
                friction = zone.friction,
                angularVelocity = zone.angularVelocity,
                rotationCenter = zone.rotationCenter,
                padding = 0f
            };
        }
    }

    /// <summary>
    /// Extension methods for creating force zones
    /// </summary>
    public static class ForceZoneExtensions
    {
    /// <summary>
    /// Create a directional force zone (like a water current)
    /// </summary>
    public static ForceZone CreateDirectional(Transform transform, Vector3 size, Vector3 direction, float strength)
    {
        return new ForceZone
        {
            transform = transform,
            isActive = true,
            shapeType = ForceZoneShape.Box,
            size = size,
            baseSize = size,
            forceMode = ForceZoneMode.Directional,
            localForceDirection = direction.normalized,
            forceDirection = direction.normalized,
            forceStrength = strength,
            falloffCurve = AnimationCurve.Constant(0, 1, 1) // Constant strength
        };
    }        /// <summary>
        /// Create a spherical radial force zone (like an explosion or implosion)
        /// </summary>
        public static ForceZone CreateRadial(Transform transform, float radius, float strength, bool pullToward = false)
        {
            return new ForceZone
            {
                transform = transform,
                isActive = true,
                shapeType = ForceZoneShape.Sphere,
                radius = radius,
                baseRadius = radius,
                forceMode = ForceZoneMode.Radial,
                forceStrength = pullToward ? -Mathf.Abs(strength) : Mathf.Abs(strength),
                falloffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0) // Strongest at center
            };
        }
        
        /// <summary>
        /// Create a cylindrical vortex force zone (like a whirlpool or tornado)
        /// </summary>
        public static ForceZone CreateVortex(Transform transform, float radius, float height, Vector3 axis, float strength, float twist = 0f)
        {
        return new ForceZone
        {
            transform = transform,
            isActive = true,
            shapeType = ForceZoneShape.Cylinder,
            size = new Vector3(height, 0, 0),
            baseSize = new Vector3(height, 0, 0),
            radius = radius,
            baseRadius = radius,
            forceMode = ForceZoneMode.Vortex,
            localVortexAxis = axis.normalized,
            vortexAxis = axis.normalized,
            forceStrength = strength,
            vortexTwist = twist,
            falloffCurve = AnimationCurve.Linear(0, 1, 1, 0) // Linear falloff to edge
        };
    }        /// <summary>
        /// Create a turbulence zone (chaotic random forces)
        /// </summary>
        public static ForceZone CreateTurbulence(Transform transform, Vector3 size, float strength, float frequency = 1f, float octaves = 3f)
        {
            return new ForceZone
            {
                transform = transform,
                isActive = true,
                shapeType = ForceZoneShape.Box,
                size = size,
                baseSize = size,
                forceMode = ForceZoneMode.Turbulence,
                forceStrength = strength,
                turbulenceFrequency = frequency,
                turbulenceOctaves = octaves,
                falloffCurve = AnimationCurve.Constant(0, 1, 1) // Constant throughout
            };
        }
        
        /// <summary>
        /// Create force zone from GameObject with automatic shape detection
        /// </summary>
        public static ForceZone CreateFromGameObject(GameObject gameObject)
        {
            ForceZone forceZone;
            
            // Detect collider type
            var collider = gameObject.GetComponent<Collider>();
            if (collider is SphereCollider sphere)
            {
                forceZone = CreateRadial(gameObject.transform, sphere.radius, 10f);
            }
            else if (collider is BoxCollider box)
            {
                forceZone = CreateDirectional(gameObject.transform, box.size, Vector3.forward, 10f);
            }
            else if (collider is CapsuleCollider capsule)
            {
                forceZone = CreateVortex(gameObject.transform, capsule.radius, capsule.height, Vector3.up, 10f);
            }
            else
            {
                // Default to box
                forceZone = CreateDirectional(gameObject.transform, Vector3.one, Vector3.forward, 10f);
            }
            
            // Apply custom settings if component exists
            var settings = gameObject.GetComponent<ForceZoneSettings>();
            forceZone.settings = settings;  // Store reference for runtime updates
            if (settings != null)
            {
                settings.ApplyToForceZone(ref forceZone);
            }
            
            return forceZone;
        }
    }
}

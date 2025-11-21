using UnityEngine;

namespace Seb.Fluid.Simulation
{
    /// <summary>
    /// Attach this component to GameObjects to configure how they behave as force zones.
    /// This component stores settings that will be applied when the FluidSim creates
    /// ForceZone objects from GameObjects.
    /// 
    /// Example use: Attach to a box collider to create a directional water current,
    /// or to a sphere to create a radial force field.
    /// </summary>
    [AddComponentMenu("Fluid Simulation/Force Zone Settings")]
    public class ForceZoneSettings : MonoBehaviour
    {
        [Header("Force Type")]
        public ForceZoneMode forceMode = ForceZoneMode.Directional;
        
        [Header("Basic Force Properties")]
        [Tooltip("Direction for directional forces (in local space)")]
        public Vector3 forceDirection = Vector3.forward;
        
        [Tooltip("Strength of the force applied to particles")]
        [Range(0f, 100f)]
        public float forceStrength = 10f;
        
        [Tooltip("How force strength changes with distance from center (0) to edge (1)")]
        public AnimationCurve falloffCurve = AnimationCurve.Constant(0, 1, 1);
        
        [Header("Vortex Settings (Vortex Mode Only)")]
        [Tooltip("Axis of rotation for vortex forces (in local space)")]
        public Vector3 vortexAxis = Vector3.up;
        
        [Tooltip("Additional radial component: negative pulls inward, positive pushes outward")]
        [Range(-1f, 1f)]
        public float vortexTwist = 0f;
        
        [Header("Turbulence Settings (Turbulence Mode Only)")]
        [Tooltip("Frequency of turbulence noise")]
        [Range(0.1f, 10f)]
        public float turbulenceFrequency = 1f;
        
        [Tooltip("Number of noise octaves (more = more detail)")]
        [Range(1f, 6f)]
        public float turbulenceOctaves = 3f;
        
        [Header("Rigid Collision Settings (RigidStatic/RigidDynamic Only)")]
        [Tooltip("Mass of the object for momentum transfer (higher = less affected by particles)")]
        [Range(0.1f, 100f)]
        public float mass = 1f;
        
        [Tooltip("Bounciness/restitution coefficient (0 = no bounce, 1 = perfect bounce)")]
        [Range(0f, 1f)]
        public float bounciness = 0.5f;
        
        [Tooltip("Friction coefficient (0 = no friction, 1 = maximum friction)")]
        [Range(0f, 1f)]
        public float friction = 0.9f;
        
        [Header("Shape Override (Optional)")]
        [Tooltip("Leave as None to auto-detect from collider")]
        public ForceZoneShape shapeOverride = ForceZoneShape.None;
        
        [Tooltip("Custom size (only used if shapeOverride is set and not using collider)")]
        public Vector3 customSize = Vector3.one;
        
        [Tooltip("Custom radius (for Sphere/Cylinder/Capsule)")]
        public float customRadius = 1f;
        
        [Tooltip("Custom height (for Cylinder/Capsule)")]
        public float customHeight = 2f;
        
        [Header("Visualization")]
        [Tooltip("Draw gizmos in the editor to visualize the force zone")]
        public bool showGizmos = true;
        
        [Tooltip("Color of the gizmo")]
        public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.3f);
        
    /// <summary>
    /// Apply these settings to a ForceZone
    /// </summary>
    public void ApplyToForceZone(ref ForceZone forceZone)
    {
        Debug.Log($"[ForceZoneSettings] ApplyToForceZone called: forceDirection={forceDirection}, forceStrength={forceStrength}");
        
        forceZone.forceMode = forceMode;
        
        // Store LOCAL space directions (will be transformed to world space each frame)
        forceZone.localForceDirection = forceDirection.normalized;
        forceZone.localVortexAxis = vortexAxis.normalized;
        
        // Initialize world space directions (will be updated in UpdateForceZones)
        forceZone.forceDirection = transform.TransformDirection(forceDirection.normalized);
        forceZone.vortexAxis = transform.TransformDirection(vortexAxis.normalized);
        
        Debug.Log($"[ForceZoneSettings] After transform: localDir={forceZone.localForceDirection}, worldDir={forceZone.forceDirection}");
        
        forceZone.forceStrength = forceStrength;
        forceZone.falloffCurve = falloffCurve;
        forceZone.vortexTwist = vortexTwist;
        forceZone.turbulenceFrequency = turbulenceFrequency;
        forceZone.turbulenceOctaves = turbulenceOctaves;
        
        // Initialize rigid collision properties (for RigidStatic/RigidDynamic modes)
        forceZone.mass = mass;
        forceZone.bounciness = bounciness;
        forceZone.friction = friction;
        forceZone.velocity = Vector3.zero;          // Will be calculated at runtime
        forceZone.angularVelocity = Vector3.zero;   // Will be calculated at runtime
        forceZone.rotationCenter = transform.position;  // Use transform position as pivot
        
            // Apply shape override if specified
            if (shapeOverride != ForceZoneShape.None)
            {
                forceZone.shapeType = shapeOverride;
                forceZone.size = customSize;
                forceZone.radius = customRadius;
                forceZone.height = customHeight;
            }
        }
        
        void OnDrawGizmos()
        {
            if (!showGizmos) return;
            
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            
            // Detect shape from collider or use override
            ForceZoneShape shape = shapeOverride;
            if (shape == ForceZoneShape.None)
            {
                var collider = GetComponent<Collider>();
                if (collider is SphereCollider) shape = ForceZoneShape.Sphere;
                else if (collider is BoxCollider) shape = ForceZoneShape.Box;
                else if (collider is CapsuleCollider) shape = ForceZoneShape.Capsule;
                else shape = ForceZoneShape.Box;
            }
            
            // Draw shape
            switch (shape)
            {
                case ForceZoneShape.Sphere:
                    float radius = customRadius;
                    if (GetComponent<SphereCollider>() is SphereCollider sc) radius = sc.radius;
                    Gizmos.DrawWireSphere(Vector3.zero, radius);
                    break;
                    
                case ForceZoneShape.Box:
                    Vector3 size = customSize;
                    if (GetComponent<BoxCollider>() is BoxCollider bc) size = bc.size;
                    Gizmos.DrawWireCube(Vector3.zero, size);
                    break;
                    
                case ForceZoneShape.Cylinder:
                    DrawWireCylinder(Vector3.zero, customRadius, customHeight);
                    break;
                    
                case ForceZoneShape.Capsule:
                    float capRadius = customRadius;
                    float capHeight = customHeight;
                    if (GetComponent<CapsuleCollider>() is CapsuleCollider cc)
                    {
                        capRadius = cc.radius;
                        capHeight = cc.height;
                    }
                    DrawWireCapsule(Vector3.zero, capRadius, capHeight);
                    break;
            }
            
            // Draw force direction arrow for directional forces
            if (forceMode == ForceZoneMode.Directional || forceMode == ForceZoneMode.DirectionalWithFalloff)
            {
                Gizmos.color = Color.yellow;
                Vector3 dir = forceDirection.normalized;
                Gizmos.DrawRay(Vector3.zero, dir * 0.5f);
                // Draw arrow head
                Vector3 right = Vector3.Cross(dir, Vector3.up);
                if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(dir, Vector3.right);
                right.Normalize();
                Vector3 up = Vector3.Cross(right, dir);
                Gizmos.DrawRay(dir * 0.5f, (-dir + right) * 0.15f);
                Gizmos.DrawRay(dir * 0.5f, (-dir - right) * 0.15f);
                Gizmos.DrawRay(dir * 0.5f, (-dir + up) * 0.15f);
                Gizmos.DrawRay(dir * 0.5f, (-dir - up) * 0.15f);
            }
            
            // Draw vortex axis for vortex forces
            if (forceMode == ForceZoneMode.Vortex)
            {
                Gizmos.color = Color.cyan;
                Vector3 axis = vortexAxis.normalized;
                Gizmos.DrawRay(Vector3.zero - axis * 0.5f, axis);
            }
        }
        
        void DrawWireCylinder(Vector3 center, float radius, float height)
        {
            int segments = 16;
            float halfHeight = height * 0.5f;
            
            // Draw top and bottom circles
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (i / (float)segments) * Mathf.PI * 2f;
                float angle2 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                
                Vector3 p1 = new Vector3(Mathf.Cos(angle1) * radius, halfHeight, Mathf.Sin(angle1) * radius);
                Vector3 p2 = new Vector3(Mathf.Cos(angle2) * radius, halfHeight, Mathf.Sin(angle2) * radius);
                Vector3 p3 = new Vector3(Mathf.Cos(angle1) * radius, -halfHeight, Mathf.Sin(angle1) * radius);
                Vector3 p4 = new Vector3(Mathf.Cos(angle2) * radius, -halfHeight, Mathf.Sin(angle2) * radius);
                
                Gizmos.DrawLine(center + p1, center + p2); // Top circle
                Gizmos.DrawLine(center + p3, center + p4); // Bottom circle
                
                if (i % 4 == 0)
                {
                    Gizmos.DrawLine(center + p1, center + p3); // Vertical lines
                }
            }
        }
        
        void DrawWireCapsule(Vector3 center, float radius, float height)
        {
            int segments = 16;
            float cylinderHeight = Mathf.Max(0f, height - 2f * radius);
            float halfCylinder = cylinderHeight * 0.5f;
            
            // Draw cylinder part
            DrawWireCylinder(center, radius, cylinderHeight);
            
            // Draw hemisphere caps (simplified)
            for (int i = 0; i < segments / 2; i++)
            {
                float angle1 = (i / (float)(segments / 2)) * Mathf.PI * 0.5f;
                float angle2 = ((i + 1) / (float)(segments / 2)) * Mathf.PI * 0.5f;
                
                float y1 = Mathf.Sin(angle1) * radius;
                float y2 = Mathf.Sin(angle2) * radius;
                float r1 = Mathf.Cos(angle1) * radius;
                float r2 = Mathf.Cos(angle2) * radius;
                
                // Top hemisphere
                for (int j = 0; j < segments; j++)
                {
                    float hAngle1 = (j / (float)segments) * Mathf.PI * 2f;
                    float hAngle2 = ((j + 1) / (float)segments) * Mathf.PI * 2f;
                    
                    Vector3 p1 = center + new Vector3(Mathf.Cos(hAngle1) * r1, halfCylinder + y1, Mathf.Sin(hAngle1) * r1);
                    Vector3 p2 = center + new Vector3(Mathf.Cos(hAngle2) * r1, halfCylinder + y1, Mathf.Sin(hAngle2) * r1);
                    
                    Gizmos.DrawLine(p1, p2);
                }
                
                // Bottom hemisphere
                for (int j = 0; j < segments; j++)
                {
                    float hAngle1 = (j / (float)segments) * Mathf.PI * 2f;
                    float hAngle2 = ((j + 1) / (float)segments) * Mathf.PI * 2f;
                    
                    Vector3 p1 = center + new Vector3(Mathf.Cos(hAngle1) * r1, -halfCylinder - y1, Mathf.Sin(hAngle1) * r1);
                    Vector3 p2 = center + new Vector3(Mathf.Cos(hAngle2) * r1, -halfCylinder - y1, Mathf.Sin(hAngle2) * r1);
                    
                    Gizmos.DrawLine(p1, p2);
                }
            }
    }
    
    void OnValidate()
    {
        forceDirection.Normalize();
        vortexAxis.Normalize();
        
        // Notify all FluidSim instances that this zone's settings changed
        #if UNITY_EDITOR
        NotifyFluidSimsOfChange();
        #endif
    }
    
    /// <summary>
    /// Notify all FluidSim instances in the scene that this zone's settings have changed
    /// </summary>
    private void NotifyFluidSimsOfChange()
    {
        // Find all FluidSim instances and trigger a refresh
        var fluidSims = FindObjectsByType<FluidSim>(FindObjectsSortMode.None);
        foreach (var sim in fluidSims)
        {
            sim.RefreshForceZoneSettings(this);
        }
    }
}
}
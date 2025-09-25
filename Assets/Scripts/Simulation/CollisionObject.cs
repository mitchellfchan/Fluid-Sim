using UnityEngine;

namespace Seb.Fluid.Simulation
{

    public interface ICollisionObjectManager
    {
        bool AddCollisionObject(CollisionObject obj);
        bool RemoveCollisionObject(CollisionObject obj);
        bool RemoveCollisionObjectAt(int index);
        void ClearAllCollisionObjects();
        CollisionObject GetCollisionObject(int index);
        int GetCollisionObjectCount();
        int GetMaxCollisionObjects();
    }

    [System.Serializable]
    public struct CollisionObject
    {
        [Header("Object Reference")]
        public Transform transform;           // Unity GameObject reference
        public bool isActive;                // Enable/disable this object
        
        [Header("Shape Properties")]
        public CollisionShape shapeType;     // What shape this represents
        public Vector3 size;                 // For Box: width, height, depth (scaled)
        public float radius;                 // For Sphere/Cylinder/Capsule (scaled)
        public float height;                 // For Cylinder/Capsule
        
        [Header("Base Shape (Pre-Scale)")]
        public Vector3 baseSize;             // Original size before scale is applied
        public float baseRadius;             // Original radius before scale is applied
        
        [Header("Physics Properties")]
        public float mass;                   // Relative mass for momentum transfer
        public float bounciness;             // Normal collision damping (0-1)
        public float friction;               // Tangential collision damping (0-1)
        public bool enableMomentumTransfer;  // Use advanced physics vs simple bounce
        
        [Header("Rotation Settings")]
        public bool useExternalPivot;         // Use custom pivot point instead of auto-detection
        public Transform pivotPoint;          // Custom rotation center (overrides auto-detection)
        
        [Header("Runtime Data - Auto-Calculated")]
        public Vector3 position;             // Current world position
        public Vector3 velocity;             // Current velocity (calculated each frame)
        public Vector3 rotation;             // Current rotation (Euler angles)
        public Vector3 angularVelocity;      // Current angular velocity (radians/second)
        
        // GPU-friendly data layout helpers
        public Vector4 GetPositionAndRadius() => new Vector4(position.x, position.y, position.z, radius);
        public Vector4 GetSizeAndMass() => new Vector4(size.x, size.y, size.z, mass);
        public Vector4 GetPhysicsProperties() => new Vector4(bounciness, friction, enableMomentumTransfer ? 1f : 0f, 0f);
    }

    // GPU-optimized structure for compute shader
    [System.Serializable]
    public struct GPUCollisionObject
    {
        public Vector3 position;             // 12 bytes
        public float radius;                 // 4 bytes
        
        public Vector3 velocity;             // 12 bytes  
        public int shapeType;                // 4 bytes (CollisionShape as int)
        
        public Vector3 size;                 // 12 bytes
        public float mass;                   // 4 bytes
        
        public float bounciness;             // 4 bytes
        public float friction;               // 4 bytes
        public float enableMomentumTransfer; // 4 bytes (bool as float)
        public float isActive;               // 4 bytes (bool as float)
        
        // Rotation and angular velocity support
        public Matrix4x4 rotationMatrix;    // 64 bytes - full transform matrix
        public Vector3 angularVelocity;      // 12 bytes - rotation speed (radians/second)
        public Vector3 rotationCenter;       // 12 bytes - true pivot point for rotation
        public float padding;                // 4 bytes - keep 16-byte alignment
        
        // Total: 156 bytes (GPU-friendly 16-byte alignment)
        
        // Convert from CollisionObject to GPU format
        public static GPUCollisionObject FromCollisionObject(CollisionObject obj)
        {
            // Create PURE rotation matrix (no position/scale)
            Matrix4x4 rotMatrix = Matrix4x4.identity;
            if (obj.transform != null)
            {
                // Extract only rotation from transform
                Quaternion rotation = obj.transform.rotation;
                rotMatrix = Matrix4x4.Rotate(rotation);
            }
            
            // Calculate correct rotation center
            Vector3 rotationCenter = obj.position;  // Default to object center
            
            if (obj.useExternalPivot && obj.pivotPoint != null)
            {
                // Only use external pivot when explicitly enabled
                rotationCenter = obj.pivotPoint.position;
            }
            // Otherwise, always use object center (no auto-parent detection)
            
            return new GPUCollisionObject
            {
                position = obj.position,
                radius = obj.radius,
                velocity = obj.velocity,
                shapeType = (int)obj.shapeType,
                size = obj.size,
                mass = obj.mass,
                bounciness = obj.bounciness,
                friction = obj.friction,
                enableMomentumTransfer = obj.enableMomentumTransfer ? 1f : 0f,
                isActive = obj.isActive ? 1f : 0f,
                rotationMatrix = rotMatrix,
                angularVelocity = obj.angularVelocity, // Pass calculated angular velocity
                rotationCenter = rotationCenter,       // Pass calculated rotation center
                padding = 0f
            };
        }
    }

    public static class CollisionObjectExtensions
    {
        // Create a sphere collision object
        public static CollisionObject CreateSphere(Transform transform, float radius, float mass = 1.0f)
        {
            return new CollisionObject
            {
                transform = transform,
                isActive = true,
                shapeType = CollisionShape.Sphere,
                radius = radius,
                baseRadius = radius,      // Store original size
                mass = mass,
                bounciness = 0.7f,
                friction = 0.8f,
                enableMomentumTransfer = true
            };
        }
        
        // Create a box collision object
        public static CollisionObject CreateBox(Transform transform, Vector3 size, float mass = 1.0f)
        {
            return new CollisionObject
            {
                transform = transform,
                isActive = true,
                shapeType = CollisionShape.Box,
                size = size,
                baseSize = size,          // Store original size
                mass = mass,
                bounciness = 0.7f,
                friction = 0.8f,
                enableMomentumTransfer = true
            };
        }
        
        // Create a cylinder collision object (flat caps, no rounded ends)
        public static CollisionObject CreateCylinder(Transform transform, float height, float radius, float mass = 1.0f)
        {
            return new CollisionObject
            {
                transform = transform,
                isActive = true,
                shapeType = CollisionShape.Cylinder,
                size = new Vector3(height, 0, 0),  // Use size.x for height
                baseSize = new Vector3(height, 0, 0),
                radius = radius,
                baseRadius = radius,
                mass = mass,
                bounciness = 0.7f,
                friction = 0.8f,
                enableMomentumTransfer = true
            };
        }
        
        // Create a capsule collision object (perfect for pinball paddles!)
        public static CollisionObject CreateCapsule(Transform transform, float height, float radius, float mass = 1.0f)
        {
            return new CollisionObject
            {
                transform = transform,
                isActive = true,
                shapeType = CollisionShape.Capsule,
                size = new Vector3(height, 0, 0),  // Use size.x for height
                baseSize = new Vector3(height, 0, 0),
                radius = radius,
                baseRadius = radius,
                mass = mass,
                bounciness = 0.8f,        // Paddles should be bouncy!
                friction = 0.9f,          // Low friction for good flow
                enableMomentumTransfer = true
            };
        }
        
        // Auto-detect shape from GameObject and apply custom settings
        public static CollisionObject CreateFromGameObject(GameObject gameObject)
        {
            CollisionObject collisionObj;
            
            // Create collision object based on collider type
            var collider = gameObject.GetComponent<Collider>();
            if (collider is SphereCollider sphere)
            {
                collisionObj = CreateSphere(gameObject.transform, sphere.radius);
            }
            else if (collider is BoxCollider box)
            {
                collisionObj = CreateBox(gameObject.transform, box.size);
            }
            else if (collider is CapsuleCollider capsule)
            {
                collisionObj = CreateCapsule(gameObject.transform, capsule.height, capsule.radius);
            }
            else
            {
                // Default to box
                collisionObj = CreateBox(gameObject.transform, Vector3.one);
            }
            
            // Apply custom settings if component exists
            var settings = gameObject.GetComponent<CollisionObjectSettings>();
            if (settings != null)
            {
                settings.ApplyToCollisionObject(ref collisionObj);
            }
            
            return collisionObj;
        }
    }

}
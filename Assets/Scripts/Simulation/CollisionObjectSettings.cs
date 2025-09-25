using UnityEngine;

namespace Seb.Fluid.Simulation
{
    /// <summary>
    /// Attach this component to GameObjects to configure how they behave as collision objects.
    /// This component stores settings that will be applied when the FluidSim automatically 
    /// creates CollisionObjects from GameObjects with colliders.
    /// </summary>
    [AddComponentMenu("Fluid Simulation/Collision Object Settings")]
    public class CollisionObjectSettings : MonoBehaviour
    {
        [Header("Physics Properties")]
        [Range(0.1f, 10f)]
        public float mass = 1.0f;
        
        [Range(0f, 1f)]
        public float bounciness = 0.7f;
        
        [Range(0f, 1f)]
        public float friction = 0.8f;
        
        public bool enableMomentumTransfer = true;
        
        [Header("Rotation Settings")]
        [Tooltip("Use a custom pivot point instead of auto-detection")]
        public bool useExternalPivot = false;
        
        [Tooltip("Custom rotation center (overrides auto-detection)")]
        public Transform pivotPoint;
        
        [Header("Info")]
        [Tooltip("Shows what rotation center will be used")]
        public string rotationCenterInfo = "Will be calculated at runtime";
        
        void OnValidate()
        {
            UpdateRotationCenterInfo();
        }
        
        void UpdateRotationCenterInfo()
        {
            if (useExternalPivot && pivotPoint != null)
            {
                rotationCenterInfo = $"Custom: {pivotPoint.name}";
            }
            else if (useExternalPivot && pivotPoint == null)
            {
                rotationCenterInfo = "⚠️ External pivot enabled but not assigned!";
            }
            else
            {
                rotationCenterInfo = "Object Center";
            }
        }
        
        /// <summary>
        /// Apply these settings to a CollisionObject
        /// </summary>
        public void ApplyToCollisionObject(ref CollisionObject collisionObj)
        {
            collisionObj.mass = mass;
            collisionObj.bounciness = bounciness;
            collisionObj.friction = friction;
            collisionObj.enableMomentumTransfer = enableMomentumTransfer;
            collisionObj.useExternalPivot = useExternalPivot;
            collisionObj.pivotPoint = pivotPoint;
        }
    }
}

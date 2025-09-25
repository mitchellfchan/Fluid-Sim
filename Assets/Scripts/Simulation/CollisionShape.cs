using UnityEngine;

namespace Seb.Fluid.Simulation
{
    public enum CollisionShape
    {
        None = 0,
        Sphere = 1,
        Box = 2,
        Cylinder = 3,
        Capsule = 4,     // Pinball paddle shape!
        Composite = 5    // Multiple shapes combined
    }
}
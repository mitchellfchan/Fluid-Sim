using UnityEngine;

namespace Seb.Fluid.Simulation
{
    /// <summary>
    /// Interface for fluid simulation systems. 
    /// Provides access to GPU buffers needed for rendering.
    /// </summary>
    public interface IFluidSimulation
    {
        ComputeBuffer positionBuffer { get; }
        ComputeBuffer velocityBuffer { get; }
        ComputeBuffer debugBuffer { get; }
        ComputeBuffer particleIDBuffer { get; }
    }
}

#ifndef PARTICLE_INSTANCE_DATA_INCLUDED
#define PARTICLE_INSTANCE_DATA_INCLUDED

// Particle data buffers - these will be set by the CopilotParticleDisplay renderer feature
StructuredBuffer<float3> _Positions;
StructuredBuffer<float3> _Velocities;
StructuredBuffer<float> _Densities;
float _ParticleScale;
float4x4 _LocalToWorld;

// Function to get particle position for the current instance
void GetParticlePosition_float(out float3 Position)
{
    // Get the instance ID (which particle we're rendering)
    uint instanceID = unity_InstanceID;
    
    // Read position from buffer
    if (instanceID < (uint)_Positions.Length)
    {
        Position = _Positions[instanceID];
    }
    else
    {
        Position = float3(0, 0, 0);
    }
}

// Function to get particle velocity for the current instance
void GetParticleVelocity_float(out float3 Velocity, out float Speed)
{
    uint instanceID = unity_InstanceID;
    
    if (instanceID < (uint)_Velocities.Length)
    {
        Velocity = _Velocities[instanceID];
        Speed = length(Velocity);
    }
    else
    {
        Velocity = float3(0, 0, 0);
        Speed = 0.0;
    }
}

// Function to get particle density for the current instance
void GetParticleDensity_float(out float Density)
{
    uint instanceID = unity_InstanceID;
    
    if (instanceID < (uint)_Densities.Length)
    {
        Density = _Densities[instanceID];
    }
    else
    {
        Density = 1.0;
    }
}

// Function to transform vertex position for particle instance
void TransformParticleVertex_float(float3 ObjectPosition, out float3 WorldPosition)
{
    uint instanceID = unity_InstanceID;
    
    // Get particle position
    float3 particlePos = float3(0, 0, 0);
    if (instanceID < (uint)_Positions.Length)
    {
        particlePos = _Positions[instanceID];
    }
    
    // Scale the object position by particle scale
    float3 scaledPosition = ObjectPosition * _ParticleScale;
    
    // Transform to world space: particle position + scaled vertex offset
    float3 worldPos = particlePos + scaledPosition;
    
    // Apply the fluid simulation's transform
    WorldPosition = mul(_LocalToWorld, float4(worldPos, 1.0)).xyz;
}

// Utility function to get particle scale
void GetParticleScale_float(out float Scale)
{
    Scale = _ParticleScale;
}

#endif // PARTICLE_INSTANCE_DATA_INCLUDED
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class ParticleAuthoring : MonoBehaviour
{
}

class ParticleBaker : Baker<ParticleAuthoring>
{
    public override void Bake(ParticleAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, new ParticleComponent
        {
            GridCache = Entity.Null,
            
            Velocity = float3.zero,
            DeformationDisplacement = float3x3.zero,
            DeformationGradient = float3x3.identity,
            Mass = 1f,
            LiquidDensity = 1f,
            Volume = 1f,
            hydroFactor = 0.15f,
            viscFactor = 0.02f
        });
    }
}

struct ParticleComponent : IComponentData
{
    public Entity GridCache;
    
    public float3 Position;
    public float3 Velocity;
    public float3x3 DeformationDisplacement;
    public float3x3 DeformationGradient;
    public float Volume;
    public float Mass;
    public float LiquidDensity;
    public float hydroFactor;
    public float viscFactor;
}

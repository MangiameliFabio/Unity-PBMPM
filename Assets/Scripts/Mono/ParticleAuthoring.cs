using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
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
            Displacement = float3.zero,
            DeformationDisplacement = float3x3.zero,
            DeformationGradient = float3x3.identity,
            Mass = 1f,
            LiquidDensity = 1f,
            GridMeasuredLiquidDensity = 1f,
            Volume = 1f,
            LiquidHydroFactor = 0.15f,
            LiquidViscosityFactor = 0.001f,
        });
        AddComponent(entity, new URPMaterialPropertyBaseColor
        {
            Value = new float4(1f, 1f, 1f, 1f)
        });
    }
}

struct ParticleComponent : IComponentData
{
    public Entity GridCache;

    public float3 Position;
    public float3 Velocity;
    public float3 Displacement;
    public float3x3 DeformationDisplacement;
    public float3x3 DeformationGradient;
    public float Volume;
    public float Mass;
    public float LiquidDensity;
    public float GridMeasuredLiquidDensity;
    public float LiquidHydroFactor;
    public float LiquidViscosityFactor;
}

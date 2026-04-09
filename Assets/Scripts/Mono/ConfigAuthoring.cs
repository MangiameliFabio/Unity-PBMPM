using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

class ConfigAuthoring : MonoBehaviour
{
    public GameObject particlePrefab;
    public float updateFrequency = 30f;
    public int liquidSolverIterations = 4;
}

class ConfigAuthoringBaker : Baker<ConfigAuthoring>
{
    public override void Bake(ConfigAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.None);
        AddComponent(entity, new Config
        {
            ParticlePrefab = GetEntity(authoring.particlePrefab, TransformUsageFlags.Dynamic),
            UpdateFrequency = authoring.updateFrequency,
            LiquidSolverIterations = Mathf.Max(1, authoring.liquidSolverIterations)
        });
    }
}

public struct Config : IComponentData
{
    public Entity ParticlePrefab;
    
    public float UpdateFrequency;
    public int LiquidSolverIterations;
}

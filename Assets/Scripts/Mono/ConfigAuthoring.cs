using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

class ConfigAuthoring : MonoBehaviour
{
    public GameObject particlePrefab;
}

class ConfigAuthoringBaker : Baker<ConfigAuthoring>
{
    public override void Bake(ConfigAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.None);
        AddComponent(entity, new Config
        {
            ParticlePrefab = GetEntity(authoring.particlePrefab, TransformUsageFlags.Dynamic)
        });
    }
}

public struct Config : IComponentData
{
    public Entity ParticlePrefab;
}

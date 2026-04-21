using Unity.Entities;
using UnityEngine;
class ConfigAuthoring : MonoBehaviour
{
    public GameObject particlePrefab;
    public float updateFrequency = 30f;
    public int iterationCount = 4;
    public GridInterpolationMode interpolationMode = GridInterpolationMode.QuadraticBSplineNodes;
    public bool useGridVolumePreservation = true;
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
            IterationCount = Mathf.Max(1, authoring.iterationCount),
            InterpolationMode = authoring.interpolationMode,
            UseGridVolumePreservation = authoring.useGridVolumePreservation
        });
    }
}

public struct Config : IComponentData
{
    public Entity ParticlePrefab;
    
    public float UpdateFrequency;
    public int IterationCount;
    public GridInterpolationMode InterpolationMode;
    public bool UseGridVolumePreservation;
}

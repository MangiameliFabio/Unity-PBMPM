using Unity.Entities;
using UnityEngine;
class ConfigAuthoring : MonoBehaviour
{
    public GameObject particlePrefab;
    public float updateFrequency = 30f;
    public int iterationCount = 4;
    public GridInterpolationMode interpolationMode = GridInterpolationMode.QuadraticBSplineNodes;
    public bool useGridVolumePreservation = true;
    public bool useVisualSmoothing = true;
    public float liquidHydroFactor = 0.15f;
    public float liquidViscosityFactor = 0.02f;
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
            UseGridVolumePreservation = authoring.useGridVolumePreservation,
            UseVisualSmoothing = authoring.useVisualSmoothing,
            LiquidHydroFactor = Mathf.Max(0f, authoring.liquidHydroFactor),
            LiquidViscosityFactor = Mathf.Max(0f, authoring.liquidViscosityFactor)
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
    public bool UseVisualSmoothing;
    public float LiquidHydroFactor;
    public float LiquidViscosityFactor;
}

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class SpawnShapeAuthoring : MonoBehaviour
{
    private static readonly Vector3 DefaultBoundsSize = Vector3.one;
    public const int MinSpawnAmount = 1;

    public Bounds spawnBounds = new Bounds(Vector3.zero, DefaultBoundsSize);
    public Color boundsColor = Color.cyan;
    public float liquidHydroFactor = 0.15f;
    public float liquidViscosityFactor = 0.001f;
    public Color particleAlbedo = Color.white;
    
    public int spawnAmountX = 1;
    public int spawnAmountY = 1;
    public int spawnAmountZ = 1;

    private void Reset()
    {
        EnsureVisibleBounds();
    }

    private void OnValidate()
    {
        EnsureVisibleBounds();
    }

    private void EnsureVisibleBounds()
    {
        if (spawnBounds.extents == Vector3.zero)
        {
            spawnBounds = new Bounds(spawnBounds.center, DefaultBoundsSize);
        }

        spawnAmountX = Mathf.Max(MinSpawnAmount, spawnAmountX);
        spawnAmountY = Mathf.Max(MinSpawnAmount, spawnAmountY);
        spawnAmountZ = Mathf.Max(MinSpawnAmount, spawnAmountZ);
        liquidHydroFactor = Mathf.Max(0f, liquidHydroFactor);
        liquidViscosityFactor = Mathf.Max(0f, liquidViscosityFactor);
    }

    public void ValidateRuntimeValues()
    {
        EnsureVisibleBounds();
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = boundsColor;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(spawnBounds.center, spawnBounds.size);
        Gizmos.matrix = oldMatrix;
    }

    public SpawnShapeComponent CreateSpawnShapeComponent()
    {
        int3 spawnAmount = new int3(
            math.max(MinSpawnAmount, spawnAmountX),
            math.max(MinSpawnAmount, spawnAmountY),
            math.max(MinSpawnAmount, spawnAmountZ)
        );

        float3 halfSize = (float3)spawnBounds.size * 0.5f;
        float3 localStart = new float3(
            spawnAmount.x > 1 ? -halfSize.x : 0f,
            spawnAmount.y > 1 ? -halfSize.y : 0f,
            spawnAmount.z > 1 ? -halfSize.z : 0f
        );

        float3 localStep = new float3(
            spawnAmount.x > 1 ? spawnBounds.size.x / (spawnAmount.x - 1) : 0f,
            spawnAmount.y > 1 ? spawnBounds.size.y / (spawnAmount.y - 1) : 0f,
            spawnAmount.z > 1 ? spawnBounds.size.z / (spawnAmount.z - 1) : 0f
        );

        return new SpawnShapeComponent
        {
            SpawnAmount = spawnAmount,
            LocalCenter = spawnBounds.center,
            LocalStart = localStart,
            LocalStep = localStep,
            GlobalPosition = transform.position,
            LiquidHydroFactor = liquidHydroFactor,
            LiquidViscosityFactor = liquidViscosityFactor,
            ParticleAlbedo = (Vector4)particleAlbedo
        };
    }
}

public class SpawnShapeAuthoringBaker : Baker<SpawnShapeAuthoring>
{
    public override void Bake(SpawnShapeAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.CreateSpawnShapeComponent());
        AddComponent(entity, new SpawnShapeAuthoringReference
        {
            AuthoringInstanceId = authoring.GetInstanceID()
        });
    }
}

public struct SpawnShapeAuthoringReference : IComponentData
{
    public int AuthoringInstanceId;
}

public struct SpawnShapeComponent : IComponentData
{
    public int3 SpawnAmount;

    public float3 LocalCenter;
    public float3 LocalStart;
    public float3 LocalStep;

    public float3 GlobalPosition;
    public float LiquidHydroFactor;
    public float LiquidViscosityFactor;
    public float4 ParticleAlbedo;
}

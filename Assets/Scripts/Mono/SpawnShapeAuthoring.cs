using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class SpawnShapeAuthoring : MonoBehaviour
{
    private static readonly Vector3 DefaultBoundsSize = Vector3.one;

    public Bounds spawnBounds = new Bounds(Vector3.zero, DefaultBoundsSize);
    public Color boundsColor = Color.cyan;
    
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
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = boundsColor;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(spawnBounds.center, spawnBounds.size);
        Gizmos.matrix = oldMatrix;
    }
}

public class SpawnShapeAuthoringBaker : Baker<SpawnShapeAuthoring>
{
    public override void Bake(SpawnShapeAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        int3 spawnAmount = new int3(
            math.max(1, authoring.spawnAmountX),
            math.max(1, authoring.spawnAmountY),
            math.max(1, authoring.spawnAmountZ)
        );

        float3 halfSize = (float3)authoring.spawnBounds.size * 0.5f;

        float3 localStart = new float3(
            spawnAmount.x > 1 ? -halfSize.x : 0f,
            spawnAmount.y > 1 ? -halfSize.y : 0f,
            spawnAmount.z > 1 ? -halfSize.z : 0f
        );

        float3 localStep = new float3(
            spawnAmount.x > 1 ? authoring.spawnBounds.size.x / (spawnAmount.x - 1) : 0f,
            spawnAmount.y > 1 ? authoring.spawnBounds.size.y / (spawnAmount.y - 1) : 0f,
            spawnAmount.z > 1 ? authoring.spawnBounds.size.z / (spawnAmount.z - 1) : 0f
        );

        AddComponent(entity, new SpawnShapeComponent
        {
            SpawnAmount = spawnAmount,

            LocalCenter = authoring.spawnBounds.center,
            LocalStart = localStart,
            LocalStep = localStep,
            
            GlobalPosition = authoring.transform.position
        });
    }
}

public struct SpawnShapeComponent : IComponentData
{
    public int3 SpawnAmount;

    public float3 LocalCenter;
    public float3 LocalStart;
    public float3 LocalStep;

    public float3 GlobalPosition;
}
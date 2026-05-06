using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class RotatorAuthoring : MonoBehaviour
{
    public float3 rotationSpeed = new float3(0f, 0f, 0f);
}

class RotatorBaker : Baker<RotatorAuthoring>
{
    public override void Bake(RotatorAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, new RotatorComponent
        {
            
            RotationSpeed = authoring.rotationSpeed
        });
    }
}

public struct RotatorComponent : IComponentData
{
    public float3 RotationSpeed;
}
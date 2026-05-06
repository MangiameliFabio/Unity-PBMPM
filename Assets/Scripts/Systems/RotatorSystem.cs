using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct RotatorSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach ((var rotator, var transform) 
                 in SystemAPI.Query<RefRO<RotatorComponent>, RefRW<LocalTransform>>())
        {
            float3 rotSpeed = rotator.ValueRO.RotationSpeed;
            var trans = transform.ValueRW;
            
            float dt = SystemAPI.Time.DeltaTime;

            quaternion deltaRotation = quaternion.Euler(rotSpeed * dt);

            trans.Rotation = math.mul(trans.Rotation, deltaRotation);

            transform.ValueRW = trans;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
    }
}

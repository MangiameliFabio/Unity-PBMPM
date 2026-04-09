using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

partial struct ParticleSpawnSystem : ISystem
{
    private float current_game_time;
    private float last_upadet_time;
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Config>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // current_game_time += SystemAPI.Time.DeltaTime;
        //
        // if (current_game_time < last_upadet_time + 1f)
        // {
        //     return;
        // }
        //
        // last_upadet_time = current_game_time;
        
        state.Enabled = false;
        
        var config = SystemAPI.GetSingleton<Config>();

        foreach (var shape in SystemAPI.Query<RefRO<SpawnShapeComponent>>())
        {
            for (int i = 0; i < shape.ValueRO.SpawnAmount.x; i++)
            {
                for (int j = 0; j < shape.ValueRO.SpawnAmount.y; j++)
                {
                    for (int k = 0; k < shape.ValueRO.SpawnAmount.z; k++)
                    {
                        var localPosition = shape.ValueRO.LocalCenter + new float3(
                            shape.ValueRO.LocalStart.x + i * shape.ValueRO.LocalStep.x,
                            shape.ValueRO.LocalStart.y + j * shape.ValueRO.LocalStep.y,
                            shape.ValueRO.LocalStart.z + k * shape.ValueRO.LocalStep.z
                        );
                        
                        var globalPosition = localPosition + shape.ValueRO.GlobalPosition;

                        var particleEntity = state.EntityManager.Instantiate(config.ParticlePrefab);
                        state.EntityManager.SetComponentData(particleEntity, LocalTransform.FromPosition(globalPosition));

                        var particleData = state.EntityManager.GetComponentData<ParticleComponent>(particleEntity);
                        particleData.Position = globalPosition;
                        particleData.Volume = shape.ValueRO.LocalStep.x * shape.ValueRO.LocalStep.y * shape.ValueRO.LocalStep.z;
                        state.EntityManager.SetComponentData(particleEntity, particleData);
                    }
                }
            }
        }
    }

    public void OnDestroy(ref SystemState state)
    {
    }
}

using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

partial struct ParticleSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Config>();
        Entity spawnStateEntity = state.EntityManager.CreateEntity(typeof(ParticleSpawnState));
        state.EntityManager.SetComponentData(spawnStateEntity, new ParticleSpawnState
        {
            PendingSpawn = false
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        RefRW<ParticleSpawnState> spawnState = SystemAPI.GetSingletonRW<ParticleSpawnState>();
        if (!spawnState.ValueRO.PendingSpawn)
        {
            return;
        }

        spawnState.ValueRW.PendingSpawn = false;
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
                        particleData.LiquidHydroFactor = shape.ValueRO.LiquidHydroFactor;
                        particleData.LiquidViscosityFactor = shape.ValueRO.LiquidViscosityFactor;
                        state.EntityManager.SetComponentData(particleEntity, particleData);

                        if (state.EntityManager.HasComponent<URPMaterialPropertyBaseColor>(particleEntity))
                        {
                            state.EntityManager.SetComponentData(particleEntity, new URPMaterialPropertyBaseColor
                            {
                                Value = shape.ValueRO.ParticleAlbedo
                            });
                        }
                    }
                }
            }
        }
    }

    public void OnDestroy(ref SystemState state)
    {
    }
}

public struct ParticleSpawnState : IComponentData
{
    public bool PendingSpawn;
}

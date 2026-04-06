using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics.Systems;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(PhysicsSystemGroup))]
partial struct PBMPMSolverSystem : ISystem
{
    private static readonly float3 Gravity = new float3(0f, -9.81f, 0f);
    private EntityQuery _gridQuery;
    private ComponentLookup<GridComponent> _gridLookup;
    private BufferLookup<GridCell> _gridCellsLookupRW;
    private BufferLookup<GridCell> _gridCellsLookupRO;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _gridQuery = SystemAPI.QueryBuilder().WithAll<GridComponent>().Build();
        _gridLookup = state.GetComponentLookup<GridComponent>(true);
        _gridCellsLookupRW = state.GetBufferLookup<GridCell>(false);
        _gridCellsLookupRO = state.GetBufferLookup<GridCell>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _gridLookup.Update(ref state);
        _gridCellsLookupRW.Update(ref state);
        _gridCellsLookupRO.Update(ref state);
        ScheduleParticleJobs(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    private void ScheduleParticleJobs(ref SystemState state)
    {
        var gridEntities = _gridQuery.ToEntityArray(Allocator.TempJob);
        float deltaTime = SystemAPI.Time.DeltaTime;

        var clearGridJob = new ClearGridJob();
        state.Dependency = clearGridJob.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
        
        var particleToGridJob = new ParticleToGridJob
        {
            GridLookup = _gridLookup,
            GridCellsLookup = _gridCellsLookupRW,
            GridEntities = gridEntities
        };
        state.Dependency = particleToGridJob.Schedule(state.Dependency);
        state.Dependency.Complete();
        
        var gridCellsLookup = _gridCellsLookupRW;
        JobHandle gridUpdateHandle = default;
        foreach (var gridEntity in gridEntities)
        {
            NativeArray<GridCell> gridCells = gridCellsLookup[gridEntity].AsNativeArray();
            var gridUpdateJob = new GridUpdateJob
            {
                GridCells = gridCells,
                DeltaTime = deltaTime
            };
            gridUpdateHandle = gridUpdateJob.ScheduleParallel(gridCells.Length, 64, gridUpdateHandle);
        }
        state.Dependency = gridUpdateHandle;
        state.Dependency.Complete();
        
        var gridToParticleJob = new GridToParticleJob
        {
            GridLookup = _gridLookup,
            GridCellsLookup = _gridCellsLookupRO,
            GridEntities = gridEntities,
            DeltaTime = deltaTime
        };
        state.Dependency = gridToParticleJob.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
        
        var integrateParticles = new IntegrateParticlesJob
        {
            DeltaTime = deltaTime
        };
        state.Dependency = integrateParticles.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
        state.Dependency = gridEntities.Dispose(state.Dependency);
    }
    
    [BurstCompile]
    private partial struct SolveConstraintsJob : IJobEntity
    {
        private void Execute(ref ParticleComponent particle)
        {
        }
    }
    
    [BurstCompile]
    private partial struct ClearGridJob : IJobEntity
    {
        private void Execute(DynamicBuffer<GridCell> gridCells)
        {
            for (int i = 0; i < gridCells.Length; i++)
            {
                gridCells.ElementAt(i) = new GridCell
                {
                    Momentum = float3.zero,
                    Velocity = float3.zero,
                    Mass = 0f,
                    Volume = 0f
                };
            }
        }
    }

    [BurstCompile]
    private partial struct ParticleToGridJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<GridComponent> GridLookup;
        public BufferLookup<GridCell> GridCellsLookup;
        [ReadOnly] public NativeArray<Entity> GridEntities;

        private void Execute(ref ParticleComponent particle)
        {
            if (!TryResolveGrid(ref particle, out GridComponent grid))
            {
                return;
            }

            DynamicBuffer<GridCell> gridCells = GridCellsLookup[particle.GridCache];
            if (!GridUtilities.TryConvertToGridSpace(
                    grid,
                    particle.Position,
                    out int3 cellCounts,
                    out int3 baseCoord,
                    out float3 fraction))
            {
                return;
            }

            for (int xOffset = 0; xOffset <= 1; xOffset++)
            {
                for (int yOffset = 0; yOffset <= 1; yOffset++)
                {
                    for (int zOffset = 0; zOffset <= 1; zOffset++)
                    {
                        int3 offset = new int3(xOffset, yOffset, zOffset);
                        int3 cellCoordinates = baseCoord + offset;
                        if (!GridUtilities.IsInsideCellCounts(cellCounts, cellCoordinates))
                        {
                            continue;
                        }

                        float weight = GridUtilities.GetTrilinearWeight(fraction, offset);
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        int cellIndex = GridUtilities.GetGridCellIndex(
                            cellCounts,
                            cellCoordinates.x,
                            cellCoordinates.y,
                            cellCoordinates.z);
                        float3 cellCenter = GridUtilities.GetGlobalPositionFromCellCoordinates(grid, cellCoordinates);
                        float3 relativePosition = cellCenter - particle.Position;

                        float massContribution = weight * particle.Mass;
                        float3 momentumContribution = weight * particle.Mass *
                                                      (particle.Velocity + math.mul(particle.AffineVelocity, relativePosition));

                        GridCell cell = gridCells[cellIndex];
                        cell.Mass += massContribution;
                        cell.Momentum += momentumContribution;
                        cell.Volume += weight * particle.Volume;
                        gridCells.ElementAt(cellIndex) = cell;
                    }
                }
            }
        }

        private bool TryResolveGrid(ref ParticleComponent particle, out GridComponent grid)
        {
            grid = default;

            if (particle.GridCache != Entity.Null)
            {
                GridComponent cachedGrid = GridLookup[particle.GridCache];
                if (GridUtilities.CheckIfInsideBounds(cachedGrid, particle.Position))
                {
                    grid = cachedGrid;
                    return true;
                }
            }

            foreach (Entity gridEntity in GridEntities)
            {
                GridComponent candidateGrid = GridLookup[gridEntity];
                if (!GridUtilities.CheckIfInsideBounds(candidateGrid, particle.Position))
                {
                    continue;
                }

                particle.GridCache = gridEntity;
                grid = candidateGrid;
                return true;
            }

            particle.GridCache = Entity.Null;
            return false;
        }
    }

    [BurstCompile]
    private struct GridUpdateJob : IJobFor
    {
        public NativeArray<GridCell> GridCells;
        [ReadOnly] public float DeltaTime;

        public void Execute(int index)
        {
            GridCell cell = GridCells[index];
            if (cell.Mass <= 0f)
            {
                cell.Velocity = float3.zero;
                GridCells[index] = cell;
                return;
            }

            cell.Velocity = cell.Momentum / cell.Mass;
            cell.Velocity += Gravity * DeltaTime;
            GridCells[index] = cell;
        }
    }

    [BurstCompile]
    private partial struct GridToParticleJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<GridComponent> GridLookup;
        [ReadOnly] public BufferLookup<GridCell> GridCellsLookup;
        [ReadOnly] public NativeArray<Entity> GridEntities;
        [ReadOnly] public float DeltaTime;

        private void Execute(ref ParticleComponent particle)
        {
            if (!TryResolveGrid(ref particle, out GridComponent grid))
            {
                return;
            }

            DynamicBuffer<GridCell> gridCells = GridCellsLookup[particle.GridCache];
            if (!GridUtilities.TryConvertToGridSpace(
                    grid,
                    particle.Position,
                    out int3 cellCounts,
                    out int3 baseCoord,
                    out float3 fraction))
            {
                return;
            }

            float3 velocity = float3.zero;
            float3x3 affineVelocity = float3x3.zero;
            float inverseCellSizeSquared = 1f / (grid.CellSize * grid.CellSize);

            for (int xOffset = 0; xOffset <= 1; xOffset++)
            {
                for (int yOffset = 0; yOffset <= 1; yOffset++)
                {
                    for (int zOffset = 0; zOffset <= 1; zOffset++)
                    {
                        int3 offset = new int3(xOffset, yOffset, zOffset);
                        int3 cellCoordinates = baseCoord + offset;
                        if (!GridUtilities.IsInsideCellCounts(cellCounts, cellCoordinates))
                        {
                            continue;
                        }

                        float weight = GridUtilities.GetTrilinearWeight(fraction, offset);
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        int cellIndex = GridUtilities.GetGridCellIndex(
                            cellCounts,
                            cellCoordinates.x,
                            cellCoordinates.y,
                            cellCoordinates.z);
                        GridCell cell = gridCells[cellIndex];
                        float3 cellCenter = GridUtilities.GetGlobalPositionFromCellCoordinates(grid, cellCoordinates);
                        float3 relativePosition = cellCenter - particle.Position;

                        velocity += weight * cell.Velocity;
                        affineVelocity += weight * OuterProduct(cell.Velocity, relativePosition) * inverseCellSizeSquared;
                    }
                }
            }

            particle.Velocity = velocity;
            particle.AffineVelocity = affineVelocity;
        }

        private bool TryResolveGrid(ref ParticleComponent particle, out GridComponent grid)
        {
            grid = default;

            if (particle.GridCache != Entity.Null)
            {
                GridComponent cachedGrid = GridLookup[particle.GridCache];
                if (GridUtilities.CheckIfInsideBounds(cachedGrid, particle.Position))
                {
                    grid = cachedGrid;
                    return true;
                }
            }

            foreach (Entity gridEntity in GridEntities)
            {
                GridComponent candidateGrid = GridLookup[gridEntity];
                if (!GridUtilities.CheckIfInsideBounds(candidateGrid, particle.Position))
                {
                    continue;
                }

                particle.GridCache = gridEntity;
                grid = candidateGrid;
                return true;
            }

            particle.GridCache = Entity.Null;
            return false;
        }

        private static float3x3 OuterProduct(float3 left, float3 right)
        {
            return new float3x3(
                left * right.x,
                left * right.y,
                left * right.z);
        }
    }
    
    [BurstCompile]
    private partial struct IntegrateParticlesJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        private void Execute(ref ParticleComponent particle, ref LocalTransform transform)
        {
            particle.Position += particle.Velocity * DeltaTime;
            transform.Position = particle.Position;
        }
    }
}

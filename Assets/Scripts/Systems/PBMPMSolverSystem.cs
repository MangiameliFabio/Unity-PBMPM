using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Profiling;

partial struct PBMPMSolverSystem : ISystem
{
    public static readonly float3 Gravity = new float3(0f, -9.81f, 0f);
    private const int GridTileSize = 4;

    private EntityQuery _gridQuery;
    private EntityQuery _particleQuery;
    private ComponentLookup<GridComponent> _gridLookup;
    private BufferLookup<GridCell> _gridCellsLookupRW;
    private BufferLookup<GridCell> _gridCellsLookupRO;
    private NativeList<GridBoxCollider> _colliders;
    private NativeParallelMultiHashMap<int, GridTileParticleRecord> _tileParticlesCache;
    private NativeParallelHashSet<int> _activeTileSetCache;
    private NativeList<int> _activeTilesCache;
    private Entity _debugStatsEntity;

    private float _currentTime;
    private float _solverDeltaTime;
    private float _lastTime;
    private float _remainingTime;
    private float _fixedDeltaTime;
    
    private int _solverSubsteps;
    private int _gridIterationId;
    
    private Config _config;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Config>();

        _gridQuery = SystemAPI.QueryBuilder().WithAll<GridComponent>().Build();
        _particleQuery = SystemAPI.QueryBuilder().WithAll<ParticleComponent>().Build();
        _gridLookup = state.GetComponentLookup<GridComponent>(true);
        _gridCellsLookupRW = state.GetBufferLookup<GridCell>(false);
        _gridCellsLookupRO = state.GetBufferLookup<GridCell>(true);
        _colliders = new NativeList<GridBoxCollider>(Allocator.Persistent);
        _tileParticlesCache = new NativeParallelMultiHashMap<int, GridTileParticleRecord>(1, Allocator.Persistent);
        _activeTileSetCache = new NativeParallelHashSet<int>(1, Allocator.Persistent);
        _activeTilesCache = new NativeList<int>(1, Allocator.Persistent);
        _debugStatsEntity = state.EntityManager.CreateEntity(typeof(SimulationDebugStats), typeof(SimulationMemoryStats));
    }

    public void OnUpdate(ref SystemState state)
    {
        _config = SystemAPI.GetSingleton<Config>();
        int iterationCount = math.max(1, _config.IterationCount);
        GridInterpolationMode interpolationMode = _config.InterpolationMode;
        bool useGridVolumePreservation = _config.UseGridVolumePreservation;

        _currentTime += SystemAPI.Time.DeltaTime;
        _solverDeltaTime = _currentTime - _lastTime;
        _remainingTime += _solverDeltaTime;

        _fixedDeltaTime = 1f / _config.UpdateFrequency;
        _solverSubsteps = (int)(_remainingTime / _fixedDeltaTime);
        _solverSubsteps = math.min(_solverSubsteps, 10);
        _remainingTime -= _solverSubsteps * _fixedDeltaTime;

        _gridLookup.Update(ref state);
        _gridCellsLookupRW.Update(ref state);
        _gridCellsLookupRO.Update(ref state);

        bool solverRan = _solverSubsteps > 0;
        double solverUpdateTimeMs = 0d;
        if (solverRan)
        {
            for (int substepIndex = 0; substepIndex < _solverSubsteps; substepIndex++)
            {
                ProfilerMarker.AutoScope solverUpdateScope = default;
                long startTimestamp = PerformanceUtilities.BeginSolverUpdateMeasurement(out solverUpdateScope);
                using (solverUpdateScope)
                {
                    PBMPMSolverUpdate(ref state, iterationCount, interpolationMode, useGridVolumePreservation);
                }

                solverUpdateTimeMs = PerformanceUtilities.EndSolverUpdateMeasurement(startTimestamp);
            }
        }

        ScheduleSmoothing(ref state);

        int particleCount = _particleQuery.CalculateEntityCount();
        SimulationMemoryStats memoryStats = MemoryUtilities.BuildMemoryStats(
            _gridQuery,
            _gridLookup,
            _gridCellsLookupRO,
            particleCount,
            _tileParticlesCache.Capacity,
            _activeTileSetCache.Capacity,
            _activeTilesCache.Capacity);

        state.EntityManager.SetComponentData(_debugStatsEntity, new SimulationDebugStats
        {
            ParticleCount = particleCount,
            SolverIterations = _solverSubsteps * iterationCount,
            SolverSubsteps = _solverSubsteps
        });
        state.EntityManager.SetComponentData(_debugStatsEntity, memoryStats);
        PBMPMProfilerCaptureStats.SampleFrame(
            particleCount,
            memoryStats.GridCellCount,
            _config.UpdateFrequency,
            iterationCount,
            SystemAPI.Time.DeltaTime,
            solverRan,
            solverUpdateTimeMs);

        _lastTime = _currentTime;
    }

    private void PBMPMSolverUpdate(
        ref SystemState state,
        int iterationCount,
        GridInterpolationMode interpolationMode,
        bool useGridVolumePreservation)
    {
        RebuildColliderCache(ref state);
        ScheduleParticleJobs(ref state, iterationCount, interpolationMode, useGridVolumePreservation);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_colliders.IsCreated)
        {
            _colliders.Dispose();
        }

        if (_tileParticlesCache.IsCreated)
        {
            _tileParticlesCache.Dispose();
        }

        if (_activeTileSetCache.IsCreated)
        {
            _activeTileSetCache.Dispose();
        }

        if (_activeTilesCache.IsCreated)
        {
            _activeTilesCache.Dispose();
        }
    }

    private void RebuildColliderCache(ref SystemState state)
    {
        _colliders.Clear();

        foreach (var (collider, transform) in SystemAPI.Query<RefRO<PhysicsCollider>, RefRO<Unity.Transforms.LocalTransform>>())
        {
            if (!collider.ValueRO.IsValid)
            {
                continue;
            }

            ref var sourceCollider = ref collider.ValueRO.Value.As<Collider>();
            if (sourceCollider.Type != ColliderType.Box)
            {
                continue;
            }

            ref var boxCollider = ref collider.ValueRO.Value.As<BoxCollider>();
            BoxGeometry geometry = boxCollider.Geometry;
            float scale = math.abs(transform.ValueRO.Scale);

            GridBoxCollider gridCollider = new GridBoxCollider
            {
                Center = transform.ValueRO.Position + math.rotate(transform.ValueRO.Rotation, geometry.Center * scale),
                Rotation = math.normalize(math.mul(transform.ValueRO.Rotation, geometry.Orientation)),
                HalfExtents = geometry.Size * (0.5f * scale)
            };
            _colliders.Add(gridCollider);
        }
    }

    [BurstCompile]
    private void ScheduleParticleJobs(ref SystemState state, int iterationCount, GridInterpolationMode interpolationMode, bool useGridVolumePreservation)
    {
        var gridEntities = _gridQuery.ToEntityArray(Allocator.TempJob);
        
        for (int iterationIndex = 0; iterationIndex < iterationCount; iterationIndex++)
        {
            _gridIterationId++;
            RunSolverIteration(
                ref state,
                gridEntities,
                interpolationMode,
                useGridVolumePreservation,
                applyParticleGravity: iterationIndex == iterationCount - 1);
        }

        ScheduleIntegrateParticles(ref state);

        state.Dependency = gridEntities.Dispose(state.Dependency);
    }

    [BurstCompile]
    private void RunSolverIteration(
        ref SystemState state,
        NativeArray<Entity> gridEntities,
        GridInterpolationMode interpolationMode,
        bool useGridVolumePreservation,
        bool applyParticleGravity)
    {
        ScheduleSolveConstraints(ref state, useGridVolumePreservation);
        ScheduleParticleToGrid(ref state, gridEntities, interpolationMode);
        ScheduleUpdateGrid(ref state, gridEntities, interpolationMode);
        ScheduleGridToParticle(ref state, gridEntities, interpolationMode, useGridVolumePreservation, applyParticleGravity);
    }

    [BurstCompile]
    private void ScheduleIntegrateParticles(ref SystemState state)
    {
        var integrateParticles = new IntegrateParticlesJob
        {
            DeltaTime = _fixedDeltaTime,
            Colliders = _colliders.AsArray()
        };

        state.Dependency = integrateParticles.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
    }

    [BurstCompile]
    private void ScheduleSmoothing(ref SystemState state)
    {
        var particleSmoothing = new ParticleSmoothingJob()
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            UseVisualSmoothing = _config.UseVisualSmoothing
        };

        state.Dependency = particleSmoothing.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
    }

    [BurstCompile]
    private void ScheduleSolveConstraints(ref SystemState state, bool useGridVolumePreservation)
    {
        var constraintSolverJob = new SolveConstraintsJob
        {
            UseGridVolumePreservation = useGridVolumePreservation
        };
        state.Dependency = constraintSolverJob.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
    }

    [BurstCompile]
    private void ScheduleParticleToGrid(ref SystemState state, NativeArray<Entity> gridEntities, GridInterpolationMode interpolationMode)
    {
        int tileParticleCapacity = math.max(1, _particleQuery.CalculateEntityCount() * 8);
        int maxActiveTileCapacity = 1;

        foreach (var gridEntity in gridEntities)
        {
            GridComponent grid = _gridLookup[gridEntity];
            maxActiveTileCapacity = math.max(
                maxActiveTileCapacity,
                MemoryUtilities.GetActiveTileCapacity(grid, GridTileSize));
        }

        _tileParticlesCache.Clear();
        _activeTileSetCache.Clear();
        _activeTilesCache.Clear();
        MemoryUtilities.UpdateParticleToGridCacheCapacity(
            ref _tileParticlesCache,
            ref _activeTileSetCache,
            ref _activeTilesCache,
            tileParticleCapacity,
            maxActiveTileCapacity);

        foreach (var gridEntity in gridEntities)
        {
            GridComponent grid = _gridLookup[gridEntity];
            _tileParticlesCache.Clear();
            _activeTileSetCache.Clear();
            _activeTilesCache.Clear();

            var particleToGridTileJob = new ParticleToGridTileJob
            {
                GridLookup = _gridLookup,
                GridEntities = gridEntities,
                TargetGridEntity = gridEntity,
                TargetGrid = grid,
                InterpolationMode = interpolationMode,
                TileSize = GridTileSize,
                TileParticles = _tileParticlesCache.AsParallelWriter(),
                ActiveTiles = _activeTileSetCache.AsParallelWriter()
            };

            state.Dependency = particleToGridTileJob.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();

            MemoryUtilities.CollectActiveTiles(_activeTileSetCache, ref _activeTilesCache);
            if (_activeTilesCache.Length > 0)
            {
                NativeArray<GridCell> gridCells = _gridCellsLookupRW[gridEntity].AsNativeArray();
                var accumulateGridTilesJob = new AccumulateGridTilesJob
                {
                    ActiveTiles = _activeTilesCache.AsArray(),
                    TileParticles = _tileParticlesCache,
                    GridCells = gridCells,
                    Grid = grid,
                    InterpolationMode = interpolationMode,
                    CurrentGridIteration = _gridIterationId,
                    TileSize = GridTileSize
                };

                state.Dependency = accumulateGridTilesJob.ScheduleParallel(_activeTilesCache.Length, 1, state.Dependency);
                state.Dependency.Complete();
            }
        }
    }

    [BurstCompile]
    private void ScheduleUpdateGrid(ref SystemState state, NativeArray<Entity> gridEntities, GridInterpolationMode interpolationMode)
    {
        foreach (var gridEntity in gridEntities)
        {
            NativeArray<GridCell> gridCells = _gridCellsLookupRW[gridEntity].AsNativeArray();
            var gridUpdateJob = new GridUpdateJob
            {
                Colliders = _colliders.AsArray(),
                GridCells = gridCells,
                Grid = _gridLookup[gridEntity],
                InterpolationMode = interpolationMode,
                CurrentGridIteration = _gridIterationId
            };
            state.Dependency = gridUpdateJob.ScheduleParallel(gridCells.Length, 64, state.Dependency);
            state.Dependency.Complete();
        }
    }

    [BurstCompile]
    private void ScheduleGridToParticle(
        ref SystemState state,
        NativeArray<Entity> gridEntities,
        GridInterpolationMode interpolationMode,
        bool useGridVolumePreservation,
        bool applyParticleGravity)
    {
        var gridToParticleJob = new GridToParticleJob
        {
            GridLookup = _gridLookup,
            GridCellsLookup = _gridCellsLookupRO,
            GridEntities = gridEntities,
            DeltaTime = _fixedDeltaTime,
            GravityDisplacement = Gravity * (_fixedDeltaTime * _fixedDeltaTime),
            ApplyParticleGravity = applyParticleGravity,
            InterpolationMode = interpolationMode,
            UseGridVolumePreservation = useGridVolumePreservation,
            CurrentGridIteration = _gridIterationId
        };

        state.Dependency = gridToParticleJob.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;

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
        RebuildColliderCache(ref state);
        ScheduleParticleJobs(ref state, iterationCount, interpolationMode, useGridVolumePreservation);

        state.EntityManager.SetComponentData(_debugStatsEntity, new SimulationDebugStats
        {
            ParticleCount = _particleQuery.CalculateEntityCount(),
            SolverIterations = _solverSubsteps * iterationCount
        });
        UpdateMemoryStats(ref state);

        _lastTime = _currentTime;
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

        for (int substepIndex = 0; substepIndex < _solverSubsteps; substepIndex++)
        {
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
        }
        
        ScheduleSmoothing(ref state);

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
            maxActiveTileCapacity = math.max(maxActiveTileCapacity, GetActiveTileCapacity(grid));
        }

        _tileParticlesCache.Clear();
        _activeTileSetCache.Clear();
        _activeTilesCache.Clear();
        UpdateParticleToGridCacheCapacity(tileParticleCapacity, maxActiveTileCapacity);

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

            CollectActiveTiles();
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

    private int GetActiveTileCapacity(GridComponent grid)
    {
        int3 nodeCounts = GridUtilities.GetNodeCounts(grid);
        int3 tileCounts = GridUtilities.GetTileCounts(nodeCounts, GridTileSize);
        return math.max(1, tileCounts.x * tileCounts.y * tileCounts.z);
    }

    private void UpdateParticleToGridCacheCapacity(int tileParticleCapacity, int activeTileCapacity)
    {
        ResizeCacheCapacity(ref _tileParticlesCache, math.max(1, tileParticleCapacity));
        ResizeCacheCapacity(ref _activeTileSetCache, math.max(1, activeTileCapacity));
        ResizeCacheCapacity(ref _activeTilesCache, math.max(1, activeTileCapacity));
    }

    private static void ResizeCacheCapacity(ref NativeParallelMultiHashMap<int, GridTileParticleRecord> container, int requiredCapacity)
    {
        int currentCapacity = container.Capacity;
        if (currentCapacity < requiredCapacity)
        {
            container.Capacity = requiredCapacity;
            return;
        }

        if (ShouldShrinkCacheCapacity(currentCapacity, requiredCapacity))
        {
            container.Dispose();
            container = new NativeParallelMultiHashMap<int, GridTileParticleRecord>(requiredCapacity, Allocator.Persistent);
        }
    }

    private static void ResizeCacheCapacity(ref NativeParallelHashSet<int> container, int requiredCapacity)
    {
        int currentCapacity = container.Capacity;
        if (currentCapacity < requiredCapacity)
        {
            container.Capacity = requiredCapacity;
            return;
        }

        if (ShouldShrinkCacheCapacity(currentCapacity, requiredCapacity))
        {
            container.Dispose();
            container = new NativeParallelHashSet<int>(requiredCapacity, Allocator.Persistent);
        }
    }

    private static void ResizeCacheCapacity(ref NativeList<int> container, int requiredCapacity)
    {
        int currentCapacity = container.Capacity;
        if (currentCapacity < requiredCapacity || ShouldShrinkCacheCapacity(currentCapacity, requiredCapacity))
        {
            container.Capacity = requiredCapacity;
        }
    }

    private static bool ShouldShrinkCacheCapacity(int currentCapacity, int requiredCapacity)
    {
        return currentCapacity > math.max(requiredCapacity * 2, 256);
    }

    private void CollectActiveTiles()
    {
        var activeTileEnumerator = _activeTileSetCache.GetEnumerator();
        while (activeTileEnumerator.MoveNext())
        {
            _activeTilesCache.AddNoResize(activeTileEnumerator.Current);
        }
    }

    private void UpdateMemoryStats(ref SystemState state)
    {
        int particleCount = _particleQuery.CalculateEntityCount();
        int gridCount = 0;
        int totalGridCellCount = 0;
        int totalGridNodeCount = 0;

        foreach (var (grid, gridCells) in SystemAPI.Query<RefRO<GridComponent>, DynamicBuffer<GridCell>>())
        {
            gridCount++;
            int3 cellCounts = GridUtilities.GetCellCounts(grid.ValueRO);
            totalGridCellCount += cellCounts.x * cellCounts.y * cellCounts.z;
            totalGridNodeCount += gridCells.Length;
        }

        long particleComponentBytes = (long)particleCount * UnsafeUtility.SizeOf<ParticleComponent>();
        long particleTransformBytes = (long)particleCount * UnsafeUtility.SizeOf<Unity.Transforms.LocalTransform>();
        long gridComponentBytes = (long)gridCount * UnsafeUtility.SizeOf<GridComponent>();
        long gridNodeBytes = (long)totalGridNodeCount * UnsafeUtility.SizeOf<GridCell>();
        long tileParticleCacheBytes = EstimateParallelMultiHashMapBytes(
            _tileParticlesCache.Capacity,
            UnsafeUtility.SizeOf<GridTileParticleRecord>());
        long activeTileSetBytes = EstimateParallelHashSetBytes(
            _activeTileSetCache.Capacity,
            UnsafeUtility.SizeOf<int>());
        long activeTileListBytes = AlignTo64((long)_activeTilesCache.Capacity * UnsafeUtility.SizeOf<int>());
        long totalEstimatedSolverBytes = particleComponentBytes
            + gridComponentBytes
            + gridNodeBytes
            + tileParticleCacheBytes
            + activeTileSetBytes
            + activeTileListBytes;

        state.EntityManager.SetComponentData(_debugStatsEntity, new SimulationMemoryStats
        {
            ParticleCount = particleCount,
            GridCount = gridCount,
            GridCellCount = totalGridCellCount,
            GridNodeCount = totalGridNodeCount,
            TileParticleCacheCapacity = _tileParticlesCache.Capacity,
            ActiveTileCapacity = _activeTileSetCache.Capacity,
            ParticleComponentBytes = particleComponentBytes,
            ParticleTransformBytes = particleTransformBytes,
            GridComponentBytes = gridComponentBytes,
            GridNodeBytes = gridNodeBytes,
            TileParticleCacheBytes = tileParticleCacheBytes,
            ActiveTileSetBytes = activeTileSetBytes,
            ActiveTileListBytes = activeTileListBytes,
            TotalEstimatedSolverBytes = totalEstimatedSolverBytes,
            TotalEstimatedRuntimeBytes = totalEstimatedSolverBytes + particleTransformBytes
        });
    }

    private static long EstimateParallelMultiHashMapBytes(int capacity, int valueSize)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        int bucketCount = CeilPow2(math.max(1, capacity * 2));
        return AlignTo64((long)valueSize * capacity)
            + AlignTo64(4L * capacity)
            + AlignTo64(4L * capacity)
            + AlignTo64(4L * bucketCount);
    }

    private static long EstimateParallelHashSetBytes(int capacity, int keySize)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        int bucketCount = CeilPow2(math.max(1, capacity * 2));
        return AlignTo64((long)keySize * capacity)
            + AlignTo64(4L * capacity)
            + AlignTo64(4L * bucketCount);
    }

    private static int CeilPow2(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private static long AlignTo64(long value)
    {
        return (value + 63L) & ~63L;
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

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

static class MemoryUtilities
{
    public static int GetActiveTileCapacity(in GridComponent grid, int gridTileSize)
    {
        int3 nodeCounts = GridUtilities.GetNodeCounts(grid);
        int3 tileCounts = GridUtilities.GetTileCounts(nodeCounts, gridTileSize);
        return math.max(1, tileCounts.x * tileCounts.y * tileCounts.z);
    }

    public static void UpdateParticleToGridCacheCapacity(
        ref NativeParallelMultiHashMap<int, GridTileParticleRecord> tileParticlesCache,
        ref NativeParallelHashSet<int> activeTileSetCache,
        ref NativeList<int> activeTilesCache,
        int tileParticleCapacity,
        int activeTileCapacity)
    {
        ResizeCacheCapacity(ref tileParticlesCache, math.max(1, tileParticleCapacity));
        ResizeCacheCapacity(ref activeTileSetCache, math.max(1, activeTileCapacity));
        ResizeCacheCapacity(ref activeTilesCache, math.max(1, activeTileCapacity));
    }

    public static void CollectActiveTiles(
        NativeParallelHashSet<int> activeTileSetCache,
        ref NativeList<int> activeTilesCache)
    {
        var activeTileEnumerator = activeTileSetCache.GetEnumerator();
        while (activeTileEnumerator.MoveNext())
        {
            activeTilesCache.AddNoResize(activeTileEnumerator.Current);
        }
    }

    public static SimulationMemoryStats BuildMemoryStats(
        EntityQuery gridQuery,
        ComponentLookup<GridComponent> gridLookup,
        BufferLookup<GridCell> gridCellsLookup,
        int particleCount,
        int tileParticleCacheCapacity,
        int activeTileSetCapacity,
        int activeTileListCapacity)
    {
        int gridCount = 0;
        int totalGridCellCount = 0;
        int totalGridNodeCount = 0;

        using NativeArray<Entity> gridEntities = gridQuery.ToEntityArray(Allocator.Temp);
        for (int gridIndex = 0; gridIndex < gridEntities.Length; gridIndex++)
        {
            Entity gridEntity = gridEntities[gridIndex];
            GridComponent grid = gridLookup[gridEntity];
            DynamicBuffer<GridCell> gridCells = gridCellsLookup[gridEntity];
            gridCount++;
            int3 cellCounts = GridUtilities.GetCellCounts(grid);
            totalGridCellCount += cellCounts.x * cellCounts.y * cellCounts.z;
            totalGridNodeCount += gridCells.Length;
        }

        long particleComponentBytes = (long)particleCount * UnsafeUtility.SizeOf<ParticleComponent>();
        long particleTransformBytes = (long)particleCount * UnsafeUtility.SizeOf<Unity.Transforms.LocalTransform>();
        long gridComponentBytes = (long)gridCount * UnsafeUtility.SizeOf<GridComponent>();
        long gridNodeBytes = (long)totalGridNodeCount * UnsafeUtility.SizeOf<GridCell>();
        long tileParticleCacheBytes = EstimateParallelMultiHashMapBytes(
            tileParticleCacheCapacity,
            UnsafeUtility.SizeOf<GridTileParticleRecord>());
        long activeTileSetBytes = EstimateParallelHashSetBytes(
            activeTileSetCapacity,
            UnsafeUtility.SizeOf<int>());
        long activeTileListBytes = AlignTo64((long)activeTileListCapacity * UnsafeUtility.SizeOf<int>());
        long totalEstimatedSolverBytes = particleComponentBytes
            + gridComponentBytes
            + gridNodeBytes
            + tileParticleCacheBytes
            + activeTileSetBytes
            + activeTileListBytes;

        return new SimulationMemoryStats
        {
            ParticleCount = particleCount,
            GridCount = gridCount,
            GridCellCount = totalGridCellCount,
            GridNodeCount = totalGridNodeCount,
            TileParticleCacheCapacity = tileParticleCacheCapacity,
            ActiveTileCapacity = activeTileSetCapacity,
            ParticleComponentBytes = particleComponentBytes,
            ParticleTransformBytes = particleTransformBytes,
            GridComponentBytes = gridComponentBytes,
            GridNodeBytes = gridNodeBytes,
            TileParticleCacheBytes = tileParticleCacheBytes,
            ActiveTileSetBytes = activeTileSetBytes,
            ActiveTileListBytes = activeTileListBytes,
            TotalEstimatedSolverBytes = totalEstimatedSolverBytes,
            TotalEstimatedRuntimeBytes = totalEstimatedSolverBytes + particleTransformBytes
        };
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
}

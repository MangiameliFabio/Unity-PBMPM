using Unity.Entities;

public struct SimulationMemoryStats : IComponentData
{
    public int ParticleCount;
    public int GridCount;
    public int GridCellCount;
    public int GridNodeCount;
    public int TileParticleCacheCapacity;
    public int ActiveTileCapacity;

    public long ParticleComponentBytes;
    public long ParticleTransformBytes;
    public long GridComponentBytes;
    public long GridNodeBytes;
    public long TileParticleCacheBytes;
    public long ActiveTileSetBytes;
    public long ActiveTileListBytes;
    public long TotalEstimatedSolverBytes;
    public long TotalEstimatedRuntimeBytes;
}

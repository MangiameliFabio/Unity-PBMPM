using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
partial struct ParticleToGridTileJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<GridComponent> GridLookup;
    [ReadOnly] public NativeArray<Entity> GridEntities;
    [ReadOnly] public Entity TargetGridEntity;
    [ReadOnly] public GridComponent TargetGrid;
    [ReadOnly] public GridInterpolationMode InterpolationMode;
    [ReadOnly] public int TileSize;

    public NativeParallelMultiHashMap<int, GridTileParticleRecord>.ParallelWriter TileParticles;
    public NativeParallelHashSet<int>.ParallelWriter ActiveTiles;

    private void Execute(ref ParticleComponent particle)
    {
        if (!GridUtilities.TryResolveGrid(GridLookup, GridEntities, particle.Position, ref particle.GridCache, out GridComponent resolvedGrid))
        {
            return;
        }

        if (particle.GridCache != TargetGridEntity)
        {
            return;
        }

        int3 nodeCounts = GridUtilities.GetNodeCounts(TargetGrid);
        int3 tileCounts = GridUtilities.GetTileCounts(nodeCounts, TileSize);
        FixedList128Bytes<int> touchedTiles = default;

        if (InterpolationMode == GridInterpolationMode.TrilinearCellCentered)
        {
            if (!GridUtilities.TryConvertToGridSpace(
                    resolvedGrid,
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
                        int3 supportCoordinates = baseCoord + new int3(xOffset, yOffset, zOffset);
                        if (!GridUtilities.IsInsideCellCounts(cellCounts, supportCoordinates))
                        {
                            continue;
                        }

                        int tileIndex = GridUtilities.GetTileIndex(tileCounts, supportCoordinates, TileSize);
                        GridUtilities.AddUniqueTile(ref touchedTiles, tileIndex);
                    }
                }
            }
        }
        else
        {
            if (!GridUtilities.TryGetQuadraticWeights(
                    resolvedGrid,
                    particle.Position,
                    out QuadraticWeights3D weights))
            {
                return;
            }

            for (int xOffset = 0; xOffset < 3; xOffset++)
            {
                for (int yOffset = 0; yOffset < 3; yOffset++)
                {
                    for (int zOffset = 0; zOffset < 3; zOffset++)
                    {
                        int3 supportCoordinates = weights.BaseCoordinate + new int3(xOffset, yOffset, zOffset);
                        if (!GridUtilities.IsInsideNodeCounts(nodeCounts, supportCoordinates))
                        {
                            continue;
                        }

                        int tileIndex = GridUtilities.GetTileIndex(tileCounts, supportCoordinates, TileSize);
                        GridUtilities.AddUniqueTile(ref touchedTiles, tileIndex);
                    }
                }
            }
        }

        GridTileParticleRecord record = new GridTileParticleRecord
        {
            Position = particle.Position,
            Displacement = particle.Displacement,
            DeformationDisplacement = particle.DeformationDisplacement,
            Mass = particle.Mass,
            Volume = particle.Volume
        };

        for (int i = 0; i < touchedTiles.Length; i++)
        {
            int tileIndex = touchedTiles[i];
            ActiveTiles.Add(tileIndex);
            TileParticles.Add(tileIndex, record);
        }
    }
}

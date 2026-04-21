using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
partial struct AccumulateGridTilesJob : IJobParallelFor
{
    [NativeDisableParallelForRestriction]
    public NativeArray<GridCell> GridCells;
    
    [ReadOnly] public NativeArray<int> ActiveTiles;
    [ReadOnly] public NativeParallelMultiHashMap<int, GridTileParticleRecord> TileParticles;
    [ReadOnly] public GridComponent Grid;
    [ReadOnly] public GridInterpolationMode InterpolationMode;
    [ReadOnly] public int CurrentGridIteration;
    [ReadOnly] public int TileSize;
    
    public void Execute(int index)
    {
        int tileIndex = ActiveTiles[index];
        int3 nodeCounts = GridUtilities.GetNodeCounts(Grid);
        int3 tileCounts = GridUtilities.GetTileCounts(nodeCounts, TileSize);
        int3 tileCoordinates = GridUtilities.GetTileCoordinates(tileCounts, tileIndex);
        int3 tileMin = tileCoordinates * TileSize;
        int3 tileMax = math.min(tileMin + TileSize, nodeCounts);

        ClearTile(tileMin, tileMax, nodeCounts);

        NativeParallelMultiHashMapIterator<int> iterator;
        GridTileParticleRecord particleRecord;
        if (!TileParticles.TryGetFirstValue(tileIndex, out particleRecord, out iterator))
        {
            return;
        }

        do
        {
            AccumulateParticle(ref particleRecord, tileMin, tileMax, nodeCounts);
        } while (TileParticles.TryGetNextValue(out particleRecord, ref iterator));
    }

    private void ClearTile(int3 tileMin, int3 tileMax, int3 nodeCounts)
    {
        for (int x = tileMin.x; x < tileMax.x; x++)
        {
            for (int y = tileMin.y; y < tileMax.y; y++)
            {
                for (int z = tileMin.z; z < tileMax.z; z++)
                {
                    int gridIndex = GridUtilities.GetGridIndex(nodeCounts, x, y, z);
                    GridCell cell = GridCells[gridIndex];
                    cell.WeightedDisplacement = float3.zero;
                    cell.Displacement = float3.zero;
                    cell.Mass = 0f;
                    cell.Volume = 0f;
                    cell.LastTouchedIteration = CurrentGridIteration;
                    GridCells[gridIndex] = cell;
                }
            }
        }
    }

    private void AccumulateParticle(ref GridTileParticleRecord particle, int3 tileMin, int3 tileMax, int3 nodeCounts)
    {
        if (InterpolationMode == GridInterpolationMode.TrilinearCellCentered)
        {
            if (!GridUtilities.TryConvertToGridSpace(
                    Grid,
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
                        int3 supportCoordinates = baseCoord + offset;
                        if (!GridUtilities.IsInsideCellCounts(cellCounts, supportCoordinates) ||
                            !GridUtilities.IsInsideTile(tileMin, tileMax, supportCoordinates))
                        {
                            continue;
                        }

                        float weight = GridUtilities.GetTrilinearWeight(fraction, offset);
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        int gridIndex = GridUtilities.GetGridIndex(nodeCounts, supportCoordinates.x, supportCoordinates.y, supportCoordinates.z);
                        float3 supportPosition = GridUtilities.GetGridPosition(Grid, supportCoordinates, InterpolationMode);
                        float3 relativePosition = supportPosition - particle.Position;

                        GridCell cell = GridCells[gridIndex];
                        cell.Mass += weight * particle.Mass;
                        cell.WeightedDisplacement += weight * particle.Mass *
                                                     (particle.Displacement + math.mul(particle.DeformationDisplacement, relativePosition));
                        cell.Volume += weight * particle.Volume;
                        GridCells[gridIndex] = cell;
                    }
                }
            }

            return;
        }

        if (!GridUtilities.TryGetQuadraticWeights(Grid, particle.Position, out QuadraticWeights3D weights))
        {
            return;
        }

        for (int xOffset = 0; xOffset < 3; xOffset++)
        {
            for (int yOffset = 0; yOffset < 3; yOffset++)
            {
                for (int zOffset = 0; zOffset < 3; zOffset++)
                {
                    int3 offset = new int3(xOffset, yOffset, zOffset);
                    int3 supportCoordinates = weights.BaseCoordinate + offset;
                    if (!GridUtilities.IsInsideNodeCounts(nodeCounts, supportCoordinates) ||
                        !GridUtilities.IsInsideTile(tileMin, tileMax, supportCoordinates))
                    {
                        continue;
                    }

                    float weight = weights.GetWeight(offset);
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    int gridIndex = GridUtilities.GetGridIndex(nodeCounts, supportCoordinates.x, supportCoordinates.y, supportCoordinates.z);
                    float3 supportPosition = GridUtilities.GetGridPosition(Grid, supportCoordinates, InterpolationMode);
                    float3 relativePosition = supportPosition - particle.Position;

                    GridCell cell = GridCells[gridIndex];
                    cell.Mass += weight * particle.Mass;
                    cell.WeightedDisplacement += weight * particle.Mass *
                                                 (particle.Displacement + math.mul(particle.DeformationDisplacement, relativePosition));
                    cell.Volume += weight * particle.Volume;
                    GridCells[gridIndex] = cell;
                }
            }
        }
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
partial struct GridToParticleJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<GridComponent> GridLookup;
    [ReadOnly] public BufferLookup<GridCell> GridCellsLookup;
    [ReadOnly] public NativeArray<Entity> GridEntities;
    [ReadOnly] public float DeltaTime;
    [ReadOnly] public float3 GravityDisplacement;
    [ReadOnly] public bool ApplyParticleGravity;
    [ReadOnly] public GridInterpolationMode InterpolationMode;
    [ReadOnly] public bool UseGridVolumePreservation;
    [ReadOnly] public int CurrentGridIteration;

    private void Execute(ref ParticleComponent particle)
    {
        if (!GridUtilities.TryResolveGrid(GridLookup, GridEntities, particle.Position, ref particle.GridCache, out GridComponent grid))
        {
            return;
        }

        DynamicBuffer<GridCell> gridCells = GridCellsLookup[particle.GridCache];
        int3 nodeCounts = GridUtilities.GetNodeCounts(grid);

        float3 displacement = float3.zero;
        float3x3 c = float3x3.zero;
        float gridMeasuredLiquidDensity = 0f;
        float inverseCellSizeSquared = 1f / (grid.CellSize * grid.CellSize);
        float inverseCellVolume = 1f / (grid.CellSize * grid.CellSize * grid.CellSize);

        if (InterpolationMode == GridInterpolationMode.TrilinearCellCentered)
        {
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
                        int3 supportCoordinates = baseCoord + offset;
                        if (!GridUtilities.IsInsideCellCounts(cellCounts, supportCoordinates))
                        {
                            continue;
                        }

                        float weight = GridUtilities.GetTrilinearWeight(fraction, offset);
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        int cellIndex = GridUtilities.GetGridIndex(nodeCounts, supportCoordinates.x, supportCoordinates.y, supportCoordinates.z);
                        GridCell cell = gridCells[cellIndex];
                        if (cell.LastTouchedIteration != CurrentGridIteration)
                        {
                            continue;
                        }

                        float3 supportPosition = GridUtilities.GetGridPosition(grid, supportCoordinates, InterpolationMode);
                        float3 relativePosition = supportPosition - particle.Position;

                        displacement += weight * cell.Displacement;
                        c += GridUtilities.OuterProduct(cell.Displacement, relativePosition) * (weight * inverseCellSizeSquared);
                        gridMeasuredLiquidDensity += weight * cell.Volume * inverseCellVolume;
                    }
                }
            }
        }
        else
        {
            if (!GridUtilities.TryGetQuadraticWeights(
                    grid,
                    particle.Position,
                    out QuadraticWeights3D weights))
            {
                return;
            }
            
            //quadratic B-spline interpolation
            for (int xOffset = 0; xOffset < 3; xOffset++)
            {
                for (int yOffset = 0; yOffset < 3; yOffset++)
                {
                    for (int zOffset = 0; zOffset < 3; zOffset++)
                    {
                        int3 offset = new int3(xOffset, yOffset, zOffset);
                        int3 supportCoordinates = weights.BaseCoordinate + offset;
                        if (!GridUtilities.IsInsideNodeCounts(nodeCounts, supportCoordinates))
                        {
                            continue;
                        }

                        float weight = weights.GetWeight(offset);
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        int cellIndex = GridUtilities.GetGridIndex(nodeCounts, supportCoordinates.x, supportCoordinates.y, supportCoordinates.z);
                        GridCell cell = gridCells[cellIndex];
                        if (cell.LastTouchedIteration != CurrentGridIteration)
                        {
                            continue;
                        }

                        float3 supportPosition = GridUtilities.GetGridPosition(grid, supportCoordinates, InterpolationMode);
                        float3 relativePosition = supportPosition - particle.Position;

                        displacement += weight * cell.Displacement;
                        c += 4f * GridUtilities.OuterProduct(cell.Displacement, relativePosition) * (weight * inverseCellSizeSquared);
                        gridMeasuredLiquidDensity += weight * cell.Volume * inverseCellVolume;
                    }
                }
            }
        }

        particle.Displacement = displacement;
        if (ApplyParticleGravity)
        {
            particle.Displacement += GravityDisplacement;
        }

        particle.DeformationDisplacement = c * DeltaTime;
        particle.GridMeasuredLiquidDensity = math.max(gridMeasuredLiquidDensity, 1e-4f);
        if (UseGridVolumePreservation)
        {
            particle.LiquidDensity = particle.GridMeasuredLiquidDensity;
        }
    }
}

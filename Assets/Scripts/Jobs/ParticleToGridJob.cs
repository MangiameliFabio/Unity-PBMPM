using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
partial struct ParticleToGridJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<GridComponent> GridLookup;
    public BufferLookup<GridCell> GridCellsLookup;
    [ReadOnly] public NativeArray<Entity> GridEntities;
    [ReadOnly] public float DeltaTime;
    [ReadOnly] public GridInterpolationMode InterpolationMode;

    private void Execute(ref ParticleComponent particle)
    {
        if (!GridUtilities.TryResolveGrid(GridLookup, GridEntities, particle.Position, ref particle.GridCache, out GridComponent grid))
        {
            return;
        }

        DynamicBuffer<GridCell> gridCells = GridCellsLookup[particle.GridCache];
        int3 nodeCounts = GridUtilities.GetNodeCounts(grid);

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
                        float3 supportPosition = GridUtilities.GetGridPosition(grid, supportCoordinates, InterpolationMode);
                        float3 relativePosition = supportPosition - particle.Position;

                        float massContribution = weight * particle.Mass;
                        //Particle to Grid transfter using APIC / MLS-MPM
                        float3 displacementContribution = weight * particle.Mass *
                            (particle.Displacement + math.mul(particle.DeformationDisplacement, relativePosition)); //APIC Formula 8

                        GridCell cell = gridCells[cellIndex];
                        cell.Mass += massContribution;
                        cell.WeightedDisplacement += displacementContribution;
                        cell.Volume += weight * particle.Volume;
                        gridCells.ElementAt(cellIndex) = cell;
                    }
                }
            }

            return;
        }

        if (!GridUtilities.TryGetQuadraticWeights(
                grid,
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
                    float3 supportPosition = GridUtilities.GetGridPosition(grid, supportCoordinates, InterpolationMode);
                    float3 relativePosition = supportPosition - particle.Position;

                    float massContribution = weight * particle.Mass;
                    float3 displacementContribution = weight * particle.Mass *
                                                      (particle.Displacement + math.mul(particle.DeformationDisplacement, relativePosition));

                    GridCell cell = gridCells[cellIndex];
                    cell.Mass += massContribution;
                    cell.WeightedDisplacement += displacementContribution;
                    cell.Volume += weight * particle.Volume;
                    gridCells.ElementAt(cellIndex) = cell;
                }
            }
        }
    }
}

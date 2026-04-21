using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
partial struct ClearGridJob : IJobFor
{
    public NativeArray<GridCell> GridCells;

    public void Execute(int index)
    {
        GridCell cell = GridCells[index];
        cell.WeightedDisplacement = float3.zero;
        cell.Displacement = float3.zero;
        cell.Mass = 0f;
        cell.Volume = 0f;
        GridCells[index] = cell;
    }
}

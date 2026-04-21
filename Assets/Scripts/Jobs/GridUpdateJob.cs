using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
struct GridUpdateJob : IJobFor
{
    [ReadOnly] public float FrictionCoefficient;
    public NativeArray<GridCell> GridCells;
    [ReadOnly] public float DeltaTime;
    [ReadOnly] public NativeArray<GridBoxCollider> Colliders;
    [ReadOnly] public GridComponent Grid;
    [ReadOnly] public GridInterpolationMode InterpolationMode;

    public void Execute(int index)
    {
        GridCell cell = GridCells[index];

        if (cell.Mass <= 0f)
        {
            cell.Displacement = float3.zero;
            cell.WeightedDisplacement = float3.zero;
            GridCells[index] = cell;
            return;
        }

        float3 supportPosition = GridUtilities.GetGridPosition(Grid, cell.Coordinates, InterpolationMode);
        //MPM Section 8.3, “Eulerian/Lagrangian Momentum” and  Section 10, “Explicit Time Integration”
        // Convert the mass-weighted grid quantity back into a candidate displacement
        // and then apply gravity directly in displacement form.
        cell.Displacement = cell.WeightedDisplacement / cell.Mass;
        cell.Displacement += PBMPMSolverSystem.Gravity * (DeltaTime * DeltaTime);


        //MPM Section 12.1 Collision Objects
        foreach (var collider in Colliders)
        {
            float3 displacedSupportPosition = supportPosition + cell.Displacement;
            float phi = collider.GetSignedDistance(displacedSupportPosition);
            if (phi <= 0f)
            {
                float3 normal = collider.GetNormal(displacedSupportPosition);
                float3 projectedDisplacement = cell.Displacement - phi * normal;
                float3 tangentialDisplacement = projectedDisplacement - normal * math.dot(projectedDisplacement, normal);
                cell.Displacement = normal * math.dot(projectedDisplacement, normal) +
                                    tangentialDisplacement * math.saturate(1f - FrictionCoefficient);
            }
        }

        GridCells[index] = cell;
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
struct GridUpdateJob : IJobFor
{
    public NativeArray<GridCell> GridCells;
    [ReadOnly] public NativeArray<GridBoxCollider> Colliders;
    [ReadOnly] public GridComponent Grid;
    [ReadOnly] public GridInterpolationMode InterpolationMode;
    [ReadOnly] public int CurrentGridIteration;

    public void Execute(int index)
    {
        GridCell cell = GridCells[index];

        if (cell.LastTouchedIteration != CurrentGridIteration || cell.Mass <= 0f)
        {
            return;
        }

        float3 supportPosition = GridUtilities.GetGridPosition(Grid, cell.Coordinates, InterpolationMode);

        // Convert the mass-weighted grid quantity back into a candidate displacement.
        cell.Displacement = cell.WeightedDisplacement / cell.Mass;

        foreach (var collider in Colliders)
        {
            float3 displacedSupportPosition = supportPosition + cell.Displacement;
            GridBoxCollider.CollisionResult collision = collider.Collide(displacedSupportPosition);
            if (collision.Collides)
            {
                float gap = math.min(0f, math.dot(collision.Normal, collision.PointOnCollider - supportPosition));
                float penetration = math.dot(collision.Normal, cell.Displacement) - gap;
                float radialImpulse = math.max(penetration, 0f);
                cell.Displacement -= radialImpulse * collision.Normal;
            }
        }

        GridCells[index] = cell;
    }
}


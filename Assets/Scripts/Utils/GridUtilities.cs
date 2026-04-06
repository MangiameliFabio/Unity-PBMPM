using Unity.Mathematics;

public static class GridUtilities
{
    public static bool CheckIfInsideBounds(in GridComponent grid, float3 position)
    {
        return position.x >= grid.GlobalCenter.x - grid.Width * 0.5f &&
               position.x <= grid.GlobalCenter.x + grid.Width * 0.5f &&
               position.y >= grid.GlobalCenter.y - grid.Height * 0.5f &&
               position.y <= grid.GlobalCenter.y + grid.Height * 0.5f &&
               position.z >= grid.GlobalCenter.z - grid.Depth * 0.5f &&
               position.z <= grid.GlobalCenter.z + grid.Depth * 0.5f;
    }
}
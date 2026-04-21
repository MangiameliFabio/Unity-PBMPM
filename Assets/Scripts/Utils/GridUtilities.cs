using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct QuadraticWeights3D
{
    public int3 BaseCoordinate;
    public float3 W0;
    public float3 W1;
    public float3 W2;

    public float GetWeight(int3 offset)
    {
        float wx = offset.x == 0 ? W0.x : offset.x == 1 ? W1.x : W2.x;
        float wy = offset.y == 0 ? W0.y : offset.y == 1 ? W1.y : W2.y;
        float wz = offset.z == 0 ? W0.z : offset.z == 1 ? W1.z : W2.z;
        return wx * wy * wz;
    }
}

public static class GridUtilities
{
    public static bool TryConvertToGridSpace(
        in GridComponent grid,
        float3 globalPosition,
        out int3 cellCounts,
        out int3 baseCoord,
        out float3 fraction)
    {
        cellCounts = default;
        baseCoord = default;
        fraction = default;

        if (!CheckIfInsideBounds(grid, globalPosition))
        {
            return false;
        }

        cellCounts = GetCellCounts(grid);
        float3 gridSpace = (globalPosition - grid.GlobalStart) / grid.CellSize - 0.5f; //Position in grid space
        baseCoord = (int3)math.floor(gridSpace); //Which cell we are in
        fraction = gridSpace - baseCoord; // Where we are in the cell
        return true;
    }

    public static int3 GetCellCounts(in GridComponent grid)
    {
        return new int3(
            math.max(1, (int)math.round(grid.Width / grid.CellSize)),
            math.max(1, (int)math.round(grid.Height / grid.CellSize)),
            math.max(1, (int)math.round(grid.Depth / grid.CellSize)));
    }

    public static int3 GetNodeCounts(in GridComponent grid)
    {
        return GetCellCounts(grid) + 1;
    }

    public static bool IsInsideCellCounts(int3 cellCounts, int3 coordinates)
    {
        return coordinates.x >= 0 && coordinates.x < cellCounts.x &&
               coordinates.y >= 0 && coordinates.y < cellCounts.y &&
               coordinates.z >= 0 && coordinates.z < cellCounts.z;
    }

    public static bool IsInsideNodeCounts(int3 nodeCounts, int3 coordinates)
    {
        return coordinates.x >= 0 && coordinates.x < nodeCounts.x &&
               coordinates.y >= 0 && coordinates.y < nodeCounts.y &&
               coordinates.z >= 0 && coordinates.z < nodeCounts.z;
    }

    public static float GetTrilinearWeight(float3 fraction, int3 offset)
    {
        float wx = offset.x == 0 ? 1f - fraction.x : fraction.x;
        float wy = offset.y == 0 ? 1f - fraction.y : fraction.y;
        float wz = offset.z == 0 ? 1f - fraction.z : fraction.z;
        return wx * wy * wz;
    }

    public static bool CheckIfInsideBounds(in GridComponent grid, float3 position)
    {
        return position.x >= grid.GlobalCenter.x - grid.Width * 0.5f &&
               position.x <= grid.GlobalCenter.x + grid.Width * 0.5f &&
               position.y >= grid.GlobalCenter.y - grid.Height * 0.5f &&
               position.y <= grid.GlobalCenter.y + grid.Height * 0.5f &&
               position.z >= grid.GlobalCenter.z - grid.Depth * 0.5f &&
               position.z <= grid.GlobalCenter.z + grid.Depth * 0.5f;
    }

    public static bool TryGetQuadraticWeights(
        in GridComponent grid,
        float3 globalPosition,
        out QuadraticWeights3D weights)
    {
        weights = default;

        if (!CheckIfInsideBounds(grid, globalPosition))
        {
            return false;
        }

        float3 x = (globalPosition - grid.GlobalStart) / grid.CellSize;
        int3 baseCoordinate = (int3)math.floor(x - 0.5f); //Determines in which cell we are
        float3 fx = x - baseCoordinate; //position relative to the cell
        float3 w0Delta = 1.5f - fx;
        float3 w1Delta = fx - 1f;
        float3 w2Delta = fx - 0.5f;

        weights = new QuadraticWeights3D
        {
            BaseCoordinate = baseCoordinate,
            W0 = 0.5f * w0Delta * w0Delta,
            W1 = 0.75f - w1Delta * w1Delta,
            W2 = 0.5f * w2Delta * w2Delta
        };

        return true;
    }

    public static bool TryResolveGrid(
        ComponentLookup<GridComponent> gridLookup,
        NativeArray<Entity> gridEntities,
        float3 position,
        ref Entity gridCache,
        out GridComponent grid)
    {
        grid = default;

        if (gridCache != Entity.Null)
        {
            GridComponent cachedGrid = gridLookup[gridCache];
            if (CheckIfInsideBounds(cachedGrid, position))
            {
                grid = cachedGrid;
                return true;
            }
        }

        foreach (Entity gridEntity in gridEntities)
        {
            GridComponent candidateGrid = gridLookup[gridEntity];
            if (!CheckIfInsideBounds(candidateGrid, position))
            {
                continue;
            }

            gridCache = gridEntity;
            grid = candidateGrid;
            return true;
        }

        gridCache = Entity.Null;
        return false;
    }

    public static bool TryGetGridCell(
        in GridComponent grid,
        DynamicBuffer<GridCell> gridCells,
        float3 globalPosition,
        out GridCell gridCell)
    {
        gridCell = default;

        if (!TryGetGridCellIndex(grid, globalPosition, out int cellIndex))
        {
            return false;
        }

        if (cellIndex < 0 || cellIndex >= gridCells.Length)
        {
            return false;
        }

        gridCell = gridCells[cellIndex];
        return true;
    }

    public static bool TryGetGridCellIndex(in GridComponent grid, float3 globalPosition, out int cellIndex)
    {
        cellIndex = -1;

        if (!CheckIfInsideBounds(grid, globalPosition))
        {
            return false;
        }

        int3 cellCounts = GetCellCounts(grid);

        float3 localPosition = globalPosition - grid.GlobalStart;
        int x = math.clamp((int)math.floor(localPosition.x / grid.CellSize), 0, cellCounts.x - 1);
        int y = math.clamp((int)math.floor(localPosition.y / grid.CellSize), 0, cellCounts.y - 1);
        int z = math.clamp((int)math.floor(localPosition.z / grid.CellSize), 0, cellCounts.z - 1);

        cellIndex = GetGridCellIndex(cellCounts, x, y, z);
        return true;
    }

    public static int GetGridCellIndex(int3 cellCounts, int x, int y, int z)
    {
        return (x * cellCounts.y * cellCounts.z) + (y * cellCounts.z) + z;
    }

    public static int GetGridIndex(int3 counts, int x, int y, int z)
    {
        return (x * counts.y * counts.z) + (y * counts.z) + z;
    }

    public static float3 GetGlobalPositionFromCellCoordinates(in GridComponent grid, int3 coordinates)
    {
        return grid.GlobalStart + (new float3(coordinates) + 0.5f) * grid.CellSize;
    }

    public static float3 GetGlobalPositionFromNodeCoordinates(in GridComponent grid, int3 coordinates)
    {
        return grid.GlobalStart + new float3(coordinates) * grid.CellSize;
    }

    public static float3 GetGridPosition(in GridComponent grid, int3 coordinates, GridInterpolationMode interpolationMode)
    {
        return interpolationMode == GridInterpolationMode.QuadraticBSplineNodes
            ? GetGlobalPositionFromNodeCoordinates(grid, coordinates)
            : GetGlobalPositionFromCellCoordinates(grid, coordinates);
    }

    public static float3x3 OuterProduct(float3 left, float3 right)
    {
        return new float3x3(
            left * right.x,
            left * right.y,
            left * right.z);
    }
}

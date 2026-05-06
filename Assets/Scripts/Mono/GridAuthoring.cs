using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GridAuthoring : MonoBehaviour
{
    private static readonly Vector3 DefaultBoundsSize = Vector3.one;
    public const float MinCellSize = 0.01f;

    public Bounds gridBounds = new Bounds(Vector3.zero, DefaultBoundsSize);
    [Min(MinCellSize)] public float cellSize = 5f;

    [Header("Debug")]
    public bool showDebugGrid;
    public bool showOnlyOuterFaces;
    public Color boundsColor = Color.cyan;
    public Color gridColor = new Color(0, 0.9f, 0, 0.6f);

    private void Reset()
    {
        EnsureVisibleBounds();
    }

    private void OnValidate()
    {
        EnsureVisibleBounds();
    }

    private void EnsureVisibleBounds()
    {
        if (gridBounds.extents == Vector3.zero)
        {
            gridBounds = new Bounds(gridBounds.center, DefaultBoundsSize);
        }

        if (cellSize < MinCellSize)
        {
            cellSize = MinCellSize;
        }
    }

    public void ValidateRuntimeValues()
    {
        EnsureVisibleBounds();
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = boundsColor;
        Gizmos.DrawWireCube(gridBounds.center, gridBounds.size);

        if (showDebugGrid)
        {
            DrawDebugGrid();
        }

        Gizmos.matrix = oldMatrix;
    }

    private void DrawDebugGrid()
    {
        int cellCountX = Mathf.Max(1, Mathf.RoundToInt(gridBounds.size.x / cellSize));
        int cellCountY = Mathf.Max(1, Mathf.RoundToInt(gridBounds.size.y / cellSize));
        int cellCountZ = Mathf.Max(1, Mathf.RoundToInt(gridBounds.size.z / cellSize));

        Vector3 min = gridBounds.min;
        Vector3 max = gridBounds.max;

        Gizmos.color = gridColor;

        if (showOnlyOuterFaces)
        {
            DrawOuterFaceGrid(min, max, cellCountX, cellCountY, cellCountZ);
        }
        else
        {
            DrawFullGrid(min, max, cellCountX, cellCountY, cellCountZ);
        }
    }

    private static void DrawFullGrid(Vector3 min, Vector3 max, int cellCountX, int cellCountY, int cellCountZ)
    {
        for (int x = 0; x <= cellCountX; x++)
        {
            float xPos = Mathf.Lerp(min.x, max.x, x / (float)cellCountX);
            for (int y = 0; y <= cellCountY; y++)
            {
                float yPos = Mathf.Lerp(min.y, max.y, y / (float)cellCountY);
                Gizmos.DrawLine(new Vector3(xPos, yPos, min.z), new Vector3(xPos, yPos, max.z));
            }
        }

        for (int x = 0; x <= cellCountX; x++)
        {
            float xPos = Mathf.Lerp(min.x, max.x, x / (float)cellCountX);
            for (int z = 0; z <= cellCountZ; z++)
            {
                float zPos = Mathf.Lerp(min.z, max.z, z / (float)cellCountZ);
                Gizmos.DrawLine(new Vector3(xPos, min.y, zPos), new Vector3(xPos, max.y, zPos));
            }
        }

        for (int y = 0; y <= cellCountY; y++)
        {
            float yPos = Mathf.Lerp(min.y, max.y, y / (float)cellCountY);
            for (int z = 0; z <= cellCountZ; z++)
            {
                float zPos = Mathf.Lerp(min.z, max.z, z / (float)cellCountZ);
                Gizmos.DrawLine(new Vector3(min.x, yPos, zPos), new Vector3(max.x, yPos, zPos));
            }
        }
    }

    private static void DrawOuterFaceGrid(Vector3 min, Vector3 max, int cellCountX, int cellCountY, int cellCountZ)
    {
        for (int x = 0; x <= cellCountX; x++)
        {
            float xPos = Mathf.Lerp(min.x, max.x, x / (float)cellCountX);
            Gizmos.DrawLine(new Vector3(xPos, min.y, min.z), new Vector3(xPos, max.y, min.z));
            Gizmos.DrawLine(new Vector3(xPos, min.y, max.z), new Vector3(xPos, max.y, max.z));
            Gizmos.DrawLine(new Vector3(xPos, min.y, min.z), new Vector3(xPos, min.y, max.z));
            Gizmos.DrawLine(new Vector3(xPos, max.y, min.z), new Vector3(xPos, max.y, max.z));
        }

        for (int y = 0; y <= cellCountY; y++)
        {
            float yPos = Mathf.Lerp(min.y, max.y, y / (float)cellCountY);
            Gizmos.DrawLine(new Vector3(min.x, yPos, min.z), new Vector3(max.x, yPos, min.z));
            Gizmos.DrawLine(new Vector3(min.x, yPos, max.z), new Vector3(max.x, yPos, max.z));
            Gizmos.DrawLine(new Vector3(min.x, yPos, min.z), new Vector3(min.x, yPos, max.z));
            Gizmos.DrawLine(new Vector3(max.x, yPos, min.z), new Vector3(max.x, yPos, max.z));
        }

        for (int z = 0; z <= cellCountZ; z++)
        {
            float zPos = Mathf.Lerp(min.z, max.z, z / (float)cellCountZ);
            Gizmos.DrawLine(new Vector3(min.x, min.y, zPos), new Vector3(max.x, min.y, zPos));
            Gizmos.DrawLine(new Vector3(min.x, max.y, zPos), new Vector3(max.x, max.y, zPos));
            Gizmos.DrawLine(new Vector3(min.x, min.y, zPos), new Vector3(min.x, max.y, zPos));
            Gizmos.DrawLine(new Vector3(max.x, min.y, zPos), new Vector3(max.x, max.y, zPos));
        }
    }

    public void SnapBoundsToCellSize()
    {
        Vector3 snappedSize = new Vector3(
            SnapAxisToCellSize(gridBounds.size.x),
            SnapAxisToCellSize(gridBounds.size.y),
            SnapAxisToCellSize(gridBounds.size.z));

        if (gridBounds.size != snappedSize)
        {
            gridBounds = new Bounds(gridBounds.center, snappedSize);
        }
    }

    private float SnapAxisToCellSize(float axisSize)
    {
        int cellCount = Mathf.Max(1, Mathf.CeilToInt(axisSize / cellSize));
        return cellCount * cellSize;
    }

    public GridComponent CreateGridComponent()
    {
        float width = gridBounds.max.x - gridBounds.min.x;
        float height = gridBounds.max.y - gridBounds.min.y;
        float depth = gridBounds.max.z - gridBounds.min.z;

        return new GridComponent
        {
            GlobalCenter = gridBounds.center + transform.position,
            GlobalStart = gridBounds.min + transform.position,
            Width = width,
            Height = height,
            Depth = depth,
            CellSize = cellSize
        };
    }

    public void RebuildGridCells(DynamicBuffer<GridCell> gridCells)
    {
        GridComponent grid = CreateGridComponent();
        int3 cellCounts = GridUtilities.GetCellCounts(grid);
        int3 nodeCounts = cellCounts + 1;

        gridCells.Clear();
        gridCells.EnsureCapacity(nodeCounts.x * nodeCounts.y * nodeCounts.z);

        for (int x = 0; x < nodeCounts.x; x++)
        {
            for (int y = 0; y < nodeCounts.y; y++)
            {
                for (int z = 0; z < nodeCounts.z; z++)
                {
                    gridCells.Add(new GridCell
                    {
                        Coordinates = new int3(x, y, z),
                        WeightedDisplacement = float3.zero,
                        Displacement = float3.zero,
                        Mass = 0f,
                        Volume = 0f,
                        LastTouchedIteration = 0
                    });
                }
            }
        }

        gridCells.TrimExcess();
    }
}

class GridAuthoringBaker : Baker<GridAuthoring>
{
    public override void Bake(GridAuthoring authoring)
    {
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, authoring.CreateGridComponent());
        AddComponent(entity, new GridAuthoringReference
        {
            AuthoringInstanceId = authoring.GetInstanceID()
        });
        var gridCells = AddBuffer<GridCell>(entity);
        authoring.RebuildGridCells(gridCells);
    }
}

public struct GridAuthoringReference : IComponentData
{
    public int AuthoringInstanceId;
}

public struct GridComponent : IComponentData
{
    public float3 GlobalCenter;
    public float3 GlobalStart;
    public float Width, Height, Depth;
    public float CellSize;
}

public struct GridCell : IBufferElementData
{
    public int3 Coordinates;
    public float3 WeightedDisplacement;
    public float3 Displacement;
    
    public float Mass;
    public float Volume;
    
    public int LastTouchedIteration;
}

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GridAuthoring : MonoBehaviour
{
    private static readonly Vector3 DefaultBoundsSize = Vector3.one;
    private const float MinCellSize = 0.01f;

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
}

class GridAuthoringBaker : Baker<GridAuthoring>
{
    public override void Bake(GridAuthoring authoring)
    {
        var width = authoring.gridBounds.max.x - authoring.gridBounds.min.x;
        var height = authoring.gridBounds.max.y - authoring.gridBounds.min.y;
        var depth = authoring.gridBounds.max.z - authoring.gridBounds.min.z;
            
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
        AddComponent(entity, new GridComponent
        {
            GlobalCenter = authoring.gridBounds.center + authoring.transform.position,
            GlobalStart = authoring.gridBounds.min + authoring.transform.position,
            
            Width = width,
            Height = height,
            Depth = depth,
            
            CellSize = authoring.cellSize
        });
        var gridCells = AddBuffer<GridCell>(entity);

        var cellCountX = Mathf.Max(1, Mathf.RoundToInt(width / authoring.cellSize));
        var cellCountY = Mathf.Max(1, Mathf.RoundToInt(height / authoring.cellSize));
        var cellCountZ = Mathf.Max(1, Mathf.RoundToInt(depth / authoring.cellSize));

        for (int x = 0; x < cellCountX; x++)
        {
            for (int y = 0; y < cellCountY; y++)
            {
                for (int z = 0; z < cellCountZ; z++)
                {
                    gridCells.Add(new GridCell
                    {
                        GlobalCenter = (float3)(authoring.gridBounds.min + authoring.transform.position) + new float3(x, y, z) * authoring.cellSize + 0.5f * authoring.cellSize,
                        Momentum = float3.zero,
                        Velocity = float3.zero,
                        Mass = 0f,
                        Volume = 0f
                    });
                }
            }
        }

        gridCells.TrimExcess();
    }
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
    public float3 GlobalCenter;
    public float3 Momentum;
    public float3 Velocity;
    public float Mass;
    public float Volume;
}

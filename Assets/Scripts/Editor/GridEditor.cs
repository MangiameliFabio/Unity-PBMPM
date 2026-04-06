using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(GridAuthoring))]
public class GridAuthoringEditor : Editor
{
    private static readonly Color HandleColor = new Color32(145, 244, 139, 210);

    private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
    private SerializedProperty boundsProperty;
    private SerializedProperty centerProperty;
    private SerializedProperty extentsProperty;

    private void OnEnable()
    {
        boundsProperty = serializedObject.FindProperty("gridBounds");
        centerProperty = boundsProperty?.FindPropertyRelative("m_Center");
        extentsProperty = boundsProperty?.FindPropertyRelative("m_Extent");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            GridAuthoring grid = (GridAuthoring)target;
            Undo.RecordObject(grid, "Edit Grid Settings");
            grid.SnapBoundsToCellSize();
            EditorUtility.SetDirty(grid);
            serializedObject.Update();
        }

        EditorGUILayout.HelpBox("Select the Grid object in the Scene view to edit its bounds handle.", MessageType.None);
    }

    private void OnSceneGUI()
    {
        if (centerProperty == null || extentsProperty == null)
        {
            return;
        }

        serializedObject.Update();

        GridAuthoring grid = (GridAuthoring)target;
        Bounds previousBounds = grid.gridBounds;
        boundsHandle.center = centerProperty.vector3Value;

        Vector3 handleSize = extentsProperty.vector3Value * 2f;
        if (handleSize == Vector3.zero)
        {
            handleSize = Vector3.one;
        }

        boundsHandle.size = handleSize;

        using (new Handles.DrawingScope(HandleColor, grid.transform.localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            boundsHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(grid, "Edit Grid Bounds");

                Bounds editedBounds = new Bounds(boundsHandle.center, boundsHandle.size);
                Bounds snappedBounds = SnapBoundsForHandleEdit(previousBounds, editedBounds, grid.cellSize);

                centerProperty.vector3Value = snappedBounds.center;
                extentsProperty.vector3Value = snappedBounds.extents;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(grid);
            }
        }
    }

    private static Bounds SnapBoundsForHandleEdit(Bounds previousBounds, Bounds editedBounds, float cellSize)
    {
        Vector3 min = editedBounds.min;
        Vector3 max = editedBounds.max;

        SnapAxisForHandleEdit(previousBounds.min.x, previousBounds.max.x, ref min.x, ref max.x, cellSize);
        SnapAxisForHandleEdit(previousBounds.min.y, previousBounds.max.y, ref min.y, ref max.y, cellSize);
        SnapAxisForHandleEdit(previousBounds.min.z, previousBounds.max.z, ref min.z, ref max.z, cellSize);

        Bounds snappedBounds = new Bounds();
        snappedBounds.SetMinMax(min, max);
        return snappedBounds;
    }

    private static void SnapAxisForHandleEdit(float previousMin, float previousMax, ref float editedMin, ref float editedMax, float cellSize)
    {
        float minDelta = Mathf.Abs(editedMin - previousMin);
        float maxDelta = Mathf.Abs(editedMax - previousMax);
        float snappedSize = Mathf.Max(cellSize, Mathf.Ceil((editedMax - editedMin) / cellSize) * cellSize);
        const float epsilon = 0.0001f;

        if (minDelta > epsilon && maxDelta <= epsilon)
        {
            editedMin = previousMax - snappedSize;
            editedMax = previousMax;
            return;
        }

        if (maxDelta > epsilon && minDelta <= epsilon)
        {
            editedMin = previousMin;
            editedMax = previousMin + snappedSize;
            return;
        }

        float center = (editedMin + editedMax) * 0.5f;
        float halfSize = snappedSize * 0.5f;
        editedMin = center - halfSize;
        editedMax = center + halfSize;
    }
}

[CustomEditor(typeof(SpawnShapeAuthoring))]
public class SpawnShapeAuthoringEditor : Editor
{
    private static readonly Color HandleColor = new Color32(200, 200, 10, 210);

    private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
    private SerializedProperty boundsProperty;
    private SerializedProperty centerProperty;
    private SerializedProperty extentsProperty;

    private void OnEnable()
    {
        boundsProperty = serializedObject.FindProperty("spawnBounds");
        centerProperty = boundsProperty?.FindPropertyRelative("m_Center");
        extentsProperty = boundsProperty?.FindPropertyRelative("m_Extent");
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Select the Spawn Shape object in the Scene view to edit its bounds handle.", MessageType.None);
    }

    private void OnSceneGUI()
    {
        if (centerProperty == null || extentsProperty == null)
        {
            return;
        }

        serializedObject.Update();

        SpawnShapeAuthoring spawnShape = (SpawnShapeAuthoring)target;
        boundsHandle.center = centerProperty.vector3Value;

        Vector3 handleSize = extentsProperty.vector3Value * 2f;
        if (handleSize == Vector3.zero)
        {
            handleSize = Vector3.one;
        }

        boundsHandle.size = handleSize;

        using (new Handles.DrawingScope(HandleColor, spawnShape.transform.localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            boundsHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(spawnShape, "Edit Spawn Bounds");
                centerProperty.vector3Value = boundsHandle.center;
                extentsProperty.vector3Value = boundsHandle.size * 0.5f;
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}

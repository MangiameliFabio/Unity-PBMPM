using Unity.Entities;
using UnityEngine;

public class SimulationDebugOverlay : MonoBehaviour
{
    private const int SampleCount = 300;
    private readonly float[] _fpsSamples = new float[SampleCount];
    private readonly float[] _iterationSamples = new float[SampleCount];

    private int _sampleIndex;
    private int _filledSamples;
    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;
    private EntityQuery _statsQuery;
    private EntityQuery _configQuery;
    private World _cachedWorld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOverlay()
    {
        if (FindFirstObjectByType<SimulationDebugOverlay>() != null)
        {
            return;
        }

        var overlayObject = new GameObject("Simulation Debug Overlay");
        DontDestroyOnLoad(overlayObject);
        overlayObject.AddComponent<SimulationDebugOverlay>();
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        float fps = deltaTime > 1e-6f ? 1f / deltaTime : 0f;
        _fpsSamples[_sampleIndex] = fps;

        int solverIterations = 0;
        if (TryGetSimulationStats(out SimulationDebugStats stats))
        {
            solverIterations = stats.SolverIterations;
        }

        _iterationSamples[_sampleIndex] = solverIterations;
        _sampleIndex = (_sampleIndex + 1) % SampleCount;
        _filledSamples = Mathf.Min(_filledSamples + 1, SampleCount);
    }

    private void OnGUI()
    {
        EnsureStyles();

        int particleCount = 0;
        GridInterpolationMode interpolationMode = GridInterpolationMode.QuadraticBSplineNodes;
        bool useGridVolumePreservation = true;
        bool useVisualSmoothing = true;
        if (TryGetSimulationStats(out SimulationDebugStats stats))
        {
            particleCount = stats.ParticleCount;
        }
        if (TryGetConfig(out Config config))
        {
            interpolationMode = config.InterpolationMode;
            useGridVolumePreservation = config.UseGridVolumePreservation;
            useVisualSmoothing = config.UseVisualSmoothing;
        }

        float averageFps = GetAverage(_fpsSamples, _filledSamples);
        float averageIterations = GetAverage(_iterationSamples, _filledSamples);

        GUILayout.BeginArea(new Rect(12f, 12f, 340f, 210f), _boxStyle);
        GUILayout.Label($"Avg FPS (300f): {averageFps:F1}", _labelStyle);
        GUILayout.Label($"Particles: {particleCount}", _labelStyle);
        GUILayout.Label($"Avg Solver Iterations: {averageIterations:F2}", _labelStyle);
        GUILayout.Label($"Interpolation: {interpolationMode}", _labelStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Trilinear", GUILayout.Height(28f)))
        {
            SetInterpolationMode(GridInterpolationMode.TrilinearCellCentered);
        }

        if (GUILayout.Button("Quadratic", GUILayout.Height(28f)))
        {
            SetInterpolationMode(GridInterpolationMode.QuadraticBSplineNodes);
        }
        GUILayout.EndHorizontal();
        bool toggledGridVolumePreservation = GUILayout.Toggle(useGridVolumePreservation, " Grid Volume Preservation");
        if (toggledGridVolumePreservation != useGridVolumePreservation)
        {
            SetGridVolumePreservation(toggledGridVolumePreservation);
        }
        bool toggledVisualSmoothing = GUILayout.Toggle(useVisualSmoothing, " Particle Visual Smoothing");
        if (toggledVisualSmoothing != useVisualSmoothing)
        {
            SetVisualSmoothing(toggledVisualSmoothing);
        }
        GUILayout.EndArea();
    }

    private bool TryGetSimulationStats(out SimulationDebugStats stats)
    {
        stats = default;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        if (_cachedWorld != world || _statsQuery == default)
        {
            _cachedWorld = world;
            _statsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationDebugStats>());
            _configQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Config>());
        }

        if (_statsQuery.IsEmptyIgnoreFilter)
        {
            return false;
        }

        stats = _statsQuery.GetSingleton<SimulationDebugStats>();
        return true;
    }

    private bool TryGetConfig(out Config config)
    {
        config = default;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        if (_cachedWorld != world || _configQuery == default)
        {
            _cachedWorld = world;
            _statsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationDebugStats>());
            _configQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Config>());
        }

        if (_configQuery.IsEmptyIgnoreFilter)
        {
            return false;
        }

        config = _configQuery.GetSingleton<Config>();
        return true;
    }

    private void SetVisualSmoothing(bool useVisualSmoothing)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }
        
        EntityManager entityManager = world.EntityManager;
        if (_cachedWorld != world || _configQuery == default)
        {
            _cachedWorld = world;
            _configQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Config>());
        }
        
        if (_configQuery.IsEmptyIgnoreFilter)
        {
            return;
        }
        
        Config config = _configQuery.GetSingleton<Config>();
        if (config.UseVisualSmoothing == useVisualSmoothing)
        {
            return;
        }
        
        config.UseVisualSmoothing = useVisualSmoothing;
        entityManager.SetComponentData(_configQuery.GetSingletonEntity(), config);
    }

    private void SetInterpolationMode(GridInterpolationMode interpolationMode)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        if (_cachedWorld != world || _configQuery == default)
        {
            _cachedWorld = world;
            _statsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationDebugStats>());
            _configQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Config>());
        }

        if (_configQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        Config config = _configQuery.GetSingleton<Config>();
        if (config.InterpolationMode == interpolationMode)
        {
            return;
        }

        config.InterpolationMode = interpolationMode;
        entityManager.SetComponentData(_configQuery.GetSingletonEntity(), config);
    }

    private void SetGridVolumePreservation(bool useGridVolumePreservation)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        if (_cachedWorld != world || _configQuery == default)
        {
            _cachedWorld = world;
            _statsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationDebugStats>());
            _configQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Config>());
        }

        if (_configQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        Config config = _configQuery.GetSingleton<Config>();
        if (config.UseGridVolumePreservation == useGridVolumePreservation)
        {
            return;
        }

        config.UseGridVolumePreservation = useGridVolumePreservation;
        entityManager.SetComponentData(_configQuery.GetSingletonEntity(), config);
    }

    private static float GetAverage(float[] values, int count)
    {
        if (count <= 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            sum += values[i];
        }

        return sum / count;
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null)
        {
            return;
        }

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            richText = false
        };
        _labelStyle.normal.textColor = Color.white;

        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(12, 12, 10, 10)
        };
    }
}

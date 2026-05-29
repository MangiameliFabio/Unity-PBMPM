using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SimulationDebugOverlay : MonoBehaviour
{
    private const int SampleCount = 300;
    private const int PipelineSolverStepsBetweenLogs = 200;
    private const int PipelineSolverStepsBeforeFirstLog = 250;
    private readonly float[] _fpsSamples = new float[SampleCount];
    private readonly float[] _iterationSamples = new float[SampleCount];

    private int _sampleIndex;
    private int _filledSamples;
    private int _currentSolverSubsteps;
    private bool _isVisible = true;
    private Vector2 _scrollPosition;
    private string _updateFrequencyText;
    private string _iterationCountText;
    private string _solverRunTimeMinText;
    private string _solverRunTimeMaxText;
    private string _solverRunTimeMeanText;
    private string _solverRunTimeSampleCountText;
    private string _solverRunTimeStatusText;
    private string _logRepeatCountText;
    private string _pipelineStartSpawnAmountText;
    private string _pipelineEndSpawnAmountText;
    private string _pipelineStatusText;
    private string _frequencyPipelineStartText;
    private string _frequencyPipelineEndText;
    private string _frequencyPipelineStepText;
    private string _frequencyPipelineStatusText;
    private string _iterationPipelineStartText;
    private string _iterationPipelineEndText;
    private string _iterationPipelineStepText;
    private string _iterationPipelineStatusText;
    private string _cellSizePipelineStartText;
    private string _cellSizePipelineEndText;
    private string _cellSizePipelineStepText;
    private string _cellSizePipelineStatusText;
    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;
    private EntityQuery _statsQuery;
    private EntityQuery _configQuery;
    private World _cachedWorld;
    private readonly List<GridDebugEntry> _gridEntries = new List<GridDebugEntry>();
    private readonly List<SpawnerDebugEntry> _spawnerEntries = new List<SpawnerDebugEntry>();
    private const float PipelineWaitBeforeLoggingSeconds = 10f;
    private int _pendingScheduledLogCount;
    private bool _isScheduledLoggingActive;
    private int _scheduledLogAccumulatedSolverSubsteps;
    private bool _isPipelineActive;
    private PipelineStage _pipelineStage;
    private PipelineKind _pipelineKind;
    private int _pipelineCurrentSpawnAmount;
    private int _pipelineEndSpawnAmount;
    private float _pipelineNextActionTime;
    private float _frequencyPipelineCurrentValue;
    private float _frequencyPipelineEndValue;
    private float _frequencyPipelineStepValue;
    private int _iterationPipelineCurrentValue;
    private int _iterationPipelineEndValue;
    private int _iterationPipelineStepValue;
    private float _cellSizePipelineCurrentValue;
    private float _cellSizePipelineEndValue;
    private float _cellSizePipelineStepValue;
    private int _pipelineRemainingLogs;
    private int _pipelineAccumulatedSolverSubsteps;

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

    private void Awake()
    {
        ResetSolverRunTimeDisplay();
        _logRepeatCountText = "1";
        _pipelineStartSpawnAmountText = "15";
        _pipelineEndSpawnAmountText = "40";
        _frequencyPipelineStartText = "15";
        _frequencyPipelineEndText = "40";
        _frequencyPipelineStepText = "1";
        _iterationPipelineStartText = "1";
        _iterationPipelineEndText = "10";
        _iterationPipelineStepText = "1";
        _cellSizePipelineStartText = "0.1";
        _cellSizePipelineEndText = "1";
        _cellSizePipelineStepText = "0.1";
        _pipelineStatusText = "Pipeline idle.";
        _frequencyPipelineStatusText = "Pipeline idle.";
        _iterationPipelineStatusText = "Pipeline idle.";
        _cellSizePipelineStatusText = "Pipeline idle.";
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
            _currentSolverSubsteps = stats.SolverSubsteps;
        }
        else
        {
            _currentSolverSubsteps = 0;
        }

        _iterationSamples[_sampleIndex] = solverIterations;
        _sampleIndex = (_sampleIndex + 1) % SampleCount;
        _filledSamples = Mathf.Min(_filledSamples + 1, SampleCount);

        UpdateScheduledStatLogging();
        UpdateSpawnerSweepPipeline();
    }

    private void OnDestroy()
    {
        ReleaseCachedQueries();
    }

    private void OnGUI()
    {
        EnsureStyles();

        if (!_isVisible)
        {
            if (GUI.Button(new Rect(12f, 12f, 110f, 32f), "Show Debug"))
            {
                _isVisible = true;
            }

            return;
        }

        int particleCount = 0;
        GridInterpolationMode interpolationMode = GridInterpolationMode.QuadraticBSplineNodes;
        bool useGridVolumePreservation = true;
        bool useVisualSmoothing = true;
        float updateFrequency = 60f;
        int iterationCount = 1;
        if (TryGetSimulationStats(out SimulationDebugStats stats))
        {
            particleCount = stats.ParticleCount;
        }
        if (TryGetConfig(out Config config))
        {
            updateFrequency = config.UpdateFrequency;
            iterationCount = config.IterationCount;
            interpolationMode = config.InterpolationMode;
            useGridVolumePreservation = config.UseGridVolumePreservation;
            useVisualSmoothing = config.UseVisualSmoothing;
        }

        if (string.IsNullOrWhiteSpace(_updateFrequencyText))
        {
            _updateFrequencyText = updateFrequency.ToString("0.###");
        }

        if (string.IsNullOrWhiteSpace(_iterationCountText))
        {
            _iterationCountText = iterationCount.ToString();
        }

        float averageFps = GetAverage(_fpsSamples, _filledSamples);
        float averageIterations = GetAverage(_iterationSamples, _filledSamples);
        float averageSolverFrequency = averageFps * averageIterations;
        RefreshDebugEntries();

        float panelWidth = Mathf.Min(Screen.width - 24f, 460f);
        float panelHeight = Mathf.Max(220f, Screen.height - 24f);
        GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, panelHeight), _boxStyle);
        _scrollPosition = GUILayout.BeginScrollView(
            _scrollPosition,
            false,
            true,
            GUILayout.Width(panelWidth - 24f),
            GUILayout.Height(panelHeight - 20f));
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Hide Debug", GUILayout.Width(110f), GUILayout.Height(26f)))
        {
            _isVisible = false;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label($"Avg FPS (300f): {averageFps:F1}", _labelStyle);
        GUILayout.Label($"Particles: {particleCount}", _labelStyle);
        GUILayout.Label($"Avg Solver Iterations per frame: {averageIterations:F2}", _labelStyle);
        GUILayout.Label($"Solver Frequency: {averageSolverFrequency:F1}/s", _labelStyle);
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

        GUILayout.Space(8f);
        GUILayout.Label("Solver", _labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Frequency", _labelStyle, GUILayout.Width(75f));
        _updateFrequencyText = GUILayout.TextField(_updateFrequencyText, GUILayout.Width(80f));
        GUILayout.Label("Iterations", _labelStyle, GUILayout.Width(70f));
        _iterationCountText = GUILayout.TextField(_iterationCountText, GUILayout.Width(60f));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Apply Solver Settings", GUILayout.Height(28f)))
        {
            bool applied = false;
            if (float.TryParse(_updateFrequencyText, out float parsedFrequency))
            {
                SetUpdateFrequency(parsedFrequency);
                applied = true;
            }

            if (int.TryParse(_iterationCountText, out int parsedIterationCount))
            {
                SetIterationCount(parsedIterationCount);
                applied = true;
            }

            if (applied && TryGetConfig(out Config updatedConfig))
            {
                _updateFrequencyText = updatedConfig.UpdateFrequency.ToString("0.###");
                _iterationCountText = updatedConfig.IterationCount.ToString();
            }
        }

        GUILayout.Space(8f);
        GUILayout.Label($"Solver Runtime ({PBMPMProfilerCaptureStats.SolverRunSampleWindow} running frames)", _labelStyle);
        GUILayout.Label($"Min: {_solverRunTimeMinText}", _labelStyle);
        GUILayout.Label($"Max: {_solverRunTimeMaxText}", _labelStyle);
        GUILayout.Label($"Mean: {_solverRunTimeMeanText}", _labelStyle);
        GUILayout.Label(_solverRunTimeSampleCountText, _labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Logs", _labelStyle, GUILayout.Width(40f));
        _logRepeatCountText = GUILayout.TextField(_logRepeatCountText, GUILayout.Width(45f));
        GUILayout.EndHorizontal();
        GUILayout.Label($"Manual logging: first log now, then every {PipelineSolverStepsBetweenLogs} solver steps", _labelStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Update Runtime Stats", GUILayout.Height(28f)))
        {
            RefreshSolverRunTimeDisplay();
        }

        if (GUILayout.Button("Log Stats", GUILayout.Height(28f)))
        {
            StartScheduledSolverRunTimeLogging();
        }
        GUILayout.EndHorizontal();
        if (_pendingScheduledLogCount > 0 && GUILayout.Button("Cancel Scheduled Logs", GUILayout.Height(24f)))
        {
            CancelScheduledSolverRunTimeLogging("Scheduled logging canceled.");
        }
        GUILayout.Label(_solverRunTimeStatusText, _labelStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Particle Logging Pipeline", _labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Start", _labelStyle, GUILayout.Width(40f));
        _pipelineStartSpawnAmountText = GUILayout.TextField(_pipelineStartSpawnAmountText, GUILayout.Width(45f));
        GUILayout.Label("End", _labelStyle, GUILayout.Width(30f));
        _pipelineEndSpawnAmountText = GUILayout.TextField(_pipelineEndSpawnAmountText, GUILayout.Width(45f));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Run Particle Logging Pipeline", GUILayout.Height(28f)))
        {
            StartSpawnerSweepPipeline();
        }
        if (_isPipelineActive && _pipelineKind == PipelineKind.ParticleLogging &&
            GUILayout.Button("Cancel Particle Logging Pipeline", GUILayout.Height(24f)))
        {
            CancelSpawnerSweepPipeline("Particle logging pipeline canceled.");
        }
        GUILayout.Label(_pipelineStatusText, _labelStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Frequency Logging Pipeline", _labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Start", _labelStyle, GUILayout.Width(40f));
        _frequencyPipelineStartText = GUILayout.TextField(_frequencyPipelineStartText, GUILayout.Width(45f));
        GUILayout.Label("End", _labelStyle, GUILayout.Width(30f));
        _frequencyPipelineEndText = GUILayout.TextField(_frequencyPipelineEndText, GUILayout.Width(45f));
        GUILayout.Label("Step", _labelStyle, GUILayout.Width(35f));
        _frequencyPipelineStepText = GUILayout.TextField(_frequencyPipelineStepText, GUILayout.Width(45f));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Run Frequency Logging Pipeline", GUILayout.Height(28f)))
        {
            StartFrequencyLoggingPipeline();
        }
        if (_isPipelineActive && _pipelineKind == PipelineKind.FrequencyLogging &&
            GUILayout.Button("Cancel Frequency Logging Pipeline", GUILayout.Height(24f)))
        {
            CancelSpawnerSweepPipeline("Frequency logging pipeline canceled.");
        }
        GUILayout.Label(_frequencyPipelineStatusText, _labelStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Iteration Logging Pipeline", _labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Start", _labelStyle, GUILayout.Width(40f));
        _iterationPipelineStartText = GUILayout.TextField(_iterationPipelineStartText, GUILayout.Width(45f));
        GUILayout.Label("End", _labelStyle, GUILayout.Width(30f));
        _iterationPipelineEndText = GUILayout.TextField(_iterationPipelineEndText, GUILayout.Width(45f));
        GUILayout.Label("Step", _labelStyle, GUILayout.Width(35f));
        _iterationPipelineStepText = GUILayout.TextField(_iterationPipelineStepText, GUILayout.Width(45f));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Run Iteration Logging Pipeline", GUILayout.Height(28f)))
        {
            StartIterationLoggingPipeline();
        }
        if (_isPipelineActive && _pipelineKind == PipelineKind.IterationLogging &&
            GUILayout.Button("Cancel Iteration Logging Pipeline", GUILayout.Height(24f)))
        {
            CancelSpawnerSweepPipeline("Iteration logging pipeline canceled.");
        }
        GUILayout.Label(_iterationPipelineStatusText, _labelStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Cell Size Logging Pipeline", _labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Start", _labelStyle, GUILayout.Width(40f));
        _cellSizePipelineStartText = GUILayout.TextField(_cellSizePipelineStartText, GUILayout.Width(45f));
        GUILayout.Label("End", _labelStyle, GUILayout.Width(30f));
        _cellSizePipelineEndText = GUILayout.TextField(_cellSizePipelineEndText, GUILayout.Width(45f));
        GUILayout.Label("Step", _labelStyle, GUILayout.Width(35f));
        _cellSizePipelineStepText = GUILayout.TextField(_cellSizePipelineStepText, GUILayout.Width(45f));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Run Cell Size Logging Pipeline", GUILayout.Height(28f)))
        {
            StartCellSizeLoggingPipeline();
        }
        if (_isPipelineActive && _pipelineKind == PipelineKind.CellSizeLogging &&
            GUILayout.Button("Cancel Cell Size Logging Pipeline", GUILayout.Height(24f)))
        {
            CancelSpawnerSweepPipeline("Cell size logging pipeline canceled.");
        }
        GUILayout.Label(_cellSizePipelineStatusText, _labelStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Grids", _labelStyle);
        foreach (GridDebugEntry entry in _gridEntries)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(entry.Label, _labelStyle, GUILayout.Width(190f));
            GUILayout.Label("Cell Size", _labelStyle, GUILayout.Width(70f));
            entry.CellSizeText = GUILayout.TextField(entry.CellSizeText, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(12f);
            GUILayout.Label($"Cell Count {entry.CellCountText}", _labelStyle);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8f);
        GUILayout.Label("Spawners", _labelStyle);
        foreach (SpawnerDebugEntry entry in _spawnerEntries)
        {
            GUILayout.Label(entry.Label, _labelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", _labelStyle, GUILayout.Width(18f));
            entry.SpawnAmountXText = GUILayout.TextField(entry.SpawnAmountXText, GUILayout.Width(55f));
            GUILayout.Label("Y", _labelStyle, GUILayout.Width(18f));
            entry.SpawnAmountYText = GUILayout.TextField(entry.SpawnAmountYText, GUILayout.Width(55f));
            GUILayout.Label("Z", _labelStyle, GUILayout.Width(18f));
            entry.SpawnAmountZText = GUILayout.TextField(entry.SpawnAmountZText, GUILayout.Width(55f));
            GUILayout.Label("All", _labelStyle, GUILayout.Width(24f));
            entry.SpawnAmountAllText = GUILayout.TextField(entry.SpawnAmountAllText, GUILayout.Width(55f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Extent X", _labelStyle, GUILayout.Width(70f));
            entry.ExtentXText = GUILayout.TextField(entry.ExtentXText, GUILayout.Width(55f));
            GUILayout.Label("Y", _labelStyle, GUILayout.Width(18f));
            entry.ExtentYText = GUILayout.TextField(entry.ExtentYText, GUILayout.Width(55f));
            GUILayout.Label("Z", _labelStyle, GUILayout.Width(18f));
            entry.ExtentZText = GUILayout.TextField(entry.ExtentZText, GUILayout.Width(55f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Hydro", _labelStyle, GUILayout.Width(50f));
            entry.HydroFactorText = GUILayout.TextField(entry.HydroFactorText, GUILayout.Width(60f));
            GUILayout.Label("Visc", _labelStyle, GUILayout.Width(35f));
            entry.ViscosityFactorText = GUILayout.TextField(entry.ViscosityFactorText, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Albedo R", _labelStyle, GUILayout.Width(62f));
            entry.AlbedoRText = GUILayout.TextField(entry.AlbedoRText, GUILayout.Width(45f));
            GUILayout.Label("G", _labelStyle, GUILayout.Width(14f));
            entry.AlbedoGText = GUILayout.TextField(entry.AlbedoGText, GUILayout.Width(45f));
            GUILayout.Label("B", _labelStyle, GUILayout.Width(14f));
            entry.AlbedoBText = GUILayout.TextField(entry.AlbedoBText, GUILayout.Width(45f));
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(10f);
        if (GUILayout.Button("Reset Scene", GUILayout.Height(32f)))
        {
            ResetScene();
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private bool TryGetSimulationStats(out SimulationDebugStats stats)
    {
        stats = default;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ReleaseCachedQueries();
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: true);

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
            ReleaseCachedQueries();
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: false);

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
            ReleaseCachedQueries();
            return;
        }
        
        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: false);
        
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

    private void SetUpdateFrequency(float updateFrequency)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ReleaseCachedQueries();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: false);

        if (_configQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        float clampedFrequency = Mathf.Max(1f, updateFrequency);
        Config config = _configQuery.GetSingleton<Config>();
        if (Mathf.Approximately(config.UpdateFrequency, clampedFrequency))
        {
            return;
        }

        config.UpdateFrequency = clampedFrequency;
        entityManager.SetComponentData(_configQuery.GetSingletonEntity(), config);
    }

    private void SetIterationCount(int iterationCount)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ReleaseCachedQueries();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: false);

        if (_configQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        int clampedIterationCount = Mathf.Max(1, iterationCount);
        Config config = _configQuery.GetSingleton<Config>();
        if (config.IterationCount == clampedIterationCount)
        {
            return;
        }

        config.IterationCount = clampedIterationCount;
        entityManager.SetComponentData(_configQuery.GetSingletonEntity(), config);
    }

    private void SetInterpolationMode(GridInterpolationMode interpolationMode)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ReleaseCachedQueries();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: false);

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
            ReleaseCachedQueries();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        EnsureCachedQueries(world, entityManager, includeStatsQuery: false);

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

    private void RefreshDebugEntries()
    {
        GridAuthoring[] gridAuthorings = FindObjectsByType<GridAuthoring>(FindObjectsSortMode.InstanceID);
        SpawnShapeAuthoring[] spawnAuthorings = FindObjectsByType<SpawnShapeAuthoring>(FindObjectsSortMode.InstanceID);

        if (TryGetEntityManager(out EntityManager entityManager))
        {
            SyncGridEntries(entityManager, gridAuthorings);
            SyncSpawnerEntries(entityManager, spawnAuthorings);
            return;
        }

        _gridEntries.Clear();
        _spawnerEntries.Clear();
    }

    private bool TryGetEntityManager(out EntityManager entityManager)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ReleaseCachedQueries();
            entityManager = default;
            return false;
        }

        entityManager = world.EntityManager;
        return true;
    }

    private void SyncGridEntries(EntityManager entityManager, GridAuthoring[] grids)
    {
        Dictionary<Entity, GridDebugEntry> previousEntries = new Dictionary<Entity, GridDebugEntry>(_gridEntries.Count);
        foreach (GridDebugEntry entry in _gridEntries)
        {
            if (entry.RuntimeEntity != Entity.Null)
            {
                previousEntries[entry.RuntimeEntity] = entry;
            }
        }

        Dictionary<int, GridAuthoring> authoringById = new Dictionary<int, GridAuthoring>(grids.Length);
        foreach (GridAuthoring grid in grids)
        {
            authoringById[grid.GetInstanceID()] = grid;
        }

        _gridEntries.Clear();
        using EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<GridComponent>() },
            None = new[] { ComponentType.ReadOnly<Prefab>() }
        });

        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            if (!previousEntries.TryGetValue(entity, out GridDebugEntry entry))
            {
                entry = new GridDebugEntry();
            }

            entry.RuntimeEntity = entity;
            entry.Authoring = TryResolveGridAuthoring(entityManager, entity, authoringById);
            entry.Label = entry.Authoring != null ? $"Grid {index + 1}: {entry.Authoring.name}" : $"Grid {index + 1}";
            GridComponent grid = entityManager.GetComponentData<GridComponent>(entity);
            if (string.IsNullOrWhiteSpace(entry.CellSizeText))
            {
                entry.CellSizeText = grid.CellSize.ToString("0.###");
            }
            entry.CellCountText = FormatCellCount(grid);

            _gridEntries.Add(entry);
        }
    }

    private void SyncSpawnerEntries(EntityManager entityManager, SpawnShapeAuthoring[] spawners)
    {
        Dictionary<Entity, SpawnerDebugEntry> previousEntries = new Dictionary<Entity, SpawnerDebugEntry>(_spawnerEntries.Count);
        foreach (SpawnerDebugEntry entry in _spawnerEntries)
        {
            if (entry.RuntimeEntity != Entity.Null)
            {
                previousEntries[entry.RuntimeEntity] = entry;
            }
        }

        Dictionary<int, SpawnShapeAuthoring> authoringById = new Dictionary<int, SpawnShapeAuthoring>(spawners.Length);
        foreach (SpawnShapeAuthoring spawner in spawners)
        {
            authoringById[spawner.GetInstanceID()] = spawner;
        }

        _spawnerEntries.Clear();
        using EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<SpawnShapeComponent>() },
            None = new[] { ComponentType.ReadOnly<Prefab>() }
        });

        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            SpawnShapeComponent shape = entityManager.GetComponentData<SpawnShapeComponent>(entity);
            if (!previousEntries.TryGetValue(entity, out SpawnerDebugEntry entry))
            {
                entry = new SpawnerDebugEntry();
            }

            entry.RuntimeEntity = entity;
            entry.Authoring = TryResolveSpawnAuthoring(entityManager, entity, authoringById);
            entry.Label = entry.Authoring != null ? $"Spawner {index + 1}: {entry.Authoring.name}" : $"Spawner {index + 1}";
            if (string.IsNullOrWhiteSpace(entry.SpawnAmountXText))
            {
                entry.SpawnAmountXText = shape.SpawnAmount.x.ToString();
            }

            if (string.IsNullOrWhiteSpace(entry.SpawnAmountYText))
            {
                entry.SpawnAmountYText = shape.SpawnAmount.y.ToString();
            }

            if (string.IsNullOrWhiteSpace(entry.SpawnAmountZText))
            {
                entry.SpawnAmountZText = shape.SpawnAmount.z.ToString();
            }

            if (entry.SpawnAmountAllText == null)
            {
                entry.SpawnAmountAllText = GetSharedSpawnAmountText(shape.SpawnAmount);
            }

            float3 extents = GetSpawnExtents(shape);
            if (string.IsNullOrWhiteSpace(entry.ExtentXText))
            {
                entry.ExtentXText = extents.x.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.ExtentYText))
            {
                entry.ExtentYText = extents.y.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.ExtentZText))
            {
                entry.ExtentZText = extents.z.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.HydroFactorText))
            {
                entry.HydroFactorText = shape.LiquidHydroFactor.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.ViscosityFactorText))
            {
                entry.ViscosityFactorText = shape.LiquidViscosityFactor.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.AlbedoRText))
            {
                entry.AlbedoRText = shape.ParticleAlbedo.x.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.AlbedoGText))
            {
                entry.AlbedoGText = shape.ParticleAlbedo.y.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.AlbedoBText))
            {
                entry.AlbedoBText = shape.ParticleAlbedo.z.ToString("0.###");
            }

            _spawnerEntries.Add(entry);
        }
    }

    private void ResetScene()
    {
        ResetScene(cancelAutomation: true);
    }

    private void ResetScene(bool cancelAutomation)
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        entityManager.CompleteAllTrackedJobs();
        ApplyPendingValuesToAuthoring();
        RebuildRuntimeGrids(entityManager);
        RebuildRuntimeSpawners(entityManager);
        DestroyRuntimeParticles(entityManager);
        QueueParticleRespawn(entityManager);
        ResetOverlaySamples();
        PBMPMProfilerCaptureStats.ResetRuntimeStats();
        CancelScheduledSolverRunTimeLogging(null);
        if (cancelAutomation)
        {
            CancelSpawnerSweepPipeline(null);
        }
        ResetSolverRunTimeDisplay("Solver runtime samples reset.");
    }

    private void ApplyPendingValuesToAuthoring()
    {
        foreach (GridDebugEntry entry in _gridEntries)
        {
            if (entry.Authoring == null)
            {
                continue;
            }

            if (float.TryParse(entry.CellSizeText, out float parsedCellSize))
            {
                entry.Authoring.cellSize = Mathf.Max(GridAuthoring.MinCellSize, parsedCellSize);
            }

            entry.Authoring.ValidateRuntimeValues();
            entry.Authoring.SnapBoundsToCellSize();
            entry.CellSizeText = entry.Authoring.cellSize.ToString("0.###");
            entry.CellCountText = FormatCellCount(entry.Authoring.CreateGridComponent());
        }

        foreach (SpawnerDebugEntry entry in _spawnerEntries)
        {
            if (entry.Authoring == null)
            {
                continue;
            }

            if (int.TryParse(entry.SpawnAmountXText, out int parsedX))
            {
                entry.Authoring.spawnAmountX = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedX);
            }

            if (int.TryParse(entry.SpawnAmountYText, out int parsedY))
            {
                entry.Authoring.spawnAmountY = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedY);
            }

            if (int.TryParse(entry.SpawnAmountZText, out int parsedZ))
            {
                entry.Authoring.spawnAmountZ = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedZ);
            }

            if (TryGetSharedSpawnAmount(entry.SpawnAmountAllText, out int parsedAll))
            {
                entry.Authoring.spawnAmountX = parsedAll;
                entry.Authoring.spawnAmountY = parsedAll;
                entry.Authoring.spawnAmountZ = parsedAll;
            }

            Vector3 extents = entry.Authoring.spawnBounds.extents;
            if (float.TryParse(entry.ExtentXText, out float parsedExtentX))
            {
                extents.x = Mathf.Max(0f, parsedExtentX);
            }

            if (float.TryParse(entry.ExtentYText, out float parsedExtentY))
            {
                extents.y = Mathf.Max(0f, parsedExtentY);
            }

            if (float.TryParse(entry.ExtentZText, out float parsedExtentZ))
            {
                extents.z = Mathf.Max(0f, parsedExtentZ);
            }

            if (float.TryParse(entry.HydroFactorText, out float parsedHydroFactor))
            {
                entry.Authoring.liquidHydroFactor = Mathf.Max(0f, parsedHydroFactor);
            }

            if (float.TryParse(entry.ViscosityFactorText, out float parsedViscosityFactor))
            {
                entry.Authoring.liquidViscosityFactor = Mathf.Max(0f, parsedViscosityFactor);
            }

            Color particleAlbedo = entry.Authoring.particleAlbedo;
            if (float.TryParse(entry.AlbedoRText, out float parsedAlbedoR))
            {
                particleAlbedo.r = Mathf.Clamp01(parsedAlbedoR);
            }

            if (float.TryParse(entry.AlbedoGText, out float parsedAlbedoG))
            {
                particleAlbedo.g = Mathf.Clamp01(parsedAlbedoG);
            }

            if (float.TryParse(entry.AlbedoBText, out float parsedAlbedoB))
            {
                particleAlbedo.b = Mathf.Clamp01(parsedAlbedoB);
            }

            entry.Authoring.particleAlbedo = particleAlbedo;
            entry.Authoring.spawnBounds = new Bounds(entry.Authoring.spawnBounds.center, extents * 2f);
            entry.Authoring.ValidateRuntimeValues();
            int3 authoringSpawnAmount = new int3(
                entry.Authoring.spawnAmountX,
                entry.Authoring.spawnAmountY,
                entry.Authoring.spawnAmountZ);
            UpdateSpawnAmountTexts(entry, authoringSpawnAmount);
            entry.ExtentXText = entry.Authoring.spawnBounds.extents.x.ToString("0.###");
            entry.ExtentYText = entry.Authoring.spawnBounds.extents.y.ToString("0.###");
            entry.ExtentZText = entry.Authoring.spawnBounds.extents.z.ToString("0.###");
            entry.HydroFactorText = entry.Authoring.liquidHydroFactor.ToString("0.###");
            entry.ViscosityFactorText = entry.Authoring.liquidViscosityFactor.ToString("0.###");
            entry.AlbedoRText = entry.Authoring.particleAlbedo.r.ToString("0.###");
            entry.AlbedoGText = entry.Authoring.particleAlbedo.g.ToString("0.###");
            entry.AlbedoBText = entry.Authoring.particleAlbedo.b.ToString("0.###");
        }
    }

    private void RebuildRuntimeGrids(EntityManager entityManager)
    {
        foreach (GridDebugEntry entry in _gridEntries)
        {
            if (entry.RuntimeEntity == Entity.Null ||
                !entityManager.Exists(entry.RuntimeEntity) ||
                !entityManager.HasComponent<GridComponent>(entry.RuntimeEntity) ||
                !entityManager.HasBuffer<GridCell>(entry.RuntimeEntity))
            {
                continue;
            }

            GridComponent grid;
            if (entry.Authoring != null)
            {
                grid = entry.Authoring.CreateGridComponent();
            }
            else
            {
                grid = entityManager.GetComponentData<GridComponent>(entry.RuntimeEntity);
                if (float.TryParse(entry.CellSizeText, out float parsedCellSize))
                {
                    grid.CellSize = Mathf.Max(GridAuthoring.MinCellSize, parsedCellSize);
                }
            }

            entityManager.SetComponentData(entry.RuntimeEntity, grid);
            DynamicBuffer<GridCell> gridCells = entityManager.GetBuffer<GridCell>(entry.RuntimeEntity);
            RebuildGridCells(gridCells, grid);
            entry.CellSizeText = grid.CellSize.ToString("0.###");
            entry.CellCountText = FormatCellCount(grid);
        }
    }

    private void RebuildRuntimeSpawners(EntityManager entityManager)
    {
        foreach (SpawnerDebugEntry entry in _spawnerEntries)
        {
            if (entry.RuntimeEntity == Entity.Null ||
                !entityManager.Exists(entry.RuntimeEntity) ||
                !entityManager.HasComponent<SpawnShapeComponent>(entry.RuntimeEntity))
            {
                continue;
            }

            SpawnShapeComponent shape;
            if (entry.Authoring != null)
            {
                shape = entry.Authoring.CreateSpawnShapeComponent();
            }
            else
            {
                shape = entityManager.GetComponentData<SpawnShapeComponent>(entry.RuntimeEntity);
                int3 spawnAmount = shape.SpawnAmount;
                if (int.TryParse(entry.SpawnAmountXText, out int parsedX))
                {
                    spawnAmount.x = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedX);
                }

                if (int.TryParse(entry.SpawnAmountYText, out int parsedY))
                {
                    spawnAmount.y = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedY);
                }

                if (int.TryParse(entry.SpawnAmountZText, out int parsedZ))
                {
                    spawnAmount.z = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedZ);
                }

                if (TryGetSharedSpawnAmount(entry.SpawnAmountAllText, out int parsedAll))
                {
                    spawnAmount = new int3(parsedAll, parsedAll, parsedAll);
                }

                float3 extents = GetSpawnExtents(shape);
                if (float.TryParse(entry.ExtentXText, out float parsedExtentX))
                {
                    extents.x = Mathf.Max(0f, parsedExtentX);
                }

                if (float.TryParse(entry.ExtentYText, out float parsedExtentY))
                {
                    extents.y = Mathf.Max(0f, parsedExtentY);
                }

                if (float.TryParse(entry.ExtentZText, out float parsedExtentZ))
                {
                    extents.z = Mathf.Max(0f, parsedExtentZ);
                }

                if (float.TryParse(entry.HydroFactorText, out float parsedHydroFactor))
                {
                    shape.LiquidHydroFactor = Mathf.Max(0f, parsedHydroFactor);
                }

                if (float.TryParse(entry.ViscosityFactorText, out float parsedViscosityFactor))
                {
                    shape.LiquidViscosityFactor = Mathf.Max(0f, parsedViscosityFactor);
                }

                float4 particleAlbedo = shape.ParticleAlbedo;
                if (float.TryParse(entry.AlbedoRText, out float parsedAlbedoR))
                {
                    particleAlbedo.x = Mathf.Clamp01(parsedAlbedoR);
                }

                if (float.TryParse(entry.AlbedoGText, out float parsedAlbedoG))
                {
                    particleAlbedo.y = Mathf.Clamp01(parsedAlbedoG);
                }

                if (float.TryParse(entry.AlbedoBText, out float parsedAlbedoB))
                {
                    particleAlbedo.z = Mathf.Clamp01(parsedAlbedoB);
                }

                shape.SpawnAmount = spawnAmount;
                shape.LocalExtents = extents;
                shape.LocalStart = new float3(
                    spawnAmount.x > 1 ? -extents.x : 0f,
                    spawnAmount.y > 1 ? -extents.y : 0f,
                    spawnAmount.z > 1 ? -extents.z : 0f);
                shape.LocalStep = new float3(
                    spawnAmount.x > 1 ? extents.x * 2f / (spawnAmount.x - 1) : 0f,
                    spawnAmount.y > 1 ? extents.y * 2f / (spawnAmount.y - 1) : 0f,
                    spawnAmount.z > 1 ? extents.z * 2f / (spawnAmount.z - 1) : 0f);
                shape.ParticleAlbedo = particleAlbedo;
            }

            entityManager.SetComponentData(entry.RuntimeEntity, shape);
            float3 normalizedExtents = GetSpawnExtents(shape);
            UpdateSpawnAmountTexts(entry, shape.SpawnAmount);
            entry.ExtentXText = normalizedExtents.x.ToString("0.###");
            entry.ExtentYText = normalizedExtents.y.ToString("0.###");
            entry.ExtentZText = normalizedExtents.z.ToString("0.###");
            entry.HydroFactorText = shape.LiquidHydroFactor.ToString("0.###");
            entry.ViscosityFactorText = shape.LiquidViscosityFactor.ToString("0.###");
            entry.AlbedoRText = shape.ParticleAlbedo.x.ToString("0.###");
            entry.AlbedoGText = shape.ParticleAlbedo.y.ToString("0.###");
            entry.AlbedoBText = shape.ParticleAlbedo.z.ToString("0.###");
        }
    }

    private void DestroyRuntimeParticles(EntityManager entityManager)
    {
        using EntityQuery particleQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<ParticleComponent>() },
            None = new[] { ComponentType.ReadOnly<Prefab>() }
        });

        entityManager.DestroyEntity(particleQuery);
    }

    private void QueueParticleRespawn(EntityManager entityManager)
    {
        using EntityQuery spawnStateQuery = entityManager.CreateEntityQuery(ComponentType.ReadWrite<ParticleSpawnState>());
        if (spawnStateQuery.IsEmptyIgnoreFilter)
        {
            Entity spawnStateEntity = entityManager.CreateEntity(typeof(ParticleSpawnState));
            entityManager.SetComponentData(spawnStateEntity, new ParticleSpawnState
            {
                PendingSpawn = true
            });
            return;
        }

        ParticleSpawnState spawnState = spawnStateQuery.GetSingleton<ParticleSpawnState>();
        spawnState.PendingSpawn = true;
        entityManager.SetComponentData(spawnStateQuery.GetSingletonEntity(), spawnState);
    }

    private void EnsureCachedQueries(World world, EntityManager entityManager, bool includeStatsQuery)
    {
        if (_cachedWorld != world)
        {
            ReleaseCachedQueries();
            _cachedWorld = world;
        }

        if (includeStatsQuery && _statsQuery == default)
        {
            _statsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationDebugStats>());
        }

        if (_configQuery == default)
        {
            _configQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Config>());
        }
    }

    private void ReleaseCachedQueries()
    {
        bool canDisposeQueries = _cachedWorld != null && _cachedWorld.IsCreated;

        if (_statsQuery != default)
        {
            if (canDisposeQueries)
            {
                _statsQuery.Dispose();
            }
            _statsQuery = default;
        }

        if (_configQuery != default)
        {
            if (canDisposeQueries)
            {
                _configQuery.Dispose();
            }
            _configQuery = default;
        }

        _cachedWorld = null;
    }

    private void ResetOverlaySamples()
    {
        for (int i = 0; i < SampleCount; i++)
        {
            _fpsSamples[i] = 0f;
            _iterationSamples[i] = 0f;
        }

        _sampleIndex = 0;
        _filledSamples = 0;
    }

    private void RefreshSolverRunTimeDisplay()
    {
        PBMPMProfilerCaptureStats.SolverRunTimeSnapshot snapshot = PBMPMProfilerCaptureStats.CaptureSolverRunTimeSnapshot();
        if (!snapshot.HasSamples)
        {
            ResetSolverRunTimeDisplay("No solver runtime samples recorded yet.");
            return;
        }

        _solverRunTimeMinText = FormatDurationMs(snapshot.MinMs);
        _solverRunTimeMaxText = FormatDurationMs(snapshot.MaxMs);
        _solverRunTimeMeanText = FormatDurationMs(snapshot.MeanMs);
        _solverRunTimeSampleCountText =
            $"Samples: {snapshot.SampleCount} / {PBMPMProfilerCaptureStats.SolverRunSampleWindow}";
        _solverRunTimeStatusText = $"CSV: {PBMPMProfilerCaptureStats.RuntimeCsvRelativePath}";
    }

    private void StartScheduledSolverRunTimeLogging()
    {
        if (!int.TryParse(_logRepeatCountText, out int requestedLogCount))
        {
            _solverRunTimeStatusText = "Log count must be a whole number.";
            return;
        }

        requestedLogCount = Mathf.Max(1, requestedLogCount);
        _logRepeatCountText = requestedLogCount.ToString();

        if (!TryLogSolverRunTimeStats())
        {
            _isScheduledLoggingActive = false;
            return;
        }

        _pendingScheduledLogCount = requestedLogCount - 1;
        _scheduledLogAccumulatedSolverSubsteps = 0;
        _isScheduledLoggingActive = _pendingScheduledLogCount > 0;

        if (_pendingScheduledLogCount > 0)
        {
            _solverRunTimeStatusText =
                $"Logged 1/{requestedLogCount}. Next log after {PipelineSolverStepsBetweenLogs} solver steps.";
            return;
        }

        _solverRunTimeStatusText = "Logged 1/1.";
    }

    private void UpdateScheduledStatLogging()
    {
        if (_pendingScheduledLogCount <= 0)
        {
            return;
        }

        _scheduledLogAccumulatedSolverSubsteps += _currentSolverSubsteps;
        if (_scheduledLogAccumulatedSolverSubsteps < PipelineSolverStepsBetweenLogs)
        {
            return;
        }

        _scheduledLogAccumulatedSolverSubsteps = 0;

        if (!TryLogSolverRunTimeStats())
        {
            CancelScheduledSolverRunTimeLogging(null);
            return;
        }

        _pendingScheduledLogCount--;
        if (_pendingScheduledLogCount > 0)
        {
            _solverRunTimeStatusText =
                $"Scheduled logs remaining: {_pendingScheduledLogCount}. Next log after {PipelineSolverStepsBetweenLogs} solver steps.";
            return;
        }

        _isScheduledLoggingActive = false;
        _solverRunTimeStatusText = "Scheduled logging complete.";
    }

    private bool TryLogSolverRunTimeStats()
    {
        RefreshSolverRunTimeDisplay();

        if (PBMPMProfilerCaptureStats.TryAppendCsvEntry(out _, out string errorMessage))
        {
            return true;
        }

        _solverRunTimeStatusText = errorMessage;
        return false;
    }

    private void CancelScheduledSolverRunTimeLogging(string statusText)
    {
        _pendingScheduledLogCount = 0;
        _scheduledLogAccumulatedSolverSubsteps = 0;
        _isScheduledLoggingActive = false;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            _solverRunTimeStatusText = statusText;
        }
    }

    private void StartSpawnerSweepPipeline()
    {
        if (!int.TryParse(_pipelineStartSpawnAmountText, out int startSpawnAmount))
        {
            _pipelineStatusText = "Particle pipeline start amount must be a whole number.";
            return;
        }

        if (!int.TryParse(_pipelineEndSpawnAmountText, out int endSpawnAmount))
        {
            _pipelineStatusText = "Particle pipeline end amount must be a whole number.";
            return;
        }

        startSpawnAmount = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, startSpawnAmount);
        endSpawnAmount = Mathf.Max(startSpawnAmount, endSpawnAmount);
        _pipelineStartSpawnAmountText = startSpawnAmount.ToString();
        _pipelineEndSpawnAmountText = endSpawnAmount.ToString();

        CancelSpawnerSweepPipeline(null);
        CancelScheduledSolverRunTimeLogging(null);
        _isPipelineActive = true;
        _pipelineKind = PipelineKind.ParticleLogging;
        _pipelineCurrentSpawnAmount = startSpawnAmount;
        _pipelineEndSpawnAmount = endSpawnAmount;
        _pipelineRemainingLogs = 0;
        _pipelineAccumulatedSolverSubsteps = 0;

        ApplySpawnerSweepSpawnAmount(_pipelineCurrentSpawnAmount);
        _pipelineStage = PipelineStage.WaitingBeforeLogging;
        _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
        _pipelineStatusText =
            $"Particle logging pipeline running: spawn amount {_pipelineCurrentSpawnAmount}. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
        _frequencyPipelineStatusText = "Pipeline idle.";
        _iterationPipelineStatusText = "Pipeline idle.";
        _cellSizePipelineStatusText = "Pipeline idle.";
    }

    private void StartFrequencyLoggingPipeline()
    {
        if (!float.TryParse(_frequencyPipelineStartText, out float startFrequency))
        {
            _frequencyPipelineStatusText = "Frequency pipeline start must be a number.";
            return;
        }

        if (!float.TryParse(_frequencyPipelineEndText, out float endFrequency))
        {
            _frequencyPipelineStatusText = "Frequency pipeline end must be a number.";
            return;
        }

        if (!float.TryParse(_frequencyPipelineStepText, out float stepFrequency))
        {
            _frequencyPipelineStatusText = "Frequency pipeline step must be a number.";
            return;
        }

        startFrequency = Mathf.Max(1f, startFrequency);
        endFrequency = Mathf.Max(startFrequency, endFrequency);
        stepFrequency = Mathf.Max(0.001f, stepFrequency);
        _frequencyPipelineStartText = startFrequency.ToString("0.###");
        _frequencyPipelineEndText = endFrequency.ToString("0.###");
        _frequencyPipelineStepText = stepFrequency.ToString("0.###");

        CancelSpawnerSweepPipeline(null);
        CancelScheduledSolverRunTimeLogging(null);
        _isPipelineActive = true;
        _pipelineKind = PipelineKind.FrequencyLogging;
        _frequencyPipelineCurrentValue = startFrequency;
        _frequencyPipelineEndValue = endFrequency;
        _frequencyPipelineStepValue = stepFrequency;
        _pipelineRemainingLogs = 0;
        _pipelineAccumulatedSolverSubsteps = 0;

        ApplyFrequencyPipelineValue(_frequencyPipelineCurrentValue);
        _pipelineStage = PipelineStage.WaitingBeforeLogging;
        _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
        _frequencyPipelineStatusText =
            $"Frequency logging pipeline running: frequency {_frequencyPipelineCurrentValue:0.###} Hz. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
        _pipelineStatusText = "Pipeline idle.";
        _iterationPipelineStatusText = "Pipeline idle.";
        _cellSizePipelineStatusText = "Pipeline idle.";
    }

    private void StartIterationLoggingPipeline()
    {
        if (!int.TryParse(_iterationPipelineStartText, out int startIterationCount))
        {
            _iterationPipelineStatusText = "Iteration pipeline start must be a whole number.";
            return;
        }

        if (!int.TryParse(_iterationPipelineEndText, out int endIterationCount))
        {
            _iterationPipelineStatusText = "Iteration pipeline end must be a whole number.";
            return;
        }

        if (!int.TryParse(_iterationPipelineStepText, out int stepIterationCount))
        {
            _iterationPipelineStatusText = "Iteration pipeline step must be a whole number.";
            return;
        }

        startIterationCount = Mathf.Max(1, startIterationCount);
        endIterationCount = Mathf.Max(startIterationCount, endIterationCount);
        stepIterationCount = Mathf.Max(1, stepIterationCount);
        _iterationPipelineStartText = startIterationCount.ToString();
        _iterationPipelineEndText = endIterationCount.ToString();
        _iterationPipelineStepText = stepIterationCount.ToString();

        CancelSpawnerSweepPipeline(null);
        CancelScheduledSolverRunTimeLogging(null);
        _isPipelineActive = true;
        _pipelineKind = PipelineKind.IterationLogging;
        _iterationPipelineCurrentValue = startIterationCount;
        _iterationPipelineEndValue = endIterationCount;
        _iterationPipelineStepValue = stepIterationCount;
        _pipelineRemainingLogs = 0;
        _pipelineAccumulatedSolverSubsteps = 0;

        ApplyIterationPipelineValue(_iterationPipelineCurrentValue);
        _pipelineStage = PipelineStage.WaitingBeforeLogging;
        _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
        _iterationPipelineStatusText =
            $"Iteration logging pipeline running: iterations {_iterationPipelineCurrentValue}. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
        _pipelineStatusText = "Pipeline idle.";
        _frequencyPipelineStatusText = "Pipeline idle.";
        _cellSizePipelineStatusText = "Pipeline idle.";
    }

    private void StartCellSizeLoggingPipeline()
    {
        if (!float.TryParse(_cellSizePipelineStartText, out float startCellSize))
        {
            _cellSizePipelineStatusText = "Cell size pipeline start must be a number.";
            return;
        }

        if (!float.TryParse(_cellSizePipelineEndText, out float endCellSize))
        {
            _cellSizePipelineStatusText = "Cell size pipeline end must be a number.";
            return;
        }

        if (!float.TryParse(_cellSizePipelineStepText, out float stepCellSize))
        {
            _cellSizePipelineStatusText = "Cell size pipeline step must be a number.";
            return;
        }

        startCellSize = Mathf.Max(GridAuthoring.MinCellSize, startCellSize);
        endCellSize = Mathf.Max(startCellSize, endCellSize);
        stepCellSize = Mathf.Max(0.001f, stepCellSize);
        _cellSizePipelineStartText = startCellSize.ToString("0.###");
        _cellSizePipelineEndText = endCellSize.ToString("0.###");
        _cellSizePipelineStepText = stepCellSize.ToString("0.###");

        CancelSpawnerSweepPipeline(null);
        CancelScheduledSolverRunTimeLogging(null);
        _isPipelineActive = true;
        _pipelineKind = PipelineKind.CellSizeLogging;
        _cellSizePipelineCurrentValue = startCellSize;
        _cellSizePipelineEndValue = endCellSize;
        _cellSizePipelineStepValue = stepCellSize;
        _pipelineRemainingLogs = 0;
        _pipelineAccumulatedSolverSubsteps = 0;

        ApplyCellSizePipelineValue(_cellSizePipelineCurrentValue);
        _pipelineStage = PipelineStage.WaitingBeforeLogging;
        _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
        _cellSizePipelineStatusText =
            $"Cell size logging pipeline running: cell size {_cellSizePipelineCurrentValue:0.###}. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
        _pipelineStatusText = "Pipeline idle.";
        _frequencyPipelineStatusText = "Pipeline idle.";
        _iterationPipelineStatusText = "Pipeline idle.";
    }

    private void UpdateSpawnerSweepPipeline()
    {
        if (!_isPipelineActive)
        {
            return;
        }

        switch (_pipelineStage)
        {
            case PipelineStage.WaitingBeforeLogging:
                _pipelineAccumulatedSolverSubsteps += _currentSolverSubsteps;
                if (Time.unscaledTime < _pipelineNextActionTime)
                {
                    return;
                }

                if (_pipelineAccumulatedSolverSubsteps < PipelineSolverStepsBeforeFirstLog)
                {
                    return;
                }

                _pipelineAccumulatedSolverSubsteps = 0;

                if (!TryStartPipelineLogging())
                {
                    CancelSpawnerSweepPipeline(GetLoggingStartFailureStatus());
                    return;
                }

                if (_pipelineRemainingLogs > 0)
                {
                    _pipelineStage = PipelineStage.WaitingForLoggingCompletion;
                    SetActivePipelineStatus(GetLoggingInProgressStatus());
                    return;
                }

                AdvanceSpawnerSweepPipeline();
                return;

            case PipelineStage.WaitingForLoggingCompletion:
                _pipelineAccumulatedSolverSubsteps += _currentSolverSubsteps;
                if (_pipelineAccumulatedSolverSubsteps < PipelineSolverStepsBetweenLogs)
                {
                    return;
                }

                _pipelineAccumulatedSolverSubsteps = 0;
                _pipelineRemainingLogs--;
                if (!TryLogSolverRunTimeStats())
                {
                    CancelSpawnerSweepPipeline(GetLoggingStartFailureStatus());
                    return;
                }

                if (_pipelineRemainingLogs > 0)
                {
                    SetActivePipelineStatus(GetLoggingInProgressStatus());
                    return;
                }

                AdvanceSpawnerSweepPipeline();
                return;
        }
    }

    private void AdvanceSpawnerSweepPipeline()
    {
        switch (_pipelineKind)
        {
            case PipelineKind.ParticleLogging:
                if (_pipelineCurrentSpawnAmount >= _pipelineEndSpawnAmount)
                {
                    CancelSpawnerSweepPipeline(
                        $"Particle logging pipeline complete at spawn amount {_pipelineCurrentSpawnAmount}.");
                    return;
                }

                _pipelineCurrentSpawnAmount++;
                ApplySpawnerSweepSpawnAmount(_pipelineCurrentSpawnAmount);
                _pipelineStage = PipelineStage.WaitingBeforeLogging;
                _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
                _pipelineStatusText =
                    $"Particle logging pipeline running: spawn amount {_pipelineCurrentSpawnAmount}. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
                return;

            case PipelineKind.FrequencyLogging:
                float nextFrequency = _frequencyPipelineCurrentValue + _frequencyPipelineStepValue;
                if (nextFrequency > _frequencyPipelineEndValue + 0.0001f)
                {
                    CancelSpawnerSweepPipeline(
                        $"Frequency logging pipeline complete at {_frequencyPipelineCurrentValue:0.###} Hz.");
                    return;
                }

                _frequencyPipelineCurrentValue = nextFrequency;
                ApplyFrequencyPipelineValue(_frequencyPipelineCurrentValue);
                _pipelineStage = PipelineStage.WaitingBeforeLogging;
                _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
                _frequencyPipelineStatusText =
                    $"Frequency logging pipeline running: frequency {_frequencyPipelineCurrentValue:0.###} Hz. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
                return;

            case PipelineKind.IterationLogging:
                int nextIterationCount = _iterationPipelineCurrentValue + _iterationPipelineStepValue;
                if (nextIterationCount > _iterationPipelineEndValue)
                {
                    CancelSpawnerSweepPipeline(
                        $"Iteration logging pipeline complete at {_iterationPipelineCurrentValue} iterations.");
                    return;
                }

                _iterationPipelineCurrentValue = nextIterationCount;
                ApplyIterationPipelineValue(_iterationPipelineCurrentValue);
                _pipelineStage = PipelineStage.WaitingBeforeLogging;
                _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
                _iterationPipelineStatusText =
                    $"Iteration logging pipeline running: iterations {_iterationPipelineCurrentValue}. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
                return;

            case PipelineKind.CellSizeLogging:
                float nextCellSize = _cellSizePipelineCurrentValue + _cellSizePipelineStepValue;
                if (nextCellSize > _cellSizePipelineEndValue + 0.0001f)
                {
                    CancelSpawnerSweepPipeline(
                        $"Cell size logging pipeline complete at {_cellSizePipelineCurrentValue:0.###}.");
                    return;
                }

                _cellSizePipelineCurrentValue = nextCellSize;
                ApplyCellSizePipelineValue(_cellSizePipelineCurrentValue);
                _pipelineStage = PipelineStage.WaitingBeforeLogging;
                _pipelineNextActionTime = Time.unscaledTime + PipelineWaitBeforeLoggingSeconds;
                _cellSizePipelineStatusText =
                    $"Cell size logging pipeline running: cell size {_cellSizePipelineCurrentValue:0.###}. Logging starts after {PipelineWaitBeforeLoggingSeconds:0} s and {PipelineSolverStepsBeforeFirstLog} solver steps.";
                return;
        }
    }

    private void ApplySpawnerSweepSpawnAmount(int spawnAmount)
    {
        RefreshDebugEntries();
        foreach (SpawnerDebugEntry entry in _spawnerEntries)
        {
            UpdateSpawnAmountTexts(entry, new int3(spawnAmount, spawnAmount, spawnAmount));
        }

        ResetScene(cancelAutomation: false);
    }

    private void ApplyFrequencyPipelineValue(float frequency)
    {
        _updateFrequencyText = frequency.ToString("0.###");
        SetUpdateFrequency(frequency);
        ResetScene(cancelAutomation: false);
        if (TryGetConfig(out Config updatedConfig))
        {
            _updateFrequencyText = updatedConfig.UpdateFrequency.ToString("0.###");
        }
    }

    private void ApplyIterationPipelineValue(int iterationCount)
    {
        _iterationCountText = iterationCount.ToString();
        SetIterationCount(iterationCount);
        ResetScene(cancelAutomation: false);
        if (TryGetConfig(out Config updatedConfig))
        {
            _iterationCountText = updatedConfig.IterationCount.ToString();
        }
    }

    private void ApplyCellSizePipelineValue(float cellSize)
    {
        RefreshDebugEntries();
        foreach (GridDebugEntry entry in _gridEntries)
        {
            entry.CellSizeText = cellSize.ToString("0.###");
        }

        ResetScene(cancelAutomation: false);
    }

    private bool TryStartPipelineLogging()
    {
        if (!int.TryParse(_logRepeatCountText, out int requestedLogCount))
        {
            _solverRunTimeStatusText = "Log count must be a whole number.";
            return false;
        }

        requestedLogCount = Mathf.Max(1, requestedLogCount);
        _logRepeatCountText = requestedLogCount.ToString();
        _pipelineAccumulatedSolverSubsteps = 0;
        _pipelineRemainingLogs = requestedLogCount - 1;

        if (!TryLogSolverRunTimeStats())
        {
            _pipelineRemainingLogs = 0;
            return false;
        }

        return true;
    }

    private void CancelSpawnerSweepPipeline(string statusText)
    {
        PipelineKind previousPipelineKind = _pipelineKind;
        _isPipelineActive = false;
        _pipelineStage = PipelineStage.Inactive;
        _pipelineKind = PipelineKind.None;
        _pipelineCurrentSpawnAmount = 0;
        _pipelineEndSpawnAmount = 0;
        _pipelineNextActionTime = 0f;
        _frequencyPipelineCurrentValue = 0f;
        _frequencyPipelineEndValue = 0f;
        _frequencyPipelineStepValue = 0f;
        _iterationPipelineCurrentValue = 0;
        _iterationPipelineEndValue = 0;
        _iterationPipelineStepValue = 0;
        _cellSizePipelineCurrentValue = 0f;
        _cellSizePipelineEndValue = 0f;
        _cellSizePipelineStepValue = 0f;
        _pipelineRemainingLogs = 0;
        _pipelineAccumulatedSolverSubsteps = 0;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            if (previousPipelineKind == PipelineKind.FrequencyLogging)
            {
                _frequencyPipelineStatusText = statusText;
            }
            else if (previousPipelineKind == PipelineKind.IterationLogging)
            {
                _iterationPipelineStatusText = statusText;
            }
            else if (previousPipelineKind == PipelineKind.CellSizeLogging)
            {
                _cellSizePipelineStatusText = statusText;
            }
            else
            {
                _pipelineStatusText = statusText;
            }
        }
    }

    private string GetLoggingStartFailureStatus()
    {
        switch (_pipelineKind)
        {
            case PipelineKind.FrequencyLogging:
                return "Frequency logging pipeline stopped because logging could not start.";
            case PipelineKind.IterationLogging:
                return "Iteration logging pipeline stopped because logging could not start.";
            case PipelineKind.CellSizeLogging:
                return "Cell size logging pipeline stopped because logging could not start.";
            default:
                return "Particle logging pipeline stopped because logging could not start.";
        }
    }

    private string GetLoggingInProgressStatus()
    {
        switch (_pipelineKind)
        {
            case PipelineKind.FrequencyLogging:
                return $"Frequency logging pipeline running: logging for {_frequencyPipelineCurrentValue:0.###} Hz. Next log after {PipelineSolverStepsBetweenLogs} solver steps.";
            case PipelineKind.IterationLogging:
                return $"Iteration logging pipeline running: logging for {_iterationPipelineCurrentValue} iterations. Next log after {PipelineSolverStepsBetweenLogs} solver steps.";
            case PipelineKind.CellSizeLogging:
                return $"Cell size logging pipeline running: logging for {_cellSizePipelineCurrentValue:0.###}. Next log after {PipelineSolverStepsBetweenLogs} solver steps.";
            default:
                return $"Particle logging pipeline running: logging for spawn amount {_pipelineCurrentSpawnAmount}. Next log after {PipelineSolverStepsBetweenLogs} solver steps.";
        }
    }

    private void SetActivePipelineStatus(string statusText)
    {
        if (_pipelineKind == PipelineKind.FrequencyLogging)
        {
            _frequencyPipelineStatusText = statusText;
            return;
        }

        if (_pipelineKind == PipelineKind.IterationLogging)
        {
            _iterationPipelineStatusText = statusText;
            return;
        }

        if (_pipelineKind == PipelineKind.CellSizeLogging)
        {
            _cellSizePipelineStatusText = statusText;
            return;
        }

        _pipelineStatusText = statusText;
    }

    private void ResetSolverRunTimeDisplay(string statusText = null)
    {
        _solverRunTimeMinText = "Press Update";
        _solverRunTimeMaxText = "Press Update";
        _solverRunTimeMeanText = "Press Update";
        _solverRunTimeSampleCountText =
            $"Samples: 0 / {PBMPMProfilerCaptureStats.SolverRunSampleWindow}";
        _solverRunTimeStatusText = string.IsNullOrWhiteSpace(statusText)
            ? $"CSV: {PBMPMProfilerCaptureStats.RuntimeCsvRelativePath}"
            : statusText;
    }

    private static string FormatDurationMs(double valueMs)
    {
        return $"{valueMs:F3} ms";
    }

    private static GridAuthoring TryResolveGridAuthoring(EntityManager entityManager, Entity entity, Dictionary<int, GridAuthoring> authoringById)
    {
        if (!entityManager.HasComponent<GridAuthoringReference>(entity))
        {
            return null;
        }

        GridAuthoringReference reference = entityManager.GetComponentData<GridAuthoringReference>(entity);
        authoringById.TryGetValue(reference.AuthoringInstanceId, out GridAuthoring authoring);
        return authoring;
    }

    private static SpawnShapeAuthoring TryResolveSpawnAuthoring(EntityManager entityManager, Entity entity, Dictionary<int, SpawnShapeAuthoring> authoringById)
    {
        if (!entityManager.HasComponent<SpawnShapeAuthoringReference>(entity))
        {
            return null;
        }

        SpawnShapeAuthoringReference reference = entityManager.GetComponentData<SpawnShapeAuthoringReference>(entity);
        authoringById.TryGetValue(reference.AuthoringInstanceId, out SpawnShapeAuthoring authoring);
        return authoring;
    }

    private static float3 GetSpawnExtents(SpawnShapeComponent shape)
    {
        if (math.any(shape.LocalExtents != float3.zero))
        {
            return shape.LocalExtents;
        }

        return new float3(
            shape.SpawnAmount.x > 1 ? shape.LocalStep.x * (shape.SpawnAmount.x - 1) * 0.5f : 0f,
            shape.SpawnAmount.y > 1 ? shape.LocalStep.y * (shape.SpawnAmount.y - 1) * 0.5f : 0f,
            shape.SpawnAmount.z > 1 ? shape.LocalStep.z * (shape.SpawnAmount.z - 1) * 0.5f : 0f);
    }

    private static bool TryGetSharedSpawnAmount(string sharedText, out int spawnAmount)
    {
        spawnAmount = 0;
        if (string.IsNullOrWhiteSpace(sharedText) || !int.TryParse(sharedText, out int parsedAmount))
        {
            return false;
        }

        spawnAmount = Mathf.Max(SpawnShapeAuthoring.MinSpawnAmount, parsedAmount);
        return true;
    }

    private static string GetSharedSpawnAmountText(int3 spawnAmount)
    {
        return spawnAmount.x == spawnAmount.y && spawnAmount.y == spawnAmount.z
            ? spawnAmount.x.ToString()
            : string.Empty;
    }

    private static void UpdateSpawnAmountTexts(SpawnerDebugEntry entry, int3 spawnAmount)
    {
        entry.SpawnAmountXText = spawnAmount.x.ToString();
        entry.SpawnAmountYText = spawnAmount.y.ToString();
        entry.SpawnAmountZText = spawnAmount.z.ToString();
        entry.SpawnAmountAllText = GetSharedSpawnAmountText(spawnAmount);
    }

    private static void RebuildGridCells(DynamicBuffer<GridCell> gridCells, GridComponent grid)
    {
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

    private static string FormatCellCount(GridComponent grid)
    {
        int3 cellCounts = GridUtilities.GetCellCounts(grid);
        int totalCellCount = cellCounts.x * cellCounts.y * cellCounts.z;
        return $"{cellCounts.x} x {cellCounts.y} x {cellCounts.z} ({totalCellCount})";
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

    private class GridDebugEntry
    {
        public GridAuthoring Authoring;
        public Entity RuntimeEntity;
        public string Label;
        public string CellSizeText;
        public string CellCountText;
    }

    private class SpawnerDebugEntry
    {
        public SpawnShapeAuthoring Authoring;
        public Entity RuntimeEntity;
        public string Label;
        public string SpawnAmountXText;
        public string SpawnAmountYText;
        public string SpawnAmountZText;
        public string SpawnAmountAllText;
        public string ExtentXText;
        public string ExtentYText;
        public string ExtentZText;
        public string HydroFactorText;
        public string ViscosityFactorText;
        public string AlbedoRText;
        public string AlbedoGText;
        public string AlbedoBText;
    }

    private enum PipelineStage
    {
        Inactive,
        WaitingBeforeLogging,
        WaitingForLoggingCompletion
    }

    private enum PipelineKind
    {
        None,
        ParticleLogging,
        FrequencyLogging,
        IterationLogging,
        CellSizeLogging
    }
}

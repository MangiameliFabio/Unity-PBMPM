using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class SimulationDebugOverlay : MonoBehaviour
{
    private const int SampleCount = 300;
    private readonly float[] _fpsSamples = new float[SampleCount];
    private readonly float[] _iterationSamples = new float[SampleCount];

    private int _sampleIndex;
    private int _filledSamples;
    private bool _isVisible = true;
    private string _updateFrequencyText;
    private string _iterationCountText;
    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;
    private EntityQuery _statsQuery;
    private EntityQuery _configQuery;
    private World _cachedWorld;
    private readonly List<GridDebugEntry> _gridEntries = new List<GridDebugEntry>();
    private readonly List<SpawnerDebugEntry> _spawnerEntries = new List<SpawnerDebugEntry>();

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
        float updateFrequency = 30f;
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
        RefreshDebugEntries();

        GUILayout.BeginArea(new Rect(12f, 12f, 420f, Mathf.Min(Screen.height - 24f, 720f)), _boxStyle);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Hide Debug", GUILayout.Width(110f), GUILayout.Height(26f)))
        {
            _isVisible = false;
        }
        GUILayout.EndHorizontal();
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
        GUILayout.Label("Grids", _labelStyle);
        foreach (GridDebugEntry entry in _gridEntries)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(entry.Label, _labelStyle, GUILayout.Width(220f));
            GUILayout.Label("Cell Size", _labelStyle, GUILayout.Width(70f));
            entry.CellSizeText = GUILayout.TextField(entry.CellSizeText, GUILayout.Width(90f));
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

    private void SetUpdateFrequency(float updateFrequency)
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

    private void RefreshDebugEntries()
    {
        SyncGridEntries(FindObjectsByType<GridAuthoring>(FindObjectsSortMode.InstanceID));
        SyncSpawnerEntries(FindObjectsByType<SpawnShapeAuthoring>(FindObjectsSortMode.InstanceID));
    }

    private void SyncGridEntries(GridAuthoring[] grids)
    {
        Dictionary<int, GridDebugEntry> previousEntries = new Dictionary<int, GridDebugEntry>(_gridEntries.Count);
        foreach (GridDebugEntry entry in _gridEntries)
        {
            if (entry.Authoring != null)
            {
                previousEntries[entry.Authoring.GetInstanceID()] = entry;
            }
        }

        _gridEntries.Clear();
        for (int index = 0; index < grids.Length; index++)
        {
            GridAuthoring grid = grids[index];
            int instanceId = grid.GetInstanceID();
            if (!previousEntries.TryGetValue(instanceId, out GridDebugEntry entry))
            {
                entry = new GridDebugEntry();
            }

            entry.Authoring = grid;
            entry.Label = $"Grid {index + 1}: {grid.name}";
            if (string.IsNullOrWhiteSpace(entry.CellSizeText))
            {
                entry.CellSizeText = grid.cellSize.ToString("0.###");
            }

            _gridEntries.Add(entry);
        }
    }

    private void SyncSpawnerEntries(SpawnShapeAuthoring[] spawners)
    {
        Dictionary<int, SpawnerDebugEntry> previousEntries = new Dictionary<int, SpawnerDebugEntry>(_spawnerEntries.Count);
        foreach (SpawnerDebugEntry entry in _spawnerEntries)
        {
            if (entry.Authoring != null)
            {
                previousEntries[entry.Authoring.GetInstanceID()] = entry;
            }
        }

        _spawnerEntries.Clear();
        for (int index = 0; index < spawners.Length; index++)
        {
            SpawnShapeAuthoring spawner = spawners[index];
            int instanceId = spawner.GetInstanceID();
            if (!previousEntries.TryGetValue(instanceId, out SpawnerDebugEntry entry))
            {
                entry = new SpawnerDebugEntry();
            }

            entry.Authoring = spawner;
            entry.Label = $"Spawner {index + 1}: {spawner.name}";
            if (string.IsNullOrWhiteSpace(entry.SpawnAmountXText))
            {
                entry.SpawnAmountXText = spawner.spawnAmountX.ToString();
            }

            if (string.IsNullOrWhiteSpace(entry.SpawnAmountYText))
            {
                entry.SpawnAmountYText = spawner.spawnAmountY.ToString();
            }

            if (string.IsNullOrWhiteSpace(entry.SpawnAmountZText))
            {
                entry.SpawnAmountZText = spawner.spawnAmountZ.ToString();
            }

            if (string.IsNullOrWhiteSpace(entry.ExtentXText))
            {
                entry.ExtentXText = spawner.spawnBounds.extents.x.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.ExtentYText))
            {
                entry.ExtentYText = spawner.spawnBounds.extents.y.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.ExtentZText))
            {
                entry.ExtentZText = spawner.spawnBounds.extents.z.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.HydroFactorText))
            {
                entry.HydroFactorText = spawner.liquidHydroFactor.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.ViscosityFactorText))
            {
                entry.ViscosityFactorText = spawner.liquidViscosityFactor.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.AlbedoRText))
            {
                entry.AlbedoRText = spawner.particleAlbedo.r.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.AlbedoGText))
            {
                entry.AlbedoGText = spawner.particleAlbedo.g.ToString("0.###");
            }

            if (string.IsNullOrWhiteSpace(entry.AlbedoBText))
            {
                entry.AlbedoBText = spawner.particleAlbedo.b.ToString("0.###");
            }

            _spawnerEntries.Add(entry);
        }
    }

    private void ResetScene()
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
            entry.SpawnAmountXText = entry.Authoring.spawnAmountX.ToString();
            entry.SpawnAmountYText = entry.Authoring.spawnAmountY.ToString();
            entry.SpawnAmountZText = entry.Authoring.spawnAmountZ.ToString();
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
        EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<GridComponent>(),
            ComponentType.ReadWrite<GridCell>(),
            ComponentType.ReadOnly<GridAuthoringReference>());

        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            GridAuthoringReference reference = entityManager.GetComponentData<GridAuthoringReference>(entity);
            GridAuthoring authoring = FindAuthoringByInstanceId(_gridEntries, reference.AuthoringInstanceId);
            if (authoring == null)
            {
                continue;
            }

            entityManager.SetComponentData(entity, authoring.CreateGridComponent());
            DynamicBuffer<GridCell> gridCells = entityManager.GetBuffer<GridCell>(entity);
            authoring.RebuildGridCells(gridCells);
        }
    }

    private void RebuildRuntimeSpawners(EntityManager entityManager)
    {
        EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<SpawnShapeComponent>(),
            ComponentType.ReadOnly<SpawnShapeAuthoringReference>());

        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            SpawnShapeAuthoringReference reference = entityManager.GetComponentData<SpawnShapeAuthoringReference>(entity);
            SpawnShapeAuthoring authoring = FindAuthoringByInstanceId(_spawnerEntries, reference.AuthoringInstanceId);
            if (authoring == null)
            {
                continue;
            }

            entityManager.SetComponentData(entity, authoring.CreateSpawnShapeComponent());
        }
    }

    private void DestroyRuntimeParticles(EntityManager entityManager)
    {
        EntityQuery particleQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<ParticleComponent>() },
            None = new[] { ComponentType.ReadOnly<Prefab>() }
        });

        entityManager.DestroyEntity(particleQuery);
    }

    private void QueueParticleRespawn(EntityManager entityManager)
    {
        EntityQuery spawnStateQuery = entityManager.CreateEntityQuery(ComponentType.ReadWrite<ParticleSpawnState>());
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

    private static GridAuthoring FindAuthoringByInstanceId(List<GridDebugEntry> entries, int instanceId)
    {
        foreach (GridDebugEntry entry in entries)
        {
            if (entry.Authoring != null && entry.Authoring.GetInstanceID() == instanceId)
            {
                return entry.Authoring;
            }
        }

        return null;
    }

    private static SpawnShapeAuthoring FindAuthoringByInstanceId(List<SpawnerDebugEntry> entries, int instanceId)
    {
        foreach (SpawnerDebugEntry entry in entries)
        {
            if (entry.Authoring != null && entry.Authoring.GetInstanceID() == instanceId)
            {
                return entry.Authoring;
            }
        }

        return null;
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
        public string Label;
        public string CellSizeText;
    }

    private class SpawnerDebugEntry
    {
        public SpawnShapeAuthoring Authoring;
        public string Label;
        public string SpawnAmountXText;
        public string SpawnAmountYText;
        public string SpawnAmountZText;
        public string ExtentXText;
        public string ExtentYText;
        public string ExtentZText;
        public string HydroFactorText;
        public string ViscosityFactorText;
        public string AlbedoRText;
        public string AlbedoGText;
        public string AlbedoBText;
    }
}

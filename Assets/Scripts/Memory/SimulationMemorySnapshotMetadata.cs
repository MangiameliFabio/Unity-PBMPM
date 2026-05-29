using System;
using Unity.Entities;
using Unity.MemoryProfiler;
using Unity.Profiling.Memory;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class SimulationMemorySnapshotMetadata : MetadataCollect
{
    private static SimulationMemorySnapshotMetadata s_Instance;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void InitializeInEditor()
    {
        EnsureInstance();
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void InitializeAtRuntime()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        s_Instance?.Dispose();
        s_Instance = new SimulationMemorySnapshotMetadata();
    }

    private SimulationMemorySnapshotMetadata()
        : base()
    {
    }

    public override void CollectMetadata(MemorySnapshotMetadata data)
    {
        data.Description += $"Project name: {Application.productName}\n"
                          + $"This Memory Snapshot capture started at {DateTime.UtcNow} (UTC)\n"
                          + $"Time.frameCount: {Time.frameCount}\n"
                          + $"Time.realtimeSinceStartup: {FormatSecondsToTime(Time.realtimeSinceStartupAsDouble)}\n";
#if UNITY_EDITOR
        data.Description += $"EditorApplication.timeSinceStartup: {FormatSecondsToTime(EditorApplication.timeSinceStartup)}\n";
#endif

        if (!TryGetSimulationMemoryStats(out SimulationMemoryStats stats))
        {
            data.Description += "PBMPM.StatsAvailable=false\n";
            return;
        }

        data.Description += "PBMPM.StatsAvailable=true\n";
        data.Description += $"PBMPM.ParticleCount={stats.ParticleCount}\n";
        data.Description += $"PBMPM.GridCount={stats.GridCount}\n";
        data.Description += $"PBMPM.GridCellCount={stats.GridCellCount}\n";
        data.Description += $"PBMPM.GridNodeCount={stats.GridNodeCount}\n";
        data.Description += $"PBMPM.TileParticleCacheCapacity={stats.TileParticleCacheCapacity}\n";
        data.Description += $"PBMPM.ActiveTileCapacity={stats.ActiveTileCapacity}\n";
        data.Description += $"PBMPM.ParticleComponentBytes={stats.ParticleComponentBytes}\n";
        data.Description += $"PBMPM.ParticleTransformBytes={stats.ParticleTransformBytes}\n";
        data.Description += $"PBMPM.GridComponentBytes={stats.GridComponentBytes}\n";
        data.Description += $"PBMPM.GridNodeBytes={stats.GridNodeBytes}\n";
        data.Description += $"PBMPM.TileParticleCacheBytes={stats.TileParticleCacheBytes}\n";
        data.Description += $"PBMPM.ActiveTileSetBytes={stats.ActiveTileSetBytes}\n";
        data.Description += $"PBMPM.ActiveTileListBytes={stats.ActiveTileListBytes}\n";
        data.Description += $"PBMPM.TotalEstimatedSolverBytes={stats.TotalEstimatedSolverBytes}\n";
        data.Description += $"PBMPM.TotalEstimatedRuntimeBytes={stats.TotalEstimatedRuntimeBytes}\n";
    }

    private static bool TryGetSimulationMemoryStats(out SimulationMemoryStats stats)
    {
        stats = default;

        var worlds = World.All;
        for (int i = 0; i < worlds.Count; i++)
        {
            World world = worlds[i];
            if (world == null || !world.IsCreated)
            {
                continue;
            }

            EntityManager entityManager = world.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationMemoryStats>());
            if (query.IsEmptyIgnoreFilter)
            {
                continue;
            }

            stats = query.GetSingleton<SimulationMemoryStats>();
            return true;
        }

        return false;
    }

    private static string FormatSecondsToTime(double timeInSeconds)
    {
        int seconds = (int)timeInSeconds;
        int ms = (int)((timeInSeconds - seconds) * 1000);
        int minutes = seconds / 60;
        seconds %= 60;
        int hours = minutes / 60;
        minutes %= 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}.{ms:000}";
    }
}

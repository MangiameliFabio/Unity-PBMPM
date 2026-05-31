#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class MemoryCaptureStatsExporter
{
    [MenuItem("Tools/Memory/Export Capture Quick Stats")]
    public static void ExportQuickStats()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            Debug.LogError("Could not resolve project root.");
            return;
        }

        string captureDirectory = Path.Combine(projectRoot, "MemoryCaptures");
        if (!Directory.Exists(captureDirectory))
        {
            Debug.LogError($"Memory capture directory not found: {captureDirectory}");
            return;
        }

        string outputPath = Path.Combine(captureDirectory, "memory-capture-quick-stats.csv");

        Assembly profilerAssembly;
        try
        {
            profilerAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Unity.MemoryProfiler.Editor")
                ?? Assembly.Load("Unity.MemoryProfiler.Editor");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not load Unity.MemoryProfiler.Editor assembly: {exception}");
            return;
        }

        Type builderType = profilerAssembly.GetType("Unity.MemoryProfiler.Editor.SnapshotFileModelBuilder");
        if (builderType == null)
        {
            Debug.LogError("Could not find Unity.MemoryProfiler.Editor.SnapshotFileModelBuilder.");
            return;
        }

        MethodInfo buildMethod = builderType.GetMethod(
            "Build",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (buildMethod == null)
        {
            Debug.LogError("Could not find SnapshotFileModelBuilder.Build().");
            return;
        }

        var snapshotFiles = Directory
            .GetFiles(captureDirectory, "*.snap", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var csv = new StringBuilder();
        csv.AppendLine(
            "capture_name,particle_count,cell_count,total_allocated_bytes,total_allocated_mib,total_resident_bytes,total_resident_mib,max_available_bytes,max_available_gib,product_name,unity_version,platform,is_editor_capture,scripting_implementation,capture_flags,timestamp_utc,pbmpm_stats_available,pbmpm_grid_count,pbmpm_grid_node_count,pbmpm_tile_particle_cache_capacity,pbmpm_particle_component_bytes,pbmpm_particle_transform_bytes,pbmpm_grid_component_bytes,pbmpm_grid_node_bytes,pbmpm_tile_particle_cache_bytes,pbmpm_total_estimated_solver_bytes,pbmpm_total_estimated_runtime_bytes");

        foreach (string snapshotPath in snapshotFiles)
        {
            string captureName = Path.GetFileNameWithoutExtension(snapshotPath);

            object builder = Activator.CreateInstance(builderType, snapshotPath);
            object fileModel = buildMethod.Invoke(builder, null);
            if (fileModel == null)
            {
                Debug.LogWarning($"Could not build snapshot model for {snapshotPath}");
                continue;
            }

            ulong totalAllocated = GetPropertyValue<ulong>(fileModel, "TotalAllocatedMemory");
            ulong totalResident = GetPropertyValue<ulong>(fileModel, "TotalResidentMemory");
            ulong maxAvailable = GetPropertyValue<ulong>(fileModel, "MaxAvailableMemory");

            string productName = GetPropertyValue<string>(fileModel, "ProductName") ?? string.Empty;
            string unityVersion = GetPropertyValue<string>(fileModel, "UnityVersion") ?? string.Empty;
            string platform = GetPropertyValue<object>(fileModel, "Platform")?.ToString() ?? string.Empty;
            bool editorCapture = GetPropertyValue<bool>(fileModel, "EditorPlatform");
            string scriptingImplementation = GetPropertyValue<string>(fileModel, "ScriptingImplementation") ?? string.Empty;
            string captureFlags = GetPropertyValue<object>(fileModel, "CaptureFlags")?.ToString() ?? string.Empty;
            string metadataDescription = GetPropertyValue<string>(fileModel, "MetadataDescription") ?? string.Empty;
            DateTime timestampUtc = GetPropertyValue<DateTime>(fileModel, "Timestamp").ToUniversalTime();
            bool pbmpmStatsAvailable = TryGetMetadataBool(metadataDescription, "StatsAvailable");
            long pbmpmParticleCount = GetMetadataLong(metadataDescription, "ParticleCount");
            long pbmpmGridCellCount = GetMetadataLong(metadataDescription, "GridCellCount");
            long pbmpmGridCount = GetMetadataLong(metadataDescription, "GridCount");
            long pbmpmGridNodeCount = GetMetadataLong(metadataDescription, "GridNodeCount");
            long pbmpmTileParticleCacheCapacity = GetMetadataLongWithFallback(
                metadataDescription,
                "TileParticleCacheCapacity",
                "TileParticleScratchCapacity");
            long pbmpmParticleComponentBytes = GetMetadataLong(metadataDescription, "ParticleComponentBytes");
            long pbmpmParticleTransformBytes = GetMetadataLong(metadataDescription, "ParticleTransformBytes");
            long pbmpmGridComponentBytes = GetMetadataLong(metadataDescription, "GridComponentBytes");
            long pbmpmGridNodeBytes = GetMetadataLong(metadataDescription, "GridNodeBytes");
            long pbmpmTileParticleCacheBytes = GetMetadataLongWithFallback(
                metadataDescription,
                "TileParticleCacheBytes",
                "TileParticleScratchBytes");
            long pbmpmTotalEstimatedSolverBytes = GetMetadataLong(metadataDescription, "TotalEstimatedSolverBytes");
            long pbmpmTotalEstimatedRuntimeBytes = GetMetadataLong(metadataDescription, "TotalEstimatedRuntimeBytes");

            csv.Append(Escape(captureName)).Append(',');
            csv.Append(pbmpmParticleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmGridCellCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(totalAllocated.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(ToMiB(totalAllocated).ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(totalResident.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(ToMiB(totalResident).ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(maxAvailable.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(ToGiB(maxAvailable).ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(Escape(productName)).Append(',');
            csv.Append(Escape(unityVersion)).Append(',');
            csv.Append(Escape(platform)).Append(',');
            csv.Append(editorCapture ? "true" : "false").Append(',');
            csv.Append(Escape(scriptingImplementation)).Append(',');
            csv.Append(Escape(captureFlags)).Append(',');
            csv.Append(Escape(timestampUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            csv.Append(pbmpmStatsAvailable ? "true" : "false").Append(',');
            csv.Append(pbmpmGridCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmGridNodeCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmTileParticleCacheCapacity.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmParticleComponentBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmParticleTransformBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmGridComponentBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmGridNodeBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmTileParticleCacheBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmTotalEstimatedSolverBytes.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(pbmpmTotalEstimatedRuntimeBytes.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        File.WriteAllText(outputPath, csv.ToString(), Encoding.UTF8);
        Debug.Log($"Wrote quick memory capture stats to {outputPath}");
    }

    public static void ExportQuickStatsBatchMode()
    {
        try
        {
            ExportQuickStats();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    private static T GetPropertyValue<T>(object instance, string propertyName)
    {
        if (instance == null)
        {
            return default;
        }

        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (property == null)
        {
            return default;
        }

        object value = property.GetValue(instance);
        if (value is T typed)
        {
            return typed;
        }

        return default;
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static long GetMetadataLong(string metadataDescription, string key)
    {
        if (string.IsNullOrEmpty(metadataDescription))
        {
            return 0;
        }

        Match match = Regex.Match(
            metadataDescription,
            $@"^PBMPM\.{Regex.Escape(key)}=(?<value>-?\d+)$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return 0;
        }

        return long.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
    }

    private static long GetMetadataLongWithFallback(string metadataDescription, string preferredKey, string legacyKey)
    {
        long value = GetMetadataLong(metadataDescription, preferredKey);
        return value != 0 ? value : GetMetadataLong(metadataDescription, legacyKey);
    }

    private static bool TryGetMetadataBool(string metadataDescription, string key)
    {
        if (string.IsNullOrEmpty(metadataDescription))
        {
            return false;
        }

        Match match = Regex.Match(
            metadataDescription,
            $@"^PBMPM\.{Regex.Escape(key)}=(?<value>true|false)$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        return match.Success && bool.TryParse(match.Groups["value"].Value, out bool value) && value;
    }

    private static double ToMiB(ulong bytes)
    {
        return bytes / 1048576d;
    }

    private static double ToGiB(ulong bytes)
    {
        return bytes / 1073741824d;
    }
}
#endif

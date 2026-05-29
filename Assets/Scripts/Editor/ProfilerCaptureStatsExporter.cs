#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static class ProfilerCaptureStatsExporter
{
    private static readonly Regex ParticleCountPattern = new Regex(
        @"(?<value>\d+)P(?:_|-|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex GridCellCountPattern = new Regex(
        @"(?<value>\d+)C(?:_|-|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SolverFrequencyPattern = new Regex(
        @"(?<value>\d+(?:\.\d+)?)Hz(?:_|-|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SolverIterationPattern = new Regex(
        @"(?<value>\d+)(?:I|Iter|Iterations)(?:_|-|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [MenuItem("Tools/Profiler/Export PBMPM Capture Stats")]
    public static void ExportQuickStats()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            Debug.LogError("Could not resolve project root.");
            return;
        }

        string captureDirectory = Path.Combine(projectRoot, "ProfilerCaptures");
        if (!Directory.Exists(captureDirectory))
        {
            Debug.LogError($"Profiler capture directory not found: {captureDirectory}");
            return;
        }

        string outputPath = Path.Combine(captureDirectory, "pbmpm-profiler-capture-stats.csv");
        string[] captureFiles = Directory
            .GetFiles(captureDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedProfilerCaptureFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var csv = new StringBuilder();
        csv.AppendLine(
            "capture_name,particle_count,grid_cell_count,solver_frequency_hz,solver_iteration_count,average_frame_rate_fps,pbmpm_onupdate_min_ms,pbmpm_onupdate_max_ms,pbmpm_onupdate_mean_ms,frame_count,marker_sample_count,stats_source");

        foreach (string capturePath in captureFiles)
        {
            CaptureAnalysis analysis = AnalyzeCapture(capturePath);
            csv.Append(Escape(analysis.CaptureName)).Append(',');
            csv.Append(FormatOptionalInt(analysis.ParticleCount, analysis.HasParticleCount)).Append(',');
            csv.Append(FormatOptionalInt(analysis.GridCellCount, analysis.HasGridCellCount)).Append(',');
            csv.Append(FormatOptionalFloat(analysis.SolverFrequency, analysis.HasSolverFrequency)).Append(',');
            csv.Append(FormatOptionalInt(analysis.SolverIterationCount, analysis.HasSolverIterationCount)).Append(',');
            csv.Append(FormatOptionalDouble(analysis.AverageFrameRate, analysis.HasAverageFrameRate)).Append(',');
            csv.Append(FormatOptionalDouble(analysis.MinOnUpdateMs, analysis.HasTimingSamples)).Append(',');
            csv.Append(FormatOptionalDouble(analysis.MaxOnUpdateMs, analysis.HasTimingSamples)).Append(',');
            csv.Append(FormatOptionalDouble(analysis.MeanOnUpdateMs, analysis.HasTimingSamples)).Append(',');
            csv.Append(analysis.FrameCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(analysis.MarkerSampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(Escape(analysis.StatsSource)).AppendLine();
        }

        File.WriteAllText(outputPath, csv.ToString(), Encoding.UTF8);
        Debug.Log($"Wrote PBMPM profiler capture stats to {outputPath}");
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

    private static CaptureAnalysis AnalyzeCapture(string capturePath)
    {
        string captureName = Path.GetFileNameWithoutExtension(capturePath);
        CaptureAnalysis analysis = CaptureAnalysis.Create(captureName);
        ApplyCaptureNameFallback(captureName, ref analysis);

        ProfilerDriver.ClearAllFrames();
        if (!ProfilerDriver.LoadProfile(capturePath, false))
        {
            analysis.StatsSource = analysis.HasAnyCaptureStat ? "filename_fallback" : "unavailable";
            return analysis;
        }

        int firstFrameIndex = ProfilerDriver.firstFrameIndex;
        int lastFrameIndex = ProfilerDriver.lastFrameIndex;
        if (firstFrameIndex < 0 || lastFrameIndex < firstFrameIndex)
        {
            analysis.StatsSource = analysis.HasAnyCaptureStat ? "filename_fallback" : "unavailable";
            return analysis;
        }

        int validFrameCount = 0;
        bool foundCounterValue = false;
        double timingSumMs = 0d;
        double timingMinMs = double.MaxValue;
        double timingMaxMs = double.MinValue;
        int timingSampleCount = 0;
        double totalFrameTimeNs = 0d;

        for (int frameIndex = firstFrameIndex; frameIndex <= lastFrameIndex; frameIndex++)
        {
            using RawFrameDataView rootFrame = ProfilerDriver.GetRawFrameDataView(frameIndex, 0);
            if (!rootFrame.valid || rootFrame.frameTimeNs <= 0)
            {
                continue;
            }

            validFrameCount++;
            totalFrameTimeNs += rootFrame.frameTimeNs;
            foundCounterValue |= TryReadCaptureCounters(rootFrame, ref analysis);

            for (int threadIndex = 0; ; threadIndex++)
            {
                using RawFrameDataView threadFrame = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
                if (!threadFrame.valid)
                {
                    break;
                }

                for (int sampleIndex = 1; sampleIndex < threadFrame.sampleCount; sampleIndex++)
                {
                    if (!IsPBMPMSolverSample(threadFrame.GetSampleName(sampleIndex)))
                    {
                        continue;
                    }

                    float durationMs = threadFrame.GetSampleTimeMs(sampleIndex);
                    if (durationMs < 0f)
                    {
                        continue;
                    }

                    timingSampleCount++;
                    timingSumMs += durationMs;
                    timingMinMs = Math.Min(timingMinMs, durationMs);
                    timingMaxMs = Math.Max(timingMaxMs, durationMs);
                }
            }
        }

        analysis.FrameCount = validFrameCount;
        analysis.MarkerSampleCount = timingSampleCount;
        if (validFrameCount > 0 && totalFrameTimeNs > 0d)
        {
            analysis.HasAverageFrameRate = true;
            analysis.AverageFrameRate = validFrameCount * 1000000000d / totalFrameTimeNs;
        }
        if (timingSampleCount > 0)
        {
            analysis.HasTimingSamples = true;
            analysis.MinOnUpdateMs = timingMinMs;
            analysis.MaxOnUpdateMs = timingMaxMs;
            analysis.MeanOnUpdateMs = timingSumMs / timingSampleCount;
        }

        analysis.StatsSource = GetStatsSource(analysis, foundCounterValue);
        ProfilerDriver.ClearAllFrames();
        return analysis;
    }

    private static bool TryReadCaptureCounters(RawFrameDataView frameData, ref CaptureAnalysis analysis)
    {
        bool foundAny = false;

        int particleCountMarkerId = frameData.GetMarkerId(PBMPMProfilerCaptureStats.ParticleCountCounterName);
        if (particleCountMarkerId >= 0)
        {
            analysis.HasParticleCount = true;
            analysis.ParticleCount = frameData.GetCounterValueAsInt(particleCountMarkerId);
            foundAny = true;
        }

        int gridCellCountMarkerId = frameData.GetMarkerId(PBMPMProfilerCaptureStats.GridCellCountCounterName);
        if (gridCellCountMarkerId >= 0)
        {
            analysis.HasGridCellCount = true;
            analysis.GridCellCount = frameData.GetCounterValueAsInt(gridCellCountMarkerId);
            foundAny = true;
        }

        int solverFrequencyMarkerId = frameData.GetMarkerId(PBMPMProfilerCaptureStats.SolverFrequencyCounterName);
        if (solverFrequencyMarkerId >= 0)
        {
            analysis.HasSolverFrequency = true;
            analysis.SolverFrequency = frameData.GetCounterValueAsFloat(solverFrequencyMarkerId);
            foundAny = true;
        }

        int solverIterationMarkerId = frameData.GetMarkerId(PBMPMProfilerCaptureStats.SolverIterationCountCounterName);
        if (solverIterationMarkerId >= 0)
        {
            analysis.HasSolverIterationCount = true;
            analysis.SolverIterationCount = frameData.GetCounterValueAsInt(solverIterationMarkerId);
            foundAny = true;
        }

        return foundAny;
    }

    private static void ApplyCaptureNameFallback(string captureName, ref CaptureAnalysis analysis)
    {
        if (TryParseIntFromPattern(captureName, ParticleCountPattern, out int particleCount))
        {
            analysis.HasParticleCount = true;
            analysis.ParticleCount = particleCount;
            analysis.HasAnyFilenameFallbackStat = true;
        }

        if (TryParseIntFromPattern(captureName, GridCellCountPattern, out int gridCellCount))
        {
            analysis.HasGridCellCount = true;
            analysis.GridCellCount = gridCellCount;
            analysis.HasAnyFilenameFallbackStat = true;
        }

        if (TryParseFloatFromPattern(captureName, SolverFrequencyPattern, out float solverFrequency))
        {
            analysis.HasSolverFrequency = true;
            analysis.SolverFrequency = solverFrequency;
            analysis.HasAnyFilenameFallbackStat = true;
        }

        if (TryParseIntFromPattern(captureName, SolverIterationPattern, out int solverIterationCount))
        {
            analysis.HasSolverIterationCount = true;
            analysis.SolverIterationCount = solverIterationCount;
            analysis.HasAnyFilenameFallbackStat = true;
        }
    }

    private static bool TryParseIntFromPattern(string value, Regex pattern, out int result)
    {
        Match match = pattern.Match(value);
        if (!match.Success)
        {
            result = 0;
            return false;
        }

        return int.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryParseFloatFromPattern(string value, Regex pattern, out float result)
    {
        Match match = pattern.Match(value);
        if (!match.Success)
        {
            result = 0f;
            return false;
        }

        return float.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool IsSupportedProfilerCaptureFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".data", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".raw", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPBMPMSolverSample(string sampleName)
    {
        if (string.IsNullOrEmpty(sampleName))
        {
            return false;
        }

        return sampleName.Equals("PB-MPMSolverUpdate", StringComparison.Ordinal)
            || sampleName.EndsWith(" PB-MPMSolverUpdate", StringComparison.Ordinal)
            || sampleName.Equals("PBMPMSolverUpdate", StringComparison.Ordinal)
            || sampleName.EndsWith(" PBMPMSolverUpdate", StringComparison.Ordinal)
            || sampleName.Equals("PBMPMSolverSystem", StringComparison.Ordinal)
            || sampleName.EndsWith(" PBMPMSolverSystem", StringComparison.Ordinal);
    }

    private static string GetStatsSource(CaptureAnalysis analysis, bool foundCounterValue)
    {
        if (foundCounterValue)
        {
            return analysis.HasAnyFilenameFallbackStat ? "mixed" : "profiler_counters";
        }

        return analysis.HasAnyCaptureStat ? "filename_fallback" : "unavailable";
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

    private static string FormatOptionalInt(int value, bool hasValue)
    {
        return hasValue ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatOptionalFloat(float value, bool hasValue)
    {
        return hasValue ? value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatOptionalDouble(double value, bool hasValue)
    {
        return hasValue ? value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
    }

    private struct CaptureAnalysis
    {
        public string CaptureName;
        public int ParticleCount;
        public int GridCellCount;
        public float SolverFrequency;
        public int SolverIterationCount;
        public double AverageFrameRate;
        public double MinOnUpdateMs;
        public double MaxOnUpdateMs;
        public double MeanOnUpdateMs;
        public int FrameCount;
        public int MarkerSampleCount;
        public string StatsSource;
        public bool HasParticleCount;
        public bool HasGridCellCount;
        public bool HasSolverFrequency;
        public bool HasSolverIterationCount;
        public bool HasAverageFrameRate;
        public bool HasTimingSamples;
        public bool HasAnyFilenameFallbackStat;

        public bool HasAnyCaptureStat =>
            HasParticleCount || HasGridCellCount || HasSolverFrequency || HasSolverIterationCount;

        public static CaptureAnalysis Create(string captureName)
        {
            return new CaptureAnalysis
            {
                CaptureName = captureName,
                StatsSource = string.Empty
            };
        }
    }
}
#endif

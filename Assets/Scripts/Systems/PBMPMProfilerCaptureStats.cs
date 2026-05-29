using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PBMPMProfilerCaptureStats
{
    public const string ParticleCountCounterName = "PBMPM Particle Count";
    public const string GridCellCountCounterName = "PBMPM Grid Cell Count";
    public const string SolverFrequencyCounterName = "PBMPM Solver Frequency";
    public const string SolverIterationCountCounterName = "PBMPM Solver Iteration Count";
    public const int SolverRunSampleWindow = 200;
    private const int FrameRateSampleWindow = 300;

    private const string CsvDirectoryName = "ProfilerCaptures";
    private const string CsvFileName = "pbmpm-profiler-capture-stats.csv";
    private const string CsvHeader =
        "capture_name,particle_count,grid_cell_count,solver_frequency_hz,solver_iteration_count,average_frame_rate_fps,pbmpm_onupdate_min_ms,pbmpm_onupdate_max_ms,pbmpm_onupdate_mean_ms,frame_count,marker_sample_count,stats_source";

    private static readonly ProfilerCategory Category = new ProfilerCategory("PBMPM");
    private static readonly ProfilerCounter<int> ParticleCountCounter =
        new ProfilerCounter<int>(Category, ParticleCountCounterName, ProfilerMarkerDataUnit.Count);
    private static readonly ProfilerCounter<int> GridCellCountCounter =
        new ProfilerCounter<int>(Category, GridCellCountCounterName, ProfilerMarkerDataUnit.Count);
    private static readonly ProfilerCounter<float> SolverFrequencyCounter =
        new ProfilerCounter<float>(Category, SolverFrequencyCounterName, ProfilerMarkerDataUnit.Undefined);
    private static readonly ProfilerCounter<int> SolverIterationCountCounter =
        new ProfilerCounter<int>(Category, SolverIterationCountCounterName, ProfilerMarkerDataUnit.Count);
    private static readonly double[] SolverRunTimeSamplesMs = new double[SolverRunSampleWindow];
    private static readonly float[] FrameRateSamples = new float[FrameRateSampleWindow];

    private static int _particleCount;
    private static int _gridCellCount;
    private static float _solverFrequency;
    private static int _solverIterationCount;
    private static int _solverRunSampleIndex;
    private static int _solverRunSampleCount;
    private static int _frameRateSampleIndex;
    private static int _frameRateSampleCount;

    public static string RuntimeCsvRelativePath => Path.Combine(CsvDirectoryName, CsvFileName);

    public struct SolverRunTimeSnapshot
    {
        public bool HasSamples;
        public int SampleCount;
        public int ParticleCount;
        public int GridCellCount;
        public float SolverFrequency;
        public int SolverIterationCount;
        public float AverageFrameRate;
        public double MinMs;
        public double MaxMs;
        public double MeanMs;
    }

    public static void SampleFrame(
        int particleCount,
        int gridCellCount,
        float solverFrequency,
        int solverIterationCount,
        float frameDeltaTime,
        bool solverRan,
        double solverUpdateTimeMs)
    {
        _particleCount = particleCount;
        _gridCellCount = gridCellCount;
        _solverFrequency = solverFrequency;
        _solverIterationCount = solverIterationCount;

        ParticleCountCounter.Sample(particleCount);
        GridCellCountCounter.Sample(gridCellCount);
        SolverFrequencyCounter.Sample(solverFrequency);
        SolverIterationCountCounter.Sample(solverIterationCount);
        float frameRate = frameDeltaTime > 1e-6f ? 1f / frameDeltaTime : 0f;
        FrameRateSamples[_frameRateSampleIndex] = frameRate;
        _frameRateSampleIndex = (_frameRateSampleIndex + 1) % FrameRateSampleWindow;
        _frameRateSampleCount = Math.Min(_frameRateSampleCount + 1, FrameRateSampleWindow);

        if (!solverRan)
        {
            return;
        }

        SolverRunTimeSamplesMs[_solverRunSampleIndex] = Math.Max(0d, solverUpdateTimeMs);
        _solverRunSampleIndex = (_solverRunSampleIndex + 1) % SolverRunSampleWindow;
        _solverRunSampleCount = Math.Min(_solverRunSampleCount + 1, SolverRunSampleWindow);
    }

    public static SolverRunTimeSnapshot CaptureSolverRunTimeSnapshot()
    {
        SolverRunTimeSnapshot snapshot = new SolverRunTimeSnapshot
        {
            ParticleCount = _particleCount,
            GridCellCount = _gridCellCount,
            SolverFrequency = _solverFrequency,
            SolverIterationCount = _solverIterationCount,
            AverageFrameRate = GetAverageFrameRate(),
            SampleCount = _solverRunSampleCount,
            HasSamples = _solverRunSampleCount > 0
        };

        if (!snapshot.HasSamples)
        {
            return snapshot;
        }

        double minMs = double.MaxValue;
        double maxMs = double.MinValue;
        double sumMs = 0d;
        for (int sampleIndex = 0; sampleIndex < _solverRunSampleCount; sampleIndex++)
        {
            double sampleMs = SolverRunTimeSamplesMs[sampleIndex];
            minMs = Math.Min(minMs, sampleMs);
            maxMs = Math.Max(maxMs, sampleMs);
            sumMs += sampleMs;
        }

        snapshot.MinMs = minMs;
        snapshot.MaxMs = maxMs;
        snapshot.MeanMs = sumMs / _solverRunSampleCount;
        return snapshot;
    }

    public static bool TryAppendCsvEntry(out string outputPath, out string errorMessage)
    {
        SolverRunTimeSnapshot snapshot = CaptureSolverRunTimeSnapshot();
        outputPath = GetCsvOutputPath();
        if (!snapshot.HasSamples)
        {
            errorMessage = "No solver runtime samples recorded yet.";
            return false;
        }

        try
        {
            string directoryPath = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var csv = new StringBuilder();
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                csv.AppendLine(CsvHeader);
            }

            csv.Append(Escape(BuildCaptureName(snapshot))).Append(',');
            csv.Append(snapshot.ParticleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.GridCellCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.SolverFrequency.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.SolverIterationCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.AverageFrameRate.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.MinMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.MaxMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.MeanMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(snapshot.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(Escape("runtime_overlay")).AppendLine();

            File.AppendAllText(outputPath, csv.ToString(), Encoding.UTF8);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = $"Failed to write runtime stats CSV: {exception.Message}";
            return false;
        }
    }

    public static void ResetRuntimeStats()
    {
        Array.Clear(SolverRunTimeSamplesMs, 0, SolverRunTimeSamplesMs.Length);
        _particleCount = 0;
        _gridCellCount = 0;
        _solverFrequency = 0f;
        _solverIterationCount = 0;
        _solverRunSampleIndex = 0;
        _solverRunSampleCount = 0;
        _frameRateSampleIndex = 0;
        _frameRateSampleCount = 0;
        Array.Clear(FrameRateSamples, 0, FrameRateSamples.Length);
    }

    private static string GetCsvOutputPath()
    {
        string rootPath = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(rootPath))
        {
            rootPath = Application.persistentDataPath;
        }

        return Path.Combine(rootPath, CsvDirectoryName, CsvFileName);
    }

    private static string BuildCaptureName(SolverRunTimeSnapshot snapshot)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string sceneName = activeScene.IsValid() && !string.IsNullOrWhiteSpace(activeScene.name)
            ? activeScene.name
            : Application.productName;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}P_{2}C_{3:0.###}Hz_{4}Iter_{5}",
            sceneName,
            snapshot.ParticleCount,
            snapshot.GridCellCount,
            snapshot.SolverFrequency,
            snapshot.SolverIterationCount,
            timestamp);
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

    private static float GetAverageFrameRate()
    {
        if (_frameRateSampleCount <= 0)
        {
            return 0f;
        }

        float totalFrameRate = 0f;
        for (int sampleIndex = 0; sampleIndex < _frameRateSampleCount; sampleIndex++)
        {
            totalFrameRate += FrameRateSamples[sampleIndex];
        }

        return totalFrameRate / _frameRateSampleCount;
    }
}

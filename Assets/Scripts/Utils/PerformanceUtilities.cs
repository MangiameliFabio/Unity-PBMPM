using System.Diagnostics;
using Unity.Profiling;

static class PerformanceUtilities
{
    private static readonly ProfilerMarker SolverUpdateProfilerMarker = new ProfilerMarker("PB-MPMSolverUpdate");
    private static readonly double StopwatchTickToMilliseconds = 1000d / Stopwatch.Frequency;

    public static long BeginSolverUpdateMeasurement(out ProfilerMarker.AutoScope profilerScope)
    {
        profilerScope = SolverUpdateProfilerMarker.Auto();
        return Stopwatch.GetTimestamp();
    }

    public static double EndSolverUpdateMeasurement(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * StopwatchTickToMilliseconds;
    }
}

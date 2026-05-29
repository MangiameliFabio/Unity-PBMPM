using Unity.Entities;

public struct SimulationDebugStats : IComponentData
{
    public int ParticleCount;
    public int SolverIterations;
    public int SolverSubsteps;
}

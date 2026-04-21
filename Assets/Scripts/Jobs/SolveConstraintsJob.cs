using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
partial struct SolveConstraintsJob : IJobEntity
{
    public bool UseGridVolumePreservation;
    public float LiquidHydroFactor;
    public float LiquidViscosityFactor;

    private void Execute(ref ParticleComponent particle)
    {
        //PBMPM Paper Algorithm 2
        float3x3 D = particle.DeformationDisplacement;
        float c = D.c0.x + D.c1.y + D.c2.z;
        float liquidDensity = UseGridVolumePreservation
            ? math.max(particle.GridMeasuredLiquidDensity, 1e-4f)
            : math.max(particle.LiquidDensity, 1e-4f);

        // Use the objective grid-measured liquid density rather than only particle-local deformation history.
        float3x3 dHydro = float3x3.identity * (liquidDensity - 1f - c);
        float3x3 dVisc = -(D - c * float3x3.identity);

        D += LiquidHydroFactor * dHydro;
        D += LiquidViscosityFactor * dVisc;

        particle.DeformationDisplacement = D;
        particle.LiquidDensity = liquidDensity;
    }
}

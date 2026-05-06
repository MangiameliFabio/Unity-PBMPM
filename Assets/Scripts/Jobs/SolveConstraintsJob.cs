using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
partial struct SolveConstraintsJob : IJobEntity
{
    public bool UseGridVolumePreservation;

    private void Execute(ref ParticleComponent particle)
    {
        //PBMPM Paper Algorithm 2
        float3x3 D = particle.DeformationDisplacement;
        float c = D.c0.x + D.c1.y + D.c2.z;
        float deformationVolume = math.max(math.determinant(particle.DeformationGradient), 1e-4f);
        float particleLocalLiquidDensity = 1f / deformationVolume;
        float liquidDensity = UseGridVolumePreservation
            ? math.max(particle.GridMeasuredLiquidDensity, 1e-4f)
            : math.max(particleLocalLiquidDensity, 1e-4f);

        // Use the objective grid-measured liquid density when enabled; otherwise fall back
        // to the particle-local density implied by the deformation gradient volume change.
        float3x3 dHydro = float3x3.identity * (liquidDensity - 1f - c);
        float3x3 dVisc = -(D - c * float3x3.identity);

        D += particle.LiquidHydroFactor * dHydro;
        D += particle.LiquidViscosityFactor * dVisc;

        particle.DeformationDisplacement = D;
        particle.LiquidDensity = liquidDensity;
    }
}

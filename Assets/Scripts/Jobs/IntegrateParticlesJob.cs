using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
partial struct IntegrateParticlesJob : IJobEntity
{
    public float DeltaTime;
    [ReadOnly] public float FrictionCoefficient;
    [ReadOnly] public NativeArray<GridBoxCollider> Colliders;

    private void Execute(ref ParticleComponent particle, ref LocalTransform transform)
    {
        particle.Position += particle.Displacement;

        //MPM Section 9.4 “Deformation Gradient Evolution”
        //Section 10.2, “Deformation Gradient Update”
        particle.DeformationGradient = math.mul(
            float3x3.identity + particle.DeformationDisplacement, particle.DeformationGradient);

        foreach (var collider in Colliders)
        {
            float phi = collider.GetSignedDistance(particle.Position);
            if (phi <= 0f)
            {
                float3 normal = collider.GetNormal(particle.Position);

                particle.Position -= phi * normal;
                particle.Position += 1e-4f * normal;

                particle.Displacement = particle.Position - transform.Position;
            }
        }

        particle.Velocity = particle.Displacement / DeltaTime;
        transform.Position = particle.Position;
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
partial struct IntegrateParticlesJob : IJobEntity
{
    public float DeltaTime;
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
            GridBoxCollider.CollisionResult collision = collider.Collide(particle.Position);
            if (collision.Collides)
            {
                particle.Displacement -= collision.Penetration * collision.Normal;
                particle.Position -= collision.Penetration * collision.Normal;
            }
        }

        particle.Velocity = particle.Displacement / DeltaTime;
        transform.Position = particle.Position;
    }
}

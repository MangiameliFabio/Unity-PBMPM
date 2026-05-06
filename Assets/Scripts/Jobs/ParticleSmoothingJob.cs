using Unity.Entities;
using Unity.Transforms;

partial struct ParticleSmoothingJob : IJobEntity
{
    public float DeltaTime;
    public bool UseVisualSmoothing;
    
    private void Execute(ref ParticleComponent particle, ref LocalTransform transform)
    {
        if (UseVisualSmoothing)
            transform.Position += particle.Velocity * DeltaTime;
        else
            transform.Position = particle.Position;
    }
}
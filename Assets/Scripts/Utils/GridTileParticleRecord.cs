using Unity.Mathematics;

struct GridTileParticleRecord
{
    public float3 Position;
    public float3 Displacement;
    public float3x3 DeformationDisplacement;
    public float Mass;
    public float Volume;
}
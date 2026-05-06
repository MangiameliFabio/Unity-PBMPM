using Unity.Mathematics;

public struct GridBoxCollider
{
    public struct CollisionResult
    {
        public bool Collides;
        public float Penetration;
        public float3 Normal;
        public float3 PointOnCollider;
    }

    public float3 Center;
    public quaternion Rotation;
    public float3 HalfExtents;
    public CollisionResult Collide(float3 x)
    {
        float3 localPoint = math.rotate(math.inverse(Rotation), x - Center);
        float3 distanceToFaces = HalfExtents - math.abs(localPoint);
        float minPenetration = math.cmin(distanceToFaces);

        if (minPenetration <= 0f)
        {
            return default;
        }

        float3 localNormal;
        float3 localPointOnCollider = localPoint;

        if (distanceToFaces.x <= distanceToFaces.y && distanceToFaces.x <= distanceToFaces.z)
        {
            float sign = localPoint.x >= 0f ? 1f : -1f;
            localNormal = new float3(-sign, 0f, 0f);
            localPointOnCollider.x = sign * HalfExtents.x;
        }
        else if (distanceToFaces.y <= distanceToFaces.z)
        {
            float sign = localPoint.y >= 0f ? 1f : -1f;
            localNormal = new float3(0f, -sign, 0f);
            localPointOnCollider.y = sign * HalfExtents.y;
        }
        else
        {
            float sign = localPoint.z >= 0f ? 1f : -1f;
            localNormal = new float3(0f, 0f, -sign);
            localPointOnCollider.z = sign * HalfExtents.z;
        }

        return new CollisionResult
        {
            Collides = true,
            Penetration = minPenetration,
            Normal = math.rotate(Rotation, localNormal),
            PointOnCollider = Center + math.rotate(Rotation, localPointOnCollider)
        };
    }
}

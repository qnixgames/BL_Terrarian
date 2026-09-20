#ifndef BYTESIZED_GRASS_HELPERS_INCLUDED
#define BYTESIZED_GRASS_HELPERS_INCLUDED

float rand(float3 co)
{
    return frac(sin(dot(co.xyz, float3(12.9898, 78.233, 53.539))) * 43758.5453);
}

// Avoid Unity IsNormalized assertions from normalize(0).
float3 GrassSafeNormalize(float3 v, float3 fallback)
{
    float lenSq = max(dot(v, v), 1e-8);
    return lenSq > 1e-6 ? v * rsqrt(lenSq) : fallback;
}

float3x3 RotationMatrix(float angle, float3 axis)
{
    axis = GrassSafeNormalize(axis, float3(0, 0, 1));
    float c, s;
    sincos(angle, s, c);

    float t = 1.0 - c;
    float x = axis.x;
    float y = axis.y;
    float z = axis.z;

    return float3x3(
        t * x * x + c, t * x * y - s * z, t * x * z + s * y,
        t * x * y + s * z, t * y * y + c, t * y * z - s * x,
        t * x * z - s * y, t * y * z + s * x, t * z * z + c
    );
}

float3x3 TangentToLocal(float3 normal, float4 tangent)
{
    normal = GrassSafeNormalize(normal, float3(0, 1, 0));
    float3 t = GrassSafeNormalize(tangent.xyz, float3(1, 0, 0));
    // Re-orthogonalize tangent against normal.
    t = GrassSafeNormalize(t - normal * dot(t, normal), float3(1, 0, 0));
    float3 binormal = cross(normal, t) * sign(tangent.w == 0.0 ? 1.0 : tangent.w);
    return float3x3(
        t.x, binormal.x, normal.x,
        t.y, binormal.y, normal.y,
        t.z, binormal.z, normal.z
    );
}

#endif

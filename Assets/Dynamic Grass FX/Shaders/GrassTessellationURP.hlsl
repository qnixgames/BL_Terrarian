#ifndef BYTESIZED_GRASS_TESSELLATION_URP_INCLUDED
#define BYTESIZED_GRASS_TESSELLATION_URP_INCLUDED

struct TessVertexInput
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
};

// Use POSITION (not SV_POSITION) so domain→geometry keeps object space on D3D12.
struct TessVertexOutput
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
};

struct TessellationFactors
{
    float edge[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

float TessellationEdgeFactor(TessVertexInput cp0, TessVertexInput cp1)
{
    float3 p0 = TransformObjectToWorld(cp0.positionOS.xyz);
    float3 p1 = TransformObjectToWorld(cp1.positionOS.xyz);
    float edgeLength = distance(p0, p1);
    float3 edgeCenter = (p0 + p1) * 0.5;
    float viewDistance = distance(edgeCenter, _WorldSpaceCameraPos);
    return min(_MaxStages, edgeLength / (viewDistance / _ViewLOD)) + _BaseStages;
}

TessellationFactors constantFunction(InputPatch<TessVertexInput, 3> patch)
{
    TessellationFactors f;
    f.edge[0] = TessellationEdgeFactor(patch[1], patch[2]);
    f.edge[1] = TessellationEdgeFactor(patch[2], patch[0]);
    f.edge[2] = TessellationEdgeFactor(patch[0], patch[1]);
    f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) * (1.0 / 3.0);
    return f;
}

[domain("tri")]
[outputcontrolpoints(3)]
[outputtopology("triangle_cw")]
[partitioning("fractional_odd")]
[patchconstantfunc("constantFunction")]
TessVertexInput hull(InputPatch<TessVertexInput, 3> patch, uint id : SV_OutputControlPointID)
{
    return patch[id];
}

TessVertexOutput tessVert(TessVertexInput v)
{
    TessVertexOutput o;
    o.positionOS = v.positionOS;
    o.normalOS = v.normalOS;
    o.tangentOS = v.tangentOS;
    return o;
}

[domain("tri")]
TessVertexOutput domain(
    TessellationFactors factors,
    OutputPatch<TessVertexInput, 3> patch,
    float3 barycentricCoordinates : SV_DomainLocation)
{
    TessVertexInput v;

#define DOMAIN_INTERPOLATE(fieldName) \
    v.fieldName = patch[0].fieldName * barycentricCoordinates.x + \
                  patch[1].fieldName * barycentricCoordinates.y + \
                  patch[2].fieldName * barycentricCoordinates.z;

    DOMAIN_INTERPOLATE(positionOS)
    DOMAIN_INTERPOLATE(normalOS)
    DOMAIN_INTERPOLATE(tangentOS)

#undef DOMAIN_INTERPOLATE

    return tessVert(v);
}

TessVertexInput vert(TessVertexInput v)
{
    return v;
}

#endif

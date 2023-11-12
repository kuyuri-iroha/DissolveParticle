#define KUYURI_MESHSAMPLINGHEADER_HLSL
#ifdef KUYURI_MESHSAMPLINGHEADER_HLSL

struct MeshSamplingPoint
{
    float3 position;
    float3 positionOS;
    float3 normal;
    float3 velocity;
    uint meshIndex;
    float2 uv;
};

struct SkinnedMeshData
{
    int meshIndex;
    int materialCount;
            
    float4x4 currentRootMatrix;
    float4x4 previousRootMatrix;
    float4x4 worldToLocalTransform;
};

#define MESH_MAX 256

#endif
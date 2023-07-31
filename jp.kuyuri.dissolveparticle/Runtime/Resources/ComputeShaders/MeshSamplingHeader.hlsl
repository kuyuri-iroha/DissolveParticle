#define KUYURI_MESHSAMPLINGHEADER_HLSL
#ifdef KUYURI_MESHSAMPLINGHEADER_HLSL

struct MeshSamplingPoint
{
    float3 position;
    float3 positionOS;
    float3 normal;
    float3 velocity;
    float2 uv;
};

#define WORLD_TO_LOCAL_TRANSFORM_MAX 64

#endif
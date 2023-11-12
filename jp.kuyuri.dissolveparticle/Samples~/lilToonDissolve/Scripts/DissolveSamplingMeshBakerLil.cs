using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.VFX;

namespace Kuyuri
{
    [VFXType(VFXTypeAttribute.Usage.GraphicsBuffer), Serializable]
    public struct DissolveSamplingPointLil
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector3 velocity;
        public Vector2 uv;
        public uint meshIndex;
        public float enabled;
    };
    
    public class DissolveSamplingMeshBakerLil : SkinnedMeshBaker
    {
        public struct DissolveMeshData
        {
            public int isDissolve;
            public Vector3 dissolvePosition;
            public float dissolveRange;
            public float dissolveBlur;
        }
        
        private readonly ComputeShader _dissolveBorderCompute;
        private GraphicsBuffer _dissolveBorderSamplingBuffer;
        private GraphicsBuffer _dissolveMeshDataBuffer;

        private int _samplingKernelIndex;
        
        public GraphicsBuffer DissolveBorderSamplingBuffer => _dissolveBorderSamplingBuffer;

        public DissolveSamplingMeshBakerLil()
        {
            _dissolveBorderCompute = Resources.Load<ComputeShader>("ComputeShaders/DissolveBorderSamplingLil");
            
            _samplingKernelIndex = _dissolveBorderCompute.FindKernel("DissolveBorderSamplingLil");
        }

        /// <summary>
        /// ディゾルブ境界をリサンプリング
        /// </summary>
        public void UpdateBuffer(DissolveMeshData[] dissolveMeshData)
        {
            base.UpdateBuffer();
            
            _dissolveMeshDataBuffer.SetData(dissolveMeshData);
            
            if (!IsValid) return;
            
            // Initialize Dispatch
            _dissolveBorderCompute.SetBuffer(0, "DissolveBorderSamplingBuffer", _dissolveBorderSamplingBuffer);
            _dissolveBorderCompute.Dispatch(0, VertexCount / ComputeThreadNum, 1, 1);
            
            // Sampling Dispatch
            _dissolveBorderCompute.SetInt("SourceCount", VertexCount);
            
            _dissolveBorderCompute.SetBuffer(_samplingKernelIndex, "MeshSamplingBuffer", MeshSamplingBuffer);
            _dissolveBorderCompute.SetBuffer(_samplingKernelIndex, "DissolveBorderSamplingBuffer", _dissolveBorderSamplingBuffer);
            _dissolveBorderCompute.SetBuffer(_samplingKernelIndex, "DissolveMeshDataBuffer", _dissolveMeshDataBuffer);
            
            _dissolveBorderCompute.Dispatch(_samplingKernelIndex, VertexCount / ComputeThreadNum, 1, 1);
        }

        public void SetSamplingKernelMethod(string kernelName)
        {
            _samplingKernelIndex = _dissolveBorderCompute.FindKernel(kernelName);
        }

        public override void Validation()
        {
            base.Validation();
            
            Dispose();
        }

        public override void Dispose()
        {
            base.Dispose();
            
            _dissolveBorderSamplingBuffer?.Dispose();
            _dissolveBorderSamplingBuffer = null;
            
            _dissolveMeshDataBuffer?.Dispose();
            _dissolveMeshDataBuffer = null;
        }

        protected override void Initialize()
        {
            base.Initialize();
            
            _dissolveBorderSamplingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VertexCount, Marshal.SizeOf<DissolveSamplingPointLil>());
            _dissolveMeshDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SkinnedMeshSources.Count, Marshal.SizeOf<DissolveMeshData>());
        }
    }
}
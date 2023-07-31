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
        public float enabled;
    };
    
    public class DissolveSamplingMeshBakerLil : SkinnedMeshBaker
    {
        private readonly ComputeShader _dissolveBorderCompute;
        private GraphicsBuffer _dissolveBorderSamplingBuffer;

        private int _samplingKernelIndex;
        
        public GraphicsBuffer DissolveBorderSamplingBuffer => _dissolveBorderSamplingBuffer;

        public DissolveSamplingMeshBakerLil()
        {
            _dissolveBorderCompute = Resources.Load<ComputeShader>("ComputeShaders/DissolveBorderSamplingLil");
            
            _samplingKernelIndex = _dissolveBorderCompute.FindKernel("DissolveBorderSamplingLil");
        }
        
        /// <summary>
        /// 使わないため、呼び出すと例外を投げる
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override void UpdateBuffer()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// ディゾルブに必要なパラメータを設定してディゾルブ境界をリサンプリング
        /// </summary>
        /// <param name="dissolvePosition"></param>
        /// <param name="dissolveRange"></param>
        /// <param name="dissolveBlur"></param>
        public void UpdateBuffer(Vector3 dissolvePosition, float dissolveRange, float dissolveBlur)
        {
            base.UpdateBuffer();
            
            if (!IsValid) return;
            
            // Initialize Dispatch
            _dissolveBorderCompute.SetBuffer(0, "DissolveBorderSamplingBuffer", _dissolveBorderSamplingBuffer);
            _dissolveBorderCompute.Dispatch(0, VertexCount / ComputeThreadNum, 1, 1);
            
            // Sampling Dispatch
            _dissolveBorderCompute.SetInt("SourceCount", VertexCount);
            
            _dissolveBorderCompute.SetVector("DissolvePosition", dissolvePosition);
            _dissolveBorderCompute.SetFloat("DissolveRange", dissolveRange);
            _dissolveBorderCompute.SetFloat("DissolveBlur", dissolveBlur);
            
            _dissolveBorderCompute.SetBuffer(_samplingKernelIndex, "MeshSamplingBuffer", MeshSamplingBuffer);
            _dissolveBorderCompute.SetBuffer(_samplingKernelIndex, "DissolveBorderSamplingBuffer", _dissolveBorderSamplingBuffer);
            
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
        }

        protected override void Initialize()
        {
            base.Initialize();
            
            _dissolveBorderSamplingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VertexCount, Marshal.SizeOf<DissolveSamplingPointLil>());
        }
    }
}
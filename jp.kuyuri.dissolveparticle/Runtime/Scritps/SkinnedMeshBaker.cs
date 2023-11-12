using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using Kuyuri.Externals.Smrvfx;
using UnityEngine;
using UnityEngine.VFX;

namespace Kuyuri
{
    [VFXType(VFXTypeAttribute.Usage.GraphicsBuffer), Serializable]
    public struct SkinnedMeshSampling
    {
        public Vector3 position;
        public Vector3 positionOS;
        public Vector3 normal;
        public Vector3 velocity;
        public Vector2 uv;
        public uint meshIndex;
    }
    
    public class SkinnedMeshBaker
    {
        private struct SkinnedMeshData
        {
            public int MeshIndex;
            public int MaterialCount;
            
            public Matrix4x4 CurrentRootMatrix;
            public Matrix4x4 PreviousRootMatrix;
            public Matrix4x4 WorldToLocalTransform;
        }
        
        private OrderedDictionary _skinnedMeshsources;
        public OrderedDictionary SkinnedMeshSources => _skinnedMeshsources;
        private int _pointCount = 65536;
        
        private readonly ComputeShader _compute = null;

        protected int VertexCount => _pointCount;

        public GraphicsBuffer MeshSamplingBuffer => _meshSamplingBuffer;

        protected bool IsValid => _skinnedMeshsources is { Count: > 0 };
        
        private GraphicsBuffer _samplePoints;
        private GraphicsBuffer _positionBuffer1;
        private GraphicsBuffer _positionOSIndexBuffer;
        private GraphicsBuffer _positionBuffer2;
        private GraphicsBuffer _normalBuffer;
        private GraphicsBuffer _uvBuffer;
        private GraphicsBuffer _skinnedMeshDataBuffer;

        private GraphicsBuffer _meshSamplingBuffer;

        private Mesh _tempMesh;


        protected const int ComputeThreadNum = 64;
        protected const int MaxSkinnedMeshSourceCount = 256;

        #region Public

        public SkinnedMeshBaker()
        {
            _compute = Resources.Load<ComputeShader>("ComputeShaders/TransferSkinnedMeshSamplingBuffer");
        }

        /// <summary>
        /// バッファのアップデート
        /// 破棄されていたり初期状態であればバッファの初期化処理を入れる
        /// </summary>
        public virtual void UpdateBuffer()
        {
            if (!IsValid) return;

            // Lazy initialization
            if (_tempMesh == null) Initialize();

            // Bake the sources into the buffers.
            var skinnedMeshRenderers = _skinnedMeshsources.Keys.Cast<SkinnedMeshRenderer>().ToArray();
            var offset = 0;
            for (var i = 0; i < _skinnedMeshsources.Count; i++)
            {
                var skinnedMeshRenderer = skinnedMeshRenderers[i];
                offset += BakeSource(skinnedMeshRenderer, offset);
                
                var data = (SkinnedMeshData) _skinnedMeshsources[i];
                data.PreviousRootMatrix = data.CurrentRootMatrix;
                data.CurrentRootMatrix = skinnedMeshRenderer.transform.localToWorldMatrix;
                data.WorldToLocalTransform = skinnedMeshRenderer.rootBone.worldToLocalMatrix;
                
                _skinnedMeshsources[i] = data;
            }
            
            _skinnedMeshDataBuffer.SetData(_skinnedMeshsources.Values.Cast<SkinnedMeshData>().ToArray());

            // Transfer to MeshSampling
            TransferMeshSampling();

            // Position buffer swapping
            (_positionBuffer1, _positionBuffer2)
                = (_positionBuffer2, _positionBuffer1);
        }

        /// <summary>
        /// 変数の整合性チェックとキャッシュの破棄
        /// パラメータが変更されたときに呼び出す
        /// </summary>
        public virtual void Validation()
        {
            _pointCount = Mathf.Max(64, _pointCount);

            // We assume that someone changed the values/references in the
            // serialized fields, so let us dispose the internal objects to
            // re-initialize them with the new values/references. #BADCODE
            DisposeInternals();
        }

        /// <summary>
        /// キャッシュや変数の破棄
        /// </summary>
        public virtual void Dispose()
        {
            DisposeInternals();
        }

        /// <summary>
        /// サンプリングする頂点数の変更（Validation処理を省いたもの）
        /// 設定後にValidation処理を入れないと未定義動作になる
        /// </summary>
        /// <param name="vertexCount"></param>
        public void SetVertexCountNoValidation(int vertexCount)
        {
            _pointCount = vertexCount;
        }

        /// <summary>
        /// サンプリング対象のSkinnedMeshの変更（Validation処理を省いたもの）
        /// 設定後にValidation処理を入れないと未定義動作になる
        /// </summary>
        /// <param name="skinnedMeshRenderers"></param>
        public void SetSkinnedMeshesNoValidation(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            _skinnedMeshsources = new OrderedDictionary();
            for (var i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                _skinnedMeshsources.Add(skinnedMeshRenderers[i], new SkinnedMeshData()
                {
                    MeshIndex = i,
                    MaterialCount = skinnedMeshRenderers[i].sharedMaterials.Length,
                    CurrentRootMatrix = skinnedMeshRenderers[i].transform.localToWorldMatrix,
                    PreviousRootMatrix = skinnedMeshRenderers[i].transform.localToWorldMatrix,
                    WorldToLocalTransform = skinnedMeshRenderers[i].transform.worldToLocalMatrix
                });
            }
        }

        /// <summary>
        /// サンプリングする頂点数の変更
        /// </summary>
        /// <param name="vertexCount"></param>
        public void SetVertexCount(int vertexCount)
        {
            SetVertexCountNoValidation(vertexCount);
            
            Validation();
        }

        /// <summary>
        /// サンプリング対象のSkinnedMeshの変更
        /// </summary>
        /// <param name="skinnedMeshRenderers"></param>
        public void SetSkinnedMeshes(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            SetSkinnedMeshesNoValidation(skinnedMeshRenderers);
            
            Validation();
        }

        #endregion

        #region Private And Protected

        /// <summary>
        /// バッファなどの初期化
        /// </summary>
        protected virtual void Initialize()
        {
            if (_skinnedMeshsources == null || _skinnedMeshsources.Count == 0)
            {
                Debug.LogError("SkinnedMeshBaker: SkinnedMeshRenderer is not set.");
                return;
            }
            
            if (_skinnedMeshsources.Count > MaxSkinnedMeshSourceCount)
            {
                Debug.LogError($"Too many skinned mesh sources(${_skinnedMeshsources.Count}). Max count is {MaxSkinnedMeshSourceCount}");
                return;
            }

            var vCount = 0;
            using (var mesh = new CombinedMesh(_skinnedMeshsources.Keys.Cast<SkinnedMeshRenderer>().Select(v => v.sharedMesh)))
            {
                // Sample point generation
                using (var points = SamplePointGenerator.Generate
                                      (mesh, _pointCount))
                {
                    _samplePoints = new GraphicsBuffer
                      (GraphicsBuffer.Target.Structured, _pointCount, SamplePoint.SizeInByte);
                    _samplePoints.SetData(points);
                }

                // Intermediate buffer allocation
                vCount = mesh.Vertices.Length;
                const int float3Size = sizeof(float) * 3;
                _positionBuffer1 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, float3Size);
                _positionBuffer2 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, float3Size);
                _normalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, float3Size);
                _uvBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, sizeof(float) * 2);
                
                _skinnedMeshDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _skinnedMeshsources.Count, Marshal.SizeOf<SkinnedMeshData>());
                
                _meshSamplingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,_pointCount, Marshal.SizeOf<SkinnedMeshSampling>());
            }

            // Object space
            _positionOSIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, sizeof(uint));
            var offset = 0;
            var skinnedMeshSourceI = 0;
            foreach (DictionaryEntry pair in _skinnedMeshsources)
            {
                var skinnedMeshRenderer = (SkinnedMeshRenderer) pair.Key;
                var localVCount = skinnedMeshRenderer.sharedMesh.vertexCount;
                var osIndex = new int[vCount];
                for (var j = 0; j < osIndex.Length; j++)
                {
                    osIndex[j] = skinnedMeshSourceI;
                }
                _positionOSIndexBuffer.SetData(osIndex, 0, offset, localVCount);
                
                offset += localVCount;
                
                skinnedMeshSourceI++;
            }

            // Temporary mesh object
            _tempMesh = new Mesh();
            _tempMesh.hideFlags = HideFlags.DontSave;
        }

        /// <summary>
        /// バッファなどの破棄処理
        /// </summary>
        private void DisposeInternals()
        {
            _samplePoints?.Dispose();
            _samplePoints = null;

            _positionBuffer1?.Dispose();
            _positionBuffer1 = null;
            
            _positionOSIndexBuffer?.Dispose();
            _positionOSIndexBuffer = null;

            _positionBuffer2?.Dispose();
            _positionBuffer2 = null;

            _normalBuffer?.Dispose();
            _normalBuffer = null;
            
            _uvBuffer?.Dispose();
            _uvBuffer = null;
            
            _skinnedMeshDataBuffer?.Dispose();
            _skinnedMeshDataBuffer = null;
            
            _meshSamplingBuffer?.Dispose();
            _meshSamplingBuffer = null;

            ObjectUtil.Destroy(_tempMesh);
            _tempMesh = null;
        }

        /// <summary>
        /// 頂点情報をGraphicsBufferに転送
        /// </summary>
        /// <param name="source">元になるSkinnedMeshRenderer</param>
        /// <param name="offset">GraphicsBufferのoffset</param>
        /// <returns></returns>
        private int BakeSource(SkinnedMeshRenderer source, int offset)
        {
            source.BakeMesh(_tempMesh, true);

            using (var dataArray = Mesh.AcquireReadOnlyMeshData(_tempMesh))
            {
                var data = dataArray[0];
                var vCount = data.vertexCount;

                using (var pos = MemoryUtil.TempJobArray<Vector3>(vCount))
                using (var nrm = MemoryUtil.TempJobArray<Vector3>(vCount))
                using (var uv = MemoryUtil.TempJobArray<Vector2>(vCount))
                {
                    data.GetVertices(pos);
                    data.GetNormals(nrm);
                    data.GetUVs(0, uv);

                    _positionBuffer1.SetData(pos, 0, offset, vCount);
                    _normalBuffer.SetData(nrm, 0, offset, vCount);
                    _uvBuffer.SetData(uv, 0, offset, vCount);

                    return vCount;
                }
            }
        }

        /// <summary>
        /// メッシュのサンプリング
        /// </summary>
        private void TransferMeshSampling()
        {
            _compute.SetInt("SampleCount", _pointCount);
            _compute.SetFloat("FrameRate", 1 / Time.deltaTime);

            _compute.SetBuffer(0, "SamplePoints", _samplePoints);
            _compute.SetBuffer(0, "PositionBuffer", _positionBuffer1);
            _compute.SetBuffer(0, "PositionOSIndexBuffer", _positionOSIndexBuffer);
            _compute.SetBuffer(0, "OldPositionBuffer", _positionBuffer2);
            _compute.SetBuffer(0, "NormalBuffer", _normalBuffer);
            _compute.SetBuffer(0, "UvBuffer", _uvBuffer);
            _compute.SetBuffer(0, "SkinnedMeshDataBuffer", _skinnedMeshDataBuffer);
            
            _compute.SetBuffer(0, "MeshSamplingBuffer", _meshSamplingBuffer);

            var count = _pointCount;
            _compute.Dispatch(0, count / ComputeThreadNum, 1, 1);
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
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
    }
    
    public class SkinnedMeshBaker
    {
        private SkinnedMeshRenderer[] _sources;
        private int[] _sourcesMaterialsLength;
        private int _pointCount = 65536;
        
        private readonly ComputeShader _compute = null;

        public SkinnedMeshRenderer[] Sources => _sources;
        public int[] SourcesMaterialsLength => _sourcesMaterialsLength;
        
        public int VertexCount => _pointCount;

        public GraphicsBuffer MeshSamplingBuffer => _meshSamplingBuffer;
        
        public bool IsValid => _sources != null && _sources.Length > 0;
        
        private GraphicsBuffer _samplePoints;
        private GraphicsBuffer _positionBuffer1;
        private GraphicsBuffer _positionOSIndexBuffer;
        private GraphicsBuffer _positionBuffer2;
        private GraphicsBuffer _normalBuffer;
        private GraphicsBuffer _uvBuffer;

        private GraphicsBuffer _meshSamplingBuffer;

        private Matrix4x4[] _currentRootMatrices;
        private Matrix4x4[] _previousRootMatrices;
        private Matrix4x4[] _worldToLocalTransforms;
        public Matrix4x4[] WorldToLocalTransforms => _worldToLocalTransforms;
        private Mesh _tempMesh;


        protected const int ComputeThreadNum = 64;
        protected const int MaxSkinnedMeshSourceCount = 64;

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
            var offset = 0;
            for (var i = 0; i < _sources.Length; i++)
            {
                offset += BakeSource(_sources[i], offset);
                
                // Current transform matrix
                _currentRootMatrices[i] = _sources[i].transform.localToWorldMatrix;

                _worldToLocalTransforms[i] = _sources[i].transform.worldToLocalMatrix;
                _worldToLocalTransforms[i] = _sources[i].rootBone.worldToLocalMatrix;
            }

            // Transfer to MeshSampling
            TransferMeshSampling();

            // Position buffer swapping
            (_positionBuffer1, _positionBuffer2)
                = (_positionBuffer2, _positionBuffer1);
            
            // Previous transform matrix
            (_previousRootMatrices, _currentRootMatrices)
                = (_currentRootMatrices, _previousRootMatrices);
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
            _sources = skinnedMeshRenderers;
            
            var sourcesLength = new List<int>();
            for (var i = 0; i < _sources.Length; i++)
            {
                sourcesLength.Add(_sources[i].sharedMaterials.Length);
            }
            _sourcesMaterialsLength = sourcesLength.ToArray();
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
            var vCount = 0;
            using (var mesh = new CombinedMesh(_sources))
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
                _positionBuffer1 = new GraphicsBuffer(GraphicsBuffer.Target.Structured,vCount, float3Size);
                _positionBuffer2 = new GraphicsBuffer(GraphicsBuffer.Target.Structured,vCount, float3Size);
                _normalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,vCount, float3Size);
                _uvBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,vCount, sizeof(float) * 2);
                
                _meshSamplingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,_pointCount, Marshal.SizeOf<SkinnedMeshSampling>());
            }

            // Object space
            var sourceLength = _sources.Length;
            if (sourceLength > MaxSkinnedMeshSourceCount)
            {
                Debug.LogError($"Too many skinned mesh sources. Max count is {MaxSkinnedMeshSourceCount}");
                sourceLength = MaxSkinnedMeshSourceCount;
            }
            
            _previousRootMatrices = new Matrix4x4[sourceLength];
            _currentRootMatrices = new Matrix4x4[sourceLength];
            _worldToLocalTransforms = new Matrix4x4[sourceLength];
            _positionOSIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, sizeof(uint));
            
            var offset = 0;
            for(var i = 0; i < sourceLength; i++)
            {
                var localVCount = _sources[i].sharedMesh.vertexCount;
                var osIndex = new int[vCount];
                for (var j = 0; j < osIndex.Length; j++)
                {
                    osIndex[j] = i;
                }
                _positionOSIndexBuffer.SetData(osIndex, 0, offset, localVCount);
                
                offset += localVCount;
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
            _compute.SetMatrixArray("Transforms", _currentRootMatrices);
            _compute.SetMatrixArray("OldTransforms", _previousRootMatrices);
            _compute.SetFloat("FrameRate", 1 / Time.deltaTime);
            
            _compute.SetMatrixArray("WorldToLocalTransforms", _worldToLocalTransforms);

            _compute.SetBuffer(0, "SamplePoints", _samplePoints);
            _compute.SetBuffer(0, "PositionBuffer", _positionBuffer1);
            _compute.SetBuffer(0, "PositionOSIndexBuffer", _positionOSIndexBuffer);
            _compute.SetBuffer(0, "OldPositionBuffer", _positionBuffer2);
            _compute.SetBuffer(0, "NormalBuffer", _normalBuffer);
            _compute.SetBuffer(0, "UvBuffer", _uvBuffer);
            
            _compute.SetBuffer(0, "MeshSamplingBuffer", _meshSamplingBuffer);

            var count = _pointCount;
            _compute.Dispatch(0, count / ComputeThreadNum, 1, 1);
        }

        #endregion
    }
}
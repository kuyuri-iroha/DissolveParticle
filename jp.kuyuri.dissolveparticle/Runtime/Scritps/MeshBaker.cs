using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Kuyuri.Externals.Smrvfx;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Kuyuri
{
    [VFXType(VFXTypeAttribute.Usage.GraphicsBuffer), Serializable]
    public struct MeshSampling
    {
        public Vector3 position;
        public Vector3 positionOS;
        public Vector3 normal;
        public Vector3 velocity;
        public Vector2 uv;
    }
    
    public class MeshBaker
    {
        private MeshRenderer[] _sources;
        private int _pointCount = 65536;
        
        private readonly ComputeShader _compute = null;

        public IReadOnlyCollection<MeshRenderer> Renderers => _sources;
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

        private Transform[] _sourceTransforms;

        private Matrix4x4[] _currentRootMatrices;
        private Matrix4x4[] _previousRootMatrices;
        
        private Matrix4x4[] _worldToLocalTransforms;
        public Matrix4x4[] WorldToLocalTransforms => _worldToLocalTransforms;

        private Dictionary<MeshRenderer, Mesh> _staticMeshes;


        protected const int ComputeThreadNum = 64;
        protected const int MaxSkinnedMeshSourceCount = 128;

        #region Public

        public MeshBaker()
        {
            _compute = Resources.Load<ComputeShader>("ComputeShaders/TransferMeshSamplingBuffer");
        }

        /// <summary>
        /// バッファのアップデート
        /// 破棄されていたり初期状態であればバッファの初期化処理を入れる
        /// </summary>
        public virtual void UpdateBuffer()
        {
            if (!IsValid) return;

            for (var i = 0; i < _sources.Length; i++)
            {
                // Current transform matrix
                if(_currentRootMatrices.Length <= i) return;
                _currentRootMatrices[i] = _sourceTransforms[i].localToWorldMatrix;

                _worldToLocalTransforms[i] = _sourceTransforms[i].worldToLocalMatrix;
            }

            // Transfer to MeshSampling
            TransferMeshSampling();

            // Transform matrix history
            Array.Copy(_currentRootMatrices, _previousRootMatrices, _previousRootMatrices.Length);
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
        /// サンプリング対象のRendererの変更（Validation処理を省いたもの）
        /// 設定後にValidation処理を入れないと未定義動作になる
        /// </summary>
        /// <param name="renderers"></param>
        public void SetRenderersNoValidation(MeshRenderer[] renderers)
        {
            _sources = renderers;
            _sourceTransforms = _sources.Select(x => x.transform).ToArray();
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
        /// <param name="renderers"></param>
        public void SetRenderers(MeshRenderer[] renderers)
        {
            SetRenderersNoValidation(renderers);
            
            Validation();
        }

        #endregion

        #region Private And Protected

        /// <summary>
        /// バッファなどの初期化
        /// </summary>
        public virtual void Initialize()
        {
            // Static Mesh
            _staticMeshes = new Dictionary<MeshRenderer, Mesh>();
            foreach (var source in _sources)
            {
                if (source is MeshRenderer meshRenderer)
                {
                    var mesh = meshRenderer.gameObject.GetComponent<MeshFilter>().sharedMesh;
                    try
                    {
                        if (!mesh)
                        {
                            Debug.LogWarning($"{meshRenderer.gameObject.GetComponent<MeshFilter>().name} is MeshFilter but Mesh is null");
                        }
                        else if(!mesh.isReadable)
                        {
                            Debug.LogWarning($"{meshRenderer.gameObject.GetComponent<MeshFilter>().name} is MeshFilter but Mesh is not readable");
                        }
                        else
                        {
                            _staticMeshes.Add(source, mesh);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("MeshFilter error occurred.\n" + e.Message);
                    }
                }
            }
            
            var vCount = 0;
            using (var mesh = new CombinedMesh(_sources.Select(GetStaticMesh).Where(val => val != null)))
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
                
                _meshSamplingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,_pointCount, Marshal.SizeOf<MeshSampling>());
            }
            
            // Object space
            var sourceLength = _sources.Length;
            if (sourceLength > MaxSkinnedMeshSourceCount)
            {
                Debug.LogError($"Too many mesh sources. Max count is {MaxSkinnedMeshSourceCount}");
                sourceLength = MaxSkinnedMeshSourceCount;
            }

            _previousRootMatrices = new Matrix4x4[sourceLength];
            _currentRootMatrices = new Matrix4x4[sourceLength];
            _worldToLocalTransforms = new Matrix4x4[sourceLength];
            _positionOSIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vCount, sizeof(uint));
            
            var offset = 0;
            for(var i = 0; i < sourceLength; i++)
            {
                var mesh = GetStaticMesh(_sources[i]);
                if (mesh == null) continue;
                var localVCount = mesh.vertexCount;
                var osIndex = new int[vCount];
                for (var j = 0; j < osIndex.Length; j++)
                {
                    osIndex[j] = i;
                }
                _positionOSIndexBuffer.SetData(osIndex, 0, offset, localVCount);
                
                offset += localVCount;
            }
            
            // Bake the sources into the buffers.
            var bakeOffset = 0;
            for (var i = 0; i < _sources.Length; i++)
            {
                bakeOffset += BakeSource(_sources[i], bakeOffset);
                
                // Current transform matrix
                if(_currentRootMatrices.Length <= i) return;
                _currentRootMatrices[i] = _sourceTransforms[i].localToWorldMatrix;

                _worldToLocalTransforms[i] = _sourceTransforms[i].worldToLocalMatrix;
            }
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
        }

        /// <summary>
        /// 頂点情報をGraphicsBufferに転送
        /// </summary>
        /// <param name="source">元になるRenderer</param>
        /// <param name="offset">GraphicsBufferのoffset</param>
        /// <returns></returns>
        private int BakeSource(MeshRenderer source, int offset)
        {
            var mesh = _staticMeshes.TryGetValue(source, out var m) ? m : null;
            if (mesh == null) return 0;
            using (var dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
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
                    _positionBuffer2.SetData(pos, 0, offset, vCount);
                    _normalBuffer.SetData(nrm, 0, offset, vCount);
                    _uvBuffer.SetData(uv, 0, offset, vCount);

                    return vCount;
                }
            }
        }

        private void GetMesh(Renderer source, ref Mesh mesh)
        {
            if (source is MeshRenderer meshRenderer)
            {
                mesh = _staticMeshes[meshRenderer];
            }
            else
            {
                Debug.LogError($"{source.name}, Unsupported renderer type");
            }
        }
        
        private Mesh GetStaticMesh(Renderer source)
        {
            if (source is MeshRenderer meshRenderer)
            {
                return _staticMeshes.TryGetValue(meshRenderer, out var mesh) ? mesh : null;
            }
            Debug.LogError($"{source.name}, Unsupported renderer type");
            return null;
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
using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;

namespace Kuyuri
{
    [ExecuteAlways]
    public class AllSamplingMeshTransfer : MonoBehaviour
    {
        [FormerlySerializedAs("character")] [SerializeField] private GameObject parent;
        [SerializeField, Min(64)] private int pointCount = 65536;
        [SerializeField] private VisualEffect visualEffect;
        [SerializeField] private string meshSamplingBufferProperty = "MeshSamplingBuffer";
        
        private MeshBaker _meshBaker;
        private MeshRenderer[] _cachedMeshRenderers;
        private GameObject _lastParent;
        
        private void OnEnable()
        {
            _meshBaker = new MeshBaker();
            
            if (visualEffect != null && !visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                Debug.LogError($"{meshSamplingBufferProperty} not found in {visualEffect.name}.");
            }
            
            _meshBaker.SetVertexCountNoValidation(pointCount);
            _meshBaker.SetRenderersNoValidation(GetMeshesFromParent(parent));
            _meshBaker.Validation();
            
            _meshBaker.Initialize();
        }

        private void OnValidate()
        {
            if (_lastParent != parent)
            {
                _cachedMeshRenderers = null;
                _lastParent = parent;
            }
            _meshBaker?.Validation();
        }

        private void OnDisable()
        {
            _meshBaker?.Dispose();
            _cachedMeshRenderers = null;
            _lastParent = null;
        }

        private void OnDestroy()
        {
            _meshBaker?.Dispose();
            _cachedMeshRenderers = null;
            _lastParent = null;
        }

        private void Update()
        {
            UpdateBuffer();
        }

        #region Private

        private void UpdateBuffer()
        {
            if (_meshBaker == null) return;
            
            _meshBaker.UpdateBuffer();
            
            if (visualEffect != null && visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                visualEffect.SetGraphicsBuffer(meshSamplingBufferProperty, _meshBaker.MeshSamplingBuffer);
            }
        }

        private MeshRenderer[] GetMeshesFromParent(GameObject characterGameObject)
        {
            if (characterGameObject == null) return new MeshRenderer[0];
            
            if (_cachedMeshRenderers == null || _lastParent != characterGameObject)
            {
                _cachedMeshRenderers = characterGameObject.GetComponentsInChildren<MeshRenderer>();
                _lastParent = characterGameObject;
            }
            
            return _cachedMeshRenderers ?? new MeshRenderer[0];
        }

        #endregion
    }
}

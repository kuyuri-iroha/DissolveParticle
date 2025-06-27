using System;
using UnityEngine;
using UnityEngine.VFX;

namespace Kuyuri
{
    [ExecuteAlways]
    public class AllSamplingSkinnedMeshTransfer : MonoBehaviour
    {
        [SerializeField] private GameObject character;
        [SerializeField, Min(64)] private int pointCount = 65536;
        [SerializeField] private VisualEffect visualEffect;
        [SerializeField] private string meshSamplingBufferProperty = "MeshSamplingBuffer";
        
        private SkinnedMeshBaker _skinnedMeshBaker;
        private SkinnedMeshRenderer[] _cachedSkinnedMeshRenderers;
        private GameObject _lastCharacter;
        
        private void OnEnable()
        {
            _skinnedMeshBaker = new SkinnedMeshBaker();
            
            if (visualEffect != null && !visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                Debug.LogError($"{meshSamplingBufferProperty} not found in {visualEffect.name}.");
            }
            
            _skinnedMeshBaker.SetVertexCountNoValidation(pointCount);
            _skinnedMeshBaker.SetSkinnedMeshesNoValidation(GetSkinnedMeshesFromCharacter(character));
            _skinnedMeshBaker.Validation();
        }

        private void OnValidate()
        {
            if (_lastCharacter != character)
            {
                _cachedSkinnedMeshRenderers = null;
                _lastCharacter = character;
            }
            _skinnedMeshBaker?.Validation();
        }

        private void OnDisable()
        {
            _skinnedMeshBaker?.Dispose();
            _cachedSkinnedMeshRenderers = null;
            _lastCharacter = null;
        }

        private void OnDestroy()
        {
            _skinnedMeshBaker?.Dispose();
            _cachedSkinnedMeshRenderers = null;
            _lastCharacter = null;
        }

        private void Update()
        {
            UpdateBuffer();
        }

        #region Private

        private void UpdateBuffer()
        {
            if (_skinnedMeshBaker == null) return;
            
            _skinnedMeshBaker.UpdateBuffer();
            
            if (visualEffect != null && visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                visualEffect.SetGraphicsBuffer(meshSamplingBufferProperty, _skinnedMeshBaker.MeshSamplingBuffer);
            }
        }

        private SkinnedMeshRenderer[] GetSkinnedMeshesFromCharacter(GameObject characterGameObject)
        {
            if (characterGameObject == null) return new SkinnedMeshRenderer[0];
            
            if (_cachedSkinnedMeshRenderers == null || _lastCharacter != characterGameObject)
            {
                _cachedSkinnedMeshRenderers = characterGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                _lastCharacter = characterGameObject;
            }
            
            return _cachedSkinnedMeshRenderers ?? new SkinnedMeshRenderer[0];
        }

        #endregion
    }
}

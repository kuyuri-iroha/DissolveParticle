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
        
        private void OnEnable()
        {
            _skinnedMeshBaker = new SkinnedMeshBaker();
            
            if (!visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                Debug.LogError($"{meshSamplingBufferProperty} not found in {visualEffect.name}.");
            }
            
            _skinnedMeshBaker.SetVertexCountNoValidation(pointCount);
            _skinnedMeshBaker.SetSkinnedMeshesNoValidation(GetSkinnedMeshesFromCharacter(character));
            _skinnedMeshBaker.Validation();
        }

        private void OnValidate()
        {
            _skinnedMeshBaker?.Validation();
        }

        private void OnDisable()
        {
            _skinnedMeshBaker?.Dispose();
        }

        private void OnDestroy()
        {
            _skinnedMeshBaker?.Dispose();
        }

        private void Update()
        {
            UpdateBuffer();
        }

        #region Private

        private void UpdateBuffer()
        {
            _skinnedMeshBaker.UpdateBuffer();
            
            if (visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                visualEffect.SetGraphicsBuffer(meshSamplingBufferProperty, _skinnedMeshBaker.MeshSamplingBuffer);
            }
        }

        private SkinnedMeshRenderer[] GetSkinnedMeshesFromCharacter(GameObject characterGameObject)
        {
            return characterGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        #endregion
    }
}
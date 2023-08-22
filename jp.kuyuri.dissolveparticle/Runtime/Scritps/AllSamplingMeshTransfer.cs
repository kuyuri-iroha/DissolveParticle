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
        
        private void OnEnable()
        {
            _meshBaker = new MeshBaker();
            
            if (!visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
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
            _meshBaker?.Validation();
        }

        private void OnDisable()
        {
            _meshBaker?.Dispose();
        }

        private void OnDestroy()
        {
            _meshBaker?.Dispose();
        }

        private void Update()
        {
            UpdateBuffer();
        }

        #region Private

        private void UpdateBuffer()
        {
            _meshBaker.UpdateBuffer();
            
            if (visualEffect.HasGraphicsBuffer(meshSamplingBufferProperty))
            {
                visualEffect.SetGraphicsBuffer(meshSamplingBufferProperty, _meshBaker.MeshSamplingBuffer);
            }
        }

        private MeshRenderer[] GetMeshesFromParent(GameObject characterGameObject)
        {
            return characterGameObject.GetComponentsInChildren<MeshRenderer>();
        }

        #endregion
    }
}
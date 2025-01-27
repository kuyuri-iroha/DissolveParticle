using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;
using LitMotion;
using LitMotion.Extensions;

namespace Kuyuri
{

    /// <summary>
    /// lilToon用ディゾルブスクリプト
    /// 予めシェーダのディゾルブ設定を有効にしてください
    /// </summary>
    public class CharacterDissolveEffectLil : MonoBehaviour
    {
        
        [SerializeField] private GameObject characterSceneObject;

        [SerializeField, Min(64)] private int pointCount = 32768;
        [SerializeField] private VisualEffect visualEffect;
        [SerializeField] private string dissolveSamplingBufferProperty = "DissolveSamplingBuffer";
        [SerializeField] private string dissolveStartEventName = "DissolveStart";
        [SerializeField] private string spawnEventName = "Spawn";
        [SerializeField] private string stopEventName = "Stop";

        [SerializeField] private bool appearOnStart = true;

        [SerializeField] private float dissolveRange = 3f;
        [SerializeField] private float dissolveBlur = 0.1f;

        [Header("出現時のディゾルブポジション")] [SerializeField]
        private Vector3 appearStartPos = Vector3.zero;

        [SerializeField] private Vector3 appearEndPos = new Vector3(7f, 0, 0);

        [Header("消滅時のディゾルブポジション")] [SerializeField]
        private Vector3 disappearStartPos = new Vector3(-7f, 0, 0);

        [SerializeField] private Vector3 disappearEndPos = Vector3.zero;

        [Header("アニメーションのプロパティ")] [SerializeField]
        private float dissolveDuration = 1f;
        [SerializeField] private Ease dissolveEase = Ease.Linear;

        private DissolveSamplingMeshBakerLil dissolveSamplingMeshBakerLil;
        private bool _initialized = false;
        private bool _dissolving = false;

        private static readonly int DissolvePos = Shader.PropertyToID("_DissolvePos");
        private static readonly int DissolveParam = Shader.PropertyToID("_DissolveParams");
        
        private class DissolveEffectMeshData
        {
            public SkinnedMeshRenderer skinnedMeshRenderer;
            public MaterialPropertyBlock materialPropertyBlock;
            public DissolveSamplingMeshBakerLil.DissolveMeshData dissolveMeshData;
            
            public MotionHandle appearMotion;
            public MotionHandle disappearMotion;
            
            public Vector3 DissolvePosition
            {
                get => dissolveMeshData.dissolvePosition;
                set => SetDissolvePosition(value);
            }
        
            public bool isAppear = false;
        
            public DissolveEffectMeshData(SkinnedMeshRenderer skinnedMeshRenderer, Vector3 startDissolvePosition, bool appearOnStart, float dissolveRange, float dissolveBlur)
            {
                materialPropertyBlock = new MaterialPropertyBlock();

                this.skinnedMeshRenderer = skinnedMeshRenderer;
                dissolveMeshData.dissolveRange = dissolveRange;
                dissolveMeshData.dissolveBlur = dissolveBlur;
                dissolveMeshData.dissolvePosition = startDissolvePosition;
                isAppear = appearOnStart;
            }

            private void SetDissolvePosition(Vector3 dissolvePos)
            {
                // MaterialPropertyBlockを使用するとなぜか幸祜のMotionVectorだけが暴れるのでMaterialに直接Set
                dissolveMeshData.dissolvePosition = dissolvePos;
                foreach(var mat in skinnedMeshRenderer.materials)
                {
                    mat.SetVector(DissolvePos, dissolveMeshData.dissolvePosition);
                    mat.SetVector(DissolveParam, new Vector4(3.0f, 0, dissolveMeshData.dissolveRange, dissolveMeshData.dissolveBlur));
                }

                // skinnedMeshRenderer.GetPropertyBlock(materialPropertyBlock);
                // materialPropertyBlock.SetVector(DissolvePos, dissolveMeshData.dissolvePosition);
                // materialPropertyBlock.SetVector(DissolveParam, new Vector4(3.0f, 0, dissolveMeshData.dissolveRange, dissolveMeshData.dissolveBlur));
                // skinnedMeshRenderer.SetPropertyBlock(materialPropertyBlock);
            }
        }
        
        private OrderedDictionary _dissolveEffectMeshData = new OrderedDictionary();

        private void OnEnable()
        {
            _dissolveEffectMeshData.Clear();
            
            if(characterSceneObject == null)
            {
                characterSceneObject = transform.parent.gameObject;
            }
            if(visualEffect == null)
            {
                visualEffect = GetComponent<VisualEffect>();
            }
            
            var skinnedMeshRenderers = GetSkinnedMeshesFromCharacter(characterSceneObject);
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var dissolveEffectMeshData = new DissolveEffectMeshData(skinnedMeshRenderer, appearStartPos, appearOnStart, dissolveRange, dissolveBlur);
                _dissolveEffectMeshData.Add(skinnedMeshRenderer.name, dissolveEffectMeshData);
            }
            
            // Mesh baker
            dissolveSamplingMeshBakerLil = new DissolveSamplingMeshBakerLil();

            if (!visualEffect.HasGraphicsBuffer(dissolveSamplingBufferProperty))
            {
                Debug.LogError($"{dissolveSamplingBufferProperty} not found in {visualEffect.name}.");
            }

            dissolveSamplingMeshBakerLil.SetVertexCountNoValidation(pointCount);
            dissolveSamplingMeshBakerLil.SetSkinnedMeshesNoValidation(skinnedMeshRenderers);
            dissolveSamplingMeshBakerLil.Validation();

            // Dissolve
            var startDissolvePosition = Vector3.zero;
            if (appearOnStart)
            {
                startDissolvePosition = appearEndPos;
            }
            else
            {
                startDissolvePosition = appearStartPos;
            }
        }

        private void OnValidate()
        {
            dissolveSamplingMeshBakerLil?.Validation();
        }

        private void OnDisable()
        {
            dissolveSamplingMeshBakerLil?.Dispose();
        }

        private void OnDestroy()
        {
            dissolveSamplingMeshBakerLil?.Dispose();
        }
        
        private void Update()
        {
            if (_dissolving)
            {
                UpdateBuffer();
            }
        }

        #region Private

        /// <summary>
        /// バッファのアップデートとGraphicsBufferへのセット
        /// </summary>
        private void UpdateBuffer()
        {
            dissolveSamplingMeshBakerLil.UpdateBuffer(_dissolveEffectMeshData.Values.Cast<DissolveEffectMeshData>().Select(v => v.dissolveMeshData).ToArray());

            if (visualEffect.HasGraphicsBuffer("MeshSamplingBuffer"))
            {
                visualEffect.SetGraphicsBuffer("MeshSamplingBuffer", dissolveSamplingMeshBakerLil.MeshSamplingBuffer);
            }
            if (visualEffect.HasGraphicsBuffer(dissolveSamplingBufferProperty))
            {
                visualEffect.SetGraphicsBuffer(dissolveSamplingBufferProperty, dissolveSamplingMeshBakerLil.DissolveBorderSamplingBuffer);
            }
        }

        #endregion

        #region Public

        // == キャラクターのメッシュ全てに対して処理する ==
        
        public void DissolveAppear(float duration)
        {
            foreach (var data in from DictionaryEntry dissolveEffectMeshData in _dissolveEffectMeshData select (DissolveEffectMeshData) dissolveEffectMeshData.Value)
            {
                DissolveAppearInternal(data, duration);
            }
        }
        
        public void DissolveAppear()
        {
            DissolveAppear(dissolveDuration);
        }

        public void DissolveDisappear(float duration)
        {
            foreach (var data in from DictionaryEntry dissolveEffectMeshData in _dissolveEffectMeshData select (DissolveEffectMeshData) dissolveEffectMeshData.Value)
            {
                DissolveDisappearInternal(data, duration);
            }
        }
        
        public void DissolveDisappear()
        {
            DissolveDisappear(dissolveDuration);
        }
        
        public void DissolveToggle()
        {
            DissolveFromBool(!IsAppear());
        }
        
        public void DissolveToggle(float duration)
        {
            DissolveFromBool(!IsAppear(), duration);
        }

        public void InstantAppear(bool forceMode = false)
        {
            foreach (var data in from DictionaryEntry dissolveEffectMeshData in _dissolveEffectMeshData select (DissolveEffectMeshData)dissolveEffectMeshData.Value)
            {
                InstantAppearInternal(data, forceMode);
            }
        }
        
        public void InstantDisappear(bool forceMode = false)
        {
            foreach (var data in from DictionaryEntry dissolveEffectMeshData in _dissolveEffectMeshData select (DissolveEffectMeshData)dissolveEffectMeshData.Value)
            {
                InstantDisappearInternal(data, forceMode);
            }
        }
        
        public void InstantToggle()
        {
            InstantFromBool(!IsAppear());
        }
        
        // == 文字列で指定したメッシュ全てに対して処理する ==
        
        public void DissolveAppearMesh(string meshNamesStr, float duration)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    DissolveAppearInternal(data, duration);
                }
            }
        }
        
        public void DissolveAppearMesh(string meshNamesStr)
        {
            DissolveAppearMesh(meshNamesStr, dissolveDuration);
        }
        
        public void DissolveDisappearMesh(string meshNamesStr, float duration)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    DissolveDisappearInternal(data, duration);
                }
            }
        }
        
        public void DissolveDisappearMesh(string meshNamesStr)
        {
            DissolveDisappearMesh(meshNamesStr, dissolveDuration);
        }
        
        public void DissolveToggleMesh(string meshNamesStr, float duration)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    DissolveMeshFromBool(!data.isAppear, meshName, duration);
                }
            }
        }
        
        public void DissolveToggleMesh(string meshNamesStr)
        {
            DissolveToggleMesh(meshNamesStr, dissolveDuration);
        }
        
        public void InstantAppearMesh(string meshNamesStr, bool forceMode = false)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    InstantAppearInternal(data, forceMode);
                }
            }
        }
        
        public void InstantDisappearMesh(string meshNamesStr, bool forceMode = false)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    InstantDisappearInternal(data, forceMode);
                }
            }
        }
        
        public void InstantToggleMesh(string meshNamesStr)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    InstantMeshFromBool(!data.isAppear, meshName);
                }
            }
        }
        
        // == コンポーネントで指定したメッシュ全てに対して処理する ==
        
        public void DissolveAppearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, float duration, bool forceMode = false)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    DissolveAppearInternal(data, duration);
                }
            }
        }
        
        public void DissolveAppearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, bool forceMode = false)
        {
            DissolveAppearMesh(skinnedMeshRenderers, dissolveDuration, forceMode);
        }
        
        public void DissolveDisappearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, float duration)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    DissolveDisappearInternal(data, duration);
                }
            }
        }
        
        public void DissolveDisappearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            DissolveDisappearMesh(skinnedMeshRenderers, dissolveDuration);
        }
        
        public void DissolveToggleMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, float duration)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    DissolveMeshFromBool(!data.isAppear, skinnedMeshRenderer.name);
                }
            }
        }
        
        public void DissolveToggleMesh(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            DissolveToggleMesh(skinnedMeshRenderers, dissolveDuration);
        }
        
        public void InstantAppearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, bool forceMode = false)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    InstantAppearInternal(data, forceMode);
                }
            }
        }
        
        public void InstantDisappearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, bool forceMode = false)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    InstantDisappearInternal(data, forceMode);
                }
            }
        }
        
        public void InstantToggleMesh(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    InstantMeshFromBool(!data.isAppear, skinnedMeshRenderer.name);
                }
            }
        }

        /// <summary>
        /// ディゾルブをブールで制御する
        /// </summary>
        /// <param name="appear">tureで現れ、falseで消える</param>
        /// <param name="duration"></param>
        /// <param name="forceMode"></param>
        public void DissolveFromBool(bool appear, float duration)
        {
            if (appear)
            {
                DissolveAppear(duration);
            }
            else
            {
                DissolveDisappear(duration);
            }
        }
        
        public void DissolveFromBool(bool appear)
        {
            DissolveFromBool(appear, dissolveDuration);
        }

        public void InstantFromBool(bool appear, bool forceMode = false)
        {
            if (appear)
            {
                InstantAppear(forceMode);
            }
            else
            {
                InstantDisappear(forceMode);
            }
        }
        
        public void DissolveMeshFromBool(bool appear, string meshNamesStr, float duration)
        {
            if (appear)
            {
                DissolveAppearMesh(meshNamesStr, duration);
            }
            else
            {
                DissolveDisappearMesh(meshNamesStr, duration);
            }
        }
        
        public void DissolveMeshFromBool(bool appear, string meshNamesStr)
        {
            DissolveMeshFromBool(appear, meshNamesStr, dissolveDuration);
        }
        
        public void InstantMeshFromBool(bool appear, string meshNamesStr, bool forceMode = false)
        {
            if (appear)
            {
                InstantAppearMesh(meshNamesStr, forceMode);
            }
            else
            {
                InstantDisappearMesh(meshNamesStr, forceMode);
            }
        }

        #endregion

        #region Private

        private SkinnedMeshRenderer[] GetSkinnedMeshesFromCharacter(GameObject characterGameObject)
        {
            return characterGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        private bool DissolveAppearInternal(DissolveEffectMeshData data, float duration)
        {
            data.skinnedMeshRenderer.enabled = true;
            data.isAppear = false;
            
            visualEffect.SendEvent(dissolveStartEventName);

            data.DissolvePosition = appearStartPos;
            
            if (data.appearMotion.IsActive())
            {
                data.appearMotion.Cancel();
            }
            if(data.disappearMotion.IsActive())
            {
                data.disappearMotion.Cancel();
            }

            data.appearMotion = LMotion.Create(data.DissolvePosition, appearEndPos, duration)
                .WithEase(dissolveEase)
                .WithOnComplete(() =>
                {
                    visualEffect.SendEvent(stopEventName);
                    _dissolving = false;
                    data.dissolveMeshData.isDissolve = 0;

                    data.isAppear = true;
                })
                .WithCancelOnError()
                .Bind(x =>
                {
                    visualEffect.SendEvent(spawnEventName);
                    _dissolving = true;
                    data.dissolveMeshData.isDissolve = 1;

                    data.DissolvePosition = x;
                })
                .AddTo(this);

            return true;
        }

        private bool DissolveDisappearInternal(DissolveEffectMeshData data, float duration)
        {
            data.skinnedMeshRenderer.enabled = true;
            data.isAppear = true;
            
            visualEffect.SendEvent(dissolveStartEventName);

            data.DissolvePosition = disappearStartPos;

            if (data.appearMotion.IsActive())
            {
                data.appearMotion.Cancel();
            }
            if(data.disappearMotion.IsActive())
            {
                data.disappearMotion.Cancel();
            }
            
            data.disappearMotion = LMotion.Create(data.DissolvePosition, disappearEndPos, duration)
                .WithEase(dissolveEase)
                .WithOnComplete(() =>
                {
                    visualEffect.SendEvent(stopEventName);
                    _dissolving = false;
                    data.dissolveMeshData.isDissolve = 0;
                    
                    data.skinnedMeshRenderer.enabled = false;
                    data.isAppear = false;
                })
                .WithCancelOnError()
                .Bind(x =>
                {
                    visualEffect.SendEvent(spawnEventName);
                    _dissolving = true;
                    data.dissolveMeshData.isDissolve = 1;
                    
                    data.DissolvePosition = x;
                })
                .AddTo(this);

            return true;
        }
        
        private bool InstantAppearInternal(DissolveEffectMeshData data, bool forceMode)
        {
            if(data.isAppear && !forceMode) return false;
            
            if (data.appearMotion.IsActive())
            {
                data.appearMotion.Cancel();
            }
            if(data.disappearMotion.IsActive())
            {
                data.disappearMotion.Cancel();
            }

            data.skinnedMeshRenderer.enabled = true;
            data.isAppear = true;

            data.DissolvePosition = appearEndPos;

            return true;
        }
        
        private bool InstantDisappearInternal(DissolveEffectMeshData data, bool forceMode)
        {
            if(!data.isAppear && !forceMode) return false;
            
            if (data.appearMotion.IsActive())
            {
                data.appearMotion.Cancel();
            }
            if(data.disappearMotion.IsActive())
            {
                data.disappearMotion.Cancel();
            }

            data.skinnedMeshRenderer.enabled = false;
            data.isAppear = false;

            data.DissolvePosition = disappearEndPos;

            return true;
        }

        private string[] SplitMeshNames(string meshNameStr)
        {
            return meshNameStr.Split(',');
        }

        /// <summary>
        /// キャラクターのメッシュの半数以上が表示されているかどうか
        /// </summary>
        private bool IsAppear()
        {
            return _dissolveEffectMeshData.Values.Cast<DissolveEffectMeshData>().Count(v => v.isAppear) > _dissolveEffectMeshData.Values.Count / 2;
        }

        #endregion

        #region ContexMenu

        [ContextMenu("DissolveAppear")]
        private void DissolveFromBoolTrue()
        {
            DissolveFromBool(true);
        }

        [ContextMenu("DissolveDisappear")]
        private void DissolveFromBoolFalse()
        {
            DissolveFromBool(false);
        }
        
        [ContextMenu("InstantAppear")]
        private void InstantFromBoolTrue()
        {
            InstantFromBool(true);
        }
        
        [ContextMenu("InstantDisappear")]
        private void InstantFromBoolFalse()
        {
            InstantFromBool(false);
        }
        
        [ContextMenu("InstantToggle")]
        private void InstantToggleContext()
        {
            InstantToggle();
        }
        
        [ContextMenu("DissolveToggle")]
        private void DissolveToggleContext()
        {
            DissolveToggle();
        }

        #endregion
    }
}
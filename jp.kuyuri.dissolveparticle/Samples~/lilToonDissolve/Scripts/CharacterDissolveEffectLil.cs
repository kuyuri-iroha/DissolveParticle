using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.VFX;

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
            
            public Vector3 DissolvePosition
            {
                get => dissolveMeshData.dissolvePosition;
                set => SetDissolvePosition(value);
            }
        
            public Tween appearTween;
            public Tween disappearTween;
        
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
                dissolveMeshData.dissolvePosition = dissolvePos;
                skinnedMeshRenderer.GetPropertyBlock(materialPropertyBlock);
                materialPropertyBlock.SetVector(DissolvePos, dissolveMeshData.dissolvePosition);
                materialPropertyBlock.SetVector(DissolveParam, new Vector4(3.0f, 0, dissolveMeshData.dissolveRange, dissolveMeshData.dissolveBlur));
                skinnedMeshRenderer.SetPropertyBlock(materialPropertyBlock);
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
        
        public void DissolveAppear(bool forceMode = false)
        {
            foreach (var data in from DictionaryEntry dissolveEffectMeshData in _dissolveEffectMeshData select (DissolveEffectMeshData) dissolveEffectMeshData.Value)
            {
                DissolveAppearInternal(data, forceMode);
            }
        }

        public void DissolveDisappear(bool forceMode = false)
        {
            foreach (var data in from DictionaryEntry dissolveEffectMeshData in _dissolveEffectMeshData select (DissolveEffectMeshData) dissolveEffectMeshData.Value)
            {
                DissolveDisappearInternal(data, forceMode);
            }
        }
        
        public void DissolveToggle()
        {
            DissolveFromBool(!IsAppear());
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
        
        public void DissolveAppearMesh(string meshNamesStr, bool forceMode = false)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    DissolveAppearInternal(data, forceMode);
                }
            }
        }
        
        public void DissolveDisappearMesh(string meshNamesStr, bool forceMode = false)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    DissolveDisappearInternal(data, forceMode);
                }
            }
        }
        
        public void DissolveToggleMesh(string meshNamesStr)
        {
            var meshNames = SplitMeshNames(meshNamesStr);
            foreach (var meshName in meshNames)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[meshName];
                if (data != null)
                {
                    DissolveMeshFromBool(!data.isAppear, meshName);
                }
            }
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
        
        public void DissolveAppearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, bool forceMode = false)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    DissolveAppearInternal(data, forceMode);
                }
            }
        }
        
        public void DissolveDisappearMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, bool forceMode = false)
        {
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var data = (DissolveEffectMeshData)_dissolveEffectMeshData[skinnedMeshRenderer.name];
                if (data != null)
                {
                    DissolveDisappearInternal(data, forceMode);
                }
            }
        }
        
        public void DissolveToggleMesh(SkinnedMeshRenderer[] skinnedMeshRenderers, string meshNamesStr)
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
        /// <param name="forceMode"></param>
        public void DissolveFromBool(bool appear, bool forceMode = false)
        {
            if (appear)
            {
                DissolveAppear(forceMode);
            }
            else
            {
                DissolveDisappear(forceMode);
            }
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
        
        public void DissolveMeshFromBool(bool appear, string meshNamesStr, bool forceMode = false)
        {
            if (appear)
            {
                DissolveAppearMesh(meshNamesStr, forceMode);
            }
            else
            {
                DissolveDisappearMesh(meshNamesStr, forceMode);
            }
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

        private bool DissolveAppearInternal(DissolveEffectMeshData data, bool forceMode)
        {
            if(data.isAppear && !forceMode) return false;
            
            data.appearTween?.Kill();
            data.disappearTween?.Kill();

            data.skinnedMeshRenderer.enabled = true;
            data.isAppear = false;

            data.DissolvePosition = appearStartPos;
            data.appearTween = DOTween.To(
                    () => data.DissolvePosition,
                    (x) => data.DissolvePosition = x,
                    appearEndPos,
                    dissolveDuration
                )
                .SetEase(dissolveEase)
                .OnUpdate(() =>
                {
                    visualEffect.SendEvent(spawnEventName);
                    _dissolving = true;
                    data.dissolveMeshData.isDissolve = 1;
                })
                .OnComplete(() =>
                {
                    visualEffect.SendEvent(stopEventName);
                    _dissolving = false;
                    data.dissolveMeshData.isDissolve = 0;
                    
                    data.isAppear = true;
                });

            return true;
        }

        private bool DissolveDisappearInternal(DissolveEffectMeshData data, bool forceMode)
        {
            if(!data.isAppear && !forceMode) return false;
            
            data.appearTween?.Kill();
            data.disappearTween?.Kill();

            data.skinnedMeshRenderer.enabled = true;
            data.isAppear = true;

            data.DissolvePosition = disappearStartPos;
            data.appearTween = DOTween.To(
                    () => data.DissolvePosition,
                    (x) => data.DissolvePosition = x,
                    disappearEndPos,
                    dissolveDuration
                )
                .SetEase(dissolveEase)
                .OnUpdate(() =>
                {
                    visualEffect.SendEvent(spawnEventName);
                    _dissolving = true;
                    data.dissolveMeshData.isDissolve = 1;
                })
                .OnComplete(() =>
                {
                    visualEffect.SendEvent(stopEventName);
                    _dissolving = false;
                    data.dissolveMeshData.isDissolve = 0;
                    
                    data.skinnedMeshRenderer.enabled = false;
                    data.isAppear = false;
                });

            return true;
        }
        
        private bool InstantAppearInternal(DissolveEffectMeshData data, bool forceMode)
        {
            if(data.isAppear && !forceMode) return false;
            
            data.appearTween?.Kill();
            data.disappearTween?.Kill();

            data.skinnedMeshRenderer.enabled = true;
            data.isAppear = true;

            data.DissolvePosition = appearEndPos;

            return true;
        }
        
        private bool InstantDisappearInternal(DissolveEffectMeshData data, bool forceMode)
        {
            if(!data.isAppear && !forceMode) return false;
            
            data.appearTween?.Kill();
            data.disappearTween?.Kill();

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
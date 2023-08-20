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
        private MaterialPropertyBlock _materialPropertyBlock;
        private Vector3 _dissolvePosition;

        private Vector3 DissolvePosition
        {
            get => _dissolvePosition;
            set => SetDissolvePosition(value);
        }

        private Tween _appearTween;
        private Tween _disappearTween;
        
        private bool _initialized = false;

        private static readonly int DissolvePos = Shader.PropertyToID("_DissolvePos");
        private static readonly int DissolveParam = Shader.PropertyToID("_DissolveParams");

        private void OnEnable()
        {
            if(characterSceneObject == null)
            {
                characterSceneObject = transform.parent.gameObject;
            }
            if(visualEffect == null)
            {
                visualEffect = GetComponent<VisualEffect>();
            }
            
            // Mesh baker
            dissolveSamplingMeshBakerLil = new DissolveSamplingMeshBakerLil();

            if (!visualEffect.HasGraphicsBuffer(dissolveSamplingBufferProperty))
            {
                Debug.LogError($"{dissolveSamplingBufferProperty} not found in {visualEffect.name}.");
            }

            dissolveSamplingMeshBakerLil.SetVertexCountNoValidation(pointCount);
            dissolveSamplingMeshBakerLil.SetSkinnedMeshesNoValidation(GetSkinnedMeshesFromCharacter(characterSceneObject));
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

            _materialPropertyBlock = new MaterialPropertyBlock();
            DissolvePosition = startDissolvePosition;
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
            if(_initialized == false)
            {
                UpdateBuffer();
                _initialized = true;
            }
        }

        #region Private

        /// <summary>
        /// バッファのアップデートとGraphicsBufferへのセット
        /// </summary>
        private void UpdateBuffer()
        {
            dissolveSamplingMeshBakerLil.UpdateBuffer(_dissolvePosition, dissolveRange, dissolveBlur);

            if (visualEffect.HasGraphicsBuffer("MeshSamplingBuffer"))
            {
                visualEffect.SetGraphicsBuffer("MeshSamplingBuffer", dissolveSamplingMeshBakerLil.MeshSamplingBuffer);
            }
            if (visualEffect.HasGraphicsBuffer(dissolveSamplingBufferProperty))
            {
                visualEffect.SetGraphicsBuffer(dissolveSamplingBufferProperty, dissolveSamplingMeshBakerLil.DissolveBorderSamplingBuffer);
            }
        }

        /// <summary>
        /// ディゾルブ位置の変更とともに、マテリアルの変更を行う
        /// </summary>
        /// <param name="dissolvePos">ディゾルブ位置</param>
        private void SetDissolvePosition(Vector3 dissolvePos)
        {
            _dissolvePosition = dissolvePos;
            _materialPropertyBlock.SetVector(DissolvePos, dissolvePos);
            _materialPropertyBlock.SetVector(DissolveParam, new Vector4(3.0f, 0, dissolveRange, dissolveBlur));

            for (var i = 0; i <  dissolveSamplingMeshBakerLil.Sources.Length; i++)
            {
                var materialCount = dissolveSamplingMeshBakerLil.SourcesMaterialsLength[i];
                for (var j = 0; j < materialCount; j++)
                {
                    dissolveSamplingMeshBakerLil.Sources[i].SetPropertyBlock(_materialPropertyBlock, j);
                }
            }
        }

        #endregion

        #region Public

        /// <summary>
        /// ディゾルブで現れる
        /// </summary>
        public void DissolveAppear()
        {
            _appearTween?.Kill();
            _disappearTween?.Kill();

            ActivateMeshes(true);

            DissolvePosition = appearStartPos;
            _appearTween = DOTween.To(
                    () => DissolvePosition,
                    (x) => DissolvePosition = x,
                    appearEndPos,
                    dissolveDuration
                )
                .SetEase(dissolveEase)
                .OnUpdate(UpdateBuffer)
                .OnComplete(() =>
                {
                    visualEffect.SendEvent(stopEventName);
                });

            visualEffect.SendEvent(spawnEventName);
        }

        /// <summary>
        /// ディゾルブで消える
        /// </summary>
        public void DissolveDisappear()
        {
            _appearTween?.Kill();
            _disappearTween?.Kill();

            ActivateMeshes(true);
            
            DissolvePosition = disappearStartPos;
            _disappearTween = DOTween.To(
                    () => DissolvePosition,
                    (x) => DissolvePosition = x,
                    disappearEndPos,
                    dissolveDuration
                )
                .SetEase(dissolveEase)
                .OnUpdate(UpdateBuffer)
                .OnComplete(() =>
                {
                    visualEffect.SendEvent(stopEventName);
                    ActivateMeshes(false);
                });

            visualEffect.SendEvent(spawnEventName);
        }

        public void ToggleAppear()
        {
            ActivateMeshes(true);
        }
        
        public void ToggleDisappear()
        {
            ActivateMeshes(false);
        }


        /// <summary>
        /// ディゾルブをブールで制御する
        /// </summary>
        /// <param name="appear">tureで現れ、falseで消える</param>
        public void DissolveFromBool(bool appear)
        {
            if (appear)
            {
                DissolveAppear();
            }
            else
            {
                DissolveDisappear();
            }
        }

        public void ToggleFromBool(bool appear)
        {
            if (appear)
            {
                ToggleAppear();
            }
            else
            {
                ToggleDisappear();
            }
        }

        #endregion

        #region Private

        private SkinnedMeshRenderer[] GetSkinnedMeshesFromCharacter(GameObject characterGameObject)
        {
            return characterGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        private void ActivateMeshes(bool active)
        {
            for (var i = 0; i <  dissolveSamplingMeshBakerLil.Sources.Length; i++)
            {
                dissolveSamplingMeshBakerLil.Sources[i].enabled = active;
            }
        }

        #endregion

        #region ContexMenu

        [ContextMenu("DissolveFromBoolTrue")]
        private void DissolveFromBoolTrue()
        {
            DissolveFromBool(true);
        }

        [ContextMenu("DissolveFromBoolFalse")]
        private void DissolveFromBoolFalse()
        {
            DissolveFromBool(false);
        }
        
        [ContextMenu("ToggleFromBoolTrue")]
        private void ToggleFromBoolTrue()
        {
            ToggleFromBool(true);
        }
        
        [ContextMenu("ToggleFromBoolFalse")]
        private void ToggleFromBoolFalse()
        {
            ToggleFromBool(false);
        }
        
        #endregion
    }
}
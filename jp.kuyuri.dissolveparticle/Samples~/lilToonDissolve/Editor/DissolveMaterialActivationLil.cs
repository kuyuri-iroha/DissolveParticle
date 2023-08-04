using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using lilToon;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Object = System.Object;

namespace Kuyuri
{
    public class DissolveMaterialActivationLil : MonoBehaviour
    {
        private static string Suffix = "dissolve";

        [MenuItem("Assets/ActivateDissolveMaterial/lilToon")]
        public static void LilToon()
        {
            var log = new StringBuilder("Dissolve Activation Result.\n");

            var prefabPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            var prefabRoot = PrefabUtility.InstantiatePrefab(Selection.activeObject) as GameObject;
            if (prefabRoot == null)
            {
                Debug.LogError("Prefab not found. Please select prefab.");
                return;
            }

            try
            {
                var animator = prefabRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("Animator not found.");
                }

                // ディゾルブ用のマテリアルを作成
                var createdMaterials = new Dictionary<string, Material>();
                var skinnedMeshRenderers = prefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                for (var i = 0; i < skinnedMeshRenderers.Length; i++)
                {
                    skinnedMeshRenderers[i].rootBone = hips;
                    
                    var materials = skinnedMeshRenderers[i].sharedMaterials;
                    var dissolveMaterials = new Material[materials.Length];
                    for (var j = 0; j < materials.Length; j++)
                    {
                        var material = materials[j];
                        var materialPath = AssetDatabase.GetAssetPath(material);

                        var dissolveDirectoryPath = $"{Path.GetDirectoryName(materialPath)}/{Suffix}";
                        var dissolveMaterialName = $"{material.name}_{Suffix}";
                        var dissolveMaterialPath = $"{dissolveDirectoryPath}/{dissolveMaterialName}.mat";

                        if (!createdMaterials.ContainsKey(dissolveMaterialPath))
                        {
                            Material dissolveMaterial;
                            dissolveMaterial = new Material(material);

                            dissolveMaterial.name = dissolveMaterialName;
                            if (lilMaterialUtils.CheckShaderIslilToon(material))
                            {
                                // Dissolveに対応したマテリアル設定へ変更
                                CheckShaderType(material, out var lilMaterialMode);

                                if (lilMaterialMode.renderingMode != RenderingMode.Cutout &&
                                    lilMaterialMode.renderingMode != RenderingMode.Transparent)
                                {
                                    if (lilMaterialMode.isMulti)
                                    {
                                        // マルチマテリアルの場合はTransparentModeでRenderingModeを切り替えるため、Cutoutになるよう変更する必要がある
                                        dissolveMaterial.SetFloat("_TransparentMode", 1.0f);
                                    }
                                    
                                    // RenderingModeがOpaqueの場合はThresholdを調整する
                                    if(lilMaterialMode.renderingMode == RenderingMode.Opaque)
                                    {
                                        dissolveMaterial.SetFloat("_Cutoff", 0.001f);
                                    }

                                    var type = typeof(lilMaterialUtils);
                                    var setupMaterialWithRenderingMode = type.GetMethod("SetupMaterialWithRenderingMode",
                                        BindingFlags.Static | BindingFlags.NonPublic);
                                    setupMaterialWithRenderingMode.Invoke(null,
                                        new object[]
                                        {
                                            dissolveMaterial,
                                            RenderingMode.Cutout,
                                            lilMaterialMode.transparentMode,
                                            lilMaterialMode.isOutl,
                                            lilMaterialMode.isLite,
                                            lilMaterialMode.isTess,
                                            lilMaterialMode.isMulti
                                        });

                                    log.AppendLine(
                                        $"Change RenderingMode of {material.name} from {lilMaterialMode.renderingMode} to {RenderingMode.Cutout}");
                                }

                                // Dissolveの有効化
                                dissolveMaterial.SetVector("_DissolveParams", new Vector4(3.0f, 0.0f, 0.0f, 0.0f));
                            }

                            Directory.CreateDirectory(dissolveDirectoryPath);
                            AssetDatabase.CreateAsset(dissolveMaterial, dissolveMaterialPath);
                            
                            createdMaterials.Add(dissolveMaterialPath, dissolveMaterial);
                            
                            dissolveMaterials[j] = dissolveMaterial;
                        }
                        else
                        {
                            dissolveMaterials[j] = AssetDatabase.LoadAssetAtPath<Material>(dissolveMaterialPath);
                        }
                    }

                    skinnedMeshRenderers[i].sharedMaterials = dissolveMaterials;
                }
                
                // ディゾルブエフェクトの追加
                var dissolveEffect = new GameObject("CharacterDissolveEffectLil");
                dissolveEffect.transform.SetParent(prefabRoot.transform);
                dissolveEffect.AddComponent<CharacterDissolveEffectLil>();
                dissolveEffect.AddComponent<VisualEffect>();

                AssetDatabase.SaveAssets();
                
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, CreateAssetPathWithSuffix(prefabPath, Suffix));
                DestroyImmediate(prefabRoot);

                log.AppendLine("Success");
                Debug.Log(log);
            }
            catch (Exception e)
            {
                DestroyImmediate(prefabRoot);
                
                log.AppendLine($"Failed: {e.Message}");
                
                Debug.Log(log);
                throw;
            }
        }

        private static string CreateAssetPathWithSuffix(string assetPath, string suffix)
        {
            return
                $"{Path.GetDirectoryName(assetPath)}/{Path.GetFileNameWithoutExtension(assetPath)}_{suffix}{Path.GetExtension(assetPath)}";
        }

        public struct lilMaterialMode
        {
            public RenderingMode renderingMode;
            public TransparentMode transparentMode;
            public bool isLite;
            public bool isCutout;
            public bool isTransparent;
            public bool isOutl;
            public bool isRefr;
            public bool isBlur;
            public bool isFur;
            public bool isTess;
            public bool isGem;
            public bool isFakeShadow;
            public bool isOnePass;
            public bool isTwoPass;
            public bool isMulti;
            public bool isCustomShader;
            public bool isShowRenderMode;
            public bool isStWr;
            public bool isUseAlpha;
        }

        private static void CheckShaderType(Material material, out lilMaterialMode lilMaterialMode)
        {
            var stencilPassFloatValue = material.GetFloat("_StencilPass");
            Object[] objects = Selection.GetFiltered<Material>(SelectionMode.DeepAssets)
                .Where(obj => obj.shader != null).Where(obj => obj.shader.name.Contains("lilToon")).ToArray();
            var isMultiVariants = objects.Any(obj => ((Material) obj).shader != material.shader);

            var isLite = material.shader.name.Contains("Lite");
            var isCutout = material.shader.name.Contains("Cutout");
            var isTransparent = material.shader.name.Contains("Transparent") ||
                                material.shader.name.Contains("Overlay");
            var isOutl = !isMultiVariants && material.shader.name.Contains("Outline");
            var isRefr = !isMultiVariants && material.shader.name.Contains("Refraction");
            var isBlur = !isMultiVariants && material.shader.name.Contains("Blur");
            var isFur = !isMultiVariants && material.shader.name.Contains("Fur");
            var isTess = !isMultiVariants && material.shader.name.Contains("Tessellation");
            var isGem = !isMultiVariants && material.shader.name.Contains("Gem");
            var isFakeShadow = !isMultiVariants && material.shader.name.Contains("FakeShadow");
            var isOnePass = material.shader.name.Contains("OnePass");
            var isTwoPass = material.shader.name.Contains("TwoPass");
            var isMulti = material.shader.name.Contains("Multi");
            var isCustomShader = material.shader.name.Contains("Optional");
            var isShowRenderMode = !isCustomShader;
            var isStWr = stencilPassFloatValue == (float) StencilOp.Replace;

            var renderingMode = RenderingMode.Opaque;
            if (isCutout) renderingMode = RenderingMode.Cutout;
            if (isTransparent) renderingMode = RenderingMode.Transparent;
            if (isRefr) renderingMode = RenderingMode.Refraction;
            if (isRefr && isBlur) renderingMode = RenderingMode.RefractionBlur;
            if (isFur) renderingMode = RenderingMode.Fur;
            if (isFur && isCutout) renderingMode = RenderingMode.FurCutout;
            if (isFur && isTwoPass) renderingMode = RenderingMode.FurTwoPass;
            if (isGem) renderingMode = RenderingMode.Gem;

            var transparentMode = TransparentMode.Normal;
            if (isOnePass) transparentMode = TransparentMode.OnePass;
            if (!isFur && isTwoPass) transparentMode = TransparentMode.TwoPass;

            float tpmode = 0.0f;
            if (material.HasProperty("_TransparentMode")) tpmode = material.GetFloat("_TransparentMode");

            var isUseAlpha =
                renderingMode == RenderingMode.Cutout ||
                renderingMode == RenderingMode.Transparent ||
                renderingMode == RenderingMode.Fur ||
                renderingMode == RenderingMode.FurCutout ||
                renderingMode == RenderingMode.FurTwoPass ||
                (isMulti && tpmode != 0.0f && tpmode != 3.0f && tpmode != 6.0f);

            if (isMulti)
            {
                isCutout = tpmode == 1.0f || tpmode == 5.0f;
                isTransparent = tpmode == 2.0f;
            }

            lilMaterialMode = new lilMaterialMode()
            {
                renderingMode = renderingMode,
                transparentMode = transparentMode,
                isLite = isLite,
                isCutout = isCutout,
                isTransparent = isTransparent,
                isOutl = isOutl,
                isRefr = isRefr,
                isBlur = isBlur,
                isFur = isFur,
                isTess = isTess,
                isGem = isGem,
                isFakeShadow = isFakeShadow,
                isOnePass = isOnePass,
                isTwoPass = isTwoPass,
                isMulti = isMulti,
                isCustomShader = isCustomShader,
                isShowRenderMode = isShowRenderMode,
                isStWr = isStWr,
                isUseAlpha = isUseAlpha
            };
        }
    }
}
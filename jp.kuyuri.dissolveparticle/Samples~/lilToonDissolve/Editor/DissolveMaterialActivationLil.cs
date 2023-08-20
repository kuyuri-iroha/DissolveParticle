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
                            var dissolveMaterial = new Material(material.shader)
                            {
                                name = dissolveMaterialName
                            };

                            dissolveMaterial.CopyPropertiesFromMaterial(material);

                            if (lilMaterialUtils.CheckShaderIslilToon(material))
                            {
                                // Dissolveに対応したマテリアル設定へ変更
                                CheckShaderType(material, out var lilMaterialMode);

                                if (!(lilMaterialMode.isTransparent || lilMaterialMode.isCutout))
                                {
                                    if (material.HasProperty("_TransparentMode"))
                                    {
                                        // TransparentModeでRenderingModeを切り替えるためにCutoutになるよう変更する必要がある
                                        dissolveMaterial.SetFloat("_TransparentMode", 1.0f);
                                    }

                                    log.AppendLine(
                                        $"Change RenderingMode of {material.name} from {lilMaterialMode.renderingMode} to {RenderingMode.Cutout}");
                                }
                                else
                                {
                                    log.AppendLine(
                                        $"Unchanged RenderingMode {lilMaterialMode.renderingMode} of {material.name}");
                                }

                                // Dissolveの有効化
                                dissolveMaterial.SetVector("_DissolveParams", new Vector4(3.0f, 0.0f, 0.0f, 0.0f));
                            }
                            
                            Directory.CreateDirectory(dissolveDirectoryPath);
                            AssetDatabase.CreateAsset(dissolveMaterial, dissolveMaterialPath);
                            EditorUtility.SetDirty(dissolveMaterial);
                            AssetDatabase.SaveAssets();

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
            Object[] objects = Selection.GetFiltered<Material>(SelectionMode.DeepAssets).Where(obj => obj.shader != null).Where(obj => obj.shader.name.Contains("lilToon")).ToArray();
            var isMultiVariants = objects.Any(obj => ((Material)obj).shader != material.shader);
            
            var isLite          = material.shader.name.Contains("Lite");
            var isCutout        = material.shader.name.Contains("Cutout");
            var isTransparent   = material.shader.name.Contains("Transparent") || material.shader.name.Contains("Overlay");
            var isOutl          = !isMultiVariants && material.shader.name.Contains("Outline");
            var isRefr          = !isMultiVariants && material.shader.name.Contains("Refraction");
            var isBlur          = !isMultiVariants && material.shader.name.Contains("Blur");
            var isFur           = !isMultiVariants && material.shader.name.Contains("Fur");
            var isTess          = !isMultiVariants && material.shader.name.Contains("Tessellation");
            var isGem           = !isMultiVariants && material.shader.name.Contains("Gem");
            var isFakeShadow    = !isMultiVariants && material.shader.name.Contains("FakeShadow");
            var isOnePass       = material.shader.name.Contains("OnePass");
            var isTwoPass       = material.shader.name.Contains("TwoPass");
            var isMulti         = material.shader.name.Contains("Multi");
            var isCustomShader  = material.shader.name.Contains("Optional");
            var isShowRenderMode = !isCustomShader;
            var isStWr          = stencilPassFloatValue == (float)UnityEngine.Rendering.StencilOp.Replace;
            
            float tpmode = 0.0f;
            if(material.HasProperty("_TransparentMode")) tpmode = material.GetFloat("_TransparentMode");
            if(isMulti)
            {
                isCutout = tpmode == 1.0f || tpmode == 5.0f;
                isTransparent = tpmode == 2.0f;
            }

            var                     renderingModeBuf = RenderingMode.Opaque;
            if(isCutout)            renderingModeBuf = RenderingMode.Cutout;
            if(isTransparent)       renderingModeBuf = RenderingMode.Transparent;
            if(isRefr)              renderingModeBuf = RenderingMode.Refraction;
            if(isRefr && isBlur)    renderingModeBuf = RenderingMode.RefractionBlur;
            if(isFur)               renderingModeBuf = RenderingMode.Fur;
            if(isFur && isCutout)   renderingModeBuf = RenderingMode.FurCutout;
            if(isFur && isTwoPass)  renderingModeBuf = RenderingMode.FurTwoPass;
            if(isGem)               renderingModeBuf = RenderingMode.Gem;

            var                     transparentModeBuf = TransparentMode.Normal;
            if(isOnePass)           transparentModeBuf = TransparentMode.OnePass;
            if(!isFur && isTwoPass) transparentModeBuf = TransparentMode.TwoPass;

            var isUseAlpha =
                renderingModeBuf == RenderingMode.Cutout ||
                renderingModeBuf == RenderingMode.Transparent ||
                renderingModeBuf == RenderingMode.Fur ||
                renderingModeBuf == RenderingMode.FurCutout ||
                renderingModeBuf == RenderingMode.FurTwoPass ||
                (isMulti && tpmode != 0.0f && tpmode != 3.0f && tpmode != 6.0f);

            lilMaterialMode = new lilMaterialMode()
            {
                renderingMode = renderingModeBuf,
                transparentMode = transparentModeBuf,
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

        //[MenuItem("Assets/ActivateDissolveMaterial/lilToonMultiMaterialSetup")]
        public static void lilToonMultiMaterialSetup()
        {
            var material = Selection.activeObject as Material;
            
            CheckShaderType(material, out var lilMaterialMode);
            
            var type = typeof(lilMaterialUtils);
            var setupMaterialWithRenderingMode = type.GetMethod("SetupMaterialWithRenderingMode",
                BindingFlags.Static | BindingFlags.NonPublic);
            
            setupMaterialWithRenderingMode.Invoke(null,
                new object[]
                {
                    material,
                    RenderingMode.Cutout,
                    lilMaterialMode.transparentMode,
                    lilMaterialMode.isOutl,
                    lilMaterialMode.isLite,
                    lilMaterialMode.isTess,
                    lilMaterialMode.isMulti
                }
            );
        }
        
    }
}
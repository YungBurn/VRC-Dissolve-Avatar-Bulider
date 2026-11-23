using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBPcssLighting
    {
        private const string PcssRoot = "Assets/nHaruka/PCSS4VRC";
        private const string ShaderFolder = PcssRoot + "/PCSS4lilToon/Shaders";
        private const string PcssShaderPrefix = "YBTools/PCSS4VRC/";
        private const string BaseShaderPrefix = "YBTools/";
        private const string LilIncludes = "Packages/jp.lilxyzw.liltoon/Shader/Includes/";
        private const string ConfigTypeName = "nHaruka.PCSS4VRC.PCSS4VRC_NDMF";

        private const string OutputDirectory = YBPathUtility.GeneratedRoot + "/PCSSShaders";

        private readonly Component configuration;
        private readonly Renderer[] excludedRenderers;
        private readonly Dictionary<Shader, Shader> shaders = new Dictionary<Shader, Shader>();

        internal YBPcssLighting(GameObject avatarRoot)
        {
            configuration = avatarRoot.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(component =>
                component != null && component.GetType().FullName == ConfigTypeName &&
                Convert.ToString(component.GetType().GetField("currentShaderType")?.GetValue(component)) == "lilToon");
            excludedRenderers = configuration != null
                ? configuration.GetType().GetField("excludeObj")?.GetValue(configuration) as Renderer[]
                : null;
        }

        internal bool AppliesTo(Renderer renderer, Material source, Shader target)
        {
            return target != null && AssetDatabase.GetAssetPath(target).EndsWith(".shader", StringComparison.OrdinalIgnoreCase) &&
                   (IsPcssMaterial(source) || (configuration != null &&
                    (excludedRenderers == null || !excludedRenderers.Contains(renderer))));
        }

        internal static string UpgradeInstalledMaterials(GameObject avatarRoot, YBAvatarInstallRecord installRecord, string outputRoot)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "PCSS 更新失败：请先退出 Play Mode。";
            if (avatarRoot == null || installRecord == null)
                return "PCSS 更新失败：找不到当前 Avatar 的安装记录。";

            var pcss = new YBPcssLighting(avatarRoot);
            var updates = new Dictionary<Material, Material>();
            var activeMaterials = new HashSet<Material>();
            int unresolvedRenderers = 0;
            int alreadyUpgraded = 0;
            int unsupported = 0;
            try
            {
                foreach (YBInstalledRendererRecord binding in installRecord.renderers)
                {
                    Renderer renderer = YBAvatarInstaller.FindInstalledRenderer(avatarRoot, binding);
                    if (renderer == null)
                    {
                        unresolvedRenderers++;
                        continue;
                    }

                    Material[] assigned = renderer.sharedMaterials;
                    for (int i = 0; i < binding.installedMaterials.Count; i++)
                    {
                        Material material = binding.installedMaterials[i];
                        if (material == null || material.shader == null || !assigned.Contains(material)) continue;
                        string path = AssetDatabase.GetAssetPath(material);
                        if (!path.StartsWith(outputRoot.TrimEnd('/') + "/Materials/Dissolve/", StringComparison.Ordinal))
                            continue;

                        if (material.shader.name.StartsWith(PcssShaderPrefix, StringComparison.Ordinal))
                        {
                            if (activeMaterials.Add(material)) alreadyUpgraded++;
                            continue;
                        }

                        Material original = i < binding.originalMaterials.Count ? binding.originalMaterials[i] : null;
                        if (!pcss.AppliesTo(renderer, original, material.shader))
                        {
                            if (AssetDatabase.GetAssetPath(material.shader).EndsWith(".lilcontainer", StringComparison.OrdinalIgnoreCase) &&
                                activeMaterials.Add(material)) unsupported++;
                            continue;
                        }

                        if (updates.ContainsKey(material)) continue;
                        Shader shader = pcss.GetShader(material.shader);
                        var replacement = new Material(material) { name = material.name };
                        updates.Add(material, replacement);
                        int queue = material.renderQueue;
                        replacement.shader = shader;
                        replacement.renderQueue = queue;
                        pcss.ConfigureMaterial(replacement, original != null ? original : material);
                    }
                }

                if (updates.Count > 0)
                {
                    Undo.RecordObjects(updates.Keys.Cast<UnityEngine.Object>().ToArray(), "Update dissolve PCSS materials");
                    foreach (var update in updates)
                    {
                        EditorUtility.CopySerialized(update.Value, update.Key);
                        EditorUtility.SetDirty(update.Key);
                    }
                    AssetDatabase.SaveAssets();
                }

                string result = updates.Count + alreadyUpgraded > 0
                    ? $"PCSS 材质已更新：{updates.Count} 个。已使用 PCSS 的材质：{alreadyUpgraded} 个。"
                    : "PCSS 更新未找到可转换的衣服材质。请确认当前 Avatar 使用 PCSS4VRC lilToon，且 Renderer 仍引用已安装的 YB 材质。";
                if (unsupported > 0) result += $" 特殊渲染模式：{unsupported} 个。";
                if (unresolvedRenderers > 0) result += $" 找不到 Renderer：{unresolvedRenderers} 个。";
                return result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "PCSS 更新失败：" + e.Message;
            }
            finally
            {
                foreach (Material temporary in updates.Values)
                    UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        internal Shader GetShader(Shader original)
        {
            if (shaders.TryGetValue(original, out Shader shader)) return shader;

            string sourcePath = AssetDatabase.GetAssetPath(original);
            string passSourcePath = sourcePath.Substring(0, sourcePath.Length - ".shader".Length) + "Pass.shader";
            string properties = File.ReadAllText(ShaderFolder + "/lilCustomShaderProperties.lilblock", Encoding.UTF8);
            RequireFile(ShaderFolder + "/custom.hlsl");
            RequireFile(ShaderFolder + "/custom_insert.hlsl");

            string passText = AddPcssProperties(File.ReadAllText(passSourcePath, Encoding.UTF8), properties);
            passText = AddPcssForwardLighting(passText);
            string materialText = AddPcssProperties(File.ReadAllText(sourcePath, Encoding.UTF8), properties);

            if (!AssetDatabase.IsValidFolder(OutputDirectory))
                AssetDatabase.CreateFolder(YBPathUtility.GeneratedRoot, "PCSSShaders");
            WriteShader(OutputDirectory + "/" + Path.GetFileName(passSourcePath), passText);
            shader = WriteShader(OutputDirectory + "/" + Path.GetFileName(sourcePath), materialText);
            shaders.Add(original, shader);
            return shader;
        }

        internal void ConfigureMaterial(Material destination, Material source)
        {
            destination.SetFloat("_BlendOpFA", (float)BlendOp.Add);
            destination.SetFloat("_AlphaBoostFA", 1f);
            SetDefaultTexture(destination, source, "_IgnoreCookieTexture", PcssRoot + "/Soft.png");
            SetDefaultTexture(destination, source, "_EnvLightLevelTexture", PcssRoot + "/EnvLightLevelSensor/EnvLightColor.asset");
            if (destination.GetFloat("_StencilRef") == 0 || destination.GetFloat("_StencilPass") == 0)
            {
                destination.SetFloat("_StencilRef", 125f);
                destination.SetFloat("_StencilComp", (float)CompareFunction.Always);
                destination.SetFloat("_StencilPass", (float)StencilOp.Replace);
            }

            if (configuration != null && destination.renderQueue >= 2500 &&
                configuration.GetType().GetField("forceRQu2500")?.GetValue(configuration) is true)
                destination.renderQueue = 2499;
        }

        private static bool IsPcssMaterial(Material material)
        {
            if (material == null || material.shader == null) return false;
            string name = material.shader.name;
            return name.StartsWith(PcssShaderPrefix, StringComparison.Ordinal) ||
                   name.IndexOf("PCSS4VRC/PCSS4lilToon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SetDefaultTexture(Material target, Material source, string property, string path)
        {
            Texture texture = source.HasProperty(property) ? source.GetTexture(property) : null;
            if (texture == null) texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (texture == null) throw new FileNotFoundException("缺少 PCSS4VRC 贴图：" + path);
            target.SetTexture(property, texture);
        }

        private static string AddPcssProperties(string source, string properties)
        {
            const string propertiesHeader = @"\bProperties\s*\{";
            if (!Regex.IsMatch(source, propertiesHeader))
                throw new InvalidOperationException("FinalBaked Shader 缺少 Properties。");
            return Regex.Replace(source.Replace(BaseShaderPrefix, PcssShaderPrefix), propertiesHeader,
                match => match.Value + "\n" + properties + "\n");
        }

        internal static string AddPcssForwardLighting(string source)
        {
            const string forwardInclude = "#include \"" + LilIncludes + "lil_pass_forward.hlsl\"";
            const string pipelineInclude = "#include \"" + LilIncludes + "lil_pipeline_brp.hlsl\"";
            const string outlineDefine = "#define LIL_OUTLINE";
            const string pcssInclude = "#include \"" + ShaderFolder + "/custom.hlsl\"";
            const string pcssInsert = "#include \"" + ShaderFolder + "/custom_insert.hlsl\"";
            int patchedPasses = 0;
            string result = Regex.Replace(source, @"HLSLPROGRAM[\s\S]*?ENDHLSL", match =>
            {
                string program = match.Value;
                if (!program.Contains(forwardInclude)) return program;

                if (!program.Contains(pipelineInclude) || !program.Contains("input.ybBakedAvatarY"))
                    throw new InvalidOperationException("FinalBaked Forward Pass 结构已改变，无法接入 PCSS。");
                bool outline = program.Contains(outlineDefine);
                if (outline) program = program.Replace(outlineDefine, string.Empty);

                // lilToon's forward passes already pack UV4.x into uv23.z. Leave the custom
                // vertex hooks available for PCSS's spot-light varyings.
                program = Regex.Replace(program, @"(?m)^[ \t]*#define LIL_CUSTOM_(V2F_MEMBER|VERT_COPY)[^\r\n]*\r?\n", string.Empty);
                patchedPasses++;
                return program.Replace("input.ybBakedAvatarY", "input.uv23.z")
                    .Replace(pipelineInclude, (outline ? outlineDefine + "\n" : string.Empty) + pcssInclude + "\n" + pipelineInclude)
                    .Replace(forwardInclude, pcssInsert + "\n" + forwardInclude);
            });
            if (patchedPasses == 0)
                throw new InvalidOperationException("FinalBaked Shader 缺少可接入 PCSS 的 Forward Pass。");
            return result;
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("缺少 PCSS4lilToon Shader 文件：" + path);
        }

        private static Shader WriteShader(string path, string text)
        {
            if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != text)
            {
                File.WriteAllText(path, text, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("PCSS 溶解 Shader 编译失败，请查看 Unity Console：" + path);
            return shader;
        }
    }
}

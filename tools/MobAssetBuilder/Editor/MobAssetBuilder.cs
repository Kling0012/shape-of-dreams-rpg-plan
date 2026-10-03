// Copied into an isolated, exact-version Unity project by prepare_project.py.
// No game binaries, imported templates or generated AssetBundles belong in this repository.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Dreamforge.MobAssets
{
    [Serializable] public sealed class BuildConfig
    {
        public int schemaVersion;
        public string unityVersion, urpVersion, gameVersion, buildTarget, contentVersion;
        public string renderPipelineAsset, renderPipelineSha256;
        public AssemblyLock[] sdkAssemblies;
        public PackageLock[] additionalPackages;
        public ModelBinding[] models;
    }
    [Serializable] public sealed class PackageLock { public string name, version; }
    [Serializable] public sealed class AssemblyLock { public string file, sha256; }
    [Serializable] public sealed class ModelBinding
    {
        public string id, templatePrefab, templateSha256, replaceChildPath;
        public string[] baseMonsterTypes;
        public ClipBinding[] clipBindings;
        public ObjectBinding[] objectBindings;
    }
    [Serializable] public sealed class ClipBinding
    {
        public string semantic, componentPath, componentType, propertyPath;
    }
    [Serializable] public sealed class ObjectBinding { public string propertyPath, targetPath; }
    [Serializable] public sealed class SourceManifest
    {
        public int schemaVersion;
        public string packId;
        public SourceModel[] models;
        public SourceFile[] files;
    }
    [Serializable] public sealed class SourceFile { public string path, sha256; public long bytes; }
    [Serializable] public sealed class SourceModel
    {
        public string modelId, fbx;
        public int triangles, bones, meshCount, materialCount;
        public TextureSources textures;
        public SourceClip[] clips;
    }
    [Serializable] public sealed class TextureSources
    { public string baseColor, metallicRoughness, metallicSmoothness, emission; }
    [Serializable] public sealed class SourceClip
    { public string name, takeName; public float frameStart, frameEnd, fps, durationSeconds; public bool loop; }
    [Serializable] public sealed class RuntimeManifest
    {
        public int schemaVersion = 1;
        public string packId, contentVersion, unityVersion, gameVersion, buildTarget, bundleFile, bundleSha256;
        public RuntimeModel[] models;
    }
    [Serializable] public sealed class RuntimeModel { public string id, prefab; public string[] baseMonsterTypes; }

    public static class MobAssetBuilder
    {
        const string PackId = "dreamborne_enemies_vol01";
        const string Root = "Assets/MobAssetBuilder";
        const string Source = Root + "/Source/";
        const string Generated = Root + "/Generated";
        static readonly string[] Ids = { "ember_warden", "mirecap_stomper", "duskwing_oracle", "thorncrown_stag",
            "frostjaw_prowler", "runestone_tortoise", "glasswing_moth", "obsidian_scorpion", "lantern_wisp", "abyssal_bell" };
        static readonly string[] Semantics = { "Idle", "Walk", "Attack" };
        static readonly HashSet<string> ProducedAssets = new HashSet<string>(StringComparer.Ordinal);

        // Unity -batchmode -projectPath PROJECT -buildTarget CLI_TARGET (Win64/Linux64/OSXUniversal)
        //       -executeMethod Dreamforge.MobAssets.MobAssetBuilder.Build -mobOutput OUTPUT -logFile LOG
        public static void Build()
        {
            try { BuildChecked(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        // Import-only inspection is useful while the actual game's Attack binding is unresolved.
        // This entry point never emits a runtime manifest, prefab or AssetBundle.
        public static void ImportSources()
        {
            try
            {
                var config = Read<BuildConfig>("mob-build.json");
                var source = Read<SourceManifest>(Source + "manifest.json");
                ValidateEnvironment(config, source, false);
                PrepareGeneratedFolders();
                foreach (var model in source.models)
                {
                    VerifySourceFile(source, model.fbx);
                    ConfigureModel(model);
                    CopyClips(model);
                    MakeMaterial(source, model);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("Imported ten source rigs, thirty clips and palette materials. No game-ready model pack was built.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void PrepareGeneratedFolders()
        {
            foreach (var folder in new[] { "Prefabs", "Materials", "Textures", "Clips" })
                Directory.CreateDirectory(Generated + "/" + folder);
            AssetDatabase.Refresh();
        }

        static void BuildChecked()
        {
            var config = Read<BuildConfig>("mob-build.json");
            var source = Read<SourceManifest>(Source + "manifest.json");
            ValidateEnvironment(config, source);
            var output = Path.GetFullPath(Argument("-mobOutput"));
            Require(!output.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(p => p.Equals("Mods", StringComparison.OrdinalIgnoreCase)), "Never build into a live Mods directory");
            Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(),
                "Output must be empty; old manifests must never survive a failed build");
            ProducedAssets.Clear();
            PrepareGeneratedFolders();
            var prefabs = new List<string>();
            var runtimeModels = new List<RuntimeModel>();
            foreach (var id in Ids)
            {
                var model = source.models.Single(x => x.modelId == id);
                var binding = config.models.Single(x => x.id == id);
                VerifySourceFile(source, model.fbx);
                ConfigureModel(model);
                var clips = CopyClips(model);
                var material = MakeMaterial(source, model);
                string prefabPath = BuildPrefab(model, binding, clips, material);
                prefabs.Add(prefabPath);
                runtimeModels.Add(new RuntimeModel { id = id, prefab = prefabPath.ToLowerInvariant(),
                    baseMonsterTypes = binding.baseMonsterTypes });
            }
            AssetDatabase.SaveAssets();
            ValidateDependencyClosure(config, source, prefabs);
            // Explicit asset list: no SDK assemblies, source Blend/GLB or Editor scripts are included.
            var bundleFile = PackId + "_" + config.buildTarget.ToLowerInvariant() + ".bundle";
            Directory.CreateDirectory(output);
            var bundle = new AssetBundleBuild { assetBundleName = bundleFile, assetNames = prefabs.ToArray(),
                addressableNames = prefabs.Select(x => x.ToLowerInvariant()).ToArray() };
            var built = BuildPipeline.BuildAssetBundles(output, new[] { bundle },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                (BuildTarget)Enum.Parse(typeof(BuildTarget), config.buildTarget));
            Require(built != null && File.Exists(Path.Combine(output, bundleFile)), "AssetBundle build failed");
            Require(built.GetAllDependencies(bundleFile).Length == 0, "Unexpected external AssetBundle dependency");
            // Verify actual serialized bundle content and event safety before publishing its manifest.
            VerifyBuiltBundle(Path.Combine(output, bundleFile), runtimeModels);
            var manifest = new RuntimeManifest { packId = PackId, contentVersion = config.contentVersion,
                unityVersion = config.unityVersion, gameVersion = config.gameVersion,
                buildTarget = config.buildTarget, bundleFile = bundleFile,
                bundleSha256 = Hash(Path.Combine(output, bundleFile)), models = runtimeModels.ToArray() };
            var bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(manifest, true) + "\n");
            File.WriteAllBytes(Path.Combine(output, "manifest.json"), bytes);
            Debug.Log("Built and reloaded 10 model prefabs for " + config.buildTarget + ". Manifest SHA256: " +
                Hash(Path.Combine(output, "manifest.json")) + ". In-game animation/multiplayer testing is still required.");
        }

        // AssetBundle roots are not a dependency allowlist: Unity embeds referenced content implicitly.
        // Only assets produced in this invocation, exact hashed source inputs and official package
        // dependencies may carry content. SDK MonoScripts carry runtime type identity, not DLL bytes.
        static void ValidateDependencyClosure(BuildConfig config, SourceManifest source, List<string> prefabs)
        {
            var allowed = new HashSet<string>(ProducedAssets, StringComparer.Ordinal);
            foreach (var model in source.models)
            {
                var paths = new[] { model.fbx, model.textures.baseColor, model.textures.emission,
                    model.textures.metallicRoughness, model.textures.metallicSmoothness };
                foreach (var path in paths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    VerifySourceFile(source, path);
                    allowed.Add(Source + path);
                }
            }
            var packages = AllowedUnityPackages(config);
            var sdk = new HashSet<string>(config.sdkAssemblies.Select(a => "Assets/ThirdParty/GameSDK/" + a.file), StringComparer.Ordinal);
            var scriptReferences = new HashSet<string>(StringComparer.Ordinal);
            // Examine every object reference, including otherwise allowed Renderer/Animator fields.
            // A DLL used as TextAsset or a user field is not an external script type reference.
            foreach (var prefab in prefabs)
            foreach (var component in AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponentsInChildren<Component>(true))
            {
                Require(component != null, "Missing script before dependency verification: " + prefab);
                var properties = new SerializedObject(component).GetIterator();
                while (properties.Next(true))
                {
                    if (properties.propertyType != SerializedPropertyType.ObjectReference || properties.objectReferenceValue == null) continue;
                    string path = AssetDatabase.GetAssetPath(properties.objectReferenceValue);
                    if (!sdk.Contains(path)) continue;
                    Require(properties.propertyPath == "m_Script" && properties.objectReferenceValue is MonoScript,
                        "SDK content is not an external script reference: " + prefab + "/" + properties.propertyPath);
                    scriptReferences.Add(path);
                }
            }
            foreach (var dependency in AssetDatabase.GetDependencies(prefabs.ToArray(), true))
            {
                if (allowed.Contains(dependency)) continue;
                if (dependency == "Resources/unity_builtin_extra" || dependency == "Library/unity default resources") continue;
                if (sdk.Contains(dependency) && scriptReferences.Contains(dependency))
                {
                    Require(AssetImporter.GetAtPath(dependency) is PluginImporter, "SDK dependency is not an imported code plugin: " + dependency);
                    continue;
                }
                if (dependency.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(dependency);
                    if (package != null && packages.Contains(package.name)) continue;
                }
                throw new InvalidOperationException("Unapproved bundle content dependency: " + dependency +
                    ". Clear an optional reference or rebind it to an original hashed source/generated asset in your local template; " +
                    "do not remove required behavior, move game content into Generated, or expand the allowlist to game assets.");
            }
        }

        static HashSet<string> AllowedUnityPackages(BuildConfig config)
        {
            var roots = new List<PackageLock>(config.additionalPackages ?? new PackageLock[0]);
            roots.Add(new PackageLock { name = "com.unity.render-pipelines.universal", version = config.urpVersion });
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + root.name);
                Require(package != null && package.version == root.version, "Unity package lock mismatch: " + root.name);
                RequireOfficialUnityPackage(package);
                allowed.Add(package.name);
                foreach (var dependency in package.resolvedDependencies)
                {
                    var resolved = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + dependency.name);
                    Require(resolved != null && resolved.version == dependency.version, "Resolved Unity package version mismatch: " + dependency.name);
                    RequireOfficialUnityPackage(resolved);
                    allowed.Add(resolved.name);
                }
            }
            return allowed;
        }

        static void RequireOfficialUnityPackage(UnityEditor.PackageManager.PackageInfo package)
        {
            Require(package.name.StartsWith("com.unity.", StringComparison.Ordinal), "Non-Unity package content is not approved: " + package.name);
            if (package.source == UnityEditor.PackageManager.PackageSource.BuiltIn) return;
            Require(package.source == UnityEditor.PackageManager.PackageSource.Registry && package.registry != null &&
                package.registry.url.TrimEnd('/') == "https://packages.unity.com",
                "Package content must resolve from Unity's official registry, not a local/embedded/git replacement: " + package.name);
        }

        static bool ExactGameVersion(string value)
        {
            return value != null && value.Length <= 128 &&
                System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z") &&
                !new[] { "unknown", "todo", "replace_me", "latest" }.Contains(value.ToLowerInvariant());
        }

        static void ValidateEnvironment(BuildConfig config, SourceManifest source, bool requireBindings = true)
        {
            Require(config != null && config.schemaVersion == 1, "Build config schema must be 1");
            Require(Application.unityVersion == config.unityVersion, "Unity version must match exactly");
            Require(System.Text.RegularExpressions.Regex.IsMatch(config.unityVersion ?? "", @"^\d+\.\d+\.\d+[abfp]\d+$"), "Exact Unity version required");
            Require(ExactGameVersion(config.gameVersion), "Exact bounded game version required; no placeholders, wildcards or paths");
            Require(config.contentVersion == "1", "contentVersion must be the JSON string 1");
            Require(new[] { "StandaloneWindows64", "StandaloneLinux64", "StandaloneOSX" }.Contains(config.buildTarget), "Unsupported platform");
            Require(EditorUserBuildSettings.activeBuildTarget.ToString() == config.buildTarget,
                "Run Unity with -buildTarget matching mob-build.json; no implicit platform switch");
            var urp = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.universal");
            Require(urp != null && urp.version == config.urpVersion, "Installed URP package must match exact lock");
            foreach (var package in config.additionalPackages ?? new PackageLock[0])
            {
                var installed = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + package.name);
                Require(installed != null && installed.version == package.version, "Additional Unity package does not match exact lock: " + package.name);
            }
            Require(config.renderPipelineAsset != null && config.renderPipelineAsset.StartsWith("Assets/LocalTemplates/", StringComparison.Ordinal) &&
                !config.renderPipelineAsset.Contains("..") && Hash(config.renderPipelineAsset) == config.renderPipelineSha256,
                "Exact-game URP pipeline asset/hash is required");
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(config.renderPipelineAsset);
            Require(pipeline != null && pipeline.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset",
                "Supplied rendering asset is not URP");
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            Require(config.sdkAssemblies != null && config.sdkAssemblies.Any(a => a.file == "Dew.Core.dll"), "Dew.Core.dll SDK lock missing");
            foreach (var assembly in config.sdkAssemblies)
            {
                Require(System.Text.RegularExpressions.Regex.IsMatch(assembly.file ?? "", @"\A[A-Za-z0-9_.-]+\.dll\z"), "Unsafe SDK filename");
                Require(config.sdkAssemblies.Count(a => a.file == assembly.file) == 1, "Duplicate SDK filename");
                Require(Hash("Assets/ThirdParty/GameSDK/" + assembly.file) == assembly.sha256, "SDK DLL hash mismatch: " + assembly.file);
            }
            Require(source != null && source.schemaVersion == 1 && source.packId == PackId, "Wrong source pack");
            Require(source.models != null && source.models.Length == 10 && new HashSet<string>(source.models.Select(m => m.modelId)).SetEquals(Ids), "Ten source models required");
            Require(config.models != null && config.models.Length == 10 && new HashSet<string>(config.models.Select(m => m.id)).SetEquals(Ids), "Ten explicit model bindings required");
            if (!requireBindings) return;
            foreach (var binding in config.models)
            {
                Require(binding.baseMonsterTypes != null && binding.baseMonsterTypes.Length > 0 &&
                    binding.baseMonsterTypes.Distinct().Count() == binding.baseMonsterTypes.Length, "Missing/duplicate curated Monster type: " + binding.id);
                foreach (string monster in binding.baseMonsterTypes)
                {
                    Require(!string.IsNullOrEmpty(monster), "Empty Monster type");
                    var type = FindGameType(monster);
                    var baseType = FindGameType("Monster");
                    Require(type != null && baseType != null && baseType.IsAssignableFrom(type) && type != baseType && !type.IsAbstract,
                        "Unknown/nonconcrete Monster type; no guessed bindings: " + monster);
                }
                Require(binding.templatePrefab != null && binding.templatePrefab.StartsWith("Assets/LocalTemplates/", StringComparison.Ordinal) &&
                    binding.templatePrefab.EndsWith(".prefab", StringComparison.Ordinal) && !binding.templatePrefab.Contains(".."), "Template must be a local SDK prefab");
                Require(Hash(binding.templatePrefab) == binding.templateSha256, "Template prefab hash mismatch: " + binding.id);
                Require(!string.IsNullOrEmpty(binding.replaceChildPath) && !binding.replaceChildPath.Split('/').Contains(".."), "An explicit replaceable child branch is required");
                Require(binding.clipBindings != null && new HashSet<string>(binding.clipBindings.Select(b => b.semantic)).SetEquals(Semantics), "Explicit Idle/Walk/Attack bindings required");
                Require(binding.clipBindings.Select(b => b.propertyPath).Distinct().Count() == binding.clipBindings.Length, "Duplicate clip property binding");
            }
        }

        static void ConfigureModel(SourceModel model)
        {
            var path = Source + model.fbx;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Require(importer != null, "FBX was not imported: " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.optimizeGameObjects = false; // Required anchors/bones must remain addressable.
            importer.isReadable = true; // Validate vertices, bone weights and animation bounds.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            var takes = importer.defaultClipAnimations;
            Require(model.clips != null && model.clips.Length == 3 && new HashSet<string>(model.clips.Select(c => c.name)).SetEquals(Semantics), "Source must contain the three real clips");
            var imports = new List<ModelImporterClipAnimation>();
            foreach (var clip in model.clips)
            {
                // Use the actual FBX take name recorded by the structural source validator.
                var matches = takes.Where(t => t.takeName == clip.takeName || t.name == clip.takeName).ToArray();
                Require(matches.Length == 1, "Missing/ambiguous actual FBX take " + model.modelId + "/" + clip.name);
                var take = matches[0];
                Require(Math.Abs((take.lastFrame - take.firstFrame) / clip.fps - clip.durationSeconds) < 0.08f, "FBX take duration differs from source manifest");
                take.name = clip.name;
                take.loopTime = clip.loop;
                take.loopPose = clip.loop;
                take.lockRootRotation = true;
                take.lockRootHeightY = true;
                take.lockRootPositionXZ = true;
                take.keepOriginalOrientation = true;
                take.keepOriginalPositionY = true;
                take.keepOriginalPositionXZ = true;
                take.events = new AnimationEvent[0];
                imports.Add(take);
            }
            importer.clipAnimations = imports.ToArray();
            importer.SaveAndReimport();
        }

        static Dictionary<string, AnimationClip> CopyClips(SourceModel model)
        {
            var imported = AssetDatabase.LoadAllAssetsAtPath(Source + model.fbx).OfType<AnimationClip>().ToArray();
            var result = new Dictionary<string, AnimationClip>();
            foreach (var semantic in Semantics)
            {
                var candidates = imported.Where(c => c.name == semantic).ToArray();
                Require(candidates.Length == 1, "Imported semantic clip is missing or ambiguous: " + semantic);
                var clip = Object.Instantiate(candidates[0]);
                clip.name = model.modelId + "_" + semantic;
                Require(clip.length > 0 && AnimationUtility.GetCurveBindings(clip).Length > 0, "Clip has no actual animation curves");
                AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                var path = Generated + "/Clips/" + clip.name + ".anim";
                ReplaceAsset(clip, path);
                result[semantic] = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            return result;
        }

        static Material MakeMaterial(SourceManifest source, SourceModel model)
        {
            var baseColor = ImportTexture(source, model.textures.baseColor, true, false);
            var emission = ImportTexture(source, model.textures.emission, true, false);
            Texture2D metal;
            if (!string.IsNullOrEmpty(model.textures.metallicSmoothness))
                metal = ImportTexture(source, model.textures.metallicSmoothness, false, false);
            else
            {
                var mr = ImportTexture(source, model.textures.metallicRoughness, false, true);
                var pixels = mr.GetPixels32();
                // glTF MR: B=metallic, G=roughness. URP metallic map: R=metallic, A=smoothness.
                for (int i = 0; i < pixels.Length; ++i) pixels[i] = new Color32(pixels[i].b, 0, 0, (byte)(255 - pixels[i].g));
                var converted = new Texture2D(mr.width, mr.height, TextureFormat.RGBA32, false, true);
                converted.SetPixels32(pixels);
                converted.Apply();
                var output = Generated + "/Textures/" + model.modelId + "_MetallicSmoothness.png";
                File.WriteAllBytes(output, converted.EncodeToPNG());
                ProducedAssets.Add(output);
                Object.DestroyImmediate(converted);
                AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
                ConfigureTexture(output, false, false);
                metal = AssetDatabase.LoadAssetAtPath<Texture2D>(output);
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null && shader.isSupported, "Exact URP/Lit shader unavailable on this build editor");
            var material = new Material(shader) { name = model.modelId + "_Palette" };
            Require(material.HasProperty("_BaseMap") && material.HasProperty("_MetallicGlossMap") && material.HasProperty("_EmissionMap"), "URP material property contract changed");
            material.SetTexture("_BaseMap", baseColor);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", metal);
            material.SetFloat("_Metallic", 1);
            material.SetFloat("_Smoothness", 1);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
            var path = Generated + "/Materials/" + model.modelId + ".mat";
            ReplaceAsset(material, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        static Texture2D ImportTexture(SourceManifest source, string relative, bool srgb, bool readable)
        {
            VerifySourceFile(source, relative);
            ConfigureTexture(Source + relative, srgb, readable);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Source + relative);
            Require(texture != null && texture.width > 0 && texture.height > 0, "Missing palette texture: " + relative);
            return texture;
        }
        static void ConfigureTexture(string path, bool srgb, bool readable)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null, "Not a texture: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.isReadable = readable;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point; // Preserve authored palette swatches.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static string BuildPrefab(SourceModel model, ModelBinding binding,
            Dictionary<string, AnimationClip> clips, Material material)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(binding.templatePrefab);
            Require(template != null, "Missing exact-game template: " + binding.templatePrefab);
            var root = Object.Instantiate(template);
            root.name = model.modelId;
            try
            {
                var entityType = FindGameType("EntityModel");
                Require(entityType != null && entityType.Assembly.GetName().Name == "Dew.Core", "Verified Dew.Core EntityModel unavailable");
                var entity = root.GetComponent(entityType);
                Require(entity != null, "Template needs an EntityModel on its root");
                RequireUninitialized(entity);
                var replace = root.transform.Find(binding.replaceChildPath);
                Require(replace != null && replace != root.transform, "Replace branch missing; root replacement is forbidden");
                var serialized = new SerializedObject(entity);
                // Never silently lose a health-bar, weapon or custom-mapping anchor under the replaced mesh.
                var rebound = new HashSet<string>((binding.objectBindings ?? new ObjectBinding[0]).Select(x => x.propertyPath));
                var iterator = serialized.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var referenced = iterator.objectReferenceValue;
                    var transform = referenced is GameObject ? ((GameObject)referenced).transform :
                        referenced is Component ? ((Component)referenced).transform : null;
                    if (transform != null && (transform == replace || transform.IsChildOf(replace)))
                        Require(iterator.propertyPath.StartsWith("bodyRenderers.Array.data[", StringComparison.Ordinal) || rebound.Contains(iterator.propertyPath),
                            "Template anchor would be destroyed; add exact objectBindings entry: " + iterator.propertyPath);
                }
                var parent = replace.parent;
                string branchName = replace.name;
                Vector3 position = replace.localPosition, scale = replace.localScale;
                Quaternion rotation = replace.localRotation;
                Object.DestroyImmediate(replace.gameObject);
                var visual = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source + model.fbx), parent);
                visual.name = branchName;
                visual.transform.localPosition = position;
                visual.transform.localRotation = rotation;
                visual.transform.localScale = scale;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    var skin = renderer as SkinnedMeshRenderer;
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                    Require(mesh != null && mesh.subMeshCount == model.materialCount, "Source submesh/material count differs from manifest");
                    renderer.sharedMaterials = Enumerable.Repeat(material, mesh.subMeshCount).ToArray();
                }
                var animator = visual.GetComponent<Animator>();
                Require(animator != null && animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman,
                    "A valid Generic Avatar and Animator must exist at imported FBX root");
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = null; // Game EntityAnimation owns clip playback.
                serialized.Update();
                var locomotion = serialized.FindProperty("locomotion");
                Require(locomotion != null && locomotion.propertyType == SerializedPropertyType.Enum &&
                    locomotion.enumNames[locomotion.enumValueIndex] == "Simple",
                    "This pack has forward Walk only; use an SDK template with verified Simple locomotion");
                var idleSpeed = serialized.FindProperty("idle.speed");
                Require(idleSpeed != null && idleSpeed.propertyType == SerializedPropertyType.Float && Finite(idleSpeed.floatValue) && idleSpeed.floatValue > 0,
                    "Exact template idle playback speed must be positive");
                var bodies = serialized.FindProperty("bodyRenderers");
                Require(bodies != null && bodies.isArray, "Verified EntityModel.bodyRenderers is unavailable");
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                bodies.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; ++i) bodies.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                foreach (var clipBinding in binding.clipBindings)
                {
                    Require(clipBinding.componentType == "EntityModel" && string.IsNullOrEmpty(clipBinding.componentPath), "Only verified root EntityModel clip slots are supported");
                    if (clipBinding.semantic == "Idle") Require(clipBinding.propertyPath == "idle.clip", "Idle must bind verified idle.clip");
                    if (clipBinding.semantic == "Walk") Require(clipBinding.propertyPath == "runForwardClip", "Walk requires verified runForwardClip for Simple locomotion");
                    if (clipBinding.semantic == "Attack") Require(!new[] { "idle.clip", "stagger.clip", "death.clip" }.Contains(clipBinding.propertyPath) && !clipBinding.propertyPath.StartsWith("run", StringComparison.Ordinal),
                        "An Attack binding cannot masquerade as idle/locomotion/stagger/death; exact SDK attack slot remains required");
                    SetObject(serialized, clipBinding.propertyPath, clips[clipBinding.semantic]);
                }
                foreach (var objectBinding in binding.objectBindings ?? new ObjectBinding[0])
                {
                    var target = root.transform.Find(objectBinding.targetPath);
                    Require(target != null, "Explicit anchor target missing: " + objectBinding.targetPath);
                    var property = serialized.FindProperty(objectBinding.propertyPath);
                    Require(property != null && property.propertyType == SerializedPropertyType.ObjectReference, "Unknown anchor property: " + objectBinding.propertyPath);
                    // Serialized type is verified before assignment; no guessed GameObject/Transform conversion.
                    if (property.type.Contains("GameObject")) property.objectReferenceValue = target.gameObject;
                    else if (property.type.Contains("Transform")) property.objectReferenceValue = target;
                    else throw new InvalidOperationException("Anchor slot is not GameObject/Transform: " + objectBinding.propertyPath);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                ValidatePrefab(root, model, clips.Values.ToArray());
                ValidateAnimationBounds(visual, clips.Values.ToArray());
                var path = Generated + "/Prefabs/" + model.modelId + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                Require(saved != null, "Saving prefab failed");
                ProducedAssets.Add(path);
                return path;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void ValidatePrefab(GameObject root, SourceModel source, AnimationClip[] expectedClips)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                Require(component != null, "Prefab has missing scripts");
                var type = component.GetType();
                bool allowed = component is Transform || component is Animator || component is SkinnedMeshRenderer ||
                    component is MeshRenderer || component is MeshFilter || (type.FullName == "EntityModel" && type.Assembly.GetName().Name == "Dew.Core");
                Require(allowed, "Model-only prefab contains unsupported component (including physics/network/gameplay): " + type.FullName);
            }
            var entities = components.Where(c => c.GetType().FullName == "EntityModel").ToArray();
            Require(entities.Length == 1 && entities[0].gameObject == root, "Exactly one root EntityModel required");
            RequireUninitialized(entities[0]);
            var animators = root.GetComponentsInChildren<Animator>(true);
            Require(animators.Length == 1 && !animators[0].applyRootMotion && animators[0].runtimeAnimatorController == null &&
                animators[0].avatar != null && animators[0].avatar.isValid && !animators[0].avatar.isHuman,
                "Exactly one valid Generic Animator, with no controller or root motion, required");
            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(skins.Length == source.meshCount && root.GetComponentsInChildren<Renderer>(true).Length == source.meshCount, "Renderer/skinned mesh count differs from source manifest");
            int triangles = 0;
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                Require(mesh != null && mesh.vertexCount > 0 && mesh.boneWeights.Length == mesh.vertexCount, "Skin lacks vertex weights");
                Require(skin.bones.Length > 0 && mesh.bindposes.Length == skin.bones.Length && skin.bones.All(b => b != null && b.IsChildOf(root.transform)), "Invalid skin bones/bindposes");
                Require(skin.rootBone != null && skin.rootBone.IsChildOf(root.transform), "Skin root bone missing/outside model");
                Require(mesh.uv.Length == mesh.vertexCount, "Palette UVs missing");
                Require(mesh.uv.All(v => Finite(v.x) && Finite(v.y) && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1), "Palette UVs outside [0,1]");
                Require(mesh.boneWeights.All(w => w.weight0 + w.weight1 + w.weight2 + w.weight3 > 0.99f &&
                    w.weight0 + w.weight1 + w.weight2 + w.weight3 < 1.01f &&
                    ValidWeight(w.weight0, w.boneIndex0, skin.bones.Length) && ValidWeight(w.weight1, w.boneIndex1, skin.bones.Length) &&
                    ValidWeight(w.weight2, w.boneIndex2, skin.bones.Length) && ValidWeight(w.weight3, w.boneIndex3, skin.bones.Length)),
                    "Unweighted/non-normalized vertex or invalid bone index");
                Require(ValidBounds(skin.localBounds), "Invalid/empty skin bounds");
                triangles += mesh.triangles.Length / 3;
                Require(skin.sharedMaterials.Length == source.materialCount && skin.sharedMaterials.All(m => m != null &&
                    m.shader != null && m.shader.name == "Universal Render Pipeline/Lit" && m.GetTexture("_BaseMap") != null &&
                    m.GetTexture("_EmissionMap") != null && m.GetTexture("_MetallicGlossMap") != null), "Invalid URP palette material");
            }
            Require(triangles == source.triangles, "Triangle count differs from source manifest");
            var serialized = new SerializedObject(entities[0]);
            var healthBar = serialized.FindProperty("healthBarPosition");
            var healthTransform = healthBar == null ? null : healthBar.objectReferenceValue as Transform;
            Require(healthTransform != null && (healthTransform == root.transform || healthTransform.IsChildOf(root.transform)),
                "Verified healthBarPosition anchor must remain in this model");
            var mappings = serialized.FindProperty("customMappings");
            Require(mappings != null && mappings.isArray, "Verified customMappings field unavailable");
            var mappingIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < mappings.arraySize; ++i)
            {
                var mapping = mappings.GetArrayElementAtIndex(i);
                var id = mapping.FindPropertyRelative("id");
                var target = mapping.FindPropertyRelative("target");
                var obj = target == null ? null : target.objectReferenceValue as GameObject;
                Require(id != null && !string.IsNullOrEmpty(id.stringValue) && mappingIds.Add(id.stringValue) && obj != null &&
                    (obj == root || obj.transform.IsChildOf(root.transform)), "Custom mapping key is missing/duplicate or target is missing/outside model");
            }
            var propertyIterator = serialized.GetIterator();
            while (propertyIterator.Next(true))
            {
                if (propertyIterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                var clip = propertyIterator.objectReferenceValue as AnimationClip;
                Require(clip == null || (!clip.legacy && expectedClips.Contains(clip)), "Template references an old/incompatible skeleton clip: " + propertyIterator.propertyPath);
                var reference = propertyIterator.objectReferenceValue;
                if (reference == null || clip != null || propertyIterator.propertyPath == "m_Script") continue;
                var target = reference is GameObject ? ((GameObject)reference).transform :
                    reference is Component ? ((Component)reference).transform : null;
                Require(target != null && (target == root.transform || target.IsChildOf(root.transform)),
                    "EntityModel contains external/unsupported data, FX or gameplay asset: " + propertyIterator.propertyPath);
            }
            foreach (var clip in expectedClips) Require(AnimationUtility.GetAnimationEvents(clip).Length == 0, "Animation events are forbidden");
        }

        static void ValidateAnimationBounds(GameObject visual, AnimationClip[] clips)
        {
            // Sample every authored 30 fps frame and expand conservative local skin bounds.
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = skins.ToDictionary(s => s, s => s.localBounds);
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var clip in clips)
                {
                    foreach (var curve in AnimationUtility.GetCurveBindings(clip))
                        Require(string.IsNullOrEmpty(curve.path) || visual.transform.Find(curve.path) != null,
                            "Animation curve targets a missing bone: " + curve.path);
                    int frames = Mathf.CeilToInt(clip.length * 30);
                    for (int frame = 0; frame <= frames; ++frame)
                    {
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(visual, clip, Mathf.Min(frame / 30f, clip.length));
                        AnimationMode.EndSampling();
                        foreach (var skin in skins)
                        {
                            var baked = new Mesh();
                            try
                            {
                                skin.BakeMesh(baked);
                                baked.RecalculateBounds();
                                Require(ValidBounds(baked.bounds), "Animation produced invalid mesh bounds");
                                var expanded = bounds[skin];
                                expanded.Encapsulate(baked.bounds);
                                bounds[skin] = expanded;
                            }
                            finally { Object.DestroyImmediate(baked); }
                        }
                    }
                }
            }
            finally { AnimationMode.StopAnimationMode(); }
            foreach (var skin in skins)
            {
                var expanded = bounds[skin];
                expanded.Expand(0.1f);
                Require(ValidBounds(expanded) && expanded.size.magnitude < 100, "Unreasonable animated model bounds; review scale/skin");
                skin.localBounds = expanded;
                skin.updateWhenOffscreen = false;
            }
        }

        static void VerifyBuiltBundle(string path, List<RuntimeModel> models)
        {
            var bundle = AssetBundle.LoadFromFile(path);
            Require(bundle != null, "Cannot reload generated bundle on this target editor; build verification incomplete");
            try
            {
                Require(bundle.GetAllAssetNames().All(name => !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
                    "SDK DLL must remain an external runtime code reference, never a bundle asset");
                Require(bundle.LoadAllAssets<TextAsset>().Length == 0, "Unexpected text/binary payload in model-only bundle");
                foreach (var model in models)
                {
                    var prefab = bundle.LoadAsset<GameObject>(model.prefab);
                    Require(prefab != null && prefab.GetComponent(FindGameType("EntityModel")) != null, "Bundle prefab/EntityModel missing: " + model.id);
                    foreach (var component in prefab.GetComponentsInChildren<Component>(true)) Require(component != null, "Serialized bundle prefab has missing script");
                }
                foreach (var clip in bundle.LoadAllAssets<AnimationClip>())
                    Require(AnimationUtility.GetAnimationEvents(clip).Length == 0, "Built bundle contains an animation event");
            }
            finally { bundle.Unload(true); }
        }
        static void RequireUninitialized(Component entity)
        {
            var property = entity.GetType().GetProperty("isInitialized", BindingFlags.Instance | BindingFlags.Public);
            Require(property != null && property.PropertyType == typeof(bool) && !(bool)property.GetValue(entity, null), "EntityModel must be uninitialized");
        }
        static void SetObject(SerializedObject serialized, string path, Object value)
        {
            var property = serialized.FindProperty(path);
            Require(property != null && property.propertyType == SerializedPropertyType.ObjectReference && property.type.Contains("AnimationClip"),
                "Exact SDK AnimationClip binding missing: " + path + ". Do not guess an attack slot.");
            property.objectReferenceValue = value;
        }
        static void VerifySourceFile(SourceManifest source, string path)
        {
            Require(!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) && !path.Contains("..") && !path.Contains("\\"), "Unsafe source path");
            var matches = source.files.Where(f => f.path == path).ToArray();
            Require(matches.Length == 1 && new FileInfo(Source + path).Length == matches[0].bytes && Hash(Source + path) == matches[0].sha256, "Source bytes/hash mismatch: " + path);
        }
        static Type FindGameType(string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("Dew.", StringComparison.Ordinal) || a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType(name, false)).Where(t => t != null).ToArray();
            Require(matches.Length <= 1, "Ambiguous game type: " + name);
            return matches.SingleOrDefault();
        }
        static void ReplaceAsset(Object value, string path)
        {
            // Generated-only paths; preserve source/template assets.
            Require(path.StartsWith(Generated + "/", StringComparison.Ordinal), "Refusing to replace nongenerated asset");
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(value, path);
            ProducedAssets.Add(path);
        }
        static bool ValidWeight(float weight, int bone, int count) { return Finite(weight) && weight >= 0 && (weight == 0 || (bone >= 0 && bone < count)); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool ValidBounds(Bounds b) { return Finite(b.center.x) && Finite(b.center.y) && Finite(b.center.z) && Finite(b.size.x) && Finite(b.size.y) && Finite(b.size.z) && b.size.sqrMagnitude > 0; }
        static T Read<T>(string path) { return JsonUtility.FromJson<T>(File.ReadAllText(path)); }
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        static string Argument(string name) { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); Require(index >= 0 && index + 1 < args.Length, "Missing argument " + name); return args[index + 1]; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>One immutable, local-only pack. No network download and no game resource registration.</summary>
    internal sealed class MobModelAssets : IDisposable
    {
        private AssetBundle _bundle;
        private readonly Dictionary<string, EntityModel> _prefabs = new Dictionary<string, EntityModel>(StringComparer.Ordinal);
        public MobModelManifest Manifest { get; private set; }
        public MobCompatibility Compatibility { get; private set; }
        public string Error { get; private set; }
        public bool Ready => _bundle != null && Error == null;

        public MobModelAssets(string modPath)
        {
            try
            {
                if (string.IsNullOrEmpty(modPath)) throw new InvalidDataException("MOD path is unavailable");
                string dir = Path.Combine(modPath, "models");
                string path = Path.Combine(dir, "manifest.json");
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 1024 * 1024) throw new InvalidDataException("models/manifest.json missing or too large");
                byte[] bytes = File.ReadAllBytes(path);
                Manifest = JsonConvert.DeserializeObject<MobModelManifest>(Encoding.UTF8.GetString(bytes), new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None, MaxDepth = 16, MissingMemberHandling = MissingMemberHandling.Error
                });
                string target = CurrentTarget();
                if (Manifest == null) throw new InvalidDataException("empty manifest");
                if (!Manifest.TryValidate(Application.unityVersion, Application.version, target, out string error))
                    throw new InvalidDataException(error);
                string bundlePath = Path.Combine(dir, Manifest.BundleFile);
                var bundleInfo = new FileInfo(bundlePath);
                if (!bundleInfo.Exists || bundleInfo.Length > 256L * 1024 * 1024) throw new InvalidDataException("model bundle missing or exceeds 256 MiB");
                using (var stream = File.OpenRead(bundlePath))
                    if (!string.Equals(Hash(stream), Manifest.BundleSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("model bundle SHA-256 mismatch");
                _bundle = AssetBundle.LoadFromFile(bundlePath);
                if (_bundle == null) throw new InvalidDataException("Unity rejected the model bundle");
                foreach (var entry in Manifest.Models)
                {
                    var prefab = _bundle.LoadAsset<GameObject>(entry.Prefab);
                    if (prefab == null) throw new InvalidDataException("missing prefab " + entry.Id);
                    var model = prefab.GetComponent<EntityModel>();
                    ValidatePrefab(prefab, model, entry.Id);
                    _prefabs.Add(entry.Id, model);
                }
                // Animation Events in cosmetic clips must never initiate game damage or rewards.
                foreach (var clip in _bundle.LoadAllAssets<AnimationClip>())
                    if (clip.events.Length != 0) throw new InvalidDataException("animation events are forbidden: " + clip.name);
                string modHash;
                using (var stream = File.OpenRead(Path.Combine(modPath, "DreamforgeRPG.dll"))) modHash = Hash(stream);
                using (var stream = new MemoryStream(bytes, false))
                    Compatibility = new MobCompatibility
                    {
                        ProtocolVersion = MobCompatibility.CurrentProtocolVersion,
                        ContentHash = Hash(stream), ModSha256 = modHash, Target = target,
                        UnityVersion = Application.unityVersion, GameVersion = Application.version
                    };
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Dispose();
                Log.Warn("Custom mobs unavailable; original models retained: " + Error);
            }
        }

        private static void ValidatePrefab(GameObject prefab, EntityModel model, string id)
        {
            if (model == null || model.isInitialized) throw new InvalidDataException("not a fresh EntityModel: " + id);
            // Deliberately match the builder's model-only acceptance policy. A blacklist misses
            // arbitrary behaviours, rigid bodies and nested EntityModels that can run on cloning.
            int models = 0;
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidDataException("missing prefab component: " + id);
                var type = component.GetType();
                bool allowed = type == typeof(Transform) || type == typeof(Animator) || type == typeof(MeshRenderer)
                    || type == typeof(SkinnedMeshRenderer) || type == typeof(MeshFilter) || type == typeof(EntityModel);
                if (!allowed) throw new InvalidDataException("model prefab contains unsupported components: " + id);
                if (type == typeof(EntityModel)) models++;
            }
            if (models != 1 || model.gameObject != prefab) throw new InvalidDataException("exactly one root EntityModel required: " + id);
            if (!Local(model.healthBarPosition, prefab)) throw new InvalidDataException("invalid healthBarPosition: " + id);
            if (model.customMappings == null) throw new InvalidDataException("missing customMappings: " + id);
            var mappingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in model.customMappings)
                if (string.IsNullOrEmpty(mapping.id) || !mappingIds.Add(mapping.id) || mapping.target == null
                    || !Local(mapping.target.transform, prefab)) throw new InvalidDataException("invalid custom mapping: " + id);
            var animators = prefab.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].applyRootMotion || animators[0].runtimeAnimatorController != null
                || animators[0].avatar == null || !animators[0].avatar.isValid || animators[0].avatar.isHuman)
                throw new InvalidDataException("one valid Generic Animator without root motion/controller required: " + id);
            // This pack is authored with forward locomotion only. Do not guess directional/ability slots.
            if (model.locomotion != EntityAnimation.LocomotionType.Simple) throw new InvalidDataException("Simple locomotion required: " + id);
            ValidateClip(model.idle.clip, id, true); ValidateSpeed(model.idle, id);
            ValidateClip(model.runForwardClip, id, true);
            ValidateClip(model.stagger.clip, id, false); ValidateSpeed(model.stagger, id);
            ValidateClip(model.death.clip, id, false); ValidateSpeed(model.death, id);
            if (model.bodyRenderers == null || model.bodyRenderers.Length == 0) throw new InvalidDataException("no bodyRenderers: " + id);
            var bodies = new HashSet<Renderer>();
            foreach (var renderer in model.bodyRenderers)
            {
                if (renderer == null || !Local(renderer.transform, prefab) || !bodies.Add(renderer))
                    throw new InvalidDataException("invalid renderer reference: " + id);
                if (renderer.sharedMaterials.Length == 0) throw new InvalidDataException("no renderer material: " + id);
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || !material.shader.isSupported)
                        throw new InvalidDataException("missing or unsupported material/shader: " + id);
                if (renderer is SkinnedMeshRenderer skin)
                {
                    if (skin.sharedMesh == null || skin.sharedMesh.vertexCount == 0 || skin.bones == null || skin.bones.Length == 0
                        || !Local(skin.rootBone, prefab) || !ValidBounds(skin.localBounds))
                        throw new InvalidDataException("invalid skin or bounds: " + id);
                    foreach (var bone in skin.bones)
                        if (!Local(bone, prefab)) throw new InvalidDataException("invalid skin bone: " + id);
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (!(renderer is MeshRenderer) || filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                        throw new InvalidDataException("invalid static mesh: " + id);
                }
            }
            if (bodies.Count != prefab.GetComponentsInChildren<Renderer>(true).Length)
                throw new InvalidDataException("bodyRenderers must include all model renderers: " + id);
        }

        private static bool Local(Transform value, GameObject root) => value != null && value.IsChildOf(root.transform);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidBounds(Bounds bounds) => Finite(bounds.center.x) && Finite(bounds.center.y) && Finite(bounds.center.z)
            && Finite(bounds.size.x) && Finite(bounds.size.y) && Finite(bounds.size.z)
            && bounds.size.x >= 0 && bounds.size.y >= 0 && bounds.size.z >= 0 && bounds.size.sqrMagnitude > 0;
        private static void ValidateClip(AnimationClip clip, string id, bool required)
        {
            if (clip == null)
            {
                if (required) throw new InvalidDataException("required animation missing: " + id);
                return;
            }
            if (clip.legacy || !Finite(clip.length) || clip.length <= 0 || clip.events.Length != 0)
                throw new InvalidDataException("invalid animation or forbidden events: " + id);
        }
        private static void ValidateSpeed(AnimationClipWithSpeed value, string id)
        {
            if (value.clip != null && (!Finite(value.speed) || value.speed <= 0))
                throw new InvalidDataException("invalid animation speed: " + id);
        }

        public EntityModel Get(string id) => Ready && id != null && _prefabs.TryGetValue(id, out var model) ? model : null;
        public MobModelEntry ForMonster(string type)
        {
            if (!Ready) return null;
            foreach (var entry in Manifest.Models)
                foreach (string baseType in entry.BaseMonsterTypes)
                    if (string.Equals(baseType, type, StringComparison.Ordinal)) return entry;
            return null;
        }

        public static string CurrentTarget()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer: return "StandaloneWindows64";
                case RuntimePlatform.LinuxPlayer: return "StandaloneLinux64";
                case RuntimePlatform.OSXPlayer: return "StandaloneOSX";
                default: return "unsupported";
            }
        }

        private static string Hash(Stream stream)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public void Release(bool allInstancesRestored)
        {
            _prefabs.Clear();
            if (_bundle != null) _bundle.Unload(allInstancesRestored);
            _bundle = null;
        }
        public void Dispose() => Release(true);
    }
}

using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        partial void EnableOwnershipSpawnCountHooks(ref bool ready)
        {
            ready = NativePerfOptions.OwnershipSpawnCounts && EnemyOwnershipSpawnCountHooks.Ready;
        }
    }

    // Counts are reused only when both native liveness setters are actually patched.
    internal static class EnemyOwnershipSpawnCountHooks
    {
        private static bool _checked, _ready, _warned;
        internal static bool Ready
        {
            get
            {
                if (_checked) return _ready;
                _checked = true;
                try
                {
                    _ready = Installed(AccessTools.PropertySetter(typeof(Actor), "Network_isActive"), typeof(EnemyOwnershipActiveCount))
                        && Installed(AccessTools.PropertySetter(typeof(EntityStatus), "Network_currentHealth"), typeof(EnemyOwnershipHealthCount));
                    if (!_ready) NativePerfPatch.Warn(ref _warned, "spawn ownership counts", "native liveness hooks unavailable; recount retained");
                }
                catch (Exception ex) { NativePerfPatch.Warn(ref _warned, "spawn ownership counts", ex.Message); }
                return _ready;
            }
        }

        internal static bool ValidateSetter(Type type, string property, string fieldName)
        {
            try
            {
                var setter = AccessTools.PropertySetter(type, property);
                var field = AccessTools.Field(type, fieldName);
                if (setter == null || field == null) throw new MissingMemberException(type.Name + "." + property);
                bool writes = false;
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(setter))
                {
                    if (instruction.opcode == OpCodes.Ldflda && Equals(instruction.operand, field)) writes = true;
                    if (writes && instruction.operand is MethodInfo method && method.Name == "GeneratedSyncVarSetter") return true;
                }
                throw new InvalidOperationException("native liveness setter changed");
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "spawn ownership counts", ex.Message);
                return false;
            }
        }

        private static bool Installed(MethodBase method, Type patch)
        {
            if (method == null) return false;
            var info = Harmony.GetPatchInfo(method);
            if (info == null) return false;
            bool prefix = false, postfix = false;
            foreach (var entry in info.Prefixes)
                if (PatchMethodOwnership.DeclaresPatch(entry, patch)) prefix = true;
            foreach (var entry in info.Postfixes)
                if (PatchMethodOwnership.DeclaresPatch(entry, patch)) postfix = true;
            return prefix && postfix;
        }
    }

    [HarmonyPatch]
    internal static class EnemyOwnershipActiveCount
    {
        private static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(Actor), "Network_isActive");
        private static bool Prepare() => EnemyOwnershipSpawnCountHooks.ValidateSetter(
            typeof(Actor), "Network_isActive", "_isActive");
        private static void Prefix(Actor __instance, out bool __state) => __state = __instance.isActive;
        private static void Postfix(Actor __instance, bool __state)
        {
            if (NetworkServer.active && __state != __instance.isActive && __instance is Entity entity)
                HostAuthority.NativeInstance?.RefreshOwnershipSpawnCount(entity);
        }
    }

    [HarmonyPatch]
    internal static class EnemyOwnershipHealthCount
    {
        private static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(EntityStatus), "Network_currentHealth");
        private static bool Prepare() => EnemyOwnershipSpawnCountHooks.ValidateSetter(
            typeof(EntityStatus), "Network_currentHealth", "_currentHealth");
        private static void Prefix(EntityStatus __instance, out bool __state) => __state = __instance.currentHealth <= 0f;
        private static void Postfix(EntityStatus __instance, bool __state)
        {
            if (NetworkServer.active && __state != (__instance.currentHealth <= 0f))
                HostAuthority.NativeInstance?.RefreshOwnershipSpawnCount(__instance.entity);
        }
    }
}

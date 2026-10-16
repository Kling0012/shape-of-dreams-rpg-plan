using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine;

// 同行の刻印（HostAuthority.CoopKeystones.cs）と撃破報酬の共有（HostAuthority.NativeKillRewards.cs）の
// 境界スタブ。実ゲームの Harmony は IL を織り込まないので、パッチ表へ記録して
// RequireCoopHook / Install の両方が参照できるようにする。
namespace HarmonyLib
{
    internal sealed class HarmonyPatchEntry
    {
        internal readonly MethodInfo PatchMethod;
        internal HarmonyPatchEntry(MethodInfo method) => PatchMethod = method;
    }
    internal sealed class HarmonyPatches
    {
        internal readonly List<HarmonyPatchEntry> Prefixes = new List<HarmonyPatchEntry>();
        internal readonly List<HarmonyPatchEntry> Postfixes = new List<HarmonyPatchEntry>();
        internal readonly List<HarmonyPatchEntry> Finalizers = new List<HarmonyPatchEntry>();
    }
    internal sealed class HarmonyMethod
    {
        internal readonly MethodInfo PatchMethod;
        internal HarmonyMethod(MethodInfo method) => PatchMethod = method;
        internal Priority priority;
    }
    internal sealed class Harmony
    {
        private static readonly Dictionary<MethodBase, HarmonyPatches> Table = new Dictionary<MethodBase, HarmonyPatches>();
        internal Harmony(string id) => Id = id;
        internal string Id { get; }
        internal readonly List<(MethodInfo Target, MethodInfo Patch)> Applied = new List<(MethodInfo, MethodInfo)>();
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var patches = For(target);
            if (prefix != null) { patches.Prefixes.Add(new HarmonyPatchEntry(prefix.PatchMethod)); Applied.Add((target, prefix.PatchMethod)); }
            if (postfix != null) { patches.Postfixes.Add(new HarmonyPatchEntry(postfix.PatchMethod)); Applied.Add((target, postfix.PatchMethod)); }
            if (finalizer != null) { patches.Finalizers.Add(new HarmonyPatchEntry(finalizer.PatchMethod)); Applied.Add((target, finalizer.PatchMethod)); }
        }
        public void Unpatch(MethodInfo target, MethodInfo patch)
        {
            Applied.RemoveAll(pair => pair.Target == target && pair.Patch == patch);
            if (!Table.TryGetValue(target, out var patches)) return;
            patches.Prefixes.RemoveAll(entry => entry.PatchMethod == patch);
            patches.Postfixes.RemoveAll(entry => entry.PatchMethod == patch);
            patches.Finalizers.RemoveAll(entry => entry.PatchMethod == patch);
        }
        public static HarmonyPatches GetPatchInfo(MethodBase method)
            => method != null && Table.TryGetValue(method, out var patches) ? patches : null;
        internal static HarmonyPatches For(MethodBase method)
        {
            if (!Table.TryGetValue(method, out var patches)) Table[method] = patches = new HarmonyPatches();
            return patches;
        }
    }
}

namespace SodRpg.Mod
{
    internal static class DewMath
    {
        public static int RandomRoundToInt(float value) => (int)Math.Round(Math.Max(0f, value), MidpointRounding.AwayFromZero);
    }

    internal sealed class DewCollider
    {
        public float CenterX, CenterY, Radius = 1f;
        public bool OverlapPoint(Vector2 point)
        {
            float dx = point.x - CenterX, dy = point.y - CenterY;
            return dx * dx + dy * dy <= Radius * Radius;
        }
    }
    internal sealed class Ai_QR_InfernalTales : AbilityInstance { public DewCollider range = new DewCollider(); }
    internal sealed class Ai_R_Frostbite_Freeze : Entity { }
    internal sealed class Ai_R_Frostbite_Freeze_Projectile : Entity { }

    internal sealed class AiContext { public Entity targetEnemy; }
    internal sealed class AiBehavior
    {
        public readonly AiContext context = new AiContext();
        public readonly List<Entity> AggroTargets = new List<Entity>();
        public void Aggro(Entity target) { if (target != null) AggroTargets.Add(target); }
    }
    internal partial class Entity { public AiBehavior AI; }

    internal sealed partial class StatBonus { public float armorFlat, abilityHasteFlat; }

    internal class StackedStatusEffect : StatusEffect { public float Network_lastStackDecayTime; public int stack; }
    internal sealed class Se_Elm_Cold : StackedStatusEffect { }

    internal sealed partial class Summon { public bool CanAggroTo(Entity enemy) => enemy != null; }

    internal partial class Actor
    {
        public uint persistentNetId;
        // 本体（NativeKillRewards.Install / NativeCoopKeystoneLifetime）が Actor.OnDisable を探す。
        public void OnDisable() { }
    }

    internal sealed class StatusBonusStats { public float armorPercentage; }
    internal sealed partial class EntityStatus
    {
        public float critChance, armor;
        public bool hasAttackCritical;
        public readonly StatusBonusStats bonusStats = new StatusBonusStats();
        // AddStatBonus/RemoveStatBonus の記録は BossNativeRuntimeApi.cs の実装でAttachedBonusesへ入る。
        public readonly List<StatBonus> AttachedBonuses = new List<StatBonus>();
        public StatusEffect GetStatusEffect(Type type)
            => statusEffects.FirstOrDefault(effect => effect != null && effect.isActive && type.IsInstanceOfType(effect));
    }
    internal partial class StatusEffect { public float? maxDuration; }

    internal partial class SkillTrigger
    {
        public readonly int[] currentCharges = new int[8];
        public int NativeKillEventCalls;
        public EventInfoKill? LastNativeKillEvent;
        public void ActorEventOnKill(EventInfoKill info) { NativeKillEventCalls++; LastNativeKillEvent = info; }
        public bool IsNullOrInactive() => !isActive;
    }
    internal partial class Hero { public readonly SafeAction<EventInfoKill> ActorEvent_OnKill = new SafeAction<EventInfoKill>(); }

    internal sealed class KillTracker
    {
        internal Actor target;
        internal readonly Dictionary<Entity, float> trackingDict = new Dictionary<Entity, float>();
        internal float gracePeriod = 10f;
        internal Action<EventInfoKill> _callback;
        internal bool _isStopped;
        public int Reports;
        public void ReportKill(EventInfoKill info) { Reports++; if (_callback != null && !_isStopped) _callback(info); }
    }
    internal sealed class Gem_E_Metal : Gem
    {
        internal KillTracker _tracker;
        public int Callbacks;
        public EventInfoKill? LastKill;
        public void KillCallback(EventInfoKill info) { Callbacks++; LastKill = info; }
    }
    internal sealed class Gem_E_Predation : Gem
    {
        public int Callbacks;
        public void KillCallback(EventInfoKill info) => Callbacks++;
    }
    internal sealed class Se_Star_Aurena_F_CR_NoHealCooldownReduction : StatusEffect
    {
        public SkillTrigger skill; public float cooldownReductionRatio = 0.2f;
        public void ApplyCooldownReductionByRatio(SkillTrigger target, float ratio)
            => target.currentConfigUnscaledCooldownTime = Math.Max(0f,
                target.currentConfigUnscaledCooldownTime - target.currentConfigUnscaledMaxCooldownTime * ratio);
    }
    internal sealed class Se_Star_Lacerta_F_PS_CooldownReset : StatusEffect
    {
        public SkillTrigger skill;
        public void ResetCooldown(SkillTrigger target) => target.currentConfigUnscaledCooldownTime = 0f;
    }
    internal sealed class Se_Star_Cetus_F_BA_CdReductionOnKill : StatusEffect
    {
        public Hero hero; public float trackingGracePeriod = 10f, cdReductionAmount = 1f; public SkillTrigger skill;
        public int VictimDeaths;
        public void OnVictimDeath(EventInfoKill info) => VictimDeaths++;
        public void ApplyCooldownReduction(SkillTrigger target, float amount)
            => target.currentConfigUnscaledCooldownTime = Math.Max(0f, target.currentConfigUnscaledCooldownTime - amount);
    }

    public partial class DreamforgeMod
    {
        internal readonly Harmony harmony = new Harmony("test.native-kill-rewards");
        // 定義宣言は本体（HostAuthority.cs / DreamforgeMod.cs）にある。テスト境界ではこちらで足す。
        partial void StartNativeKillRewards();
        partial void StopNativeKillRewards();
    }

    internal sealed partial class HostAuthority
    {
        // 定義宣言は本体（HostAuthority.cs）にある。テスト境界ではこちらで足す。
        partial void ConfigureCoopKeystones(Hero hero, Build build);
        partial void ScanCoopKeystones();
        partial void ClearCoopKeystones();
        partial void ForgetCoopKeystones(Hero hero);
        partial void EndCoopKeystonePacket(NativeAttributedDamagePacket.Packet packet);
        internal sealed partial class HeroRuntime
        {
            public readonly Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>> Summons
                = new Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>>();
        }
        internal ActorManager TestActorManager => _am;
        internal void ConfigureCoopKeystonesForTest(Hero hero, Build build) => ConfigureCoopKeystones(hero, build);
        internal void ScanCoopKeystonesForTest() => ScanCoopKeystones();
    }
}

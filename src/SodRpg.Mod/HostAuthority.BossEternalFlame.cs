using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Se_U_EternalFlame_Curse), "OnCreate")]
    internal static class BossEternalFlameCurseCreated
    {
        private static void Postfix(Se_U_EternalFlame_Curse __instance) => HostAuthority.NativeInstance?.CaptureEternalFlameCurse(__instance);
    }
    [HarmonyPatch(typeof(Gem_U_EternalFlame), "OnDealDamage")]
    internal static class BossEternalFlameNativeCurseAdmission
    {
        private static bool Prefix() => HostAuthority.NativeInstance?.EternalFlameGeneratedDamage() != true;
    }
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.SetTimer), new[] { typeof(float), typeof(float) })]
    internal static class BossEternalFlameSetTimer
    {
        private static void Postfix(StatusEffect __instance, float maxDuration, float duration)
        { if (__instance is Se_U_EternalFlame_Curse curse) HostAuthority.NativeInstance?.SetEternalFlameNativeTimer(curse, maxDuration, duration); }
    }
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.ResetTimer))]
    internal static class BossEternalFlameRefreshTimer
    {
        private static void Postfix(StatusEffect __instance)
        { if (__instance is Se_U_EternalFlame_Curse curse) HostAuthority.NativeInstance?.RefreshEternalFlameNativeTimer(curse); }
    }
    [HarmonyPatch(typeof(Gem_U_EternalFlame), nameof(Gem_U_EternalFlame.OnUnequipSkill))]
    internal static class BossEternalFlameUnequipped
    {
        private static void Postfix(Gem_U_EternalFlame __instance) => HostAuthority.NativeInstance?.ClearEternalFlameActor(__instance);
    }
    [HarmonyPatch(typeof(Se_U_EternalFlame_Curse), "EntityEventOnTakeDamage")]
    internal static class BossEternalFlameNativeProc
    {
        internal struct Scope
        {
            internal Se_U_EternalFlame_Curse Curse;
            internal EventInfoDamage Damage;
            internal int Before;
            internal bool Applied;
        }
        internal static Scope Current;
        private static bool Prefix(Se_U_EternalFlame_Curse __instance, EventInfoDamage obj, out Scope __state)
        {
            __state = Current;
            Current = default(Scope);
            if (HostAuthority.NativeInstance?.EternalFlameGeneratedDamage() == true) return false;
            Current = new Scope { Curse = __instance, Damage = obj, Before = __instance.victim.Status.fireStack };
            return true;
        }
        private static void Postfix(Se_U_EternalFlame_Curse __instance)
        {
            var scope = Current;
            if (scope.Curse != null && scope.Curse == __instance && scope.Applied && __instance.victim.Status.fireStack > scope.Before)
                HostAuthority.NativeInstance?.EternalFlameNativeProcSucceeded(__instance, scope.Damage);
        }
        private static void Finalizer(Scope __state) { Current = __state; }
    }
    // A proc-success scope alone is insufficient: immunity/failed status creation must not grant an extra stack.
    [HarmonyPatch(typeof(Actor), nameof(Actor.ApplyElemental))]
    internal static class BossEternalFlameAppliedFire
    {
        private static void Postfix(Actor __instance, ElementalType type, Entity to, int appliedStacks, ElementalStatusEffect __result)
        {
            var scope = BossEternalFlameNativeProc.Current;
            if (scope.Curse != null && __instance == scope.Curse && to == scope.Curse.victim && type == ElementalType.Fire
                && appliedStacks > 0 && __result != null) BossEternalFlameNativeProc.Current.Applied = true;
        }
    }
    [HarmonyPatch(typeof(Gem_U_EternalFlame), "Processor")]
    internal static class BossEternalFlameCritGate
    {
        internal struct Scope
        {
            internal Gem_U_EternalFlame Gem;
            internal Actor Actor;
            internal Entity Target;
            internal bool First;
        }
        internal static Scope Current;
        private static void Prefix(Gem_U_EternalFlame __instance, Actor actor, Entity target, out Scope __state)
        {
            __state = Current;
            Current = new Scope { Gem = __instance, Actor = actor, Target = target,
                First = HostAuthority.NativeInstance?.ClaimEternalFlameMainPacket(__instance, actor, target) == true };
        }
        private static void Finalizer(Scope __state) { Current = __state; }
        private static int NativeGate(int original, Gem_U_EternalFlame gem, Actor actor, Entity target)
            => HostAuthority.NativeInstance?.EternalFlameCritThreshold(original, gem, actor, target, Current) ?? original;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var getter = AccessTools.PropertyGetter(typeof(EntityStatus), nameof(EntityStatus.fireStack));
            int site = -1, matches = 0;
            for (int i = 1; i + 1 < code.Count; i++)
                if (code[i - 1].Calls(getter) && code[i].opcode == OpCodes.Ldc_I4_5
                    && (code[i + 1].opcode == OpCodes.Blt || code[i + 1].opcode == OpCodes.Blt_S)) { site = i; matches++; }
            if (matches != 1) throw new InvalidOperationException("EternalFlame native crit gate must have exactly one fireStack/5/blt IL site.");
            // Leave the real fireStack on the evaluation stack and replace only its comparison threshold.
            code.InsertRange(site + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(BossEternalFlameCritGate),nameof(NativeGate))) });
            return code;
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class EternalFlameCurse
        {
            internal Se_U_EternalFlame_Curse Curse;
            internal Gem_U_EternalFlame Gem;
            internal HeroRuntime Runtime;
            internal float NativeMaximum, AddedMaximum, AddedRemaining;
            internal long Life, GemLife, HeroLife, VictimLife;
        }
        private sealed class EternalFlameOwner
        {
            internal readonly BossNativeSeen MainPackets = new BossNativeSeen();
            internal Entity[] Targets;
            internal long[] TargetLife;
            internal float[] TargetReady;
            internal float StackReady, CritReady;
            internal long CritVisual;
            internal bool CritVisualReady;
            internal float CritVisualUntil;
            internal int Stage;
        }
        private readonly Dictionary<Se_U_EternalFlame_Curse, EternalFlameCurse> _eternalFlameCurses = new Dictionary<Se_U_EternalFlame_Curse, EternalFlameCurse>();
        private readonly Dictionary<HeroRuntime, EternalFlameOwner> _eternalFlameOwners = new Dictionary<HeroRuntime, EternalFlameOwner>();
        private readonly List<Se_U_EternalFlame_Curse> _eternalFlameScratch = new List<Se_U_EternalFlame_Curse>();
        private bool _eternalFlameWritingTimer;
        internal bool EternalFlameGeneratedDamage() => AttributionGeneratedOrigin() != GeneratedOrigin.None;
        private bool EternalFlameEquipped(Gem_U_EternalFlame gem, out HeroRuntime rt)
        {
            rt = null;
            if (!NetworkServer.active || gem == null || !gem.isValid || gem.owner == null || gem.skill == null
                || gem.skill.owner != gem.owner || !_runtimes.TryGetValue(gem.owner, out rt) || !Alive(gem.owner) || gem.owner.isKnockedOut) return false;
            bool slot = false;
            foreach (var installed in gem.owner.Skill.gems) if (installed.Value == gem) { slot = true; break; }
            if (!slot) return false;
            foreach (var location in LinkSkills) if (gem.owner.Skill.GetSkill(location) == gem.skill) return true;
            return false;
        }
        private BossRewardAction EternalFlameAction(int stage, int index)
            => BossProfiles.TryGetReward(BossProfiles.InfernusRewardId, out var profile) ? profile.Stages[stage - 1].Actions[index] : null;
        private EternalFlameOwner EternalFlameState(HeroRuntime rt, int stage)
        {
            if (!_eternalFlameOwners.TryGetValue(rt, out var state))
            {
                var stack = EternalFlameAction(2, 1);
                state = new EternalFlameOwner { Targets = new Entity[stack.Count], TargetLife = new long[stack.Count], TargetReady = new float[stack.Count], Stage = stage };
                _eternalFlameOwners.Add(rt, state);
            }
            return state;
        }
        internal void CaptureEternalFlameCurse(Se_U_EternalFlame_Curse curse)
        {
            if (_eternalFlameWritingTimer || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || curse == null || !curse.isActive || !(curse.parentActor is Gem_U_EternalFlame gem)
                || curse.info.caster != gem.owner || curse.victim == null || !EternalFlameEquipped(gem, out var rt)
                || !curse.maxDuration.HasValue || !curse.remainingDuration.HasValue) return;
            if (!_eternalFlameCurses.TryGetValue(curse, out var capture) || !EternalFlameCurseCurrent(capture) || capture.Gem != gem)
            {
                capture = new EternalFlameCurse { Curse = curse, Gem = gem, Runtime = rt, NativeMaximum = curse.maxDuration.Value,
                    Life = BossNativeActorLife(curse), GemLife = BossNativeActorLife(gem), HeroLife = BossNativeActorLife(rt.Hero), VictimLife = BossNativeActorLife(curse.victim) };
                _eternalFlameCurses[curse] = capture;
            }
            int stage = BossRewardStage(rt, BossProfiles.InfernusRewardId);
            if (stage > 0 && capture.AddedMaximum == 0) ExtendEternalFlameTimer(capture, curse.maxDuration.Value, curse.remainingDuration.Value, stage);
        }
        internal void SetEternalFlameNativeTimer(Se_U_EternalFlame_Curse curse, float maximum, float remaining)
        {
            if (_eternalFlameWritingTimer) return;
            CaptureEternalFlameCurse(curse);
            if (!_eternalFlameCurses.TryGetValue(curse, out var capture) || !EternalFlameCurseCurrent(capture)) return;
            capture.NativeMaximum = maximum;
            capture.AddedMaximum = capture.AddedRemaining = 0;
            if (EternalFlameEquipped(capture.Gem, out var rt))
            {
                int stage = BossRewardStage(rt, BossProfiles.InfernusRewardId);
                if (stage > 0) ExtendEternalFlameTimer(capture, maximum, remaining, stage);
            }
        }
        internal void RefreshEternalFlameNativeTimer(Se_U_EternalFlame_Curse curse)
        {
            if (_eternalFlameWritingTimer || !_eternalFlameCurses.TryGetValue(curse, out var capture) || !EternalFlameCurseCurrent(capture)) return;
            int stage = EternalFlameEquipped(capture.Gem, out var rt) ? BossRewardStage(rt, BossProfiles.InfernusRewardId) : 0;
            if (stage == 0) { RestoreEternalFlameTimer(capture); return; }
            capture.AddedMaximum = capture.AddedRemaining = 0;
            ExtendEternalFlameTimer(capture, capture.NativeMaximum, capture.NativeMaximum, stage);
        }
        private void ExtendEternalFlameTimer(EternalFlameCurse capture, float maximum, float remaining, int stage)
        {
            var action = EternalFlameAction(stage, 0);
            float extra = Math.Min(action.ValueMilli, action.CapMilli) / 1000f;
            capture.NativeMaximum = maximum;
            capture.AddedMaximum = extra;
            capture.AddedRemaining = extra;
            _eternalFlameWritingTimer = true;
            try { capture.Curse.SetTimer(maximum + extra, Math.Min(maximum + extra, remaining + extra)); }
            finally { _eternalFlameWritingTimer = false; }
        }
        private void RestoreEternalFlameTimer(EternalFlameCurse capture)
        {
            if (capture.AddedMaximum == 0 && capture.AddedRemaining == 0) return;
            if (capture.Curse != null && capture.Curse.isActive && BossNativeSameLife(capture.Curse, capture.Life)
                && capture.Curse.maxDuration.HasValue && capture.Curse.remainingDuration.HasValue)
            {
                float maximum = Math.Max(0, capture.Curse.maxDuration.Value - capture.AddedMaximum);
                float remaining = Math.Min(maximum, Math.Max(0, capture.Curse.remainingDuration.Value - capture.AddedRemaining));
                _eternalFlameWritingTimer = true;
                try { capture.Curse.SetTimer(maximum, remaining); }
                finally { _eternalFlameWritingTimer = false; }
            }
            capture.AddedMaximum = capture.AddedRemaining = 0;
        }
        private bool EternalFlameCurseCurrent(EternalFlameCurse capture)
            => capture.Curse != null && capture.Curse.isActive && BossNativeSameLife(capture.Curse, capture.Life)
                && BossNativeSameLife(capture.Gem, capture.GemLife) && BossNativeSameLife(capture.Runtime.Hero, capture.HeroLife)
                && BossNativeSameLife(capture.Curse.victim, capture.VictimLife);
        private bool EternalFlameNativePacket(Gem_U_EternalFlame gem, Actor actor, Entity target, out HeroRuntime rt,
            out NativeAttributedDamagePacket.Packet packet)
        {
            packet = NativeAttributedDamagePacket.Current;
            if (!EternalFlameEquipped(gem, out rt) || packet == null || !packet.Admitted || packet.Actor != actor || packet.Victim != target
                || target == null || target.GetRelation(rt.Hero) != EntityRelation.Enemy || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || packet.Identity.GeneratedOrigin != GeneratedOrigin.None || packet.Identity.OwnerId != rt.Hero.GetInstanceID()
                || packet.Identity.NativePayloadKind == NativePayloadKind.SummonAttack || !_memoryAttribution.IsCurrent(packet.Identity)) return false;
            return true;
        }
        internal void EternalFlameNativeProcSucceeded(Se_U_EternalFlame_Curse curse, EventInfoDamage damage)
        {
            if (!(curse.parentActor is Gem_U_EternalFlame gem) || curse.info.caster != gem.owner
                || !EternalFlameNativePacket(gem, damage.actor, damage.victim, out var rt, out var packet)
                || !packet.Chain.Equals(damage.chain) || damage.damage.amount <= 0 || curse.victim != damage.victim) return;
            int stage = BossRewardStage(rt, BossProfiles.InfernusRewardId);
            if (stage < 2) return;
            var state = EternalFlameState(rt, stage);
            var action = EternalFlameAction(stage, 1);
            float now = Time.time;
            if (now < state.StackReady) return;
            int slot = -1;
            for (int i = 0; i < state.Targets.Length; i++)
            {
                if (state.Targets[i] == damage.victim && BossNativeSameLife(damage.victim, state.TargetLife[i]))
                { if (now < state.TargetReady[i]) return; slot = i; break; }
                if (slot < 0 && (state.Targets[i] == null || !state.Targets[i].isActive
                    || !BossNativeSameLife(state.Targets[i], state.TargetLife[i]) || now >= state.TargetReady[i])) slot = i;
            }
            if (slot < 0) return;
            int before = damage.victim.Status.fireStack;
            ElementalStatusEffect applied;
            EnterGenerated(rt.Hero);
            try { applied = curse.ApplyElemental(ElementalType.Fire, damage.victim, Math.Min(action.ValueMilli, action.CapMilli) / 1000); }
            finally { ExitGenerated(rt.Hero); }
            if (applied == null || damage.victim.Status.fireStack <= before) return;
            state.Targets[slot] = damage.victim; state.TargetLife[slot] = BossNativeActorLife(damage.victim);
            state.TargetReady[slot] = now + action.DurationMillis / 1000f;
            state.StackReady = now + action.CooldownMillis / 1000f;
        }
        internal bool ClaimEternalFlameMainPacket(Gem_U_EternalFlame gem, Actor actor, Entity target)
        {
            if (!EternalFlameNativePacket(gem, actor, target, out var rt, out var packet)
                || packet.Identity.NativePayloadKind != NativePayloadKind.Skill || packet.Identity.SourceMemory != gem.skill.GetType().Name
                || (actor != gem.skill && actor.FindFirstAncestorOfType<SkillTrigger>() != gem.skill)) return false;
            int stage = BossRewardStage(rt, BossProfiles.InfernusRewardId);
            return stage == 3 && EternalFlameState(rt, stage).MainPackets.Add(packet.Identity.ActivationId);
        }
        internal int EternalFlameCritThreshold(int original, Gem_U_EternalFlame gem, Actor actor, Entity target, BossEternalFlameCritGate.Scope scope)
        {
            if (!scope.First || scope.Gem != gem || scope.Actor != actor || scope.Target != target
                || !EternalFlameNativePacket(gem, actor, target, out var rt, out _) || BossRewardStage(rt, BossProfiles.InfernusRewardId) != 3) return original;
            var action = EternalFlameAction(3, 2);
            int threshold = action.ValueMilli / 1000;
            if (target.Status.fireStack < threshold || target.Status.fireStack >= original || Time.time < EternalFlameState(rt, 3).CritReady) return original;
            bool ownCurse = false;
            foreach (var effect in target.Status.statusEffects)
                if (effect is Se_U_EternalFlame_Curse curse && curse.isActive && curse.parentActor == gem && curse.info.caster == rt.Hero)
                { ownCurse = true; break; }
            if (!ownCurse) return original;
            var state = EternalFlameState(rt, 3);
            state.CritReady = Time.time + action.CooldownMillis / 1000f;
            EternalFlameCritVisual(rt, state, Time.time);
            return threshold;
        }
        private void EternalFlameCritVisual(HeroRuntime rt, EternalFlameOwner state, float now)
        {
            if (state.CritVisual == 0) state.CritVisual = ++rt.Boss.NextId;
            state.CritVisualReady = now >= state.CritReady;
            state.CritVisualUntil = Math.Max(now, state.CritReady) + EternalFlameAction(3, 2).CooldownMillis / 1000f;
            PublishBossVisual(rt, state.CritVisual, 9, rt.Hero.agentPosition, rt.Hero.agentPosition, 1,
                state.CritReady > now ? state.CritReady : now, state.CritVisualUntil,
                element:BossElement.Fire,count:state.CritVisualReady ? 1 : 0);
        }
        private void TickEternalFlame(HeroRuntime rt, float now)
        {
            int stage = BossRewardStage(rt, BossProfiles.InfernusRewardId);
            if (_eternalFlameOwners.TryGetValue(rt, out var state))
            {
                if (state.Stage != stage) { ClearEternalFlame(rt); state = null; }
                else if (stage == 3 && (state.CritVisual == 0 || state.CritVisualReady != (now >= state.CritReady) || now >= state.CritVisualUntil))
                    EternalFlameCritVisual(rt, state, now);
            }
            else if (stage > 0) state = EternalFlameState(rt, stage);
            _eternalFlameScratch.Clear();
            foreach (var pair in _eternalFlameCurses)
            {
                var capture = pair.Value;
                if (capture.Runtime != rt) continue;
                if (!EternalFlameCurseCurrent(capture))
                { RestoreEternalFlameTimer(capture); _eternalFlameScratch.Add(pair.Key); continue; }
                if (stage == 0 || capture.Curse.parentActor != capture.Gem || capture.Curse.info.caster != rt.Hero
                    || capture.Gem.owner != rt.Hero || !EternalFlameEquipped(capture.Gem, out _)) RestoreEternalFlameTimer(capture);
                else if (capture.AddedMaximum == 0) CaptureEternalFlameCurse(capture.Curse);
            }
            foreach (var curse in _eternalFlameScratch) _eternalFlameCurses.Remove(curse);
        }
        private void ClearEternalFlame(HeroRuntime rt)
        {
            foreach (var pair in _eternalFlameCurses) if (pair.Value.Runtime == rt) RestoreEternalFlameTimer(pair.Value);
            if (_eternalFlameOwners.TryGetValue(rt, out var state) && state.CritVisual != 0)
                PublishBossVisual(rt, state.CritVisual, 9, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, Time.time, Time.time, true);
            _eternalFlameOwners.Remove(rt);
        }
        internal void ClearEternalFlameActor(Actor actor)
        {
            if (actor is Se_U_EternalFlame_Curse curse)
            {
                if (!_eternalFlameCurses.TryGetValue(curse, out var capture)) return;
                RestoreEternalFlameTimer(capture);
                _eternalFlameCurses.Remove(curse);
                return;
            }
            if (!(actor is Gem_U_EternalFlame) && !(actor is Hero)) return;
            _eternalFlameScratch.Clear();
            foreach (var pair in _eternalFlameCurses)
                if (pair.Value.Gem == actor || pair.Value.Runtime.Hero == actor)
                { RestoreEternalFlameTimer(pair.Value); _eternalFlameScratch.Add(pair.Key); }
            foreach (var removed in _eternalFlameScratch) _eternalFlameCurses.Remove(removed);
            if (actor is Hero hero && _runtimes.TryGetValue(hero, out var rt)) ClearEternalFlame(rt);
        }
    }
}

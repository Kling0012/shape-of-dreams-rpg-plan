using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Ai_U_ShoutOfOblivion), "get_currentDamageAmp")]
    internal static class ObliviaxShoutHunt
    {
        private static void Postfix(Ai_U_ShoutOfOblivion __instance, ref float __result)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.AmplifyBossShoutHunt(__instance, ref __result);
        }
    }

    // Actual Dew.Contents IL: OnCreate IL_0085 is the sole StartDisplacement;
    // its native 4m destination is IL_0043 and its duration remains IL_0058 (0.4s).
    // Replace only that call, not the global displacement API or the native lifecycle.
    [HarmonyPatch(typeof(Ai_U_ShoutOfOblivion), "OnCreate")]
    internal static class ObliviaxShoutBackstep
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(EntityControl), nameof(EntityControl.StartDisplacement));
            var replacement = AccessTools.Method(typeof(ObliviaxShoutBackstep), nameof(Start));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    count++;
                    var instance = new CodeInstruction(OpCodes.Ldarg_0); instance.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    yield return instance;
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Shout adapter requires exactly one native backstep entry.");
        }
        private static void Start(EntityControl control, Displacement displacement, Ai_U_ShoutOfOblivion instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.ExtendBossShoutBackstep(instance, control, displacement);
            control.StartDisplacement(displacement);
        }
    }

    // Actual OnHit IL_000f reads this instance's loaded stunDuration; IL_0016
    // creates its native StunEffect after DamageInstance.OnHit. No assumed asset duration.
    [HarmonyPatch(typeof(Ai_U_ShoutOfOblivion), "OnHit")]
    internal static class ObliviaxShoutStun
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(Actor), nameof(Actor.CreateBasicEffect),
                new[] { typeof(Entity), typeof(BasicEffect), typeof(float), typeof(string), typeof(DuplicateEffectBehavior) });
            var replacement = AccessTools.Method(typeof(ObliviaxShoutStun), nameof(Create));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    count++;
                    var instance = new CodeInstruction(OpCodes.Ldarg_0); instance.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    yield return instance;
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Shout adapter requires exactly one native hit-stun entry.");
        }
        private static Se_GenericEffectContainer Create(Actor actor, Entity target, BasicEffect effect, float duration,
            string id, DuplicateEffectBehavior behavior, Ai_U_ShoutOfOblivion instance)
        {
            if (NetworkServer.active && actor == instance && effect is StunEffect)
                HostAuthority.NativeInstance?.ExtendBossShoutStun(instance, target, ref duration);
            return actor.CreateBasicEffect(target, effect, duration, id, behavior);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class BossShoutActivation
        {
            internal long Id;
            internal int References, TargetCount;
            internal readonly long[] Targets = new long[64];
            internal void Reset() { Id = 0; References = TargetCount = 0; Array.Clear(Targets, 0, Targets.Length); }
        }
        private sealed class BossShoutBinding
        {
            internal Ai_U_ShoutOfOblivion Instance;
            internal St_U_ShoutOfOblivion Skill;
            internal HeroRuntime Runtime;
            internal Actor Parent;
            internal Hero Owner;
            internal Room Room;
            internal string Run;
            internal BossShoutActivation Activation;
            internal long InstanceLife, SkillLife, ParentLife, OwnerLife;
            internal bool BackstepApplied;
            internal void Reset()
            {
                Instance = null; Skill = null; Runtime = null; Parent = null; Owner = null; Room = null; Run = null; Activation = null;
                InstanceLife = SkillLife = ParentLife = OwnerLife = 0; BackstepApplied = false;
            }
        }
        private readonly BossObjectPool<BossShoutBinding> _bossShoutBindingPool = new BossObjectPool<BossShoutBinding>(128, () => new BossShoutBinding());
        private readonly BossObjectPool<BossShoutActivation> _bossShoutActivationPool = new BossObjectPool<BossShoutActivation>(128, () => new BossShoutActivation());
        private readonly Dictionary<Ai_U_ShoutOfOblivion, BossShoutBinding> _bossShoutBindings = new Dictionary<Ai_U_ShoutOfOblivion, BossShoutBinding>(128);
        private readonly Dictionary<long, BossShoutActivation> _bossShoutActivations = new Dictionary<long, BossShoutActivation>(128);
        private readonly Ai_U_ShoutOfOblivion[] _bossShoutScratch = new Ai_U_ShoutOfOblivion[128];

        internal void BindBossShoutInstance(Ai_U_ShoutOfOblivion instance, Actor parent, BossNativeCast cast)
        {
            if (!NetworkServer.active || instance == null || instance.gem != null || cast == null || cast.Activation <= 0
                || !(cast.Trigger is St_U_ShoutOfOblivion skill) || parent != skill || !(cast.Owner is Hero hero)
                || instance.info.caster != hero || skill.owner != hero || !BossNativeCastCurrent(cast)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !_runtimes.TryGetValue(hero, out var rt)
                || !BossEnsure(rt) || BossRewardStage(rt, BossProfiles.ObliviaxRewardId) < 1) return;
            ClearBossShoutActor(instance);
            var binding = _bossShoutBindingPool.Rent(); if (binding == null) return;
            if (!_bossShoutActivations.TryGetValue(cast.Activation, out var activation))
            {
                activation = _bossShoutActivationPool.Rent();
                if (activation == null) { _bossShoutBindingPool.Return(binding); return; }
                activation.Reset(); activation.Id = cast.Activation; _bossShoutActivations.Add(activation.Id, activation);
            }
            activation.References++;
            binding.Instance = instance; binding.Skill = skill; binding.Runtime = rt; binding.Owner = hero; binding.Parent = parent;
            binding.Room = cast.Room; binding.Run = cast.Run; binding.Activation = activation;
            binding.InstanceLife = BossNativeActorLife(instance); binding.SkillLife = BossNativeActorLife(skill);
            binding.ParentLife = BossNativeActorLife(parent); binding.OwnerLife = BossNativeActorLife(hero); binding.BackstepApplied = false;
            _bossShoutBindings.Add(instance, binding);
        }
        private bool BossShoutCurrent(Ai_U_ShoutOfOblivion instance, out BossShoutBinding binding, out int stage)
        {
            stage = 0; binding = null;
            if (!NetworkServer.active || instance == null || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || !_bossShoutBindings.TryGetValue(instance, out binding) || binding.Activation == null || binding.Activation.Id <= 0
                || !instance.isActive || instance.gem != null || instance.info.caster != binding.Owner || instance.parentActor != binding.Parent
                || binding.Parent != binding.Skill || binding.Skill == null || !binding.Skill.isActive || binding.Skill.owner != binding.Owner
                || !BossAlive(binding.Owner) || !BossNativeEquippedSkill(binding.Owner, binding.Skill)
                || !BossNativeSameLife(instance, binding.InstanceLife) || !BossNativeSameLife(binding.Skill, binding.SkillLife)
                || !BossNativeSameLife(binding.Parent, binding.ParentLife) || !BossNativeSameLife(binding.Owner, binding.OwnerLife)
                || !BossNativeContextCurrent(binding.Room, binding.Run)
                || !_runtimes.TryGetValue(binding.Owner, out var rt) || rt != binding.Runtime
                || !BossEnsure(rt)) return false;
            if (!_bossShoutBindings.TryGetValue(instance, out var current) || current != binding) return false;
            stage = BossRewardStage(binding.Runtime, BossProfiles.ObliviaxRewardId); return stage > 0;
        }
        internal void AmplifyBossShoutHunt(Ai_U_ShoutOfOblivion instance, ref float amount)
        {
            if (!BossShoutCurrent(instance, out _, out _) || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            if (zone != null) amount += .03f * Mathf.Clamp(zone.currentHuntLevel, 0, 5);
        }
        internal void ExtendBossShoutBackstep(Ai_U_ShoutOfOblivion instance, EntityControl control, Displacement displacement)
        {
            if (!BossShoutCurrent(instance, out var binding, out int stage) || stage < 2 || binding.BackstepApplied
                || control != binding.Owner.Control || !(displacement is DispByDestination step) || step.hasStarted
                || !step.isFriendly || step.rotateForward || step.isCanceledByCC || step.canGoOverTerrain
                || Math.Abs(step.duration - .4f) > .0001f || step.ease != DewEase.EaseOutQuad) return;
            var origin = binding.Owner.agentPosition; var forward = instance.info.forward;
            var native = origin - forward * 4f;
            if (!BossFinite(forward) || !BossFinite(step.destination) || (step.destination - native).sqrMagnitude > .000001f
                || !BossGround(origin, origin - forward * 5f, 5f, out var legal)) return;
            binding.BackstepApplied = true;
            step.destination = legal;
        }
        internal void ExtendBossShoutStun(Ai_U_ShoutOfOblivion instance, Entity target, ref float duration)
        {
            if (!BossShoutCurrent(instance, out var binding, out int stage) || stage < 3 || !BossAlive(target)
                || target.GetRelation(binding.Owner) != EntityRelation.Enemy || target.IsAnyBoss() || target.Status.hasCrowdControlImmunity
                || float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f) return;
            var activation = binding.Activation; long life = BossNativeActorLife(target);
            if (life <= 0) return;
            for (int i = 0; i < activation.TargetCount; i++) if (activation.Targets[i] == life) return;
            if (activation.TargetCount == activation.Targets.Length) return;
            activation.Targets[activation.TargetCount++] = life;
            duration += .4f;
        }
        private void RemoveBossShout(Ai_U_ShoutOfOblivion instance)
        {
            if (!_bossShoutBindings.TryGetValue(instance, out var binding)) return;
            _bossShoutBindings.Remove(instance); var activation = binding.Activation;
            if (--activation.References == 0)
            { _bossShoutActivations.Remove(activation.Id); activation.Reset(); _bossShoutActivationPool.Return(activation); }
            binding.Reset(); _bossShoutBindingPool.Return(binding);
        }
        internal void ClearBossShoutActor(Actor actor)
        {
            if (actor is Ai_U_ShoutOfOblivion instance) { RemoveBossShout(instance); return; }
            int count = 0;
            foreach (var pair in _bossShoutBindings)
                if (pair.Value.Parent == actor || pair.Value.Skill == actor || pair.Value.Owner == actor) _bossShoutScratch[count++] = pair.Key;
            for (int i = 0; i < count; i++) { var key = _bossShoutScratch[i]; _bossShoutScratch[i] = null; RemoveBossShout(key); }
        }
        internal void ClearBossShoutCast(SkillTrigger skill)
        {
            ClearBossShoutActor(skill);
            if (skill is St_U_ShoutOfOblivion && skill.owner is Hero hero && _runtimes.TryGetValue(hero, out var rt)) ClearObliviaxBoss(rt);
        }
        private void TickBossShout(HeroRuntime rt)
        {
            float now = Time.time;
            if (now < rt.BossNative.ShoutPoll) return;
            rt.BossNative.ShoutPoll = now + .1f;
            int count = 0;
            foreach (var pair in _bossShoutBindings)
                if (pair.Value.Runtime == rt && !BossShoutCurrent(pair.Key, out _, out _)) _bossShoutScratch[count++] = pair.Key;
            for (int i = 0; i < count; i++) { var key = _bossShoutScratch[i]; _bossShoutScratch[i] = null; RemoveBossShout(key); }
        }
        private void ClearBossShout(HeroRuntime rt)
        {
            int count = 0;
            foreach (var pair in _bossShoutBindings) if (pair.Value.Runtime == rt) _bossShoutScratch[count++] = pair.Key;
            for (int i = 0; i < count; i++) { var key = _bossShoutScratch[i]; _bossShoutScratch[i] = null; RemoveBossShout(key); }
        }
    }
}

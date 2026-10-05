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
    [HarmonyPatch(typeof(Gem_U_GlacialCore), "OnDealDamage")]
    internal static class SkollCoreNativeDamageAdmission
    {
        private static bool Prefix() => !NetworkServer.active || HostAuthority.NativeInstance?.SkollCoreGeneratedDamage() != true;
    }

    // Managed 1.4.0.13: exact display class and iterator; one DoHeal at IL_01ab.
    [HarmonyPatch]
    internal static class SkollCoreColdHealRoutine
    {
        private static readonly Type Closure = typeof(Gem_U_GlacialCore).GetNestedType("<>c__DisplayClass29_0",BindingFlags.NonPublic);
        private static readonly Type Routine = Closure?.GetNestedType("<<OnDealDamage>g__Routine|0>d",BindingFlags.NonPublic);
        private static readonly FieldInfo RoutineClosure = Routine == null ? null : AccessTools.DeclaredField(Routine,"<>4__this");
        private static readonly FieldInfo RoutineState = Routine == null ? null : AccessTools.DeclaredField(Routine,"<>1__state");
        private static readonly FieldInfo Core = Closure == null ? null : AccessTools.DeclaredField(Closure,"<>4__this");
        private static readonly FieldInfo Info = Closure == null ? null : AccessTools.DeclaredField(Closure,"info");
        internal struct HealScope
        {
            internal object Routine;
            internal Gem_U_GlacialCore Core;
            internal EventInfoDamage Info;
            internal Actor Actor;
            internal Entity Target;
            internal ReactionChain Chain;
            internal bool Applied, Completed;
            internal int Depth;
        }
        internal static HealScope Current;
        private static MethodBase TargetMethod()
        {
            if (RoutineClosure?.FieldType != Closure || RoutineState?.FieldType != typeof(int)
                || Core?.FieldType != typeof(Gem_U_GlacialCore) || Info?.FieldType != typeof(EventInfoDamage))
                throw new InvalidOperationException("GlacialCore adapter does not match the native cold-heal iterator layout.");
            return AccessTools.DeclaredMethod(Routine,"MoveNext") ?? throw new InvalidOperationException("Missing GlacialCore native cold-heal continuation.");
        }
        private static bool Read(object routine, out Gem_U_GlacialCore core, out EventInfoDamage info)
        {
            var closure = RoutineClosure.GetValue(routine);
            core = Core.GetValue(closure) as Gem_U_GlacialCore;
            info = (EventInfoDamage)Info.GetValue(closure);
            return core != null;
        }
        private static void Prefix(object __instance)
        {
            if (NetworkServer.active && (int)RoutineState.GetValue(__instance) == 0 && Read(__instance,out var core,out var info))
                HostAuthority.NativeInstance?.BeginSkollCoreHeal(__instance,core,info);
        }
        private static void Postfix(object __instance, bool __result)
        {
            if (!__result && NetworkServer.active && Read(__instance,out var core,out _)) HostAuthority.NativeInstance?.EndSkollCoreHeal(__instance,core);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var heal = AccessTools.Method(typeof(Actor),nameof(Actor.DoHeal),new[] { typeof(HealData),typeof(Entity),typeof(ReactionChain) });
            var replacement = AccessTools.Method(typeof(SkollCoreColdHealRoutine),nameof(DoNativeHeal));
            int sites = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(heal))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    instruction.opcode = OpCodes.Call; instruction.operand = replacement; sites++;
                }
                yield return instruction;
            }
            if (sites != 1) throw new InvalidOperationException("GlacialCore adapter expected exactly one native coroutine heal call.");
        }
        private static void DoNativeHeal(Actor actor, HealData heal, Entity target, ReactionChain chain, object routine)
        {
            var previous = Current;
            Current = default;
            if (NetworkServer.active && Read(routine,out var core,out var info))
                Current = new HealScope { Routine=routine,Core=core,Info=info,Actor=actor,Target=target,Chain=chain };
            // One original dispatch, preserving all native processors and heal-to-bank conversion.
            try { actor.DoHeal(heal,target,chain); }
            finally { Current = previous; }
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DoHeal))]
    internal static class SkollCoreHealScopeDepth
    {
        private static void Prefix(out int __state)
        {
            ref var scope = ref SkollCoreColdHealRoutine.Current;
            __state = scope.Core != null ? scope.Depth : -1;
            if (__state >= 0) scope.Depth++;
        }
        private static void Finalizer(int __state)
        {
            if (__state >= 0) SkollCoreColdHealRoutine.Current.Depth = __state;
        }
    }

    [HarmonyPatch(typeof(Entity), nameof(Entity.ProcessReceivedHeal))]
    internal static class SkollCoreFinalNativeHeal
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Entity __instance, ref HealData data, Actor actor)
        {
            ref var scope = ref SkollCoreColdHealRoutine.Current;
            if (!NetworkServer.active || scope.Core == null || scope.Depth != 1 || scope.Applied || scope.Actor != actor || scope.Target != __instance) return;
            scope.Applied = true;
            HostAuthority.NativeInstance?.AmplifySkollCoreHeal(scope.Routine,scope.Core,scope.Info,ref data);
        }
    }
    [HarmonyPatch(typeof(Gem_U_GlacialCore), "EntityEventOnTakeHeal")]
    internal static class SkollCoreSuccessfulNativeHeal
    {
        private static void Postfix(Gem_U_GlacialCore __instance, EventInfoHeal obj)
        {
            ref var scope = ref SkollCoreColdHealRoutine.Current;
            if (!NetworkServer.active || scope.Core != __instance || scope.Depth != 1 || scope.Completed || obj.actor != scope.Actor
                || obj.target != scope.Target || !obj.chain.Equals(scope.Chain) || obj.amount + obj.discardedAmount <= 0) return;
            scope.Completed = true;
            HostAuthority.NativeInstance?.CompleteSkollCoreHeal(scope.Routine,__instance,scope.Info);
        }
    }

    [HarmonyPatch(typeof(Gem_U_GlacialCore), "ActiveLogicUpdate")]
    internal static class SkollCoreNativeCadence
    {
        private static void Prefix(Gem_U_GlacialCore __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BeginSkollCoreUpdate(__instance);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var evaluate = AccessTools.Method(typeof(AnimationCurve),nameof(AnimationCurve.Evaluate),new[] { typeof(float) });
            var interval = AccessTools.Method(typeof(SkollCoreNativeCadence),nameof(ChooseInterval));
            int sites = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(evaluate)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call,interval);
                sites++;
            }
            if (sites != 1) throw new InvalidOperationException("GlacialCore adapter expected exactly one native shoot-interval curve evaluation.");
        }
        private static float ChooseInterval(float nativeInterval, Gem_U_GlacialCore core)
            => NetworkServer.active ? HostAuthority.NativeInstance?.SkollCoreInterval(core,nativeInterval) ?? nativeInterval : nativeInterval;
    }

    [HarmonyPatch(typeof(Gem_U_GlacialCore), "InitProjectile")]
    internal static class SkollCoreNativeTarget
    {
        private static void Postfix(Gem_U_GlacialCore __instance, Ai_Gem_U_GlacialCore_Projectile p)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RetargetSkollCoreProjectile(__instance,p);
        }
    }
    // Dew.CreateAbilityInstance invokes this only after PrepareAndSpawn returns;
    // unlike OnCreate, this also runs synchronously on a dedicated server.
    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnAbilityInstanceCreated))]
    internal static class SkollCoreRealProjectile
    {
        private static void Postfix(Actor __instance, EventInfoAbilityInstance info)
        {
            if (NetworkServer.active && __instance is Gem_U_GlacialCore && info.actor == __instance
                && info.instance is Ai_Gem_U_GlacialCore_Projectile projectile && projectile.parentActor == __instance)
                HostAuthority.NativeInstance?.CountSkollCoreShot(projectile);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class SkollCorePending
        {
            internal long Epoch, CoreLife, ActorLife, VictimLife, ParentLife;
            internal float CoreCreation, VictimCreation;
            internal Entity Victim;
            internal Actor Actor;
            internal Actor ParentActor;
            internal ReactionChain Chain;
        }
        private sealed class SkollCoreState
        {
            internal float Creation, BurstUntil, PriorityUntil, PriorityCreation;
            internal Entity Priority;
            internal Gem_U_GlacialCore Core;
            internal Hero Owner;
            internal long CoreLife, OwnerLife, PriorityLife;
            internal int Remaining;
            internal long BurstVisual;
            internal bool ApprovedThisUpdate;
            internal readonly Dictionary<object, SkollCorePending> Pending = new Dictionary<object, SkollCorePending>();
        }
        private static readonly AccessTools.FieldRef<Gem_U_GlacialCore,Dictionary<ReactionChain,float>> SkollCoreBank
            = AccessTools.FieldRefAccess<Gem_U_GlacialCore,Dictionary<ReactionChain,float>>("_remainingDamages");
        private static readonly AccessTools.FieldRef<Gem,AbilityTargetValidatorWrapper> SkollCoreTargets
            = AccessTools.FieldRefAccess<Gem,AbilityTargetValidatorWrapper>("tvDefaultHarmfulEffectTargets");
        internal bool SkollCoreGeneratedDamage() => AttributionGeneratedOrigin() != GeneratedOrigin.None;
        private bool SkollCoreRuntime(Gem_U_GlacialCore core, out HeroRuntime rt, out SkollCoreState state, bool create = false)
        {
            rt = null; state = null;
            if (!NetworkServer.active || core == null || !core.isValid || !BossAlive(core.owner) || !_runtimes.TryGetValue(core.owner,out rt)
                || !BossEnsure(rt) || BossRewardStage(rt,BossProfiles.SkollRewardId) == 0) return false;
            bool equipped = false;
            if (rt.Hero.Skill != null) foreach (var slot in rt.Hero.Skill.gems) if (ReferenceEquals(slot.Value,core)) { equipped = true; break; }
            if (!equipped) return false;
            if (!_skollStates.TryGetValue(rt,out var owner))
            {
                if (!create) return false;
                owner = SkollOwnerState(rt);
            }
            if (owner.Cores.TryGetValue(core,out state) && (!ReferenceEquals(state.Core,core) || !ReferenceEquals(state.Owner,rt.Hero)
                || state.Creation != core.creationTime || !BossNativeSameLife(core,state.CoreLife) || !BossNativeSameLife(rt.Hero,state.OwnerLife)))
            {
                if (!create) return false;
                state.Pending.Clear();
                state.Remaining=0;
                if (state.BurstVisual != 0) SkollPublishBurst(rt,state,Time.time);
                owner.Cores.Remove(core); state = null;
            }
            if (state == null && create)
            {
                if (owner.Cores.Count >= BossProfiles.MaxEntries) return false;
                owner.Cores.Add(core,state = new SkollCoreState { Core=core,Owner=rt.Hero,Creation=core.creationTime,
                    CoreLife=BossNativeActorLife(core),OwnerLife=BossNativeActorLife(rt.Hero) });
            }
            return state != null;
        }
        private BossRewardStage SkollCoreReward(HeroRuntime rt)
            => BossProfiles.TryGetReward(BossProfiles.SkollRewardId,out var profile) ? profile.Stages[BossRewardStage(rt,profile.Id) - 1] : null;
        internal void BeginSkollCoreHeal(object routine, Gem_U_GlacialCore core, EventInfoDamage info)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (SkollCoreGeneratedDamage() || info.damage.elemental != ElementalType.Cold || info.damage.amount <= 0
                || packet == null || packet.Actor != info.actor || packet.Victim != info.victim || !packet.Chain.Equals(info.chain)
                || info.actor == null || !SkollCoreRuntime(core,out var rt,out var state,true)
                || info.actor.FindFirstOfType<Hero>() != rt.Hero || info.victim == null || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy
                || state.Pending.Count >= BossProfiles.MaxEntries) return;
            var parent=info.actor.parentActor;
            state.Pending[routine] = new SkollCorePending { Epoch=rt.ShieldEquipmentEpoch,CoreCreation=core.creationTime,CoreLife=state.CoreLife,
                ActorLife=BossNativeActorLife(info.actor),VictimLife=BossNativeActorLife(info.victim),ParentActor=parent,
                ParentLife=parent != null ? BossNativeActorLife(parent) : 0,
                VictimCreation=info.victim.creationTime,Victim=info.victim,Actor=info.actor,Chain=info.chain };
        }
        private bool SkollPendingHeal(object routine, Gem_U_GlacialCore core, EventInfoDamage info, out HeroRuntime rt, out SkollCoreState state)
        {
            if (!SkollCoreRuntime(core,out rt,out state) || !state.Pending.TryGetValue(routine,out var pending)) return false;
            return pending.Epoch == rt.ShieldEquipmentEpoch && pending.CoreCreation == core.creationTime
                && BossNativeSameLife(core,pending.CoreLife) && ReferenceEquals(info.actor,pending.Actor) && BossNativeSameLife(info.actor,pending.ActorLife)
                && ReferenceEquals(info.actor.FindFirstOfType<Hero>(),rt.Hero) && ReferenceEquals(info.actor.parentActor,pending.ParentActor)
                && (ReferenceEquals(pending.ParentActor,null) || BossNativeSameLife(pending.ParentActor,pending.ParentLife))
                && ReferenceEquals(info.victim,pending.Victim) && BossNativeSameLife(info.victim,pending.VictimLife) && pending.VictimCreation == info.victim.creationTime
                && pending.Chain.Equals(info.chain) && info.damage.elemental == ElementalType.Cold;
        }
        internal void AmplifySkollCoreHeal(object routine, Gem_U_GlacialCore core, EventInfoDamage info, ref HealData heal)
        {
            if (!SkollPendingHeal(routine,core,info,out var rt,out var state)) return;
            var action = SkollCoreReward(rt).Actions[0];
            float amount = heal.currentAmount;
            float multiplier = heal.amplificationMultiplier * heal.reductionMultiplier;
            if (amount <= 0 || multiplier <= 0) return;
            float high = Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower);
            float bonus = Math.Min(amount * action.ValueMilli / 100000f,high * action.MagnitudeMilli / 100000f);
            if (bonus <= 0 || !BossReady(rt,BossProfiles.SkollRewardId + ".heal",Time.time,action.CooldownMillis)) return;
            heal.AddAmount(bonus / multiplier);
        }
        internal void CompleteSkollCoreHeal(object routine, Gem_U_GlacialCore core, EventInfoDamage info)
        {
            if (!SkollPendingHeal(routine,core,info,out var rt,out var state)) return;
            var actions = SkollCoreReward(rt).Actions; float now = Time.time;
            if (actions.Count > 1)
            {
                state.Priority=info.victim; state.PriorityCreation=info.victim.creationTime;
                state.PriorityLife=BossNativeActorLife(info.victim);
                state.PriorityUntil=now + actions[1].DurationMillis / 1000f;
            }
            if (actions.Count > 2 && BossReady(rt,BossProfiles.SkollRewardId + ".burst",now,actions[2].CooldownMillis))
            {
                var action=actions[2]; state.Remaining=action.Count; state.BurstUntil=now + action.DurationMillis / 1000f;
                if (state.BurstVisual == 0) state.BurstVisual=++rt.Boss.NextId;
                SkollPublishBurst(rt,state,now);
            }
            state.Pending.Remove(routine);
        }
        internal void EndSkollCoreHeal(object routine, Gem_U_GlacialCore core)
        {
            if (core != null && core.owner != null && _runtimes.TryGetValue(core.owner,out var rt) && _skollStates.TryGetValue(rt,out var owner)
                && owner.Cores.TryGetValue(core,out var state)) state.Pending.Remove(routine);
        }
        internal void BeginSkollCoreUpdate(Gem_U_GlacialCore core)
        {
            if (SkollCoreRuntime(core,out _,out var state)) state.ApprovedThisUpdate=false;
        }
        internal float SkollCoreInterval(Gem_U_GlacialCore core, float nativeInterval)
        {
            if (!SkollCoreRuntime(core,out var rt,out var state) || state.Remaining <= 0 || Time.time >= state.BurstUntil
                || BossRewardStage(rt,BossProfiles.SkollRewardId) < 3) return nativeInterval;
            float bank=0; foreach (var entry in SkollCoreBank(core)) bank += entry.Value;
            if (bank <= 0) return nativeInterval;
            var validator=SkollCoreTargets(core);
            if (validator == null) return nativeInterval;
            var enemies=DewPhysics.OverlapCircleAllEntities(out var handle,rt.Hero.position,core.shootRadius,validator);
            bool legal=false;
            try { foreach (var enemy in enemies) if (BossAlive(enemy) && enemy.GetRelation(rt.Hero) == EntityRelation.Enemy) { legal=true; break; } }
            finally { handle.Return(); }
            if (!legal) return nativeInterval;
            state.ApprovedThisUpdate=true;
            return Math.Min(nativeInterval,SkollCoreReward(rt).Actions[2].IntervalMillis / 1000f);
        }
        internal void RetargetSkollCoreProjectile(Gem_U_GlacialCore core, Ai_Gem_U_GlacialCore_Projectile projectile)
        {
            if (!SkollCoreRuntime(core,out var rt,out var state) || projectile == null || projectile.parentActor != core
                || projectile.info.caster != rt.Hero || BossRewardStage(rt,BossProfiles.SkollRewardId) < 2) return;
            var target=state.Priority;
            state.Priority=null;
            if (Time.time >= state.PriorityUntil || !BossAlive(target) || target.creationTime != state.PriorityCreation || !BossNativeSameLife(target,state.PriorityLife)
                || target.GetRelation(rt.Hero) != EntityRelation.Enemy || SkollCoreTargets(core)?.Evaluate(target) != true
                || (target.position - rt.Hero.position).Flattened().sqrMagnitude > core.shootRadius * core.shootRadius) return;
            var info=projectile.info; info.target=target; projectile.info=info;
        }
        internal void CountSkollCoreShot(Ai_Gem_U_GlacialCore_Projectile projectile)
        {
            if (!(projectile.parentActor is Gem_U_GlacialCore core) || !SkollCoreRuntime(core,out var rt,out var state)
                || projectile.info.caster != rt.Hero || !state.ApprovedThisUpdate || state.Remaining <= 0 || Time.time >= state.BurstUntil) return;
            state.ApprovedThisUpdate=false; state.Remaining--;
            SkollPublishBurst(rt,state,Time.time);
        }
        private void SkollPublishBurst(HeroRuntime rt, SkollCoreState state, float now)
        {
            PublishBossVisual(rt,state.BurstVisual,9,rt.Hero.agentPosition,rt.Hero.agentPosition,state.Remaining,now,state.BurstUntil,
                state.Remaining == 0,element:BossElement.Cold,count:state.Remaining);
        }
        private void TickSkollCores(HeroRuntime rt, SkollState owner, float now)
        {
            owner.ExpiredCores.Clear();
            foreach (var pair in owner.Cores)
            {
                var core=pair.Key; var state=pair.Value;
                if (!SkollCoreRuntime(core,out var current,out var live) || current != rt || !ReferenceEquals(state,live)) { owner.ExpiredCores.Add(core); continue; }
                if (now >= state.PriorityUntil) state.Priority=null;
                if (state.Remaining > 0)
                {
                    if (now >= state.BurstUntil) state.Remaining=0;
                    SkollPublishBurst(rt,state,now);
                }
            }
            foreach (var core in owner.ExpiredCores)
            {
                var state=owner.Cores[core]; state.Pending.Clear(); state.Remaining=0;
                if (state.BurstVisual != 0) SkollPublishBurst(rt,state,now);
                owner.Cores.Remove(core);
            }
            owner.ExpiredCores.Clear();
        }
        private void ClearSkollCores(HeroRuntime rt, SkollState owner)
        {
            foreach (var pair in owner.Cores) pair.Value.Pending.Clear();
            owner.Cores.Clear(); owner.ExpiredCores.Clear();
        }
    }
}

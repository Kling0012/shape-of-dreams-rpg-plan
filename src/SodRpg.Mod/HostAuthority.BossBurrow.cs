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
    // A destruction callback is not an exit attack. Only the actual native Emerge creation is admitted.
    [HarmonyPatch(typeof(Se_U_Burrow), "Emerge")]
    internal static class AzurakBurrowNativeExit
    {
        internal static Se_U_Burrow Current;
        private static void Prefix(Se_U_Burrow __instance, out Se_U_Burrow __state)
        { __state = Current; Current = NetworkServer.active ? __instance : null; }
        private static void Finalizer(Se_U_Burrow __state) { Current = __state; }
        private static void ObserveChild(Ai_U_Burrow_Emerge child, Se_U_Burrow parent)
        { if (NetworkServer.active) HostAuthority.NativeInstance?.BeginAzurakBurrow(child, parent); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int sites = 0;
            var observe = AccessTools.DeclaredMethod(typeof(AzurakBurrowNativeExit), nameof(ObserveChild));
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method && method.DeclaringType == typeof(Actor)
                    && method.Name == nameof(Actor.CreateAbilityInstance) && method.IsGenericMethod
                    && method.GetGenericArguments()[0] == typeof(Ai_U_Burrow_Emerge))
                {
                    yield return new CodeInstruction(OpCodes.Dup); yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, observe); sites++;
                }
            }
            if (sites != 1) throw new InvalidOperationException("Burrow native emerge creation contract changed.");
        }
    }
    [HarmonyPatch(typeof(Ai_U_Burrow_Emerge), "OnCreate")]
    internal static class AzurakBurrowNativeCreate
    {
        internal static Ai_U_Burrow_Emerge Current;
        private static void Prefix(Ai_U_Burrow_Emerge __instance, out Ai_U_Burrow_Emerge __state)
        {
            __state = Current; Current = NetworkServer.active ? __instance : null;
            if (NetworkServer.active) HostAuthority.NativeInstance?.BeginAzurakBurrow(__instance, AzurakBurrowNativeExit.Current);
        }
        private static void Finalizer(Ai_U_Burrow_Emerge __state) { Current = __state; }
        private static float AdjustDaze(float duration, Ai_U_Burrow_Emerge instance)
            => HostAuthority.NativeInstance?.AzurakBurrowDaze(instance, duration) ?? duration;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(Ai_U_Burrow_Emerge), nameof(Ai_U_Burrow_Emerge.dazeDuration));
            var adjust = AccessTools.DeclaredMethod(typeof(AzurakBurrowNativeCreate), nameof(AdjustDaze));
            int sites = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, adjust); sites++; }
            }
            if (sites != 1) throw new InvalidOperationException("Burrow native daze argument contract changed.");
        }
    }
    [HarmonyPatch(typeof(Ai_U_Burrow_Emerge), "OnHit")]
    internal static class AzurakBurrowNativeHit
    {
        internal struct Scope { internal Ai_U_Burrow_Emerge Instance; internal Entity Target; }
        internal static Scope Current;
        private static void Prefix(Ai_U_Burrow_Emerge __instance, Entity entity, out Scope __state)
        { __state = Current; Current = NetworkServer.active ? new Scope { Instance = __instance, Target = entity } : default(Scope); }
        private static void Finalizer(Scope __state) { Current = __state; }
        private static float AdjustStun(float duration, Ai_U_Burrow_Emerge instance, Entity target)
            => HostAuthority.NativeInstance?.AzurakBurrowStun(instance, target, duration) ?? duration;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(Ai_U_Burrow_Emerge), nameof(Ai_U_Burrow_Emerge.stunDuration));
            var adjust = AccessTools.DeclaredMethod(typeof(AzurakBurrowNativeHit), nameof(AdjustStun));
            int sites = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, adjust); sites++;
                }
            }
            if (sites != 1) throw new InvalidOperationException("Burrow native stun argument contract changed.");
        }
    }
    [HarmonyPatch(typeof(Actor), "OnDisable")]
    internal static class AzurakBurrowNativePoolEnd
    {
        private static void Prefix(Actor __instance)
        { if (NetworkServer.active && __instance is Ai_U_Burrow_Emerge emerge) HostAuthority.NativeInstance?.EndAzurakBurrow(emerge); }
    }
    internal sealed partial class HostAuthority
    {
        private sealed class AzurakBurrowExit
        {
            internal HeroRuntime Runtime;
            internal Ai_U_Burrow_Emerge Instance;
            internal Se_U_Burrow Parent;
            internal Actor ParentActor;
            internal St_U_Burrow Skill;
            internal long InstanceLife, ParentLife, ParentActorLife, SkillLife, HeroLife, LastPacket, Visual;
            internal Room Room;
            internal string Run;
            internal float H, Spent;
            internal bool BudgetAdmitted, BudgetRejected;
            internal HostAuthority Host;
            internal readonly DataProcessor<DamageData, Actor, Entity> Processor;
            internal AzurakBurrowExit() { Processor=Process; }
            private void Process(ref DamageData damage,Actor actor,Entity target)
            { Host?.AzurakBurrowFlat(this,ref damage,actor,target); }
            internal void Reset()
            {
                Host=null; Runtime=null; Instance=null; Parent=null; ParentActor=null; Skill=null; Room=null; Run=null;
                InstanceLife=ParentLife=ParentActorLife=SkillLife=HeroLife=LastPacket=Visual=0;
                H=Spent=0; BudgetAdmitted=BudgetRejected=false;
            }
        }
        private readonly Dictionary<Ai_U_Burrow_Emerge, AzurakBurrowExit> _azurakBurrows = new Dictionary<Ai_U_Burrow_Emerge, AzurakBurrowExit>(64);
        private readonly BossObjectPool<AzurakBurrowExit> _azurakBurrowPool = new BossObjectPool<AzurakBurrowExit>(64,()=>new AzurakBurrowExit());
        private readonly List<Ai_U_Burrow_Emerge> _azurakBurrowExpired = new List<Ai_U_Burrow_Emerge>(64);
        internal void BeginAzurakBurrow(Ai_U_Burrow_Emerge instance, Se_U_Burrow parent)
        {
            if (instance != null && _azurakBurrows.TryGetValue(instance, out var existing)
                && BossNativeSameLife(instance, existing.InstanceLife)) return;
            EndAzurakBurrow(instance);
            if (!NetworkServer.active || instance == null || parent == null || instance.parentActor != parent
                || !(instance.info.caster is Hero hero) || parent.victim != hero || parent.info.caster != hero
                || !(instance.firstTrigger is St_U_Burrow skill) || parent.firstTrigger != skill || instance.gem != null
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !_runtimes.TryGetValue(hero, out var rt)
                || !BossEnsure(rt) || BossRewardStage(rt, BossProfiles.AzurakRewardId) == 0
                || !skill.isActive || skill.owner != hero || FindMemory(hero, nameof(St_U_Burrow)) != skill
                || _azurakBurrows.Count >= 64) return;
            if(AzurakOwner(rt)==null) return;
            var capture=_azurakBurrowPool.Rent(); if(capture==null) return;
            capture.Host=this; capture.Runtime=rt; capture.Instance=instance; capture.Parent=parent; capture.ParentActor=parent.parentActor; capture.Skill=skill;
            capture.InstanceLife=BossNativeActorLife(instance); capture.ParentLife=BossNativeActorLife(parent);
            capture.ParentActorLife=BossNativeActorLife(parent.parentActor); capture.SkillLife=BossNativeActorLife(skill); capture.HeroLife=BossNativeActorLife(hero);
            capture.Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom; capture.Run=NetworkedManagerBase<GameManager>.softInstance?.runId;
            capture.H=Math.Max(hero.Status.attackDamage,hero.Status.abilityPower); capture.Visual=++rt.Boss.NextId;
            _azurakBurrows.Add(instance, capture);
            // Process after ordinary native amplifiers: compensate them rather than amplifying the flat budget again.
            instance.dealtDamageProcessor.Add(capture.Processor, int.MaxValue - 1);
        }
        private bool AzurakBurrowCurrent(AzurakBurrowExit capture)
        {
            var hero = capture.Runtime.Hero;
            // The native parent is in OnDestroyActor during creation and may already be inactive at impact.
            // Its immutable pool generation/ancestry remains required, never its active status.
            return NetworkServer.active && capture.Instance != null
                && (capture.Instance.isActive || AzurakBurrowNativeCreate.Current == capture.Instance)
                && BossNativeSameLife(capture.Instance, capture.InstanceLife) && BossNativeSameLife(hero, capture.HeroLife)
                && capture.Instance.info.caster == hero && capture.Instance.parentActor == capture.Parent && capture.Instance.gem == null
                && capture.Parent != null && capture.Parent.victim == hero && capture.Parent.info.caster == hero
                && capture.Parent.parentActor == capture.ParentActor && BossNativeSameLife(capture.Parent, capture.ParentLife)
                && BossNativeSameLife(capture.ParentActor, capture.ParentActorLife)
                && capture.Skill != null && capture.Skill.isActive && capture.Skill.owner == hero
                && BossNativeSameLife(capture.Skill, capture.SkillLife) && capture.Instance.firstTrigger == capture.Skill
                && capture.Parent.firstTrigger == capture.Skill && FindMemory(hero, nameof(St_U_Burrow)) == capture.Skill
                && BossAlive(hero) && _runtimes.TryGetValue(hero, out var rt) && rt == capture.Runtime
                && BossNativeContextCurrent(capture.Room, capture.Run) && AttributionGeneratedOrigin() == GeneratedOrigin.None;
        }
        private int AzurakBurrowStage(AzurakBurrowExit capture)
            => AzurakBurrowCurrent(capture) && BossEnsure(capture.Runtime) ? BossRewardStage(capture.Runtime, BossProfiles.AzurakRewardId) : 0;
        internal float AzurakBurrowDaze(Ai_U_Burrow_Emerge instance, float native)
        {
            return AzurakBurrowNativeCreate.Current == instance && _azurakBurrows.TryGetValue(instance, out var capture) && AzurakBurrowStage(capture) == 3
                && !float.IsNaN(native) && !float.IsInfinity(native) ? Math.Max(0, native - 0.05f) : native;
        }
        internal float AzurakBurrowStun(Ai_U_Burrow_Emerge instance, Entity target, float native)
        {
            var scope = AzurakBurrowNativeHit.Current;
            if (scope.Instance != instance || scope.Target != target || !_azurakBurrows.TryGetValue(instance, out var capture)
                || AzurakBurrowStage(capture) == 0 || !BossAlive(target) || target.GetRelation(capture.Runtime.Hero) != EntityRelation.Enemy
                || target.IsAnyBoss() || target.Status.hasCrowdControlImmunity || float.IsNaN(native) || float.IsInfinity(native)) return native;
            return native + Math.Min(0.15f, Math.Max(0, 1 - native));
        }
        private void AzurakBurrowFlat(AzurakBurrowExit capture, ref DamageData damage, Actor actor, Entity target)
        {
            var scope = AzurakBurrowNativeHit.Current;
            var packet = NativeAttributedDamagePacket.Current;
            if (actor != capture.Instance || scope.Instance != capture.Instance || scope.Target != target
                || packet == null || packet.Actor != capture.Instance || packet.Victim != target
                || !packet.Chain.Equals(default(ReactionChain)) || packet.Serial == capture.LastPacket
                || !BossAlive(target) || target.GetRelation(capture.Runtime.Hero) != EntityRelation.Enemy
                || IsBossGeneratedDamage(damage)) return;
            int stage = AzurakBurrowStage(capture);
            if (stage < 2 || capture.BudgetRejected) return;
            capture.LastPacket = packet.Serial;
            float nativeAmount = damage.currentAmount;
            float multiplier = damage.amplificationMultiplier * damage.reductionMultiplier;
            if (nativeAmount <= 0 || multiplier <= 0 || float.IsNaN(nativeAmount) || float.IsInfinity(nativeAmount)
                || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || damage.isBlockedByImmunity) return;
            float now = Time.time;
            var owner=AzurakOwner(capture.Runtime); if(owner==null) return;
            if (!capture.BudgetAdmitted)
            {
                if (now < owner.BurrowReady) { capture.BudgetRejected = true; return; }
                capture.BudgetAdmitted = true;
                owner.BurrowReady = now + 8;
            }
            float budget = capture.H * (stage == 3 ? 0.45f : 0.20f);
            float amount = Math.Min(nativeAmount * (stage == 3 ? 0.25f : 0.20f), Math.Max(0, budget - capture.Spent));
            float flat = amount / multiplier;
            if (amount <= 0 || float.IsNaN(flat) || float.IsInfinity(flat)) return;
            damage.AddFlatAmount(flat);
            capture.Spent += amount;
            // One packet and one original attack effect/crit, never a generated duplicate dispatch.
            PublishBossVisual(capture.Runtime, capture.Visual, 9, capture.Instance.position, capture.Instance.position, 0,
                now, now + 0.25f, budget: Math.Max(0, budget - capture.Spent), nativeLife: capture.InstanceLife);
        }
        internal void EndAzurakBurrow(Ai_U_Burrow_Emerge instance)
        {
            if (ReferenceEquals(instance, null) || !_azurakBurrows.TryGetValue(instance, out var capture)) return;
            _azurakBurrows.Remove(instance);
            if (instance != null && BossNativeSameLife(instance, capture.InstanceLife)) instance.dealtDamageProcessor.Remove(capture.Processor);
            PublishBossVisual(capture.Runtime, capture.Visual, 9, capture.Runtime.Hero.agentPosition, capture.Runtime.Hero.agentPosition, 0,
                Time.time, Time.time, true, nativeLife: capture.InstanceLife);
            capture.Reset(); _azurakBurrowPool.Return(capture);
        }
        private void TickAzurakBurrow(HeroRuntime rt)
        {
            _azurakBurrowExpired.Clear();
            foreach (var pair in _azurakBurrows)
                if (pair.Value.Runtime == rt && (!AzurakBurrowCurrent(pair.Value) || BossRewardStage(rt, BossProfiles.AzurakRewardId) == 0))
                    _azurakBurrowExpired.Add(pair.Key);
            foreach (var instance in _azurakBurrowExpired) EndAzurakBurrow(instance);
            _azurakBurrowExpired.Clear();
        }
        private void ClearAzurakBurrow(HeroRuntime rt)
        {
            _azurakBurrowExpired.Clear();
            foreach (var pair in _azurakBurrows) if (pair.Value.Runtime == rt) _azurakBurrowExpired.Add(pair.Key);
            foreach (var instance in _azurakBurrowExpired) EndAzurakBurrow(instance);
            _azurakBurrowExpired.Clear();
        }
    }
}

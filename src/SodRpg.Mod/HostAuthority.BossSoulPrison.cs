using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Dew.Contents 1.4.0.13: OnCreate registers b__3_1 with DoDeathInterrupt at IL_003a.
    // The quality multiplier is IL_0058; the sole rescue Dispatch is IL_006f.
    [HarmonyPatch(typeof(Se_Gem_U_SoulPrison_DeathInterrupt),"<OnCreate>b__3_1")]
    internal static class SeekerSoulRescueScope
    {
        internal sealed class Scope
        {
            internal Se_Gem_U_SoulPrison_DeathInterrupt Status;
            internal Gem_U_SoulPrison Gem;
            internal Hero Hero;
            internal long StatusLife, GemLife, HeroLife, SkillLife, ParentLife, Epoch;
            internal SkillTrigger Skill;
            internal Actor Parent;
            internal Room Room;
            internal string Run;
            internal int Depth;
            internal bool Applied, Completed;
        }
        internal static Scope Current;
        private static void Prefix(Se_Gem_U_SoulPrison_DeathInterrupt __instance,out Scope __state)
        {
            __state=Current; Current=null;
            if(NetworkServer.active) Current=HostAuthority.NativeInstance?.BeginSeekerSoulRescue(__instance);
        }
        private static void Finalizer(Scope __state) { Current=__state; }
    }
    [HarmonyPatch(typeof(Actor),nameof(Actor.DoHeal))]
    internal static class SeekerSoulHealDepth
    {
        private static void Prefix(out int __state) { var scope=SeekerSoulRescueScope.Current; __state=scope?.Depth??-1; if(__state>=0) scope.Depth++; }
        private static void Finalizer(int __state) { if(__state>=0 && SeekerSoulRescueScope.Current!=null) SeekerSoulRescueScope.Current.Depth=__state; }
    }
    [HarmonyPatch(typeof(Entity),nameof(Entity.ProcessReceivedHeal))]
    internal static class SeekerSoulFinalHeal
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Entity __instance,ref HealData data,Actor actor)
        {
            var scope=SeekerSoulRescueScope.Current;
            if(!NetworkServer.active || scope==null || scope.Applied || scope.Depth!=1 || scope.Status!=actor || data.actor!=actor || scope.Hero!=__instance) return;
            scope.Applied=true; HostAuthority.NativeInstance?.AmplifySeekerSoulHeal(scope,ref data);
        }
    }
    // Native b__3_0 keeps its full discardedAmount/infinite shield; additions run afterwards.
    [HarmonyPatch(typeof(Se_Gem_U_SoulPrison_DeathInterrupt),"<OnCreate>b__3_0")]
    internal static class SeekerSoulOverflow
    {
        private static void Postfix(Se_Gem_U_SoulPrison_DeathInterrupt __instance,EventInfoHeal heal)
        {
            var scope=SeekerSoulRescueScope.Current;
            if(!NetworkServer.active || scope==null || scope.Completed || !scope.Applied || scope.Depth!=1 || scope.Status!=__instance || heal.actor!=__instance || heal.target!=scope.Hero) return;
            scope.Completed=true; HostAuthority.NativeInstance?.CompleteSeekerSoulHeal(scope,heal);
        }
    }
    // b__3_3 is the native delayed consumption callback. IL_002d UnequipGem, IL_0039 gem.Destroy.
    [HarmonyPatch(typeof(Se_Gem_U_SoulPrison_DeathInterrupt),"<OnCreate>b__3_3")]
    internal static class SeekerSoulConsumption
    {
        internal static Se_Gem_U_SoulPrison_DeathInterrupt Current;
        private static void Prefix(Se_Gem_U_SoulPrison_DeathInterrupt __instance,out Se_Gem_U_SoulPrison_DeathInterrupt __state) { __state=Current; Current=NetworkServer.active?__instance:null; }
        private static void Finalizer(Se_Gem_U_SoulPrison_DeathInterrupt __state) { Current=__state; }
    }
    [HarmonyPatch(typeof(HeroSkill),nameof(HeroSkill.UnequipGem))]
    internal static class SeekerSoulUnequip
    {
        private static void Postfix(Gem __result)
        {
            if(NetworkServer.active && __result is Gem_U_SoulPrison gem)
                HostAuthority.NativeInstance?.SeekerSoulUnequipped(gem,SeekerSoulConsumption.Current);
        }
    }
    internal sealed partial class HostAuthority
    {
        private sealed class SeekerSoulShield
        {
            internal Hero Target;
            internal StatusEffect Container;
            internal ShieldEffect Shield;
            internal long TargetLife, Life, Visual;
            internal float Until;
        }
        private sealed class SeekerSoulState
        {
            internal SeekerSoulRescueScope.Scope Scope;
            internal readonly List<SeekerSoulShield> Shields=new List<SeekerSoulShield>(2);
            internal readonly List<BossMoveEntry> Moves=new List<BossMoveEntry>();
            internal long Epoch;
            internal bool Consumed, ConsumptionReconcile;
        }
        private readonly Dictionary<HeroRuntime,SeekerSoulState> _seekerSoulStates=new Dictionary<HeroRuntime,SeekerSoulState>();
        private static bool SeekerSoulFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal SeekerSoulRescueScope.Scope BeginSeekerSoulRescue(Se_Gem_U_SoulPrison_DeathInterrupt status)
        {
            if(!NetworkServer.active || status==null || !status.isActive || !(status.gem is Gem_U_SoulPrison gem) || !(status.victim is Hero hero) || status.info.caster!=hero || status.parentActor!=gem || gem.owner!=hero || gem.skill!=null && !BossNativeEquippedSkill(hero,gem.skill) || !_runtimes.TryGetValue(hero,out var rt) || AttributionGeneratedOrigin()!=GeneratedOrigin.None) return null;
            if(hero.Skill==null || !hero.Skill.TryGetGemLocation(gem,out _)) return null;
            return new SeekerSoulRescueScope.Scope{Status=status,Gem=gem,Hero=hero,Skill=gem.skill,Parent=status.parentActor,StatusLife=BossNativeActorLife(status),GemLife=BossNativeActorLife(gem),HeroLife=BossNativeActorLife(hero),SkillLife=gem.skill!=null?BossNativeActorLife(gem.skill):0,ParentLife=BossNativeActorLife(status.parentActor),Epoch=rt.ShieldEquipmentEpoch,Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,Run=NetworkedManagerBase<GameManager>.softInstance?.runId};
        }
        private bool SeekerSoulCurrent(SeekerSoulRescueScope.Scope scope,out HeroRuntime rt)
        {
            rt=null;
            return scope!=null && BossAlive(scope.Hero) && scope.Status!=null && scope.Status.isActive && scope.Gem!=null && scope.Gem.isActive && scope.Status.gem==scope.Gem && scope.Status.victim==scope.Hero && scope.Status.info.caster==scope.Hero && scope.Status.parentActor==scope.Parent && scope.Parent==scope.Gem && scope.Gem.owner==scope.Hero && scope.Gem.skill==scope.Skill && (scope.Skill==null || BossNativeEquippedSkill(scope.Hero,scope.Skill) && BossNativeSameLife(scope.Skill,scope.SkillLife))
                && BossNativeSameLife(scope.Status,scope.StatusLife) && BossNativeSameLife(scope.Gem,scope.GemLife) && BossNativeSameLife(scope.Hero,scope.HeroLife) && BossNativeSameLife(scope.Parent,scope.ParentLife) && BossNativeContextCurrent(scope.Room,scope.Run) && _runtimes.TryGetValue(scope.Hero,out rt) && rt.ShieldEquipmentEpoch==scope.Epoch && scope.Hero.Skill.TryGetGemLocation(scope.Gem,out _) && BossEnsure(rt) && BossRewardStage(rt,BossProfiles.SeekerRewardId)>0;
        }
        internal void AmplifySeekerSoulHeal(SeekerSoulRescueScope.Scope scope,ref HealData data)
        {
            if(!SeekerSoulCurrent(scope,out var rt) || AttributionGeneratedOrigin()!=GeneratedOrigin.None) return;
            float original=data.currentAmount; float multiplier=data.amplificationMultiplier*data.reductionMultiplier;
            if(!SeekerSoulFinite(original) || original<=0 || !SeekerSoulFinite(multiplier) || multiplier<=0) return;
            float h=Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower);
            float bonus=Math.Min(original*.10f,h*.20f);
            if(SeekerSoulFinite(bonus) && bonus>0) data=data.AddAmount(bonus/multiplier);
        }
        internal void CompleteSeekerSoulHeal(SeekerSoulRescueScope.Scope scope,EventInfoHeal heal)
        {
            if(!SeekerSoulCurrent(scope,out var rt) || !SeekerSoulFinite(heal.discardedAmount) || heal.discardedAmount<=0) return;
            int stage=BossRewardStage(rt,BossProfiles.SeekerRewardId); if(stage<2) return;
            ClearSeekerSoulPrison(rt,false);
            var state=new SeekerSoulState{Scope=scope,Epoch=rt.ShieldEquipmentEpoch};
            foreach(var move in rt.Powers.Build.BossMoves) state.Moves.Add(move);
            _seekerSoulStates.Add(rt,state);
            float h=Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower);
            SeekerSoulGiveShield(rt,state,rt.Hero,Math.Min(heal.discardedAmount*.25f,h*.30f),6);
            if(stage<3) return;
            Hero ally=null; float best=36;
            ListReturnHandle<Entity> handle; var entities=DewPhysics.OverlapCircleAllEntities(out handle,rt.Hero.agentPosition,6);
            try { foreach(var entity in entities) if(entity is Hero target && target!=rt.Hero && BossAlive(target) && !rt.Hero.CheckEnemyOrNeutral(target)) { float d=(target.agentPosition-rt.Hero.agentPosition).Flattened().sqrMagnitude; if(d<=best && (d<best || ally==null || target.netId<ally.netId)) { ally=target; best=d; } } }
            finally { handle.Return(); }
            if(ally!=null) SeekerSoulGiveShield(rt,state,ally,Math.Min(heal.discardedAmount*.25f,h*.40f),3);
        }
        private void SeekerSoulGiveShield(HeroRuntime rt,SeekerSoulState state,Hero target,float amount,float duration)
        {
            if(!SeekerSoulFinite(amount) || amount<=0) return;
            Se_GenericShield_OneShot container;
            EnterGenerated(rt.Hero); try { container=rt.Hero.GiveShield(target,amount,duration); } finally { ExitGenerated(rt.Hero); }
            if(container==null) return;
            var shield=new SeekerSoulShield{Target=target,TargetLife=BossNativeActorLife(target),Container=container,Shield=container.shield,Life=BossNativeActorLife(container),Until=Time.time+duration,Visual=++rt.Boss.NextId}; state.Shields.Add(shield);
            PublishBossVisual(rt,shield.Visual,7,target.agentPosition,target.agentPosition,.6f,Time.time,shield.Until,targetNetId:target.netId,nativeLife:state.Scope.StatusLife);
        }
        internal void SeekerSoulUnequipped(Gem_U_SoulPrison gem,Se_Gem_U_SoulPrison_DeathInterrupt consumption)
        {
            HeroRuntime rt=null; SeekerSoulState state=null;
            foreach(var pair in _seekerSoulStates) if(pair.Value.Scope.Gem==gem) { rt=pair.Key; state=pair.Value; break; }
            if(state==null) return;
            if(consumption==state.Scope.Status && BossNativeSameLife(consumption,state.Scope.StatusLife) && BossNativeSameLife(gem,state.Scope.GemLife) && BossNativeContextCurrent(state.Scope.Room,state.Scope.Run)) { state.Consumed=true; state.ConsumptionReconcile=true; return; }
            ClearSeekerSoulPrison(rt,false);
        }
        private static bool SeekerSoulMovesMatch(HeroRuntime rt,SeekerSoulState state)
        {
            var moves=rt.Powers.Build?.BossMoves; if(moves==null || moves.Count!=state.Moves.Count) return false;
            for(int i=0;i<moves.Count;i++) { var a=moves[i]; var b=state.Moves[i]; if(a.SetId!=b.SetId || a.ProfileId!=b.ProfileId || a.Channels.Count!=b.Channels.Count) return false; for(int j=0;j<a.Channels.Count;j++) if(a.Channels[j].ChannelId!=b.Channels[j].ChannelId || a.Channels[j].ValueMilli!=b.Channels[j].ValueMilli) return false; }
            return true;
        }
        private bool SeekerSoulShieldOwned(SeekerSoulShield shield) => shield.Container!=null && shield.Container.isActive && shield.Container.victim==shield.Target && BossNativeSameLife(shield.Target,shield.TargetLife) && BossNativeSameLife(shield.Container,shield.Life) && shield.Container is Se_GenericShield_OneShot one && one.shield==shield.Shield;
        private void SeekerSoulRemoveShield(HeroRuntime rt,SeekerSoulShield shield)
        {
            if(SeekerSoulShieldOwned(shield)) shield.Container.Destroy();
            PublishBossVisual(rt,shield.Visual,7,shield.Target!=null?shield.Target.agentPosition:Vector3.zero,Vector3.zero,0,Time.time,Time.time,true);
        }
        private void TickSeekerSoulPrison(HeroRuntime rt,float now)
        {
            if(!_seekerSoulStates.TryGetValue(rt,out var state)) return;
            if(!BossAlive(rt.Hero) || !BossNativeSameLife(rt.Hero,state.Scope.HeroLife) || !BossNativeContextCurrent(state.Scope.Room,state.Scope.Run) || !SeekerSoulMovesMatch(rt,state) || state.Epoch!=rt.ShieldEquipmentEpoch || !state.Consumed && !SeekerSoulCurrent(state.Scope,out _)) { ClearSeekerSoulPrison(rt,false); return; }
            for(int i=state.Shields.Count-1;i>=0;i--) if(now>=state.Shields[i].Until || !BossAlive(state.Shields[i].Target) || !SeekerSoulShieldOwned(state.Shields[i])) { SeekerSoulRemoveShield(rt,state.Shields[i]); state.Shields.RemoveAt(i); }
            if(state.Shields.Count==0) _seekerSoulStates.Remove(rt);
        }
        private void ClearSeekerSoulPrison(HeroRuntime rt,bool preserveRewards)
        {
            if(!_seekerSoulStates.TryGetValue(rt,out var state)) return;
            if(preserveRewards && state.Consumed && BossAlive(rt.Hero) && BossNativeContextCurrent(state.Scope.Room,state.Scope.Run) && SeekerSoulMovesMatch(rt,state) && (state.Epoch==rt.ShieldEquipmentEpoch || state.ConsumptionReconcile)) { state.Epoch=rt.ShieldEquipmentEpoch; state.ConsumptionReconcile=false; return; }
            foreach(var shield in state.Shields) SeekerSoulRemoveShield(rt,shield);
            _seekerSoulStates.Remove(rt);
        }
    }
}

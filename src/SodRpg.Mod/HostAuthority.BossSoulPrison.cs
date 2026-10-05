using System;
using System.Collections.Generic;
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
        internal struct Scope
        {
            internal Se_Gem_U_SoulPrison_DeathInterrupt Status;
            internal Gem_U_SoulPrison Gem;
            internal Hero Hero;
            internal long StatusLife, GemLife, HeroLife, SkillLife, ParentLife;
            internal SkillTrigger Skill;
            internal GemLocation Location;
            internal Actor Parent;
            internal Room Room;
            internal string Run;
            internal int Depth;
            internal bool Applied, Completed;
        }
        internal static Scope Current;
        private static void Prefix(Se_Gem_U_SoulPrison_DeathInterrupt __instance,out Scope __state)
        {
            __state=Current; Current=default;
            if(NetworkServer.active && HostAuthority.NativeInstance!=null) Current=HostAuthority.NativeInstance.BeginSeekerSoulRescue(__instance);
        }
        private static void Finalizer(Scope __state) { Current=__state; }
    }
    [HarmonyPatch(typeof(Actor),nameof(Actor.DoHeal))]
    internal static class SeekerSoulHealDepth
    {
        private static void Prefix(out int __state) { ref var scope=ref SeekerSoulRescueScope.Current; __state=scope.Status!=null?scope.Depth:-1; if(__state>=0) scope.Depth++; }
        private static void Finalizer(int __state) { if(__state>=0 && SeekerSoulRescueScope.Current.Status!=null) SeekerSoulRescueScope.Current.Depth=__state; }
    }
    [HarmonyPatch(typeof(Entity),nameof(Entity.ProcessReceivedHeal))]
    internal static class SeekerSoulFinalHeal
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Entity __instance,ref HealData data,Actor actor)
        {
            ref var scope=ref SeekerSoulRescueScope.Current;
            if(!NetworkServer.active || scope.Status==null || scope.Applied || scope.Depth!=1 || scope.Status!=actor || data.actor!=actor || scope.Hero!=__instance) return;
            scope.Applied=true; HostAuthority.NativeInstance?.AmplifySeekerSoulHeal(scope,ref data);
        }
    }
    // Native b__3_0 keeps its full discardedAmount/infinite shield; additions run afterwards.
    [HarmonyPatch(typeof(Se_Gem_U_SoulPrison_DeathInterrupt),"<OnCreate>b__3_0")]
    internal static class SeekerSoulOverflow
    {
        private static void Postfix(Se_Gem_U_SoulPrison_DeathInterrupt __instance,EventInfoHeal heal)
        {
            ref var scope=ref SeekerSoulRescueScope.Current;
            if(!NetworkServer.active || scope.Status==null || scope.Completed || !scope.Applied || scope.Depth!=1 || scope.Status!=__instance || heal.actor!=__instance || heal.target!=scope.Hero) return;
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
    // Creation scope is independent of the pooled reward state: native support
    // callbacks may clear/rent that state before CreateStatusEffect returns.
    internal static class SeekerSoulShieldCreation
    {
        internal struct Scope
        {
            internal float Amount, Duration;
            internal Se_GenericShield_OneShot Container;
        }
        internal static Scope Current;
        internal static readonly Action<Se_GenericShield_OneShot> Prepare = PrepareOwnedShield;
        private static void PrepareOwnedShield(Se_GenericShield_OneShot shield)
        {
            Current.Container=shield;
            shield.chain=default; shield.initAmount=Current.Amount; shield.isDecay=false;
            shield.SetTimer(Current.Duration);
            NativeModShieldCreationCap.Register(shield,Current.Amount);
        }
    }
    internal sealed partial class HostAuthority
    {
        private struct SeekerSoulShield
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
            internal readonly SeekerSoulShield[] Shields=new SeekerSoulShield[2];
            internal int ShieldCount;
            internal long Lease;
            internal bool Consumed;
            internal void Reset()
            {
                Scope=default; Consumed=false; ShieldCount=0;
                Array.Clear(Shields,0,Shields.Length);
            }
        }
        private readonly Dictionary<HeroRuntime,SeekerSoulState> _seekerSoulStates=new Dictionary<HeroRuntime,SeekerSoulState>(64);
        private readonly BossObjectPool<SeekerSoulState> _seekerSoulPool=new BossObjectPool<SeekerSoulState>(64,()=>new SeekerSoulState());
        private static bool SeekerSoulFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal SeekerSoulRescueScope.Scope BeginSeekerSoulRescue(Se_Gem_U_SoulPrison_DeathInterrupt status)
        {
            if(!NetworkServer.active || status==null || !status.isActive || !(status.gem is Gem_U_SoulPrison gem) || !(status.victim is Hero hero) || status.info.caster!=hero || status.parentActor!=gem || gem.owner!=hero || gem.skill!=null && !BossNativeEquippedSkill(hero,gem.skill) || !_runtimes.ContainsKey(hero) || AttributionGeneratedOrigin()!=GeneratedOrigin.None) return default;
            if(hero.Skill==null || !hero.Skill.gems.TryGetValue(gem.location,out var equipped) || !ReferenceEquals(equipped,gem)) return default;
            return new SeekerSoulRescueScope.Scope{Status=status,Gem=gem,Hero=hero,Skill=gem.skill,Location=gem.location,Parent=status.parentActor,StatusLife=BossNativeActorLife(status),GemLife=BossNativeActorLife(gem),HeroLife=BossNativeActorLife(hero),SkillLife=gem.skill!=null?BossNativeActorLife(gem.skill):0,ParentLife=BossNativeActorLife(status.parentActor),Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,Run=NetworkedManagerBase<GameManager>.softInstance?.runId};
        }
        private bool SeekerSoulCurrent(SeekerSoulRescueScope.Scope scope,out HeroRuntime rt)
        {
            rt=null;
            return BossAlive(scope.Hero) && scope.Status!=null && scope.Status.isActive && scope.Gem!=null && scope.Gem.isActive && scope.Status.gem==scope.Gem && scope.Status.victim==scope.Hero && scope.Status.info.caster==scope.Hero && scope.Status.parentActor==scope.Parent && scope.Parent==scope.Gem && scope.Gem.owner==scope.Hero && scope.Gem.skill==scope.Skill && (scope.Skill==null || BossNativeEquippedSkill(scope.Hero,scope.Skill) && BossNativeSameLife(scope.Skill,scope.SkillLife))
                && BossNativeSameLife(scope.Status,scope.StatusLife) && BossNativeSameLife(scope.Gem,scope.GemLife) && BossNativeSameLife(scope.Hero,scope.HeroLife) && BossNativeSameLife(scope.Parent,scope.ParentLife) && BossNativeContextCurrent(scope.Room,scope.Run) && _runtimes.TryGetValue(scope.Hero,out rt) && scope.Hero.Skill!=null && scope.Hero.Skill.gems.TryGetValue(scope.Gem.location,out var equipped) && ReferenceEquals(equipped,scope.Gem) && BossEnsure(rt) && BossRewardStage(rt,BossProfiles.SeekerRewardId)>0;
        }
        private bool SeekerSoulConsumedCurrent(SeekerSoulRescueScope.Scope scope)
        {
            // Native consumption may destroy/recycle the old gem/status, but a
            // replacement at its slot or replacement of its memory ends ownership.
            return scope.Hero.Skill!=null && !scope.Hero.Skill.gems.ContainsKey(scope.Location)
                && (scope.SkillLife==0 || scope.Skill!=null && BossNativeEquippedSkill(scope.Hero,scope.Skill) && BossNativeSameLife(scope.Skill,scope.SkillLife));
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
            if(_seekerSoulStates.Count>=64) return;
            var state=_seekerSoulPool.Rent(); if(state==null) return;
            state.Lease++; state.Scope=scope;
            _seekerSoulStates.Add(rt,state);
            long lease=state.Lease;
            float h=Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower);
            SeekerSoulGiveShield(rt,state,rt.Hero,Math.Min(heal.discardedAmount*.25f,h*.30f),6);
            if(stage<3 || state.Lease!=lease || !_seekerSoulStates.TryGetValue(rt,out var current) || !ReferenceEquals(current,state)) return;
            Hero ally=null; float best=36;
            ListReturnHandle<Entity> handle; var entities=DewPhysics.OverlapCircleAllEntities(out handle,rt.Hero.agentPosition,6);
            try
            {
                for(int i=0,limit=Math.Min(256,entities.Count);i<limit;i++)
                {
                    if(!(entities[i] is Hero target) || target==rt.Hero || !BossAlive(target) || rt.Hero.CheckEnemyOrNeutral(target)) continue;
                    float d=(target.agentPosition-rt.Hero.agentPosition).Flattened().sqrMagnitude;
                    if(d<=best && (d<best || ally==null || target.netId<ally.netId)) { ally=target; best=d; }
                }
            }
            finally { handle.Return(); }
            if(ally!=null) SeekerSoulGiveShield(rt,state,ally,Math.Min(heal.discardedAmount*.25f,h*.40f),3);
        }
        private void SeekerSoulGiveShield(HeroRuntime rt,SeekerSoulState state,Hero target,float amount,float duration)
        {
            if(!SeekerSoulFinite(amount) || amount<=0 || state.ShieldCount>=state.Shields.Length) return;
            long lease=state.Lease;
            Se_GenericShield_OneShot container;
            // Exact native construction, with a cached preparation delegate and an
            // exact-handle cap at ProcessShieldAmount's fully processed return.
            var previous=SeekerSoulShieldCreation.Current;
            SeekerSoulShieldCreation.Current=new SeekerSoulShieldCreation.Scope{Amount=amount,Duration=duration};
            EnterGenerated(rt.Hero);
            try { container=rt.Hero.CreateStatusEffect<Se_GenericShield_OneShot>(target,new CastInfo(target),SeekerSoulShieldCreation.Prepare); }
            finally
            {
                if(!ReferenceEquals(SeekerSoulShieldCreation.Current.Container,null))
                    NativeModShieldCreationCap.Remove(SeekerSoulShieldCreation.Current.Container);
                SeekerSoulShieldCreation.Current=previous;
                ExitGenerated(rt.Hero);
            }
            if(container==null) return;
            if(state.Lease!=lease || !_seekerSoulStates.TryGetValue(rt,out var current) || !ReferenceEquals(current,state))
            { container.Destroy(); return; }
            var shield=new SeekerSoulShield{Target=target,TargetLife=BossNativeActorLife(target),Container=container,Shield=container.shield,Life=BossNativeActorLife(container),Until=Time.time+duration,Visual=++rt.Boss.NextId};
            state.Shields[state.ShieldCount++]=shield;
            PublishBossVisual(rt,shield.Visual,7,target.agentPosition,target.agentPosition,.6f,Time.time,shield.Until,targetNetId:target.netId,nativeLife:state.Scope.StatusLife);
        }
        internal void SeekerSoulUnequipped(Gem_U_SoulPrison gem,Se_Gem_U_SoulPrison_DeathInterrupt consumption)
        {
            HeroRuntime rt=null; SeekerSoulState state=null;
            foreach(var pair in _seekerSoulStates) if(pair.Value.Scope.Gem==gem) { rt=pair.Key; state=pair.Value; break; }
            if(state==null) return;
            if(consumption==state.Scope.Status && BossNativeSameLife(consumption,state.Scope.StatusLife) && BossNativeSameLife(gem,state.Scope.GemLife) && BossNativeContextCurrent(state.Scope.Room,state.Scope.Run)) { state.Consumed=true; return; }
            ClearSeekerSoulPrison(rt,false);
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
            long lease=state.Lease;
            if(!BossAlive(rt.Hero) || !BossNativeSameLife(rt.Hero,state.Scope.HeroLife) || !BossNativeContextCurrent(state.Scope.Room,state.Scope.Run)
                || !(state.Consumed ? SeekerSoulConsumedCurrent(state.Scope) : SeekerSoulCurrent(state.Scope,out _))) { ClearSeekerSoulPrison(rt,false); return; }
            for(int i=state.ShieldCount-1;i>=0;i--) if(now>=state.Shields[i].Until || !BossAlive(state.Shields[i].Target) || !SeekerSoulShieldOwned(state.Shields[i]))
            {
                var shield=state.Shields[i];
                state.Shields[i]=state.Shields[--state.ShieldCount]; state.Shields[state.ShieldCount]=default;
                SeekerSoulRemoveShield(rt,shield);
                if(state.Lease!=lease || !_seekerSoulStates.TryGetValue(rt,out var current) || !ReferenceEquals(current,state)) return;
            }
            if(state.ShieldCount==0) { _seekerSoulStates.Remove(rt); state.Reset(); _seekerSoulPool.Return(state); }
        }
        private void ClearSeekerSoulPrison(HeroRuntime rt,bool preserveRewards)
        {
            if(!_seekerSoulStates.TryGetValue(rt,out var state)) return;
            if(preserveRewards && BossAlive(rt.Hero) && BossNativeSameLife(rt.Hero,state.Scope.HeroLife)
                && BossNativeContextCurrent(state.Scope.Room,state.Scope.Run)
                && (state.Consumed ? SeekerSoulConsumedCurrent(state.Scope) : SeekerSoulCurrent(state.Scope,out _)))
                return;
            _seekerSoulStates.Remove(rt);
            for(int i=0;i<state.ShieldCount;i++) SeekerSoulRemoveShield(rt,state.Shields[i]);
            state.Reset(); _seekerSoulPool.Return(state);
        }
    }
}

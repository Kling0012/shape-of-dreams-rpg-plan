using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Dew.Contents 1.4.0.13 <OnCreateSequenced>d__17.MoveNext has exactly two
    // SI waits: delay at IL_0056/005b, duration at IL_00a4/00a9. Keep the native
    // iterator and all of its FX/flag/destroy instructions; adapt only its waits.
    [HarmonyPatch(typeof(Ai_Gem_U_LastStarlight),"OnCreateSequenced")]
    internal static class ErebosLastStarlightSequence
    {
        private static void Prefix(Ai_Gem_U_LastStarlight __instance,out object __state)
            => __state=NetworkServer.active?HostAuthority.NativeInstance?.CaptureLastStarlight(__instance):null;
        private static void Postfix(Ai_Gem_U_LastStarlight __instance,ref IEnumerator __result,object __state)
        {
            if (__state!=null) __result=HostAuthority.NativeInstance.WrapLastStarlight(__result,__state);
        }
    }
    [HarmonyPatch(typeof(Actor),"InvokeOnDestroyActorIfDidnt")]
    internal static class ErebosLastStarlightDestroy
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Actor __instance)
        {
            if (NetworkServer.active && __instance is Ai_Gem_U_LastStarlight instance)
                HostAuthority.NativeInstance?.ReleaseLastStarlight(instance);
        }
    }
    [HarmonyPatch(typeof(Ai_Gem_U_LastStarlight),"OnDisable")]
    internal static class ErebosLastStarlightDisable
    {
        private static void Prefix(Ai_Gem_U_LastStarlight __instance)
        { if (NetworkServer.active) HostAuthority.NativeInstance?.ReleaseLastStarlight(__instance); }
    }
    [HarmonyPatch(typeof(Ai_Gem_U_LastStarlight),"ActiveLogicUpdate")]
    internal static class ErebosLastStarlightUpdate
    {
        private static void Prefix(Ai_Gem_U_LastStarlight __instance)
        { if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshLastStarlight(__instance); }
    }
    [HarmonyPatch(typeof(Gem_U_LastStarlight),nameof(Gem_U_LastStarlight.OnUnequipSkill))]
    internal static class ErebosLastStarlightUnequip
    {
        private static void Prefix(Gem_U_LastStarlight __instance)
        { if (NetworkServer.active) HostAuthority.NativeInstance?.UnequipLastStarlight(__instance); }
    }
    internal sealed partial class HostAuthority
    {
        private sealed class LastStarlightState
        {
            internal Ai_Gem_U_LastStarlight Instance;
            internal Gem_U_LastStarlight Gem;
            internal SkillTrigger Skill;
            internal HeroRuntime Runtime;
            internal Hero Owner;
            internal Room Room;
            internal string Run;
            internal long Life,GemLife,SkillLife,OwnerLife,Epoch,Visual;
            internal float Creation,Delay,Duration,Attraction,TickRadius,DelayDelta,DurationDelta,AttractionDelta,TickDelta;
            internal float WaitStart,LastReconcile = float.NegativeInfinity;
            internal Vector3 OriginalCenter,Center;
            internal bool Active,Relocated,Retired,Waiting,Detached;
            internal int Stage;
            internal HostAuthority Host;
            internal object Build;
            internal readonly LastStarlightSequence Sequence;
            internal LastStarlightState() { Sequence=new LastStarlightSequence(this); }
            internal void Reset()
            {
                Instance=null; Gem=null; Skill=null; Runtime=null; Owner=null; Room=null; Run=null; Host=null; Build=null;
                Life=GemLife=SkillLife=OwnerLife=Epoch=Visual=0;
                Creation=Delay=Duration=Attraction=TickRadius=DelayDelta=DurationDelta=AttractionDelta=TickDelta=WaitStart=0;
                LastReconcile=float.NegativeInfinity; OriginalCenter=Center=default;
                Active=Relocated=Retired=Waiting=Detached=false; Stage=0;
            }
        }
        private sealed class LastStarlightSequence : IEnumerator,IDisposable
        {
            private HostAuthority _host;
            private IEnumerator _native;
            private readonly LastStarlightState _state;
            private readonly object _wait;
            private int _waits;
            private bool _moving,_disposed,_passthrough;
            private object _current;
            internal bool Bound => _native!=null;
            internal LastStarlightSequence(LastStarlightState state)
            { _state=state; _wait=new SI.WaitForCondition(WaitFinished); }
            internal void Bind(HostAuthority host,IEnumerator native)
            { _host=host; _native=native; _waits=0; _moving=_disposed=_passthrough=false; _current=null; }
            public object Current => _current;
            public bool MoveNext()
            {
                if (_native==null) return false;
                if (!_passthrough && !_host.BossNativeSameLife(_state.Instance,_state.Life)) { Complete(); return false; }
                _moving=true;
                bool result=false;
                try
                {
                    result=_native.MoveNext();
                    if(_disposed || !result) return false;
                    _current=_native.Current;
                    if (_passthrough || !(_current is SI.WaitForSeconds)) return true;
                    if (++_waits>2)
                    {
                        // Detach only our adapter, not the running native iterator. Keep this
                        // wait and all subsequent yields/completion exactly as the native emits them.
                        _passthrough=true;
                        _state.Waiting=false;
                        _host.ReleaseLastStarlight(_state.Instance);
                        Log.Warn("LastStarlight wait adaptation disabled: unexpected native wait; passing through the remaining sequence.");
                        return true;
                    }
                    _state.Active=_waits==2; _state.WaitStart=Time.time; _state.Waiting=true;
                    _host.ReconcileLastStarlight(_state,Time.time);
                    _current=_wait;
                    _host.LastStarlightVisual(_state,Time.time);
                    return true;
                }
                catch { _disposed=true; throw; }
                finally
                {
                    _moving=false;
                    if(_disposed || !result) Complete();
                }
            }
            private bool WaitFinished()
            {
                if (_passthrough || _native==null || _disposed || !_host.BossNativeSameLife(_state.Instance,_state.Life)) return true;
                _host.ReconcileLastStarlight(_state,Time.time);
                float seconds=_state.Active?_state.Duration+_state.DurationDelta:_state.Delay+_state.DelayDelta;
                return Time.time>=_state.WaitStart+Math.Max(0,seconds);
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                if(_native==null) return;
                _disposed=true;
                if(!_moving) Complete();
            }
            private void Complete()
            {
                if(_native==null) return;
                var native=_native; var host=_host;
                _native=null; _host=null; _current=null; _state.Waiting=false;
                try { (native as IDisposable)?.Dispose(); }
                finally { host.FinishLastStarlightSequence(_state); }
            }
        }
        private readonly Dictionary<Ai_Gem_U_LastStarlight,LastStarlightState> _lastStarlights = new Dictionary<Ai_Gem_U_LastStarlight,LastStarlightState>(128);
        private readonly BossObjectPool<LastStarlightState> _lastStarlightPool = new BossObjectPool<LastStarlightState>(128,()=>new LastStarlightState());
        private readonly List<Ai_Gem_U_LastStarlight> _lastStarlightScratch = new List<Ai_Gem_U_LastStarlight>(128);
        internal object CaptureLastStarlight(Ai_Gem_U_LastStarlight instance)
        {
            if (!NetworkServer.active || instance==null || AttributionGeneratedOrigin()!=GeneratedOrigin.None
                || !(instance.gem is Gem_U_LastStarlight gem) || instance.parentActor!=gem || !gem.isValid || !gem.isActive
                || !(gem.owner is Hero hero) || !BossAlive(hero) || instance.info.caster!=hero
                || gem.skill==null || !gem.skill.isActive || gem.skill.owner!=hero
                || !_runtimes.TryGetValue(hero,out var rt)
                || !LastStarlightEquipped(hero,gem,gem.skill)
                || !BossEnsure(rt) || BossRewardStage(rt,BossProfiles.ErebosRewardId)<=0) return null;
            if (_lastStarlights.ContainsKey(instance)) ReleaseLastStarlight(instance);
            if (_lastStarlights.Count>=128) return null;
            var state=_lastStarlightPool.Rent(); if(state==null) return null;
            state.Host=this; state.Instance=instance; state.Gem=gem; state.Skill=gem.skill; state.Owner=hero; state.Runtime=rt;
            state.Life=BossNativeActorLife(instance); state.GemLife=BossNativeActorLife(gem);
            state.SkillLife=BossNativeActorLife(gem.skill); state.OwnerLife=BossNativeActorLife(hero);
            state.Epoch=rt.ShieldEquipmentEpoch; state.Creation=instance.creationTime;
            state.Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom; state.Run=NetworkedManagerBase<GameManager>.softInstance?.runId;
            state.Delay=instance.delay; state.Duration=instance.duration; state.Attraction=instance.attractionRadius;
            state.TickRadius=instance.tickDamageRadius; state.OriginalCenter=state.Center=instance.info.point;
            if (!BossNativeContextCurrent(state.Room,state.Run)) { state.Reset(); _lastStarlightPool.Return(state); return null; }
            _lastStarlights.Add(instance,state);
            ReconcileLastStarlight(state,Time.time);
            return state;
        }
        internal IEnumerator WrapLastStarlight(IEnumerator native,object capture)
        {
            var state=(LastStarlightState)capture;
            if(native==null) { ReleaseLastStarlight(state.Instance); return null; }
            state.Sequence.Bind(this,native); return state.Sequence;
        }
        private void FinishLastStarlightSequence(LastStarlightState state)
        {
            if(!state.Detached && _lastStarlights.TryGetValue(state.Instance,out var current) && ReferenceEquals(current,state))
                ReleaseLastStarlight(state.Instance);
            else { state.Reset(); _lastStarlightPool.Return(state); }
        }
        private static bool LastStarlightEquipped(Hero hero,Gem_U_LastStarlight gem,SkillTrigger skill)
        {
            if (hero.Skill==null || gem.skill!=skill || skill.owner!=hero) return false;
            return hero.Skill.gems.TryGetValue(gem.location,out var equipped) && ReferenceEquals(equipped,gem);
        }
        private bool LastStarlightCurrent(LastStarlightState state)
        {
            return !state.Retired && NetworkServer.active && state.Instance!=null && state.Instance.isActive
                && state.Instance.creationTime==state.Creation && BossNativeSameLife(state.Instance,state.Life)
                && BossNativeSameLife(state.Gem,state.GemLife) && BossNativeSameLife(state.Skill,state.SkillLife)
                && BossNativeSameLife(state.Owner,state.OwnerLife) && BossAlive(state.Owner)
                && state.Instance.info.caster==state.Owner && state.Instance.gem==state.Gem && state.Gem.isValid && state.Gem.isActive
                && state.Skill.isActive && LastStarlightEquipped(state.Owner,state.Gem,state.Skill)
                && BossNativeContextCurrent(state.Room,state.Run) && _runtimes.TryGetValue(state.Owner,out var rt) && rt==state.Runtime;
        }
        private void ReconcileLastStarlight(LastStarlightState state,float now)
        {
            if (!BossNativeSameLife(state.Instance,state.Life)) return;
            if(!state.Retired && state.LastReconcile==now && state.Epoch==state.Runtime.ShieldEquipmentEpoch && ReferenceEquals(state.Build,state.Runtime.Powers.Build)) return;
            state.LastReconcile=now;
            bool current=LastStarlightCurrent(state);
            int stage=current && BossEnsure(state.Runtime)?BossRewardStage(state.Runtime,BossProfiles.ErebosRewardId):0;
            float delayDelta=stage>0?-Math.Min(PowersBalance.LastStarlightDelayReductionMax,Math.Max(0,state.Delay-PowersBalance.LastStarlightDelayFloor)):0;
            float attractionDelta=stage>=2?Math.Min(PowersBalance.LastStarlightAttractionBonusMax,Math.Max(0,PowersBalance.LastStarlightAttractionCap-state.Attraction)):0;
            float tickDelta=stage>=2?Math.Min(PowersBalance.LastStarlightTickRadiusBonusMax,Math.Max(0,PowersBalance.LastStarlightTickRadiusCap-state.TickRadius)):0;
            float durationDelta=stage>=3?Math.Min(PowersBalance.LastStarlightDurationBonusMax,Math.Max(0,PowersBalance.LastStarlightDurationCap-state.Duration)):0;
            // Preserve any external/native write and remove only this adapter's own difference.
            state.Instance.delay+=delayDelta-state.DelayDelta;
            state.Instance.duration+=durationDelta-state.DurationDelta;
            state.Instance.attractionRadius+=attractionDelta-state.AttractionDelta;
            state.Instance.tickDamageRadius+=tickDelta-state.TickDelta;
            bool changed=state.Stage!=stage || state.DelayDelta!=delayDelta || state.DurationDelta!=durationDelta
                || state.AttractionDelta!=attractionDelta || state.TickDelta!=tickDelta;
            state.DelayDelta=delayDelta; state.DurationDelta=durationDelta; state.AttractionDelta=attractionDelta; state.TickDelta=tickDelta; state.Stage=stage;
            state.Build=state.Runtime.Powers.Build;
            if (current) state.Epoch=state.Runtime.ShieldEquipmentEpoch;
            else state.Retired=true;
            if (changed && state.Waiting) LastStarlightVisual(state,now);
        }
        private void LastStarlightVisual(LastStarlightState state,float now,bool removed=false)
        {
            if (state.Visual==0 && state.Stage>0) state.Visual=++state.Runtime.Boss.NextId;
            if (state.Visual==0) return;
            float deadline=state.WaitStart+(state.Active?state.Duration+state.DurationDelta:state.Delay+state.DelayDelta);
            PublishBossVisual(state.Runtime,state.Visual,4,state.Center,state.Center,
                state.Active?state.Instance.attractionRadius:state.Instance.tickDamageRadius,deadline,
                removed || state.Stage==0?now:Math.Max(now,deadline),removed || state.Stage==0,
                element:BossElement.Light,count:state.Active?2:1,budget:state.Active?state.Instance.tickDamageRadius:0,nativeLife:state.Life);
            if (removed || state.Stage==0) state.Visual=0;
        }
        internal void RefreshLastStarlight(Ai_Gem_U_LastStarlight instance)
        { if (_lastStarlights.TryGetValue(instance,out var state)) ReconcileLastStarlight(state,Time.time); }
        private void RelocateLastStarlight(HeroRuntime rt,Vector3 endpoint,float now)
        {
            foreach (var state in _lastStarlights.Values)
            {
                if (state.Runtime!=rt) continue;
                ReconcileLastStarlight(state,now);
                if (state.Stage<3 || state.Relocated || state.Active || state.Instance.Network_isBlackholeOn
                    || !state.Waiting || now>=state.WaitStart+state.Delay+state.DelayDelta) continue;
                if (!BossGround(state.OriginalCenter,endpoint,PowersBalance.LastStarlightRelocateRange,out var center)) continue;
                var info=state.Instance.info; info.point=center;
                state.Instance.FxStopNetworked(state.Instance.fxBlackholePrepare);
                state.Instance.Network_info=info;
                state.Instance.position=center;
                state.Instance.FxPlayNetworked(state.Instance.fxBlackholePrepare,center,Quaternion.identity);
                state.Center=center; state.Relocated=true;
                LastStarlightVisual(state,now);
            }
        }
        private void TickLastStarlight(HeroRuntime rt,float now)
        {
            if (now < rt.BossNative.StarlightPoll) return;
            rt.BossNative.StarlightPoll = now + .1f;
            _lastStarlightScratch.Clear();
            foreach (var pair in _lastStarlights)
                if (pair.Value.Runtime==rt)
                {
                    if (!BossNativeSameLife(pair.Key,pair.Value.Life) || pair.Key==null || !pair.Key.isActive) _lastStarlightScratch.Add(pair.Key);
                    else ReconcileLastStarlight(pair.Value,now);
                }
            foreach (var instance in _lastStarlightScratch) ReleaseLastStarlight(instance);
        }
        internal void UnequipLastStarlight(Gem_U_LastStarlight gem)
        {
            foreach (var state in _lastStarlights.Values) if (state.Gem==gem)
            { state.Retired=true; ReconcileLastStarlight(state,Time.time); }
        }
        internal void ReleaseLastStarlight(Ai_Gem_U_LastStarlight instance)
        {
            if (!_lastStarlights.TryGetValue(instance,out var state)) return;
            state.Retired=true; ReconcileLastStarlight(state,Time.time); LastStarlightVisual(state,Time.time,true);
            _lastStarlights.Remove(instance);
            state.Detached=true;
            if(!state.Sequence.Bound) { state.Reset(); _lastStarlightPool.Return(state); }
        }
        private void ClearLastStarlight(HeroRuntime rt)
        {
            foreach (var state in _lastStarlights.Values) if (state.Runtime==rt)
            { state.Retired=true; ReconcileLastStarlight(state,Time.time); LastStarlightVisual(state,Time.time,true); }
        }
    }
}

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
            internal float WaitStart;
            internal Vector3 OriginalCenter,Center;
            internal bool Active,Relocated,Retired,Waiting;
            internal int Stage;
        }
        private sealed class LastStarlightSequence : IEnumerator,IDisposable
        {
            private readonly HostAuthority _host;
            private readonly IEnumerator _native;
            private readonly LastStarlightState _state;
            private int _waits;
            private object _current;
            internal LastStarlightSequence(HostAuthority host,IEnumerator native,LastStarlightState state)
            { _host=host; _native=native; _state=state; }
            public object Current => _current;
            public bool MoveNext()
            {
                // Never run a continuation against a reused pooled instance.
                if (!_host.BossNativeSameLife(_state.Instance,_state.Life)) return false;
                bool result=_native.MoveNext();
                if (!result) { _state.Waiting=false; return false; }
                _current=_native.Current;
                if (!(_current is SI.WaitForSeconds)) return true;
                if (++_waits>2) throw new InvalidOperationException("LastStarlight native sequence unexpectedly added a wait.");
                _state.Active=_waits==2;
                _state.WaitStart=Time.time;
                _state.Waiting=true;
                _host.ReconcileLastStarlight(_state,Time.time);
                _current=new SI.WaitForCondition(WaitFinished);
                _host.LastStarlightVisual(_state,Time.time);
                return true;
            }
            private bool WaitFinished()
            {
                if (!_host.BossNativeSameLife(_state.Instance,_state.Life)) return true;
                _host.ReconcileLastStarlight(_state,Time.time);
                float seconds=_state.Active?_state.Duration+_state.DurationDelta:_state.Delay+_state.DelayDelta;
                return Time.time>=_state.WaitStart+Math.Max(0,seconds);
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() { (_native as IDisposable)?.Dispose(); }
        }
        private readonly Dictionary<Ai_Gem_U_LastStarlight,LastStarlightState> _lastStarlights = new Dictionary<Ai_Gem_U_LastStarlight,LastStarlightState>();
        private readonly List<Ai_Gem_U_LastStarlight> _lastStarlightScratch = new List<Ai_Gem_U_LastStarlight>();
        internal object CaptureLastStarlight(Ai_Gem_U_LastStarlight instance)
        {
            if (!NetworkServer.active || instance==null || AttributionGeneratedOrigin()!=GeneratedOrigin.None
                || !(instance.gem is Gem_U_LastStarlight gem) || instance.parentActor!=gem || !gem.isValid
                || !(gem.owner is Hero hero) || !BossAlive(hero) || instance.info.caster!=hero
                || gem.skill==null || !gem.skill.isActive || gem.skill.owner!=hero
                || !_runtimes.TryGetValue(hero,out var rt)
                || !LastStarlightEquipped(hero,gem,gem.skill)) return null;
            if (_lastStarlights.ContainsKey(instance)) ReleaseLastStarlight(instance);
            if (_lastStarlights.Count>=128) return null;
            var state=new LastStarlightState { Instance=instance,Gem=gem,Skill=gem.skill,Owner=hero,Runtime=rt,
                Life=BossNativeActorLife(instance),GemLife=BossNativeActorLife(gem),SkillLife=BossNativeActorLife(gem.skill),OwnerLife=BossNativeActorLife(hero),
                Epoch=rt.ShieldEquipmentEpoch,Creation=instance.creationTime,Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,
                Run=NetworkedManagerBase<GameManager>.softInstance?.runId,Delay=instance.delay,Duration=instance.duration,
                Attraction=instance.attractionRadius,TickRadius=instance.tickDamageRadius,OriginalCenter=instance.info.point,Center=instance.info.point };
            if (!BossNativeContextCurrent(state.Room,state.Run)) return null;
            _lastStarlights.Add(instance,state);
            ReconcileLastStarlight(state,Time.time);
            return state;
        }
        internal IEnumerator WrapLastStarlight(IEnumerator native,object capture)
            => new LastStarlightSequence(this,native,(LastStarlightState)capture);
        private static bool LastStarlightEquipped(Hero hero,Gem_U_LastStarlight gem,SkillTrigger skill)
        {
            if (hero.Skill==null || gem.skill!=skill || skill.owner!=hero) return false;
            foreach (var slot in hero.Skill.gems) if (ReferenceEquals(slot.Value,gem)) return true;
            return false;
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
            bool current=LastStarlightCurrent(state);
            int stage=current && BossEnsure(state.Runtime)?BossRewardStage(state.Runtime,BossProfiles.ErebosRewardId):0;
            float delayDelta=stage>0?-Math.Min(.2f,Math.Max(0,state.Delay-.25f)):0;
            float attractionDelta=stage>=2?Math.Min(1,Math.Max(0,12-state.Attraction)):0;
            float tickDelta=stage>=2?Math.Min(.5f,Math.Max(0,8-state.TickRadius)):0;
            float durationDelta=stage>=3?Math.Min(1,Math.Max(0,6-state.Duration)):0;
            // Preserve any external/native write and remove only this adapter's own difference.
            state.Instance.delay+=delayDelta-state.DelayDelta;
            state.Instance.duration+=durationDelta-state.DurationDelta;
            state.Instance.attractionRadius+=attractionDelta-state.AttractionDelta;
            state.Instance.tickDamageRadius+=tickDelta-state.TickDelta;
            bool changed=state.Stage!=stage || state.DelayDelta!=delayDelta || state.DurationDelta!=durationDelta
                || state.AttractionDelta!=attractionDelta || state.TickDelta!=tickDelta;
            state.DelayDelta=delayDelta; state.DurationDelta=durationDelta; state.AttractionDelta=attractionDelta; state.TickDelta=tickDelta; state.Stage=stage;
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
                if (!BossGround(state.OriginalCenter,endpoint,2,out var center)) continue;
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
        }
        private void ClearLastStarlight(HeroRuntime rt)
        {
            foreach (var state in _lastStarlights.Values) if (state.Runtime==rt)
            { state.Retired=true; ReconcileLastStarlight(state,Time.time); LastStarlightVisual(state,Time.time,true); }
        }
    }
}

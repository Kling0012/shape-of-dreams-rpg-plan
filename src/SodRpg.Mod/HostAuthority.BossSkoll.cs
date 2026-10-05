using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class SkollState
        {
            internal int Marks;
            internal float MarksUntil;
            internal long MarkVisual;
            internal readonly Dictionary<Gem_U_GlacialCore, SkollCoreState> Cores = new Dictionary<Gem_U_GlacialCore, SkollCoreState>(BossProfiles.MaxEntries);
            internal readonly List<Gem_U_GlacialCore> ExpiredCores = new List<Gem_U_GlacialCore>(BossProfiles.MaxEntries);
        }
        private readonly BossObjectPool<SkollState> _skollStatePool = new BossObjectPool<SkollState>(64, () => new SkollState());
        private readonly Dictionary<HeroRuntime, SkollState> _skollStates = new Dictionary<HeroRuntime, SkollState>(64);
        private SkollState SkollOwnerState(HeroRuntime rt)
        {
            if (!_skollStates.TryGetValue(rt, out var state))
            {
                state = _skollStatePool.Rent();
                if (state == null) return null;
                _skollStates.Add(rt, state);
            }
            return state;
        }
        private static bool SkollCooldownAvailable(HeroRuntime rt, BossAction action, float now)
            => !rt.Boss.Ready.TryGetValue(action.RuntimeKey, out var due) || now >= due;
        private static void SkollCommitAction(HeroRuntime rt, BossAction action, float now)
        {
            BossReady(rt, action.RuntimeKey, now, action.CooldownMillis);
            if (action.MainHits > 0) rt.Boss.Ledger.Consume(action.RuntimeKey);
        }
        private bool SkollReserve(HeroRuntime rt, BossMoveProfile profile, BossAction action, BossPulseBuffer pulses, float now, int lifetime = 0)
        {
            if (!SkollCooldownAvailable(rt, action, now)
                || !BossReserveSequence(rt, profile.SetId, profile.Id, pulses, now, action.MaxInstances, lifetime)) return false;
            SkollCommitAction(rt, action, now);
            return true;
        }
        private static Vector3 SkollFacing(HeroRuntime rt, Vector3 point)
        {
            var dir = BossDirection(rt.Hero.agentPosition, point);
            return dir == Vector3.zero ? rt.Hero.transform.forward.Flattened().normalized : dir;
        }
        private bool SkollSlash(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, BossAction action, Vector3 requested, float now, out Vector3 fixedPoint)
        {
            fixedPoint = default;
            var owner = rt.Hero.agentPosition;
            if (!SkollCooldownAvailable(rt, action, now) || !BossGround(owner, requested, action.MagnitudeMilli / 1000f, out fixedPoint)) return false;
            var dir = SkollFacing(rt, fixedPoint);
            var start = fixedPoint - dir * (action.RangeMilli / 2000f);
            var end = fixedPoint + dir * (action.RangeMilli / 2000f);
            if (!BossGround(fixedPoint, start, action.RangeMilli / 2000f, out start)
                || !BossGround(start, end, action.RangeMilli / 1000f, out end)) return false;
            var pulses = rt.Boss.Sequence.Reset();
            if (!pulses.Add(new BossScheduledPulse(action,start,end,BossAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),action.DelayMillis))) return false;
            return SkollReserve(rt, profile, action, pulses, now);
        }
        private void SkollArrowField(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, Vector3 fixedPoint, float now)
        {
            var initial = profile.Actions[0]; var ticks = profile.Actions[1];
            if (!SkollCooldownAvailable(rt, initial, now)) return;
            var pulses = rt.Boss.Sequence.Reset();
            bool magic = BossMagic(rt);
            if (!pulses.Add(new BossScheduledPulse(initial,fixedPoint,fixedPoint,BossAmount(rt,entry,profile,initial.ChannelId),magic,initial.DelayMillis))) return;
            float tickDamage = BossAmount(rt,entry,profile,ticks.ChannelId);
            for (int i = 0; i < ticks.Count; i++) if (!pulses.Add(new BossScheduledPulse(ticks,fixedPoint,fixedPoint,tickDamage,magic,ticks.DelayMillis + i * ticks.IntervalMillis))) return;
            SkollReserve(rt,profile,initial,pulses,now,initial.DelayMillis + initial.LifetimeMillis);
        }
        private void SkollRain(HeroRuntime rt, Vector3 fixedPoint, float now)
        {
            if (!BossFind(rt,"boss_skoll.stage6",out var entry,out var profile)) return;
            var rain = profile.Actions[0]; var shield = profile.Actions[1];
            if (!SkollCooldownAvailable(rt,rain,now)) return;
            var pulses = rt.Boss.Sequence.Reset();
            float damage = BossAmount(rt,entry,profile,rain.ChannelId) / rain.Count;
            bool magic = BossMagic(rt);
            for (int i = 0; i < rain.Count; i++)
            {
                var dir = Quaternion.Euler(0,360f * i / rain.Count,0) * Vector3.forward;
                var desired = fixedPoint + dir * (rain.RangeMilli / 1000f);
                if (!BossGround(fixedPoint,desired,rain.RangeMilli / 1000f,out var legal)) return;
                if (!pulses.Add(new BossScheduledPulse(rain,legal,legal,damage,magic,rain.DelayMillis + i * rain.IntervalMillis))) return;
            }
            if (!pulses.Add(new BossScheduledPulse(shield,rt.Hero.agentPosition,rt.Hero.agentPosition,BossAmount(rt,entry,profile,shield.ChannelId),magic,shield.DelayMillis))) return;
            SkollReserve(rt,profile,rain,pulses,now);
        }
        private void SkollPublishMarks(HeroRuntime rt, SkollState state, float now)
        {
            if (state.MarkVisual == 0) state.MarkVisual = ++rt.Boss.NextId;
            PublishBossVisual(rt,state.MarkVisual,9,rt.Hero.agentPosition,rt.Hero.agentPosition,state.Marks,now,state.MarksUntil,state.Marks == 0,
                element:BossElement.Cold,count:state.Marks);
        }
        private void DispatchSkollBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (!NetworkServer.active || activation == 0 || !BossAlive(rt.Hero)) return;
            var owner = rt.Hero.agentPosition;
            if ((kind == BossEvent.MainHit || kind == BossEvent.MemoryUse) && BossFind(rt,"boss_skoll.stage2",out var sealEntry,out var sealProfile))
            {
                var state = SkollOwnerState(rt);
                if (state == null) return;
                var mark = sealProfile.Actions[kind == BossEvent.MainHit ? 0 : 1]; var slash = sealProfile.Actions[2];
                if (now >= state.MarksUntil) state.Marks = 0;
                if (state.Marks >= mark.Count)
                {
                    var desired = kind == BossEvent.MemoryUse ? BossCursor(rt) : point;
                    if (SkollSlash(rt,sealEntry,sealProfile,slash,desired,now,out var fixedPoint))
                    {
                        state.Marks = 0; state.MarksUntil = now;
                        if (BossFind(rt,"boss_skoll.stage3",out var arrowEntry,out var arrowProfile)) SkollArrowField(rt,arrowEntry,arrowProfile,fixedPoint,now);
                        SkollRain(rt,fixedPoint,now);
                    }
                }
                else
                {
                    state.Marks = Math.Min(mark.Count,state.Marks + 1);
                    state.MarksUntil = now + mark.LifetimeMillis / 1000f;
                }
                SkollPublishMarks(rt,state,now);
            }
            var moves = rt.Powers.Build.BossMoves;
            for (int moveIndex = 0; moveIndex < moves.Count && moveIndex < BossProfiles.MaxEntries; moveIndex++)
            {
                var entry = moves[moveIndex];
                if (entry.SetId != BossProfiles.SkollSetId || !BossProfiles.TryGetMove(entry.ProfileId,out var profile)
                    || profile.Id == "boss_skoll.stage2" || profile.Id == "boss_skoll.stage3" || profile.Id == "boss_skoll.stage6") continue;
                var action = profile.Actions[0];
                if (action.Event != kind) continue;
                string key = action.RuntimeKey;
                if (action.MainHits > 0 && !rt.Boss.Ledger.Advance(key,activation,action.MainHits,action.CounterLifetimeMillis / 1000f,now)
                    || !SkollCooldownAvailable(rt,action,now)) continue;
                if (profile.Id == "boss_skoll.weapon") { SkollSlash(rt,entry,profile,action,point,now,out _); continue; }
                if (profile.Id == "boss_skoll.charm")
                {
                    if (BossGround(owner,BossCursor(rt),action.RangeMilli / 1000f,out var legal)) SkollArrowField(rt,entry,profile,legal,now);
                    continue;
                }
                bool magic = BossMagic(rt); float amount = BossAmount(rt,entry,profile,action.ChannelId);
                if (profile.Id == "boss_skoll.head")
                {
                    var side = Vector3.Cross(Vector3.up,SkollFacing(rt,point));
                    var pulses = rt.Boss.Sequence.Reset(); bool valid = true;
                    for (int i = 0; i < action.Count; i++)
                    {
                        var desired = point + side * ((i - (action.Count - 1) / 2f) * action.WidthMilli / 1000f);
                        if (!BossGround(point,desired,Math.Abs(i - (action.Count - 1) / 2f) * action.WidthMilli / 1000f,out var legal)) { valid = false; break; }
                        if (!pulses.Add(new BossScheduledPulse(action,legal,legal,amount,magic,action.DelayMillis + i * action.IntervalMillis))) { valid = false; break; }
                    }
                    if (valid) SkollReserve(rt,profile,action,pulses,now);
                }
                else if (profile.Id == "boss_skoll.feet")
                {
                    var pulses = rt.Boss.Sequence.Reset(); bool valid = true;
                    for (int i = 0; i < action.Count; i++) if (!pulses.Add(new BossScheduledPulse(action,owner,owner,amount,magic,action.DelayMillis + i * action.IntervalMillis))) { valid = false; break; }
                    if (!valid) continue;
                    SkollReserve(rt,profile,action,pulses,now,action.LifetimeMillis);
                }
                else
                {
                    var end = profile.Id == "boss_skoll.hands" ? owner + SkollFacing(rt,point) * (action.RangeMilli / 1000f) : owner;
                    var pulses = rt.Boss.Sequence.Reset();
                    if (pulses.Add(new BossScheduledPulse(action,owner,end,amount,magic,action.DelayMillis))) SkollReserve(rt,profile,action,pulses,now);
                }
            }
        }
        private void TickSkollBoss(HeroRuntime rt, float now)
        {
            if (!_skollStates.TryGetValue(rt,out var state)) return;
            if (state.Marks > 0)
            {
                if (now >= state.MarksUntil) state.Marks = 0;
                SkollPublishMarks(rt,state,now);
            }
            TickSkollCores(rt,state,now);
        }
        private void ClearSkollBoss(HeroRuntime rt, bool preserveRewards = false)
        {
            if (!preserveRewards)
            {
                rt.Boss.Ready.Remove("boss_skoll.glacial_core.heal");
                rt.Boss.Ready.Remove("boss_skoll.glacial_core.burst");
            }
            if (!_skollStates.TryGetValue(rt,out var state)) return;
            state.Marks = 0; state.MarksUntil = 0; state.MarkVisual = 0;
            if (preserveRewards) return;
            ClearSkollCores(rt,state);
            _skollStates.Remove(rt);
            _skollStatePool.Return(state);
        }
    }
}

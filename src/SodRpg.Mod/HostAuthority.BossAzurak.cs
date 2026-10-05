using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class AzurakState
        {
            internal int Hits;
            internal long LastMain, HitVisual, HavenVisual;
            internal float HitsUntil, HavenDue, HavenUntil, HavenAmount, BurrowReady;
            internal Vector3 HavenPoint;
            internal bool HavenChecked;
            internal void Reset()
            {
                Hits=0; LastMain=HitVisual=HavenVisual=0; HitsUntil=HavenDue=HavenUntil=HavenAmount=BurrowReady=0;
                HavenPoint=default; HavenChecked=false;
            }
        }
        private readonly Dictionary<HeroRuntime, AzurakState> _azurakStates = new Dictionary<HeroRuntime, AzurakState>(64);
        private readonly BossObjectPool<AzurakState> _azurakPool = new BossObjectPool<AzurakState>(64,()=>new AzurakState());
        private AzurakState AzurakOwner(HeroRuntime rt)
        {
            if (!_azurakStates.TryGetValue(rt,out var state))
            {
                if(_azurakStates.Count>=64 || (state=_azurakPool.Rent())==null) return null;
                _azurakStates.Add(rt,state);
            }
            return state;
        }
        private static bool AzurakAvailable(HeroRuntime rt, BossAction action, float now)
            => rt.Boss.Ready.TryGetValue(action.RuntimeKey,out var due) ? now>=due : rt.Boss.Ready.Count<128;
        private static void AzurakCommit(HeroRuntime rt, BossAction action, float now)
            => BossReady(rt, action.RuntimeKey, now, action.CooldownMillis);
        private bool AzurakStomp(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, Vector3 center, float now)
        {
            var action = profile.Actions[0];
            if (!AzurakAvailable(rt, action, now)) return false;
            float amount = BossAmount(rt, entry, profile, action.ChannelId) / action.Count;
            bool magic = BossMagic(rt);
            // Immediate attacks need no finite field token; delayed/multiple attacks share one token.
            if (action.Count == 1 && action.DelayMillis == 0)
            {
                AzurakCommit(rt, action, now);
                rt.Boss.Shapes.Execute(this, rt, action, center, center, amount, magic);
                if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return false;
                PublishBossVisual(rt, ++rt.Boss.NextId, 1, center, center, action.RadiusMilli / 1000f, now, now + 0.25f);
                return true;
            }
            if(action.Count>32) return false;
            var pulses=rt.Boss.Sequence.Reset();
            for (int i=0;i<action.Count;i++)
                if(!pulses.Add(new BossScheduledPulse(action,center,center,amount,magic,action.DelayMillis+i*action.IntervalMillis))) return false;
            if (!BossReserveSequence(rt, profile.SetId, profile.Id, pulses, now, 1)) return false;
            AzurakCommit(rt, action, now);
            return true;
        }
        private void AzurakPublishHits(HeroRuntime rt, AzurakState state, float now)
        {
            if (state.HitVisual == 0) state.HitVisual = ++rt.Boss.NextId;
            PublishBossVisual(rt, state.HitVisual, 9, rt.Hero.agentPosition, rt.Hero.agentPosition, state.Hits, now,
                state.HitsUntil, removed: state.Hits == 0, count: state.Hits);
        }
        private void DispatchAzurakBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (!NetworkServer.active || activation == 0 || !BossAlive(rt.Hero)) return;
            Vector3 owner = rt.Hero.agentPosition;
            if (kind == BossEvent.MainHit && BossFind(rt, "boss_azurak.stage2", out var stageEntry, out var stageProfile))
            {
                var state=AzurakOwner(rt); if(state==null) return;
                if (state.LastMain != activation)
                {
                    state.LastMain = activation;
                    if (now >= state.HitsUntil) state.Hits = 0;
                    state.Hits = Math.Min(3, state.Hits + 1);
                    state.HitsUntil = now + 4;
                    if (state.Hits == 3)
                    {
                        state.Hits = 0;
                        // All stages observe the identical third hit, even if another stage is cooling down or cannot reserve.
                        if (BossGround(owner, point, 8, out var fixedPoint))
                        {
                            AzurakStomp(rt, stageEntry, stageProfile, fixedPoint, now);
                            if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
                            if (BossFind(rt, "boss_azurak.stage3", out var restompEntry, out var restompProfile))
                                AzurakStomp(rt, restompEntry, restompProfile, fixedPoint, now);
                            if (BossFind(rt, "boss_azurak.stage6", out var sixEntry, out var sixProfile)
                                && AzurakStomp(rt, sixEntry, sixProfile, fixedPoint, now))
                            {
                                var shield = sixProfile.Actions[1];
                                rt.Boss.Defense.Execute(this, rt, sixProfile.Id, shield, BossAmount(rt, sixEntry, sixProfile, shield.ChannelId), now);
                            }
                        }
                    }
                    AzurakPublishHits(rt, state, now);
                }
            }
            for(int bossMoveIndex=0;bossMoveIndex<rt.Powers.Build.BossMoves.Count;bossMoveIndex++)
            {
                var entry=rt.Powers.Build.BossMoves[bossMoveIndex];
                if (entry.SetId != BossProfiles.AzurakSetId || !BossProfiles.TryGetMove(entry.ProfileId, out var profile)
                    || profile.Id == "boss_azurak.stage2" || profile.Id == "boss_azurak.stage3" || profile.Id == "boss_azurak.stage6") continue;
                var action = profile.Actions[0];
                if (action.Event != kind || !AzurakAvailable(rt, action, now)) continue;
                // Native event admission is shared; this action gate also excludes duplicate callbacks per activation.
                string key = action.RuntimeKey;
                if (!rt.Boss.Ledger.Advance(key, activation, 1, 0, now)) continue;
                if (profile.Id == "boss_azurak.armor")
                {
                    if (rt.Boss.Defense.Execute(this, rt, profile.Id, action, BossAmount(rt, entry, profile, action.ChannelId), now))
                        AzurakCommit(rt, action, now);
                }
                else if (profile.Id == "boss_azurak.head")
                {
                    var state=AzurakOwner(rt); if(state==null) continue;
                    if (!BossGround(owner, owner, 0, out var center)
                        || !BossReserveSequence(rt, profile.SetId, profile.Id, rt.Boss.Sequence.Reset(), now, 1, action.LifetimeMillis)) continue;
                    state.HavenPoint = center; state.HavenDue = now + action.DelayMillis / 1000f;
                    state.HavenUntil = now + action.LifetimeMillis / 1000f;
                    state.HavenAmount = BossAmount(rt, entry, profile, action.ChannelId); state.HavenChecked = false;
                    state.HavenVisual = ++rt.Boss.NextId;
                    PublishBossVisual(rt, state.HavenVisual, 4, center, center, 2, now, state.HavenUntil);
                    AzurakCommit(rt, action, now);
                }
                else
                {
                    Vector3 center;
                    if (profile.Id == "boss_azurak.charm") center = owner;
                    else if (!BossGround(owner, point, profile.Id == "boss_azurak.hands" ? 8 : BossDirectionDistance(owner, point), out center)) continue;
                    AzurakStomp(rt, entry, profile, center, now);
                }
            }
        }
        private void AzurakClearHaven(HeroRuntime rt, AzurakState state, float now)
        {
            if (state.HavenVisual != 0)
                PublishBossVisual(rt, state.HavenVisual, 4, state.HavenPoint, state.HavenPoint, 2, now, now, true);
            state.HavenVisual = 0; state.HavenUntil = 0; state.HavenChecked = true;
            BossCancelProfileReservations(rt, BossProfiles.AzurakSetId, "boss_azurak.head");
        }
        private void TickAzurakBoss(HeroRuntime rt, float now)
        {
            if (_azurakStates.TryGetValue(rt, out var state))
            {
                if (state.Hits>0 && now>=state.HitsUntil)
                { state.Hits = 0; AzurakPublishHits(rt, state, now); }
                if (state.HavenVisual != 0)
                {
                    if (now>=state.HavenUntil)
                        AzurakClearHaven(rt, state, now);
                    else if (!state.HavenChecked && now >= state.HavenDue)
                    {
                        if(!BossFind(rt,"boss_azurak.head",out _,out var profile)) { AzurakClearHaven(rt,state,now); return; }
                        state.HavenChecked = true;
                        if (BossAlive(rt.Hero) && BossDirectionDistance(rt.Hero.agentPosition, state.HavenPoint) <= 2
                            && Math.Abs(rt.Hero.agentPosition.y - state.HavenPoint.y) <= 2)
                            rt.Boss.Defense.Execute(this, rt, profile.Id, profile.Actions[1], state.HavenAmount, now);
                    }
                }
            }
            TickAzurakBurrow(rt);
        }
        private void ClearAzurakBoss(HeroRuntime rt, bool preserveRewards = false)
        {
            if (_azurakStates.TryGetValue(rt, out var state))
            {
                state.Hits = 0; state.LastMain = 0;
                if (state.HitVisual != 0) AzurakPublishHits(rt, state, Time.time);
                AzurakClearHaven(rt, state, Time.time);
                rt.Boss.Defense.CancelSource(this,rt,"boss_azurak.armor");
                rt.Boss.Defense.CancelSource(this,rt,"boss_azurak.head");
                rt.Boss.Defense.CancelSource(this,rt,"boss_azurak.stage6");
                if (!preserveRewards) { _azurakStates.Remove(rt); state.Reset(); _azurakPool.Return(state); }
            }
            if (!preserveRewards) ClearAzurakBurrow(rt);
        }
    }
}

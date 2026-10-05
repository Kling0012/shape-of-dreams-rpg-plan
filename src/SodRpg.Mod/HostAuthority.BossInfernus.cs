using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class InfernusCombat
        {
            internal readonly BossNativeSeen Inputs = new BossNativeSeen();
            internal int Heat;
            internal float HeatUntil;
            internal long HeatVisual;
        }
        private readonly BossObjectPool<InfernusCombat> _infernusPool = new BossObjectPool<InfernusCombat>(64, () => new InfernusCombat());
        private readonly Dictionary<HeroRuntime, InfernusCombat> _infernusCombat = new Dictionary<HeroRuntime, InfernusCombat>(64);
        private InfernusCombat InfernusState(HeroRuntime rt)
        {
            if (!_infernusCombat.TryGetValue(rt, out var state))
            {
                state = _infernusPool.Rent();
                if (state == null) return null;
                _infernusCombat.Add(rt, state);
            }
            return state;
        }
        private static bool InfernusAvailable(HeroRuntime rt, BossAction action, float now)
            => !rt.Boss.Ready.TryGetValue(action.RuntimeKey, out float due) || now >= due;
        private static Vector3 InfernusDirection(HeroRuntime rt, BossEvent kind, Vector3 point)
        {
            if (kind == BossEvent.MovementCompleted) return rt.Hero.transform.forward;
            var direction = BossDirection(rt.Hero.agentPosition, kind == BossEvent.MainHit ? point : BossCursor(rt));
            return direction == Vector3.zero ? rt.Hero.transform.forward : direction;
        }
        private void DispatchInfernusBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (kind == BossEvent.MainHit || kind == BossEvent.MemoryUse) InfernusHeatInput(rt, kind, activation, point, now);
            var moves = rt.Powers.Build.BossMoves;
            for (int moveIndex = 0; moveIndex < moves.Count && moveIndex < BossProfiles.MaxEntries; moveIndex++)
            {
                var entry = moves[moveIndex];
                if (entry.SetId != BossProfiles.InfernusSetId || entry.ProfileId == "boss_infernus.stage2"
                    || entry.ProfileId == "boss_infernus.stage3" || entry.ProfileId == "boss_infernus.stage6"
                    || !BossProfiles.TryGetMove(entry.ProfileId, out var profile)) continue;
                var action = profile.Actions[0];
                if (action.Event != kind) continue;
                string key = action.RuntimeKey;
                if (action.MainHits > 0 && !rt.Boss.Ledger.Advance(key, activation, action.MainHits, action.CounterLifetimeMillis / 1000f, now)) continue;
                if (!InfernusAvailable(rt, action, now)) continue;
                Vector3 center = rt.Hero.agentPosition, direction = InfernusDirection(rt, kind, point), end = center + direction * (action.RangeMilli / 1000f);
                bool success;
                if (entry.ProfileId == "boss_infernus.weapon")
                    success = InfernusReserveEruptions(rt, entry, profile, action, center, direction, now, out _);
                else if (entry.ProfileId == "boss_infernus.charm")
                {
                    if (!BossGround(center, BossCursor(rt), action.RangeMilli / 1000f, out var legal)) continue;
                    success = InfernusReservePillar(rt, entry, profile, legal, now);
                }
                else
                {
                    int count = action.Mechanism == BossMechanism.Projectile ? 1 : action.Count;
                    var pulses = rt.Boss.Sequence.Reset(); bool valid = true;
                    float amount = BossAmount(rt, entry, profile, action.ChannelId);
                    if (action.Mechanism == BossMechanism.Projectile) amount /= action.Count;
                    for (int i = 0; i < count; i++)
                        if (!pulses.Add(new BossScheduledPulse(action, center, end, amount, BossMagic(rt), action.DelayMillis + i * action.IntervalMillis))) { valid = false; break; }
                    if (!valid) continue;
                    success = BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, action.MaxInstances,
                        action.DelayMillis + action.LifetimeMillis);
                }
                if (!success) continue;
                BossReady(rt, key, now, action.CooldownMillis);
                if (action.MainHits > 0) rt.Boss.Ledger.Consume(key);
            }
        }
        private bool InfernusReserveEruptions(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, BossAction action,
            Vector3 origin, Vector3 direction, float now, out Vector3 last)
        {
            last = origin;
            var center = Dew.GetPositionOnGround(origin);
            var pulses = rt.Boss.Sequence.Reset();
            float amount = BossAmount(rt, entry, profile, action.ChannelId);
            bool magic = BossMagic(rt);
            for (int i = 0; i < action.Count; i++)
            {
                Vector3 desired = Dew.GetPositionOnGround(center + direction * (action.MagnitudeMilli * (i + 1) / 1000f));
                if (!BossFinite(desired) || Math.Abs(desired.y - center.y) > action.WidthMilli / 1000f
                    || !BossGround(center, desired, action.RangeMilli / 1000f, out var legal)
                    || (legal - desired).Flattened().sqrMagnitude > 0.0001f) break;
                if (!pulses.Add(new BossScheduledPulse(action, desired, desired, amount, magic, action.DelayMillis + i * action.IntervalMillis))) return false;
                last = desired;
            }
            if (pulses.Count == 0) return false;
            if (!BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, action.MaxInstances)) return false;
            return true;
        }
        private bool InfernusReservePillar(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, Vector3 center, float now)
        {
            var first = profile.Actions[0];
            bool initial = profile.Actions.Count > 1;
            var ticks = initial ? profile.Actions[1] : first;
            var pulses = rt.Boss.Sequence.Reset();
            bool magic = BossMagic(rt);
            if (initial && !pulses.Add(new BossScheduledPulse(first, center, center, BossAmount(rt, entry, profile, first.ChannelId), magic, first.DelayMillis))) return false;
            float amount = BossAmount(rt, entry, profile, ticks.ChannelId);
            for (int i = 0; i < ticks.Count; i++)
                if (!pulses.Add(new BossScheduledPulse(ticks, center, center, amount, magic, ticks.DelayMillis + ticks.IntervalMillis * i))) return false;
            return BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, first.MaxInstances, first.DelayMillis + first.LifetimeMillis);
        }
        private void InfernusHeatInput(HeroRuntime rt, BossEvent kind, long activation, Vector3 point, float now)
        {
            if (!BossFind(rt, "boss_infernus.stage2", out var entry, out var profile)) return;
            var state = InfernusState(rt);
            if (state == null || !state.Inputs.Add(activation)) return;
            var marks = profile.Actions[0];
            var attack = profile.Actions[1];
            if (now >= state.HeatUntil) state.Heat = 0;
            if (state.Heat < marks.Count)
            {
                state.Heat++;
                state.HeatUntil = now + marks.LifetimeMillis / 1000f;
                InfernusHeatVisual(rt, state, now);
                return;
            }
            if (!InfernusAvailable(rt, attack, now)) return;
            Vector3 origin = rt.Hero.agentPosition, direction = InfernusDirection(rt, kind, point);
            if (!InfernusReserveEruptions(rt, entry, profile, attack, origin, direction, now, out var last)) return;
            BossReady(rt, attack.RuntimeKey, now, attack.CooldownMillis);
            state.Heat = 0;
            InfernusHeatVisual(rt, state, now);
            if (BossFind(rt, "boss_infernus.stage3", out var pillarEntry, out var pillarProfile))
                InfernusReservePillar(rt, pillarEntry, pillarProfile, last, now);
            if (!BossFind(rt, "boss_infernus.stage6", out var burstEntry, out var burstProfile)) return;
            var burst = burstProfile.Actions[0];
            if (!InfernusAvailable(rt, burst, now)) return;
            var shield = burstProfile.Actions[1];
            var pulses = rt.Boss.Sequence.Reset();
            if (!pulses.Add(new BossScheduledPulse(burst,origin,origin+direction*(burst.RangeMilli/1000f),BossAmount(rt,burstEntry,burstProfile,burst.ChannelId)/burst.Count,BossMagic(rt),burst.DelayMillis))
                || !pulses.Add(new BossScheduledPulse(shield,origin,origin,BossAmount(rt,burstEntry,burstProfile,shield.ChannelId),BossMagic(rt),shield.DelayMillis))) return;
            if (BossReserveSequence(rt, burstEntry.SetId, burstProfile.Id, pulses, now, burst.MaxInstances, burst.DelayMillis + burst.LifetimeMillis))
                BossReady(rt, burst.RuntimeKey, now, burst.CooldownMillis);
        }
        private void InfernusHeatVisual(HeroRuntime rt, InfernusCombat state, float now)
        {
            if (state.HeatVisual == 0 && state.Heat > 0) state.HeatVisual = ++rt.Boss.NextId;
            if (state.HeatVisual == 0) return;
            PublishBossVisual(rt, state.HeatVisual, 9, rt.Hero.agentPosition, rt.Hero.agentPosition, state.Heat, now,
                state.Heat > 0 ? state.HeatUntil : now, state.Heat == 0, element:BossElement.Fire,count:state.Heat);
            if (state.Heat == 0) state.HeatVisual = 0;
        }
        private void TickInfernusBoss(HeroRuntime rt, float now)
        {
            if (_infernusCombat.TryGetValue(rt, out var state) && state.Heat > 0 && now >= state.HeatUntil)
            { state.Heat = 0; InfernusHeatVisual(rt, state, now); }
            TickEternalFlame(rt, now);
        }
        private void ClearInfernusBoss(HeroRuntime rt)
        {
            if (_infernusCombat.TryGetValue(rt, out var state))
            { state.Heat = 0; InfernusHeatVisual(rt, state, Time.time); _infernusCombat.Remove(rt); state.Inputs.Clear(); state.HeatUntil = 0; state.HeatVisual = 0; _infernusPool.Return(state); }
            ClearEternalFlame(rt);
        }
    }
}

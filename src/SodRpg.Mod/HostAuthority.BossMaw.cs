using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class MawCombat
        {
            internal readonly long[] Admissions = new long[256];
            internal readonly int[] NextAdmission = new int[4];
            internal int CycleHits, CharmHits;
            internal float CycleUntil, CharmUntil, ArrivalUntil;
            internal bool Admit(int kind, long activation)
            {
                int start = kind * 64;
                for (int i = start; i < start + 64; i++) if (Admissions[i] == activation) return false;
                Admissions[start + NextAdmission[kind]] = activation;
                NextAdmission[kind] = (NextAdmission[kind] + 1) % 64;
                return true;
            }
            internal void Reset()
            {
                Array.Clear(Admissions,0,Admissions.Length); Array.Clear(NextAdmission,0,NextAdmission.Length);
                CycleHits = CharmHits = 0; CycleUntil = CharmUntil = ArrivalUntil = 0;
            }
        }
        private readonly BossObjectPool<MawCombat> _mawCombatPool = new BossObjectPool<MawCombat>(64, () => new MawCombat());
        private readonly Dictionary<HeroRuntime,MawCombat> _mawCombat = new Dictionary<HeroRuntime,MawCombat>(64);
        private MawCombat MawState(HeroRuntime rt)
        {
            if (_mawCombat.TryGetValue(rt,out var state)) return state;
            if (_mawCombat.Count >= 64 || (state = _mawCombatPool.Rent()) == null) return null;
            state.Reset(); _mawCombat.Add(rt,state); return state;
        }
        private static bool MawAvailable(HeroRuntime rt, BossAction action, float now)
            => (!rt.Boss.Ready.TryGetValue(action.RuntimeKey,out var due) || now >= due)
                && (rt.Boss.Ready.ContainsKey(action.RuntimeKey) || rt.Boss.Ready.Count < 128);
        private static void MawCommit(HeroRuntime rt, BossAction action, float now)
            => rt.Boss.Ready[action.RuntimeKey] = now + action.CooldownMillis / 1000f;
        private static float MawAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channel)
            => Math.Max(0f,Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower))
                * BossCoefficient(entry,profile,channel);
        private static float MawBoundedAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, BossAction action, float basis)
        {
            for (int i = 0; i < profile.Channels.Count; i++)
            {
                var channel = profile.Channels[i];
                if (channel.ChannelId != action.ChannelId) continue;
                float coefficient = Math.Min(channel.ValueMilli,channel.CapMilli) / 100000f;
                if (coefficient <= 0) return 0;
                float multiplier = Math.Min(3f,Math.Max(0f,BossCoefficient(entry,profile,action.ChannelId) / coefficient));
                float h = Math.Max(0f,Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower));
                return Math.Min(h * coefficient,basis * action.MagnitudeMilli / 100000f) * multiplier;
            }
            return 0;
        }
        private static Vector3 MawDirection(HeroRuntime rt, BossEvent kind, Vector3 origin, Vector3 point)
        {
            Vector3 direction = kind == BossEvent.MemoryUse ? rt.Boss.MemoryDirection
                : kind == BossEvent.MainHit ? point - origin : rt.Hero.transform.forward;
            direction.y = 0;
            if (!BossFinite(direction) || direction.sqrMagnitude <= .0001f) direction = rt.Hero.transform.forward.Flattened();
            return BossFinite(direction) && direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }
        private void DispatchMawBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (!NetworkServer.active || rt.Boss.Build == null || !BossAlive(rt.Hero) || activation <= 0) return;
            int input = kind == BossEvent.MainHit ? 0 : kind == BossEvent.MemoryUse ? 1
                : kind == BossEvent.MovementCompleted ? 2 : kind == BossEvent.NativeDamageTaken ? 3 : -1;
            if (input < 0 || kind == BossEvent.MemoryUse && !rt.Boss.MemoryDirectionValid
                || kind == BossEvent.NativeDamageTaken && (rt.Boss.MainHpDamage <= 0 || victim == null
                    || victim.GetRelation(rt.Hero) != EntityRelation.Enemy)) return;
            bool owned = false;
            for (int i = 0; i < rt.Boss.Build.BossMoves.Count; i++)
                if (rt.Boss.Build.BossMoves[i].SetId == BossProfiles.MawSetId) { owned = true; break; }
            if (!owned) return;
            var state = MawState(rt);
            if (state == null || !state.Admit(input,activation)) return;
            MawExpire(state,now);
            Vector3 origin = rt.Hero.agentPosition;
            if (!BossFinite(origin)) return;
            Vector3 direction = MawDirection(rt,kind,origin,point);
            // Healing parts must not change this activation's low-HP geometry or placement.
            bool low = rt.Hero.currentHealth <= rt.Hero.maxHealth * .5f;
            bool arrival = kind == BossEvent.MainHit && state.ArrivalUntil > now;
            if (kind == BossEvent.MainHit) state.ArrivalUntil = 0;
            for (int i = 0; i < rt.Boss.Build.BossMoves.Count; i++)
            {
                var entry = rt.Boss.Build.BossMoves[i];
                if (entry.SetId != BossProfiles.MawSetId || !BossProfiles.TryGetMove(entry.ProfileId,out var profile)) continue;
                var action = profile.Actions[0];
                if (profile.Id == "boss_maw.feet")
                {
                    if (kind == BossEvent.MovementCompleted) state.ArrivalUntil = now + action.LifetimeMillis / 1000f;
                    if (kind != BossEvent.MainHit || !arrival) continue;
                    action = profile.Actions[1];
                }
                else if (profile.Id == "boss_maw.charm")
                {
                    if (kind != BossEvent.MainHit) continue;
                    state.CharmUntil = now + 4f;
                    if (state.CharmHits < 3) { state.CharmHits++; continue; }
                }
                else if (profile.Id == "boss_maw.stage2" || profile.Id == "boss_maw.stage3" || profile.Id == "boss_maw.stage6") continue;
                else if (action.Event != kind) continue;
                if (!MawAvailable(rt,action,now)) continue;
                float amount;
                bool success;
                if (profile.Id == "boss_maw.armor" || profile.Id == "boss_maw.charm" || profile.Id == "boss_maw.hands")
                {
                    float basis = profile.Id == "boss_maw.hands" ? Math.Max(0f,rt.Boss.MainHpDamage) : Math.Max(0f,rt.Hero.Status.missingHealth);
                    amount = MawBoundedAmount(rt,entry,profile,action,basis);
                    success = amount > 0 && rt.Boss.Defense.Execute(this,rt,profile.Id,action,amount,now);
                }
                else
                {
                    amount = MawAmount(rt,entry,profile,action.ChannelId);
                    var pulses = rt.Boss.Sequence.Reset();
                    if (profile.Id == "boss_maw.head")
                    {
                        for (int wave = 0; wave < 2; wave++)
                        {
                            var waveAction = profile.Actions[wave];
                            pulses.Add(new BossScheduledPulse(waveAction,origin,origin + direction * 6f,amount * .5f,BossMagic(rt),waveAction.DelayMillis));
                        }
                    }
                    else pulses.Add(new BossScheduledPulse(action,origin,origin + direction * (action.RangeMilli / 1000f),amount,BossMagic(rt),0));
                    success = BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,1);
                }
                if (success)
                {
                    MawCommit(rt,action,now);
                    if (profile.Id == "boss_maw.charm") { state.CharmHits = 0; state.CharmUntil = 0; }
                }
            }
            if (kind == BossEvent.MainHit && BossFind(rt,"boss_maw.stage2",out _,out _))
            {
                state.CycleUntil = now + 4f;
                if (++state.CycleHits < 3) return;
                state.CycleHits = 0; state.CycleUntil = 0;
                MawCycle(rt,origin,direction,point,low,now);
            }
        }
        private void MawCycle(HeroRuntime rt, Vector3 origin, Vector3 direction, Vector3 hit, bool low, float now)
        {
            if (BossFind(rt,"boss_maw.stage2",out var entry,out var profile))
            {
                var gate = profile.Actions[0];
                if (MawAvailable(rt,gate,now))
                {
                    var pulses = rt.Boss.Sequence.Reset();
                    float amount = MawAmount(rt,entry,profile,gate.ChannelId);
                    if (low)
                    {
                        var fan = profile.Actions[1];
                        pulses.Add(new BossScheduledPulse(fan,origin,origin + direction * 3f,amount * .5f,BossMagic(rt),0));
                        pulses.Add(new BossScheduledPulse(fan,origin,origin - direction * 3f,amount * .5f,BossMagic(rt),0));
                    }
                    else pulses.Add(new BossScheduledPulse(gate,origin,origin + direction * 3f,amount,BossMagic(rt),0));
                    if (BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,1)) MawCommit(rt,gate,now);
                }
            }
            if (BossFind(rt,"boss_maw.stage3",out entry,out profile))
            {
                var action = profile.Actions[0];
                if (MawAvailable(rt,action,now) && BossGround(origin,low ? origin : hit,8f,out var center))
                {
                    var pulses = rt.Boss.Sequence.Reset();
                    pulses.Add(new BossScheduledPulse(action,center,center,MawAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),500));
                    if (BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,1)) MawCommit(rt,action,now);
                }
            }
            if (!BossFind(rt,"boss_maw.stage6",out entry,out profile)) return;
            var hunt = profile.Actions[0];
            if (!MawAvailable(rt,hunt,now)) return;
            var shape = low ? profile.Actions[1] : hunt;
            Vector3 first = low ? direction : Quaternion.AngleAxis(-30f,Vector3.up) * direction;
            Vector3 second = low ? -direction : Quaternion.AngleAxis(30f,Vector3.up) * direction;
            float damage = MawAmount(rt,entry,profile,hunt.ChannelId) * .5f;
            float cap = MawAmount(rt,entry,profile,"MawHuntAbsorb");
            var sequence = rt.Boss.Sequence.Reset();
            sequence.Add(new BossScheduledPulse(shape,origin,origin + first * 3.5f,damage,BossMagic(rt),0,absorbRatio:.45f,absorbCap:cap));
            sequence.Add(new BossScheduledPulse(shape,origin,origin + second * 3.5f,damage,BossMagic(rt),200,absorbRatio:.45f,absorbCap:cap));
            if (BossReserveSequence(rt,entry.SetId,profile.Id,sequence,now,1)) MawCommit(rt,hunt,now);
        }
        private static void MawExpire(MawCombat state, float now)
        {
            if (now >= state.CycleUntil) { state.CycleHits = 0; state.CycleUntil = 0; }
            if (now >= state.CharmUntil) { state.CharmHits = 0; state.CharmUntil = 0; }
            if (now >= state.ArrivalUntil) state.ArrivalUntil = 0;
        }
        private void TickMawBoss(HeroRuntime rt, float now)
        {
            if (_mawCombat.TryGetValue(rt,out var state)) MawExpire(state,now);
            TickBossBigChomp(rt);
        }
        private void ClearMawBoss(HeroRuntime rt, bool preserveRewards = false)
        {
            if (_mawCombat.TryGetValue(rt,out var state))
            { _mawCombat.Remove(rt); state.Reset(); _mawCombatPool.Return(state); }
            ClearBossBigChomp(rt);
        }
    }
}

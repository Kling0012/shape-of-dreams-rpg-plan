using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class PolarisCombat
        {
            internal readonly long[] Seen = new long[256];
            internal readonly int[] SeenNext = new int[4];
            internal readonly float[] Ready = new float[9];
            internal int Mask, Mode, Cycle;
            internal float GateUntil, GuardReady, StompReady, SixReady, CounterUntil, SealUntil, CycleUntil, ModeRefresh;
            internal bool GuardLive;
            internal Vector3 Seal;
            internal long ModeVisual, SealVisual, CycleVisual;
            internal bool Admit(int kind, long id)
            {
                int start = kind * 64;
                for (int i = start; i < start + 64; i++) if (Seen[i] == id) return false;
                Seen[start + SeenNext[kind]] = id; SeenNext[kind] = (SeenNext[kind] + 1) % 64;
                return true;
            }
            internal void Reset()
            {
                Array.Clear(Seen, 0, Seen.Length); Array.Clear(SeenNext, 0, SeenNext.Length); Array.Clear(Ready, 0, Ready.Length);
                Mask = Mode = Cycle = 0; GateUntil = GuardReady = StompReady = SixReady = CounterUntil = SealUntil = CycleUntil = ModeRefresh = 0;
                GuardLive = false; Seal = Vector3.zero; ModeVisual = SealVisual = CycleVisual = 0;
            }
        }
        private static readonly string[] PolarisProfiles =
        {
            "boss_polaris.weapon", "boss_polaris.armor", "boss_polaris.charm", "boss_polaris.head", "boss_polaris.hands",
            "boss_polaris.feet", "boss_polaris.stage2", "boss_polaris.stage3", "boss_polaris.stage6",
        };
        private readonly BossObjectPool<PolarisCombat> _polarisPool = new BossObjectPool<PolarisCombat>(64, () => new PolarisCombat());
        private readonly Dictionary<HeroRuntime, PolarisCombat> _polarisCombat = new Dictionary<HeroRuntime, PolarisCombat>(64);
        private static float PolarisAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channelId, bool maxHp = false)
            => Math.Max(0f, maxHp ? rt.Hero.maxHealth : Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower))
                * BossCoefficient(entry, profile, channelId);
        private static Vector3 PolarisForward(HeroRuntime rt, bool memory)
        {
            Vector3 direction = memory && rt.Boss.MemoryDirectionValid ? rt.Boss.MemoryDirection : rt.Hero.transform.forward;
            direction.y = 0;
            return BossFinite(direction) && direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }
        private int PolarisMask(HeroRuntime rt)
        {
            int mask = 0;
            for (int i = 0; i < PolarisProfiles.Length; i++) if (BossFind(rt, PolarisProfiles[i], out _, out _)) mask |= 1 << i;
            return mask;
        }
        private PolarisCombat PolarisState(HeroRuntime rt)
        {
            int mask = PolarisMask(rt);
            if (_polarisCombat.TryGetValue(rt, out var state))
            {
                if ((state.Mask & ~mask) != 0) { ClearPolarisBoss(rt); state = null; }
                else { state.Mask = mask; return state; }
            }
            if (mask == 0 || _polarisCombat.Count >= 64 || (state = _polarisPool.Rent()) == null) return null;
            state.Reset(); state.Mask = mask; _polarisCombat.Add(rt, state);
            return state;
        }
        private void DispatchPolarisBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (!NetworkServer.active || rt.Boss.Build == null || !BossAlive(rt.Hero) || activation <= 0) return;
            int input = kind == BossEvent.MainHit ? 0 : kind == BossEvent.MemoryUse ? 1 : kind == BossEvent.MovementCompleted ? 2
                : kind == BossEvent.NativeDamageTaken ? 3 : -1;
            if (input < 0 || input == 3 && (victim == null || victim.GetRelation(rt.Hero) != EntityRelation.Enemy)) return;
            var state = PolarisState(rt);
            if (state == null || !state.Admit(input, activation)) return;
            PolarisObserve(rt, state, now);
            if (kind == BossEvent.NativeDamageTaken && state.CounterUntil > now)
            {
                state.CounterUntil = 0;
                if (BossFind(rt, PolarisProfiles[3], out var counterEntry, out var counterProfile)
                    && BossAlive(victim) && (victim.position - rt.Hero.agentPosition).sqrMagnitude <= 64f)
                {
                    var reply = counterProfile.Actions[1];
                    var origin = rt.Hero.agentPosition;
                    rt.Boss.Projectiles.Execute(this, rt, BossProfiles.PolarisSetId, reply, origin, victim.position,
                        PolarisAmount(rt, counterEntry, counterProfile, reply.ChannelId), BossMagic(rt), now, profile: counterProfile.Id);
                }
            }
            PolarisPhase(rt, state, kind, point, now);
            for (int i = 0; i < 6; i++)
            {
                if ((state.Mask & (1 << i)) == 0 || now < state.Ready[i]
                    || !BossFind(rt, PolarisProfiles[i], out var entry, out var profile)) continue;
                var action = profile.Actions[0];
                if (action.Event != kind) continue;
                var center = kind == BossEvent.MovementCompleted ? point : rt.Hero.agentPosition;
                if (!BossFinite(center)) continue;
                bool success;
                if (i == 0)
                    success = rt.Boss.Projectiles.Execute(this, rt, entry.SetId, action, center, center + PolarisForward(rt, false) * 8f,
                        PolarisAmount(rt, entry, profile, action.ChannelId), BossMagic(rt), now, profile: profile.Id, spreadDegrees: 12f);
                else if (i == 1 || i == 3)
                {
                    success = rt.Boss.Defense.Execute(this, rt, profile.Id, action, PolarisAmount(rt, entry, profile, action.ChannelId, true), now, linearDecay: i == 1);
                    if (success && i == 3) state.CounterUntil = now + 1f;
                }
                else
                {
                    if (i != 5 && !BossGround(rt.Hero.agentPosition, point, 6f, out center)) continue;
                    var pulses = rt.Boss.Sequence.Reset();
                    int count = i == 5 ? 1 : 3;
                    float amount = PolarisAmount(rt, entry, profile, action.ChannelId) / count;
                    float heal = i == 2 ? PolarisAmount(rt, entry, profile, profile.Actions[1].ChannelId, true) : 0;
                    for (int n = 0; n < count; n++)
                    {
                        var pulseCenter = center;
                        if (i == 4)
                        {
                            float angle = n * 120f * Mathf.Deg2Rad;
                            var requested = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.5f;
                            if (!BossGround(center, requested, 1.5f, out pulseCenter)) continue;
                        }
                        pulses.Add(new BossScheduledPulse(action, pulseCenter, pulseCenter, amount, BossMagic(rt),
                            action.DelayMillis + n * action.IntervalMillis, healOnFirstHit: heal));
                    }
                    success = pulses.Count > 0 && BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, 1);
                }
                if (success) state.Ready[i] = now + action.CooldownMillis / 1000f;
            }
        }
        private void PolarisPhase(HeroRuntime rt, PolarisCombat state, BossEvent kind, Vector3 point, float now)
        {
            if ((state.Mask & 64) == 0 || !BossFind(rt, PolarisProfiles[6], out var entry, out var profile)) return;
            if (kind == BossEvent.MainHit)
            {
                if (state.Mode == 1 && state.SealUntil > now) PolarisStomp(rt, state, entry, profile, rt.Hero.agentPosition, now);
                return;
            }
            if (kind != BossEvent.MemoryUse && kind != BossEvent.MovementCompleted || now < state.GateUntil) return;
            state.GateUntil = now + .5f;
            if (kind == BossEvent.MovementCompleted)
            {
                if (state.Mode == 0)
                {
                    PolarisEnterBeast(rt, state, rt.Boss.MovementOrigin, now, rt.Boss.MovementOriginValid);
                }
                PolarisStomp(rt, state, entry, profile, point, now);
                return;
            }
            bool finishing = state.Mode == 1 && state.Cycle == 2 && now < state.CycleUntil;
            state.Mode = 0;
            bool replaced = finishing && PolarisFinish(rt, state, now);
            if (finishing) { state.Cycle = 0; state.CycleUntil = 0; PolarisCycleVisual(rt, state, now); }
            else if ((state.Mask & 256) != 0 && state.Cycle == 0)
            { state.Cycle = 1; state.CycleUntil = now + 8f; PolarisCycleVisual(rt, state, now); }
            PolarisClearSeal(rt, state, now);
            PolarisModeVisual(rt, state, now);
            if (replaced || now < state.GuardReady) return;
            var guard = profile.Actions[1];
            if (rt.Boss.Defense.Execute(this, rt, profile.Id, guard, PolarisAmount(rt, entry, profile, guard.ChannelId, true), now))
            { state.GuardLive = true; state.GuardReady = now + 8f; }
        }
        private void PolarisEnterBeast(HeroRuntime rt, PolarisCombat state, Vector3 departure, float now, bool sealAllowed = true)
        {
            if (state.Mode != 0) return;
            state.Mode = 1; state.GuardLive = false;
            rt.Boss.Defense.CancelSource(this, rt, PolarisProfiles[6]);
            if ((state.Mask & 128) != 0 && sealAllowed && BossFinite(departure))
            {
                PolarisClearSeal(rt, state, now); state.Seal = departure; state.SealUntil = now + 4f;
                state.SealVisual = ++rt.Boss.NextId;
                PublishBossVisual(rt, state.SealVisual, 1, departure, departure, .65f, now, state.SealUntil, element: BossElement.Light);
            }
            if (state.Cycle == 1 && now < state.CycleUntil) { state.Cycle = 2; PolarisCycleVisual(rt, state, now); }
            PolarisModeVisual(rt, state, now);
        }
        private bool PolarisSealOrigin(HeroRuntime rt, PolarisCombat state, float now, out Vector3 origin)
        {
            origin = rt.Hero.agentPosition;
            if (state.SealUntil <= now) return false;
            if (!BossFinite(state.Seal) || (state.Seal - origin).sqrMagnitude > 36f)
            { PolarisClearSeal(rt, state, now); return false; }
            origin = state.Seal; return true;
        }
        private void PolarisStomp(HeroRuntime rt, PolarisCombat state, BossMoveEntry entry, BossMoveProfile profile, Vector3 landing, float now)
        {
            if (now < state.StompReady || !BossFinite(landing)) return;
            bool seal = PolarisSealOrigin(rt, state, now, out var origin);
            if (!seal) origin = landing;
            var action = profile.Actions[2]; var pulses = rt.Boss.Sequence.Reset();
            pulses.Add(new BossScheduledPulse(action, origin, origin, PolarisAmount(rt, entry, profile, action.ChannelId), BossMagic(rt), seal ? 350 : action.DelayMillis));
            if (!BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, 4)) return;
            state.StompReady = now + 4f;
            if (seal) PolarisClearSeal(rt, state, now);
        }
        private bool PolarisFinish(HeroRuntime rt, PolarisCombat state, float now)
        {
            if (now < state.SixReady || !BossFind(rt, PolarisProfiles[8], out var entry, out var profile)) return false;
            if (!rt.Boss.MemoryDirectionValid || !BossFinite(rt.Boss.MemoryDirection) || rt.Boss.MemoryDirection.Flattened().sqrMagnitude <= .0001f) return false;
            PolarisSealOrigin(rt, state, now, out var origin);
            var direction = PolarisForward(rt, true); var spear = profile.Actions[1]; var stomp = profile.Actions[2];
            float amount = PolarisAmount(rt, entry, profile, spear.ChannelId) / 2f;
            var terminal = new BossScheduledPulse(stomp, origin, origin, amount, BossMagic(rt), 500);
            if (!rt.Boss.Projectiles.Execute(this, rt, entry.SetId, spear, origin, origin + direction * 8f, amount, BossMagic(rt), now,
                profile: profile.Id, terminalPulse: terminal, terminalAtWall: true)) return false;
            state.SixReady = now + 18f; state.GuardLive = false;
            rt.Boss.Defense.CancelSource(this, rt, PolarisProfiles[6]);
            var guard = profile.Actions[3];
            rt.Boss.Defense.Execute(this, rt, profile.Id, guard, PolarisAmount(rt, entry, profile, guard.ChannelId, true), now, linearDecay: true);
            PolarisClearSeal(rt, state, now);
            return true;
        }
        private void PolarisObserve(HeroRuntime rt, PolarisCombat state, float now)
        {
            if (state.SealUntil > 0 && state.SealUntil <= now) PolarisClearSeal(rt, state, now);
            if (state.Cycle != 0 && state.CycleUntil <= now)
            { state.Cycle = 0; state.CycleUntil = 0; PolarisCycleVisual(rt, state, now); }
            if (state.CounterUntil <= now) state.CounterUntil = 0;
            if (state.GuardLive && !rt.Boss.Defense.SourceActive(this, rt, PolarisProfiles[6], now))
            {
                state.GuardLive = false;
                if ((state.Mask & 128) != 0) PolarisEnterBeast(rt, state, rt.Hero.agentPosition, now);
            }
        }
        private void PolarisClearSeal(HeroRuntime rt, PolarisCombat state, float now)
        {
            if (state.SealVisual != 0) PublishBossVisual(rt, state.SealVisual, 1, state.Seal, state.Seal, 0, now, now, true);
            state.SealVisual = 0; state.SealUntil = 0;
        }
        private void PolarisModeVisual(HeroRuntime rt, PolarisCombat state, float now, bool remove = false)
        {
            if (state.ModeVisual == 0 && !remove) state.ModeVisual = ++rt.Boss.NextId;
            if (state.ModeVisual == 0) return;
            var center = rt.Hero.agentPosition;
            PublishBossVisual(rt, state.ModeVisual, 9, center, center, .5f, now, remove ? now : now + 2f, remove,
                element: state.Mode == 0 ? BossElement.Light : BossElement.Neutral, count: state.Mode + 1, targetNetId: rt.Hero.netId);
            state.ModeRefresh = now + 1f;
        }
        private void PolarisCycleVisual(HeroRuntime rt, PolarisCombat state, float now)
        {
            if (state.CycleVisual == 0 && state.Cycle != 0) state.CycleVisual = ++rt.Boss.NextId;
            if (state.CycleVisual == 0) return;
            var center = rt.Hero.agentPosition;
            PublishBossVisual(rt, state.CycleVisual, 9, center, center, state.Cycle, now, state.Cycle == 0 ? now : state.CycleUntil,
                state.Cycle == 0, element: BossElement.Light, count: state.Cycle, budget: state.Cycle, targetNetId: rt.Hero.netId);
            if (state.Cycle == 0) state.CycleVisual = 0;
        }
        private void TickPolarisBoss(HeroRuntime rt, float now)
        {
            if (!_polarisCombat.TryGetValue(rt, out var state)) return;
            if (rt.Boss.Build == null || !BossAlive(rt.Hero)) { ClearPolarisBoss(rt); return; }
            int mask = PolarisMask(rt);
            if ((state.Mask & ~mask) != 0) { ClearPolarisBoss(rt); return; }
            state.Mask = mask; PolarisObserve(rt, state, now);
            if ((mask & 64) != 0 && state.ModeRefresh <= now) PolarisModeVisual(rt, state, now);
        }
        private void ClearPolarisBoss(HeroRuntime rt, bool preserveRewards = false)
        {
            if (!_polarisCombat.TryGetValue(rt, out var state)) return;
            float now = Time.time;
            PolarisClearSeal(rt, state, now); state.Cycle = 0; PolarisCycleVisual(rt, state, now); PolarisModeVisual(rt, state, now, true);
            for (int i = 0; i < PolarisProfiles.Length; i++)
            {
                BossCancelProfileReservations(rt, BossProfiles.PolarisSetId, PolarisProfiles[i]);
                rt.Boss.Defense.CancelSource(this, rt, PolarisProfiles[i]);
            }
            _polarisCombat.Remove(rt); state.Reset(); _polarisPool.Return(state);
        }
    }
}

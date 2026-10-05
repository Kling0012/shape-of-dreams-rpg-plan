using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class PrimusCombat
        {
            internal readonly BossNativeSeen MainInputs = new BossNativeSeen();
            internal readonly BossNativeSeen MemoryInputs = new BossNativeSeen();
            internal readonly BossNativeSeen MovementInputs = new BossNativeSeen();
            internal readonly BossNativeSeen DamageInputs = new BossNativeSeen();
            internal int Mode = -1, SeenKinds, EquippedMask;
            internal float CycleUntil, GlyphUntil, ModeRefresh;
            internal Vector3 Glyph;
            internal long ModeVisual, CycleVisual, GlyphVisual;
            internal long Epoch;
            internal void Reset()
            {
                MainInputs.Clear(); MemoryInputs.Clear(); MovementInputs.Clear(); DamageInputs.Clear();
                Mode=-1; SeenKinds=EquippedMask=0; CycleUntil=GlyphUntil=ModeRefresh=0; Glyph=default;
                ModeVisual=CycleVisual=GlyphVisual=Epoch=0;
            }
        }
        private static readonly string[] PrimusProfiles =
        {
            "boss_primus_aeron.weapon", "boss_primus_aeron.armor", "boss_primus_aeron.charm",
            "boss_primus_aeron.head", "boss_primus_aeron.hands", "boss_primus_aeron.feet",
            "boss_primus_aeron.stage2", "boss_primus_aeron.stage3", "boss_primus_aeron.stage6",
        };
        private readonly Dictionary<HeroRuntime, PrimusCombat> _primusCombat = new Dictionary<HeroRuntime, PrimusCombat>(64);
        private readonly BossObjectPool<PrimusCombat> _primusPool = new BossObjectPool<PrimusCombat>(64,()=>new PrimusCombat());
        private PrimusCombat PrimusState(HeroRuntime rt)
        {
            if (!_primusCombat.TryGetValue(rt, out var state))
            {
                if(_primusCombat.Count>=64 || (state=_primusPool.Rent())==null) return null;
                state.Epoch=rt.ShieldEquipmentEpoch; _primusCombat.Add(rt,state);
                for (int i = 0; i < PrimusProfiles.Length; i++)
                    if (BossFind(rt, PrimusProfiles[i], out _, out _)) state.EquippedMask |= 1 << i;
            }
            return state;
        }
        private static bool PrimusAvailable(HeroRuntime rt, BossAction action, float now)
            => rt.Boss.Ready.TryGetValue(action.RuntimeKey,out var due) ? now>=due : rt.Boss.Ready.Count<128;
        private static void PrimusCommit(HeroRuntime rt, BossAction action, float now)
            => BossReady(rt,action.RuntimeKey,now,action.CooldownMillis);
        private static Vector3 PrimusDirection(HeroRuntime rt, BossEvent kind)
        {
            Vector3 direction = kind == BossEvent.MemoryUse && rt.Boss.MemoryDirectionValid
                ? rt.Boss.MemoryDirection : rt.Hero.transform.forward;
            direction.y = 0;
            return BossFinite(direction) && direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }
        private static float PrimusAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channelId, bool shield = false)
        {
            for(int i=0;i<profile.Channels.Count;i++)
            {
                var channel=profile.Channels[i];
                if (channel.ChannelId == channelId)
                    return Math.Max(0f, shield ? rt.Hero.maxHealth : Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower))
                        * Math.Min(BossCoefficient(entry, profile, channel.ChannelId), channel.CapMilli / 100000f);
            }
            return 0;
        }
        private void DispatchPrimusBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (!NetworkServer.active || rt.Boss.Build == null || !BossAlive(rt.Hero) || activation <= 0) return;
            bool owned = false;
            for(int i=0;i<rt.Boss.Build.BossMoves.Count;i++)
                if(rt.Boss.Build.BossMoves[i].SetId==BossProfiles.PrimusSetId) { owned=true; break; }
            if (!owned) return;
            var state=PrimusState(rt); if(state==null) return;
            bool admitted = kind == BossEvent.MainHit ? state.MainInputs.Add(activation)
                : kind == BossEvent.MemoryUse ? state.MemoryInputs.Add(activation)
                : kind == BossEvent.MovementCompleted ? state.MovementInputs.Add(activation)
                : kind == BossEvent.NativeDamageTaken && state.DamageInputs.Add(activation);
            if (!admitted) return;
            if (kind == BossEvent.MainHit || kind == BossEvent.MemoryUse || kind == BossEvent.MovementCompleted)
                PrimusPhaseInput(rt, state, kind, point, now);
            for(int bossMoveIndex=0;bossMoveIndex<rt.Boss.Build.BossMoves.Count;bossMoveIndex++)
            {
                var entry=rt.Boss.Build.BossMoves[bossMoveIndex];
                if (entry.SetId != BossProfiles.PrimusSetId || entry.ProfileId == PrimusProfiles[6]
                    || entry.ProfileId == PrimusProfiles[7] || entry.ProfileId == PrimusProfiles[8]
                    || !BossProfiles.TryGetMove(entry.ProfileId, out var profile)) continue;
                var action = profile.Actions[0];
                if (action.Event != kind || !PrimusAvailable(rt, action, now)) continue;
                Vector3 center = kind == BossEvent.MovementCompleted ? point : rt.Hero.agentPosition;
                if (!BossFinite(center)) continue;
                var direction = PrimusDirection(rt, kind);
                var end = center + direction * (action.RangeMilli / 1000f);
                bool success;
                if (entry.ProfileId == PrimusProfiles[1])
                    success = rt.Boss.Defense.Execute(this, rt, profile.Id, action, PrimusAmount(rt, entry, profile, action.ChannelId, true), now, linearDecay: true);
                else if (entry.ProfileId == PrimusProfiles[2])
                    success = rt.Boss.Projectiles.Execute(this, rt, entry.SetId, action, center, end,
                        PrimusAmount(rt, entry, profile, action.ChannelId), BossMagic(rt), now, profile: profile.Id,
                        chainCount: 3, chainRadius: 3f, chainDelayMillis: 150, expireAtLifetime: true);
                else
                {
                    if (entry.ProfileId == PrimusProfiles[4])
                    {
                        if (!BossGround(center, point, action.RangeMilli / 1000f, out var legal)) continue;
                        center = legal; end = legal;
                    }
                    if(action.Count>32) continue;
                    var pulses=rt.Boss.Sequence.Reset();
                    float amount = PrimusAmount(rt, entry, profile, action.ChannelId) / action.Count;
                    bool magic = BossMagic(rt);
                    for (int i=0;i<action.Count;i++)
                        if(!pulses.Add(new BossScheduledPulse(action,center,end,amount,magic,action.DelayMillis+i*action.IntervalMillis))) return;
                    success = BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, action.MaxInstances);
                }
                if (success) PrimusCommit(rt, action, now);
            }
        }
        private void PrimusPhaseInput(HeroRuntime rt, PrimusCombat state, BossEvent kind, Vector3 point, float now)
        {
            if (!BossFind(rt, PrimusProfiles[6], out var entry, out var profile)) return;
            PrimusExpire(rt, state, now);
            if (kind == BossEvent.MovementCompleted && BossFind(rt, PrimusProfiles[7], out _, out var glyphProfile))
            {
                PrimusClearGlyph(rt, state, now);
                if (rt.Boss.MovementOriginValid && BossFinite(rt.Boss.MovementOrigin))
                {
                    state.Glyph = rt.Boss.MovementOrigin;
                    state.GlyphUntil = now + glyphProfile.Actions[0].LifetimeMillis / 1000f;
                    state.GlyphVisual = ++rt.Boss.NextId;
                    PublishBossVisual(rt, state.GlyphVisual, 1, state.Glyph, state.Glyph, .65f, now, state.GlyphUntil, element: BossElement.Light);
                }
            }
            var direction = PrimusDirection(rt, kind);
            bool finisher = PrimusCycleInput(rt, state, kind, direction, now);
            var gate = profile.Actions[0];
            if (!PrimusAvailable(rt, gate, now)) return;
            PrimusCommit(rt, gate, now);
            state.Mode = kind == BossEvent.MainHit ? 0 : kind == BossEvent.MemoryUse ? 1 : 2;
            PrimusModeVisual(rt, state, now);
            if (finisher) return;
            var shared = profile.Actions[1];
            if (!PrimusAvailable(rt, shared, now)) return;
            var action = profile.Actions[1 + state.Mode];
            Vector3 origin = kind == BossEvent.MovementCompleted ? point : rt.Hero.agentPosition;
            bool glyph = kind != BossEvent.MovementCompleted && PrimusGlyphOrigin(rt, state, now, out origin);
            int delay = glyph ? 350 : 0;
            var end = origin + direction * (action.RangeMilli / 1000f);
            var pulses=rt.Boss.Sequence.Reset();
            pulses.Add(new BossScheduledPulse(action,origin,end,PrimusAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),delay));
            if (!BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,1)) return;
            PrimusCommit(rt, shared, now);
            if (glyph) PrimusClearGlyph(rt, state, now);
        }
        // The distinct-input cycle is independent of the ordinary mode's 0.5s gate.
        private bool PrimusCycleInput(HeroRuntime rt, PrimusCombat state, BossEvent kind, Vector3 direction, float now)
        {
            if (!BossFind(rt, PrimusProfiles[8], out var entry, out var profile)) return false;
            int bit = kind == BossEvent.MainHit ? 1 : kind == BossEvent.MemoryUse ? 2 : 4;
            if ((state.SeenKinds & bit) != 0) return false;
            if (state.SeenKinds == 0) state.CycleUntil = now + profile.Actions[0].LifetimeMillis / 1000f;
            state.SeenKinds |= bit;
            PrimusCycleVisual(rt, state, now);
            if (state.SeenKinds != 7) return false;
            var gate = profile.Actions[0];
            if (!PrimusAvailable(rt, gate, now))
            {
                state.SeenKinds = 0; state.CycleUntil = 0;
                PrimusCycleVisual(rt, state, now);
                return false;
            }
            PrimusGlyphOrigin(rt, state, now, out var origin);
            float amount = PrimusAmount(rt, entry, profile, "PrimusConfluence") / 3f;
            bool magic = BossMagic(rt);
            var pulses=rt.Boss.Sequence.Reset();
            for (int i=0;i<3;i++)
            {
                var action = profile.Actions[1 + i];
                pulses.Add(new BossScheduledPulse(action,origin,origin+direction*(action.RangeMilli/1000f),amount,magic,action.DelayMillis));
            }
            if (BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, 1))
            {
                state.SeenKinds = 0; state.CycleUntil = 0;
                PrimusCycleVisual(rt, state, now);
                PrimusCommit(rt, gate, now);
                PrimusClearGlyph(rt, state, now);
                var shield = profile.Actions[4];
                rt.Boss.Defense.Execute(this, rt, profile.Id, shield, PrimusAmount(rt, entry, profile, shield.ChannelId, true), now, linearDecay: true);
            }
            else
            {
                // Failed admission neither consumes the third input nor queues a finisher.
                state.SeenKinds &= ~bit;
                PrimusCycleVisual(rt, state, now);
            }
            return true;
        }
        private bool PrimusGlyphOrigin(HeroRuntime rt, PrimusCombat state, float now, out Vector3 origin)
        {
            origin = rt.Hero.agentPosition;
            if (state.GlyphUntil <= now) return false;
            if ((state.Glyph - origin).sqrMagnitude > 36f || !BossFinite(state.Glyph))
            { PrimusClearGlyph(rt, state, now); return false; }
            origin = state.Glyph;
            return true;
        }
        private void PrimusClearGlyph(HeroRuntime rt, PrimusCombat state, float now)
        {
            if (state.GlyphVisual != 0)
                PublishBossVisual(rt, state.GlyphVisual, 1, state.Glyph, state.Glyph, 0, now, now, true);
            state.GlyphVisual = 0; state.GlyphUntil = 0;
        }
        private void PrimusModeVisual(HeroRuntime rt, PrimusCombat state, float now)
        {
            if (state.ModeVisual == 0 && state.Mode >= 0) state.ModeVisual = ++rt.Boss.NextId;
            if (state.ModeVisual == 0) return;
            var center = rt.Hero.agentPosition;
            var element = state.Mode == 1 ? BossElement.Light : state.Mode == 2 ? BossElement.Dark : BossElement.Neutral;
            PublishBossVisual(rt, state.ModeVisual, 9, center, center, .5f, now, state.Mode >= 0 ? now + 2f : now,
                state.Mode < 0, element: element, count: state.Mode + 1, targetNetId: rt.Hero.netId);
            state.ModeRefresh = now + 1f;
            if (state.Mode < 0) state.ModeVisual = 0;
        }
        private void PrimusCycleVisual(HeroRuntime rt, PrimusCombat state, float now)
        {
            if (state.CycleVisual == 0 && state.SeenKinds != 0) state.CycleVisual = ++rt.Boss.NextId;
            if (state.CycleVisual == 0) return;
            int count = (state.SeenKinds & 1) + ((state.SeenKinds >> 1) & 1) + ((state.SeenKinds >> 2) & 1);
            var center = rt.Hero.agentPosition;
            PublishBossVisual(rt, state.CycleVisual, 9, center, center, count, now, count > 0 ? state.CycleUntil : now,
                count == 0, element: BossElement.Light, count: count, budget: state.SeenKinds, targetNetId: rt.Hero.netId);
            if (count == 0) state.CycleVisual = 0;
        }
        private void PrimusExpire(HeroRuntime rt, PrimusCombat state, float now)
        {
            if (state.GlyphUntil > 0 && now >= state.GlyphUntil) PrimusClearGlyph(rt, state, now);
            if (state.SeenKinds != 0 && now >= state.CycleUntil)
            { state.SeenKinds = 0; state.CycleUntil = 0; PrimusCycleVisual(rt, state, now); }
        }
        private void PrimusCancelProfile(HeroRuntime rt, string id)
        {
            BossCancelProfileReservations(rt, BossProfiles.PrimusSetId, id);
            rt.Boss.Defense.CancelSource(this, rt, id);
            if (BossProfiles.TryGetMove(id, out var profile))
                for (int i=0;i<profile.Actions.Count;i++) rt.Boss.Ready.Remove(profile.Actions[i].RuntimeKey);
        }
        private void TickPrimusBoss(HeroRuntime rt, float now)
        {
            if (!_primusCombat.TryGetValue(rt, out var state)) return;
            if (rt.Boss.Build == null || !BossAlive(rt.Hero)) { ClearPrimusBoss(rt); return; }
            if(state.Epoch!=rt.ShieldEquipmentEpoch)
            {
                state.Epoch=rt.ShieldEquipmentEpoch;
                int equippedMask=0;
                for(int i=0;i<PrimusProfiles.Length;i++)
                {
                    if(BossFind(rt,PrimusProfiles[i],out _,out _)) equippedMask|=1<<i;
                    else if((state.EquippedMask&(1<<i))!=0) PrimusCancelProfile(rt,PrimusProfiles[i]);
                }
                state.EquippedMask=equippedMask;
                if(equippedMask==0) { ClearPrimusBoss(rt); return; }
                if((equippedMask&(1<<6))==0) { state.Mode=-1; PrimusModeVisual(rt,state,now); }
                if((equippedMask&(1<<7))==0) PrimusClearGlyph(rt,state,now);
                if((equippedMask&(1<<8))==0) { state.SeenKinds=0; state.CycleUntil=0; PrimusCycleVisual(rt,state,now); }
            }
            PrimusExpire(rt, state, now);
            if (state.Mode >= 0 && now >= state.ModeRefresh) PrimusModeVisual(rt, state, now);
        }
        private void ClearPrimusBoss(HeroRuntime rt, bool preserveRewards = false)
        {
            if (_primusCombat.TryGetValue(rt, out var state))
            {
                float now = Time.time;
                PrimusClearGlyph(rt, state, now);
                state.Mode = -1; PrimusModeVisual(rt, state, now);
                state.SeenKinds = 0; PrimusCycleVisual(rt, state, now);
                _primusCombat.Remove(rt);
                state.Reset(); _primusPool.Return(state);
            }
            foreach (var id in PrimusProfiles) PrimusCancelProfile(rt, id);
        }
    }
}

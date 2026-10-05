using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Only the normal movement continuation is observed. Position corrections, teleport,
    // forced displacement and generated movement never enter the voluntary-distance window.
    [HarmonyPatch(typeof(EntityControl), "DoMovementFrameUpdate")]
    internal static class ObliviaxVoluntaryMovement
    {
        internal struct Sample { internal Vector3 Position; internal bool Eligible; }
        private static void Prefix(EntityControl __instance, out Sample __state)
        {
            __state = default;
            if (!NetworkServer.active || !(__instance.entity is Hero hero)) return;
            __state.Position = hero.agentPosition;
            __state.Eligible = !__instance.isDisplacing && HostAuthority.NativeInstance?.ObliviaxMovementObservable(hero) == true;
        }
        private static void Postfix(EntityControl __instance, Sample __state)
        {
            if (NetworkServer.active && __instance.entity is Hero hero)
                HostAuthority.NativeInstance?.ObserveObliviaxVoluntaryMovement(hero, __state.Position, __state.Eligible && !__instance.isDisplacing);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class ObliviaxCombat
        {
            internal readonly BossNativeSeen Main = new BossNativeSeen(), Memory = new BossNativeSeen(), Movement = new BossNativeSeen();
            internal Vector3 Departure, Turret, OrbOrigin, OrbPoint;
            internal float AmbushUntil, TurretUntil, QuietStart, QuietDistance, LastObservation, OrbDue, OrbAmount;
            internal long AmbushVisual, TurretVisual, TurretActivation;
            internal bool QuietArmed, Observing, OrbMagic, OrbPending;
            internal int EquippedMask;
            internal void Reset()
            {
                Main.Clear(); Memory.Clear(); Movement.Clear();
                Departure = Turret = OrbOrigin = OrbPoint = default;
                AmbushUntil = TurretUntil = QuietStart = QuietDistance = LastObservation = OrbDue = OrbAmount = 0;
                AmbushVisual = TurretVisual = TurretActivation = 0;
                QuietArmed = Observing = OrbMagic = OrbPending = false; EquippedMask = 0;
            }
        }
        private static readonly string[] ObliviaxProfiles =
        {
            "boss_obliviax.weapon", "boss_obliviax.armor", "boss_obliviax.charm", "boss_obliviax.head",
            "boss_obliviax.hands", "boss_obliviax.feet", "boss_obliviax.stage2", "boss_obliviax.stage3", "boss_obliviax.stage6",
        };
        private readonly BossObjectPool<ObliviaxCombat> _obliviaxPool = new BossObjectPool<ObliviaxCombat>(64, () => new ObliviaxCombat());
        private readonly Dictionary<HeroRuntime, ObliviaxCombat> _obliviaxCombat = new Dictionary<HeroRuntime, ObliviaxCombat>(64);
        private ObliviaxCombat ObliviaxState(HeroRuntime rt)
        {
            if (_obliviaxCombat.TryGetValue(rt, out var state)) return state;
            state = _obliviaxPool.Rent(); if (state == null) return null;
            state.Reset();
            for (int i = 0; i < ObliviaxProfiles.Length; i++)
                if (BossFind(rt, ObliviaxProfiles[i], out _, out _)) state.EquippedMask |= 1 << i;
            _obliviaxCombat.Add(rt, state); return state;
        }
        private static bool ObliviaxAvailable(HeroRuntime rt, BossAction action, float now)
            => (!rt.Boss.Ready.TryGetValue(action.RuntimeKey, out var due) || now >= due)
                && (rt.Boss.Ready.ContainsKey(action.RuntimeKey) || rt.Boss.Ready.Count < 128);
        private static void ObliviaxCommit(HeroRuntime rt, BossAction action, float now)
            => BossReady(rt, action.RuntimeKey, now, action.CooldownMillis);
        private static float ObliviaxAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channel)
        {
            for (int i = 0; i < profile.Channels.Count; i++)
                if (profile.Channels[i].ChannelId == channel)
                    return Math.Max(0f, Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower))
                        * Math.Min(BossCoefficient(entry, profile, channel), profile.Channels[i].CapMilli / 100000f);
            return 0f;
        }
        private Entity ObliviaxNearest(HeroRuntime rt, Vector3 center, float range)
        {
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, center, range, EnemyFilter, rt.Hero);
            Entity best = null; float distance = range * range;
            try
            {
                int count = Math.Min(256, found.Count);
                for (int i = 0; i < count; i++)
                {
                    var target = found[i];
                    if (!BossAlive(target) || target.Status.hasUncollidable || target.GetRelation(rt.Hero) != EntityRelation.Enemy) continue;
                    float d = (target.agentPosition - center).Flattened().sqrMagnitude;
                    if (d > distance || d == distance && best != null && target.persistentNetId >= best.persistentNetId
                        || !BossGround(center, target.agentPosition, range, out _)) continue;
                    best = target; distance = d;
                }
            }
            finally { handle.Return(); }
            return best;
        }
        internal bool ObliviaxMovementObservable(Hero hero)
            => AttributionGeneratedOrigin() == GeneratedOrigin.None && BossAlive(hero)
                && NetworkedManagerBase<ZoneManager>.softInstance?.isInAnyTransition != true;
        internal void ObserveObliviaxVoluntaryMovement(Hero hero, Vector3 from, bool eligible)
        {
            if (!_runtimes.TryGetValue(hero, out var rt) || rt.Boss.Build == null
                || !BossFind(rt, ObliviaxProfiles[3], out _, out _)) return;
            var state = ObliviaxState(rt); if (state == null) return;
            float now = Time.time;
            if (!eligible || !BossFinite(from) || !BossFinite(hero.agentPosition))
            {
                if (state.Observing) state.QuietStart += Math.Max(0f, now - state.LastObservation);
                state.LastObservation = now;
                return;
            }
            if (!state.Observing) { state.Observing = true; state.QuietStart = now; state.QuietDistance = 0; }
            state.LastObservation = now;
            float moved = BossDirectionDistance(from, hero.agentPosition);
            if (moved > .0001f && state.QuietArmed)
            { state.QuietArmed = false; state.QuietStart = now; state.QuietDistance = 0; }
            state.QuietDistance += moved;
            if (state.QuietDistance >= 1f)
            { state.QuietArmed = false; state.QuietStart = now; state.QuietDistance = 0; }
            else if (now - state.QuietStart >= 1f)
            { state.QuietArmed = true; state.QuietStart = now; state.QuietDistance = 0; }
        }
        private void DispatchObliviaxBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (!NetworkServer.active || rt.Boss.Build == null || !BossAlive(rt.Hero) || activation <= 0
                || AttributionGeneratedOrigin() != GeneratedOrigin.None) return;
            bool owned = false;
            var moves = rt.Boss.Build.BossMoves;
            for (int i = 0; i < moves.Count; i++) if (moves[i].SetId == BossProfiles.ObliviaxSetId) { owned = true; break; }
            if (!owned) return;
            var state = ObliviaxState(rt); if (state == null) return;
            bool admitted = kind == BossEvent.MainHit ? state.Main.Add(activation)
                : kind == BossEvent.MemoryUse ? state.Memory.Add(activation)
                : kind == BossEvent.MovementCompleted && state.Movement.Add(activation);
            if (!admitted) return;
            ObliviaxExpire(rt, state, now);
            var owner = rt.Hero.agentPosition;
            if (kind == BossEvent.MovementCompleted)
            { state.QuietArmed = state.Observing = false; state.QuietDistance = 0; state.LastObservation = now; }
            if (kind == BossEvent.MovementCompleted && rt.Boss.MovementOriginValid && BossFinite(rt.Boss.MovementOrigin)
                && BossFind(rt, ObliviaxProfiles[6], out _, out var ambush) && ObliviaxAvailable(rt, ambush.Actions[0], now))
            {
                ObliviaxRemoveAmbush(rt, state, now);
                state.Departure = rt.Boss.MovementOrigin; state.AmbushUntil = now + 3f; state.AmbushVisual = ++rt.Boss.NextId;
                PublishBossVisual(rt, state.AmbushVisual, 1, state.Departure, state.Departure, .65f, now, state.AmbushUntil, element: BossElement.Dark);
                ObliviaxCommit(rt, ambush.Actions[0], now);
            }
            // Old turret fires before a new ambush may replace it; the placing activation cannot shoot it.
            if (kind == BossEvent.MainHit && BossAlive(victim) && BossFinite(point))
            {
                if (state.TurretUntil > now && state.TurretActivation != activation
                    && BossFind(rt, ObliviaxProfiles[7], out var turretEntry, out var turretProfile)
                    && BossDirectionDistance(state.Turret, point) <= 8f && BossGround(state.Turret, point, 8f, out var turretPoint))
                {
                    var pulses = rt.Boss.Sequence.Reset(); var action = turretProfile.Actions[1];
                    pulses.Add(new BossScheduledPulse(action, turretPoint, turretPoint, ObliviaxAmount(rt, turretEntry, turretProfile, action.ChannelId), BossMagic(rt), 400));
                    if (BossReserveSequence(rt, BossProfiles.ObliviaxSetId, turretProfile.Id, pulses, now, 1)) ObliviaxRemoveTurret(rt, state, now);
                }
                if (state.AmbushUntil > now && BossFind(rt, ObliviaxProfiles[6], out var ambushEntry, out var ambushProfile))
                {
                    var action = ambushProfile.Actions[1]; var origin = state.Departure;
                    var pulses = rt.Boss.Sequence.Reset();
                    var direction = BossDirection(origin, point);
                    if (direction == Vector3.zero) direction = rt.Hero.transform.forward.Flattened().normalized;
                    pulses.Add(new BossScheduledPulse(action, origin, origin + direction * 8f, ObliviaxAmount(rt, ambushEntry, ambushProfile, action.ChannelId), BossMagic(rt), 0));
                    if (BossReserveSequence(rt, BossProfiles.ObliviaxSetId, ambushProfile.Id, pulses, now, 1))
                    {
                        ObliviaxRemoveAmbush(rt, state, now);
                        if (BossFind(rt, ObliviaxProfiles[7], out _, out _))
                        {
                            ObliviaxRemoveTurret(rt, state, now); state.Turret = origin; state.TurretUntil = now + 3f;
                            state.TurretActivation = activation; state.TurretVisual = ++rt.Boss.NextId;
                            PublishBossVisual(rt, state.TurretVisual, 6, origin, origin, .65f, now, state.TurretUntil, element: BossElement.Dark, count: 1);
                        }
                    }
                }
            }
            Entity nearest = null; bool searched = false;
            for (int i = 0; i < moves.Count; i++)
            {
                var entry = moves[i];
                if (entry.SetId != BossProfiles.ObliviaxSetId || entry.ProfileId == ObliviaxProfiles[6]
                    || entry.ProfileId == ObliviaxProfiles[7] || !BossProfiles.TryGetMove(entry.ProfileId, out var profile)) continue;
                var action = profile.Actions[0];
                if (action.Event != kind || !ObliviaxAvailable(rt, action, now)) continue;
                float amount = ObliviaxAmount(rt, entry, profile, action.ChannelId); bool success = false;
                if (profile.Id == ObliviaxProfiles[1]) success = rt.Boss.Defense.Execute(this, rt, profile.Id, action, amount, now);
                else if (profile.Id == ObliviaxProfiles[5])
                {
                    var target = ObliviaxNearest(rt, point, 3f); if (target == null) continue;
                    success = BossDamage(rt, target, amount, BossMagic(rt), BossElement.Dark);
                    if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                    rt.Boss.EnemyMovement.Execute(rt, target, owner, true, 1f, now, .25f);
                }
                else if (profile.Id == ObliviaxProfiles[3])
                {
                    if (!state.QuietArmed || state.OrbPending || !BossAlive(victim)
                        || !BossGround(owner, point, 8f, out var orbPoint)) continue;
                    success = rt.Boss.Projectiles.Execute(this, rt, entry.SetId, action, owner, orbPoint, amount / 2f, BossMagic(rt), now,
                        shotCount: 1, explodeAtEnd: true, terminalRadius: 1f, profile: profile.Id);
                    if (success)
                    {
                        state.QuietArmed = false; state.QuietStart = now; state.QuietDistance = 0;
                        state.OrbPending = true; state.OrbDue = now + .2f; state.OrbOrigin = owner; state.OrbPoint = orbPoint;
                        state.OrbAmount = amount / 2f; state.OrbMagic = BossMagic(rt);
                    }
                }
                else
                {
                    Vector3 center = owner, end = point;
                    if (action.Shape == BossShape.Line)
                    {
                        var direction = BossDirection(owner, point);
                        if (direction == Vector3.zero) direction = rt.Hero.transform.forward.Flattened().normalized;
                        end = owner + direction * (action.RangeMilli / 1000f);
                    }
                    if (profile.Id == ObliviaxProfiles[2] || profile.Id == ObliviaxProfiles[8])
                    {
                        if (!searched) { searched = true; nearest = ObliviaxNearest(rt, owner, 8f); }
                        if (nearest == null || !BossGround(owner, nearest.agentPosition, 8f, out center)) continue;
                        end = center;
                    }
                    if (profile.Id == ObliviaxProfiles[8])
                    {
                        var direction = rt.Boss.MemoryDirectionValid ? rt.Boss.MemoryDirection.Flattened().normalized : BossDirection(owner, center);
                        if (direction == Vector3.zero) direction = rt.Hero.transform.forward.Flattened().normalized;
                        var lateral = Vector3.Cross(Vector3.up, direction) * 2f;
                        if (!BossGround(center, center - lateral, 2f, out var left) || !BossGround(center, center + lateral, 2f, out var right)) continue;
                        var pulses = rt.Boss.Sequence.Reset();
                        pulses.Add(new BossScheduledPulse(action, center, center, amount / 3f, BossMagic(rt), 600));
                        pulses.Add(new BossScheduledPulse(action, left, left, amount / 3f, BossMagic(rt), 800));
                        pulses.Add(new BossScheduledPulse(action, right, right, amount / 3f, BossMagic(rt), 1000));
                        success = BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, 1);
                        if (success) rt.Boss.Defense.Execute(this, rt, profile.Id, profile.Actions[1], ObliviaxAmount(rt, entry, profile, profile.Actions[1].ChannelId), now);
                    }
                    else
                    {
                        var pulses = rt.Boss.Sequence.Reset();
                        pulses.Add(new BossScheduledPulse(action, center, end, amount, BossMagic(rt), action.DelayMillis,
                            stunMillis: profile.Id == ObliviaxProfiles[4] ? 250 : 0,
                            slowMillis: profile.Id == ObliviaxProfiles[2] ? 500 : 0, slowPct: profile.Id == ObliviaxProfiles[2] ? 20 : 0));
                        success = BossReserveSequence(rt, entry.SetId, profile.Id, pulses, now, 1);
                    }
                }
                if (success) ObliviaxCommit(rt, action, now);
                if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
            }
        }
        private void ObliviaxRemoveAmbush(HeroRuntime rt, ObliviaxCombat state, float now)
        {
            if (state.AmbushVisual != 0) PublishBossVisual(rt, state.AmbushVisual, 1, state.Departure, state.Departure, 0, now, now, true);
            state.AmbushVisual = 0; state.AmbushUntil = 0;
        }
        private void ObliviaxRemoveTurret(HeroRuntime rt, ObliviaxCombat state, float now)
        {
            if (state.TurretVisual != 0) PublishBossVisual(rt, state.TurretVisual, 6, state.Turret, state.Turret, 0, now, now, true);
            state.TurretVisual = state.TurretActivation = 0; state.TurretUntil = 0;
        }
        private void ObliviaxExpire(HeroRuntime rt, ObliviaxCombat state, float now)
        {
            if (state.AmbushUntil > 0 && (now >= state.AmbushUntil || !BossFind(rt, ObliviaxProfiles[6], out _, out _))) ObliviaxRemoveAmbush(rt, state, now);
            if (state.TurretUntil > 0 && (now >= state.TurretUntil || !BossFind(rt, ObliviaxProfiles[7], out _, out _))) ObliviaxRemoveTurret(rt, state, now);
        }
        private void TickObliviaxBoss(HeroRuntime rt, float now)
        {
            TickBossShout(rt);
            if (!_obliviaxCombat.TryGetValue(rt, out var state)) return;
            if (rt.Boss.Build == null || !BossAlive(rt.Hero)) { ClearObliviaxBoss(rt); return; }
            int mask = 0;
            for (int i = 0; i < ObliviaxProfiles.Length; i++)
                if (BossFind(rt, ObliviaxProfiles[i], out _, out _)) mask |= 1 << i;
                else if ((state.EquippedMask & (1 << i)) != 0)
                {
                    BossCancelProfileReservations(rt, BossProfiles.ObliviaxSetId, ObliviaxProfiles[i]);
                    rt.Boss.Defense.CancelSource(this, rt, ObliviaxProfiles[i]);
                    if (BossProfiles.TryGetMove(ObliviaxProfiles[i], out var profile))
                        for (int j = 0; j < profile.Actions.Count; j++) rt.Boss.Ready.Remove(profile.Actions[j].RuntimeKey);
                }
            state.EquippedMask = mask; ObliviaxExpire(rt, state, now);
            if ((mask & 8) == 0) { state.OrbPending = state.QuietArmed = state.Observing = false; BossCancelProfileReservations(rt, BossProfiles.ObliviaxSetId, "boss_obliviax.head.orb2"); }
            if (state.OrbPending && now >= state.OrbDue)
            {
                state.OrbPending = false;
                if (BossFind(rt, ObliviaxProfiles[3], out _, out var profile))
                    rt.Boss.Projectiles.Execute(this, rt, BossProfiles.ObliviaxSetId, profile.Actions[0], state.OrbOrigin, state.OrbPoint, state.OrbAmount, state.OrbMagic, now,
                        shotCount: 1, explodeAtEnd: true, terminalRadius: 1f, profile: "boss_obliviax.head.orb2");
            }
            if (mask == 0) ClearObliviaxBoss(rt);
        }
        private void ClearObliviaxBoss(HeroRuntime rt, bool preserveRewards = false)
        {
            ClearBossShout(rt);
            if (!_obliviaxCombat.TryGetValue(rt, out var state)) return;
            ObliviaxRemoveAmbush(rt, state, Time.time); ObliviaxRemoveTurret(rt, state, Time.time);
            for (int i = 0; i < ObliviaxProfiles.Length; i++)
            { BossCancelProfileReservations(rt, BossProfiles.ObliviaxSetId, ObliviaxProfiles[i]); rt.Boss.Defense.CancelSource(this, rt, ObliviaxProfiles[i]); }
            BossCancelProfileReservations(rt, BossProfiles.ObliviaxSetId, "boss_obliviax.head.orb2");
            _obliviaxCombat.Remove(rt); state.Reset(); _obliviaxPool.Return(state);
        }
    }
}

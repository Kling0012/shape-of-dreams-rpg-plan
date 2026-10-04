using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class BossClawPair
        {
            public long First, Second; public bool FirstRight, FirstHit, SecondHit, FirstDone, SecondDone;
            public bool FirstCanCollect, SecondCanCollect;
            public Vector3 Point;
        }

        private void BossNativeMainHit(HeroRuntime rt, long activation, Entity victim)
        {
            if (!BossEnsure(rt) || victim == null || victim.GetRelation(rt.Hero) != EntityRelation.Enemy
                || !rt.Boss.Ledger.Admit(BossEvent.MainHit, activation)) return;
            BossDispatch(rt, BossEvent.MainHit, activation, victim, victim.position, Time.time, false);
        }
        private void BossConfirmedMemoryUse(HeroRuntime rt, long activation, SkillTrigger skill)
        {
            if (!BossEnsure(rt) || skill == null || skill.owner != rt.Hero || FindMemory(rt.Hero, skill.GetType().Name) != skill
                || skill.type != SkillType.Normal && skill.type != SkillType.Ultimate
                || !rt.Boss.Ledger.Admit(BossEvent.MemoryUse, activation)) return;
            BossDispatch(rt, BossEvent.MemoryUse, activation, null, BossCursor(rt), Time.time, false);
        }
        private void BossMovementCompleted(HeroRuntime rt, long activation, Vector3 destination)
        {
            if (!BossEnsure(rt) || !BossFinite(destination) || !rt.Boss.Ledger.Admit(BossEvent.MovementCompleted, activation)) return;
            BossDispatch(rt, BossEvent.MovementCompleted, activation, null, destination, Time.time, false);
        }
        private void BossNativeDamage(HeroRuntime rt, long activation, Entity victim, bool incoming)
        {
            var kind = incoming ? BossEvent.NativeDamageTaken : BossEvent.NativeDamageDealt;
            if (!BossEnsure(rt) || victim == null || !rt.Boss.Ledger.Admit(kind, activation)) return;
            BossDispatch(rt, kind, activation, victim, incoming ? rt.Hero.agentPosition : victim.position, Time.time, false);
        }
        private void BossModeTransition(HeroRuntime rt, long activation, int mode)
        {
            if (!BossEnsure(rt) || !rt.Boss.Ledger.Admit(BossEvent.ModeTransition, activation)) return;
            rt.Boss.Mode = Math.Max(0, Math.Min(3, mode));
            rt.Boss.ModeUntil = 0;
            BossDispatch(rt, BossEvent.ModeTransition, activation, null, rt.Hero.agentPosition, Time.time, false);
        }

        private bool BossRegisterNativeDeployable(HeroRuntime rt, string profileId, Summon summon, SkillTrigger source)
        {
            if (!BossEnsure(rt) || !BossFind(rt, profileId, out var entry, out var profile)) return false;
            foreach (var action in profile.Actions)
                if (action.Mechanism == BossMechanism.Deployable)
                    return rt.Boss.Deployables.RegisterNative(this, rt, entry.SetId, summon, source, action, Time.time);
            return false;
        }

        private static void BossHysteriaStateEnded(HeroRuntime rt, long stateLife)
        {
            if (rt.Boss.HysteriaState != stateLife) return;
            rt.Boss.HysteriaState = 0;
            rt.Boss.PreviousClawLeft = false;
            rt.Boss.ClawPairs.Clear();
        }

        private bool BossFind(HeroRuntime rt, string id, out BossMoveEntry entry, out BossMoveProfile profile)
        {
            foreach (var e in rt.Powers.Build.BossMoves)
                if (e.ProfileId == id && BossProfiles.TryGetMove(id, out profile) && e.SetId == profile.SetId) { entry = e; return true; }
            entry = null; profile = null; return false;
        }
        private void BossDispatch(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now, bool forest)
        {
            long before = rt.Boss.NextId;
            // Collection always precedes this event's new attacks/planting, regardless of equip slot order.
            if (!forest)
                foreach (var e in rt.Powers.Build.BossMoves)
                    if (BossProfiles.TryGetMove(e.ProfileId, out var p) && p.SetId == e.SetId)
                        foreach (var a in p.Actions)
                            if (a.Event == kind && a.Payload == BossPayload.CollectBud)
                            {
                                if (a.RequiredMode >= 0 && a.RequiredMode != rt.Boss.Mode || a.RequiredMarks > 0 && rt.Boss.Ledger.MarkCount(victim, now, a.LedgerId ?? e.SetId) < a.RequiredMarks) continue;
                                var origin = kind == BossEvent.MovementCompleted ? rt.Hero.agentPosition : point;
                                var prefer = kind == BossEvent.MovementCompleted ? BossCursor(rt) : point;
                                var bud = rt.Boss.Fields.Collect(this, rt, e.SetId, origin, prefer, a.RangeMilli / 1000f, now, before);
                                if (bud != null)
                                {
                                    if (a.ConsumeMarks) rt.Boss.Ledger.ConsumeMarks(victim, a.RequiredMarks, now, a.LedgerId ?? e.SetId);
                                    BossMarch(rt, bud.Center, now);
                                }
                            }
            bool stomp = false; Vector3 stompPoint = point;
            bool arrival = kind == BossEvent.MainHit && rt.Boss.ArrivalUntil > now;
            if (kind == BossEvent.MainHit) rt.Boss.ArrivalUntil = 0;
            foreach (var entry in rt.Powers.Build.BossMoves)
            {
                if (!BossProfiles.TryGetMove(entry.ProfileId, out var profile) || entry.SetId != profile.SetId) continue;
                for (int index = 0; index < profile.Actions.Count; index++)
                {
                    var a = profile.Actions[index];
                    if (a.Event != kind || a.Payload == BossPayload.CollectBud || a.Payload == BossPayload.March
                        || profile.Id == "boss_demon.stage6" || profile.Id == "boss_demon.stage3" && a.Payload == BossPayload.Bud) continue;
                    if (a.RequiredMode >= 0 && a.RequiredMode != rt.Boss.Mode) continue;
                    if (a.Payload == BossPayload.Mode && activation == 0) continue;
                    var markedVictim = a.Anchor == BossAnchor.Marked ? rt.Boss.Ledger.NearestMark(rt.Hero.agentPosition, now, a.LedgerId ?? entry.SetId) : victim;
                    if (a.RequiredMarks > 0 && rt.Boss.Ledger.MarkCount(markedVictim, now, a.LedgerId ?? entry.SetId) < a.RequiredMarks) continue;
                    if (forest && profile.Id != "boss_demon.weapon" && profile.Id != "boss_demon.head" && profile.Id != "boss_demon.hands" && profile.Id != "boss_demon.feet") continue;
                    if (a.LedgerId == "DemonArrival" && a.Payload == BossPayload.Damage && !arrival) continue;
                    string key = rt.Boss.ActionKeys[a];
                    if (a.MainHits > 0 && !rt.Boss.Ledger.Advance(key, activation, a.MainHits, a.CounterLifetimeMillis / 1000f, now)) continue;
                    if (!BossReady(rt, key, now, a.CooldownMillis)) continue;
                    if (a.MainHits > 0) rt.Boss.Ledger.Consume(key);
                    Vector3 center = BossAnchorPoint(rt, a, entry.SetId, victim, point, now);
                    Vector3 end = BossCursor(rt);
                    if (a.Anchor == BossAnchor.Cursor && !BossGround(rt.Hero.agentPosition, center, a.RangeMilli / 1000f, out center)) continue;
                    float amount = BossAmount(rt, entry, profile, a.ChannelId);
                    bool executed = BossExecute(rt, entry.SetId, profile.Id, a, victim, center, end, amount, BossMagic(rt), now, activation);
                    if (executed && a.ConsumeMarks) rt.Boss.Ledger.ConsumeMarks(markedVictim, a.RequiredMarks, now, a.LedgerId ?? entry.SetId);
                    if (executed && profile.SetId == BossProfiles.DemonSetId && a.Mechanism == BossMechanism.ShapeAttack && a.Payload == BossPayload.Damage)
                    { if (!stomp) stompPoint = center; stomp = true; }
                }
            }
            if (stomp) BossPlantGrove(rt, stompPoint, now);
        }

        private static Vector3 BossAnchorPoint(HeroRuntime rt, BossAction a, string set, Entity victim, Vector3 point, float now)
        {
            switch (a.Anchor)
            {
                case BossAnchor.Owner: return rt.Hero.agentPosition;
                case BossAnchor.Hit: case BossAnchor.Destination: case BossAnchor.Collected: return point;
                case BossAnchor.Cursor: return BossCursor(rt);
                case BossAnchor.Marked: return rt.Boss.Ledger.NearestMark(rt.Hero.agentPosition, now, a.LedgerId ?? set)?.position ?? point;
                default: return victim != null ? victim.position : point;
            }
        }
        private bool BossExecute(HeroRuntime rt, string set, string profile, BossAction a, Entity victim, Vector3 center, Vector3 end, float amount, bool magic, float now, long activation = 0)
        {
            switch (a.Mechanism)
            {
                case BossMechanism.ShapeAttack:
                    if (a.DelayMillis > 0 || a.Count > 1) return rt.Boss.Fields.Reserve(this, rt, set, profile, a, center, end, amount, magic, now);
                    rt.Boss.Shapes.Execute(this, rt, a, center, end, amount, magic);
                    PublishBossVisual(rt, ++rt.Boss.NextId, 1, center, end, a.RadiusMilli / 1000f, now, now + 0.25f);
                    return true;
                case BossMechanism.Projectile: return rt.Boss.Projectiles.Execute(this, rt, set, a, center, end, amount, magic, now);
                case BossMechanism.Field: return rt.Boss.Fields.Reserve(this, rt, set, profile, a, center, end, amount, magic, now);
                case BossMechanism.Movement: return rt.Boss.Movement.Execute(this, rt, a, center, now);
                case BossMechanism.EnemyMovement:
                    rt.Boss.Shapes.Execute(this, rt, a, center, end, amount, magic);
                    return true;
                case BossMechanism.Deployable: return rt.Boss.Deployables.Execute(this, rt, set, a, center, end, amount, magic, now);
                case BossMechanism.Defense: return rt.Boss.Defense.Execute(this, rt, rt.Boss.ActionKeys[a], a, amount, now);
                case BossMechanism.Ledger:
                    if (a.Payload == BossPayload.ArrivalWindow) rt.Boss.ArrivalUntil = now + a.LifetimeMillis / 1000f;
                    else if (a.Payload == BossPayload.Mark) rt.Boss.Ledger.MarkTarget(victim, a.Count, a.LifetimeMillis / 1000f, now, a.LedgerId ?? set);
                    else if (a.Payload == BossPayload.Mode)
                    {
                        rt.Boss.Mode = (rt.Boss.Mode + 1) % Math.Min(4, Math.Max(1, a.Count));
                        if (activation != 0 && a.LifetimeMillis > 0) rt.Boss.ModeUntil = now + a.LifetimeMillis / 1000f;
                    }
                    PublishBossVisual(rt, ++rt.Boss.NextId, 9, center, end, a.RadiusMilli / 1000f, now, now + Math.Max(0.2f, a.LifetimeMillis / 1000f));
                    return true;
                default: return false;
            }
        }

        private void BossPlantGrove(HeroRuntime rt, Vector3 point, float now)
        {
            if (!BossFind(rt, "boss_demon.stage3", out var entry, out var profile)) return;
            foreach (var a in profile.Actions)
                if (a.Payload == BossPayload.Bud)
                {
                    if (!BossReady(rt, BossProfiles.DemonSetId + ".plant", now, a.CooldownMillis)) return;
                    if (!BossGround(rt.Hero.agentPosition, point, a.RangeMilli / 1000f, out var legal)) return;
                    rt.Boss.Fields.Reserve(this, rt, entry.SetId, profile.Id, a, legal, legal, BossAmount(rt, entry, profile, a.ChannelId), BossMagic(rt), now);
                    return;
                }
        }
        private void BossMarch(HeroRuntime rt, Vector3 collected, float now)
        {
            if (!BossFind(rt, "boss_demon.stage6", out var entry, out var profile)) return;
            var a = profile.Actions[0];
            Vector3 start = rt.Hero.agentPosition;
            Vector3 desired = collected;
            if ((collected - start).Flattened().sqrMagnitude < 0.0001f)
            {
                var dir = BossDirection(start, BossCursor(rt));
                if (dir == Vector3.zero) return;
                desired = start + dir * (a.MagnitudeMilli / 1000f);
            }
            if (!BossGround(start, desired, a.RangeMilli / 1000f, out var end) || (end - start).Flattened().sqrMagnitude < 0.0001f
                || !BossReady(rt, profile.Id + ".march", now, a.CooldownMillis)) return;
            var points = new Vector3[a.Count];
            for (int i = 0; i < points.Length; i++)
            {
                var requested = Vector3.Lerp(start, end, (i + 1f) / points.Length);
                points[i] = BossGround(start, requested, a.RangeMilli / 1000f, out var legal) ? legal : new Vector3(float.NaN, 0, 0);
            }
            var replant = profile.Actions[1];
            float high = Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower);
            float marchDamage = high * BossCoefficient(entry, profile, a.ChannelId) / a.Count;
            float damage = high * BossCoefficient(entry, profile, replant.ChannelId);
            bool magic = BossMagic(rt);
            if (!rt.Boss.Fields.Reserve(this, rt, entry.SetId, profile.Id, a, start, end, marchDamage, magic, now, points)) return;
            // The initial pulse is consumed at activation time, not at a later host frame.
            rt.Boss.Fields.Tick(this, rt, now);
            if (BossGround(start, start, 0, out var legalStart)) rt.Boss.Fields.Reserve(this, rt, entry.SetId, profile.Id, replant, legalStart, legalStart, damage, magic, now);
            if (BossGround(start, end, a.RangeMilli / 1000f, out var legalEnd)) rt.Boss.Fields.Reserve(this, rt, entry.SetId, profile.Id, replant, legalEnd, legalEnd, damage, magic, now);
        }

        private void BossHysteriaClaw(HeroRuntime rt, long stateLife, long clawLife, bool right, bool success, Vector3 point, bool completed)
        {
            if (!BossEnsure(rt) || BossRewardStage(rt, BossProfiles.DemonRewardId) == 0 || stateLife == 0 || clawLife == 0) return;
            var s = rt.Boss; float now = Time.time;
            if (s.HysteriaState != stateLife) { s.ClawPairs.Clear(); s.HysteriaState = stateLife; s.PreviousClawLeft = false; }
            BossClawPair pair = null;
            foreach (var candidate in s.ClawPairs) if (candidate.First == clawLife || candidate.Second == clawLife) { pair = candidate; break; }
            if (pair == null && !success && !completed)
            {
                bool rightCanCollect = right && s.PreviousClawLeft;
                s.PreviousClawLeft = !right;
                if (s.ClawPairs.Count != 0)
                {
                    var last = s.ClawPairs[s.ClawPairs.Count - 1];
                    if (last.Second == 0 && last.FirstRight != right) { last.Second = clawLife; last.SecondCanCollect = rightCanCollect; pair = last; }
                }
                if (pair == null)
                {
                    if (s.ClawPairs.Count >= 8) s.ClawPairs.RemoveAt(0);
                    s.ClawPairs.Add(new BossClawPair { First = clawLife, FirstRight = right, FirstCanCollect = rightCanCollect });
                }
                return;
            }
            if (pair == null) return;
            bool first = pair.First == clawLife;
            if (success && !(first ? pair.FirstHit : pair.SecondHit))
            {
                if (first) pair.FirstHit = true; else pair.SecondHit = true;
                pair.Point = point;
                int stage = BossRewardStage(rt, BossProfiles.DemonRewardId);
                if (stage >= 2)
                {
                    if (!right) BossPlantGrove(rt, point, now);
                    else if (first ? pair.FirstCanCollect : pair.SecondCanCollect)
                    {
                        var bud = s.Fields.Collect(this, rt, BossProfiles.DemonSetId, point, point, 4, now, s.NextId);
                        if (bud != null && stage >= 3) BossMarch(rt, bud.Center, now);
                    }
                }
            }
            if (completed) { if (first) pair.FirstDone = true; else pair.SecondDone = true; }
            if (pair.Second == 0 || !pair.FirstDone || !pair.SecondDone) return;
            s.ClawPairs.Remove(pair);
            if (!pair.FirstHit && !pair.SecondHit) return;
            BossDispatch(rt, BossEvent.MainHit, -pair.Second, null, pair.Point, now, true);
            if (BossFind(rt, "boss_demon.stage2", out var entry, out var profile))
            {
                var a = profile.Actions[0];
                if (BossReady(rt, rt.Boss.ActionKeys[a], now, a.CooldownMillis))
                {
                    var center = rt.Hero.agentPosition;
                    BossExecute(rt, entry.SetId, profile.Id, a, null, center, center, BossAmount(rt, entry, profile, a.ChannelId), BossMagic(rt), now);
                    BossPlantGrove(rt, center, now);
                }
            }
        }
    }
}

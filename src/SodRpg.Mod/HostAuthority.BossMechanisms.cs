using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private static bool BossAlive(Entity e) => e != null && e.isActive && e.currentHealth > 0 && (!(e is Hero h) || !h.isKnockedOut);

        internal readonly struct BossScheduledPulse
        {
            public BossScheduledPulse(BossAction action, Vector3 center, Vector3 end, float amount, bool magic, int delayMillis, Entity target = null, int stunMillis = 0)
            {
                Action = action; Center = center; End = end; Amount = amount; Magic = magic; DelayMillis = delayMillis;
                Target = target; HasTarget = !ReferenceEquals(target, null); TargetCreation = target != null ? target.creationTime : 0;
                TargetLife = target != null ? NativeInstance.BossNativeActorLife(target) : 0;
                TargetNetId = target != null ? target.persistentNetId : 0;
                StunMillis = Math.Max(0, Math.Min(250, stunMillis));
            }
            public BossAction Action { get; }
            public Vector3 Center { get; }
            public Vector3 End { get; }
            public float Amount { get; }
            public bool Magic { get; }
            public int DelayMillis { get; }
            public Entity Target { get; }
            public bool HasTarget { get; }
            public float TargetCreation { get; }
            public long TargetLife { get; }
            public uint TargetNetId { get; }
            public int StunMillis { get; }
        }

        private bool BossReserveSequence(HeroRuntime rt, string set, string profile, BossScheduledPulse[] pulses, float now,
            int maxInstances = 1, int lifetimeMillis = 0, long nativeLife = 0, int maxSetInstances = 4, bool replaceOldest = false)
            => rt.Boss.Fields.ReserveSequence(this, rt, set, profile, pulses, now, maxInstances, lifetimeMillis, nativeLife, maxSetInstances, replaceOldest);
        private static int BossFieldCount(HeroRuntime rt, string set, string profile = null) => rt.Boss.Fields.Count(set, profile);
        private static int BossFieldPulseCount(HeroRuntime rt, string set, string profile) => rt.Boss.Fields.PulseCount(set, profile);
        private static long BossReservationId(HeroRuntime rt, string set, string profile) => rt.Boss.Fields.Latest(set, profile);
        private static bool BossReservationExists(HeroRuntime rt, long id) => rt.Boss.Fields.Contains(id);
        private void BossCancelReservation(HeroRuntime rt, long id) => rt.Boss.Fields.CancelReservation(this, rt, id, Time.time);
        private void BossCancelProfileReservations(HeroRuntime rt, string set, string profile)
        {
            rt.Boss.Fields.CancelProfile(this, rt, set, profile, Time.time);
            rt.Boss.Projectiles.CancelProfile(this, rt, set, profile, Time.time);
        }
        private void BossCancelNativeReservations(HeroRuntime rt, string set, long nativeLife)
        {
            if (nativeLife <= 0) return;
            rt.Boss.Fields.CancelNative(this, rt, set, nativeLife, Time.time);
            rt.Boss.Projectiles.CancelNative(this, rt, set, nativeLife, Time.time);
        }

        // M1: all pulses share the native enemy filter and one normal damage dispatch.
        internal sealed class BossShapeAttack
        {
            private readonly HashSet<int> _pulse = new HashSet<int>();
            private readonly List<RaycastHit2D> _walls = new List<RaycastHit2D>(32);
            public void Execute(HostAuthority host, HeroRuntime rt, BossAction action, Vector3 center, Vector3 end, float amount, bool magic,
                BossShape? pulseShape = null, float? pulseRadius = null, float? pulseRange = null, int stunMillis = 0)
            {
                _pulse.Clear();
                var shape = pulseShape ?? action.Shape;
                float radius = pulseRadius ?? action.RadiusMilli / 1000f;
                float range = Math.Min(action.RangeMilli / 1000f, pulseRange ?? action.RangeMilli / 1000f);
                bool line = shape == BossShape.Line || shape == BossShape.Column;
                var direction = BossDirection(center, end);
                if (direction == Vector3.zero) direction = rt.Hero.transform.forward;
                if (line)
                {
                    var legalEnd = Dew.GetValidAgentDestination_LinearSweep(center, center + direction * range);
                    if (!BossFinite(legalEnd) || Mathf.Abs(legalEnd.y - center.y) > 2f) return;
                    range = Math.Min(range, BossDirectionDistance(center, legalEnd));
                    if (Physics.Raycast(center + Vector3.up * .2f, direction, out var ground, range, LayerMasks.Ground)) range = ground.distance;
                    _walls.Clear();
                    var filter = new ContactFilter2D { useLayerMask = true, layerMask = LayerMasks.CollidableWithProjectile, useTriggers = true };
                    Physics2D.CircleCast(center.ToXY(), action.WidthMilli / 2000f, direction.ToXY(), filter, _walls, range);
                    foreach (var hit in _walls)
                        if (!DewPhysics.TryGetEntity(hit.collider, out _) && DewPhysics.TryGetCollidableWithProjectile(hit.collider, out _))
                            range = Math.Min(range, hit.distance);
                    if (range <= .0001f) return;
                }
                ListReturnHandle<Entity> handle;
                var found = line
                    ? DewPhysics.SphereCastAllEntities(out handle, center, action.WidthMilli / 2000f, direction, range, EnemyFilter, rt.Hero)
                    : DewPhysics.OverlapCircleAllEntities(out handle, center, shape == BossShape.Fan ? range : radius, EnemyFilter, rt.Hero);
                try
                {
                    int count = 0;
                    foreach (var victim in found)
                    {
                        if (!BossAlive(victim) || !_pulse.Add(victim.GetInstanceID())) continue;
                        if (shape == BossShape.Fan && Vector3.Angle(direction, BossDirection(center, victim.position)) > action.AngleMilli / 2000f) continue;
                        if (count++ >= action.MaxTargets) break;
                        bool accepted = host.BossDamage(rt, victim, amount, magic, action.Element);
                        if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                        if (accepted && stunMillis > 0) rt.Boss.Defense.Stun(host, rt, victim, stunMillis, Time.time);
                        if (action.Payload == BossPayload.Push || action.Payload == BossPayload.Pull)
                            rt.Boss.EnemyMovement.Execute(rt, victim, line && action.Payload == BossPayload.Push ? victim.position - direction : center,
                                action.Payload == BossPayload.Pull, action.MagnitudeMilli / 1000f, Time.time);
                    }
                }
                finally { handle.Return(); }
            }
        }

        // M2: finite ballistic states, bounded per-wave target budgets, wall-first ordered collision.
        internal sealed class BossProjectileExecutor
        {
            private sealed class Wave
            {
                public readonly Dictionary<long, int> Hits = new Dictionary<long, int>();
                public int Limit, MaxHitsPerTarget;
            }
            private sealed class Shot
            {
                public long Id, NativeLife, ReservationId, TargetLife;
                public string Set, Profile;
                public Vector3 Point, Direction;
                public float Remaining, Speed, Radius, Damage, Last, Expires, TerminalRadius, ChainRadius;
                public int ChainRemaining, ChainDelayMillis;
                public bool Magic, FirstHit, ExplodeAtEnd, ExplodeOnHit, Homing, ExpireAtLifetime;
                public Entity Target;
                public Wave Wave;
                public BossElement Element;
                public BossAction Action;
                public readonly HashSet<long> Hits = new HashSet<long>();
            }
            private readonly List<Shot> _shots = new List<Shot>(64);
            private readonly List<RaycastHit2D> _collisions = new List<RaycastHit2D>(64);
            private readonly HashSet<Wave> _activeWaves = new HashSet<Wave>();
            public bool Execute(HostAuthority host, HeroRuntime rt, string set, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now,
                int shotCount = 0, int maxHitsPerTarget = 1, bool explodeAtEnd = false, float terminalRadius = 0f, string profile = null,
                bool delayedAlready = false, long nativeLife = 0, Entity adoptedTarget = null, bool explodeOnHit = false, bool homing = false,
                int chainCount = 0, float chainRadius = 0f, int chainDelayMillis = 150, bool expireAtLifetime = false, long reservationId = 0)
            {
                if (chainCount < 0 || chainCount > 3 || chainDelayMillis < 0 || !BossFinite(center) || !BossFinite(end)
                    || float.IsNaN(chainRadius) || float.IsInfinity(chainRadius) || chainRadius < 0) return false;
                int count = shotCount > 0 ? Math.Min(a.Count, shotCount) : a.Count;
                int owned = 0;
                _activeWaves.Clear();
                foreach (var shot in _shots)
                    if (shot.Set == set)
                    {
                        owned++;
                        if (profile != null && shot.Profile == profile) _activeWaves.Add(shot.Wave);
                    }
                if (owned + count > 64 || _shots.Count + count > 128 || profile != null && _activeWaves.Count >= a.MaxInstances) return false;
                var direction = BossDirection(center, end);
                if (direction == Vector3.zero) direction = rt.Hero.transform.forward;
                float speed = a.SpeedMilli > 0 ? a.SpeedMilli / 1000f : 12;
                float range = a.RangeMilli > 0 ? a.RangeMilli / 1000f : 8;
                float delay = delayedAlready ? 0 : a.DelayMillis / 1000f;
                if (explodeAtEnd && !explodeOnHit && !homing) range = Math.Min(range, BossDirectionDistance(center, end));
                float lifetime = a.LifetimeMillis > 0 ? a.LifetimeMillis / 1000f : range / speed;
                var wave = new Wave { Limit = a.MaxTargets, MaxHitsPerTarget = Math.Max(1, Math.Min(count, maxHitsPerTarget)) };
                for (int i = 0; i < count; i++)
                {
                    var dir = a.Shape == BossShape.Radial ? Quaternion.Euler(0, 360f * i / count, 0) * direction : direction;
                    Entity target = homing ? adoptedTarget : a.Anchor == BossAnchor.Marked ? rt.Boss.Ledger.NearestMark(center, now, a.LedgerId ?? set) : null;
                    var shot = new Shot
                    {
                        Id = ++rt.Boss.NextId, Set = set, Profile = profile, Point = center, Direction = dir, Remaining = range, Speed = speed,
                        Radius = a.WidthMilli > 0 ? a.WidthMilli / 2000f : .2f, Damage = amount, Magic = magic, Last = now + delay, Element = a.Element,
                        Expires = now + delay + (expireAtLifetime ? lifetime : Math.Min(range / speed, lifetime)),
                        FirstHit = a.FirstHitOnly, Wave = wave, ExplodeAtEnd = explodeAtEnd, ExplodeOnHit = explodeOnHit,
                        TerminalRadius = terminalRadius, Action = a, NativeLife = nativeLife, ReservationId = reservationId,
                        Target = target, TargetLife = target != null ? host.BossNativeActorLife(target) : 0,
                        Homing = homing || a.Anchor == BossAnchor.Marked, ExpireAtLifetime = expireAtLifetime,
                        ChainRemaining = chainCount, ChainRadius = chainRadius, ChainDelayMillis = chainDelayMillis,
                    };
                    _shots.Add(shot);
                    host.PublishBossVisual(rt, shot.Id, 5, center, center + dir * range, shot.Radius, shot.Last, shot.Expires,
                        element: a.Element, targetNetId: adoptedTarget != null ? adoptedTarget.persistentNetId : 0, nativeLife: nativeLife);
                }
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = _shots.Count - 1; i >= 0; i--)
                {
                    var s = _shots[i];
                    if (now < s.Last) continue;
                    if (s.Homing && BossAlive(s.Target) && host.BossNativeSameLife(s.Target, s.TargetLife))
                        s.Direction = BossDirection(s.Point, s.Target.position);
                    float distance = Math.Min(s.Remaining, s.Speed * Math.Max(0, Math.Min(now, s.Expires) - s.Last));
                    s.Last = now;
                    bool ended = false, blocked = false, impact = false, chained = false;
                    if (distance > 0)
                    {
                        var filter = new ContactFilter2D { useLayerMask = true, layerMask = LayerMasks.Entity | LayerMasks.CollidableWithProjectile, useTriggers = true };
                        _collisions.Clear();
                        Physics2D.CircleCast(s.Point.ToXY(), s.Radius, s.Direction.ToXY(), filter, _collisions, distance);
                        float wall = distance + 1;
                        if (Physics.Raycast(s.Point, s.Direction, out var ground, distance, LayerMasks.Ground)) wall = ground.distance;
                        foreach (var hit in _collisions)
                            if (!DewPhysics.TryGetEntity(hit.collider, out _) && DewPhysics.TryGetCollidableWithProjectile(hit.collider, out _) && hit.distance < wall)
                                wall = hit.distance;
                        float travelled = Math.Min(distance, wall);
                        if (!s.ExplodeAtEnd || s.ExplodeOnHit)
                        {
                            ListReturnHandle<Entity> handle;
                            var entities = DewPhysics.SphereCastAllEntities(out handle, s.Point, s.Radius, s.Direction, distance, EnemyFilter, rt.Hero);
                            try
                            {
                                while (true)
                                {
                                    Entity nearest = null;
                                    float first = wall;
                                    foreach (var hit in _collisions)
                                    {
                                        if (hit.distance >= first || !DewPhysics.TryGetEntity(hit.collider, out var e) || !BossAlive(e)
                                            || e.Status.hasUncollidable || e.GetRelation(rt.Hero) != EntityRelation.Enemy || !entities.Contains(e)) continue;
                                        long life = host.BossNativeActorLife(e);
                                        if (s.Hits.Contains(life) || s.ChainRemaining > 0 && s.Wave.Hits.ContainsKey(life)) continue;
                                        nearest = e; first = hit.distance;
                                    }
                                    if (nearest == null) break;
                                    long target = host.BossNativeActorLife(nearest);
                                    s.Hits.Add(target);
                                    if (s.ExplodeOnHit) { impact = true; ended = true; travelled = first; break; }
                                    s.Wave.Hits.TryGetValue(target, out int hits);
                                    if (hits < s.Wave.MaxHitsPerTarget && (hits > 0 || s.Wave.Hits.Count < s.Wave.Limit))
                                    {
                                        s.Wave.Hits[target] = hits + 1;
                                        host.BossDamage(rt, nearest, s.Damage, s.Magic, s.Element);
                                        if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                                    }
                                    if (s.ChainRemaining > 0)
                                    {
                                        s.ChainRemaining--;
                                        Vector3 hitPoint = nearest.position;
                                        if (s.ChainRemaining > 0 && now < s.Expires)
                                        {
                                            Entity next = NextChainTarget(host, rt, s, hitPoint);
                                            if (next != null)
                                            {
                                                s.Point = hitPoint; s.Target = next; s.TargetLife = host.BossNativeActorLife(next);
                                                s.Direction = BossDirection(hitPoint, next.position); s.Remaining = s.ChainRadius;
                                                s.Homing = true; s.Last = now + s.ChainDelayMillis / 1000f;
                                                host.PublishBossVisual(rt, s.Id, 5, s.Point, next.position, s.Radius, s.Last, s.Expires,
                                                    element: s.Element, targetNetId: next.persistentNetId, nativeLife: s.NativeLife);
                                                chained = true; break;
                                            }
                                        }
                                        ended = true; travelled = first; break;
                                    }
                                    if (s.FirstHit) { ended = true; travelled = first; break; }
                                }
                            }
                            finally { handle.Return(); }
                        }
                        if (chained) continue;
                        blocked = !impact && wall <= travelled;
                        ended |= blocked;
                        s.Point += s.Direction * travelled;
                        s.Remaining = Math.Max(0, s.Remaining - travelled);
                    }
                    if (!ended && now < s.Expires && (s.Remaining > .0001f || s.ExpireAtLifetime)) continue;
                    if (!blocked && (impact || s.ExplodeAtEnd && (!ended || s.ExplodeOnHit)))
                    {
                        rt.Boss.Shapes.Execute(host, rt, s.Action, s.Point, s.Point, s.Damage, s.Magic, BossShape.Circle, s.TerminalRadius);
                        if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                        host.PublishBossVisual(rt, ++rt.Boss.NextId, 1, s.Point, s.Point, s.TerminalRadius, now, now + .25f,
                            element: s.Element, nativeLife: s.NativeLife);
                    }
                    host.PublishBossVisual(rt, s.Id, 5, s.Point, s.Point, s.Radius, now, now, true);
                    _shots.RemoveAt(i);
                }
            }
            private static Entity NextChainTarget(HostAuthority host, HeroRuntime rt, Shot shot, Vector3 point)
            {
                ListReturnHandle<Entity> handle;
                var entities = DewPhysics.OverlapCircleAllEntities(out handle, point, shot.ChainRadius, EnemyFilter, rt.Hero);
                Entity nearest = null; float best = float.MaxValue;
                try
                {
                    foreach (var e in entities)
                    {
                        if (!BossAlive(e) || e.Status.hasUncollidable || shot.Wave.Hits.ContainsKey(host.BossNativeActorLife(e))) continue;
                        float distance = (e.position - point).Flattened().sqrMagnitude;
                        if (distance < best || distance == best && (nearest == null || e.persistentNetId < nearest.persistentNetId))
                        { nearest = e; best = distance; }
                    }
                }
                finally { handle.Return(); }
                return nearest;
            }
            public void CancelReservation(HostAuthority host, HeroRuntime rt, long reservationId, float now)
            {
                if (reservationId <= 0) return;
                for (int i = _shots.Count - 1; i >= 0; i--) if (_shots[i].ReservationId == reservationId) Remove(host, rt, i, now);
            }
            public void CancelNative(HostAuthority host, HeroRuntime rt, string set, long nativeLife, float now)
            {
                for (int i = _shots.Count - 1; i >= 0; i--)
                    if (_shots[i].Set == set && _shots[i].NativeLife == nativeLife) Remove(host, rt, i, now);
            }
            public void CancelProfile(HostAuthority host, HeroRuntime rt, string set, string profile, float now)
            {
                for (int i = _shots.Count - 1; i >= 0; i--)
                    if (_shots[i].Set == set && _shots[i].Profile == profile) Remove(host, rt, i, now);
            }
            private void Remove(HostAuthority host, HeroRuntime rt, int index, float now)
            {
                var s = _shots[index];
                host.PublishBossVisual(rt, s.Id, 5, s.Point, s.Point, s.Radius, now, now, true); _shots.RemoveAt(index);
            }
            public void Clear(bool preserveNative = false)
            {
                if (!preserveNative) _shots.Clear();
                else for (int i = _shots.Count - 1; i >= 0; i--) if (_shots[i].NativeLife == 0) _shots.RemoveAt(i);
                _collisions.Clear(); _activeWaves.Clear();
            }
        }

        // M3: one reservation owns all finite pulses; buds are a stricter four-slot collection.
        internal sealed class BossFieldExecutor
        {
            internal sealed class Field
            {
                public long Id; public string Set, Profile; public BossAction Action;
                public Vector3 Center, End; public Vector3[] Points; public float Due, Created, Damage; public int Pulse, PulseCount;
                public bool Magic, Bud, Collectible;
                public BossScheduledPulse[] Scheduled;
                public float Until;
                public long[] VisualIds;
                public long NativeLife;
            }
            private readonly List<Field> _fields = new List<Field>(32);
            public bool Reserve(HostAuthority host, HeroRuntime rt, string set, string profile, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now, Vector3[] points = null)
            {
                bool bud = a.Payload == BossPayload.Bud;
                int owned = 0, sameProfile = 0; Field oldest = null;
                foreach (var f in _fields)
                    if (f.Set == set)
                    {
                        owned++;
                        if (f.Profile == profile && f.Bud == bud) { sameProfile++; if (oldest == null || f.Created < oldest.Created) oldest = f; }
                    }
                if (owned >= 4 || !bud && sameProfile >= a.MaxInstances)
                {
                    if (!a.ReplaceOldest || bud || oldest == null) return false;
                    host.PublishBossVisual(rt, oldest.Id, 4, oldest.Center, oldest.End, oldest.Action.RadiusMilli / 1000f, now, now, true);
                    _fields.Remove(oldest);
                }
                if (_fields.Count >= 128) return false;
                var field = new Field { Id = ++rt.Boss.NextId, Set = set, Profile = profile, Action = a, Center = center, End = end, Points = points,
                    Due = now + a.DelayMillis / 1000f, Created = now, Damage = amount, Magic = magic, Bud = bud, Collectible = a.Collectible, PulseCount = bud ? 1 : a.Count };
                _fields.Add(field);
                host.PublishBossVisual(rt, field.Id, bud ? (a.Collectible ? 2 : 3) : (points != null ? 6 : 4), center, end, a.RadiusMilli / 1000f,
                    field.Due, field.Due + (field.PulseCount - 1) * a.IntervalMillis / 1000f + 0.2f);
                return true;
            }
            public int Count(string set, string profile = null)
            {
                int count = 0;
                foreach (var f in _fields) if (f.Set == set && (profile == null || f.Profile == profile)) count++;
                return count;
            }
            public int PulseCount(string set, string profile)
            {
                foreach (var f in _fields) if (f.Set == set && f.Profile == profile) return f.Pulse;
                return 0;
            }
            public long Latest(string set, string profile)
            {
                long id = 0;
                foreach (var f in _fields)
                    if (f.Set == set && f.Profile == profile && f.Id > id) id = f.Id;
                return id;
            }
            public bool Contains(long id)
            {
                foreach (var f in _fields) if (f.Id == id) return true;
                return false;
            }
            public void CancelReservation(HostAuthority host, HeroRuntime rt, long id, float now)
            {
                for (int i = _fields.Count - 1; i >= 0; i--)
                    if (_fields[i].Id == id)
                    {
                        var f = _fields[i];
                        if (f.Scheduled == null) host.PublishBossVisual(rt, f.Id, 4, f.Center, f.End, 0, now, now, true);
                        else foreach (long visual in f.VisualIds) if (visual != 0)
                            host.PublishBossVisual(rt, visual, 4, Vector3.zero, Vector3.zero, 0, now, now, true);
                        _fields.RemoveAt(i); break;
                    }
                rt.Boss.Projectiles.CancelReservation(host, rt, id, now);
            }
            public bool ReserveSequence(HostAuthority host, HeroRuntime rt, string set, string profile, BossScheduledPulse[] pulses,
                float now, int maxInstances, int lifetimeMillis, long nativeLife, int maxSetInstances, bool replaceOldest)
            {
                if (pulses == null || pulses.Length > 32 || pulses.Length == 0 && lifetimeMillis <= 0 || maxInstances < 1 || maxInstances > 4
                    || lifetimeMillis < 0 || nativeLife < 0 || maxSetInstances < 1 || maxSetInstances > 4) return false;
                int lastDelay = 0;
                foreach (var pulse in pulses)
                {
                    if (pulse.Action == null || pulse.DelayMillis < lastDelay || !BossFinite(pulse.Center) || !BossFinite(pulse.End)
                        || float.IsNaN(pulse.Amount) || float.IsInfinity(pulse.Amount) || pulse.Amount < 0) return false;
                    lastDelay = pulse.DelayMillis;
                }
                while (Count(set) >= maxSetInstances || Count(set, profile) >= maxInstances)
                {
                    if (!replaceOldest) return false;
                    Field oldest = null;
                    bool setFull = Count(set) >= maxSetInstances;
                    foreach (var field in _fields)
                        if (field.Set == set && (setFull || field.Profile == profile) && (oldest == null || field.Created < oldest.Created))
                            oldest = field;
                    if (oldest == null) return false;
                    CancelReservation(host, rt, oldest.Id, now);
                }
                if (_fields.Count >= 128) return false;
                float until = now + Math.Max(lifetimeMillis, lastDelay) / 1000f;
                var f = new Field { Id = ++rt.Boss.NextId, Set = set, Profile = profile, Scheduled = pulses,
                    VisualIds = pulses.Length == 0 ? Array.Empty<long>() : new long[pulses.Length], NativeLife = nativeLife,
                    Created = now, Due = pulses.Length == 0 ? until : now + pulses[0].DelayMillis / 1000f,
                    Until = until, PulseCount = pulses.Length };
                _fields.Add(f);
                for (int i = 0; i < pulses.Length; i++)
                {
                    var pulse = pulses[i];
                    if (pulse.Action.Payload == BossPayload.Shield || pulse.Action.Payload == BossPayload.Heal) continue;
                    int kind = pulse.Action.Shape == BossShape.Line || pulse.Action.Shape == BossShape.Column || pulse.Action.Shape == BossShape.Radial ? 6 : 4;
                    float due = now + pulse.DelayMillis / 1000f;
                    float range = pulse.Action.RangeMilli / 1000f;
                    if (pulse.Action.Shape == BossShape.Line || pulse.Action.Shape == BossShape.Column)
                        range = Math.Min(range, BossDirectionDistance(pulse.Center, pulse.End));
                    f.VisualIds[i] = ++rt.Boss.NextId;
                    host.PublishBossVisual(rt, f.VisualIds[i], kind, pulse.Center, pulse.End, pulse.Action.RadiusMilli / 1000f, due, due + 0.25f,
                        element: pulse.Action.Element, shape: pulse.Action.Shape, range: range, width: pulse.Action.WidthMilli / 1000f,
                        angle: pulse.Action.AngleMilli / 1000f, count: pulse.Action.Shape == BossShape.Radial ? pulse.Action.Count : 0,
                        targetNetId: pulse.TargetNetId, nativeLife: nativeLife);
                }
                return true;
            }
            public void CancelProfile(HostAuthority host, HeroRuntime rt, string set, string profile, float now)
            {
                for (int i = _fields.Count - 1; i >= 0; i--)
                    if (_fields[i].Set == set && _fields[i].Profile == profile)
                    {
                        var f = _fields[i];
                        if (f.Scheduled == null)
                            host.PublishBossVisual(rt, f.Id, f.Bud ? (f.Collectible ? 2 : 3) : 4, f.Center, f.End, f.Action.RadiusMilli / 1000f, now, now, true);
                        else
                            foreach (long id in f.VisualIds)
                                if (id != 0) host.PublishBossVisual(rt, id, 4, Vector3.zero, Vector3.zero, 0, now, now, true);
                        _fields.RemoveAt(i);
                    }
            }
            public void CancelNative(HostAuthority host, HeroRuntime rt, string set, long nativeLife, float now)
            {
                for (int i = _fields.Count - 1; i >= 0; i--)
                    if (_fields[i].Set == set && _fields[i].NativeLife == nativeLife)
                    {
                        var f = _fields[i];
                        foreach (long id in f.VisualIds)
                            if (id != 0) host.PublishBossVisual(rt, id, 4, Vector3.zero, Vector3.zero, 0, now, now, true);
                        _fields.RemoveAt(i);
                    }
            }
            public Field Collect(HostAuthority host, HeroRuntime rt, string set, Vector3 origin, Vector3 prefer, float range, float now, long before)
            {
                Field best = null; float distance = float.MaxValue;
                foreach (var f in _fields)
                {
                    if (!f.Bud || !f.Collectible || f.Set != set || f.Id > before || now < f.Created + BossProfiles.BudCollectibleDelayMillis / 1000f || now >= f.Due
                        || (f.Center - origin).sqrMagnitude > range * range) continue;
                    float d = (f.Center - prefer).sqrMagnitude;
                    if (d < distance || d == distance && (best == null || f.Id < best.Id)) { best = f; distance = d; }
                }
                if (best == null || !BossReady(rt, set, now, 1000)) return null;
                _fields.Remove(best);
                host.PublishBossVisual(rt, best.Id, best.Collectible ? 2 : 3, best.Center, best.End, best.Action.RadiusMilli / 1000f, now, now, true);
                rt.Boss.Shapes.Execute(host, rt, best.Action, best.Center, best.End, best.Damage, best.Magic);
                host.PublishBossVisual(rt, ++rt.Boss.NextId, best.Collectible ? 2 : 3, best.Center, best.Center, best.Action.RadiusMilli / 1000f, now, now + 0.3f);
                return best;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = _fields.Count - 1; i >= 0; i--)
                {
                    var f = _fields[i];
                    if (f.Scheduled != null)
                    {
                        while (f.Pulse < f.Scheduled.Length && now >= f.Created + f.Scheduled[f.Pulse].DelayMillis / 1000f)
                        {
                            int pulseIndex = f.Pulse++;
                            var pulse = f.Scheduled[pulseIndex];
                            var center = pulse.Action.FollowOwner ? rt.Hero.agentPosition : pulse.Center;
                            var end = pulse.Action.FollowOwner ? center + (pulse.End - pulse.Center) : pulse.End;
                            if (pulse.HasTarget)
                            {
                                if (BossAlive(pulse.Target) && pulse.Target.creationTime == pulse.TargetCreation && host.BossNativeSameLife(pulse.Target, pulse.TargetLife)
                                    && host.BossDamage(rt, pulse.Target, pulse.Amount, pulse.Magic, pulse.Action.Element)
                                    && rt.Boss.Build != null && pulse.StunMillis > 0)
                                    rt.Boss.Defense.Stun(host, rt, pulse.Target, pulse.StunMillis, now);
                                if (f.VisualIds[pulseIndex] != 0)
                                    host.PublishBossVisual(rt, f.VisualIds[pulseIndex], 4, pulse.Center, pulse.End, pulse.Action.RadiusMilli / 1000f,
                                        now, now + .25f, element: pulse.Action.Element, targetNetId: pulse.TargetNetId, nativeLife: f.NativeLife);
                            }
                            else if (pulse.Action.Payload == BossPayload.Shield || pulse.Action.Payload == BossPayload.Heal
                                || pulse.Action.Payload == BossPayload.Modifier || pulse.Action.Payload == BossPayload.Unstoppable)
                                rt.Boss.Defense.Execute(host, rt, f.Profile, pulse.Action, pulse.Amount, now);
                            else if (pulse.Action.Mechanism == BossMechanism.Projectile)
                            {
                                if (f.VisualIds[pulseIndex] != 0)
                                    host.PublishBossVisual(rt, f.VisualIds[pulseIndex], 4, center, end, 0, now, now, true);
                                rt.Boss.Projectiles.Execute(host, rt, f.Set, pulse.Action, center, end, pulse.Amount, pulse.Magic, now,
                                    maxHitsPerTarget: pulse.Action.Shape == BossShape.Radial ? pulse.Action.Count : 1, profile: f.Profile,
                                    delayedAlready: true, nativeLife: f.NativeLife);
                            }
                            else
                            {
                                float? range = pulse.Action.Shape == BossShape.Line || pulse.Action.Shape == BossShape.Column
                                    ? (float?)BossDirectionDistance(center, end) : null;
                                rt.Boss.Shapes.Execute(host, rt, pulse.Action, center, end, pulse.Amount, pulse.Magic, pulseRange: range, stunMillis: pulse.StunMillis);
                                if (pulse.Action.FollowOwner && f.VisualIds[pulseIndex] != 0)
                                    host.PublishBossVisual(rt, f.VisualIds[pulseIndex], 4, center, end, pulse.Action.RadiusMilli / 1000f, now, now + 0.25f,
                                        element: pulse.Action.Element, shape: pulse.Action.Shape, range: pulse.Action.RangeMilli / 1000f,
                                        width: pulse.Action.WidthMilli / 1000f, angle: pulse.Action.AngleMilli / 1000f, nativeLife: f.NativeLife);
                            }
                            if (rt.Boss.Build == null) return;
                        }
                        if (f.Pulse == f.PulseCount && now >= f.Until) _fields.RemoveAt(i);
                        continue;
                    }
                    if (now < f.Due) continue;
                    while (now >= f.Due && f.Pulse < f.PulseCount)
                    {
                        Vector3 point = f.Action.FollowOwner ? rt.Hero.agentPosition : f.Points != null ? f.Points[f.Pulse] : f.Center;
                        if (BossFinite(point))
                        {
                            if (f.Action.Payload == BossPayload.Heal || f.Action.Payload == BossPayload.Shield || f.Action.Payload == BossPayload.Modifier || f.Action.Payload == BossPayload.Unstoppable)
                                rt.Boss.Defense.Execute(host, rt, f.Profile, f.Action, f.Damage, now);
                            else rt.Boss.Shapes.Execute(host, rt, f.Action, point, f.End, f.Damage, f.Magic, f.Points != null ? BossShape.Circle : (BossShape?)null);
                            if (f.Bud) host.PublishBossVisual(rt, ++rt.Boss.NextId, f.Collectible ? 2 : 3, point, point, f.Action.RadiusMilli / 1000f, now, now + 0.3f);
                            else if (f.Action.FollowOwner) host.PublishBossVisual(rt, f.Id, 4, point, point, f.Action.RadiusMilli / 1000f, now, f.Created + f.Action.DelayMillis / 1000f + f.Action.Count * f.Action.IntervalMillis / 1000f + 0.2f);
                        }
                        f.Pulse++; f.Due = f.Created + f.Action.DelayMillis / 1000f + f.Pulse * f.Action.IntervalMillis / 1000f;
                    }
                    if (f.Pulse < f.PulseCount) continue;
                    host.PublishBossVisual(rt, f.Id, f.Bud ? (f.Collectible ? 2 : 3) : 4, f.Center, f.End, f.Action.RadiusMilli / 1000f, now, now, true);
                    _fields.RemoveAt(i);
                }
            }
            public void Clear(bool preserveNative = false)
            {
                if (!preserveNative) _fields.Clear();
                else for (int i = _fields.Count - 1; i >= 0; i--) if (_fields[i].NativeLife == 0) _fields.RemoveAt(i);
            }
        }

        // M4: no terrain traversal or immunity; this never emits a native movement event.
        internal sealed class BossMovementExecutor
        {
            public bool Execute(HostAuthority host, HeroRuntime rt, BossAction a, Vector3 target, float now)
            {
                if (rt.Hero.Control.isDisplacing || rt.Hero.Status.hasRoot || rt.Hero.Status.hasStun
                    || !BossGround(rt.Hero.agentPosition, target, Math.Min(8, a.RangeMilli / 1000f), out var point)) return false;
                host.EnterGenerated(rt.Hero);
                try
                {
                    rt.Hero.Control.StartDisplacement(new DispByDestination { destination = point, duration = Math.Max(0.05f, a.LifetimeMillis / 1000f),
                        isFriendly = true, isDodging = false, canGoOverTerrain = false, isCanceledByCC = true });
                }
                finally { host.ExitGenerated(rt.Hero); }
                host.PublishBossVisual(rt, ++rt.Boss.NextId, 6, rt.Hero.agentPosition, point, 0.1f, now, now + Math.Max(0.05f, a.LifetimeMillis / 1000f));
                return true;
            }
        }

        // M5: positional success is independent from the damage dispatch.
        internal sealed class BossEnemyMovementExecutor
        {
            private readonly Entity[] _targets = new Entity[64];
            private readonly float[] _ready = new float[64];
            public bool Execute(HeroRuntime rt, Entity victim, Vector3 origin, bool pull, float distance, float now, float duration = .2f, float gateSeconds = 1f)
            {
                if (!BossAlive(victim) || victim == rt.Hero || victim.GetRelation(rt.Hero) != EntityRelation.Enemy || victim.IsAnyBoss()
                    || victim.Status.hasCrowdControlImmunity || victim.Status.hasUncollidable || victim.Control.isDisplacing) return false;
                if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0 || float.IsNaN(gateSeconds) || float.IsInfinity(gateSeconds) || gateSeconds < 0) return false;
                int slot = -1;
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i] == victim) { if (_ready[i] > now) return false; slot = i; break; }
                    if (slot < 0 && (_targets[i] == null || _ready[i] <= now)) slot = i;
                }
                if (slot < 0) return false;
                var direction = BossDirection(pull ? victim.position : origin, pull ? origin : victim.position);
                float length = Math.Min(2, Math.Max(0, distance));
                if (pull) length = Math.Min(length, Vector3.Distance(victim.position, origin));
                if (direction == Vector3.zero || !BossGround(victim.agentPosition, victim.agentPosition + direction * length, length, out var point)) return false;
                _targets[slot] = victim; _ready[slot] = now + gateSeconds;
                victim.Control.StartDisplacement(new DispByDestination { destination = point, duration = duration, isFriendly = false,
                    isDodging = false, canGoOverTerrain = false, isCanceledByCC = true });
                return true;
            }
            public void Clear() { Array.Clear(_targets, 0, _targets.Length); Array.Clear(_ready, 0, _ready.Length); }
        }

        // M6: host-only non-Entity shooters. Native summons must enter via the validated ownership API below.
        internal sealed class BossDeployableExecutor
        {
            private sealed class Deployable
            {
                public long Id, Epoch; public string Set; public Hero Owner; public BossAction Action; public Vector3 Center, End;
                public float Due, Until, Damage; public bool Magic, IsNative; public int Shots; public Summon Native; public float NativeCreation;
            }
            private readonly List<Deployable> _owned = new List<Deployable>(16);
            public bool Execute(HostAuthority host, HeroRuntime rt, string set, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now)
            {
                int count = 0; foreach (var d in _owned) if (d.Set == set) count++;
                if (count >= Math.Min(2, a.MaxInstances) || _owned.Count >= 128 || a.LifetimeMillis <= 0) return false;
                var entry = new Deployable { Id = ++rt.Boss.NextId, Epoch = rt.ShieldEquipmentEpoch, Set = set, Owner = rt.Hero, Action = a,
                    Center = center, End = end, Due = now + a.DelayMillis / 1000f, Until = now + a.LifetimeMillis / 1000f, Damage = amount, Magic = magic };
                _owned.Add(entry); host.PublishBossVisual(rt, entry.Id, 8, center, end, a.RadiusMilli / 1000f, entry.Due, entry.Until); return true;
            }
            public bool RegisterNative(HostAuthority host, HeroRuntime rt, string set, Summon summon, SkillTrigger source, BossAction action, float now)
            {
                if (!NetworkServer.active || summon == null || !summon.isActive || source == null || source.owner != rt.Hero
                    || FindMemory(rt.Hero, source.GetType().Name) != source || summon.hero != rt.Hero || summon.info.caster != rt.Hero
                    || summon.FindFirstAncestorOfType<SkillTrigger>() != source || action.LifetimeMillis <= 0) return false;
                int count = 0; foreach (var d in _owned) { if (d.Native == summon) return false; if (d.Set == set) count++; }
                if (count >= 2 || _owned.Count >= 128) return false;
                var native = new Deployable { Id = ++rt.Boss.NextId, Epoch = rt.ShieldEquipmentEpoch, Owner = rt.Hero, Set = set, Native = summon, IsNative = true,
                    Center = summon.agentPosition, NativeCreation = summon.creationTime, Until = now + action.LifetimeMillis / 1000f };
                _owned.Add(native);
                host.PublishBossVisual(rt, native.Id, 8, native.Center, native.Center, action.RadiusMilli / 1000f, now, native.Until);
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = _owned.Count - 1; i >= 0; i--)
                {
                    var d = _owned[i];
                    if (d.Epoch != rt.ShieldEquipmentEpoch || d.Owner != rt.Hero || now >= d.Until
                        || d.IsNative && (d.Native == null || !d.Native.isActive || d.Native.creationTime != d.NativeCreation || d.Native.hero != d.Owner))
                    {
                        DestroyNative(d); host.PublishBossVisual(rt, d.Id, 8, d.Center, d.End, 0, now, now, true); _owned.RemoveAt(i); continue;
                    }
                    if (d.IsNative || d.Shots >= d.Action.Count || now < d.Due) continue;
                    ListReturnHandle<Entity> handle;
                    var enemies = DewPhysics.OverlapCircleAllEntities(out handle, d.Center, d.Action.RangeMilli / 1000f, EnemyFilter, rt.Hero);
                    try
                    {
                        Entity nearest = null; float best = float.MaxValue;
                        foreach (var e in enemies) if (BossAlive(e) && (e.position - d.Center).sqrMagnitude < best) { nearest = e; best = (e.position - d.Center).sqrMagnitude; }
                        if (nearest != null)
                        {
                            if (d.Action.SpeedMilli > 0) rt.Boss.Projectiles.Execute(host, rt, d.Set, d.Action, d.Center, nearest.position, d.Damage, d.Magic, now, 1);
                            else rt.Boss.Shapes.Execute(host, rt, d.Action, nearest.position, nearest.position, d.Damage, d.Magic);
                            d.Shots++;
                        }
                    }
                    finally { handle.Return(); }
                    d.Due = now + Math.Max(0.1f, d.Action.IntervalMillis / 1000f);
                }
            }
            private static void DestroyNative(Deployable d)
            {
                if (d.Native != null && d.Native.isActive && d.Native.hero == d.Owner && d.Native.creationTime == d.NativeCreation) d.Native.Destroy();
            }
            public void Clear() { foreach (var d in _owned) DestroyNative(d); _owned.Clear(); }
        }

        // M7: retain only exact independent containers; pool reuse never grants ownership.
        internal sealed class BossDefenseExecutor
        {
            private sealed class Defense
            {
                public long Id; public string Source, NativeId; public float Until, LastDecay, DecayRate; public StatusEffect Effect; public ShieldEffect Shield; public BasicEffect Basic; public float Creation;
                public long Life;
                public StatBonus Bonus;
                public Entity Target;
                public long TargetLife;
            }
            private readonly List<Defense> _active = new List<Defense>(16);
            public bool Execute(HostAuthority host, HeroRuntime rt, string source, BossAction a, float amount, float now, bool linearDecay = false)
            {
                if (a.Payload != BossPayload.Heal && a.Payload != BossPayload.Shield && a.Payload != BossPayload.Unstoppable && a.Payload != BossPayload.Modifier) return false;
                if (a.Payload == BossPayload.Modifier && (!a.ModifierStat.HasValue || !BossNativeStat(a.ModifierStat.Value))) return false;
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_active[i].Source == source)
                    {
                        var old = _active[i]; Remove(host, rt, old);
                        host.PublishBossVisual(rt, old.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, now, now, true);
                        _active.RemoveAt(i);
                    }
                if (_active.Count >= 64 || a.Payload != BossPayload.Heal && a.LifetimeMillis <= 0) return false;
                var d = new Defense { Id = ++rt.Boss.NextId, Source = source, Until = now + a.LifetimeMillis / 1000f,
                    Target = rt.Hero, TargetLife = host.BossNativeActorLife(rt.Hero), LastDecay = now };
                if (a.Payload == BossPayload.Heal)
                {
                    host.EnterGenerated(rt.Hero); try { rt.Hero.Heal(amount).Dispatch(rt.Hero); } finally { host.ExitGenerated(rt.Hero); } return true;
                }
                if (a.Payload == BossPayload.Shield)
                {
                    var shield = host._am?.serverActor?.GiveShield(rt.Hero, amount, a.LifetimeMillis / 1000f, false);
                    d.Effect = shield; d.Shield = shield?.shield;
                    if (linearDecay && d.Shield != null) d.DecayRate = Math.Max(0, d.Shield.amount) / (a.LifetimeMillis / 1000f);
                }
                else if (a.Payload == BossPayload.Unstoppable)
                {
                    d.NativeId = "DreamforgeRPG.Boss." + rt.Hero.GetInstanceID() + "." + d.Id;
                    d.Basic = new UnstoppableEffect();
                    d.Effect = host._am?.serverActor?.CreateBasicEffect(rt.Hero, d.Basic, a.LifetimeMillis / 1000f, d.NativeId);
                }
                else if (a.Payload == BossPayload.Modifier)
                {
                    d.Bonus = new StatBonus();
                    AddNativeStat(d.Bonus, a.ModifierStat.Value, a.ChannelId != null ? amount : a.MagnitudeMilli / 1000f);
                    rt.Hero.Status.AddStatBonus(d.Bonus);
                    rt.Hero.Status.CalculateStatsIfDirty();
                }
                if (d.Effect == null && d.Bonus == null) return false;
                d.Creation = d.Effect != null ? d.Effect.creationTime : 0;
                d.Life = d.Effect != null ? host.BossNativeActorLife(d.Effect) : 0; _active.Add(d);
                host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0.6f, now, d.Until, targetNetId: rt.Hero.netId);
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var d = _active[i];
                    if (now >= d.Until || !Owned(host, rt, d) || !BossAlive(d.Target))
                    {
                        Remove(host, rt, d);
                        host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, now, now, true);
                        _active.RemoveAt(i);
                        continue;
                    }
                    if (d.Shield != null && d.DecayRate > 0)
                    {
                        d.Shield.amount = Math.Max(0, d.Shield.amount - d.DecayRate * Math.Max(0, now - d.LastDecay));
                        d.LastDecay = now;
                    }
                }
            }
            private static bool Owned(HostAuthority host, HeroRuntime rt, Defense d)
                => d.Bonus != null || d.Effect != null && d.Effect.isActive && d.Effect.victim == d.Target && d.Effect.creationTime == d.Creation
                    && host.BossNativeSameLife(d.Effect, d.Life)
                    && host.BossNativeSameLife(d.Target, d.TargetLife)
                    && (d.Shield != null ? d.Effect is Se_GenericShield_OneShot shield && shield.shield == d.Shield
                        : d.Effect is Se_GenericEffectContainer container && container.id == d.NativeId
                            && d.Basic != null && d.Basic.isAlive && d.Basic.parent == d.Effect);
            private static void Remove(HostAuthority host, HeroRuntime rt, Defense d)
            {
                if (d.Bonus != null)
                {
                    rt.Hero.Status.RemoveStatBonus(d.Bonus); d.Bonus = null;
                    rt.Hero.Status.CalculateStatsIfDirty();
                }
                if (Owned(host, rt, d)) d.Effect.Destroy();
            }
            public void Clear(HostAuthority host, HeroRuntime rt) { foreach (var d in _active) Remove(host, rt, d); _active.Clear(); }
            public void CancelSource(HostAuthority host, HeroRuntime rt, string source)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_active[i].Source == source)
                    {
                        var d = _active[i]; Remove(host, rt, d);
                        host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, Time.time, Time.time, true);
                        _active.RemoveAt(i);
                    }
            }
            public void Stun(HostAuthority host, HeroRuntime rt, Entity target, int durationMillis, float now)
            {
                if (_active.Count >= 64 || durationMillis <= 0 || !BossAlive(target) || target == rt.Hero || target.IsAnyBoss()
                    || target.GetRelation(rt.Hero) != EntityRelation.Enemy || target.Status.hasCrowdControlImmunity) return;
                var d = new Defense { Id = ++rt.Boss.NextId, Target = target, TargetLife = host.BossNativeActorLife(target),
                    Until = now + Math.Min(250, durationMillis) / 1000f, Basic = new StunEffect() };
                d.NativeId = "DreamforgeRPG.Boss.Stun." + rt.Hero.GetInstanceID() + "." + d.Id;
                host.EnterGenerated(rt.Hero);
                try { d.Effect = host._am?.serverActor?.CreateBasicEffect(target, d.Basic, d.Until - now, d.NativeId); }
                finally { host.ExitGenerated(rt.Hero); }
                if (d.Effect == null) return;
                d.Creation = d.Effect.creationTime; d.Life = host.BossNativeActorLife(d.Effect);
                _active.Add(d);
            }
            private static bool BossNativeStat(Stat stat)
            {
                switch (stat)
                {
                    case Stat.AttackPct: case Stat.PowerPct: case Stat.AttackSpeedPct: case Stat.CritChancePct: case Stat.CritDamagePct:
                    case Stat.MaxHealthPct: case Stat.MaxHealthFlat: case Stat.AttackFlat: case Stat.PowerFlat: case Stat.Armor:
                    case Stat.HealthRegen: case Stat.Haste: case Stat.MoveSpeedPct: case Stat.Tenacity: case Stat.FireAmp:
                    case Stat.ColdAmp: case Stat.LightAmp: case Stat.DarkAmp: case Stat.AttackRangePct: case Stat.FourthAttackShift: return true;
                    default: return false;
                }
            }
        }
    }
}

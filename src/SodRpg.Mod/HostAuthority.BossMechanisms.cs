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
            public BossScheduledPulse(BossAction action, Vector3 center, Vector3 end, float amount, bool magic, int delayMillis, Entity target = null, int stunMillis = 0,
                int slowMillis = 0, int slowPct = 0, float healOnFirstHit = 0f, float absorbRatio = 0f, float absorbCap = 0f)
            {
                Action = action; Center = center; End = end; Amount = amount; Magic = magic; DelayMillis = delayMillis;
                Target = target; HasTarget = !ReferenceEquals(target, null); TargetCreation = target != null ? target.creationTime : 0;
                TargetLife = target != null ? NativeInstance.BossNativeActorLife(target) : 0;
                TargetNetId = target != null ? target.persistentNetId : 0;
                StunMillis = Math.Max(0, Math.Min(250, stunMillis));
                SlowMillis = Math.Max(0, Math.Min(500, slowMillis)); SlowPct = Math.Max(0, Math.Min(20, slowPct));
                HealOnFirstHit = Math.Max(0, healOnFirstHit); AbsorbRatio = Math.Max(0, absorbRatio); AbsorbCap = Math.Max(0, absorbCap);
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
            public int SlowMillis { get; }
            public int SlowPct { get; }
            public float HealOnFirstHit { get; }
            public float AbsorbRatio { get; }
            public float AbsorbCap { get; }
            public BossScheduledPulse At(Vector3 point) => new BossScheduledPulse(Action, point, point + (End - Center), Amount, Magic, DelayMillis,
                Target, StunMillis, SlowMillis, SlowPct, HealOnFirstHit, AbsorbRatio, AbsorbCap);
        }

        private bool BossReserveSequence(HeroRuntime rt, string set, string profile, BossPulseBuffer pulses, float now,
            int maxInstances = 1, int lifetimeMillis = 0, long nativeLife = 0, int maxSetInstances = 4, bool replaceOldest = false)
        {
            try { return rt.Boss.Fields.ReserveSequence(this, rt, set, profile, pulses, now, maxInstances, lifetimeMillis, nativeLife, maxSetInstances, replaceOldest); }
            finally { pulses?.Reset(); }
        }
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
        internal readonly struct BossHitResult
        {
            public BossHitResult(int positiveHits, float hpDamage) { PositiveHits = positiveHits; HpDamage = hpDamage; }
            public int PositiveHits { get; }
            public float HpDamage { get; }
        }
        internal sealed class BossShapeAttack
        {
            private sealed class Scratch
            {
                public readonly int[] Hits = new int[64];
                public readonly RaycastHit2D[] Walls = new RaycastHit2D[128];
            }
            private readonly BossObjectPool<Scratch> _scratch = new BossObjectPool<Scratch>(8, () => new Scratch());
            public BossHitResult Execute(HostAuthority host, HeroRuntime rt, BossAction action, Vector3 center, Vector3 end, float amount, bool magic,
                BossShape? pulseShape = null, float? pulseRadius = null, float? pulseRange = null, int stunMillis = 0, int slowMillis = 0, int slowPct = 0)
            {
                var scratch = _scratch.Rent();
                if (scratch == null) return default;
                int hitCount = 0, positive = 0; float hpDamage = 0;
                var build = rt.Boss.Build; long epoch = rt.ShieldEquipmentEpoch, revision = rt.Boss.Revision;
                try
                {
                    var shape = pulseShape ?? action.Shape;
                    float radius = pulseRadius ?? action.RadiusMilli / 1000f;
                    float range = Math.Min(action.RangeMilli / 1000f, pulseRange ?? action.RangeMilli / 1000f);
                    bool line = shape == BossShape.Line || shape == BossShape.Column;
                    var direction = BossDirection(center, end);
                    if (direction == Vector3.zero) direction = rt.Hero.transform.forward;
                    if (line)
                    {
                        var legalEnd = Dew.GetValidAgentDestination_LinearSweep(center, center + direction * range);
                        if (!BossFinite(legalEnd) || Mathf.Abs(legalEnd.y - center.y) > 2f) return default;
                        range = Math.Min(range, BossDirectionDistance(center, legalEnd));
                        if (Physics.Raycast(center + Vector3.up * .2f, direction, out var ground, range, LayerMasks.Ground)) range = ground.distance;
                        var filter = new ContactFilter2D { useLayerMask = true, layerMask = LayerMasks.CollidableWithProjectile, useTriggers = true };
                        int walls = Physics2D.CircleCast(center.ToXY(), action.WidthMilli / 2000f, direction.ToXY(), filter, scratch.Walls, range);
                        for (int i = 0; i < walls; i++)
                        {
                            var hit = scratch.Walls[i];
                            if (!DewPhysics.TryGetEntity(hit.collider, out _) && DewPhysics.TryGetCollidableWithProjectile(hit.collider, out _))
                                range = Math.Min(range, hit.distance);
                        }
                        if (walls == scratch.Walls.Length || range <= .0001f) return default;
                    }
                    ListReturnHandle<Entity> handle;
                    var found = line
                        ? DewPhysics.SphereCastAllEntities(out handle, center, action.WidthMilli / 2000f, direction, range, EnemyFilter, rt.Hero)
                        : DewPhysics.OverlapCircleAllEntities(out handle, center, shape == BossShape.Fan ? range : radius, EnemyFilter, rt.Hero);
                    try
                    {
                        for (int i = 0; i < Math.Min(128, found.Count) && hitCount < Math.Min(64, action.MaxTargets); i++)
                        {
                            var victim = found[i];
                            if (!BossAlive(victim)) continue;
                            int id = victim.GetInstanceID(); bool duplicate = false;
                            for (int j = 0; j < hitCount; j++) if (scratch.Hits[j] == id) { duplicate = true; break; }
                            if (duplicate || shape == BossShape.Fan && Vector3.Angle(direction, BossDirection(center, victim.position)) > action.AngleMilli / 2000f) continue;
                            scratch.Hits[hitCount++] = id;
                            bool accepted = host.BossDamageHp(rt, victim, amount, magic, action.Element, out float hp);
                            if (accepted) { positive++; hpDamage += hp; }
                            if (rt.Boss.Build != build || rt.ShieldEquipmentEpoch != epoch || rt.Boss.Revision != revision || !BossAlive(rt.Hero)) break;
                            if (accepted && stunMillis > 0) rt.Boss.Defense.Stun(host, rt, victim, stunMillis, Time.time);
                            if (rt.Boss.Revision != revision) break;
                            if (accepted && slowMillis > 0 && slowPct > 0) rt.Boss.Defense.Slow(host, rt, victim, slowMillis, slowPct, Time.time);
                            if (rt.Boss.Revision != revision) break;
                            if (action.Payload == BossPayload.Push || action.Payload == BossPayload.Pull)
                                rt.Boss.EnemyMovement.Execute(rt, victim, line && action.Payload == BossPayload.Push ? victim.position - direction : center,
                                    action.Payload == BossPayload.Pull, action.MagnitudeMilli / 1000f, Time.time);
                            if (rt.Boss.Revision != revision) break;
                        }
                    }
                    finally { handle.Return(); }
                    return new BossHitResult(positive, hpDamage);
                }
                finally { _scratch.Return(scratch); }
            }
        }

        // M2: finite ballistic states, bounded per-wave target budgets, wall-first ordered collision.
        internal sealed class BossProjectileExecutor
        {
            private sealed class Wave
            {
                public readonly long[] Targets = new long[64];
                public readonly int[] Hits = new int[64];
                public int Count, Limit, MaxHitsPerTarget, Users;
                public string Set, Profile;
                public int Find(long target) { for (int i = 0; i < Count; i++) if (Targets[i] == target) return i; return -1; }
            }
            private sealed class Shot
            {
                public long Id, NativeLife, ReservationId, TargetLife, TerminalId;
                public string Set, Profile;
                public Vector3 Point, Direction;
                public float Remaining, Speed, Radius, Damage, Last, Expires, TerminalRadius, ChainRadius;
                public int ChainRemaining, ChainDelayMillis, HitCount;
                public bool Active, Processing, Magic, FirstHit, ExplodeAtEnd, ExplodeOnHit, Homing, ExpireAtLifetime, TerminalAtWall;
                public Entity Target;
                public Wave Wave;
                public BossElement Element;
                public BossAction Action;
                public readonly long[] Hits = new long[64];
                public bool Hit(long life) { for (int i = 0; i < HitCount; i++) if (Hits[i] == life) return true; return false; }
            }
            private readonly BossObjectPool<Shot> _pool = new BossObjectPool<Shot>(128, () => new Shot());
            private readonly BossObjectPool<Wave> _waves = new BossObjectPool<Wave>(64, () => new Wave());
            private readonly Shot[] _shots = new Shot[128];
            private readonly RaycastHit2D[] _collisions = new RaycastHit2D[128];
            private bool _ticking;
            public bool Execute(HostAuthority host, HeroRuntime rt, string set, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now,
                int shotCount = 0, int maxHitsPerTarget = 1, bool explodeAtEnd = false, float terminalRadius = 0f, string profile = null,
                bool delayedAlready = false, long nativeLife = 0, Entity adoptedTarget = null, bool explodeOnHit = false, bool homing = false,
                int chainCount = 0, float chainRadius = 0f, int chainDelayMillis = 150, bool expireAtLifetime = false, long reservationId = 0,
                float spreadDegrees = 0f, BossScheduledPulse? terminalPulse = null, bool terminalAtWall = false)
            {
                if (chainCount < 0 || chainCount > 3 || chainDelayMillis < 0 || !BossFinite(center) || !BossFinite(end)
                    || float.IsNaN(chainRadius) || float.IsInfinity(chainRadius) || chainRadius < 0
                    || float.IsNaN(spreadDegrees) || float.IsInfinity(spreadDegrees)) return false;
                int count = shotCount > 0 ? Math.Min(a.Count, shotCount) : a.Count;
                if (count < 1 || count > 64) return false;
                int owned = 0, free = 0, profileWaves = 0;
                for (int i = 0; i < _shots.Length; i++)
                {
                    var shot = _shots[i];
                    if (shot == null) { free++; continue; }
                    if (!shot.Active || shot.Set != set) continue;
                    owned++;
                    if (profile == null || shot.Profile != profile) continue;
                    bool first = true;
                    for (int j = 0; j < i; j++) if (_shots[j] != null && _shots[j].Active && ReferenceEquals(_shots[j].Wave, shot.Wave)) { first = false; break; }
                    if (first) profileWaves++;
                }
                if (owned + count > 64 || free < count || profile != null && profileWaves >= a.MaxInstances
                    || terminalPulse.HasValue && !rt.Boss.Fields.CanReserveTerminal(set, count)) return false;
                var wave = _waves.Rent();
                if (wave == null) return false;
                wave.Count = 0; wave.Users = 0; wave.Limit = Math.Min(64, a.MaxTargets);
                wave.MaxHitsPerTarget = Math.Max(1, Math.Min(count, maxHitsPerTarget)); wave.Set = set; wave.Profile = profile;
                var direction = BossDirection(center, end);
                if (direction == Vector3.zero) direction = rt.Hero.transform.forward;
                float speed = a.SpeedMilli > 0 ? a.SpeedMilli / 1000f : 12;
                float range = a.RangeMilli > 0 ? a.RangeMilli / 1000f : 8;
                float delay = delayedAlready ? 0 : a.DelayMillis / 1000f;
                if (explodeAtEnd && !explodeOnHit && !homing) range = Math.Min(range, BossDirectionDistance(center, end));
                float lifetime = a.LifetimeMillis > 0 ? a.LifetimeMillis / 1000f : range / speed;
                int created = 0;
                for (int slot = 0; slot < _shots.Length && created < count; slot++)
                {
                    if (_shots[slot] != null) continue;
                    var shot = _pool.Rent();
                    if (shot == null)
                    {
                        for (int j = 0; j < _shots.Length; j++) if (_shots[j] != null && ReferenceEquals(_shots[j].Wave, wave)) Remove(host, rt, j, now);
                        if (created == 0) _waves.Return(wave);
                        return false;
                    }
                    int i = created++;
                    var dir = a.Shape == BossShape.Radial ? Quaternion.Euler(0, 360f * i / count, 0) * direction
                        : Quaternion.Euler(0, count == 1 ? 0 : -spreadDegrees / 2 + spreadDegrees * i / (count - 1), 0) * direction;
                    Entity target = homing ? adoptedTarget : a.Anchor == BossAnchor.Marked ? rt.Boss.Ledger.NearestMark(center, now, a.LedgerId ?? set) : null;
                    shot.Id = ++rt.Boss.NextId; shot.Set = set; shot.Profile = profile; shot.Point = center; shot.Direction = dir;
                    shot.Remaining = range; shot.Speed = speed; shot.Radius = a.WidthMilli > 0 ? a.WidthMilli / 2000f : .2f;
                    shot.Damage = amount; shot.Magic = magic; shot.Last = now + delay; shot.Element = a.Element;
                    shot.Expires = now + delay + (expireAtLifetime ? lifetime : Math.Min(range / speed, lifetime));
                    shot.FirstHit = a.FirstHitOnly; shot.Wave = wave; shot.ExplodeAtEnd = explodeAtEnd; shot.ExplodeOnHit = explodeOnHit;
                    shot.TerminalRadius = terminalRadius; shot.Action = a; shot.NativeLife = nativeLife; shot.ReservationId = reservationId;
                    shot.Target = target; shot.TargetLife = target != null ? host.BossNativeActorLife(target) : 0;
                    shot.Homing = homing || a.Anchor == BossAnchor.Marked; shot.ExpireAtLifetime = expireAtLifetime;
                    shot.ChainRemaining = chainCount; shot.ChainRadius = chainRadius; shot.ChainDelayMillis = chainDelayMillis;
                    shot.HitCount = 0; shot.Active = true; shot.TerminalAtWall = terminalAtWall;
                    shot.TerminalId = terminalPulse.HasValue ? rt.Boss.Fields.ReserveTerminal(rt, set, profile, terminalPulse.Value, nativeLife, now) : 0;
                    wave.Users++; _shots[slot] = shot;
                    host.PublishBossVisual(rt, shot.Id, 5, center, center + dir * range, shot.Radius, shot.Last, shot.Expires,
                        element: a.Element, targetNetId: adoptedTarget != null ? adoptedTarget.persistentNetId : 0, nativeLife: nativeLife);
                }
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                if (_ticking) return;
                _ticking = true; long before = rt.Boss.NextId;
                try
                {
                    for (int i = _shots.Length - 1; i >= 0; i--)
                    {
                        var s = _shots[i];
                        if (s == null || !s.Active || s.Id > before || now < s.Last) continue;
                        s.Processing = true;
                        try { TickShot(host, rt, s, i, now); }
                        finally { s.Processing = false; if (!s.Active) Release(i, s); }
                        if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                    }
                }
                finally { _ticking = false; }
            }
            private void TickShot(HostAuthority host, HeroRuntime rt, Shot s, int index, float now)
            {
                if (s.Homing && BossAlive(s.Target) && host.BossNativeSameLife(s.Target, s.TargetLife)) s.Direction = BossDirection(s.Point, s.Target.position);
                float distance = Math.Min(s.Remaining, s.Speed * Math.Max(0, Math.Min(now, s.Expires) - s.Last));
                s.Last = now;
                bool ended = false, blocked = false, impact = false, chained = false;
                if (distance > 0)
                {
                    var filter = new ContactFilter2D { useLayerMask = true, layerMask = LayerMasks.Entity | LayerMasks.CollidableWithProjectile, useTriggers = true };
                    int collisionCount = Physics2D.CircleCast(s.Point.ToXY(), s.Radius, s.Direction.ToXY(), filter, _collisions, distance);
                    float wall = distance + 1;
                    if (Physics.Raycast(s.Point, s.Direction, out var ground, distance, LayerMasks.Ground)) wall = ground.distance;
                    for (int i = 0; i < collisionCount; i++)
                    {
                        var hit = _collisions[i];
                        if (!DewPhysics.TryGetEntity(hit.collider, out _) && DewPhysics.TryGetCollidableWithProjectile(hit.collider, out _) && hit.distance < wall) wall = hit.distance;
                    }
                    // Saturation cannot prove a safe flight segment: refuse that segment rather than crossing an omitted wall.
                    if (collisionCount == _collisions.Length) { Remove(host, rt, index, now); return; }
                    for (int i = 1; i < collisionCount; i++)
                    {
                        var hit = _collisions[i]; int j = i - 1;
                        while (j >= 0 && _collisions[j].distance > hit.distance) { _collisions[j + 1] = _collisions[j]; j--; }
                        _collisions[j + 1] = hit;
                    }
                    float travelled = Math.Min(distance, wall);
                    if (!s.ExplodeAtEnd || s.ExplodeOnHit)
                    {
                        ListReturnHandle<Entity> handle;
                        var entities = DewPhysics.SphereCastAllEntities(out handle, s.Point, s.Radius, s.Direction, distance, EnemyFilter, rt.Hero);
                        try
                        {
                            int cursor = 0;
                            for (int step = 0; step < 64 && s.HitCount < 64; step++)
                            {
                                Entity nearest = null; float first = wall;
                                while (cursor < collisionCount)
                                {
                                    var hit = _collisions[cursor++];
                                    if (hit.distance >= wall) { cursor = collisionCount; break; }
                                    if (!DewPhysics.TryGetEntity(hit.collider, out var e) || !BossAlive(e)
                                        || e.Status.hasUncollidable || e.GetRelation(rt.Hero) != EntityRelation.Enemy) continue;
                                    bool found = false;
                                    for (int j = 0; j < Math.Min(128, entities.Count); j++) if (entities[j] == e) { found = true; break; }
                                    if (!found) continue;
                                    long life = host.BossNativeActorLife(e);
                                    if (s.Hit(life) || s.ChainRemaining > 0 && s.Wave.Find(life) >= 0) continue;
                                    nearest = e; first = hit.distance; break;
                                }
                                if (nearest == null) break;
                                long target = host.BossNativeActorLife(nearest); s.Hits[s.HitCount++] = target;
                                if (s.ExplodeOnHit) { impact = true; ended = true; travelled = first; break; }
                                int targetSlot = s.Wave.Find(target);
                                int hits = targetSlot < 0 ? 0 : s.Wave.Hits[targetSlot];
                                if (hits < s.Wave.MaxHitsPerTarget && (targetSlot >= 0 || s.Wave.Count < s.Wave.Limit))
                                {
                                    if (targetSlot < 0) { targetSlot = s.Wave.Count++; s.Wave.Targets[targetSlot] = target; }
                                    s.Wave.Hits[targetSlot] = hits + 1;
                                    host.BossDamage(rt, nearest, s.Damage, s.Magic, s.Element);
                                    if (!s.Active || rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                                }
                                if (s.ChainRemaining > 0)
                                {
                                    s.ChainRemaining--; Vector3 hitPoint = nearest.position;
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
                    if (chained) return;
                    blocked = !impact && wall <= travelled; ended |= blocked;
                    s.Point += s.Direction * travelled; s.Remaining = Math.Max(0, s.Remaining - travelled);
                }
                if (!ended && now < s.Expires && (s.Remaining > .0001f || s.ExpireAtLifetime)) return;
                if (!blocked && (impact || s.ExplodeAtEnd && (!ended || s.ExplodeOnHit)))
                {
                    rt.Boss.Shapes.Execute(host, rt, s.Action, s.Point, s.Point, s.Damage, s.Magic, BossShape.Circle, s.TerminalRadius);
                    if (!s.Active || rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                    host.PublishBossVisual(rt, ++rt.Boss.NextId, 1, s.Point, s.Point, s.TerminalRadius, now, now + .25f, element: s.Element, nativeLife: s.NativeLife);
                }
                if (s.TerminalId != 0 && (!blocked || s.TerminalAtWall))
                {
                    rt.Boss.Fields.CompleteTerminal(host, rt, s.TerminalId, s.Point, now); s.TerminalId = 0;
                }
                Remove(host, rt, index, now);
            }
            private static Entity NextChainTarget(HostAuthority host, HeroRuntime rt, Shot shot, Vector3 point)
            {
                ListReturnHandle<Entity> handle;
                var entities = DewPhysics.OverlapCircleAllEntities(out handle, point, shot.ChainRadius, EnemyFilter, rt.Hero);
                Entity nearest = null; float best = float.MaxValue;
                try
                {
                    for (int i = 0; i < Math.Min(128, entities.Count); i++)
                    {
                        var e = entities[i];
                        if (!BossAlive(e) || e.Status.hasUncollidable || shot.Wave.Find(host.BossNativeActorLife(e)) >= 0) continue;
                        float distance = (e.position - point).Flattened().sqrMagnitude;
                        if (distance < best || distance == best && (nearest == null || e.persistentNetId < nearest.persistentNetId)) { nearest = e; best = distance; }
                    }
                }
                finally { handle.Return(); }
                return nearest;
            }
            public void CancelReservation(HostAuthority host, HeroRuntime rt, long id, float now)
            {
                if (id <= 0) return;
                for (int i = 0; i < _shots.Length; i++) if (_shots[i] != null && (_shots[i].ReservationId == id || _shots[i].TerminalId == id)) Remove(host, rt, i, now);
            }
            public void CancelNative(HostAuthority host, HeroRuntime rt, string set, long nativeLife, float now)
            {
                for (int i = 0; i < _shots.Length; i++) if (_shots[i] != null && _shots[i].Set == set && _shots[i].NativeLife == nativeLife) Remove(host, rt, i, now);
            }
            public void CancelProfile(HostAuthority host, HeroRuntime rt, string set, string profile, float now)
            {
                for (int i = 0; i < _shots.Length; i++) if (_shots[i] != null && _shots[i].Set == set && _shots[i].Profile == profile) Remove(host, rt, i, now);
            }
            private void Remove(HostAuthority host, HeroRuntime rt, int index, float now)
            {
                var s = _shots[index]; if (s == null || !s.Active) return;
                s.Active = false;
                long terminal = s.TerminalId; s.TerminalId = 0;
                if (terminal != 0) rt.Boss.Fields.CancelReservation(host, rt, terminal, now);
                host.PublishBossVisual(rt, s.Id, 5, s.Point, s.Point, s.Radius, now, now, true);
                if (!s.Processing) Release(index, s);
            }
            private void Release(int index, Shot s)
            {
                _shots[index] = null;
                if (--s.Wave.Users == 0) { s.Wave.Set = s.Wave.Profile = null; _waves.Return(s.Wave); }
                s.Wave = null; s.Target = null; s.Action = null; s.Set = s.Profile = null; _pool.Return(s);
            }
            public void Clear(bool preserveNative = false)
            {
                for (int i = 0; i < _shots.Length; i++)
                {
                    var s = _shots[i]; if (s == null || preserveNative && s.NativeLife != 0) continue;
                    s.Active = false; s.TerminalId = 0;
                    if (!s.Processing) Release(i, s);
                }
            }
        }

        // M3: one reservation owns all finite pulses; buds are a stricter four-slot collection.
        internal sealed class BossFieldExecutor
        {
            internal sealed class Field
            {
                public long Id, NativeLife; public string Set, Profile; public BossAction Action;
                public Vector3 Center, End; public float Due, Created, Damage, Until, Absorbed; public int Pulse, PulseCount;
                public bool Active, Processing, Magic, Bud, Collectible, Sequence, HasPoints, TerminalPending, Healed;
                public readonly Vector3[] Points = new Vector3[32];
                public readonly BossScheduledPulse[] Scheduled = new BossScheduledPulse[32];
                public readonly long[] VisualIds = new long[32];
            }
            private readonly BossObjectPool<Field> _pool = new BossObjectPool<Field>(64, () => new Field());
            private readonly Field[] _fields = new Field[64];
            private bool _ticking;
            private int FreeSlot() { for (int i = 0; i < _fields.Length; i++) if (_fields[i] == null) return i; return -1; }
            private Field Rent(HeroRuntime rt, string set, string profile, float now)
            {
                int slot = FreeSlot(); if (slot < 0) return null;
                var f = _pool.Rent(); if (f == null) return null;
                f.Id = ++rt.Boss.NextId; f.Set = set; f.Profile = profile; f.Created = now;
                f.Active = true; _fields[slot] = f; return f;
            }
            private void Release(int index, Field f)
            {
                _fields[index] = null; f.Set = f.Profile = null; f.Action = null;
                Array.Clear(f.Scheduled, 0, 32); Array.Clear(f.VisualIds, 0, 32);
                f.Active = f.Processing = f.Sequence = f.HasPoints = f.TerminalPending = f.Healed = f.Bud = f.Collectible = false;
                f.Pulse = f.PulseCount = 0; f.NativeLife = 0; f.Absorbed = 0; _pool.Return(f);
            }
            private void Remove(HostAuthority host, HeroRuntime rt, int index, float now, bool visuals = true)
            {
                var f = _fields[index]; if (f == null || !f.Active) return;
                f.Active = false;
                if (visuals)
                {
                    if (!f.Sequence) host.PublishBossVisual(rt, f.Id, f.Bud ? (f.Collectible ? 2 : 3) : 4, f.Center, f.End, f.Action.RadiusMilli / 1000f, now, now, true);
                    else for (int i = 0; i < f.PulseCount; i++) if (f.VisualIds[i] != 0)
                        host.PublishBossVisual(rt, f.VisualIds[i], 4, Vector3.zero, Vector3.zero, 0, now, now, true);
                }
                if (!f.Processing) Release(index, f);
            }
            public bool Reserve(HostAuthority host, HeroRuntime rt, string set, string profile, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now, Vector3[] points = null)
            {
                if (a.Count < 1 || a.Count > 32 || points != null && points.Length < a.Count) return false;
                bool bud = a.Payload == BossPayload.Bud;
                int owned = 0, sameProfile = 0, oldest = -1;
                for (int i = 0; i < _fields.Length; i++)
                {
                    var f = _fields[i]; if (f == null || !f.Active || f.Set != set) continue;
                    owned++;
                    if (f.Profile == profile && f.Bud == bud)
                    { sameProfile++; if (oldest < 0 || f.Created < _fields[oldest].Created) oldest = i; }
                }
                if (FreeSlot() < 0) return false;
                if (owned >= 4 || !bud && sameProfile >= a.MaxInstances)
                {
                    if (!a.ReplaceOldest || bud || oldest < 0) return false;
                    Remove(host, rt, oldest, now);
                }
                var field = Rent(rt, set, profile, now); if (field == null) return false;
                field.Action = a; field.Center = center; field.End = end; field.HasPoints = points != null;
                field.Due = now + a.DelayMillis / 1000f; field.Damage = amount; field.Magic = magic;
                field.Bud = bud; field.Collectible = a.Collectible; field.PulseCount = bud ? 1 : a.Count;
                if (points != null) Array.Copy(points, field.Points, field.PulseCount);
                host.PublishBossVisual(rt, field.Id, bud ? (a.Collectible ? 2 : 3) : (points != null ? 6 : 4), center, end, a.RadiusMilli / 1000f,
                    field.Due, field.Due + (field.PulseCount - 1) * a.IntervalMillis / 1000f + .2f);
                return true;
            }
            public int Count(string set, string profile = null)
            {
                int count = 0;
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Active && _fields[i].Set == set && (profile == null || _fields[i].Profile == profile)) count++;
                return count;
            }
            public int PulseCount(string set, string profile)
            {
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Active && _fields[i].Set == set && _fields[i].Profile == profile) return _fields[i].Pulse;
                return 0;
            }
            public long Latest(string set, string profile)
            {
                long id = 0;
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Active && _fields[i].Set == set && _fields[i].Profile == profile && _fields[i].Id > id) id = _fields[i].Id;
                return id;
            }
            public bool Contains(long id)
            {
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Active && _fields[i].Id == id) return true;
                return false;
            }
            public void CancelReservation(HostAuthority host, HeroRuntime rt, long id, float now)
            {
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Id == id) { Remove(host, rt, i, now); break; }
                rt.Boss.Projectiles.CancelReservation(host, rt, id, now);
            }
            public bool ReserveSequence(HostAuthority host, HeroRuntime rt, string set, string profile, BossPulseBuffer pulses,
                float now, int maxInstances, int lifetimeMillis, long nativeLife, int maxSetInstances, bool replaceOldest)
            {
                if (pulses == null || pulses.Count == 0 && lifetimeMillis <= 0 || maxInstances < 1 || maxInstances > 4
                    || lifetimeMillis < 0 || nativeLife < 0 || maxSetInstances < 1 || maxSetInstances > 4 || FreeSlot() < 0) return false;
                int lastDelay = 0;
                for (int i = 0; i < pulses.Count; i++)
                {
                    var pulse = pulses[i];
                    if (pulse.Action == null || pulse.DelayMillis < lastDelay || !BossFinite(pulse.Center) || !BossFinite(pulse.End)
                        || float.IsNaN(pulse.Amount) || float.IsInfinity(pulse.Amount) || pulse.Amount < 0
                        || float.IsNaN(pulse.HealOnFirstHit) || float.IsInfinity(pulse.HealOnFirstHit)
                        || float.IsNaN(pulse.AbsorbRatio) || float.IsInfinity(pulse.AbsorbRatio)
                        || float.IsNaN(pulse.AbsorbCap) || float.IsInfinity(pulse.AbsorbCap)) return false;
                    lastDelay = pulse.DelayMillis;
                }
                while (Count(set) >= maxSetInstances || Count(set, profile) >= maxInstances)
                {
                    if (!replaceOldest) return false;
                    int oldest = -1; bool setFull = Count(set) >= maxSetInstances;
                    for (int i = 0; i < _fields.Length; i++)
                    {
                        var field = _fields[i];
                        if (field != null && field.Active && field.Set == set && (setFull || field.Profile == profile)
                            && (oldest < 0 || field.Created < _fields[oldest].Created)) oldest = i;
                    }
                    if (oldest < 0) return false;
                    CancelReservation(host, rt, _fields[oldest].Id, now);
                }
                var f = Rent(rt, set, profile, now); if (f == null) return false;
                f.Sequence = true; f.NativeLife = nativeLife; f.Until = now + Math.Max(lifetimeMillis, lastDelay) / 1000f;
                f.PulseCount = pulses.Count; f.Due = pulses.Count == 0 ? f.Until : now + pulses[0].DelayMillis / 1000f;
                for (int i = 0; i < pulses.Count; i++) { f.Scheduled[i] = pulses[i]; PublishPulse(host, rt, f, i); }
                return true;
            }
            public bool CanReserveTerminal(string set, int count)
            {
                int free = 0; for (int i = 0; i < _fields.Length; i++) if (_fields[i] == null) free++;
                return free >= count && Count(set) + count <= 4;
            }
            public long ReserveTerminal(HeroRuntime rt, string set, string profile, BossScheduledPulse pulse, long nativeLife, float now)
            {
                var f = Rent(rt, set, profile, now); if (f == null) return 0;
                f.Sequence = f.TerminalPending = true; f.PulseCount = 1; f.Scheduled[0] = pulse; f.NativeLife = nativeLife;
                f.Until = float.MaxValue; return f.Id;
            }
            public void CompleteTerminal(HostAuthority host, HeroRuntime rt, long id, Vector3 endpoint, float now)
            {
                for (int i = 0; i < _fields.Length; i++)
                {
                    var f = _fields[i]; if (f == null || !f.Active || f.Id != id || !f.TerminalPending) continue;
                    f.Scheduled[0] = f.Scheduled[0].At(endpoint); f.TerminalPending = false;
                    f.Created = now; f.Until = now + f.Scheduled[0].DelayMillis / 1000f;
                    PublishPulse(host, rt, f, 0); return;
                }
            }
            private static void PublishPulse(HostAuthority host, HeroRuntime rt, Field f, int index)
            {
                var pulse = f.Scheduled[index];
                if (pulse.Action.Payload == BossPayload.Shield || pulse.Action.Payload == BossPayload.Heal) return;
                int kind = pulse.Action.Shape == BossShape.Line || pulse.Action.Shape == BossShape.Column || pulse.Action.Shape == BossShape.Radial ? 6 : 4;
                float due = f.Created + pulse.DelayMillis / 1000f, range = pulse.Action.RangeMilli / 1000f;
                if (pulse.Action.Shape == BossShape.Line || pulse.Action.Shape == BossShape.Column) range = Math.Min(range, BossDirectionDistance(pulse.Center, pulse.End));
                f.VisualIds[index] = ++rt.Boss.NextId;
                host.PublishBossVisual(rt, f.VisualIds[index], kind, pulse.Center, pulse.End, pulse.Action.RadiusMilli / 1000f, due, due + .25f,
                    element: pulse.Action.Element, shape: pulse.Action.Shape, range: range, width: pulse.Action.WidthMilli / 1000f,
                    angle: pulse.Action.AngleMilli / 1000f, count: pulse.Action.Shape == BossShape.Radial ? pulse.Action.Count : 0,
                    targetNetId: pulse.TargetNetId, nativeLife: f.NativeLife);
            }
            public void CancelProfile(HostAuthority host, HeroRuntime rt, string set, string profile, float now)
            {
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Set == set && _fields[i].Profile == profile) Remove(host, rt, i, now);
            }
            public void CancelNative(HostAuthority host, HeroRuntime rt, string set, long nativeLife, float now)
            {
                for (int i = 0; i < _fields.Length; i++) if (_fields[i] != null && _fields[i].Set == set && _fields[i].NativeLife == nativeLife) Remove(host, rt, i, now);
            }
            public bool Collect(HostAuthority host, HeroRuntime rt, string set, Vector3 origin, Vector3 prefer, float range, float now, long before, out Vector3 collectedCenter)
            {
                int best = -1; float distance = float.MaxValue; collectedCenter = default;
                for (int i = 0; i < _fields.Length; i++)
                {
                    var f = _fields[i];
                    if (f == null || !f.Active || !f.Bud || !f.Collectible || f.Set != set || f.Id > before
                        || now < f.Created + BossProfiles.BudCollectibleDelayMillis / 1000f || now >= f.Due || (f.Center - origin).sqrMagnitude > range * range) continue;
                    float d = (f.Center - prefer).sqrMagnitude;
                    if (d < distance || d == distance && (best < 0 || f.Id < _fields[best].Id)) { best = i; distance = d; }
                }
                if (best < 0 || !BossReady(rt, set, now, 1000)) return false;
                var field = _fields[best]; field.Processing = true; collectedCenter = field.Center;
                try
                {
                    Remove(host, rt, best, now);
                    rt.Boss.Shapes.Execute(host, rt, field.Action, field.Center, field.End, field.Damage, field.Magic);
                    host.PublishBossVisual(rt, ++rt.Boss.NextId, field.Collectible ? 2 : 3, field.Center, field.Center, field.Action.RadiusMilli / 1000f, now, now + .3f);
                }
                finally { field.Processing = false; Release(best, field); }
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                if (_ticking) return;
                _ticking = true; long before = rt.Boss.NextId;
                try
                {
                    for (int i = _fields.Length - 1; i >= 0; i--)
                    {
                        var f = _fields[i]; if (f == null || !f.Active || f.Id > before || f.TerminalPending) continue;
                        f.Processing = true;
                        try { TickField(host, rt, f, i, now); }
                        finally { f.Processing = false; if (!f.Active) Release(i, f); }
                        if (rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                    }
                }
                finally { _ticking = false; }
            }
            private void TickField(HostAuthority host, HeroRuntime rt, Field f, int index, float now)
            {
                if (f.Sequence)
                {
                    while (f.Pulse < f.PulseCount && now >= f.Created + f.Scheduled[f.Pulse].DelayMillis / 1000f)
                    {
                        int pulseIndex = f.Pulse++; var pulse = f.Scheduled[pulseIndex]; BossHitResult result = default;
                        var center = pulse.Action.FollowOwner ? rt.Hero.agentPosition : pulse.Center;
                        var end = pulse.Action.FollowOwner ? center + (pulse.End - pulse.Center) : pulse.End;
                        if (pulse.HasTarget)
                        {
                            if (BossAlive(pulse.Target) && pulse.Target.creationTime == pulse.TargetCreation && host.BossNativeSameLife(pulse.Target, pulse.TargetLife)
                                && host.BossDamageHp(rt, pulse.Target, pulse.Amount, pulse.Magic, pulse.Action.Element, out float hp))
                            {
                                result = new BossHitResult(1, hp);
                                if (f.Active && rt.Boss.Build != null)
                                {
                                    if (pulse.StunMillis > 0) rt.Boss.Defense.Stun(host, rt, pulse.Target, pulse.StunMillis, now);
                                    if (pulse.SlowMillis > 0 && pulse.SlowPct > 0) rt.Boss.Defense.Slow(host, rt, pulse.Target, pulse.SlowMillis, pulse.SlowPct, now);
                                }
                            }
                            if (!f.Active || rt.Boss.Build == null) return;
                            if (f.VisualIds[pulseIndex] != 0) host.PublishBossVisual(rt, f.VisualIds[pulseIndex], 4, pulse.Center, pulse.End, pulse.Action.RadiusMilli / 1000f,
                                now, now + .25f, element: pulse.Action.Element, targetNetId: pulse.TargetNetId, nativeLife: f.NativeLife);
                        }
                        else if (pulse.Action.Payload == BossPayload.Shield || pulse.Action.Payload == BossPayload.Heal
                            || pulse.Action.Payload == BossPayload.Modifier || pulse.Action.Payload == BossPayload.Unstoppable)
                            rt.Boss.Defense.Execute(host, rt, f.Profile, pulse.Action, pulse.Amount, now);
                        else if (pulse.Action.Mechanism == BossMechanism.Projectile)
                        {
                            if (f.VisualIds[pulseIndex] != 0) host.PublishBossVisual(rt, f.VisualIds[pulseIndex], 4, center, end, 0, now, now, true);
                            rt.Boss.Projectiles.Execute(host, rt, f.Set, pulse.Action, center, end, pulse.Amount, pulse.Magic, now,
                                maxHitsPerTarget: pulse.Action.Shape == BossShape.Radial ? pulse.Action.Count : 1, profile: f.Profile, delayedAlready: true, nativeLife: f.NativeLife);
                        }
                        else
                        {
                            float? range = pulse.Action.Shape == BossShape.Line || pulse.Action.Shape == BossShape.Column ? (float?)BossDirectionDistance(center, end) : null;
                            result = rt.Boss.Shapes.Execute(host, rt, pulse.Action, center, end, pulse.Amount, pulse.Magic, pulseRange: range,
                                stunMillis: pulse.StunMillis, slowMillis: pulse.SlowMillis, slowPct: pulse.SlowPct);
                            if (!f.Active || rt.Boss.Build == null) return;
                            if (pulse.Action.FollowOwner && f.VisualIds[pulseIndex] != 0)
                                host.PublishBossVisual(rt, f.VisualIds[pulseIndex], 4, center, end, pulse.Action.RadiusMilli / 1000f, now, now + .25f,
                                    element: pulse.Action.Element, shape: pulse.Action.Shape, range: pulse.Action.RangeMilli / 1000f,
                                    width: pulse.Action.WidthMilli / 1000f, angle: pulse.Action.AngleMilli / 1000f, nativeLife: f.NativeLife);
                        }
                        if (!f.Active || rt.Boss.Build == null || !BossAlive(rt.Hero)) return;
                        float heal = 0;
                        if (!f.Healed && result.PositiveHits > 0 && pulse.HealOnFirstHit > 0) { f.Healed = true; heal = pulse.HealOnFirstHit; }
                        if (pulse.AbsorbRatio > 0 && pulse.AbsorbCap > f.Absorbed)
                        {
                            float award = Math.Min(pulse.AbsorbCap - f.Absorbed, result.HpDamage * pulse.AbsorbRatio);
                            f.Absorbed += award; heal += award;
                        }
                        if (heal > 0) host.BossHeal(rt, heal);
                        if (!f.Active || rt.Boss.Build == null) return;
                    }
                    if (f.Pulse == f.PulseCount && now >= f.Until) Remove(host, rt, index, now, false);
                    return;
                }
                if (now < f.Due) return;
                while (now >= f.Due && f.Pulse < f.PulseCount)
                {
                    Vector3 point = f.Action.FollowOwner ? rt.Hero.agentPosition : f.HasPoints ? f.Points[f.Pulse] : f.Center;
                    if (BossFinite(point))
                    {
                        if (f.Action.Payload == BossPayload.Heal || f.Action.Payload == BossPayload.Shield || f.Action.Payload == BossPayload.Modifier || f.Action.Payload == BossPayload.Unstoppable)
                            rt.Boss.Defense.Execute(host, rt, f.Profile, f.Action, f.Damage, now);
                        else rt.Boss.Shapes.Execute(host, rt, f.Action, point, f.End, f.Damage, f.Magic, f.HasPoints ? BossShape.Circle : (BossShape?)null);
                        if (!f.Active || rt.Boss.Build == null) return;
                        if (f.Bud) host.PublishBossVisual(rt, ++rt.Boss.NextId, f.Collectible ? 2 : 3, point, point, f.Action.RadiusMilli / 1000f, now, now + .3f);
                        else if (f.Action.FollowOwner) host.PublishBossVisual(rt, f.Id, 4, point, point, f.Action.RadiusMilli / 1000f, now,
                            f.Created + f.Action.DelayMillis / 1000f + f.Action.Count * f.Action.IntervalMillis / 1000f + .2f);
                    }
                    f.Pulse++; f.Due = f.Created + f.Action.DelayMillis / 1000f + f.Pulse * f.Action.IntervalMillis / 1000f;
                }
                if (f.Pulse == f.PulseCount) Remove(host, rt, index, now);
            }
            public void Clear(bool preserveNative = false)
            {
                for (int i = 0; i < _fields.Length; i++)
                {
                    var f = _fields[i]; if (f == null || preserveNative && f.NativeLife != 0) continue;
                    f.Active = false; if (!f.Processing) Release(i, f);
                }
            }
        }

        private void BossHeal(HeroRuntime rt, float amount)
        {
            if (amount <= 0 || !BossAlive(rt.Hero)) return;
            EnterGenerated(rt.Hero);
            try { rt.Hero.Heal(amount).Dispatch(rt.Hero); }
            finally { ExitGenerated(rt.Hero); }
        }

        // M4: no terrain traversal or immunity; this never emits a native movement event.
        internal sealed class BossMovementExecutor
        {
            private readonly DispByDestination _displacement = new DispByDestination();
            private bool _starting;
            public BossMovementExecutor() { BossDisplacementReuse.Reset(_displacement); }
            public bool Execute(HostAuthority host, HeroRuntime rt, BossAction a, Vector3 target, float now)
            {
                if (_starting || _displacement.isAlive || rt.Hero.Control.isDisplacing || rt.Hero.Status.hasRoot || rt.Hero.Status.hasStun
                    || !BossGround(rt.Hero.agentPosition, target, Math.Min(8, a.RangeMilli / 1000f), out var point)
                    || !BossDisplacementReuse.Reset(_displacement)) return false;
                _displacement.destination = point; _displacement.duration = Math.Max(.05f, a.LifetimeMillis / 1000f);
                _displacement.isFriendly = true; _displacement.isDodging = false; _displacement.canGoOverTerrain = false; _displacement.isCanceledByCC = true;
                _starting = true;
                host.EnterGenerated(rt.Hero);
                try
                {
                    rt.Hero.Control.StartDisplacement(_displacement);
                }
                finally { _starting = false; host.ExitGenerated(rt.Hero); }
                host.PublishBossVisual(rt, ++rt.Boss.NextId, 6, rt.Hero.agentPosition, point, 0.1f, now, now + Math.Max(0.05f, a.LifetimeMillis / 1000f));
                return true;
            }
        }

        // M5: positional success is independent from the damage dispatch.
        internal sealed class BossEnemyMovementExecutor
        {
            private readonly Entity[] _targets = new Entity[64];
            private readonly float[] _ready = new float[64];
            private readonly long[] _lives = new long[64];
            private readonly Entity[] _owners = new Entity[64];
            private readonly DispByDestination[] _displacements = new DispByDestination[64];
            private readonly bool[] _starting = new bool[64];
            public BossEnemyMovementExecutor()
            {
                for (int i = 0; i < 64; i++) { _displacements[i] = new DispByDestination(); BossDisplacementReuse.Reset(_displacements[i]); }
            }
            public bool Execute(HeroRuntime rt, Entity victim, Vector3 origin, bool pull, float distance, float now, float duration = .2f, float gateSeconds = 1f)
            {
                if (!BossAlive(victim) || victim == rt.Hero || victim.GetRelation(rt.Hero) != EntityRelation.Enemy || victim.IsAnyBoss()
                    || victim.Status.hasCrowdControlImmunity || victim.Status.hasUncollidable || victim.Control.isDisplacing) return false;
                if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0 || float.IsNaN(gateSeconds) || float.IsInfinity(gateSeconds) || gateSeconds < 0) return false;
                int slot = -1;
                for (int i = 0; i < _targets.Length; i++)
                {
                    bool available = !_starting[i] && !_displacements[i].isAlive && (_owners[i] == null || !ReferenceEquals(_owners[i].Control.ongoingDisplacement, _displacements[i]));
                    if (_targets[i] == victim && NativeInstance.BossNativeSameLife(victim, _lives[i]))
                    { if (_ready[i] > now || !available) return false; slot = i; break; }
                    if (slot < 0 && available && (_targets[i] == null || _ready[i] <= now)) slot = i;
                }
                if (slot < 0) return false;
                var direction = BossDirection(pull ? victim.position : origin, pull ? origin : victim.position);
                float length = Math.Min(2, Math.Max(0, distance));
                if (pull) length = Math.Min(length, Vector3.Distance(victim.position, origin));
                if (direction == Vector3.zero || !BossGround(victim.agentPosition, victim.agentPosition + direction * length, length, out var point)) return false;
                var displacement = _displacements[slot]; if (!BossDisplacementReuse.Reset(displacement)) return false;
                displacement.destination = point; displacement.duration = duration; displacement.isFriendly = false;
                displacement.isDodging = false; displacement.canGoOverTerrain = false; displacement.isCanceledByCC = true;
                _targets[slot] = victim; _owners[slot] = victim; _lives[slot] = NativeInstance.BossNativeActorLife(victim); _ready[slot] = now + gateSeconds;
                _starting[slot] = true;
                try { victim.Control.StartDisplacement(displacement); }
                finally { _starting[slot] = false; }
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
                public float Due, Until, Damage, NativeCreation; public bool Magic, IsNative, Active, Processing; public int Shots; public Summon Native;
            }
            private readonly BossObjectPool<Deployable> _pool = new BossObjectPool<Deployable>(32, () => new Deployable());
            private readonly Deployable[] _owned = new Deployable[32];
            private bool _ticking;
            private Deployable Rent(HeroRuntime rt, string set, int limit)
            {
                int count = 0, free = -1;
                for (int i = 0; i < _owned.Length; i++)
                {
                    if (_owned[i] == null) { if (free < 0) free = i; }
                    else if (_owned[i].Active && _owned[i].Set == set) count++;
                }
                if (count >= limit || free < 0) return null;
                var d = _pool.Rent(); if (d == null) return null;
                d.Id = ++rt.Boss.NextId; d.Epoch = rt.ShieldEquipmentEpoch; d.Set = set; d.Owner = rt.Hero; d.Active = true; _owned[free] = d;
                return d;
            }
            public bool Execute(HostAuthority host, HeroRuntime rt, string set, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now)
            {
                if (a.LifetimeMillis <= 0 || a.Count < 1 || a.Count > 32) return false;
                var d = Rent(rt, set, Math.Min(2, a.MaxInstances)); if (d == null) return false;
                d.Action = a; d.Center = center; d.End = end; d.Due = now + a.DelayMillis / 1000f;
                d.Until = now + a.LifetimeMillis / 1000f; d.Damage = amount; d.Magic = magic;
                host.PublishBossVisual(rt, d.Id, 8, center, end, a.RadiusMilli / 1000f, d.Due, d.Until); return true;
            }
            public bool RegisterNative(HostAuthority host, HeroRuntime rt, string set, Summon summon, SkillTrigger source, BossAction action, float now)
            {
                if (!NetworkServer.active || summon == null || !summon.isActive || source == null || source.owner != rt.Hero
                    || !BossNativeEquippedSkill(rt.Hero, source) || summon.hero != rt.Hero || summon.info.caster != rt.Hero || action.LifetimeMillis <= 0) return false;
                Actor ancestor = summon.parentActor; SkillTrigger nearestSource = null;
                for (int depth = 0; depth < 32 && ancestor != null; depth++, ancestor = ancestor.parentActor)
                    if (ancestor is SkillTrigger skill) { nearestSource = skill; break; }
                if (nearestSource != source) return false;
                for (int i = 0; i < _owned.Length; i++) if (_owned[i] != null && _owned[i].Active && _owned[i].Native == summon) return false;
                var d = Rent(rt, set, 2); if (d == null) return false;
                d.Native = summon; d.IsNative = true; d.Center = summon.agentPosition; d.End = d.Center;
                d.NativeCreation = summon.creationTime; d.Until = now + action.LifetimeMillis / 1000f;
                host.PublishBossVisual(rt, d.Id, 8, d.Center, d.Center, action.RadiusMilli / 1000f, now, d.Until); return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                if (_ticking) return;
                _ticking = true; long before = rt.Boss.NextId;
                try
                {
                    for (int i = _owned.Length - 1; i >= 0; i--)
                    {
                        var d = _owned[i]; if (d == null || !d.Active || d.Id > before) continue;
                        d.Processing = true;
                        try
                        {
                            if (d.Epoch != rt.ShieldEquipmentEpoch || d.Owner != rt.Hero || now >= d.Until
                                || d.IsNative && (d.Native == null || !d.Native.isActive || d.Native.creationTime != d.NativeCreation || d.Native.hero != d.Owner))
                            {
                                Remove(i, d); host.PublishBossVisual(rt, d.Id, 8, d.Center, d.End, 0, now, now, true); continue;
                            }
                            if (d.IsNative || d.Shots >= d.Action.Count || now < d.Due) continue;
                            ListReturnHandle<Entity> handle;
                            var enemies = DewPhysics.OverlapCircleAllEntities(out handle, d.Center, d.Action.RangeMilli / 1000f, EnemyFilter, rt.Hero);
                            try
                            {
                                Entity nearest = null; float best = float.MaxValue;
                                for (int j = 0; j < Math.Min(128, enemies.Count); j++)
                                {
                                    var e = enemies[j];
                                    if (BossAlive(e) && (e.position - d.Center).sqrMagnitude < best) { nearest = e; best = (e.position - d.Center).sqrMagnitude; }
                                }
                                if (nearest != null)
                                {
                                    bool admitted = true;
                                    if (d.Action.SpeedMilli > 0) admitted = rt.Boss.Projectiles.Execute(host, rt, d.Set, d.Action, d.Center, nearest.position, d.Damage, d.Magic, now, 1);
                                    else rt.Boss.Shapes.Execute(host, rt, d.Action, nearest.position, nearest.position, d.Damage, d.Magic);
                                    if (!d.Active || rt.Boss.Build == null) return;
                                    if (admitted) d.Shots++;
                                }
                            }
                            finally { handle.Return(); }
                            d.Due = now + Math.Max(.1f, d.Action.IntervalMillis / 1000f);
                        }
                        finally { d.Processing = false; if (!d.Active) Release(i, d); }
                    }
                }
                finally { _ticking = false; }
            }
            private static void DestroyNative(Deployable d)
            {
                if (d.Native != null && d.Native.isActive && d.Native.hero == d.Owner && d.Native.creationTime == d.NativeCreation) d.Native.Destroy();
            }
            private void Remove(int index, Deployable d)
            {
                if (!d.Active) return;
                d.Active = false; bool processing = d.Processing; d.Processing = true;
                try { DestroyNative(d); }
                finally { d.Processing = processing; if (!processing) Release(index, d); }
            }
            private void Release(int index, Deployable d)
            {
                _owned[index] = null; d.Owner = null; d.Native = null; d.Action = null; d.Set = null; d.IsNative = false; d.Shots = 0; _pool.Return(d);
            }
            public void Clear()
            {
                for (int i = 0; i < _owned.Length; i++) if (_owned[i] != null) Remove(i, _owned[i]);
            }
        }

        // M7: retain only exact independent containers; pool reuse never grants ownership.
        internal sealed class BossDefenseExecutor
        {
            private sealed class Defense
            {
                private static int _next;
                public readonly string NativeId = "DreamforgeRPG.Boss.Container." + System.Threading.Interlocked.Increment(ref _next);
                public readonly UnstoppableEffect Unstoppable = new UnstoppableEffect();
                public readonly StunEffect Stun = new StunEffect();
                public readonly SlowEffect Slow = new SlowEffect();
                public readonly StatBonus Bonus = new StatBonus();
                public long Id, Life, TargetLife; public string Source;
                public float Until, LastDecay, DecayRate, Creation, BonusAmount;
                public StatusEffect Effect; public ShieldEffect Shield; public BasicEffect Basic; public Entity Target;
                public Stat BonusStat; public bool Active, Busy, BonusAttached;
            }
            private readonly Defense[] _slots = new Defense[64];
            public BossDefenseExecutor()
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var d = _slots[i] = new Defense();
                    BossBasicEffectReuse.Reset(d.Unstoppable); BossBasicEffectReuse.Reset(d.Stun); BossBasicEffectReuse.Reset(d.Slow);
                }
            }
            private static bool NativeBusy(BasicEffect basic)
                => basic.isAlive || basic.parent is Se_GenericEffectContainer container && container.isActive && ReferenceEquals(container.effect, basic);
            private Defense Rent(HeroRuntime rt, Entity target, string source, float until, float now)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var d = _slots[i];
                    if (d.Active || d.Busy || NativeBusy(d.Unstoppable) || NativeBusy(d.Stun) || NativeBusy(d.Slow)) continue;
                    BossBasicEffectReuse.Reset(d.Unstoppable); BossBasicEffectReuse.Reset(d.Stun); BossBasicEffectReuse.Reset(d.Slow);
                    d.Id = ++rt.Boss.NextId; d.Source = source; d.Target = target; d.TargetLife = NativeInstance.BossNativeActorLife(target);
                    d.Until = until; d.LastDecay = now; d.DecayRate = 0; d.Effect = null; d.Shield = null; d.Basic = null;
                    d.Active = d.Busy = true; return d;
                }
                return null;
            }
            public bool Execute(HostAuthority host, HeroRuntime rt, string source, BossAction a, float amount, float now, bool linearDecay = false)
            {
                if (a.Payload != BossPayload.Heal && a.Payload != BossPayload.Shield && a.Payload != BossPayload.Unstoppable && a.Payload != BossPayload.Modifier) return false;
                if (a.Payload == BossPayload.Modifier && (!a.ModifierStat.HasValue || !BossNativeStat(a.ModifierStat.Value))) return false;
                if (a.Payload != BossPayload.Heal && a.LifetimeMillis <= 0) return false;
                if (a.Payload == BossPayload.Heal) { host.BossHeal(rt, amount); return true; }
                // Reserve before replacing any existing source: failed admission leaves its exact container untouched.
                var d = Rent(rt, rt.Hero, source, now + a.LifetimeMillis / 1000f, now); if (d == null) return false;
                try
                {
                    for (int i = 0; i < _slots.Length; i++)
                        if (!ReferenceEquals(_slots[i], d) && _slots[i].Active && _slots[i].Source == source) Remove(host, rt, _slots[i], now);
                    if (!d.Active) return false;
                    host.EnterGenerated(rt.Hero);
                    try
                    {
                        if (a.Payload == BossPayload.Shield)
                        {
                            var shield = host._am?.serverActor?.GiveShield(rt.Hero, amount, a.LifetimeMillis / 1000f, false);
                            d.Effect = shield; d.Shield = shield?.shield;
                            if (linearDecay && d.Shield != null) d.DecayRate = Math.Max(0, d.Shield.amount) / (a.LifetimeMillis / 1000f);
                        }
                        else if (a.Payload == BossPayload.Unstoppable)
                        {
                            d.Basic = d.Unstoppable;
                            d.Effect = host._am?.serverActor?.CreateBasicEffect(rt.Hero, d.Basic, a.LifetimeMillis / 1000f, d.NativeId);
                        }
                        else
                        {
                            d.BonusStat = a.ModifierStat.Value; d.BonusAmount = a.ChannelId != null ? amount : a.MagnitudeMilli / 1000f;
                            AddNativeStat(d.Bonus, d.BonusStat, d.BonusAmount); d.BonusAttached = true;
                            d.Target.Status.AddStatBonus(d.Bonus); d.Target.Status.CalculateStatsIfDirty();
                        }
                    }
                    finally { host.ExitGenerated(rt.Hero); }
                    d.Creation = d.Effect != null ? d.Effect.creationTime : 0;
                    d.Life = d.Effect != null ? host.BossNativeActorLife(d.Effect) : 0;
                    if (!d.Active || d.Effect == null && !d.BonusAttached || !BossAlive(d.Target)) { Remove(host, rt, d, now); return false; }
                    host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, .6f, now, d.Until, targetNetId: rt.Hero.netId);
                    return true;
                }
                finally { try { if (!d.Active) Cleanup(host, rt, d); } finally { d.Busy = false; } }
            }
            public bool SourceActive(HostAuthority host, HeroRuntime rt, string source, float now)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var d = _slots[i];
                    if (d.Active && d.Source == source && now < d.Until && d.Shield != null && d.Shield.amount > 0 && Owned(host, d)) return true;
                }
                return false;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var d = _slots[i]; if (!d.Active || d.Busy) continue;
                    if (now >= d.Until || !Owned(host, d) || !BossAlive(d.Target)) { Remove(host, rt, d, now); continue; }
                    if (d.Shield != null && d.DecayRate > 0)
                    {
                        d.Shield.amount = Math.Max(0, d.Shield.amount - d.DecayRate * Math.Max(0, now - d.LastDecay)); d.LastDecay = now;
                    }
                }
            }
            private static bool Owned(HostAuthority host, Defense d)
                => d.BonusAttached && host.BossNativeSameLife(d.Target, d.TargetLife)
                    || d.Effect != null && d.Effect.isActive && d.Effect.victim == d.Target && d.Effect.creationTime == d.Creation
                        && host.BossNativeSameLife(d.Effect, d.Life) && host.BossNativeSameLife(d.Target, d.TargetLife)
                        && (d.Shield != null ? d.Effect is Se_GenericShield_OneShot shield && ReferenceEquals(shield.shield, d.Shield)
                            : d.Effect is Se_GenericEffectContainer container && container.id == d.NativeId && ReferenceEquals(container.effect, d.Basic)
                                && d.Basic != null && d.Basic.isAlive && d.Basic.parent == d.Effect);
            private static void Cleanup(HostAuthority host, HeroRuntime rt, Defense d)
            {
                if (d.BonusAttached)
                {
                    if (d.Target != null && host.BossNativeSameLife(d.Target, d.TargetLife))
                    { d.Target.Status.RemoveStatBonus(d.Bonus); d.Target.Status.CalculateStatsIfDirty(); }
                    AddNativeStat(d.Bonus, d.BonusStat, -d.BonusAmount); d.BonusAttached = false; d.BonusAmount = 0;
                }
                if (Owned(host, d)) d.Effect.Destroy();
                d.Effect = null; d.Shield = null; d.Basic = null; d.Source = null; d.Target = null;
            }
            private static void Remove(HostAuthority host, HeroRuntime rt, Defense d, float now)
            {
                bool active = d.Active; d.Active = false;
                if (active) host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, now, now, true);
                if (!d.Busy)
                {
                    d.Busy = true;
                    try { Cleanup(host, rt, d); }
                    finally { d.Busy = false; }
                }
            }
            public void Clear(HostAuthority host, HeroRuntime rt)
            {
                for (int i = 0; i < _slots.Length; i++) if (_slots[i].Active) Remove(host, rt, _slots[i], Time.time);
            }
            public void CancelSource(HostAuthority host, HeroRuntime rt, string source)
            {
                for (int i = 0; i < _slots.Length; i++) if (_slots[i].Active && _slots[i].Source == source) Remove(host, rt, _slots[i], Time.time);
            }
            public void Stun(HostAuthority host, HeroRuntime rt, Entity target, int durationMillis, float now)
                => CrowdControl(host, rt, target, Math.Min(250, durationMillis), 0, now);
            public void Slow(HostAuthority host, HeroRuntime rt, Entity target, int durationMillis, int percentage, float now)
            {
                if (percentage <= 0) return;
                CrowdControl(host, rt, target, Math.Min(500, durationMillis), Math.Min(20, percentage), now);
            }
            private void CrowdControl(HostAuthority host, HeroRuntime rt, Entity target, int durationMillis, int slowPct, float now)
            {
                if (durationMillis <= 0 || !BossAlive(target) || target == rt.Hero || target.IsAnyBoss()
                    || target.GetRelation(rt.Hero) != EntityRelation.Enemy || target.Status.hasCrowdControlImmunity) return;
                var d = Rent(rt, target, null, now + durationMillis / 1000f, now); if (d == null) return;
                try
                {
                    if (slowPct > 0) { d.Slow.strength = slowPct; d.Slow.decay = false; d.Basic = d.Slow; }
                    else d.Basic = d.Stun;
                    host.EnterGenerated(rt.Hero);
                    try { d.Effect = host._am?.serverActor?.CreateBasicEffect(target, d.Basic, d.Until - now, d.NativeId); }
                    finally { host.ExitGenerated(rt.Hero); }
                    d.Creation = d.Effect != null ? d.Effect.creationTime : 0; d.Life = d.Effect != null ? host.BossNativeActorLife(d.Effect) : 0;
                    if (!d.Active || d.Effect == null) Remove(host, rt, d, now);
                }
                finally { try { if (!d.Active) Cleanup(host, rt, d); } finally { d.Busy = false; } }
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

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

        // M1: all pulses share the native enemy filter and one normal damage dispatch.
        private sealed class BossShapeAttack
        {
            private readonly HashSet<int> _pulse = new HashSet<int>();
            public void Execute(HostAuthority host, HeroRuntime rt, BossAction action, Vector3 center, Vector3 end, float amount, bool magic, BossShape? pulseShape = null)
            {
                _pulse.Clear();
                var shape = pulseShape ?? action.Shape;
                float radius = action.RadiusMilli / 1000f;
                float range = action.RangeMilli / 1000f;
                bool line = shape == BossShape.Line || shape == BossShape.Column;
                var direction = BossDirection(center, end);
                if (direction == Vector3.zero) direction = rt.Hero.transform.forward;
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
                        host.BossDamage(rt, victim, amount, magic, action.Element);
                        if (action.Payload == BossPayload.Push || action.Payload == BossPayload.Pull)
                            rt.Boss.EnemyMovement.Execute(rt, victim, center, action.Payload == BossPayload.Pull, action.MagnitudeMilli / 1000f, Time.time);
                    }
                }
                finally { handle.Return(); }
            }
        }

        // M2: finite ballistic states, shared wave target gate, wall-first ordered collision.
        private sealed class BossProjectileExecutor
        {
            private sealed class Wave { public readonly HashSet<int> Hits = new HashSet<int>(); public int Limit; }
            private sealed class Shot
            {
                public long Id; public string Set; public Vector3 Point, Direction; public float Remaining, Speed, Radius, Damage, Last, Expires;
                public bool Magic, FirstHit; public Entity Target; public Wave Wave; public BossElement Element;
            }
            private readonly List<Shot> _shots = new List<Shot>(64);
            private readonly List<RaycastHit2D> _collisions = new List<RaycastHit2D>(64);
            public bool Execute(HostAuthority host, HeroRuntime rt, string set, BossAction a, Vector3 center, Vector3 end, float amount, bool magic, float now, int shotCount = 0)
            {
                int count = shotCount > 0 ? Math.Min(a.Count, shotCount) : a.Count;
                int owned = 0;
                foreach (var shot in _shots) if (shot.Set == set) owned++;
                if (owned + count > 64 || _shots.Count + count > 128) return false;
                var direction = BossDirection(center, end);
                if (direction == Vector3.zero) direction = rt.Hero.transform.forward;
                float speed = a.SpeedMilli > 0 ? a.SpeedMilli / 1000f : 12;
                float range = a.RangeMilli > 0 ? a.RangeMilli / 1000f : 8;
                var wave = new Wave { Limit = a.MaxTargets };
                for (int i = 0; i < count; i++)
                {
                    var dir = a.Shape == BossShape.Radial ? Quaternion.Euler(0, 360f * i / count, 0) * direction : direction;
                    var shot = new Shot { Id = ++rt.Boss.NextId, Set = set, Point = center, Direction = dir, Remaining = range, Speed = speed,
                        Radius = a.WidthMilli > 0 ? a.WidthMilli / 2000f : 0.2f, Damage = amount, Magic = magic, Last = now + a.DelayMillis / 1000f, Element = a.Element,
                        Expires = now + a.DelayMillis / 1000f + Math.Min(range / speed, a.LifetimeMillis > 0 ? a.LifetimeMillis / 1000f : range / speed), FirstHit = a.FirstHitOnly, Wave = wave,
                        Target = a.Anchor == BossAnchor.Marked ? rt.Boss.Ledger.NearestMark(center, now, a.LedgerId ?? set) : null };
                    _shots.Add(shot);
                    host.PublishBossVisual(rt, shot.Id, 5, center, center + dir * range, shot.Radius, shot.Last, shot.Expires);
                }
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = _shots.Count - 1; i >= 0; i--)
                {
                    var s = _shots[i];
                    if (now < s.Last) continue;
                    if (BossAlive(s.Target)) s.Direction = BossDirection(s.Point, s.Target.position);
                    float distance = Math.Min(s.Remaining, s.Speed * Math.Max(0, Math.Min(now, s.Expires) - s.Last)); s.Last = now;
                    bool ended = false;
                    if (distance > 0)
                    {
                        var filter = new ContactFilter2D { useLayerMask = true, layerMask = LayerMasks.Entity | LayerMasks.CollidableWithProjectile, useTriggers = true };
                        _collisions.Clear();
                        Physics2D.CircleCast(s.Point.ToXY(), s.Radius, s.Direction.ToXY(), filter, _collisions, distance);
                        float wall = distance + 1;
                        if (Physics.Raycast(s.Point, s.Direction, out var ground, distance, LayerMasks.Ground)) wall = ground.distance;
                        foreach (var hit in _collisions)
                            if (!DewPhysics.TryGetEntity(hit.collider, out _) && DewPhysics.TryGetCollidableWithProjectile(hit.collider, out _) && hit.distance < wall) wall = hit.distance;
                        ListReturnHandle<Entity> handle;
                        var entities = DewPhysics.SphereCastAllEntities(out handle, s.Point, s.Radius, s.Direction, distance, EnemyFilter, rt.Hero);
                        try
                        {
                            // Exact collider distances determine which enemy was encountered before a wall.
                            while (true)
                            {
                                Entity nearest = null; float first = wall;
                                foreach (var hit in _collisions)
                                {
                                    if (hit.distance > first || !DewPhysics.TryGetEntity(hit.collider, out var e) || !BossAlive(e)
                                        || e.GetRelation(rt.Hero) != EntityRelation.Enemy || !entities.Contains(e) || !s.FirstHit && s.Wave.Hits.Contains(e.GetInstanceID())) continue;
                                    nearest = e; first = hit.distance;
                                }
                                if (nearest == null) break;
                                if (s.FirstHit && s.Wave.Hits.Contains(nearest.GetInstanceID())) { ended = true; break; }
                                if (s.Wave.Hits.Count >= s.Wave.Limit) { ended = true; break; }
                                s.Wave.Hits.Add(nearest.GetInstanceID());
                                host.BossDamage(rt, nearest, s.Damage, s.Magic, s.Element);
                                if (s.FirstHit) { ended = true; break; }
                            }
                        }
                        finally { handle.Return(); }
                        // First-hit projectiles also disappear on a target already struck by this wave.
                        if (s.FirstHit)
                            foreach (var hit in _collisions)
                                if (hit.distance <= wall && DewPhysics.TryGetEntity(hit.collider, out var e) && BossAlive(e) && e.GetRelation(rt.Hero) == EntityRelation.Enemy && !e.Status.hasUncollidable) ended = true;
                        ended |= wall <= distance;
                        s.Point += s.Direction * Math.Min(distance, wall); s.Remaining -= distance;
                    }
                    if (!ended && s.Remaining > 0.0001f && now < s.Expires) continue;
                    host.PublishBossVisual(rt, s.Id, 5, s.Point, s.Point, s.Radius, now, now, true); _shots.RemoveAt(i);
                }
            }
            public void Clear() { _shots.Clear(); _collisions.Clear(); }
        }

        // M3: one reservation owns all finite pulses; buds are a stricter four-slot collection.
        private sealed class BossFieldExecutor
        {
            internal sealed class Field
            {
                public long Id; public string Set, Profile; public BossAction Action;
                public Vector3 Center, End; public Vector3[] Points; public float Due, Created, Damage; public int Pulse, PulseCount;
                public bool Magic, Bud, Collectible;
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
            public void Clear() { _fields.Clear(); }
        }

        // M4: no terrain traversal or immunity; this never emits a native movement event.
        private sealed class BossMovementExecutor
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
        private sealed class BossEnemyMovementExecutor
        {
            private readonly Entity[] _targets = new Entity[64];
            private readonly float[] _ready = new float[64];
            public bool Execute(HeroRuntime rt, Entity victim, Vector3 origin, bool pull, float distance, float now)
            {
                if (!BossAlive(victim) || victim == rt.Hero || victim.GetRelation(rt.Hero) != EntityRelation.Enemy || victim.IsAnyBoss()
                    || victim.Status.hasCrowdControlImmunity || victim.Status.hasUncollidable || victim.Control.isDisplacing) return false;
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
                _targets[slot] = victim; _ready[slot] = now + 1;
                victim.Control.StartDisplacement(new DispByDestination { destination = point, duration = 0.2f, isFriendly = false,
                    isDodging = false, canGoOverTerrain = false, isCanceledByCC = true });
                return true;
            }
            public void Clear() { Array.Clear(_targets, 0, _targets.Length); Array.Clear(_ready, 0, _ready.Length); }
        }

        // M6: host-only non-Entity shooters. Native summons must enter via the validated ownership API below.
        private sealed class BossDeployableExecutor
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
        private sealed class BossDefenseExecutor
        {
            private sealed class Defense
            {
                public long Id; public string Source, NativeId; public float Until; public StatusEffect Effect; public ShieldEffect Shield; public BasicEffect Basic; public float Creation;
                public StatBonus Bonus;
            }
            private readonly List<Defense> _active = new List<Defense>(16);
            public bool Execute(HostAuthority host, HeroRuntime rt, string source, BossAction a, float amount, float now)
            {
                if (a.Payload != BossPayload.Heal && a.Payload != BossPayload.Shield && a.Payload != BossPayload.Unstoppable && a.Payload != BossPayload.Modifier) return false;
                if (a.Payload == BossPayload.Modifier && (!a.ModifierStat.HasValue || !BossNativeStat(a.ModifierStat.Value))) return false;
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_active[i].Source == source)
                    {
                        var old = _active[i]; Remove(rt, old);
                        host.PublishBossVisual(rt, old.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, now, now, true);
                        _active.RemoveAt(i);
                    }
                if (_active.Count >= 64 || a.Payload != BossPayload.Heal && a.LifetimeMillis <= 0) return false;
                var d = new Defense { Id = ++rt.Boss.NextId, Source = source, Until = now + a.LifetimeMillis / 1000f };
                if (a.Payload == BossPayload.Heal)
                {
                    host.EnterGenerated(rt.Hero); try { rt.Hero.Heal(amount).Dispatch(rt.Hero); } finally { host.ExitGenerated(rt.Hero); } return true;
                }
                if (a.Payload == BossPayload.Shield)
                {
                    var shield = host._am?.serverActor?.GiveShield(rt.Hero, amount, a.LifetimeMillis / 1000f, false);
                    d.Effect = shield; d.Shield = shield?.shield;
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
                d.Creation = d.Effect != null ? d.Effect.creationTime : 0; _active.Add(d);
                host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0.6f, now, d.Until);
                return true;
            }
            public void Tick(HostAuthority host, HeroRuntime rt, float now)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (now >= _active[i].Until || !Owned(rt, _active[i]))
                    {
                        var d = _active[i]; Remove(rt, d); host.PublishBossVisual(rt, d.Id, 7, rt.Hero.agentPosition, rt.Hero.agentPosition, 0, now, now, true); _active.RemoveAt(i);
                    }
            }
            private static bool Owned(HeroRuntime rt, Defense d)
                => d.Bonus != null || d.Effect != null && d.Effect.isActive && d.Effect.victim == rt.Hero && d.Effect.creationTime == d.Creation
                    && (d.Shield != null ? d.Effect is Se_GenericShield_OneShot shield && shield.shield == d.Shield
                        : d.Effect is Se_GenericEffectContainer container && container.id == d.NativeId
                            && d.Basic != null && d.Basic.isAlive && d.Basic.parent == d.Effect);
            private static void Remove(HeroRuntime rt, Defense d)
            {
                if (d.Bonus != null)
                {
                    rt.Hero.Status.RemoveStatBonus(d.Bonus); d.Bonus = null;
                    rt.Hero.Status.CalculateStatsIfDirty();
                }
                if (Owned(rt, d)) d.Effect.Destroy();
            }
            public void Clear(HeroRuntime rt) { foreach (var d in _active) Remove(rt, d); _active.Clear(); }
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

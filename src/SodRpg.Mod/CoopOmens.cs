using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>Shared co-op objectives never have an individual enemy assignment.</summary>
    public static class CoopOmens
    {
        public static bool Enabled { get => HostAuthority.CoopOmensEnabled; set => HostAuthority.CoopOmensEnabled = value; }

        public static bool IsSharedObjective(Entity entity) => NetworkServer.active
            && HostAuthority.NativeInstance?.IsCoopOmenWard(entity) == true;
    }

    internal sealed partial class HostAuthority
    {
        // Reused for the whole host session; a room never assigns replacement wards after this batch.
        private sealed class CoopWard
        {
            internal Monster Monster;
            internal MonsterRuntime Runtime;
            internal uint NetId;
            internal DewPlayer LastHuman;
            internal float LastHit = float.NegativeInfinity, ResonantUntil;
            internal bool Hit, Assigned;

            internal void Clear()
            {
                Monster = null;
                Runtime = null;
                NetId = 0;
                Assigned = false;
                LastHuman = null;
                LastHit = float.NegativeInfinity;
                ResonantUntil = 0;
                Hit = false;
            }
        }

        private readonly CoopWard[] _coopWards = { new CoopWard(), new CoopWard(), new CoopWard() };
        private readonly MonsterRuntime[] _coopCandidates = new MonsterRuntime[3];
        private int _coopWardLimit;
        private float _coopFirstSpawn = float.PositiveInfinity, _coopLastSpawn = float.NegativeInfinity;
        private bool _coopBatchClosed, _coopWardFailed, _coopSyncDirty, _coopPreparingSpawns;
        private float _nextCoopSync;
        private const float InitialSpawnGap = 0.75f; // Native initial spawns are 0.1–0.5 seconds apart.

        partial void BeginCoopOmen(int humans)
        {
            _coopWardLimit = Math.Min(_coopWards.Length, humans);
            _coopWardFailed = false;
        }

        partial void ClearCoopOmen()
        {
            _coopWardLimit = 0;
            for (int i = 0; i < _coopWards.Length; i++)
            {
                var monster = _coopWards[i].Monster;
                _coopWards[i].Clear();
                _coopCandidates[i] = null;
                RefreshSharedObjectiveOwnership(monster);
            }
            _coopFirstSpawn = float.PositiveInfinity;
            _coopLastSpawn = float.NegativeInfinity;
            _coopBatchClosed = false;
            _coopSyncDirty = true;
        }

        private bool CoopWardRoomActive => !_coopWardFailed && CoopOmens.Enabled && RoomOmensEnabled
            && _roomOmen == RoomOmen.ResonantWard && _omenRoom != null && !_omenRoom.didClearRoom
            && ReferenceEquals(_zone?.currentRoom, _omenRoom) && !_zone.isInAnyTransition
            && ClientSession.HostRun?.RunId == _omenRunId;

        private bool LiveCoopWard(CoopWard ward) => ward.Monster != null && ward.NetId == ward.Monster.netId
            && _monsters.TryGetValue(ward.Monster, out var runtime) && ReferenceEquals(runtime, ward.Runtime)
            && EligibleCoopWard(ward.Monster);

        private static bool EligibleCoopWard(Monster monster)
        {
            if (monster == null || !monster.isActive || monster.isDead || monster.IsAnyBoss()
                || monster.Status == null || monster.Status.maxHealth <= 0 || monster.owner == null
                || monster.owner.isHumanPlayer) return false;
            var parent = monster.parentActor;
            for (int depth = 0; parent != null && depth < 64; depth++, parent = parent.parentActor)
                if (parent is Entity entity && entity.IsAnyBoss()) return false;
            // An invalid/cyclic parent chain cannot prove this is an ordinary enemy.
            return parent == null;
        }

        internal bool IsCoopOmenWard(Entity entity)
        {
            try
            {
                if (!CoopWardRoomActive || entity == null || HumanOmenPlayers() < 2) return false;
                for (int i = 0; i < _coopWardLimit; i++)
                    if (_coopWards[i].Monster == entity && LiveCoopWard(_coopWards[i])) return true;
            }
            catch (Exception ex) { DisableCoopWards(ex); }
            return false;
        }

        private bool StrongerCoopCandidate(MonsterRuntime candidate, MonsterRuntime other)
        {
            if (other == null) return true;
            bool strong = candidate.Variant != null || _nightmares.ContainsKey(candidate.Monster);
            bool otherStrong = other.Variant != null || _nightmares.ContainsKey(other.Monster);
            if (strong != otherStrong) return strong;
            float health = candidate.Monster.Status.maxHealth, otherHealth = other.Monster.Status.maxHealth;
            return health > otherHealth || health == otherHealth && candidate.Monster.netId < other.Monster.netId;
        }

        // Select from the first native spawn burst, after nightmare/variant processing. Until the
        // burst ends, untouched slots can upgrade to a stronger enemy; a hit ward is never swapped.
        partial void TickCoopOmen()
        {
            try
            {
                if (!CoopWardRoomActive) return;
                if (HumanOmenPlayers() < 2)
                {
                    // A departed partner must not leave a lone traveler behind a 90% ward.
                    // ClearCoopOmen zeroes _coopWardLimit and a ward room only begins with two
                    // or more humans (RollCoop never draws one for a solo party), so this runs
                    // exactly once per room; afterwards the message content cannot change again.
                    if (_coopWardLimit > 0)
                    {
                        ClearCoopOmen();
                        _coopBatchClosed = true;
                        SendRoomOmen();
                    }
                    return;
                }
                float now = Time.time;
                if (!_coopBatchClosed)
                {
                    for (int i = 0; i < _coopCandidates.Length; i++) _coopCandidates[i] = null;
                    bool pending = false;
                    foreach (var runtime in _monsters.Values)
                    {
                        var monster = runtime.Monster;
                        if (!EligibleCoopWard(monster)) continue;
                        if (runtime.QueuedAt < _coopFirstSpawn) _coopFirstSpawn = runtime.QueuedAt;
                    }
                    // Follow only the initial contiguous spawn group, never a later section/wave.
                    float last = Math.Max(_coopFirstSpawn, _coopLastSpawn);
                    bool extended;
                    do
                    {
                        extended = false;
                        foreach (var runtime in _monsters.Values)
                            if (EligibleCoopWard(runtime.Monster) && runtime.QueuedAt >= _coopFirstSpawn
                                && runtime.QueuedAt <= last + InitialSpawnGap && runtime.QueuedAt > last)
                            { last = runtime.QueuedAt; extended = true; }
                    } while (extended);
                    if (float.IsPositiveInfinity(last)) return;
                    _coopLastSpawn = last;
                    foreach (var runtime in _monsters.Values)
                    {
                        var monster = runtime.Monster;
                        if (!EligibleCoopWard(monster) || runtime.QueuedAt > _coopLastSpawn) continue;
                        if (!runtime.SpawnProcessed) { pending = true; continue; }
                        bool alreadyHit = false;
                        for (int i = 0; i < _coopWardLimit; i++)
                            if (_coopWards[i].Hit && _coopWards[i].Monster == monster) { alreadyHit = true; break; }
                        if (alreadyHit) continue;
                        for (int i = 0; i < _coopWardLimit; i++)
                        {
                            if (!StrongerCoopCandidate(runtime, _coopCandidates[i])) continue;
                            for (int j = _coopWardLimit - 1; j > i; j--) _coopCandidates[j] = _coopCandidates[j - 1];
                            _coopCandidates[i] = runtime;
                            break;
                        }
                    }
                    int candidate = 0;
                    for (int i = 0; i < _coopWardLimit; i++)
                    {
                        var ward = _coopWards[i];
                        if (ward.Hit) continue;
                        if (ward.Assigned && !LiveCoopWard(ward)) { ward.Hit = true; continue; }
                        var selected = _coopCandidates[candidate++];
                        var monster = selected?.Monster;
                        if (ward.Monster == monster && ReferenceEquals(ward.Runtime, selected)) continue;
                        var previous = ward.Monster;
                        ward.Clear();
                        ward.Monster = monster;
                        ward.Runtime = selected;
                        ward.NetId = monster != null ? monster.netId : 0;
                        ward.Assigned = monster != null;
                        RefreshSharedObjectiveOwnership(previous);
                        RefreshSharedObjectiveOwnership(monster);
                        _coopSyncDirty = true;
                    }
                    if (!float.IsPositiveInfinity(_coopFirstSpawn) && !pending && now - _coopLastSpawn > InitialSpawnGap)
                        _coopBatchClosed = true;
                }
                bool anyWardLive = false;
                for (int i = 0; i < _coopWardLimit; i++)
                {
                    var ward = _coopWards[i];
                    if (ward.Assigned && !LiveCoopWard(ward))
                    {
                        ward.Clear();
                        ward.Hit = true; // Retire the slot; death/pooling cannot assign another ward.
                        _coopSyncDirty = true;
                    }
                    if (ward.ResonantUntil > 0 && now >= ward.ResonantUntil)
                    {
                        ward.ResonantUntil = 0;
                        ward.LastHuman = null;
                        ward.LastHit = float.NegativeInfinity;
                        _coopSyncDirty = true;
                    }
                    if (ward.Monster != null) anyWardLive = true;
                }
                // Clients expire the ward snapshot after 4 seconds, so a live ward needs the 1 Hz
                // keepalive and countdown refresh. With no live ward left, the message is identical
                // to the client's own expired state and idle resends are skipped.
                if (_coopSyncDirty || anyWardLive && Time.unscaledTime >= _nextCoopSync)
                {
                    _coopSyncDirty = false;
                    _nextCoopSync = Time.unscaledTime + 1f;
                    SendRoomOmen();
                }
            }
            catch (Exception ex) { DisableCoopWards(ex); }
        }

        private static DewPlayer CoopDamageHuman(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 64; depth++, actor = actor.parentActor)
            {
                if (actor is Hero hero && hero.owner != null && hero.owner.isHumanPlayer) return hero.owner;
                if (actor is Summon summon && summon.hero != null && summon.hero.owner != null
                    && summon.hero.owner.isHumanPlayer) return summon.hero.owner;
                if (actor is Entity entity && entity.owner != null && entity.owner.isHumanPlayer) return entity.owner;
                if (actor is AbilityInstance ability && ability.info.caster != null
                    && ability.info.caster.owner != null && ability.info.caster.owner.isHumanPlayer) return ability.info.caster.owner;
                if (actor is SkillTrigger skill && skill.owner != null
                    && skill.owner.owner != null && skill.owner.owner.isHumanPlayer) return skill.owner.owner;
            }
            return null;
        }

        internal void ApplyCoopOmenDamage(Entity target, Actor actor, ref DamageData data)
        {
            if (!NetworkServer.active) return;
            try
            {
                if (!CoopWardRoomActive || data.currentAmount <= 0 || data.isBlockedByImmunity
                    || target == null || target.Status == null
                    || target.Status.hasDamageImmunity && !data.HasAttr(DamageAttribute.IgnoreDamageImmunity)) return;
                // An auto-cast can hit between the native spawn and the next host tick. Finish
                // that initial classification now so its first hit cannot bypass the ward.
                if (!_coopBatchClosed && !_coopPreparingSpawns)
                {
                    _coopPreparingSpawns = true;
                    try { ProcessSpawns(); TickCoopOmen(); }
                    finally { _coopPreparingSpawns = false; }
                    if (!CoopWardRoomActive) return;
                }
                for (int i = 0; i < _coopWardLimit; i++)
                {
                    var ward = _coopWards[i];
                    if (ward.Monster != target || !LiveCoopWard(ward)) continue;
                    if (HumanOmenPlayers() < 2) return;
                    float now = Time.time;
                    if (now >= ward.ResonantUntil)
                    {
                        if (ward.ResonantUntil > 0)
                        {
                            ward.ResonantUntil = 0;
                            ward.LastHuman = null;
                            ward.LastHit = float.NegativeInfinity;
                            _coopSyncDirty = true;
                        }
                        var human = CoopDamageHuman(actor);
                        if (human != null)
                        {
                            ward.Hit = true;
                            if (ward.LastHuman != null && ward.LastHuman != human
                                && now - ward.LastHit <= RoomOmenBalance.ResonanceWindow)
                            {
                                ward.ResonantUntil = now + (float)RoomOmenBalance.ResonanceDuration;
                                ward.LastHuman = null;
                                ward.LastHit = float.NegativeInfinity;
                                _coopSyncDirty = true;
                            }
                            else
                            {
                                ward.LastHuman = human;
                                ward.LastHit = now;
                            }
                        }
                    }
                    float multiplier = now < ward.ResonantUntil ? (float)RoomOmenBalance.ResonanceVulnerable
                        : (float)RoomOmenBalance.WardReduction;
                    // C07 normally runs in the DealDamage wrapper after ProcessReceivedDamage.
                    // Its existing marker makes that later call a no-op for this warded hit.
                    // Work on a copy so a co-op failure leaves the original damage untouched.
                    var adjusted = data;
                    ApplyAuthoredFinalNativeDamage(ref adjusted, actor, target);
                    // Reduction/amplification includes flat additions; raw multipliers do not.
                    adjusted = multiplier < 1 ? adjusted.ApplyReduction(1 - multiplier) : adjusted.ApplyAmplification(multiplier - 1);
                    data = adjusted;
                    return;
                }
            }
            catch (Exception ex) { DisableCoopWards(ex); }
        }

        partial void FillCoopOmenMessage(DreamforgeRoomOmenMsg msg)
        {
            if (!CoopWardRoomActive || HumanOmenPlayers() < 2) return;
            msg.coopVersion = 1;
            msg.wardCount = _coopWardLimit;
            float now = Time.time;
            for (int i = 0; i < _coopWardLimit; i++)
            {
                var ward = _coopWards[i];
                if (!LiveCoopWard(ward)) continue;
                msg.SetWard(i, ward.Monster.netId, Math.Max(0, ward.ResonantUntil - now));
            }
        }

        partial void PublishHostCoopOmen(DreamforgeRoomOmenMsg msg) => ClientSession.SetHostCoopOmen(msg);

        private void DisableCoopWards(Exception error)
        {
            if (_coopWardFailed) return;
            _coopWardFailed = true;
            ClearCoopOmen();
            try { SendRoomOmen(); } catch (Exception) { }
            Log.Warn("Co-op wards disabled for this room; combat continues normally: " + error.Message);
        }
    }
}

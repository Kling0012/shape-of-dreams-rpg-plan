using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>A host death fact, independent of transient monster tags and native object lifetime.</summary>
    public readonly struct AuthoritativeRunKill
    {
        public string RunId { get; }
        public string EventId { get; }
        public uint MonsterNetId { get; }
        public int ZoneIndex { get; }
        public NightmareAffix Nightmare { get; }
        public string VariantId { get; }

        public AuthoritativeRunKill(string runId, string eventId, uint monsterNetId, int zoneIndex,
            NightmareAffix nightmare, string variantId)
        {
            RunId = runId; EventId = eventId; MonsterNetId = monsterNetId; ZoneIndex = zoneIndex;
            VariantId = Variants.Get(variantId)?.Id;
            Nightmare = VariantId == null ? Nightmares.Sanitize((int)nightmare) : NightmareAffix.None;
        }
    }

    /// <summary>Only an eligible native death may consume a host fact and award local loot.</summary>
    public readonly struct PendingMonsterDeath
    {
        public uint MonsterNetId { get; }
        public PendingRunKill Kill { get; }

        public PendingMonsterDeath(uint monsterNetId, PendingRunKill kill)
        {
            MonsterNetId = monsterNetId; Kill = kill;
        }
    }

    public sealed class KillClassificationCheckpoint
    {
        public string RunId { get; set; }
        public List<PendingMonsterDeath> Deaths { get; } = new List<PendingMonsterDeath>();
        public List<AuthoritativeRunKill> Facts { get; } = new List<AuthoritativeRunKill>();
        public List<string> ResolvedEventIds { get; } = new List<string>();
        public List<uint> ExpiredMonsterNetIds { get; } = new List<uint>();

        public KillClassificationCheckpoint Clone()
        {
            var copy = new KillClassificationCheckpoint { RunId = RunId };
            copy.Deaths.AddRange(Deaths);
            copy.Facts.AddRange(Facts);
            copy.ResolvedEventIds.AddRange(ResolvedEventIds);
            copy.ExpiredMonsterNetIds.AddRange(ExpiredMonsterNetIds);
            return copy;
        }
    }

    /// <summary>
    /// Joins native eligibility with authoritative classification in either delivery order. Missing facts
    /// expire without rewards; facts and expired victim identities remain replay-safe across checkpoint restore.
    /// </summary>
    public sealed class KillClassificationLedger
    {
        public const double MissingFactTimeoutSeconds = 30;

        private readonly struct WaitingDeath
        {
            public PendingMonsterDeath Death { get; }
            public double Deadline { get; }

            public WaitingDeath(PendingMonsterDeath death, double now)
            {
                Death = death;
                Deadline = now + MissingFactTimeoutSeconds;
            }
        }

        private readonly Queue<WaitingDeath> _deaths = new Queue<WaitingDeath>();
        private readonly HashSet<uint> _observedVictims = new HashSet<uint>();
        private readonly Dictionary<uint, AuthoritativeRunKill> _facts = new Dictionary<uint, AuthoritativeRunKill>();
        private readonly HashSet<string> _eventIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _resolvedEventIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<uint> _expiredVictims = new HashSet<uint>();

        public string RunId { get; private set; }
        public int PendingCount => _deaths.Count;
        public IEnumerable<AuthoritativeRunKill> Facts => _facts.Values;

        public void BeginRun(string runId)
        {
            if (string.IsNullOrEmpty(runId) || RunId == runId) return;
            Clear();
            RunId = runId;
        }

        public bool ObserveDeath(PendingMonsterDeath death, double now = 0)
        {
            if (death.MonsterNetId == 0 || string.IsNullOrEmpty(death.Kill.RunId)) return false;
            BeginRun(death.Kill.RunId);
            if (!_observedVictims.Add(death.MonsterNetId)) return false;
            _deaths.Enqueue(new WaitingDeath(death, now));
            return true;
        }

        public bool ReceiveFact(AuthoritativeRunKill fact)
        {
            if (fact.MonsterNetId == 0 || string.IsNullOrEmpty(fact.RunId) || string.IsNullOrEmpty(fact.EventId)) return false;
            BeginRun(fact.RunId);
            if (_facts.ContainsKey(fact.MonsterNetId) || !_eventIds.Add(fact.EventId)) return false;
            _facts.Add(fact.MonsterNetId, fact);
            return true;
        }

        public bool TryResolve(out PendingRunKill kill, double now = 0)
        {
            kill = default;
            // Preserve native event order, including rewards waiting for preceding-zone rules.
            while (_deaths.Count > 0)
            {
                var waiting = _deaths.Peek();
                var death = waiting.Death;
                if (!_facts.TryGetValue(death.MonsterNetId, out var fact))
                {
                    if (now < waiting.Deadline) return false;
                    _deaths.Dequeue();
                    _expiredVictims.Add(death.MonsterNetId);
                    continue;
                }
                _deaths.Dequeue();
                if (!_resolvedEventIds.Add(fact.EventId)) continue;
                var native = death.Kill;
                kill = new PendingRunKill(native.RunId, native.ZoneIndex, native.RoomIndex, native.Tier,
                    native.Level, fact.Nightmare, fact.VariantId, native.HeroKey, fact.EventId, death.MonsterNetId,
                    native.Heat, native.Waypoint);
                return true;
            }
            return false;
        }

        public KillClassificationCheckpoint Capture()
        {
            var saved = new KillClassificationCheckpoint { RunId = RunId };
            foreach (var waiting in _deaths) saved.Deaths.Add(waiting.Death);
            saved.Facts.AddRange(_facts.Values);
            saved.ResolvedEventIds.AddRange(_resolvedEventIds);
            saved.ExpiredMonsterNetIds.AddRange(_expiredVictims);
            return saved;
        }

        public void Restore(KillClassificationCheckpoint saved, double now = 0)
        {
            Clear();
            if (saved == null || string.IsNullOrEmpty(saved.RunId)) return;
            RunId = saved.RunId;
            foreach (var fact in saved.Facts)
                if (fact.RunId == RunId) ReceiveFact(fact);
            foreach (string eventId in saved.ResolvedEventIds)
                if (!string.IsNullOrEmpty(eventId)) _resolvedEventIds.Add(eventId);
            foreach (uint monsterNetId in saved.ExpiredMonsterNetIds)
                if (monsterNetId != 0)
                {
                    _expiredVictims.Add(monsterNetId);
                    _observedVictims.Add(monsterNetId);
                }
            // Unscaled clocks do not survive reload. Pending deaths get a fresh bounded wait.
            foreach (var death in saved.Deaths)
                if (death.Kill.RunId == RunId) ObserveDeath(death, now);
        }

        public void Clear()
        {
            RunId = null;
            _deaths.Clear();
            _observedVictims.Clear();
            _facts.Clear();
            _eventIds.Clear();
            _resolvedEventIds.Clear();
            _expiredVictims.Clear();
        }
    }
}

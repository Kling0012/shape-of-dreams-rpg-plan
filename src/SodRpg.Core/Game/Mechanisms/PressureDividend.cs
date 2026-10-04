using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>C13 authored probabilities and modifiers use hundredths of one percent.</summary>
    public sealed class PressureDividendContribution
    {
        public string ContributorId { get; }
        public string SourceMemory { get; }
        public int ProbabilityUnits { get; }
        public IReadOnlyList<string> RequiredMemories { get; }

        public PressureDividendContribution(string contributorId, string sourceMemory, int probabilityUnits,
            IEnumerable<string> requiredMemories = null)
        {
            PressureDividendChannel.RequireToken(contributorId, nameof(contributorId));
            PressureDividendChannel.RequireToken(sourceMemory, nameof(sourceMemory));
            if (!Links.IsMemory(sourceMemory) || sourceMemory.StartsWith("St_M_", StringComparison.Ordinal))
                throw new ArgumentException("Pressure dividend sources must be registered non-movement memories.", nameof(sourceMemory));
            if (probabilityUnits <= 0 || probabilityUnits > 10000) throw new ArgumentOutOfRangeException(nameof(probabilityUnits));
            ContributorId = contributorId;
            SourceMemory = sourceMemory;
            ProbabilityUnits = probabilityUnits;
            var required = (requiredMemories ?? Array.Empty<string>()).ToArray();
            foreach (var memory in required)
                if (!Links.IsMemory(memory)) throw new ArgumentException("Unknown required memory.", nameof(requiredMemories));
            RequiredMemories = Array.AsReadOnly(required.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }

    public sealed class PressureDividendChannel
    {
        public const int MaximumProbabilityUnits = 4000;
        public const double MinimumAppliedHpMultiplier = 1.25;
        public string SourceMemory { get; }
        public IReadOnlyList<string> RequiredMemories { get; }
        public IReadOnlyList<string> ContributorIds { get; }
        public decimal ProbabilityUnits { get; }
        public string ConditionKey { get; }

        /// <summary>One equivalent condition is composed before its one GB multiplier and Chance addition.</summary>
        public PressureDividendChannel(IEnumerable<PressureDividendContribution> contributions,
            int boostModifierUnits = 0, int chanceProbabilityUnits = 0)
        {
            if (contributions == null) throw new ArgumentNullException(nameof(contributions));
            if (boostModifierUnits < -10000) throw new ArgumentOutOfRangeException(nameof(boostModifierUnits));
            if (chanceProbabilityUnits < 0) throw new ArgumentOutOfRangeException(nameof(chanceProbabilityUnits));
            var values = contributions.ToArray();
            if (values.Length == 0 || values.Any(x => x == null)) throw new ArgumentException("A channel requires its authored contributions.", nameof(contributions));
            SourceMemory = values[0].SourceMemory;
            RequiredMemories = values[0].RequiredMemories;
            ConditionKey = SourceMemory + ":" + string.Join("+", RequiredMemories);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long sum = 0;
            foreach (var value in values)
            {
                if (value.SourceMemory != SourceMemory || !value.RequiredMemories.SequenceEqual(RequiredMemories))
                    throw new ArgumentException("Only equivalent pressure dividend conditions may be added.", nameof(contributions));
                if (!ids.Add(value.ContributorId)) throw new ArgumentException("Duplicate pressure dividend contributor.", nameof(contributions));
                sum = checked(sum + value.ProbabilityUnits);
            }
            decimal modified = (decimal)sum * (10000m + boostModifierUnits) / 10000m + chanceProbabilityUnits;
            modified = Math.Min(MaximumProbabilityUnits, Math.Max(0m, modified));
            ProbabilityUnits = modified;
            ContributorIds = Array.AsReadOnly(ids.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
        private PressureDividendChannel(string source, IReadOnlyList<string> required, IReadOnlyList<string> contributors, decimal probability)
        {
            SourceMemory = source; RequiredMemories = required; ContributorIds = contributors;
            ProbabilityUnits = probability; ConditionKey = source + ":" + string.Join("+", required);
        }
        public static PressureDividendChannel FromEffective(string source, IEnumerable<string> required, IEnumerable<string> contributors, decimal probability)
        {
            if (!Links.IsMemory(source) || source.StartsWith("St_M_", StringComparison.Ordinal) || probability < 0 || probability > MaximumProbabilityUnits
                || required == null || contributors == null) throw new ArgumentException("Invalid effective dividend.");
            var memories = required.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var ids = contributors.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (memories.Any(x => !Links.IsMemory(x)) || memories.Distinct(StringComparer.Ordinal).Count() != memories.Length
                || ids.Length == 0 || ids.Any(x => !Gimmicks.ValidStarId(x)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new ArgumentException("Invalid dividend identities.");
            return new PressureDividendChannel(source, Array.AsReadOnly(memories), Array.AsReadOnly(ids), probability);
        }

        public bool Matches(string sourceMemory, ISet<string> equippedMemories) => SourceMemory == sourceMemory
            && equippedMemories != null && equippedMemories.Contains(SourceMemory) && RequiredMemories.All(equippedMemories.Contains);

        public string Describe()
        {
            string chance = (ProbabilityUnits / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            return Loc.T($"夢の圧で最大HPが25%以上増えた敵を指定の記憶で倒すと、{chance}%の確率（上限40%）で未確保の欠片を1個獲得します。同じ敵から得られるのは1人につき最大1個です。",
                $"Defeating enemies with the specified memory when dream pressure increased their maximum HP by at least 25% has a {chance}% chance (maximum 40%) to grant 1 unsecured shard, at most 1 per enemy for each owner.");
        }

        internal static void RequireToken(string token, string parameter)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 96 || token.Any(c => !(c >= 'a' && c <= 'z')
                && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.'))
                throw new ArgumentException("Expected a nonempty wire-safe identity of at most 96 characters.", parameter);
        }
    }

    /// <summary>A fresh object belongs to one spawn lifetime. Only an applied native stat calculation updates it.</summary>
    public sealed class PressureDividendEnemy
    {
        public string RunId { get; }
        public int ZoneId { get; }
        public long SpawnId { get; }
        public long AppliedVersion { get; private set; }
        public double AppliedHpMultiplier { get; private set; }
        public PressureDividendDeath Death { get; private set; }

        public PressureDividendEnemy(string runId, int zoneId, long spawnId)
        {
            PressureDividendChannel.RequireToken(runId, nameof(runId));
            if (zoneId < 0) throw new ArgumentOutOfRangeException(nameof(zoneId));
            if (spawnId <= 0) throw new ArgumentOutOfRangeException(nameof(spawnId));
            RunId = runId; ZoneId = zoneId; SpawnId = spawnId;
        }

        public void RecordAppliedHpMultiplier(double multiplier)
        {
            if (Death != null) throw new InvalidOperationException("A dead spawn's applied pressure is immutable.");
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            AppliedVersion = checked(AppliedVersion + 1);
            AppliedHpMultiplier = multiplier;
        }

        public PressureDividendDeath CaptureDeath(bool rewardEligible)
        {
            if (Death == null) Death = new PressureDividendDeath(this, rewardEligible && AppliedVersion > 0);
            return Death;
        }
    }

    public sealed class PressureDividendDeath
    {
        public string RunId { get; }
        public int ZoneId { get; }
        public long SpawnId { get; }
        public long AppliedVersion { get; }
        public double AppliedHpMultiplier { get; }
        public bool RewardEligible { get; }
        internal PressureDividendDeath(PressureDividendEnemy enemy, bool eligible)
        {
            RunId = enemy.RunId; ZoneId = enemy.ZoneId; SpawnId = enemy.SpawnId;
            AppliedVersion = enemy.AppliedVersion; AppliedHpMultiplier = enemy.AppliedHpMultiplier;
            RewardEligible = eligible;
        }
    }

    public enum PressureDividendKillOrigin { Unknown, NativeMemory, Generated }
    public enum PressureDividendVictimKind { Unknown, NativeLootEnemy, EnemySummon }

    /// <summary>C02 supplies this admission; the host must never construct it from a nearby owner or guessed source.</summary>
    public sealed class PressureDividendAttribution
    {
        public string OwnerId { get; }
        public string SourceMemory { get; }
        public PressureDividendKillOrigin Origin { get; }
        public PressureDividendVictimKind VictimKind { get; }
        public PressureDividendAttribution(string ownerId, string sourceMemory, PressureDividendKillOrigin origin,
            PressureDividendVictimKind victimKind)
        {
            PressureDividendChannel.RequireToken(ownerId, nameof(ownerId));
            if (sourceMemory != null) PressureDividendChannel.RequireToken(sourceMemory, nameof(sourceMemory));
            if (!Enum.IsDefined(typeof(PressureDividendKillOrigin), origin)) throw new ArgumentOutOfRangeException(nameof(origin));
            if (!Enum.IsDefined(typeof(PressureDividendVictimKind), victimKind)) throw new ArgumentOutOfRangeException(nameof(victimKind));
            if (origin == PressureDividendKillOrigin.NativeMemory && sourceMemory == null) throw new ArgumentNullException(nameof(sourceMemory));
            OwnerId = ownerId; SourceMemory = sourceMemory; Origin = origin; VictimKind = victimKind;
        }
    }

    public sealed class PressureDividendReward
    {
        public string RunId { get; }
        public int ZoneId { get; }
        public long SpawnId { get; }
        public string OwnerId { get; }
        public string RewardNonce { get; }
        public int ShardCount => 1;
        internal string DeathOwnerKey => RunId + ":" + ZoneId.ToString(CultureInfo.InvariantCulture) + ":"
            + SpawnId.ToString(CultureInfo.InvariantCulture) + ":" + OwnerId;

        public PressureDividendReward(string runId, int zoneId, long spawnId, string ownerId, string rewardNonce)
        {
            PressureDividendChannel.RequireToken(runId, nameof(runId));
            PressureDividendChannel.RequireToken(ownerId, nameof(ownerId));
            PressureDividendChannel.RequireToken(rewardNonce, nameof(rewardNonce));
            if (zoneId < 0) throw new ArgumentOutOfRangeException(nameof(zoneId));
            if (spawnId <= 0) throw new ArgumentOutOfRangeException(nameof(spawnId));
            RunId = runId; ZoneId = zoneId; SpawnId = spawnId; OwnerId = ownerId; RewardNonce = rewardNonce;
        }
    }

    public sealed class PressureDividendRuntime
    {
        private readonly HashSet<string> _rolled = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>One host roll for one run/zone/spawn/owner. Build retransmission never resets this ledger.</summary>
        public PressureDividendReward TryAward(PressureDividendDeath death, PressureDividendAttribution attribution,
            IReadOnlyList<PressureDividendChannel> channels, ISet<string> equippedMemories,
            Func<decimal> rollUnits, Func<string> createNonce)
        {
            if (death == null || attribution == null || channels == null || rollUnits == null || createNonce == null)
                throw new ArgumentNullException("Pressure dividend admission requires complete facts and host services.");
            if (attribution.Origin != PressureDividendKillOrigin.NativeMemory
                || attribution.VictimKind != PressureDividendVictimKind.NativeLootEnemy || !death.RewardEligible
                || death.AppliedVersion <= 0 || death.AppliedHpMultiplier < PressureDividendChannel.MinimumAppliedHpMultiplier) return null;
            PressureDividendChannel selected = null;
            var conditions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in channels)
            {
                if (channel == null) throw new ArgumentException("A pressure dividend channel cannot be null.", nameof(channels));
                if (!conditions.Add(channel.ConditionKey)) throw new ArgumentException("Equivalent channels must be composed before admission.", nameof(channels));
                if (channel.ProbabilityUnits <= 0 || !channel.Matches(attribution.SourceMemory, equippedMemories)) continue;
                if (selected != null) throw new InvalidOperationException("Multiple non-equivalent pressure dividend conditions match one death; their composition requires an explicit design decision.");
                selected = channel;
            }
            if (selected == null) return null;
            string key = death.RunId + ":" + death.ZoneId.ToString(CultureInfo.InvariantCulture) + ":"
                + death.SpawnId.ToString(CultureInfo.InvariantCulture) + ":" + attribution.OwnerId;
            if (!_rolled.Add(key)) return null;
            decimal roll = rollUnits();
            if (roll < 0 || roll >= 10000) throw new InvalidOperationException("Host probability roll must be in [0, 10000).");
            if (roll >= selected.ProbabilityUnits) return null;
            return new PressureDividendReward(death.RunId, death.ZoneId, death.SpawnId, attribution.OwnerId, createNonce());
        }
    }
}

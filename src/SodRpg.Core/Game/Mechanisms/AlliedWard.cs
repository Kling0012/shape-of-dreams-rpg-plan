using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum WardRecipientKind { AlliedTravelers, OwnedSummons }
    public enum WardAmountBasis { CasterMaxOffense, RecipientMaxHP }
    public enum WardLimitProfile { Standard, SummonRecipientHealth }
    public enum WardActivationBudget { PerActivation, PerActivationVictim }

    /// <summary>Effective payload, after the one GB/parameter/keystone layer. Percent integers are hundredths of one percent.</summary>
    public sealed class AlliedWardDefinition
    {
        public string ChannelId { get; }
        public WardRecipientKind RecipientKind { get; }
        public WardAmountBasis AmountBasis { get; }
        public ModShieldPoolKind PoolKind { get; }
        public WardLimitProfile Limits { get; }
        public decimal ValueUnits { get; }
        public bool IncludeOwner { get; }
        public float RadiusMetres { get; }
        public float DurationSeconds { get; }
        public int BaseTargets { get; }
        public int MaxTargets { get; }
        public int Targets { get; }
        public WardActivationBudget Budget { get; }

        public AlliedWardDefinition(string channelId, WardRecipientKind recipientKind, WardAmountBasis amountBasis,
            ModShieldPoolKind poolKind, decimal valueUnits, bool includeOwner, float radiusMetres = 10f,
            float durationSeconds = 4f, int baseTargets = 3, int extraTargets = 0, int? maxTargets = null,
            WardLimitProfile limits = WardLimitProfile.Standard, WardActivationBudget budget = WardActivationBudget.PerActivation)
        {
            if (string.IsNullOrWhiteSpace(channelId) || !Enum.IsDefined(typeof(WardRecipientKind), recipientKind)
                || !Enum.IsDefined(typeof(WardAmountBasis), amountBasis) || !Enum.IsDefined(typeof(WardLimitProfile), limits)
                || !Enum.IsDefined(typeof(WardActivationBudget), budget)) throw new ArgumentException("Invalid ward channel or selector.");
            if (poolKind != ModShieldPoolKind.Allied && poolKind != ModShieldPoolKind.Ordinary)
                throw new ArgumentException("Ward requires an explicitly authored Allied or Ordinary pool.");
            if (recipientKind == WardRecipientKind.OwnedSummons && includeOwner)
                throw new ArgumentException("Owned-summon selection cannot include a traveler.");
            if (limits == WardLimitProfile.SummonRecipientHealth && (recipientKind != WardRecipientKind.OwnedSummons
                || amountBasis != WardAmountBasis.RecipientMaxHP || poolKind != ModShieldPoolKind.Allied || baseTargets != 1))
                throw new ArgumentException("Summon recipient-health wards require their declared selector, basis, pool and baseline.");
            float durationCap = limits == WardLimitProfile.SummonRecipientHealth ? 9f : 8f;
            int targetCap = limits == WardLimitProfile.SummonRecipientHealth ? 3 : recipientKind == WardRecipientKind.OwnedSummons ? 5 : 4;
            int targetMaximum = maxTargets ?? targetCap;
            if (valueUnits <= 0 || valueUnits > 10000 || !FinitePositive(radiusMetres) || radiusMetres > 15f
                || !FinitePositive(durationSeconds) || durationSeconds > durationCap || baseTargets < 1
                || targetMaximum < baseTargets || targetMaximum > targetCap || extraTargets < 0)
                throw new ArgumentOutOfRangeException(nameof(valueUnits), "Effective ward exceeds its explicit profile limits.");
            ChannelId = channelId; RecipientKind = recipientKind; AmountBasis = amountBasis; PoolKind = poolKind;
            ValueUnits = valueUnits; IncludeOwner = includeOwner; RadiusMetres = radiusMetres; DurationSeconds = durationSeconds;
            BaseTargets = baseTargets; MaxTargets = targetMaximum; Targets = (int)Math.Min(targetMaximum, (long)baseTargets + extraTargets);
            Limits = limits; Budget = budget;
        }
        private static bool FinitePositive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct WardCandidate
    {
        public readonly long InstanceId, SummonOwnerId;
        public readonly bool IsTraveler, IsSummon, IsActive, IsAllied;
        public readonly float Health, MaxHealth, DistanceSquared;
        public WardCandidate(long instanceId, long summonOwnerId, bool traveler, bool summon, bool active, bool allied,
            float health, float maxHealth, float distanceSquared)
        {
            if (instanceId == 0 || float.IsNaN(health) || float.IsInfinity(health)
                || maxHealth < 0 || float.IsNaN(maxHealth) || float.IsInfinity(maxHealth)
                || distanceSquared < 0 || float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared))
                throw new ArgumentException("Invalid ward candidate facts.");
            InstanceId = instanceId; SummonOwnerId = summonOwnerId; IsTraveler = traveler; IsSummon = summon;
            IsActive = active; IsAllied = allied; Health = health; MaxHealth = maxHealth; DistanceSquared = distanceSquared;
        }
    }
    public readonly struct WardAward
    {
        public readonly long RecipientId;
        public readonly float RawAmount;
        public WardAward(long recipientId, float rawAmount) { RecipientId = recipientId; RawAmount = rawAmount; }
    }

    public static class AlliedWard
    {
        public static IReadOnlyList<WardAward> Select(AlliedWardDefinition definition, long ownerId, bool ownerAlive,
            float casterAttackDamage, float casterAbilityPower, IEnumerable<WardCandidate> candidates)
        {
            if (definition == null || candidates == null) throw new ArgumentNullException(nameof(definition));
            if (ownerId == 0 || float.IsNaN(casterAttackDamage) || float.IsInfinity(casterAttackDamage)
                || float.IsNaN(casterAbilityPower) || float.IsInfinity(casterAbilityPower)) throw new ArgumentException("Invalid ward caster.");
            var eligible = new List<WardCandidate>();
            var ids = new HashSet<long>();
            foreach (var candidate in candidates)
            {
                if (!ids.Add(candidate.InstanceId)) throw new ArgumentException("Duplicate ward candidate identity.");
                if (!ownerAlive || !candidate.IsActive || candidate.Health <= 0 || candidate.MaxHealth <= 0
                    || candidate.DistanceSquared > definition.RadiusMetres * definition.RadiusMetres) continue;
                bool qualifies = definition.RecipientKind == WardRecipientKind.AlliedTravelers
                    ? candidate.IsTraveler && (candidate.InstanceId == ownerId ? definition.IncludeOwner : candidate.IsAllied)
                    : candidate.IsSummon && candidate.SummonOwnerId == ownerId && candidate.IsAllied;
                if (qualifies) eligible.Add(candidate);
            }
            eligible.Sort((a, b) =>
            {
                int health = ((double)a.Health / a.MaxHealth).CompareTo((double)b.Health / b.MaxHealth);
                if (health != 0) return health;
                int distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
                return distance != 0 ? distance : a.InstanceId.CompareTo(b.InstanceId);
            });
            var awards = new List<WardAward>();
            for (int i = 0; i < eligible.Count && i < definition.Targets; i++)
            {
                var recipient = eligible[i];
                float basis = definition.AmountBasis == WardAmountBasis.RecipientMaxHP ? recipient.MaxHealth
                    : Math.Max(0f, Math.Max(casterAttackDamage, casterAbilityPower));
                awards.Add(new WardAward(recipient.InstanceId, basis * (float)(definition.ValueUnits / 10000m)));
            }
            return awards.AsReadOnly();
        }
    }

    /// <summary>C02 supplies the real activation serial. Call CompleteActivation/Reset on lifecycle boundaries.</summary>
    public sealed class AlliedWardRuntime
    {
        private readonly HashSet<(long Owner, string Channel, long Epoch, long Activation, long Victim)> _admitted =
            new HashSet<(long, string, long, long, long)>();
        public bool TryAdmit(AlliedWardDefinition definition, long ownerId, long epoch, long activationId, long victimId, bool conditionSucceeded)
        {
            if (definition == null || ownerId == 0 || activationId <= 0 || epoch < 0
                || definition.Budget == WardActivationBudget.PerActivationVictim && victimId == 0) throw new ArgumentException("Invalid ward activation identity.");
            if (!conditionSucceeded) return false;
            return _admitted.Add((ownerId, definition.ChannelId, epoch, activationId,
                definition.Budget == WardActivationBudget.PerActivationVictim ? victimId : 0));
        }
        public void CompleteActivation(long ownerId, long epoch, long activationId) =>
            _admitted.RemoveWhere(x => x.Owner == ownerId && x.Epoch == epoch && x.Activation == activationId);
        public void Reset(long ownerId) => _admitted.RemoveWhere(x => x.Owner == ownerId);
        public void Clear() => _admitted.Clear();
    }
}

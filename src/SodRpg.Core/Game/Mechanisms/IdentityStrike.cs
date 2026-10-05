using System;

namespace SodRpg.Core.Game
{
    public enum IdentityStrikeTrigger
    {
        /// <summary>After a self displacement (dash/teleport) the next own basic attack hit fires one strike; one strike per displacement.</summary>
        AfterDisplacementNextBasicHit,
        /// <summary>Every Nth own basic attack that hits fires one strike.</summary>
        EveryNthBasicAttack,
        /// <summary>No extra damage: the native dash attack's own bonus portion is re-attributed to the identity memory (see DashBonusAsMemory).</summary>
        DashAttackBonusAsMemory,
        AfterDisplacementCritical,
        ConsecutiveCritical
    }
    public enum IdentityStrikeElement { None, Fire, Cold, Light, Dark }
    public enum IdentityStrikeShape { None, ForwardLine, ForwardArc }
    /// <summary>What the damage percentage multiplies: attack damage only, or the higher of attack damage / ability power (the mod's existing convention).</summary>
    public enum IdentityStrikeBasis { AttackDamage, HigherOfAttackAndAbility }

    /// <summary>
    /// Typed payload of AuthoredMechanismKind.IdentityStrike. The damage it describes is dealt by the equipped identity memory itself
    /// (the SkillTrigger is the damage Actor), so native growth essences socketed in that memory (KillTracker, dealtDamageProcessor) see it.
    /// Units are basis points of attack damage (6000 = 60%).
    /// </summary>
    public sealed class IdentityStrikeDefinition
    {
        public const int MaxAdUnits = 20000, MaxBonusSpeedUnits = 1000;
        public const float MaxRangeMetres = 15f, MaxWindowSeconds = 10f;
        /// <summary>St_D_TheKillingFlow's default conversion: attack damage gained per 1% of bonus attack speed (its description says 0.5).</summary>
        public const float KillingFlowAdPerBonusSpeedPercent = 0.5f;
        public const string WindScar = "St_D_ScarOfTheWind", KillingFlow = "St_D_TheKillingFlow";
        public const float CriticalCooldownSeconds = 1f;
        public const float CriticalMovementRefundPercent = 35f;

        public string ChannelId { get; }
        public string Identity { get; }
        public IdentityStrikeTrigger Trigger { get; }
        public int EveryN { get; }
        public float WindowSeconds { get; }
        public int AdUnits { get; }
        /// <summary>Extra basis points of attack damage per 1% of bonus attack speed (only the Killing Flow identity can supply it).</summary>
        public int BonusSpeedUnitsPerPercent { get; }
        public IdentityStrikeElement Element { get; }
        public IdentityStrikeShape Shape { get; }
        public IdentityStrikeBasis Basis { get; }
        public float RangeMetres { get; }
        /// <summary>Full width of a line, or total angle in degrees of an arc.</summary>
        public float WidthOrArc { get; }
        public int MaxTargets { get; }

        public bool IsCriticalMechanism => Trigger == IdentityStrikeTrigger.AfterDisplacementCritical || Trigger == IdentityStrikeTrigger.ConsecutiveCritical;
        public IdentityStrikeDefinition(string channelId, string identity, IdentityStrikeTrigger trigger, int everyN, float windowSeconds,
            int adUnits, int bonusSpeedUnitsPerPercent, IdentityStrikeElement element, IdentityStrikeShape shape, float rangeMetres,
            float widthOrArc, int maxTargets, IdentityStrikeBasis basis = IdentityStrikeBasis.HigherOfAttackAndAbility)
        {
            Basis = basis;
            ChannelId = channelId; Identity = identity; Trigger = trigger; EveryN = everyN; WindowSeconds = windowSeconds; AdUnits = adUnits;
            BonusSpeedUnitsPerPercent = bonusSpeedUnitsPerPercent; Element = element; Shape = shape; RangeMetres = rangeMetres;
            WidthOrArc = widthOrArc; MaxTargets = maxTargets;
            Validate();
        }

        /// <summary>Next own basic attack hit after a self displacement: AD% strike, once per displacement (Wind Scar).</summary>
        public static IdentityStrikeDefinition AfterDisplacement(string channelId, string identity, int adUnits, IdentityStrikeElement element,
            IdentityStrikeShape shape, float rangeMetres, float widthOrArc, int maxTargets = 8, float windowSeconds = 4f,
            IdentityStrikeBasis basis = IdentityStrikeBasis.HigherOfAttackAndAbility) =>
            new IdentityStrikeDefinition(channelId, identity, IdentityStrikeTrigger.AfterDisplacementNextBasicHit, 1, windowSeconds, adUnits, 0,
                element, shape, rangeMetres, widthOrArc, maxTargets, basis);

        /// <summary>Every Nth (N = 1: every) own basic attack that hits: damage% (+ per converted bonus attack speed %) strike (Killing Flow).</summary>
        public static IdentityStrikeDefinition EveryNth(string channelId, string identity, int everyN, int adUnits, int bonusSpeedUnitsPerPercent,
            IdentityStrikeElement element, IdentityStrikeShape shape, float rangeMetres, float widthOrArc, int maxTargets = 8,
            IdentityStrikeBasis basis = IdentityStrikeBasis.HigherOfAttackAndAbility) =>
            new IdentityStrikeDefinition(channelId, identity, IdentityStrikeTrigger.EveryNthBasicAttack, everyN, 0f, adUnits, bonusSpeedUnitsPerPercent,
                element, shape, rangeMetres, widthOrArc, maxTargets, basis);

        /// <summary>Re-attributes only the native dash attack's bonus portion (75% dark) to the Wind Scar memory; adds no damage of its own.</summary>
        public static IdentityStrikeDefinition DashBonusAsMemory(string channelId) =>
            new IdentityStrikeDefinition(channelId, WindScar, IdentityStrikeTrigger.DashAttackBonusAsMemory, 1, 0f, 0, 0,
                IdentityStrikeElement.None, IdentityStrikeShape.None, 0f, 0f, 0);

        public static IdentityStrikeDefinition CriticalAfterDisplacement(string channelId, string identity, int adUnits, IdentityStrikeElement element,
            IdentityStrikeShape shape, float rangeMetres, float widthOrArc, int maxTargets = 6, float windowSeconds = 3f) =>
            new IdentityStrikeDefinition(channelId, identity, IdentityStrikeTrigger.AfterDisplacementCritical, 1, windowSeconds, adUnits, 0,
                element, shape, rangeMetres, widthOrArc, maxTargets);

        public static IdentityStrikeDefinition ConsecutiveCritical(string channelId, string identity, int adUnits, IdentityStrikeElement element,
            IdentityStrikeShape shape, float rangeMetres, float widthOrArc, int maxTargets = 6, float windowSeconds = 4f) =>
            new IdentityStrikeDefinition(channelId, identity, IdentityStrikeTrigger.ConsecutiveCritical, 3, windowSeconds, adUnits, 0,
                element, shape, rangeMetres, widthOrArc, maxTargets);

        public bool DealsDamage => Trigger != IdentityStrikeTrigger.DashAttackBonusAsMemory;

        private void Validate()
        {
            if (!Gimmicks.ValidStarId(ChannelId) || !Links.IsMemory(Identity) || Identity.StartsWith("St_M_", StringComparison.Ordinal)
                || !Enum.IsDefined(typeof(IdentityStrikeTrigger), Trigger) || !Enum.IsDefined(typeof(IdentityStrikeElement), Element)
                || !Enum.IsDefined(typeof(IdentityStrikeShape), Shape) || !Enum.IsDefined(typeof(IdentityStrikeBasis), Basis))
                throw new InvalidOperationException("Invalid identity strike.");
            if (Trigger == IdentityStrikeTrigger.DashAttackBonusAsMemory)
            {
                if (Identity != WindScar || EveryN != 1 || WindowSeconds != 0f || AdUnits != 0 || BonusSpeedUnitsPerPercent != 0
                    || Element != IdentityStrikeElement.None || Shape != IdentityStrikeShape.None || RangeMetres != 0f || WidthOrArc != 0f || MaxTargets != 0)
                    throw new InvalidOperationException("The dash-bonus attribution is only for Wind Scar and carries no damage fields.");
                return;
            }
            if (AdUnits <= 0 || AdUnits > MaxAdUnits || BonusSpeedUnitsPerPercent < 0 || BonusSpeedUnitsPerPercent > MaxBonusSpeedUnits
                || Shape == IdentityStrikeShape.None || float.IsNaN(RangeMetres) || RangeMetres < 1f || RangeMetres > MaxRangeMetres
                || float.IsNaN(WidthOrArc) || WidthOrArc <= 0f || Shape == IdentityStrikeShape.ForwardArc && WidthOrArc > 360f
                || Shape == IdentityStrikeShape.ForwardLine && WidthOrArc > MaxRangeMetres || MaxTargets < 1 || MaxTargets > 16)
                throw new InvalidOperationException("Invalid identity strike damage or shape.");
            if (BonusSpeedUnitsPerPercent > 0 && Identity != KillingFlow)
                throw new InvalidOperationException("Only the Killing Flow identity supplies a bonus attack speed term.");
            if (Trigger == IdentityStrikeTrigger.EveryNthBasicAttack && (EveryN < 1 || EveryN > 100 || WindowSeconds != 0f))
                throw new InvalidOperationException("Invalid every-Nth cadence.");
            if (Trigger == IdentityStrikeTrigger.AfterDisplacementNextBasicHit || Trigger == IdentityStrikeTrigger.AfterDisplacementCritical)
                if (EveryN != 1 || float.IsNaN(WindowSeconds) || WindowSeconds < 0.5f || WindowSeconds > MaxWindowSeconds)
                    throw new InvalidOperationException("Invalid displacement window.");
            if (IsCriticalMechanism && (BonusSpeedUnitsPerPercent != 0 || MaxTargets > 6 || Element != IdentityStrikeElement.Dark
                || Trigger == IdentityStrikeTrigger.AfterDisplacementCritical && Identity != WindScar
                || Trigger == IdentityStrikeTrigger.ConsecutiveCritical && (Identity != KillingFlow || EveryN != 3
                    || float.IsNaN(WindowSeconds) || WindowSeconds < 0.5f || WindowSeconds > MaxWindowSeconds)))
                throw new InvalidOperationException("Invalid critical strike channel.");
        }

        /// <summary>Rescales the damage terms (rank / boost); both terms scale together. The dash-bonus form has no damage and is returned as is.</summary>
        public IdentityStrikeDefinition Scaled(decimal factor)
        {
            if (!DealsDamage) return this;
            if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
            int ad = (int)Math.Min(MaxAdUnits, decimal.Round(AdUnits * factor, MidpointRounding.AwayFromZero));
            int speed = (int)Math.Min(MaxBonusSpeedUnits, decimal.Round(BonusSpeedUnitsPerPercent * factor, MidpointRounding.AwayFromZero));
            return new IdentityStrikeDefinition(ChannelId, Identity, Trigger, EveryN, WindowSeconds, Math.Max(1, ad), speed, Element, Shape,
                RangeMetres, WidthOrArc, MaxTargets, Basis);
        }
        public IdentityStrikeDefinition WithAdUnits(decimal adUnits) => !DealsDamage ? this : Scaled(adUnits / AdUnits);

        /// <summary>The strike's damage amount before the game's own amplification: basis x (damage% + per-bonus-speed% term x bonus speed %).</summary>
        public float Damage(float attackDamage, float abilityPower, float bonusAttackSpeedPercent)
        {
            float basis = Basis == IdentityStrikeBasis.AttackDamage ? attackDamage : Math.Max(attackDamage, abilityPower);
            return Damage(basis, bonusAttackSpeedPercent);
        }
        /// <summary>True when the strike is magic (ability power is the higher of the two and the basis allows it).</summary>
        public bool IsMagic(float attackDamage, float abilityPower) => Basis == IdentityStrikeBasis.HigherOfAttackAndAbility && abilityPower > attackDamage;
        public float Damage(float basisAmount, float bonusAttackSpeedPercent)
        {
            if (!DealsDamage) throw new InvalidOperationException("This identity strike deals no damage of its own.");
            if (float.IsNaN(basisAmount) || basisAmount < 0f || float.IsNaN(bonusAttackSpeedPercent) || bonusAttackSpeedPercent < 0f) return 0f;
            return basisAmount * (AdUnits + BonusSpeedUnitsPerPercent * bonusAttackSpeedPercent) / 10000f;
        }

        /// <summary>Bonus attack speed % recovered from St_D_TheKillingFlow.gainedAd (its synced, rounded converted attack damage).</summary>
        public static float BonusSpeedPercentFromGainedAd(int gainedAd, float adPerPercent = KillingFlowAdPerBonusSpeedPercent) =>
            gainedAd <= 0 || adPerPercent <= 0f ? 0f : gainedAd / adPerPercent;

        /// <summary>Whether a target lies inside the forward line/arc (ground plane, metres); slack extends the reach by the target's radius.</summary>
        public bool Contains(float originX, float originZ, float forwardX, float forwardZ, float targetX, float targetZ, float slack = 0f)
        {
            if (!DealsDamage) return false;
            float fl = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (fl < 1e-6f) return false;
            float fx = forwardX / fl, fz = forwardZ / fl, dx = targetX - originX, dz = targetZ - originZ;
            float along = dx * fx + dz * fz, lateral = Math.Abs(dx * fz - dz * fx);
            if (Shape == IdentityStrikeShape.ForwardLine)
                return along >= -slack && along <= RangeMetres + slack && lateral <= WidthOrArc / 2f + slack;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist > RangeMetres + slack) return false;
            if (dist <= slack) return true;
            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, along / dist))) * 180.0 / Math.PI;
            return angle <= WidthOrArc / 2f;
        }
    }

    /// <summary>Per-hero trigger bookkeeping for one strike channel (host side, pure). Reset on equipment change, death and room change.</summary>
    public sealed class IdentityStrikeState
    {
        private readonly IdentityStrikeDefinition _def;
        private bool _armed;
        private float _armedUntil;
        private int _count;
        private long _lastActivation = -1;
        private long _victimLifetime;
        private float _lastCriticalTime;
        private float _nextCriticalStrike = float.NegativeInfinity;
        public IdentityStrikeState(IdentityStrikeDefinition definition)
        {
            _def = definition ?? throw new ArgumentNullException(nameof(definition));
            if (!definition.DealsDamage) throw new ArgumentException("No trigger state for the dash-bonus form.", nameof(definition));
        }
        public bool Armed => _armed;
        public int Count => _count;
        /// <summary>A self displacement (dash/teleport) arms the next basic hit (re-arming while armed only extends the window, never stacks).</summary>
        public void OnDisplacement(float now)
        {
            if (_def.Trigger != IdentityStrikeTrigger.AfterDisplacementNextBasicHit && _def.Trigger != IdentityStrikeTrigger.AfterDisplacementCritical) return;
            _armed = true; _armedUntil = now + _def.WindowSeconds;
        }
        /// <summary>The first hit of an own basic attack activation; later hits of the same activation never count twice. True = fire one strike.</summary>
        public bool OnBasicHit(long activationId, float now, bool critical = false, long victimLifetime = 0)
        {
            if (activationId == _lastActivation) return false;
            _lastActivation = activationId;
            if (_def.Trigger == IdentityStrikeTrigger.AfterDisplacementNextBasicHit || _def.Trigger == IdentityStrikeTrigger.AfterDisplacementCritical)
            {
                if (!_armed) return false;
                _armed = false;
                if (now > _armedUntil) return false;
                if (_def.Trigger == IdentityStrikeTrigger.AfterDisplacementNextBasicHit) return true;
                if (!critical || now < _nextCriticalStrike) return false;
                _nextCriticalStrike = now + IdentityStrikeDefinition.CriticalCooldownSeconds;
                return true;
            }
            if (_def.Trigger == IdentityStrikeTrigger.ConsecutiveCritical)
            {
                if (!critical || victimLifetime == 0) { _count = 0; _victimLifetime = 0; return false; }
                if (_victimLifetime != victimLifetime || now > _lastCriticalTime + _def.WindowSeconds) _count = 0;
                _victimLifetime = victimLifetime;
                _lastCriticalTime = now;
                if (++_count < _def.EveryN) return false;
                _count = 0;
                if (now < _nextCriticalStrike) return false;
                _nextCriticalStrike = now + IdentityStrikeDefinition.CriticalCooldownSeconds;
                return true;
            }
            if (++_count < _def.EveryN) return false;
            _count = 0;
            return true;
        }
        public void Reset() { _armed = false; _count = 0; _lastActivation = -1; _victimLifetime = 0; }
    }
}

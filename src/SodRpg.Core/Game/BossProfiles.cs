using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public enum BossEvent { MainHit, MemoryUse, MovementCompleted, NativeDamageDealt, NativeDamageTaken, RewardInstance, Clock, ModeTransition }
    public enum BossMechanism { ShapeAttack, Projectile, Field, Movement, EnemyMovement, Deployable, Defense, Ledger }
    public enum BossPayload { Damage, Bud, CollectBud, March, ArrivalWindow, Unstoppable, Dash, Push, Pull, Deploy, Heal, Shield, Modifier, Mark, Mode }
    public enum BossShape { Circle, Fan, Line, Column, Radial }
    public enum BossAnchor { Owner, Hit, Cursor, Destination, Collected, Marked }
    public enum BossElement { Neutral, Fire, Cold, Light, Dark }
    public enum BossCoefficientKind { Damage, Heal, Shield, Duration, Distance, Stat }
    public enum BossRewardAdapter { Hysteria, GlacialCore, EternalFlame, BeamOfBalance, HerWorld, LastStarlight, SoulPrison, Burrow, WorldCracker, BigChomp, ShoutOfOblivion }
    public enum BossRewardActionKind { HysteriaPair, HysteriaPlant, HysteriaCollect, HysteriaMarch, NativeSpeed, NativeHeal, NativeTarget, NativeInterval, NativeDamage, NativeState, NativeShield, NativeMark, NativeMode }

    public sealed class BossChannelDef
    {
        public string ChannelId { get; }
        public int ValueMilli { get; }
        public int CapMilli { get; }
        public BossCoefficientKind Kind { get; }
        public BossChannelDef(string channelId, int valueMilli, int capMilli, BossCoefficientKind kind = BossCoefficientKind.Damage)
        {
            if (!Gimmicks.ValidStarId(channelId) || valueMilli <= 0 || capMilli < valueMilli || !Enum.IsDefined(typeof(BossCoefficientKind), kind)) throw new ArgumentException("Invalid boss channel.");
            ChannelId = channelId; ValueMilli = valueMilli; CapMilli = capMilli; Kind = kind;
        }
    }

    /// <summary>Fixed, bounded payload, never an executable graph or recursive effect script.</summary>
    public sealed class BossAction
    {
        public BossEvent Event { get; }
        public BossMechanism Mechanism { get; }
        public BossPayload Payload { get; }
        public string ChannelId { get; }
        public BossShape Shape { get; }
        public BossAnchor Anchor { get; }
        public int CooldownMillis { get; }
        public int DelayMillis { get; }
        public int LifetimeMillis { get; }
        public int IntervalMillis { get; }
        public int Count { get; }
        public int MaxTargets { get; }
        public int MaxInstances { get; }
        public int MainHits { get; }
        public int CounterLifetimeMillis { get; }
        public int GenerationLimit { get; }
        public int RadiusMilli { get; }
        public int RangeMilli { get; }
        public int WidthMilli { get; }
        public int SpeedMilli { get; }
        public int AngleMilli { get; }
        public int HitGateMillis { get; }
        public bool Collectible { get; }
        public bool FirstHitOnly { get; }
        public bool ReplaceOldest { get; }
        public string LedgerId { get; }
        public int MagnitudeMilli { get; }
        public BossElement Element { get; }
        public int RequiredMode { get; }
        public bool FollowOwner { get; }
        public Stat? ModifierStat { get; }
        public int RequiredMarks { get; }
        public bool ConsumeMarks { get; }
        public string RuntimeKey { get; internal set; }
        public BossAction(BossEvent @event, BossMechanism mechanism, BossPayload payload, string channelId = null,
            BossShape shape = BossShape.Circle, BossAnchor anchor = BossAnchor.Owner, int cooldownMillis = 0,
            int delayMillis = 0, int lifetimeMillis = 0, int intervalMillis = 0, int count = 1, int maxTargets = 64,
            int maxInstances = 4, int mainHits = 0, int counterLifetimeMillis = 0, int generationLimit = 1,
            int radiusMilli = 0, int rangeMilli = 0, int widthMilli = 0, int speedMilli = 0, int angleMilli = 0,
            int hitGateMillis = 0, bool collectible = false, bool firstHitOnly = false, bool replaceOldest = false,
            string ledgerId = null, int magnitudeMilli = 0, BossElement element = BossElement.Neutral, int requiredMode = -1,
            bool followOwner = false, Stat? modifierStat = null, int requiredMarks = 0, bool consumeMarks = false)
        {
            if (!Enum.IsDefined(typeof(BossEvent), @event) || !Enum.IsDefined(typeof(BossMechanism), mechanism)
                || !Enum.IsDefined(typeof(BossPayload), payload) || !Enum.IsDefined(typeof(BossShape), shape)
                || !Enum.IsDefined(typeof(BossAnchor), anchor) || cooldownMillis < 0 || delayMillis < 0 || lifetimeMillis < 0
                || intervalMillis < 0 || count < 1 || count > 64 || maxTargets < 1 || maxTargets > 64
                || maxInstances < 1 || maxInstances > 4 || mainHits < 0 || mainHits > 6 || counterLifetimeMillis < 0
                || generationLimit < 0 || generationLimit > 1 || radiusMilli < 0 || rangeMilli < 0 || widthMilli < 0
                || speedMilli < 0 || angleMilli < 0 || angleMilli > 360000 || hitGateMillis < 0
                || !Enum.IsDefined(typeof(BossElement), element) || requiredMode < -1 || requiredMode > 3
                || requiredMarks < 0 || requiredMarks > 3 || consumeMarks && requiredMarks == 0
                || payload == BossPayload.Modifier && !modifierStat.HasValue
                || modifierStat.HasValue && (!Enum.IsDefined(typeof(Stat), modifierStat.Value)
                    || modifierStat.Value == Stat.EssenceSlotIdentity || modifierStat.Value == Stat.EssenceSlotMovement
                    || modifierStat.Value == Stat.HealPower || modifierStat.Value == Stat.ShieldPower
                    || modifierStat.Value == Stat.SummonPower || modifierStat.Value == Stat.SacrificeReduction)
                || channelId != null && !Gimmicks.ValidStarId(channelId) || ledgerId != null && !Gimmicks.ValidStarId(ledgerId))
                throw new ArgumentException("Invalid finite boss action.");
            Event = @event; Mechanism = mechanism; Payload = payload; ChannelId = channelId; Shape = shape; Anchor = anchor;
            CooldownMillis = cooldownMillis; DelayMillis = delayMillis; LifetimeMillis = lifetimeMillis; IntervalMillis = intervalMillis;
            Count = count; MaxTargets = maxTargets; MaxInstances = maxInstances; MainHits = mainHits; CounterLifetimeMillis = counterLifetimeMillis;
            GenerationLimit = generationLimit; RadiusMilli = radiusMilli; RangeMilli = rangeMilli; WidthMilli = widthMilli; SpeedMilli = speedMilli;
            AngleMilli = angleMilli; HitGateMillis = hitGateMillis; Collectible = collectible; FirstHitOnly = firstHitOnly;
            ReplaceOldest = replaceOldest; LedgerId = ledgerId; MagnitudeMilli = magnitudeMilli;
            Element = element; RequiredMode = requiredMode;
            FollowOwner = followOwner; ModifierStat = modifierStat;
            RequiredMarks = requiredMarks; ConsumeMarks = consumeMarks;
        }
        internal string Fingerprint => string.Join(":", (int)Event, (int)Mechanism, (int)Payload, ChannelId, (int)Shape, (int)Anchor,
            CooldownMillis, DelayMillis, LifetimeMillis, IntervalMillis, Count, MaxTargets, MaxInstances, MainHits, CounterLifetimeMillis,
            GenerationLimit, RadiusMilli, RangeMilli, WidthMilli, SpeedMilli, AngleMilli, HitGateMillis, Collectible, FirstHitOnly, ReplaceOldest, LedgerId,
            MagnitudeMilli.ToString(CultureInfo.InvariantCulture), (int)Element, RequiredMode.ToString(CultureInfo.InvariantCulture), FollowOwner,
            (ModifierStat.HasValue ? (int)ModifierStat.Value : -1).ToString(CultureInfo.InvariantCulture), RequiredMarks, ConsumeMarks);
    }

    public sealed class BossMoveProfile
    {
        public string Id { get; }
        public string SetId { get; }
        public Txt Description { get; }
        public IReadOnlyList<BossChannelDef> Channels { get; }
        public IReadOnlyList<BossAction> Actions { get; }
        public BossMoveProfile(string id, string setId, Txt description, IEnumerable<BossChannelDef> channels, IEnumerable<BossAction> actions)
        {
            if (!Gimmicks.ValidStarId(id) || !Gimmicks.ValidStarId(setId) || description == null) throw new ArgumentException("Invalid boss profile identity.");
            var c = channels?.ToArray(); var a = actions?.ToArray();
            if (c == null || c.Length < 1 || c.Length > 8 || a == null || a.Length < 1 || a.Length > 16
                || c.Any(x => x == null) || a.Any(x => x == null) || c.Select(x => x.ChannelId).Distinct(StringComparer.Ordinal).Count() != c.Length
                || a.Any(x => x.ChannelId != null && !c.Any(y => y.ChannelId == x.ChannelId))) throw new ArgumentException("Invalid boss profile payload.");
            Id = id; SetId = setId; Description = description; Channels = Array.AsReadOnly(c); Actions = Array.AsReadOnly(a);
            for (int i = 0; i < a.Length; i++) a[i].RuntimeKey = id + "." + i;
        }
    }
    public sealed class BossRewardAction
    {
        public BossRewardActionKind Kind { get; }
        public int ValueMilli { get; }
        public int CapMilli { get; }
        public int CooldownMillis { get; }
        public int DurationMillis { get; }
        public int Count { get; }
        public int RangeMilli { get; }
        public int Order { get; }
        public int MagnitudeMilli { get; }
        public int IntervalMillis { get; }
        public int TargetCapMilli { get; }
        public int BudgetMilli { get; }
        public int DwellMillis { get; }
        public int GapToleranceMillis { get; }
        public int SpeedMilli { get; }
        public int WidthMilli { get; }
        public BossRewardAction(BossRewardActionKind kind, int valueMilli = 0, int capMilli = 0, int cooldownMillis = 0,
            int durationMillis = 0, int count = 1, int rangeMilli = 0, int order = 0, int magnitudeMilli = 0, int intervalMillis = 0,
            int targetCapMilli = 0, int budgetMilli = 0, int dwellMillis = 0, int gapToleranceMillis = 0, int speedMilli = 0, int widthMilli = 0)
        {
            if (!Enum.IsDefined(typeof(BossRewardActionKind), kind) || capMilli < Math.Abs((long)valueMilli) || cooldownMillis < 0
                || durationMillis < 0 || count < 1 || count > 64 || rangeMilli < 0 || order < 0 || magnitudeMilli < 0 || intervalMillis < 0
                || targetCapMilli < 0 || budgetMilli < 0 || dwellMillis < 0 || gapToleranceMillis < 0 || speedMilli < 0 || widthMilli < 0)
                throw new ArgumentException("Invalid boss reward action.");
            Kind = kind; ValueMilli = valueMilli; CapMilli = capMilli; CooldownMillis = cooldownMillis; DurationMillis = durationMillis;
            Count = count; RangeMilli = rangeMilli; Order = order;
            MagnitudeMilli = magnitudeMilli; IntervalMillis = intervalMillis;
            TargetCapMilli = targetCapMilli; BudgetMilli = budgetMilli; DwellMillis = dwellMillis; GapToleranceMillis = gapToleranceMillis;
            SpeedMilli = speedMilli; WidthMilli = widthMilli;
        }
        internal string Fingerprint => string.Join(":", (int)Kind, ValueMilli.ToString(CultureInfo.InvariantCulture), CapMilli, CooldownMillis, DurationMillis, Count, RangeMilli, Order,
            MagnitudeMilli, IntervalMillis, TargetCapMilli, BudgetMilli, DwellMillis, GapToleranceMillis, SpeedMilli, WidthMilli);
    }
    public sealed class BossRewardStage
    {
        public int Stage { get; }
        public Txt Description { get; }
        public IReadOnlyList<BossRewardAction> Actions { get; }
        public BossRewardStage(int stage, Txt description, IEnumerable<BossRewardAction> actions)
        {
            var a = actions?.ToArray();
            if (stage < 1 || stage > 3 || description == null || a == null || a.Length < 1 || a.Length > 16
                || a.Any(x => x == null) || a.Where((x, i) => x.Order != i).Any()) throw new ArgumentException("Invalid ordered boss reward stage.");
            Stage = stage; Description = description; Actions = Array.AsReadOnly(a);
        }
    }
    public sealed class BossRewardProfile
    {
        public string Id { get; }
        public string SetId { get; }
        public string Requires { get; }
        public BossRewardAdapter Adapter { get; }
        public IReadOnlyList<BossRewardStage> Stages { get; }
        public BossRewardProfile(string id, string setId, string requires, BossRewardAdapter adapter, IEnumerable<BossRewardStage> stages)
        {
            var s = stages?.ToArray();
            if (!Gimmicks.ValidStarId(id) || !Gimmicks.ValidStarId(setId) || !Links.IsKnown(requires) || Links.IsTraveler(requires)
                || !Enum.IsDefined(typeof(BossRewardAdapter), adapter) || s == null || s.Length != 3
                || s.Where((x, i) => x == null || x.Stage != i + 1).Any()) throw new ArgumentException("Invalid boss reward profile.");
            Id = id; SetId = setId; Requires = requires; Adapter = adapter; Stages = Array.AsReadOnly(s);
        }
    }
    public sealed class BossSetStage
    {
        public int RequiredPieces { get; }
        public string ProfileId { get; }
        public BossSetStage(int requiredPieces, string profileId)
        {
            if ((requiredPieces != 2 && requiredPieces != 3 && requiredPieces != 6) || !Gimmicks.ValidStarId(profileId)) throw new ArgumentException("Invalid boss set stage.");
            RequiredPieces = requiredPieces; ProfileId = profileId;
        }
    }
    public sealed class BossChannelValue
    {
        public string ChannelId { get; }
        public int ValueMilli { get; }
        public BossChannelValue(string channelId, int valueMilli) { ChannelId = channelId; ValueMilli = valueMilli; }
    }
    public sealed class BossMoveEntry
    {
        public string SetId { get; }
        public string ProfileId { get; }
        public IReadOnlyList<BossChannelValue> Channels { get; }
        public BossMoveEntry(string setId, string profileId, IEnumerable<BossChannelValue> channels)
        { SetId = setId; ProfileId = profileId; Channels = Array.AsReadOnly(channels.ToArray()); }
    }
    public sealed class BossRewardEntry
    {
        public string SetId { get; }
        public string ProfileId { get; }
        public int Stage { get; }
        public BossRewardEntry(string setId, string profileId, int stage) { SetId = setId; ProfileId = profileId; Stage = stage; }
    }
}

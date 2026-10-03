using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public readonly struct ValueUnits
    {
        public ValueUnits(int units) { if (units < 0) throw new ArgumentOutOfRangeException(nameof(units)); Units = units; }
        public int Units { get; }
        public int ValueMilli => checked(Units * 10);
        public static ValueUnits FromPercent(decimal percent) => new ValueUnits(UnitConversion.FromPercent(percent));
    }
    public readonly struct ModifierUnits
    {
        public ModifierUnits(int units) { if (units < 0) throw new ArgumentOutOfRangeException(nameof(units)); Units = units; }
        public int Units { get; }
        public static ModifierUnits FromPercent(decimal percent) => new ModifierUnits(UnitConversion.FromPercent(percent));
    }
    public readonly struct ProbabilityUnits
    {
        public ProbabilityUnits(int units) { if (units < 0 || units > 10000) throw new ArgumentOutOfRangeException(nameof(units)); Units = units; }
        public int Units { get; }
        public float Ratio => Units / 10000f;
        public static ProbabilityUnits FromPercent(decimal percent) => new ProbabilityUnits(UnitConversion.FromPercent(percent));
    }
    internal static class UnitConversion
    {
        internal static int FromPercent(decimal percent)
        {
            decimal units = percent * 100m;
            if (units != decimal.Truncate(units)) throw new ArgumentOutOfRangeException(nameof(percent), "Percentages require exact hundredths.");
            return checked((int)units);
        }
        internal static int WholePercent(int units)
        {
            if (units % 100 != 0) throw new InvalidOperationException("A fractional parameter cannot be read as a whole percentage.");
            return units / 100;
        }
    }
    public enum ScopeKind { Memory, EffectChannel, Receiver }
    public sealed class ScopedModifierDef
    {
        public ScopeKind ScopeKind { get; set; }
        public string ScopeMemory { get; set; }
        public string[] TargetEffectIds { get; set; } = Array.Empty<string>();
        public GimmickEffect[] TargetEffects { get; set; } = Array.Empty<GimmickEffect>();
        public GimmickParam? Param { get; set; }
        public ModifierUnits Amount { get; set; }
        public ProbabilityUnits Probability { get; set; }
        public int ExtraTargets { get; set; }
        public string CapProfileId { get; set; }
    }
    /// <summary>Explicit additive identity. Independent legacy stars never acquire this identity implicitly.</summary>
    public sealed class EffectChannelDef
    {
        public string OwnerId { get; set; } = "self";
        public string SourceMemory { get; set; }
        public string ReceiverMemory { get; set; }
        public string ChannelId { get; set; }
        public string[] EquipmentRequirements { get; set; } = Array.Empty<string>();
        public string PairSuccessId { get; set; }
        public string ActivationBudget { get; set; } = "notification";
        public string ClockPolicy { get; set; } = "channel";
        public ScopeKind ScopeKind { get; set; } = ScopeKind.EffectChannel;
    }
    public sealed class NativeMemoryModifierDef
    {
        public string Memory { get; set; }
        public LinkKind Kind { get; set; } = LinkKind.MemoryDamage;
        public ValueUnits Value { get; set; }
        public string CapProfileId { get; set; }
    }
    public sealed class NativeStarCapProfile
    {
        public string Id { get; set; }
        public LinkKind Kind { get; set; }
        public ValueUnits Maximum { get; set; }
    }
    public sealed class ScopedModifierCapProfile
    {
        public string Id { get; set; }
        public GimmickParam? Param { get; set; }
        public ModifierUnits MaximumModifier { get; set; }
        public ProbabilityUnits MaximumProbability { get; set; }
        public int MaximumTargets { get; set; }
    }
    public sealed class NativeMemoryModifierEntry
    {
        public string Memory { get; set; }
        public LinkKind Kind { get; set; }
        public int ValueMilli { get; set; }
        public string CapProfileId { get; set; }
    }
    public static class FractionalScopedModifiers
    {
        private static readonly ConcurrentDictionary<string, NativeStarCapProfile> Profiles = new ConcurrentDictionary<string, NativeStarCapProfile>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, ScopedModifierCapProfile> ScopedProfiles = new ConcurrentDictionary<string, ScopedModifierCapProfile>(StringComparer.Ordinal);
        private static readonly object CapLock = new object();
        private static string capFingerprint = StarClusters.RegistryHash("");
        public static string CapRegistryFingerprint { get { lock (CapLock) return capFingerprint; } }
        private static void RefreshCapFingerprint() => capFingerprint = StarClusters.RegistryHash(string.Join("|",
            Profiles.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => "n:" + x.Key + ":" + (int)x.Value.Kind + ":" + x.Value.Maximum.Units)
            .Concat(ScopedProfiles.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => "s:" + x.Key + ":" + x.Value.Param + ":"
                + x.Value.MaximumModifier.Units + ":" + x.Value.MaximumProbability.Units + ":" + x.Value.MaximumTargets))));

        public static void RegisterScopedCapProfile(ScopedModifierCapProfile profile)
        {
            if (profile == null || !Gimmicks.ValidStarId(profile.Id)
                || profile.Param.HasValue && !Enum.IsDefined(typeof(GimmickParam), profile.Param.Value)
                || (profile.Param == GimmickParam.Chance ? profile.MaximumProbability.Units <= 0 || profile.MaximumModifier.Units != 0 || profile.MaximumTargets != 0
                    : profile.Param == GimmickParam.ExtraTargets ? profile.MaximumTargets <= 0 || profile.MaximumTargets > Gimmicks.MaxExtraTargets
                        || profile.MaximumModifier.Units != 0 || profile.MaximumProbability.Units != 0
                    : profile.MaximumModifier.Units <= 0 || profile.MaximumProbability.Units != 0 || profile.MaximumTargets != 0
                        || profile.Param.HasValue && profile.MaximumModifier.Units > Gimmicks.MaxParameterPercent * 100))
                throw new InvalidOperationException("Invalid scoped modifier cap profile.");
            lock (CapLock)
            {
                if (!ScopedProfiles.TryAdd(profile.Id, CopyProfile(profile)))
                    throw new InvalidOperationException("Duplicate scoped modifier cap profile: " + profile.Id);
                RefreshCapFingerprint();
            }
        }

        private static ScopedModifierCapProfile CopyProfile(ScopedModifierCapProfile profile) =>
            new ScopedModifierCapProfile { Id = profile.Id, Param = profile.Param, MaximumModifier = profile.MaximumModifier,
                MaximumProbability = profile.MaximumProbability, MaximumTargets = profile.MaximumTargets };

        public static ScopedModifierCapProfile ScopedCapProfile(string profileId) =>
            ScopedProfiles.TryGetValue(profileId ?? "", out var profile) ? CopyProfile(profile)
                : throw new ArgumentException("Unknown scoped modifier cap profile.", nameof(profileId));

        public static int ScopedCapMaximumUnits(string profileId)
        {
            var profile = ScopedProfiles.TryGetValue(profileId ?? "", out var found) ? found
                : throw new ArgumentException("Unknown scoped modifier cap profile.", nameof(profileId));
            return profile.Param == GimmickParam.Chance ? profile.MaximumProbability.Units
                : profile.Param == GimmickParam.ExtraTargets ? profile.MaximumTargets : profile.MaximumModifier.Units;
        }

        private static ScopedModifierCapProfile MatchingCap(ScopedModifierCapProfile current, ScopedModifierDef modifier)
        {
            if (modifier.CapProfileId == null) return current;
            if (!ScopedProfiles.TryGetValue(modifier.CapProfileId, out var profile) || profile.Param != modifier.Param)
                throw new InvalidOperationException("Unknown or incompatible scoped cap profile: " + modifier.CapProfileId);
            if (current != null && current.Id != profile.Id)
                throw new InvalidOperationException("Contradictory scoped cap profiles: " + current.Id + " / " + profile.Id);
            return profile;
        }
        public static void RegisterCapProfile(NativeStarCapProfile profile)
        {
            if (profile == null || !Gimmicks.ValidStarId(profile.Id) || !NativeKind(profile.Kind) || profile.Maximum.Units <= 0
                || profile.Kind == LinkKind.MemoryHaste && profile.Maximum.Units > 10000)
                throw new InvalidOperationException("Invalid native star cap profile.");
            lock (CapLock)
            {
                if (!Profiles.TryAdd(profile.Id, new NativeStarCapProfile { Id = profile.Id, Kind = profile.Kind, Maximum = profile.Maximum }))
                    throw new InvalidOperationException("Duplicate native star cap profile: " + profile.Id);
                RefreshCapFingerprint();
            }
        }
        private static bool NativeKind(LinkKind kind) => kind == LinkKind.MemoryDamage || kind == LinkKind.MemoryHaste;
        public static int NativeCapValueMilli(string profileId) => Profiles.TryGetValue(profileId ?? "", out var profile)
            ? profile.Maximum.ValueMilli : throw new ArgumentException("Unknown native star cap profile.", nameof(profileId));
        public static bool ValidNativeEntry(NativeMemoryModifierEntry entry) => entry != null && Links.IsMemory(entry.Memory)
            && !entry.Memory.StartsWith("St_M_", StringComparison.Ordinal)
            && Profiles.TryGetValue(entry.CapProfileId ?? "", out var profile) && profile.Kind == entry.Kind
            && entry.ValueMilli > 0 && entry.ValueMilli <= profile.Maximum.ValueMilli;
        public static float NativePercent(IReadOnlyList<NativeMemoryModifierEntry> entries, string memory, LinkKind kind)
        {
            long total = 0;
            if (entries == null) return 0;
            foreach (var entry in entries) if (entry.Memory == memory && entry.Kind == kind) total = checked(total + entry.ValueMilli);
            return total / (float)BuildPrecision.Scale;
        }

        public static string Describe(ScopedModifierDef modifier)
        {
            string memory = Links.Name(modifier.ScopeMemory).ToString();
            string targets = string.Join(", ", modifier.TargetEffects.Select(EffectLabel));
            if (modifier.TargetEffectIds.Length > 0)
                targets += (targets.Length == 0 ? "" : " / ") + Loc.T("指定の仕掛け", "specified effects");
            string scope = modifier.ScopeKind == ScopeKind.Memory ? memory : memory + Loc.T("（" + targets + "）", " (" + targets + ")");
            string value = ((modifier.Param == GimmickParam.Chance ? modifier.Probability.Units : modifier.Amount.Units) / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            string cap = ModifierCapDescription(modifier);
            switch (modifier.Param)
            {
                case GimmickParam.Duration: return Loc.T(scope + "の指定効果の持続時間 +" + value + "%", scope + " effect duration +" + value + "%") + cap;
                case GimmickParam.Radius: return Loc.T(scope + "の指定効果の半径 +" + value + "%", scope + " effect radius +" + value + "%") + cap;
                case GimmickParam.Chance: return Loc.T(scope + "の指定効果の発動確率 +" + value + "パーセントポイント", scope + " effect chance +" + value + " percentage points") + cap;
                case GimmickParam.ExtraTargets: return Loc.T(scope + "の指定効果の追加対象 +" + modifier.ExtraTargets + "体", scope + " effect additional targets +" + modifier.ExtraTargets) + cap;
                default: return Loc.T(scope + "の指定効果量 +" + value + "%", scope + " effect value +" + value + "%") + cap;
            }
        }

        private static string EffectLabel(GimmickEffect effect)
        {
            switch (effect)
            {
                case GimmickEffect.Element: return Loc.T("属性付与", "element application");
                case GimmickEffect.Burst: return Loc.T("範囲追加ダメージ", "area damage");
                case GimmickEffect.Shield: return Loc.T("障壁", "shield");
                case GimmickEffect.Heal: return Loc.T("回復", "healing");
                case GimmickEffect.Recharge: return Loc.T("クールダウン短縮", "cooldown reduction");
                case GimmickEffect.Quicken: return Loc.T("攻撃速度", "attack speed");
                case GimmickEffect.Empower: return Loc.T("攻撃力・魔力", "attack damage and ability power");
                case GimmickEffect.Expose: return Loc.T("被ダメージ増加", "damage vulnerability");
                case GimmickEffect.Echo: return Loc.T("追撃", "echo damage");
                case GimmickEffect.Reload: return Loc.T("使用回数回復", "charge restoration");
                case GimmickEffect.RechargeOther: return Loc.T("他の記憶のクールダウン短縮", "other-memory cooldown reduction");
                case GimmickEffect.Wound: return Loc.T("継続ダメージ", "damage over time");
                case GimmickEffect.Daze: return Loc.T("スタン", "stun");
                case GimmickEffect.Ricochet: return Loc.T("跳弾", "ricochet damage");
                case GimmickEffect.Siphon: return Loc.T("吸収回復", "damage-based healing");
                case GimmickEffect.Rampart: return Loc.T("命中数による障壁", "per-target shield");
                case GimmickEffect.Primed: return Loc.T("次の通常攻撃強化", "next basic attack");
                case GimmickEffect.Crescendo: return Loc.T("記憶ダメージ累積強化", "stacking memory damage");
                case GimmickEffect.ElementEdge: return Loc.T("属性ごとの追撃", "per-element damage");
                case GimmickEffect.PackMend: return Loc.T("召喚獣回復", "summon healing");
                case GimmickEffect.Sap: return Loc.T("与ダメージ低下", "damage reduction");
                case GimmickEffect.Weakspot: return Loc.T("弱点", "weakspot");
                default: throw new ArgumentOutOfRangeException(nameof(effect));
            }
        }

        private static string ModifierCapDescription(ScopedModifierDef modifier)
        {
            if (modifier.CapProfileId == null && !modifier.Param.HasValue)
                return Loc.T("（対象効果の最終上限に従う）", " (subject to final effect caps)");
            int units = modifier.CapProfileId != null ? ScopedCapMaximumUnits(modifier.CapProfileId)
                : modifier.Param == GimmickParam.ExtraTargets ? Gimmicks.MaxExtraTargets
                : modifier.Param == GimmickParam.Chance ? 10000 : Gimmicks.MaxParameterPercent * 100;
            string maximum = (modifier.Param == GimmickParam.ExtraTargets ? units : units / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            string jaUnit = modifier.Param == GimmickParam.ExtraTargets ? "体" : modifier.Param == GimmickParam.Chance ? "パーセントポイント" : "%";
            string enUnit = modifier.Param == GimmickParam.ExtraTargets ? " targets" : modifier.Param == GimmickParam.Chance ? " percentage points" : "%";
            return Loc.T("（指定範囲の合計上限" + maximum + jaUnit + "）", " (scope total cap " + maximum + enUnit + ")");
        }

        public static string Describe(NativeMemoryModifierDef modifier)
        {
            var cap = Profiles[modifier.CapProfileId];
            string value = (modifier.Value.Units / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            string maximum = (cap.Maximum.Units / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            string memory = Links.Name(modifier.Memory).ToString();
            return modifier.Kind == LinkKind.MemoryDamage
                ? Loc.T(memory + "の記憶ダメージ +" + value + "%（星の上限" + maximum + "%）", memory + " memory damage +" + value + "% (star cap " + maximum + "%)")
                : Loc.T(memory + "を使用した際のクールダウン短縮 +" + value + "%（星の上限" + maximum + "%）", memory + " self cooldown reduction on use +" + value + "% (star cap " + maximum + "%)");
        }
        public static void ValidateTalent(TalentDef talent)
        {
            if (talent.Gimmick != null && talent.Gimmick.ValuePrecise % 10000 != 0)
                throw new InvalidOperationException("Authored base values must be exact legacy thousandths or typed hundredths: " + talent.Id);
            var m = talent.ScopedModifier;
            if (m != null)
            {
                if (!Enum.IsDefined(typeof(ScopeKind), m.ScopeKind) || !Links.IsMemory(m.ScopeMemory) || m.ScopeMemory != talent.RouteMemory
                    || m.TargetEffectIds == null || m.TargetEffects == null
                    || m.ScopeKind != ScopeKind.Memory && m.TargetEffectIds.Length == 0 && m.TargetEffects.Length == 0
                    || (m.Param == GimmickParam.ExtraTargets ? m.ExtraTargets <= 0 || m.Amount.Units != 0 || m.Probability.Units != 0
                        : m.Param == GimmickParam.Chance ? m.Probability.Units <= 0 || m.Amount.Units != 0 || m.ExtraTargets != 0
                        : m.Amount.Units <= 0 || m.ExtraTargets != 0 || m.Probability.Units != 0))
                    throw new InvalidOperationException("Invalid scoped modifier: " + talent.Id);
                if (m.Param.HasValue && !Enum.IsDefined(typeof(GimmickParam), m.Param.Value)) throw new InvalidOperationException("Unsupported parameter: " + talent.Id);
                if (m.CapProfileId != null) MatchingCap(null, m);
                foreach (string id in m.TargetEffectIds) if (!Gimmicks.ValidStarId(id)) throw new InvalidOperationException("Invalid target ID: " + talent.Id);
                foreach (var effect in m.TargetEffects) if (Gimmicks.Cap(effect) == 0) throw new InvalidOperationException("Unsupported target effect: " + talent.Id);
                if (talent.GimmickBoost != 0 || talent.GimmickParameter.HasValue || talent.Gimmick != null || talent.LinkPerRank != null || talent.PerRank != 0)
                    throw new InvalidOperationException("Scoped modifier cannot also carry an independent effect: " + talent.Id);
            }
            if (talent.NativeModifier != null)
            {
                var n = talent.NativeModifier;
                if (!Links.IsMemory(n.Memory) || n.Memory != talent.RouteMemory || n.Memory.StartsWith("St_M_", StringComparison.Ordinal)
                    || !NativeKind(n.Kind) || n.Value.Units <= 0
                    || !Profiles.TryGetValue(n.CapProfileId ?? "", out var profile) || profile.Kind != n.Kind
                    || talent.LinkPerRank != null || talent.Gimmick != null || m != null || talent.PerRank != 0 || talent.IsPowerNode)
                    throw new InvalidOperationException("Invalid native star modifier or undeclared cap profile: " + talent.Id);
            }
            if (talent.EffectChannel != null) ValidateChannel(talent.EffectChannel, talent.Gimmick, talent.RouteMemory);
            if (talent.Mechanism != null) AuthoredMechanisms.Validate(talent.Mechanism);
        }
        internal static bool RequiresMechanismRoute(EffectChannelDef c) => c != null && (c.SourceMemory != c.ReceiverMemory
            || c.PairSuccessId != null || c.ActivationBudget != "notification");
        private static void ValidateChannel(EffectChannelDef c, GimmickDef def, string memory)
        {
            if (def == null || !Links.IsMemory(memory) || memory.StartsWith("St_M_", StringComparison.Ordinal)
                || !Gimmicks.ValidStarId(c.ChannelId) || c.OwnerId != "self" || c.SourceMemory != memory
                || !Links.IsMemory(c.ReceiverMemory) || c.PairSuccessId != null && PairCombos.Get(c.PairSuccessId) == null && PairCombos.ForBridge(c.PairSuccessId) == null
                || c.ClockPolicy != "channel" || !Enum.IsDefined(typeof(ScopeKind), c.ScopeKind)
                || c.EquipmentRequirements == null || c.EquipmentRequirements.Any(r => !Links.IsMemory(r))
                || c.ActivationBudget != "notification" && !Enum.TryParse<AttributionBudget>(c.ActivationBudget, false, out _))
                throw new InvalidOperationException("Invalid routed authored channel identity.");
            if (RequiresMechanismRoute(c)) AuthoredMechanisms.Validate(AuthoredMechanisms.FromChannel(c, def));
        }
        public static string ChannelKey(GimmickEntry entry)
        {
            var c = entry.Channel ?? throw new ArgumentException("Missing authored channel.");
            ValidateChannel(c, entry.Def, entry.Memory);
            return string.Join("/", c.OwnerId, c.SourceMemory, ((int)entry.Def.Trigger).ToString(CultureInfo.InvariantCulture),
                ((int)entry.Def.Effect).ToString(CultureInfo.InvariantCulture), entry.Def.Arg.ToString(CultureInfo.InvariantCulture), c.ReceiverMemory,
                entry.Memory, c.PairSuccessId ?? "-",
                c.ActivationBudget, c.ClockPolicy, ((int)c.ScopeKind).ToString(CultureInfo.InvariantCulture), c.ChannelId,
                entry.Def.Cooldown.ToString("R", CultureInfo.InvariantCulture), entry.Def.DurationUnits.ToString(CultureInfo.InvariantCulture),
                entry.Def.RadiusUnits.ToString(CultureInfo.InvariantCulture), entry.Def.ExtraTargets.ToString(CultureInfo.InvariantCulture), entry.Def.ChanceUnits.ToString(CultureInfo.InvariantCulture));
        }
        public static bool Matches(ScopedModifierDef modifier, GimmickEntry entry, EffectChannelDef channel = null)
        {
            bool memory = modifier.ScopeKind == ScopeKind.Receiver
                ? (channel ?? entry.Channel)?.ReceiverMemory == modifier.ScopeMemory : entry.Memory == modifier.ScopeMemory;
            return memory && (modifier.TargetEffectIds.Length == 0 || modifier.TargetEffectIds.Contains(entry.StarId)
                || entry.ContributorIds.Any(id => modifier.TargetEffectIds.Contains(id)))
                && (modifier.TargetEffects.Length == 0 || modifier.TargetEffects.Contains(entry.Def.Effect));
        }
        public static void ValidateTree(IReadOnlyList<TalentDef> tree)
        {
            bool typed = false;
            foreach (var t in tree)
            {
                typed |= t.ScopedModifier != null || t.NativeModifier != null || t.EffectChannel != null || t.Mechanism != null;
                foreach (var option in t.Choices)
                    typed |= option.ScopedModifier != null || option.NativeModifier != null || option.EffectChannel != null || option.Mechanism != null;
            }
            if (!typed) return;
            var all = tree.SelectMany(t => t.IsChoice ? t.Choices : new[] { t }).ToArray();
            foreach (var t in all) ValidateTalent(t);
            foreach (var t in all)
            {
                if (t.ScopedModifier == null) continue;
                var m = t.ScopedModifier;
                var targets = all.Where(n => n.HeroKey == t.HeroKey && (n.Gimmick != null
                    && Matches(m, new GimmickEntry { StarId = n.Id, Memory = n.RouteMemory, Def = n.Gimmick, Channel = n.EffectChannel })
                    || n.Mechanism != null && AuthoredMechanisms.Matches(m, new AuthoredMechanismEntry { StarId = n.Id, ContributorIds = new[] { n.Id }, Spec = n.Mechanism }))).ToArray();
                if (targets.Length == 0 || targets.Any(n => m.Param.HasValue
                    ? n.Mechanism != null ? !AuthoredMechanisms.Supports(n.Mechanism, m.Param.Value) : !Gimmicks.SupportsParameter(n.Gimmick, m.Param.Value)
                    : n.Gimmick?.Effect == GimmickEffect.Reload)) throw new InvalidOperationException("Scoped modifier has no meaningful recipient: " + t.Id);
                foreach (string id in m.TargetEffectIds) if (!targets.Any(n => n.Id == id)) throw new InvalidOperationException("Unknown scoped recipient: " + id);
                if (m.CapProfileId != null && m.Param.HasValue)
                    foreach (var target in targets)
                        if (target.Gimmick != null) CapAdded(0, BaseParameterUnits(target.Gimmick, m.Param.Value), ScopedCapMaximumUnits(m.CapProfileId));
            }
        }

        private static int BaseParameterUnits(GimmickDef def, GimmickParam parameter)
        {
            switch (parameter)
            {
                case GimmickParam.Duration: return def.DurationUnits;
                case GimmickParam.Radius: return def.RadiusUnits;
                case GimmickParam.Chance: return def.ChanceUnits;
                case GimmickParam.ExtraTargets: return def.ExtraTargets;
                default: throw new InvalidOperationException("Unsupported scoped parameter.");
            }
        }

        private static long CapAdded(long added, int current, int maximum)
        {
            if (current > maximum) throw new InvalidOperationException("The scoped cap contradicts the authored base parameter.");
            return Math.Min(added, maximum - current);
        }
        internal static void Compose(Build build, IReadOnlyList<KeyValuePair<TalentDef, int>> selected)
        {
            var channels = new Dictionary<string, GimmickEntry>(StringComparer.Ordinal);
            var entries = new List<GimmickEntry>();
            var native = new Dictionary<string, NativeMemoryModifierEntry>(StringComparer.Ordinal);
            foreach (var s in selected)
            {
                var t = s.Key;
                if (t.NativeModifier != null)
                {
                    var n = t.NativeModifier;
                    string key = ((int)n.Kind).ToString(CultureInfo.InvariantCulture) + ":" + n.Memory;
                    if (native.TryGetValue(key, out var e))
                    {
                        if (e.CapProfileId != n.CapProfileId) throw new InvalidOperationException("Conflicting native cap profiles.");
                        e.ValueMilli = checked(e.ValueMilli + n.Value.ValueMilli * s.Value);
                    }
                    else native.Add(key, new NativeMemoryModifierEntry { Memory = n.Memory, Kind = n.Kind, ValueMilli = checked(n.Value.ValueMilli * s.Value), CapProfileId = n.CapProfileId });
                }
            }
            foreach (var e in native.Values)
            {
                e.ValueMilli = Math.Min(e.ValueMilli, Profiles[e.CapProfileId].Maximum.ValueMilli);
                build.NativeModifiers.Add(e);
            }
            foreach (var raw in build.Gimmicks)
            {
                var entry = raw;
                if (entry.Channel != null)
                {
                    string key = ChannelKey(entry);
                    if (channels.TryGetValue(key, out var same))
                    {
                        same.Def.ValuePrecise = checked(same.Def.ValuePrecise + entry.Def.ValuePrecise);
                        same.ContributorIds = same.ContributorIds.Concat(entry.ContributorIds).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                        if (StringComparer.Ordinal.Compare(entry.StarId, same.StarId) < 0) same.StarId = entry.StarId;
                        continue;
                    }
                    channels.Add(key, entry);
                }
                entries.Add(entry);
            }
            build.Gimmicks.Clear();
            foreach (var entry in entries)
            {
                long boost = 0, duration = 0, radius = 0, chance = 0, targets = 0;
                bool sourceBoost = false, receiverBoost = false;
                ScopedModifierCapProfile boostCap = null, durationCap = null, radiusCap = null, chanceCap = null, targetsCap = null;
                foreach (var s in selected)
                {
                    var t = s.Key;
                    if (t.RouteMemory == entry.Memory)
                    {
                        boost += (long)t.GimmickBoost * s.Value * 100;
                        sourceBoost |= t.GimmickBoost != 0;
                        long amount = (long)t.GimmickParamAmount * s.Value;
                        switch (t.GimmickParameter)
                        {
                            case GimmickParam.Duration: duration += amount * 100; break;
                            case GimmickParam.Radius: radius += amount * 100; break;
                            case GimmickParam.Chance: chance += amount * 100; break;
                            case GimmickParam.ExtraTargets: targets += amount; break;
                        }
                    }
                    var m = t.ScopedModifier;
                    if (m == null || !Matches(m, entry)) continue;
                    if (m.Param.HasValue && !Gimmicks.SupportsParameter(entry.Def, m.Param.Value)) throw new InvalidOperationException("Unsupported scoped field: " + t.Id);
                    long value = (long)m.Amount.Units * s.Value;
                    switch (m.Param)
                    {
                        case GimmickParam.Duration:
                            duration += value; durationCap = MatchingCap(durationCap, m); break;
                        case GimmickParam.Radius:
                            radius += value; radiusCap = MatchingCap(radiusCap, m); break;
                        case GimmickParam.Chance:
                            chance += (long)m.Probability.Units * s.Value; chanceCap = MatchingCap(chanceCap, m); break;
                        case GimmickParam.ExtraTargets:
                            targets += (long)m.ExtraTargets * s.Value; targetsCap = MatchingCap(targetsCap, m); break;
                        default:
                            boost += value;
                            boostCap = MatchingCap(boostCap, m);
                            receiverBoost |= m.ScopeKind == ScopeKind.Receiver;
                            sourceBoost |= m.ScopeKind != ScopeKind.Receiver;
                            break;
                    }
                }
                if (sourceBoost && receiverBoost) throw new InvalidOperationException("Source/receiver double boost is forbidden: " + entry.StarId);
                if (boostCap != null) boost = Math.Min(boost, boostCap.MaximumModifier.Units);
                if (durationCap != null) duration = CapAdded(duration, entry.Def.DurationUnits, durationCap.MaximumModifier.Units);
                if (radiusCap != null) radius = CapAdded(radius, entry.Def.RadiusUnits, radiusCap.MaximumModifier.Units);
                if (chanceCap != null) chance = CapAdded(chance, entry.Def.ChanceUnits, chanceCap.MaximumProbability.Units);
                if (targetsCap != null) targets = CapAdded(targets, entry.Def.ExtraTargets, targetsCap.MaximumTargets);
                entry.Def = Gimmicks.ApplyModifierUnits(entry.Def, boost, duration, radius, targets, chance);
                build.Gimmicks.Add(Gimmicks.Clamp(entry) ?? throw new InvalidOperationException("Invalid composed channel."));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    public enum AuthoredMechanismKind { Gimmick, DirectedRecharge, BridgeSuccess, MemoryPrimed, RelayWindow, SacrificeShield, AlliedWard, PressureDividend, StunSourceFilter }
    public enum AuthoredMechanismCondition { Always, BridgeSuccess, BridgeMark, BridgeWindow }

    /// <summary>Typed authoring contract. Only the payload selected by Kind may be populated.</summary>
    public sealed class AuthoredMechanismSpec
    {
        public AuthoredMechanismKind Kind { get; set; }
        public string ChannelId { get; set; }
        public MemorySelector Source { get; set; }
        public MemoryEventKind Trigger { get; set; } = MemoryEventKind.Hit;
        public AttributionBudget Budget { get; set; } = AttributionBudget.PerActivation;
        public GimmickDef Gimmick { get; set; }
        public DirectedRechargeChannel Recharge { get; set; }
        public BridgeSuccessDefinition Bridge { get; set; }
        public MemoryPrimedDefinition Primed { get; set; }
        public RelayWindowDefinition Relay { get; set; }
        public AlliedWardDefinition Ward { get; set; }
        public PressureDividendChannel Dividend { get; set; }
        public int EveryN { get; set; } = 1;
        public bool Once { get; set; }
        public int[] ValuesByRank { get; set; } = Array.Empty<int>();
        public Dictionary<string, MemoryEventKind> TriggerByIdentity { get; set; } = new Dictionary<string, MemoryEventKind>(StringComparer.Ordinal);
        public string[] Replaces { get; set; } = Array.Empty<string>();
        public string[] RequiredMemories { get; set; } = Array.Empty<string>();
        public AuthoredMechanismCondition Condition { get; set; }
        public string PairId { get; set; }
        public ModShieldPoolKind ShieldPool { get; set; } = ModShieldPoolKind.Ordinary;
        public decimal? UncappedValueUnits { get; set; }
        public decimal? UncappedProbabilityUnits { get; set; }
        public decimal? UncappedDurationSeconds { get; set; }
        public decimal? UncappedRadiusMetres { get; set; }
        public int? UncappedTargetCount { get; set; }

        internal AuthoredMechanismSpec Copy()
        {
            // Composition replaces immutable payloads and arrays; only Gimmick is mutated in place.
            var copy = (AuthoredMechanismSpec)MemberwiseClone();
            copy.Gimmick = Gimmick?.Copy();
            return copy;
        }
    }

    public sealed class AuthoredMechanismEntry
    {
        public string StarId { get; set; }
        public string[] ContributorIds { get; set; } = Array.Empty<string>();
        public AuthoredMechanismSpec Spec { get; set; }
    }

    public static class AuthoredMechanisms
    {
        public static bool CanBeSource(MemorySelector source)
        {
            if (source == null) return true;
            if (source.Alternatives.Count > 0) return source.Alternatives.All(CanBeSource);
            return source.Kind != MemorySelectorKind.EquippedMovement && source.Kind != MemorySelectorKind.OtherNormal
                && (source.Kind != MemorySelectorKind.Memory || !source.Memory.StartsWith("St_M_", StringComparison.Ordinal));
        }
        public static string Key(AuthoredMechanismSpec spec) => AuthoredMechanismCodec.EncodeSpec(spec);
        public static string Describe(AuthoredMechanismSpec spec)
        {
            string Percent(decimal units) => (units / 100m).ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) + "%";
            string source = spec.Source?.Expression ?? SourceMemory(spec) ?? Loc.T("装備中の発火元", "equipped source");
            string effect = spec.Gimmick != null ? spec.Gimmick.Effect + " " + spec.Gimmick.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
                : spec.Recharge != null ? Loc.T("残りクールダウン短縮 ", "remaining cooldown reduction ") + Percent(spec.Recharge.EffectiveValueUnits) + " → " + spec.Recharge.Recipient.Expression
                : spec.Primed != null ? Loc.T("次の通常攻撃 ", "next basic attack ") + Percent(spec.Primed.ValueUnits) + " / " + spec.Primed.DurationSeconds + "s"
                : spec.Relay != null ? Loc.T("引継ぎダメージ ", "relay native damage ") + Percent(spec.Relay.ValueUnits) + " → " + spec.Relay.TargetMemory + " / " + spec.Relay.DurationSeconds + "s"
                : spec.Ward != null ? Loc.T("味方障壁 ", "recipient ward ") + Percent(spec.Ward.ValueUnits) + " (" + spec.Ward.AmountBasis + ", " + spec.Ward.PoolKind
                    + ", " + spec.Ward.RecipientKind + ") / " + spec.Ward.RadiusMetres + "m / " + spec.Ward.DurationSeconds + "s / " + spec.Ward.Targets + Loc.T("体", " targets")
                : spec.Dividend != null ? spec.Dividend.Describe()
                : spec.Bridge != null ? Loc.T("合わせ技：", "pair payoff: ") + spec.Bridge.PairId + " / " + spec.Bridge.GateKind + " / "
                    + string.Join(", ", new[] { spec.Bridge.BasePayoff }.Concat(spec.Bridge.Extras).Select(x => x.Kind + " " + Percent(x.ValueUnits)))
                : spec.Kind == AuthoredMechanismKind.SacrificeShield ? Loc.T("固有のHP支払いの50%を障壁へ（新規付与上限10%HP）", "convert 50% of qualified native HP payment to shield (new award cap 10% HP)")
                : Loc.T("装備中のQ/R由来の成功したスタンから障壁6%HP（3秒・間隔2秒）", "successful equipped native Q/R stun grants 6% HP shield (3s, 2s interval)");
            return source + " / " + spec.Trigger + ": " + effect + "\n" + spec.Budget
                + (spec.EveryN > 1 ? " / " + Loc.T("発火回数", "every notifications: ") + spec.EveryN : "")
                + (spec.Condition == AuthoredMechanismCondition.Always ? "" : " / " + spec.Condition + ":" + spec.PairId);
        }
        public static string DescribeKeystone(KeystoneDefinition definition, bool upside)
        {
            string Number(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string Transform(KeystoneTransform transform)
            {
                var scope = transform.Scope;
                string value = transform.Operation == KeystoneOperation.Scale ? Number(transform.MagnitudeUnits.Percent) + "%"
                    : transform.Operation == KeystoneOperation.RedistributeWound ? Number(transform.MagnitudeUnits.Percent) + "% / " + Number(transform.WoundDurationUnits.Percent) + "% lifetime"
                    : transform.Operation == KeystoneOperation.Disable ? ""
                    : transform.Operation == KeystoneOperation.Set || transform.Operation == KeystoneOperation.Add || transform.Operation == KeystoneOperation.SetSeconds
                        ? Number(transform.Seconds) : transform.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string targets = string.Join(" / ", scope.TargetMemorySet
                    .Concat(scope.TargetEffectSet.Select(x => x.ToString())).Concat(scope.TargetEffectIds)
                    .Concat(scope.SourceSelectors.Select(x => x.Expression))
                    .Concat(scope.ReceiverMemorySet.Select(x => "→" + x))
                    .Concat(scope.ReceiverSelectors.Select(x => "→" + x.Expression)));
                return transform.TargetLayer + " / " + scope.PayloadKind + " / " + targets
                    + (scope.Recipient == KeystoneRecipientKind.Any ? "" : " / " + scope.Recipient)
                    + (scope.SourceKind.HasValue ? " / " + scope.SourceKind.Value : "")
                    + (scope.Argument.HasValue ? " / Arg=" + scope.Argument.Value : "")
                    + " / " + transform.Field + ": " + transform.Operation + " " + value
                    + (transform.ExpectedFrom.HasValue ? " (from " + Number(transform.ExpectedFrom.Value) + ")" : "")
                    + (transform.Maximum.HasValue ? " (max " + Number(transform.Maximum.Value) + ")" : "");
            }
            return string.Join("\n", (upside ? definition.Upside : definition.Downside).Select(Transform)
                .Concat(upside ? definition.Grants.Select(Describe) : Enumerable.Empty<string>()));
        }
        internal static void ValidateBindings(IReadOnlyList<TalentDef> tree)
        {
            var all = tree.SelectMany(t => t.IsChoice ? t.Choices : new[] { t }).ToArray();
            foreach (var node in all)
            {
                if (node.KeystoneDefinition != null)
                    foreach (var grant in node.KeystoneDefinition.Grants)
                        if (grant.Kind == AuthoredMechanismKind.StunSourceFilter && node.Id != "h.cetus.key2"
                            || grant.Kind == AuthoredMechanismKind.SacrificeShield && node.Id != "h.aurena.key2")
                            throw new InvalidOperationException("Native keystone adapters belong only to their named selected key.");
                var spec = node.Mechanism ?? (FractionalScopedModifiers.RequiresMechanismRoute(node.EffectChannel) ? FromChannel(node.EffectChannel, node.Gimmick) : null);
                if (spec == null) continue;
                if (spec.Kind == AuthoredMechanismKind.StunSourceFilter || spec.Kind == AuthoredMechanismKind.SacrificeShield)
                    throw new InvalidOperationException("Native keystone adapters are selected-key grants, never ordinary notables.");
                foreach (string id in spec.Replaces)
                    if (!tree.Any(x => x.Id == id && x.HeroKey == node.HeroKey)) throw new InvalidOperationException("Unknown replaced star: " + id);
                if (spec.Condition != AuthoredMechanismCondition.Always)
                {
                    var baseNodes = all.Where(x => x.HeroKey == node.HeroKey && x.Mechanism?.Bridge?.PairId == spec.PairId).ToArray();
                    if (baseNodes.Length != 1) throw new InvalidOperationException("A bridge condition requires one explicit typed base binding: " + node.Id);
                }
                if (spec.Bridge != null)
                {
                    var pair = PairCombos.Get(spec.Bridge.PairId);
                    if (pair == null && (node.AuthoredStar?.Region.Kind != ClusterRegionKind.Bridge || !Gimmicks.ValidStarId(node.AuthoredStar.Region.Id)))
                        throw new InvalidOperationException("A new real pair requires its explicit authored bridge region.");
                    if (pair != null && (pair.HeroKey != node.HeroKey || !spec.Bridge.Endpoints.Any(x => x.StarId == pair.StarA && x.Memory == pair.RouteA)
                        || !spec.Bridge.Endpoints.Any(x => x.StarId == pair.StarB && x.Memory == pair.RouteB)))
                        throw new InvalidOperationException("Typed bridge endpoints must match its registered real pair.");
                    foreach (var endpoint in spec.Bridge.Endpoints)
                        if (!tree.Any(x => x.HeroKey == node.HeroKey && x.Id == endpoint.StarId && x.RouteMemory == endpoint.Memory)
                            || !tree.Any(x => x.HeroKey == node.HeroKey && x.RouteMemory == endpoint.Memory && x.RouteId != null))
                            throw new InvalidOperationException("Authored real pair requires verified hero route endpoint stars.");
                }
            }
        }
        public static AuthoredMechanismSpec FromChannel(EffectChannelDef channel, GimmickDef effect)
        {
            MemoryEventKind Trigger(GimmickTrigger trigger)
            {
                switch (trigger)
                {
                    case GimmickTrigger.OnUse: return MemoryEventKind.ConfirmedUse;
                    case GimmickTrigger.OnKill: return MemoryEventKind.Kill;
                    case GimmickTrigger.OnCrit: return MemoryEventKind.CriticalHit;
                    default: return MemoryEventKind.Hit;
                }
            }
            var budget = channel.ActivationBudget == "notification" ? AttributionBudget.PerActivation
                : (AttributionBudget)Enum.Parse(typeof(AttributionBudget), channel.ActivationBudget, false);
            string pair = channel.PairSuccessId == null ? null : (PairCombos.Get(channel.PairSuccessId) ?? PairCombos.ForBridge(channel.PairSuccessId))?.Id;
            var spec = new AuthoredMechanismSpec { ChannelId = channel.ChannelId,
                Source = new MemorySelector(MemorySelectorKind.Memory, channel.SourceMemory), Trigger = Trigger(effect.Trigger),
                Budget = budget, RequiredMemories = channel.EquipmentRequirements.ToArray(), PairId = pair,
                Condition = pair == null ? AuthoredMechanismCondition.Always : AuthoredMechanismCondition.BridgeSuccess };
            if (effect.Effect == GimmickEffect.Recharge || channel.SourceMemory != channel.ReceiverMemory)
            {
                if (effect.Effect != GimmickEffect.Recharge || effect.Cooldown != 0) throw new InvalidOperationException("Cross-memory receiver requires the directed recharge engine.");
                spec.Kind = AuthoredMechanismKind.DirectedRecharge;
                spec.Recharge = new DirectedRechargeChannel(channel.ChannelId, spec.Source, spec.Trigger,
                    new MemorySelector(MemorySelectorKind.Memory, channel.ReceiverMemory), new[] { effect.Value * 100m }, budget);
            }
            else { spec.Kind = AuthoredMechanismKind.Gimmick; spec.Gimmick = effect; }
            return spec;
        }

        public static void Validate(AuthoredMechanismSpec spec)
        {
            if (spec == null || !Enum.IsDefined(typeof(AuthoredMechanismKind), spec.Kind) || !Gimmicks.ValidStarId(spec.ChannelId)
                || !CanBeSource(spec.Source) || !Enum.IsDefined(typeof(MemoryEventKind), spec.Trigger)
                || !Enum.IsDefined(typeof(AttributionBudget), spec.Budget) || !Enum.IsDefined(typeof(AuthoredMechanismCondition), spec.Condition)
                || !Enum.IsDefined(typeof(ModShieldPoolKind), spec.ShieldPool) || spec.EveryN < 1 || spec.EveryN > 10000
                || spec.ValuesByRank == null || spec.ValuesByRank.Length > StarProgression.MaxSpendablePoints
                || spec.ValuesByRank.Any(x => x <= 0) || spec.TriggerByIdentity == null || spec.TriggerByIdentity.Count > 64
                || spec.Replaces == null || spec.Replaces.Length > StarProgression.MaxSpendablePoints
                || spec.Replaces.Any(x => !Gimmicks.ValidStarId(x)) || spec.Replaces.Distinct(StringComparer.Ordinal).Count() != spec.Replaces.Length
                || (spec.Condition == AuthoredMechanismCondition.Always) != (spec.PairId == null)
                || spec.PairId != null && !Gimmicks.ValidStarId(spec.PairId))
                throw new InvalidOperationException("Invalid typed authored mechanism.");
            if (spec.UncappedValueUnits.HasValue && spec.UncappedValueUnits <= 0
                || spec.UncappedProbabilityUnits.HasValue && spec.UncappedProbabilityUnits < 0)
                throw new InvalidOperationException("Invalid exact pre-cap mechanism coefficient.");
            if (spec.UncappedDurationSeconds.HasValue && spec.UncappedDurationSeconds < 0
                || spec.UncappedRadiusMetres.HasValue && spec.UncappedRadiusMetres < 0
                || spec.UncappedTargetCount.HasValue && spec.UncappedTargetCount < 0)
                throw new InvalidOperationException("Invalid exact pre-cap mechanism parameter.");
            if (spec.Source != null) MemorySelector.Parse(spec.Source.Expression);
            if (spec.RequiredMemories == null || spec.RequiredMemories.Length > 64 || spec.RequiredMemories.Any(x => !Links.IsMemory(x))
                || spec.RequiredMemories.Distinct(StringComparer.Ordinal).Count() != spec.RequiredMemories.Length)
                throw new InvalidOperationException("Invalid mechanism equipment predicates.");
            foreach (var trigger in spec.TriggerByIdentity)
                if (!Links.IsMemory(trigger.Key) || !Enum.IsDefined(typeof(MemoryEventKind), trigger.Value))
                    throw new InvalidOperationException("Invalid identity trigger.");
            MechanismAdmission.ValidateBudgetTrigger(spec.Budget, spec.Trigger);
            int count = (spec.Gimmick == null ? 0 : 1) + (spec.Recharge == null ? 0 : 1) + (spec.Bridge == null ? 0 : 1)
                + (spec.Primed == null ? 0 : 1) + (spec.Relay == null ? 0 : 1) + (spec.Ward == null ? 0 : 1) + (spec.Dividend == null ? 0 : 1);
            bool flag = spec.Kind == AuthoredMechanismKind.SacrificeShield || spec.Kind == AuthoredMechanismKind.StunSourceFilter;
            if (count != (flag ? 0 : 1)) throw new InvalidOperationException("Mechanism requires exactly its typed payload.");
            bool correct = flag || spec.Kind == AuthoredMechanismKind.Gimmick && spec.Gimmick != null
                || spec.Kind == AuthoredMechanismKind.DirectedRecharge && spec.Recharge != null
                || spec.Kind == AuthoredMechanismKind.BridgeSuccess && spec.Bridge != null
                || spec.Kind == AuthoredMechanismKind.MemoryPrimed && spec.Primed != null
                || spec.Kind == AuthoredMechanismKind.RelayWindow && spec.Relay != null
                || spec.Kind == AuthoredMechanismKind.AlliedWard && spec.Ward != null
                || spec.Kind == AuthoredMechanismKind.PressureDividend && spec.Dividend != null;
            if (!correct) throw new InvalidOperationException("Mechanism kind does not match its payload.");
            if (spec.Gimmick != null && !Gimmicks.ValidDef(spec.Gimmick)) throw new InvalidOperationException("Invalid mechanism gimmick.");
            if (spec.Recharge != null && (!CanBeSource(spec.Recharge.Source) || spec.Recharge.ChannelId != spec.ChannelId)) throw new InvalidOperationException("Invalid recharge source/channel.");
            if (spec.Recharge != null && spec.EveryN != 1 && spec.Recharge.EveryN != 1 && spec.EveryN != spec.Recharge.EveryN)
                throw new InvalidOperationException("A recharge channel cannot declare two different cadences.");
            if (spec.Primed != null && (!Links.IsMemory(spec.Primed.SourceMemory) || spec.Primed.SourceMemory.StartsWith("St_M_", StringComparison.Ordinal)
                || spec.Primed.ChannelId != spec.ChannelId)) throw new InvalidOperationException("Invalid preparation source/channel.");
            if (spec.Relay != null && spec.Relay.ChannelId != spec.ChannelId || spec.Ward != null && spec.Ward.ChannelId != spec.ChannelId)
                throw new InvalidOperationException("Inconsistent payload channel.");
            if (spec.Bridge != null && (!Gimmicks.ValidStarId(spec.Bridge.PairId) || !CanBeSource(spec.Bridge.OpeningSource) || !CanBeSource(spec.Bridge.PayoffSource)))
                throw new InvalidOperationException("Invalid typed real pair.");
        }
        public static int EffectiveEveryN(AuthoredMechanismSpec spec) => spec.EveryN != 1 ? spec.EveryN : spec.Recharge?.EveryN ?? 1;
        public static int ChannelCount(AuthoredMechanismSpec spec) => spec.Bridge == null ? 1
            : checked(1 + spec.Bridge.Extras.Count + (spec.Bridge.GateKind == BridgeGateKind.Mark ? 1 : 0));
        public static int EffectiveChannelCount(Build build)
        {
            int count = build.Gimmicks.Count;
            foreach (var entry in build.Mechanisms) count = checked(count + ChannelCount(entry.Spec));
            return count;
        }

        internal static GimmickEffect Effect(AuthoredMechanismSpec s) => s.Gimmick?.Effect
            ?? (s.Recharge != null ? GimmickEffect.Recharge : s.Primed != null ? GimmickEffect.Primed
            : s.Ward != null || s.Kind == AuthoredMechanismKind.SacrificeShield ? GimmickEffect.Shield : GimmickEffect.None);
        internal static string SourceMemory(AuthoredMechanismSpec s) => s.Primed?.SourceMemory
            ?? (s.Relay != null ? RelayWindowDefinition.SourceMemory : s.Dividend?.SourceMemory)
            ?? s.Recharge?.Source.Memory ?? s.Source?.Memory;
        internal static string ReceiverMemory(AuthoredMechanismSpec s) => s.Recharge?.Recipient.Memory ?? s.Relay?.TargetMemory ?? SourceMemory(s);
        internal static bool Matches(ScopedModifierDef modifier, AuthoredMechanismEntry entry)
        {
            var s = entry.Spec;
            string memory = modifier.ScopeKind == ScopeKind.Receiver ? ReceiverMemory(s) : SourceMemory(s);
            return (memory == modifier.ScopeMemory || memory == null && modifier.TargetEffectIds.Length > 0)
                && (modifier.TargetEffectIds.Length == 0 || entry.ContributorIds.Any(modifier.TargetEffectIds.Contains))
                && (modifier.TargetEffects.Length == 0 || modifier.TargetEffects.Contains(Effect(s)));
        }
        internal static bool Supports(AuthoredMechanismSpec s, GimmickParam param)
        {
            if (s.Gimmick != null) return Gimmicks.SupportsParameter(s.Gimmick, param);
            if (s.Bridge != null)
            {
                bool SupportsPayload(BridgePayload payload) => payload.Gimmick != null
                    ? Gimmicks.SupportsParameter(payload.Gimmick, param)
                    : payload.Kind == BridgePayloadKind.OrdinaryShield && param == GimmickParam.Duration;
                return SupportsPayload(s.Bridge.BasePayoff) || s.Bridge.Extras.Any(SupportsPayload);
            }
            switch (param)
            {
                case GimmickParam.Duration: return s.Primed != null || s.Relay != null || s.Ward != null;
                case GimmickParam.Radius: case GimmickParam.ExtraTargets: return s.Ward != null;
                case GimmickParam.Chance: return s.Recharge != null || s.Dividend != null;
                default: return false;
            }
        }

        private static decimal BaseValue(AuthoredMechanismSpec s) => s.UncappedValueUnits
            ?? (s.Gimmick != null ? (s.Gimmick.UncappedValue ?? s.Gimmick.EffectiveValueOrAuthored) * 100m
            : s.Recharge?.EffectiveValueUnits ?? s.Primed?.ValueUnits ?? s.Relay?.ValueUnits ?? s.Ward?.ValueUnits ?? s.Dividend?.ProbabilityUnits ?? 1m);
        private static void SetValue(AuthoredMechanismSpec s, decimal value, IEnumerable<string> contributors = null)
        {
            s.UncappedValueUnits = value;
            if (s.Gimmick != null)
            {
                decimal bounded = Math.Min(Gimmicks.Cap(s.Gimmick.Effect), value / 100m);
                s.Gimmick.ValuePrecise = Math.Max(1L, checked((long)decimal.Floor(bounded * Gimmicks.PreciseValueScale)));
                s.Gimmick.UncappedValue = value / 100m;
                s.Gimmick.EffectiveValue = bounded;
            }
            else if (s.Recharge != null)
            {
                var c = s.Recharge;
                s.Recharge = new DirectedRechargeChannel(c.ChannelId, c.Source, c.SourceTrigger, c.Recipient, new[] { value },
                    c.Budget, c.ProbabilityUnits, c.EveryN, c.Condition, c.RequiredElementTypes, 0, c.ModifierScope, c.CapUnits);
            }
            else if (s.Primed != null) s.Primed = new MemoryPrimedDefinition(s.Primed.ChannelId, s.Primed.SourceMemory, s.Primed.Trigger,
                Math.Min(12000, value), s.Primed.DurationSeconds, s.Primed.Budget);
            else if (s.Relay != null) s.Relay = new RelayWindowDefinition(s.Relay.ChannelId, s.Relay.TargetMemory, Math.Min(4000, value), s.Relay.DurationModifierUnits, s.Relay.QuietRelay);
            else if (s.Ward != null)
            {
                var w = s.Ward;
                s.Ward = new AlliedWardDefinition(w.ChannelId, w.RecipientKind, w.AmountBasis, w.PoolKind, Math.Min(10000, value),
                    w.IncludeOwner, w.RadiusMetres, w.DurationSeconds, w.BaseTargets, w.Targets - w.BaseTargets, w.MaxTargets, w.Limits, w.Budget);
            }
            else if (s.Dividend != null) s.Dividend = PressureDividendChannel.FromEffective(s.Dividend.SourceMemory, s.Dividend.RequiredMemories,
                contributors ?? new[] { s.ChannelId }, Math.Min(4000, value));
        }
        internal static void Compose(Build build, IReadOnlyList<KeyValuePair<TalentDef, int>> selected)
        {
            var replaced = new HashSet<string>(StringComparer.Ordinal);
            List<KeyValuePair<TalentDef, int>> modifiers = null;
            foreach (var row in selected)
            {
                if (row.Key.Mechanism != null) replaced.UnionWith(row.Key.Mechanism.Replaces);
                if (row.Key.ScopedModifier == null) continue;
                if (modifiers == null) modifiers = new List<KeyValuePair<TalentDef, int>>();
                modifiers.Add(row);
            }
            IReadOnlyList<KeyValuePair<TalentDef, int>> appliedModifiers = modifiers
                ?? (IReadOnlyList<KeyValuePair<TalentDef, int>>)Array.Empty<KeyValuePair<TalentDef, int>>();
            build.Gimmicks.RemoveAll(e => replaced.Contains(e.StarId));
            var groups = new Dictionary<string, (AuthoredMechanismEntry Entry, decimal Value, string Signature)>(StringComparer.Ordinal);
            foreach (var row in selected)
            {
                var authored = row.Key.Mechanism ?? (FractionalScopedModifiers.RequiresMechanismRoute(row.Key.EffectChannel)
                    ? FromChannel(row.Key.EffectChannel, row.Key.Gimmick) : null);
                if (authored == null || replaced.Contains(row.Key.Id)) continue;
                var entry = new AuthoredMechanismEntry { StarId = row.Key.Id, ContributorIds = new[] { row.Key.Id }, Spec = authored.Copy() };
                var spec = entry.Spec;
                if (spec.Dividend != null) spec.ChannelId = "dividend." + StarClusters.RegistryHash(spec.Dividend.ConditionKey);
                if (spec.ValuesByRank.Length > 0 && row.Value > spec.ValuesByRank.Length) throw new InvalidOperationException("Missing authored rank value.");
                decimal value = spec.ValuesByRank.Length == 0 ? BaseValue(spec) * row.Value : spec.ValuesByRank[row.Value - 1];
                if (spec.Bridge != null) ComposeEntry(entry, row.Value, Array.Empty<KeyValuePair<TalentDef, int>>());
                if (groups.TryGetValue(spec.ChannelId, out var group))
                {
                    if (spec.Bridge != null) throw new InvalidOperationException("Conflicting authored channel definitions: " + spec.ChannelId);
                    string signature = Signature(spec);
                    if ((group.Signature ?? Signature(group.Entry.Spec)) != signature)
                        throw new InvalidOperationException("Conflicting authored channel definitions: " + spec.ChannelId);
                    group.Entry.ContributorIds = group.Entry.ContributorIds.Concat(entry.ContributorIds).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    if (StringComparer.Ordinal.Compare(entry.StarId, group.Entry.StarId) < 0) group.Entry.StarId = entry.StarId;
                    groups[spec.ChannelId] = (group.Entry, checked(group.Value + value), signature);
                }
                else groups.Add(spec.ChannelId, (entry, value, null));
                if (entry.Spec.Bridge != null)
                    foreach (var endpoint in entry.Spec.Bridge.Endpoints)
                    {
                        int rank = 0;
                        foreach (var allocation in selected) if (allocation.Key.Id == endpoint.StarId) rank = allocation.Value;
                        build.MechanismEndpointRanks[endpoint.StarId] = rank;
                    }
            }
            foreach (var group in groups.Values)
            {
                if (group.Entry.Spec.Bridge == null) SetValue(group.Entry.Spec, group.Value, group.Entry.ContributorIds);
                ComposeEntry(group.Entry, 1, appliedModifiers, false);
                build.Mechanisms.Add(group.Entry);
            }
            var admittedPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in build.Mechanisms)
                if (entry.Spec.Bridge != null) admittedPairs.Add(entry.Spec.Bridge.PairId);
            build.Mechanisms.RemoveAll(entry => entry.Spec.Condition != AuthoredMechanismCondition.Always
                && !admittedPairs.Contains(entry.Spec.PairId));
            foreach (var entry in build.Mechanisms) if (entry.Spec.Bridge != null)
                build.PairCombos.RemoveAll(e => e.Def.Id == entry.Spec.Bridge.PairId);
        }

        private static string Signature(AuthoredMechanismSpec spec)
        {
            var normalized = spec.Copy();
            SetValue(normalized, 1m);
            return Key(normalized);
        }

        private static void ComposeEntry(AuthoredMechanismEntry entry, int rank, IReadOnlyList<KeyValuePair<TalentDef, int>> selected, bool applyRankValues = true)
        {
            var s = entry.Spec;
            s.EveryN = EffectiveEveryN(s);
            long boost = 0, duration = 0, radius = 0, chance = 0, targets = 0;
            bool sourceBoost = false, receiverBoost = false;
            var caps = new Dictionary<int, ScopedModifierCapProfile>();
            foreach (var row in selected)
            {
                var t = row.Key;
                var m = t.ScopedModifier;
                if (m == null || !Matches(m, entry)) continue;
                if (m.Param.HasValue && !Supports(s, m.Param.Value)) throw new InvalidOperationException("Unsupported mechanism parameter: " + t.Id);
                if (m.CapProfileId != null)
                {
                    var cap = FractionalScopedModifiers.ScopedCapProfile(m.CapProfileId);
                    int field = m.Param.HasValue ? (int)m.Param.Value : -1;
                    if (caps.TryGetValue(field, out var old) && old.Id != cap.Id) throw new InvalidOperationException("Contradictory mechanism cap profiles.");
                    caps[field] = cap;
                }
                long amount = (long)m.Amount.Units * row.Value;
                switch (m.Param)
                {
                    case GimmickParam.Duration: duration += amount; break;
                    case GimmickParam.Radius: radius += amount; break;
                    case GimmickParam.Chance: chance += (long)m.Probability.Units * row.Value; break;
                    case GimmickParam.ExtraTargets: targets += (long)m.ExtraTargets * row.Value; break;
                    default: boost += amount; sourceBoost |= m.ScopeKind != ScopeKind.Receiver; receiverBoost |= m.ScopeKind == ScopeKind.Receiver; break;
                }
            }
            if (sourceBoost && receiverBoost) throw new InvalidOperationException("Source/receiver double boost is forbidden.");
            if (caps.TryGetValue(-1, out var bc)) boost = Math.Min(boost, bc.MaximumModifier.Units);
            if (caps.TryGetValue((int)GimmickParam.Duration, out var dc)) duration = Math.Min(duration, dc.MaximumModifier.Units);
            if (caps.TryGetValue((int)GimmickParam.Radius, out var rc)) radius = Math.Min(radius, rc.MaximumModifier.Units);
            if (caps.TryGetValue((int)GimmickParam.Chance, out var cc)) chance = Math.Min(chance, cc.MaximumProbability.Units);
            if (caps.TryGetValue((int)GimmickParam.ExtraTargets, out var tc)) targets = Math.Min(targets, tc.MaximumTargets);
            decimal factor = 1m + boost / 10000m;
            decimal Raw(decimal value) => (applyRankValues && s.ValuesByRank.Length > 0
                ? s.ValuesByRank[rank - 1] : checked(value * rank)) * factor;
            decimal Value(decimal value, int maximum)
            {
                decimal raw = Raw(s.Bridge == null ? s.UncappedValueUnits ?? value : value);
                if (s.Bridge == null) s.UncappedValueUnits = raw;
                return Math.Min(maximum, raw);
            }
            if (applyRankValues && s.ValuesByRank.Length > 0 && rank > s.ValuesByRank.Length) throw new InvalidOperationException("Missing rank value.");
            if (s.Gimmick != null)
            {
                s.Gimmick.ValuePrecise = applyRankValues && s.ValuesByRank.Length > 0 ? checked((long)s.ValuesByRank[rank - 1] * 100000) : checked(s.Gimmick.ValuePrecise * rank);
                s.UncappedValueUnits = Raw(s.UncappedValueUnits ?? s.Gimmick.Value * 100m);
                s.UncappedProbabilityUnits = (s.Gimmick.UncappedChanceUnits ?? s.Gimmick.ChanceUnits) + chance;
                s.UncappedDurationSeconds = Gimmicks.SupportsParameter(s.Gimmick, GimmickParam.Duration)
                    ? AuthoredKeystoneComposer.GimmickDurationBase(s.Gimmick, s.UncappedValueUnits / 100m)
                        * (1m + ((s.Gimmick.UncappedDurationUnits ?? s.Gimmick.DurationUnits) + duration) / 10000m) : 0;
                s.UncappedRadiusMetres = Gimmicks.SupportsParameter(s.Gimmick, GimmickParam.Radius)
                    ? AuthoredKeystoneComposer.GimmickRadiusBase(s.Gimmick) * (1m + ((s.Gimmick.UncappedRadiusUnits ?? s.Gimmick.RadiusUnits) + radius) / 10000m) : 0;
                s.UncappedTargetCount = Gimmicks.SupportsParameter(s.Gimmick, GimmickParam.ExtraTargets)
                    ? checked((s.Gimmick.Effect == GimmickEffect.Ricochet ? Math.Max(1, Math.Min(2, s.Gimmick.Arg)) : 5)
                        + (int)(s.Gimmick.UncappedExtraTargets ?? s.Gimmick.ExtraTargets) + (int)targets) : 0;
                s.Gimmick = Gimmicks.ApplyModifierUnits(s.Gimmick, boost, duration, radius, targets, chance);
            }
            else if (s.Recharge != null)
            {
                var c = s.Recharge;
                s.UncappedProbabilityUnits = c.ProbabilityUnits + chance;
                s.Recharge = new DirectedRechargeChannel(c.ChannelId, c.Source, c.SourceTrigger, c.Recipient, new[] { Value(c.ValueUnits, c.CapUnits) },
                    c.Budget, Math.Min(10000, c.ProbabilityUnits + chance), s.EveryN, c.Condition, c.RequiredElementTypes, 0, c.ModifierScope, c.CapUnits);
            }
            else if (s.Primed != null)
            {
                var p = s.Primed;
                s.UncappedDurationSeconds = (decimal)p.DurationSeconds * (1m + duration / 10000m);
                s.Primed = new MemoryPrimedDefinition(p.ChannelId, p.SourceMemory, p.Trigger, Value(p.ValueUnits, 12000),
                    Math.Min(10, p.DurationSeconds * (1 + duration / 10000f)), p.Budget);
            }
            else if (s.Relay != null)
            {
                var r = s.Relay;
                s.UncappedDurationSeconds = r.EffectiveDurationSeconds.HasValue ? (decimal)r.DurationSeconds * (1m + duration / 10000m)
                    : (r.QuietRelay ? 8m : 4m) * (1m + (r.DurationModifierUnits + duration) / 10000m);
                s.Relay = new RelayWindowDefinition(r.ChannelId, r.TargetMemory, Value(r.ValueUnits, 4000),
                    (int)Math.Min(10000, r.DurationModifierUnits + duration), r.QuietRelay);
            }
            else if (s.Ward != null)
            {
                var w = s.Ward;
                s.UncappedDurationSeconds = (decimal)w.DurationSeconds * (1m + duration / 10000m);
                s.UncappedRadiusMetres = (decimal)w.RadiusMetres * (1m + radius / 10000m);
                s.UncappedTargetCount = checked(w.Targets + (int)targets);
                s.Ward = new AlliedWardDefinition(w.ChannelId, w.RecipientKind, w.AmountBasis, w.PoolKind, Value(w.ValueUnits, 10000), w.IncludeOwner,
                    Math.Min(15, w.RadiusMetres * (1 + radius / 10000f)), Math.Min(w.Limits == WardLimitProfile.SummonRecipientHealth ? 9 : 8,
                    w.DurationSeconds * (1 + duration / 10000f)), w.BaseTargets, checked(w.Targets - w.BaseTargets + (int)targets), w.MaxTargets, w.Limits, w.Budget);
            }
            else if (s.Dividend != null)
            {
                var d = s.Dividend;
                Value(d.ProbabilityUnits, 4000);
                s.UncappedValueUnits += chance;
                s.UncappedProbabilityUnits = s.UncappedValueUnits;
                decimal value = Math.Min(4000, s.UncappedValueUnits.Value);
                s.Dividend = PressureDividendChannel.FromEffective(d.SourceMemory, d.RequiredMemories, entry.ContributorIds, value);
            }
            else if (s.Bridge != null)
            {
                var b = s.Bridge;
                BridgePayload Payload(BridgePayload p)
                {
                    decimal raw = (p == b.BasePayoff && applyRankValues && s.ValuesByRank.Length > 0 ? s.ValuesByRank[rank - 1]
                        : (p.UncappedValueUnits ?? (p.Gimmick != null ? (p.Gimmick.UncappedValue ?? p.Gimmick.EffectiveValueOrAuthored) * 100m : p.ValueUnits)) * rank) * factor;
                    decimal? rawProbability = p.Gimmick == null ? p.UncappedProbabilityUnits : (p.Gimmick.UncappedChanceUnits ?? p.Gimmick.ChanceUnits) + chance;
                    decimal? rawDuration = p.Gimmick != null && Gimmicks.SupportsParameter(p.Gimmick, GimmickParam.Duration)
                        ? AuthoredKeystoneComposer.GimmickDurationBase(p.Gimmick, raw / 100m) * (1m + ((p.Gimmick.UncappedDurationUnits ?? p.Gimmick.DurationUnits) + duration) / 10000m)
                        : p.DurationSeconds > 0 ? (p.UncappedDurationSeconds ?? (decimal)p.DurationSeconds) * (1m + duration / 10000m) : (decimal?)null;
                    decimal? rawRadius = p.Gimmick != null && Gimmicks.SupportsParameter(p.Gimmick, GimmickParam.Radius)
                        ? AuthoredKeystoneComposer.GimmickRadiusBase(p.Gimmick) * (1m + ((p.Gimmick.UncappedRadiusUnits ?? p.Gimmick.RadiusUnits) + radius) / 10000m) : (decimal?)null;
                    int? rawTargets = p.Gimmick != null && Gimmicks.SupportsParameter(p.Gimmick, GimmickParam.ExtraTargets)
                        ? checked((p.Gimmick.Effect == GimmickEffect.Ricochet ? Math.Max(1, Math.Min(2, p.Gimmick.Arg)) : 5)
                            + (int)(p.Gimmick.UncappedExtraTargets ?? p.Gimmick.ExtraTargets) + (int)targets) : (int?)null;
                    return BridgePayload.FromEffective(p.ChannelId, p.Kind, Math.Min(p.CapUnits, raw),
                        p.Recipient, p.DamageBasis, p.DurationSeconds == 0 ? 0 : (float)Math.Min(p.DurationCapSeconds, rawDuration.Value),
                        p.Gimmick == null ? null : Gimmicks.ApplyModifierUnits(p.Gimmick, boost, duration, radius, targets, chance),
                        raw, rawProbability, p.CapUnits, rawDuration, rawRadius, rawTargets, p.DurationCapSeconds);
                }
                s.Bridge = new BridgeSuccessDefinition(b.PairId, b.Endpoints, applyRankValues ? rank : b.Rank, b.GateKind, b.OpeningSource, b.OpeningTrigger,
                    b.PayoffSource, b.PayoffTrigger, Payload(b.BasePayoff), b.Extras.Select(Payload), b.Budget, b.SourcePhase, b.UsesNativeWindowLifetime, b.CooldownSeconds, b.WindowSeconds);
            }
            Validate(s);
        }
        private static bool ProjectionSelectorMatches(MemorySelector selector, string memory, MechanismMemorySlot? slot)
        {
            if (selector == null) return true;
            if (selector.Alternatives.Count > 0)
            {
                foreach (var alternative in selector.Alternatives)
                    if (ProjectionSelectorMatches(alternative, memory, slot)) return true;
                return false;
            }
            if (selector.AllowedMemories.Count > 0 && !selector.AllowedMemories.Contains(memory)) return false;
            switch (selector.Kind)
            {
                case MemorySelectorKind.Memory: return selector.Memory == memory;
                case MemorySelectorKind.EquippedIdentity: return slot == MechanismMemorySlot.Identity;
                case MemorySelectorKind.EquippedQ: return slot == MechanismMemorySlot.Q;
                case MemorySelectorKind.EquippedR: return slot == MechanismMemorySlot.R;
                case MemorySelectorKind.EquippedQOrR: return slot == MechanismMemorySlot.Q || slot == MechanismMemorySlot.R;
                default: return false;
            }
        }

        private static IEnumerable<(string Memory, MechanismMemorySlot? Slot)> ProjectionScopes(MemorySelector selector,
            string fallback = null, string heroKey = null, string sourceMemory = null)
        {
            if (selector == null && fallback == null) { yield return (null, null); yield break; }
            if (selector?.Alternatives.Count > 0)
            {
                foreach (var alternative in selector.Alternatives)
                    foreach (var scope in ProjectionScopes(alternative, fallback, heroKey, sourceMemory)) yield return scope;
                yield break;
            }
            MechanismMemorySlot? slot = selector?.Kind == MemorySelectorKind.EquippedIdentity ? MechanismMemorySlot.Identity
                : selector?.Kind == MemorySelectorKind.EquippedQ ? MechanismMemorySlot.Q
                : selector?.Kind == MemorySelectorKind.EquippedR ? MechanismMemorySlot.R
                : selector?.Kind == MemorySelectorKind.EquippedMovement ? MechanismMemorySlot.Movement : (MechanismMemorySlot?)null;
            IEnumerable<string> memories = selector?.Kind == MemorySelectorKind.Memory ? new[] { selector.Memory }
                : selector?.AllowedMemories.Count > 0 ? selector.AllowedMemories
                : fallback != null ? new[] { fallback } : VerifiedMechanismSlots.ForHero(heroKey);
            foreach (string memory in memories)
            {
                if (fallback != null && memory != fallback) continue;
                if (selector?.Kind == MemorySelectorKind.OtherNormal
                    && (memory == sourceMemory || !VerifiedMechanismSlots.TryGetCategory(heroKey, memory, out var category)
                        || category != VerifiedMechanismMemoryCategory.Normal)) continue;
                foreach (var actualSlot in VerifiedMechanismSlots.ForMemory(heroKey, memory))
                    if ((!slot.HasValue || actualSlot == slot)
                        && (selector?.Kind != MemorySelectorKind.OtherNormal
                            || actualSlot != MechanismMemorySlot.Identity && actualSlot != MechanismMemorySlot.Movement))
                        yield return (memory, actualSlot);
            }
        }
        private static bool StrongestEffect(GimmickEffect effect) => effect == GimmickEffect.Expose || effect == GimmickEffect.Sap
            || effect == GimmickEffect.Wound || effect == GimmickEffect.Weakspot || effect == GimmickEffect.Quicken
            || effect == GimmickEffect.Empower || effect == GimmickEffect.Primed || effect == GimmickEffect.Crescendo;
        internal static bool GeneratedDamageEffect(GimmickEffect effect) => effect == GimmickEffect.Burst || effect == GimmickEffect.Echo
            || effect == GimmickEffect.Wound || effect == GimmickEffect.Ricochet || effect == GimmickEffect.Primed;

        private static IEnumerable<EffectiveAllocationChannel> ProjectPayload(AuthoredMechanismEntry entry, Build build,
            KeystonePayload payload, MemorySelector sourceSelector, MemorySelector receiverSelector = null, string sourceFallback = null,
            string receiverFallback = null, KeystoneRecipientKind recipient = KeystoneRecipientKind.Self, string discriminator = null,
            float cooldown = 0, MemoryEventKind? trigger = null, string heroKey = null)
        {
            var spec = entry.Spec;
            if (sourceSelector == null && sourceFallback == null) sourceSelector = MemorySelector.Parse("@ID|@Q|@R");
            foreach (var source in ProjectionScopes(sourceSelector, sourceFallback, heroKey))
                foreach (var receiver in ProjectionScopes(receiverSelector, receiverFallback, heroKey, source.Memory))
                {
                    if (source.Slot == MechanismMemorySlot.Movement) continue;
                    if (spec.Recharge != null && !ProjectionSelectorMatches(spec.Recharge.Source, source.Memory, source.Slot)) continue;
                    if (receiver.Slot.HasValue && source.Slot.HasValue
                        && (receiver.Slot == source.Slot && receiver.Memory != source.Memory
                            || receiver.Memory == source.Memory && receiver.Slot != source.Slot)) continue;
                    var final = AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, source.Memory, receiver.Memory,
                        recipient: recipient, sourceSlot: source.Slot, recipientSlot: receiver.Slot, heroKey: heroKey);
                    if (final.Disabled) continue;
                    decimal value = final.Value;
                    if (GeneratedDamageEffect(payload.Effect) || payload.Kind == KeystonePayloadKind.BridgeSuccess)
                    {
                        var generated = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                            new KeystonePayload(KeystoneLayer.GeneratedDamage, value, new KeystoneCaps(decimal.MaxValue),
                                effect: payload.Effect, effectId: payload.EffectId), source.Memory, receiver.Memory,
                            sourceKind: KeystoneSourceKind.Generated, recipient: recipient, sourceSlot: source.Slot, recipientSlot: receiver.Slot, heroKey: heroKey);
                        if (generated.Disabled) continue;
                        value = generated.Value;
                    }
                    string scenario = source.Memory + "@" + source.Slot + ">" + receiver.Memory + "@" + receiver.Slot;
                    string predicate = "mechanism:" + (int)spec.Kind + ":" + (int)(trigger ?? spec.Trigger) + ":" + payload.Effect
                        + ":" + final.Argument + ":" + spec.Condition + ":" + spec.PairId + ":" + scenario;
                    bool strongest = StrongestEffect(payload.Effect) && spec.Kind != AuthoredMechanismKind.DirectedRecharge;
                    yield return new EffectiveAllocationChannel
                    {
                        Key = strongest ? predicate : predicate + ":" + (discriminator ?? spec.ChannelId),
                        PredicateKey = predicate, Strongest = strongest, Cooldown = cooldown, Effect = payload.Effect,
                        StarId = entry.StarId, ContributorIds = entry.ContributorIds, Memory = source.Memory,
                        ValueMilli = value * BuildPrecision.Scale / final.EveryN,
                        ValueCeiling = (long)Math.Min(long.MaxValue, payload.Caps.Value * BuildPrecision.Scale),
                        DurationUnits = final.DurationSeconds * 100m, RadiusUnits = final.RadiusMetres * 100m,
                        ExtraTargets = final.TargetCount, ChanceUnits = final.ProbabilityPercent * 100m,
                    };
                }
        }

        internal static IEnumerable<EffectiveAllocationChannel> EffectiveChannels(AuthoredMechanismEntry entry, Build build, string heroKey = null)
        {
            var s = entry.Spec;
            if (s.Bridge != null)
            {
                var bridge = s.Bridge;
                if (bridge.GateKind == BridgeGateKind.Mark)
                {
                    var expose = new KeystonePayload(KeystoneLayer.ModEffect, bridge.Rank + 1, new KeystoneCaps(100),
                        KeystonePayloadKind.Gimmick, GimmickEffect.Expose, bridge.PairId + ".Expose");
                    foreach (var channel in ProjectPayload(entry, build, expose, bridge.OpeningSource,
                        discriminator: bridge.PairId + ".Expose", trigger: bridge.OpeningTrigger, heroKey: heroKey)) yield return channel;
                }
                foreach (var payoff in new[] { bridge.BasePayoff }.Concat(bridge.Extras))
                    foreach (var channel in ProjectPayload(entry, build, AuthoredKeystoneComposer.BridgePayload(payoff),
                        bridge.PayoffSource, payoff.Recipient, discriminator: bridge.PairId + ":" + payoff.ChannelId,
                        trigger: bridge.PayoffTrigger, heroKey: heroKey)) yield return channel;
                yield break;
            }
            var payload = AuthoredKeystoneComposer.MechanismPayload(s);
            var recipient = s.Ward?.RecipientKind == WardRecipientKind.AlliedTravelers ? KeystoneRecipientKind.AlliedHero
                : s.Ward?.RecipientKind == WardRecipientKind.OwnedSummons ? KeystoneRecipientKind.OwnedSummon : KeystoneRecipientKind.Self;
            var receiver = s.Recharge?.Recipient ?? (s.Relay == null ? null : MemorySelector.Parse("@Q(" + s.Relay.TargetMemory + ")"));
            foreach (var channel in ProjectPayload(entry, build, payload, s.Source ?? s.Recharge?.Source, receiver,
                SourceMemory(s), s.Relay?.TargetMemory, recipient, cooldown: s.Gimmick?.Cooldown ?? 0, heroKey: heroKey)) yield return channel;
        }
    }
}

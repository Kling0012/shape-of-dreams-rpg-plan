using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record StarValueEntry(string Id, string Label, decimal Value, string Unit, string Hero,
    string Scenario, string Origin, string Kind, string StarId, int? Option, string Semantic, string Field,
    int MaxRank, int RankCost, string Status = "measured");
internal sealed record StarValuesConfiguration(string Hero, string Scenario,
    IReadOnlyList<StarEfficiencySelection> Selections, IReadOnlyList<string> ReplacedEffectStarIds);
internal sealed record StarValuesMeasurement(IReadOnlyList<StarValueEntry> Entries,
    IReadOnlyList<StarValuesConfiguration> Configurations);

/// <summary>Definition-level numeric observations, never a composed build or a DPS approximation.</summary>
internal static class StarValuesReport
{
    public static readonly string[] Kinds = ["MemoryHaste", "GimmickBoost", "GimmickParam", "Notable", "Keystone", "Stat"];
    public static readonly string[] Origins = ["private", "shared"];
    public const string ValuePolicy = "adopted typed definition fields; per-rank amounts and explicit rank tables; no rank scaling, boost composition, aggregation or DPS";
    public const string ReplacementPolicy = "selected mechanism Replaces suppresses only replaced gimmick/mechanism effects; other components remain";

    public static StarValuesMeasurement Measure()
    {
        StarClusters.RegisterAllGenerated();
        var entries = new List<StarValueEntry>();
        var configurations = new List<StarValuesConfiguration>();
        foreach (string hero in StarClusters.GeneratedHeroes.OrderBy(h => h, StringComparer.Ordinal))
        {
            var tree = HeroSigils.TreeFor(hero);
            for (int option = 0; option < StarEfficiencyReport.Scenarios.Length; option++)
            {
                string scenario = StarEfficiencyReport.Scenarios[option];
                var selected = tree.Select(parent => (Parent: parent, Effect: parent.IsChoice ? parent.Choices[option] : parent)).ToArray();
                var replaced = selected.Where(row => row.Effect.Mechanism != null)
                    .SelectMany(row => row.Effect.Mechanism.Replaces).ToHashSet(StringComparer.Ordinal);
                configurations.Add(new(hero, scenario, selected.Where(row => row.Parent.IsChoice)
                    .OrderBy(row => row.Parent.Id, StringComparer.Ordinal)
                    .Select(row => new StarEfficiencySelection(row.Parent.Id, option,
                        $"{row.Parent.Id}/options/{option}", Origin(row.Parent))).ToArray(),
                    replaced.OrderBy(id => id, StringComparer.Ordinal).ToArray()));
                foreach (var (parent, effect) in selected)
                {
                    var sink = new Sink(entries, hero, scenario, Origin(parent), parent, parent.IsChoice ? option : null);
                    Collect(sink, effect, replaced.Contains(parent.Id) || replaced.Contains(effect.Id));
                }
            }
        }
        return new(entries.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray(), configurations);
    }

    private static string Origin(TalentDef node) =>
        node.AuthoredStar?.LocalStarId.StartsWith("outer.", StringComparison.Ordinal) == true ? "shared" : "private";

    private sealed class Sink(List<StarValueEntry> entries, string hero, string scenario, string origin, TalentDef parent, int? option)
    {
        public void Add(string kind, string semantic, string field, string unit, decimal value)
        {
            string id = "star-values/" + string.Join("/", new[] { hero, scenario, origin, kind, parent.Id,
                option.HasValue ? "option-" + option.Value.ToString(CultureInfo.InvariantCulture) : "direct", semantic, field, unit }
                .Select(Uri.EscapeDataString));
            entries.Add(new(id, $"{hero} {scenario} {origin} {kind} {parent.Id} {semantic} {field} ({unit})",
                value, unit, hero, scenario, origin, kind, parent.Id, option, semantic, field, parent.MaxRank, parent.RankCost));
        }
    }

    private static string Set(IEnumerable<string> values) => string.Join("+", values.OrderBy(v => v, StringComparer.Ordinal));
    private static string ParamUnit(GimmickParam? parameter) => parameter == GimmickParam.ExtraTargets ? "targets"
        : parameter == GimmickParam.Chance ? "percentage-points" : "percent";
    private static string StatUnit(Stat stat) => stat switch
    {
        Stat.MaxHealthFlat => "health", Stat.AttackFlat => "attack", Stat.PowerFlat => "power", Stat.Armor => "armor",
        Stat.HealthRegen => "health/second", Stat.FourthAttackShift => "positions",
        Stat.Haste => "haste", Stat.Tenacity => "tenacity",
        Stat.EssenceSlotIdentity or Stat.EssenceSlotMovement => "slots", _ => "percent",
    };

    private static void Collect(Sink sink, TalentDef effect, bool replaced)
    {
        if (effect.RunGrowth != null || effect.RunGrowthModifier != null) return;
        if (effect.LinkPerRank is { Kind: LinkKind.MemoryHaste } haste)
            sink.Add("MemoryHaste", "link/" + Set(haste.Requires), "value", "percent", haste.Value);
        if (effect.NativeModifier is { Kind: LinkKind.MemoryHaste } native)
            sink.Add("MemoryHaste", "native/" + native.Memory, "value", "percent", native.Value.Units / 100m);
        if (effect.ScopedModifier is { } scoped)
        {
            string kind = scoped.Param.HasValue ? "GimmickParam" : "GimmickBoost";
            string semantic = $"{scoped.ScopeKind}/{scoped.ScopeMemory}/{Set(scoped.TargetEffectIds)}/{Set(scoped.TargetEffects.Select(e => e.ToString()))}/{scoped.Param?.ToString() ?? "Amount"}";
            sink.Add(kind, semantic, "amount", ParamUnit(scoped.Param), scoped.Param == GimmickParam.ExtraTargets
                ? scoped.ExtraTargets : scoped.Param == GimmickParam.Chance ? scoped.Probability.Units / 100m : scoped.Amount.Units / 100m);
        }
        else
        {
            if (effect.GimmickBoost != 0) sink.Add("GimmickBoost", "memory/" + effect.RouteMemory, "amount", "percent", effect.GimmickBoost);
            if (effect.GimmickParameter is { } parameter)
                sink.Add("GimmickParam", $"memory/{effect.RouteMemory}/{parameter}", "amount", ParamUnit(parameter), effect.GimmickParamAmount);
        }
        if (effect.KeystoneDefinition is { } key)
        {
            // Build retains powers once, except StillWater replaced by the typed stun-source grant.
            if (effect.Power != Power.None && !(effect.Power == Power.StillWater
                && key.Grants.Any(g => g.Kind == AuthoredMechanismKind.StunSourceFilter)))
                sink.Add("Keystone", "power/" + effect.Power, "value", "power:" + effect.Power, effect.PowerValue);
            sink.Add("Keystone", "definition", "cost", "points", key.Cost);
            for (int i = 0; i < key.Upside.Count; i++) Transform(sink, key.Upside[i], i);
            for (int i = 0; i < key.Grants.Count; i++) Mechanism(sink, "Keystone", "grant/" + i, key.Grants[i]);
            return;
        }
        if (effect.IsKeystone)
        {
            if (effect.Power != Power.None) sink.Add("Keystone", "power/" + effect.Power, "value", "power:" + effect.Power, effect.PowerValue);
            return;
        }
        if (!replaced)
        {
            if (effect.Mechanism is { } mechanism) Mechanism(sink, "Notable", "mechanism", mechanism);
            else if (effect.Gimmick is { } gimmick) Gimmick(sink, "Notable", "gimmick", gimmick);
        }
        if (effect.IsPowerNode) sink.Add("Notable", "power/" + effect.RankPower, "perRank", "power:" + effect.RankPower, effect.PerRank);
        else if ((effect.PerRank != 0 || effect.ClusterStar?.Kind == ClusterStarKind.Stat)
            && effect.Gimmick == null && effect.Mechanism == null && effect.ScopedModifier == null
            && effect.NativeModifier == null && effect.LinkPerRank == null && effect.GimmickBoost == 0 && !effect.GimmickParameter.HasValue)
            sink.Add("Stat", "stat/" + effect.Stat, "perRank", StatUnit(effect.Stat), effect.PerRank);
    }

    private static void Gimmick(Sink sink, string kind, string prefix, GimmickDef def)
    {
        string semantic = $"{prefix}/{def.Trigger}/{def.Effect}";
        sink.Add(kind, semantic, "value", def.Effect == GimmickEffect.Element ? "element-application-percent" : "effect-percent:" + def.Effect, def.Value);
        sink.Add(kind, semantic, "argument", "argument:" + def.Effect, def.Arg);
        sink.Add(kind, semantic, "cooldown", "seconds", (decimal)def.Cooldown);
        sink.Add(kind, semantic, "durationModifier", "percent", def.DurationUnits / 100m);
        sink.Add(kind, semantic, "radiusModifier", "percent", def.RadiusUnits / 100m);
        sink.Add(kind, semantic, "extraTargets", "targets", def.ExtraTargets);
        sink.Add(kind, semantic, "chanceModifier", "percentage-points", def.ChanceUnits / 100m);
    }

    private static void Mechanism(Sink sink, string kind, string prefix, AuthoredMechanismSpec spec)
    {
        string semantic = $"{prefix}/{spec.Kind}/{spec.ChannelId}/{spec.Source?.Expression}/{spec.Trigger}/{spec.Budget}/{spec.Condition}/{spec.PairId}/{Set(spec.RequiredMemories)}";
        sink.Add(kind, semantic, "everyN", "events", spec.EveryN);
        sink.Add(kind, semantic, "once", "boolean", spec.Once ? 1 : 0);
        for (int i = 0; i < spec.ValuesByRank.Length; i++)
            sink.Add(kind, semantic, "rank-" + (i + 1), spec.Gimmick != null ? "effect-percent:" + spec.Gimmick.Effect
                : spec.Ward != null ? "health-percent:" + spec.Ward.AmountBasis : "percent", spec.ValuesByRank[i] / 100m);
        if (spec.Gimmick is { } gimmick) Gimmick(sink, kind, semantic, gimmick);
        if (spec.Recharge is { } recharge)
        {
            string scoped = semantic + "/" + recharge.Recipient.Expression + "/" + recharge.Condition;
            sink.Add(kind, scoped, "value", "percent", recharge.ValueUnits / 100m);
            sink.Add(kind, scoped, "boost", "percent", recharge.ModifierUnits / 100m);
            sink.Add(kind, scoped, "cap", "percent", recharge.CapUnits / 100m);
            sink.Add(kind, scoped, "probability", "probability-percent", recharge.ProbabilityUnits / 100m);
            sink.Add(kind, scoped, "everyN", "events", recharge.EveryN);
            sink.Add(kind, scoped, "requiredElementTypes", "element-types", recharge.RequiredElementTypes);
        }
        if (spec.Primed is { } primed)
        {
            sink.Add(kind, semantic, "value", "damage-percent", primed.ValueUnits / 100m);
            sink.Add(kind, semantic, "duration", "seconds", (decimal)primed.DurationSeconds);
        }
        if (spec.Relay is { } relay)
        {
            string scoped = semantic + "/" + relay.TargetMemory;
            sink.Add(kind, scoped, "value", "damage-percent", relay.ValueUnits / 100m);
            sink.Add(kind, scoped, "cap", "damage-percent", relay.ValueCapUnits / 100m);
            sink.Add(kind, scoped, "duration", "seconds", (decimal)relay.DurationSeconds);
            sink.Add(kind, scoped, "durationModifier", "percent", relay.DurationModifierUnits / 100m);
            sink.Add(kind, scoped, "quietRelay", "boolean", relay.QuietRelay ? 1 : 0);
        }
        if (spec.Ward is { } ward) Ward(sink, kind, semantic, ward);
        if (spec.Dividend is { } dividend)
            sink.Add(kind, semantic + "/" + dividend.ConditionKey, "probability", "probability-percent", dividend.ProbabilityUnits / 100m);
        if (spec.IdentityStrike is { } strike)
        {
            string scoped = semantic + $"/{strike.Identity}/{strike.Trigger}/{strike.Shape}/{strike.Basis}/{strike.Element}";
            sink.Add(kind, scoped, "value", "damage-percent", strike.AdUnits / 100m);
            sink.Add(kind, scoped, "bonusSpeed", "damage-percent/bonus-speed-percent", strike.BonusSpeedUnitsPerPercent / 100m);
            sink.Add(kind, scoped, "everyN", "events", strike.EveryN);
            sink.Add(kind, scoped, "window", "seconds", (decimal)strike.WindowSeconds);
            sink.Add(kind, scoped, "range", "metres", (decimal)strike.RangeMetres);
            sink.Add(kind, scoped, "widthOrArc", strike.Shape == IdentityStrikeShape.ForwardArc ? "degrees" : "metres", (decimal)strike.WidthOrArc);
            sink.Add(kind, scoped, "maxTargets", "targets", strike.MaxTargets);
        }
        if (spec.Tuning is { } tuning)
            sink.Add(kind, semantic + "/" + tuning.Kind, "value", tuning.Kind == MemoryTuningKind.KillingFlowOnHitHealScale ? "multiplier" : "percent",
                tuning.ValueUnits / (tuning.Kind == MemoryTuningKind.KillingFlowOnHitHealScale ? 10000m : 100m));
        if (spec.Kind is AuthoredMechanismKind.SacrificeShield or AuthoredMechanismKind.StunSourceFilter)
        {
            var payload = AuthoredKeystoneComposer.MechanismPayload(spec);
            sink.Add(kind, semantic, "value", spec.Kind == AuthoredMechanismKind.SacrificeShield ? "paid-health-percent" : "max-health-percent", payload.Value);
            sink.Add(kind, semantic, "duration", "seconds", payload.DurationSeconds);
            sink.Add(kind, semantic, "payloadValueCap", "percent", payload.Caps.Value);
            sink.Add(kind, semantic, "durationCap", "seconds", payload.Caps.DurationSeconds);
            if (spec.Kind == AuthoredMechanismKind.StunSourceFilter)
                sink.Add(kind, semantic, "cooldown", "seconds", (decimal)StunSourceFilter.IntervalSeconds);
            else
                sink.Add(kind, semantic, "newAwardCap", "max-health-percent", (decimal)new SacrificeShieldAward().NewAwardCapRatio * 100m);
        }
        if (spec.Bridge is { } bridge)
        {
            string scoped = semantic + $"/{bridge.PairId}/{bridge.GateKind}/{bridge.OpeningSource.Expression}/{bridge.PayoffSource.Expression}";
            sink.Add(kind, scoped, "cooldown", "seconds", (decimal)bridge.CooldownSeconds);
            sink.Add(kind, scoped, "window", "seconds", (decimal)bridge.WindowSeconds);
            sink.Add(kind, scoped, "mark", "seconds", (decimal)bridge.MarkSeconds);
            sink.Add(kind, scoped, "windowLifetimeScale", "multiplier", (decimal)bridge.WindowLifetimeScale);
            sink.Add(kind, scoped, "nativeWindowLifetime", "boolean", bridge.UsesNativeWindowLifetime ? 1 : 0);
            sink.Add(kind, scoped, "retainedFiveRanks", "boolean", bridge.RetainedFiveRanks ? 1 : 0);
            Payload(sink, kind, scoped + "/base", bridge.BasePayoff);
            for (int i = 0; i < bridge.Extras.Count; i++) Payload(sink, kind, scoped + "/extra/" + i, bridge.Extras[i]);
        }
    }

    private static void Ward(Sink sink, string kind, string prefix, AlliedWardDefinition ward)
    {
        string semantic = $"{prefix}/ward/{ward.ChannelId}/{ward.RecipientKind}/{ward.AmountBasis}/{ward.PoolKind}/{ward.Limits}/{ward.Budget}";
        sink.Add(kind, semantic, "value", "health-percent:" + ward.AmountBasis, ward.ValueUnits / 100m);
        sink.Add(kind, semantic, "radius", "metres", (decimal)ward.RadiusMetres);
        sink.Add(kind, semantic, "duration", "seconds", (decimal)ward.DurationSeconds);
        sink.Add(kind, semantic, "baseTargets", "targets", ward.BaseTargets);
        sink.Add(kind, semantic, "targets", "targets", ward.Targets);
        sink.Add(kind, semantic, "maxTargets", "targets", ward.MaxTargets);
        sink.Add(kind, semantic, "includeOwner", "boolean", ward.IncludeOwner ? 1 : 0);
    }

    private static void Payload(Sink sink, string kind, string prefix, BridgePayload payload)
    {
        string semantic = $"{prefix}/{payload.Kind}/{payload.ChannelId}/{payload.DamageBasis}/{payload.Recipient?.Expression}";
        if (payload.Gimmick is { } gimmick) Gimmick(sink, kind, semantic, gimmick);
        else if (payload.Ward is { } ward) Ward(sink, kind, semantic, ward);
        else
        {
            sink.Add(kind, semantic, "value", "payload-percent:" + payload.Kind, payload.ValueUnits / 100m);
            sink.Add(kind, semantic, "cap", "payload-percent:" + payload.Kind, payload.CapUnits / 100m);
            sink.Add(kind, semantic, "duration", "seconds", (decimal)payload.DurationSeconds);
            sink.Add(kind, semantic, "durationCap", "seconds", payload.DurationCapSeconds);
        }
    }

    private static void Transform(Sink sink, KeystoneTransform transform, int index)
    {
        var scope = transform.Scope;
        string semantic = $"upside/{index}/{transform.TargetLayer}/{transform.Field}/{transform.Operation}/{Set(scope.TargetMemorySet)}/{Set(scope.ReceiverMemorySet)}/{Set(scope.TargetEffectIds)}/{Set(scope.TargetEffectSet.Select(e => e.ToString()))}/{scope.PayloadKind}/{scope.Recipient}/{scope.SourceKind}/{Set(scope.SourceSelectors.Select(s => s.Expression))}/{Set(scope.ReceiverSelectors.Select(s => s.Expression))}";
        string unit = transform.Operation == KeystoneOperation.Scale ? "percent" : transform.Field switch
        {
            KeystoneField.Duration or KeystoneField.Delay => "seconds", KeystoneField.Radius => "metres",
            KeystoneField.TargetCount => "targets", KeystoneField.EveryN => "events", KeystoneField.Argument => "argument:" + scope.PayloadKind,
            KeystoneField.Probability => "probability-units", _ => "value:" + transform.TargetLayer + ":" + scope.PayloadKind,
        };
        decimal value = transform.Operation switch
        {
            KeystoneOperation.Scale => transform.MagnitudeUnits.Percent,
            KeystoneOperation.AddTargets or KeystoneOperation.SetEveryN => transform.Count,
            _ => transform.Seconds,
        };
        sink.Add("Keystone", semantic, "amount", unit, value);
        if (transform.Maximum is { } maximum) sink.Add("Keystone", semantic, "maximum", unit, maximum);
        if (transform.ExpectedFrom is { } from) sink.Add("Keystone", semantic, "expectedFrom", unit, from);
        if (scope.Argument is { } argument) sink.Add("Keystone", semantic, "scopeArgument", "argument:" + scope.PayloadKind, argument);
    }

    public static string Render(StarValuesMeasurement measurement)
    {
        var text = new StringBuilder();
        text.AppendLine("# 星の種類別・意味単位別の実登録値");
        text.AppendLine();
        text.AppendLine($"ContentFingerprint: `{ContentFingerprint.Value}`");
        text.AppendLine();
        text.AppendLine("HeroSigils.TreeFor が採用した型付き定義を観測。MemoryHaste / GimmickBoost / GimmickParam / Notable / Keystone / Stat の効果欄を個別に比較し、異なる効果・単位を合算しません。数値0は実測0、存在しない欄は未観測（0ではない）。段数表は各rankを別行に記録。Powerは固有Power単位、argは効果別引数単位、フラグは0/1です。");
        text.AppendLine("all-Aは全Choiceの0、all-Bは1を固定。共有外縁と固有星を区別し、同IDの旧星を重複計上しません。選択されたMechanism.Replacesは旧Gimmick/Mechanism成分のみ除外し、複合星の他成分は残します。刻印はそれぞれ独立した定義投影で、全刻印同時取得や接続条件を満たす実Buildではありません。");
        text.AppendLine("これは効果量・秒・距離・個数・確率・条件引数の定義レポートです。位階・GB合成・最終上限適用・発動頻度・DPS・成長・進行を模型化せず、親RankCost/MaxRankはJSONのメタデータのみ。既存star-efficiencyのダメージ効率/位階投影とは別です。");
        text.AppendLine();
        text.AppendLine("| 旅人 | 構成 | 由来 | 種類 | 星 | 意味 | 欄 | 単位 | 値 |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | ---: |");
        foreach (var entry in measurement.Entries)
            text.AppendLine($"| {entry.Hero} | {entry.Scenario} | {entry.Origin} | {entry.Kind} | {entry.StarId} | {entry.Semantic.Replace("|", "\\|")} | {entry.Field} | {entry.Unit} | {entry.Value.ToString(CultureInfo.InvariantCulture)} |");
        return text.ToString();
    }
}

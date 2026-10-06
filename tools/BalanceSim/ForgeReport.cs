using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record ForgeEntry(string Id, string Label, decimal? Value, string Unit,
    string Status, IReadOnlyDictionary<string, string> Axes);

internal static class ForgeReport
{
    // Scenario inputs, not balance values. The last row exercises the existing saturating reroll formula.
    public static readonly int[] RerollCounts = [0, 1, 2, 3, 10, int.MaxValue];
    public const string ProjectionPolicy = "Core formula projections; independent state axes; no combat prediction; unavailable operations are null";

    public static List<ForgeEntry> Measure()
    {
        StarClusters.RegisterAllGenerated();
        var entries = new List<ForgeEntry>();
        void Add(string id, string label, decimal? value, string unit,
            Dictionary<string, string>? axes = null, string status = "measured") =>
            entries.Add(new ForgeEntry("forge/" + id, label, value, unit, status, axes ?? new()));
        Dictionary<string, string> State(Rarity rarity, string axis, int value) => new()
        {
            ["rarity"] = rarity.ToString(), [axis] = value.ToString(CultureInfo.InvariantCulture),
        };

        Add("enhance/demotionChance", "強化失敗時の降格確率", (decimal)Content.EnhanceDemotionChance, "ratio");
        Add("enhance/demotionSteps", "強化失敗時の降格幅", Content.EnhanceDemotionSteps, "levels");
        Add("enhance/stepPerBreak", "限界突破による強化上限の増分", Content.EnhanceStepPerBreak, "levels");
        Add("enhance/milestoneFirst", "強化の第1節目", Content.EnhanceMilestoneFirst, "levels");
        Add("enhance/milestoneSecond", "強化の第2節目", Content.EnhanceMilestoneSecond, "levels");
        Add("enhance/milestoneThird", "強化の第3節目", Content.EnhanceMilestoneThird, "levels");
        Add("enhance/milestoneFourth", "強化の第4節目", Content.EnhanceMilestoneFourth, "levels");
        Add("enhance/milestoneFifth", "強化の第5節目", Content.EnhanceMilestoneFifth, "levels");
        Add("enhance/milestonePowerPct", "限界突破の固有効果節目倍率", Content.LimitBreakPowerPct, "percent");
        Add("retune/max", "再調律の上限", Content.MaxRetunes, "count");
        Add("retune/choices", "再調律の候補数", Content.RetuneChoices, "count");

        foreach (var rarity in Enum.GetValues<Rarity>())
        {
            string name = rarity.ToString();
            int maxBreaks = Content.MaxLimitBreaks(rarity);
            Add($"break/{name}/max", $"{name} 限界突破回数上限", maxBreaks, "count", new() { ["rarity"] = name });
            Add($"cost/{name}/multiplier", $"{name} 素材費用倍率", Content.ForgeMaterialCostMultiplier(rarity), "ratio", new() { ["rarity"] = name });
            for (int breaks = 0; breaks <= maxBreaks; breaks++)
            {
                int cap = Content.MaxEnhanceFor(rarity, breaks);
                var breakAxes = State(rarity, "currentBreaks", breaks);
                string breakId = $"break/{name}/{breaks}";
                string breakLabel = $"{name} 現在突破{breaks}";
                Add(breakId + "/enhanceCap", breakLabel + " 強化上限", cap, "levels", breakAxes);
                bool breakCap = breaks == maxBreaks;
                Add(breakId + "/shards", breakLabel + " 次の突破欠片", breakCap ? null : Content.LimitBreakShardCost(breaks + 1, rarity), "shards", breakAxes, breakCap ? "cap" : "measured");
                Add(breakId + "/tuning", breakLabel + " 次の突破調律石", breakCap ? null : Content.LimitBreakTuningCost(breaks + 1, rarity), "tuning", breakAxes, breakCap ? "cap" : "measured");
                var relic = new Relic { Rarity = rarity, LimitBreaks = breaks };
                for (int level = 0; level <= cap; level++)
                {
                    relic.Enhance = level;
                    var axes = State(rarity, "currentBreaks", breaks);
                    axes["currentEnhance"] = level.ToString(CultureInfo.InvariantCulture);
                    string id = $"enhance/{name}/{breaks}/{level}";
                    string label = $"{name} 突破{breaks} 現在+{level}";
                    string status = level == cap ? "cap" : "measured";
                    Add(id + "/failurePercent", label + " 次の強化失敗率", level == cap ? null : Rules.EnhanceFailureChance(relic), "percent", axes, status);
                    Add(id + "/shards", label + " 次の強化支払い", level == cap ? null : Content.EnhanceCost(relic), "shards", axes, status);
                    Add(id + "/salvageShards", label + " 分解欠片（強化返金込み）", Rules.SalvageValue(relic), "shards", axes);
                    Add(id + "/salvageTuning", label + " 分解調律石", Content.SalvageTuning(rarity), "tuning", axes);
                    foreach (var eventKind in new[] { DreamEvent.Fountain, DreamEvent.ForgeShrine, DreamEvent.TemperingAltar })
                    {
                        bool eligible = Content.CanGuaranteedEnhance(relic, eventKind);
                        var eventAxes = new Dictionary<string, string>(axes) { ["event"] = eventKind.ToString(), ["hasRequiredSacrifice"] = "true" };
                        string eventId = $"event/{eventKind}/{name}/{breaks}/{level}";
                        string eventLabel = $"{eventKind} {label}（必要な供物あり）";
                        Add(eventId + "/shards", eventLabel + " 支払い", eligible ? Content.GuaranteedEnhanceCost(eventKind, rarity) : null, "shards", eventAxes, eligible ? "measured" : "inapplicable");
                        Add(eventId + "/steps", eventLabel + " 保証強化段数", eligible ? Content.GuaranteedEnhanceSteps(eventKind) : null, "levels", eventAxes, eligible ? "measured" : "inapplicable");
                    }
                }
            }
            for (int done = 0; done <= Content.MaxRetunes; done++)
            {
                var relic = new Relic { Rarity = rarity, Retunes = done };
                Add($"retune/{name}/{done}/tuning", $"{name} 再調律済み{done} 次の支払い", done == Content.MaxRetunes ? null : Content.RetuneCost(relic), "tuning", State(rarity, "retunesDone", done), done == Content.MaxRetunes ? "cap" : "measured");
            }
            foreach (int count in RerollCounts)
            {
                var cost = Rules.AffixRerollCost(new Relic { Rarity = rarity, AffixRerolls = count });
                var axes = State(rarity, "affixRerollsDone", count);
                Add($"reroll/{name}/{count}/shards", $"{name} 特性洗い直し済み{count} 次の欠片", cost.Shards, "shards", axes);
                Add($"reroll/{name}/{count}/tuning", $"{name} 特性洗い直し済み{count} 次の調律石", cost.Tuning, "tuning", axes);
            }
            bool canSynthesize = rarity < Rarity.Legendary;
            var synthesisAxes = new Dictionary<string, string> { ["inputRarity"] = name };
            Add($"synthesis/{name}/inputs", $"{name} 合成の材料数", canSynthesize ? Content.TransmuteInputs(rarity) : null, "relics", synthesisAxes, canSynthesize ? "measured" : "inapplicable");
            foreach (bool targeted in new[] { false, true })
            {
                var axes = new Dictionary<string, string>(synthesisAxes) { ["targeted"] = targeted.ToString() };
                string id = $"synthesis/{name}/{(targeted ? "targeted" : "random")}";
                string label = $"{name} 合成 {(targeted ? "枠指定" : "無指定")}";
                Add(id + "/shards", label + " 欠片", canSynthesize ? Rules.TransmuteCost(rarity, targeted) : null, "shards", axes, canSynthesize ? "measured" : "inapplicable");
                Add(id + "/tuning", label + " 調律石", canSynthesize ? Rules.TransmuteTuning(rarity) : null, "tuning", axes, canSynthesize ? "measured" : "inapplicable");
            }
        }
        for (int level = 0; level <= Content.EnhanceMilestoneFifth; level++)
        {
            var axes = new Dictionary<string, string> { ["currentEnhance"] = level.ToString(CultureInfo.InvariantCulture) };
            Add($"multiplier/enhance/{level}/stat", $"+{level} 能力・特性倍率", Content.EnhanceScalePct(level), "percent", axes);
            Add($"multiplier/enhance/{level}/power", $"+{level} 固有効果倍率", Content.EnhancePowerScalePct(level), "percent", axes);
        }
        for (int level = 0; level <= Content.MaxAwakenLevel; level++)
        {
            var axes = new Dictionary<string, string> { ["awakenLevel"] = level.ToString(CultureInfo.InvariantCulture) };
            Add($"awakening/{level}/threshold", $"覚醒{level} 累計必要点", Content.AwakenThresholdFor(level), "points", axes);
            Add($"awakening/{level}/stat", $"覚醒{level} 特性倍率", Content.AwakenAffixPctAt(level), "percent", axes);
            Add($"awakening/{level}/power", $"覚醒{level} 固有効果倍率", Content.AwakenPowerPctAt(level), "percent", axes);
        }
        foreach (var tier in Enum.GetValues<MonsterTier>())
            foreach (bool nightmare in new[] { false, true })
                Add($"awakening/points/{tier}/{nightmare}", $"{tier} 悪夢={nightmare} 基本覚醒点", Content.AwakenPoints(tier, nightmare), "points", new() { ["tier"] = tier.ToString(), ["nightmare"] = nightmare.ToString() });
        foreach (bool fine in new[] { false, true })
        {
            string id = "craft/" + (fine ? "fine" : "normal");
            var axes = new Dictionary<string, string> { ["fine"] = fine.ToString() };
            Add(id + "/shards", id + " 欠片", Rules.CraftShardCost(fine), "shards", axes);
            Add(id + "/tuning", id + " 調律石", Rules.CraftTuningCost(fine), "tuning", axes);
            Add(id + "/luck", id + " 抽選の幸運入力", (decimal)Rules.CraftLuck(fine), "ratio", axes);
        }
        foreach (var upgrade in Workshop.All)
            for (int level = 0; level <= upgrade.MaxLevel; level++)
            {
                var profile = new Profile();
                profile.Upgrades[upgrade.Id] = level;
                var axes = new Dictionary<string, string> { ["upgrade"] = upgrade.Key, ["currentLevel"] = level.ToString(CultureInfo.InvariantCulture), ["otherUpgrades"] = "0" };
                string id = $"workshop/{upgrade.Key}/{level}";
                string label = $"工房 {upgrade.Key} 現在{level}段";
                bool cap = level == upgrade.MaxLevel;
                Add(id + "/shards", label + " 次の欠片", cap ? null : upgrade.Costs[level].Shards, "shards", axes, cap ? "cap" : "measured");
                Add(id + "/tuning", label + " 次の調律石", cap ? null : upgrade.Costs[level].Tuning, "tuning", axes, cap ? "cap" : "measured");
                var effect = upgrade.Id switch
                {
                    Upgrade.BigSatchel => ("satchelCapacity", Workshop.SatchelCapacity(profile), "relics"),
                    Upgrade.WideStash => ("stashCapacity", Workshop.StashCapacity(profile), "relics"),
                    Upgrade.BountyReroll => ("rerollsPerRun", Workshop.RerollsPerRun(profile), "count"),
                    Upgrade.EchoLantern => ("echoPercent", Workshop.EchoPercent(profile), "percent"),
                    Upgrade.LostMap => ("roomsToRecover", Workshop.RoomsToRecover(profile), "rooms"),
                    Upgrade.PactStars => ("pactsOffered", Workshop.PactsOffered(profile), "count"),
                    _ => throw new InvalidOperationException($"Unmapped active workshop upgrade: {upgrade.Key}"),
                };
                Add(id + "/" + effect.Item1, label + " " + effect.Item1, effect.Item2, effect.Item3, axes);
            }
        return entries;
    }

    public static string Render(IReadOnlyList<ForgeEntry> entries)
    {
        var text = new StringBuilder();
        text.AppendLine("# 鍛冶・強化・工房の実測表");
        text.AppendLine();
        text.AppendLine($"ContentFingerprint: `{ContentFingerprint.Value}`（星図登録済み）");
        text.AppendLine();
        text.AppendLine("Coreの実際の費用・倍率・報酬関数および工房定義を直接投影しています。予測戦闘性能ではありません。欠片・調律石は実支払い。覚醒点は深度等の外部補正前。工房は対象以外0段、出来事は必要な供物あり。上限・対象外は未定義であり0ではありません。");
        text.AppendLine();
        text.AppendLine("| 指標 | 値 | 単位 | 状態 |");
        text.AppendLine("| --- | ---: | --- | --- |");
        foreach (var entry in entries)
            text.AppendLine($"| {entry.Label} | {entry.Value?.ToString(CultureInfo.InvariantCulture) ?? "—"} | {entry.Unit} | {entry.Status} |");
        return text.ToString();
    }
}

using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record ForgeEntry(string Rarity, int LimitBreaks, int CurrentEnhance, int MaxEnhance,
    bool IsCap, int FailurePercent, int? BaseShardCost);

internal static class ForgeReport
{
    public static List<ForgeEntry> Measure()
    {
        StarClusters.RegisterAllGenerated();
        var entries = new List<ForgeEntry>();
        foreach (var rarity in Enum.GetValues<Rarity>())
        {
            string rarityName = rarity.ToString();
            for (int breaks = 0; breaks <= Content.MaxLimitBreaks(rarity); breaks++)
            {
                int cap = Content.MaxEnhanceFor(rarity, breaks);
                var relic = new Relic { Rarity = rarity, LimitBreaks = breaks };
                for (int level = 0; level <= cap; level++)
                {
                    relic.Enhance = level;
                    entries.Add(new ForgeEntry(rarityName, breaks, level, cap, level == cap,
                        Rules.EnhanceFailureChance(relic), level == cap ? null : Content.EnhanceCost(level)));
                }
            }
        }
        return entries;
    }

    public static string Render(IReadOnlyList<ForgeEntry> entries)
    {
        var text = new StringBuilder();
        text.AppendLine("# 鍛冶・強化の実測表");
        text.AppendLine();
        text.AppendLine($"ContentFingerprint: `{ContentFingerprint.Value}`（星図登録済み）");
        text.AppendLine();
        text.AppendLine("Rules.EnhanceFailureChance / Content.EnhanceCost を直接呼び出した値です。欠片は基本費用（実際のエピック以上の支払いは2倍）。上限の失敗率0は強化可能という意味ではありません。");
        text.AppendLine();
        text.AppendLine("| レア度 | 限界突破 | 現在強化 | 上限 | 状態 | 失敗率 (%) | 基本欠片 | ");
        text.AppendLine("| --- | ---: | ---: | ---: | --- | ---: | ---: |");
        foreach (var e in entries)
            text.AppendLine($"| {e.Rarity} | {e.LimitBreaks} | {e.CurrentEnhance} | {e.MaxEnhance} | {(e.IsCap ? "上限・強化不可" : "強化可能")} | {e.FailurePercent} | {e.BaseShardCost?.ToString(CultureInfo.InvariantCulture) ?? "—"} |");
        return text.ToString();
    }
}

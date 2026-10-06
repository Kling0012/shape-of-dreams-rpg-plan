using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record ProgressionEntry(string Id, string Label, decimal Value, string Unit);

internal static class StarProgressionReport
{
    public static List<ProgressionEntry> Measure()
    {
        StarClusters.RegisterAllGenerated();
        var entries = new List<ProgressionEntry>();
        void Add(string key, string label, int value, string unit) =>
            entries.Add(new("star-progression/" + key, label, value, unit));
        Add("maxPoints", "星XPによる獲得ポイント上限", StarProgression.MaxPoints, "points");
        Add("secureXp", "確保の基本星XP", StarProgression.SecureXp, "xp");
        Add("victoryXp", "踏破の基本星XP", StarProgression.VictoryXp, "xp");
        foreach (var tier in Enum.GetValues<MonsterTier>())
            foreach (bool nightmare in new[] { false, true })
                Add($"kill/{tier}/{(nightmare ? "nightmare" : "normal")}",
                    $"{tier} 悪夢={nightmare} 撃破の基本星XP", StarProgression.KillXp(tier, nightmare), "xp");
        for (int point = 1; point <= StarProgression.MaxPoints; point++)
        {
            Add($"point/{point}/cost", $"星{point}点目の費用", StarProgression.CostForPoint(point), "xp");
            Add($"point/{point}/totalXp", $"星{point}点の累計必要XP", StarProgression.TotalXpForPoints(point), "xp");
            Add($"point/{point}/keystoneSlots", $"星{point}点の刻印枠", KeystoneSlots.CountFor(point), "slots");
        }
        for (int i = 0; i < KeystoneSlots.UnlockLevels.Length; i++)
            Add($"keystone/{i + 2}/unlock", $"刻印{i + 2}枠目の解放星レベル", KeystoneSlots.UnlockLevels[i], "points");
        return entries;
    }

    public static string Render(IReadOnlyList<ProgressionEntry> entries)
    {
        var text = new StringBuilder("# 星の進行：XP曲線・費用・報酬\n\n");
        text.AppendLine("Coreの実式。撃破・確保・踏破XPは深度等の外部補正前。旧保存の移行XPは調整対象外。");
        text.AppendLine();
        text.AppendLine("| 指標 | 値 | 単位 |");
        text.AppendLine("| --- | ---: | --- |");
        foreach (var entry in entries)
            text.AppendLine($"| {entry.Label} | {entry.Value.ToString(CultureInfo.InvariantCulture)} | {entry.Unit} |");
        return text.ToString();
    }
}

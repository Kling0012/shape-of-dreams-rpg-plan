using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>v1.32 セットバランス（--mode sets）の Markdown 出力。</summary>
internal static class SetReport
{
    public static string Render(IReadOnlyList<SetBalanceResult> results, TimeSpan elapsed)
    {
        double median = SetBalance.MedianSixBonusGain(results);
        var sb = new StringBuilder();
        sb.AppendLine("# セット6部位バランス（v1.32）");
        sb.AppendLine();
        sb.AppendLine($"- 対象: {results.Count}組（Content.Sets の通常セット。ボスprofileは対象外）");
        sb.AppendLine($"- 基準: 最良の非セット代替品6枠（伝説・エピック、各枠{SetBalance.AlternativeRolls}抽選の最良、アイテムレベル{SetBalance.GearItemLevel}・強化なし）の代理値 {Fmt(results.Count > 0 ? results[0].Baseline : 0)}");
        sb.AppendLine($"- 代理値: Build が届けた能力値・固有効果を Content.StatCap / PowerCap で割って足した予算消化率の合計");
        sb.AppendLine($"- 6点ボーナス利得の中央値: {Fmt(median)}、許容帯: {Fmt(median * SetBalance.WeakRatio)} 〜 {Fmt(median * SetBalance.StrongRatio)}（中央値の0.5〜1.5倍）");
        sb.AppendLine($"- 判定: 基準内 / 強すぎ（{SetBalance.StrongRatio}倍超） / 弱すぎ（{SetBalance.WeakRatio}倍未満）");
        sb.AppendLine("- 制約: 戦闘DPS・勝率・発動頻度の測定ではない。通常連携はPowerScoreに含めず、CLI seedではなくSetBalanceの固定seed・登録順を使用。");
        sb.AppendLine();
        sb.AppendLine("| セット | ID | 2点利得 | 3点利得 | 6点利得 | 6点ボーナス利得 | 中央値比 | 6点占有率 | 6つ装着の効果 | 判定 |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");
        foreach (var r in results)
        {
            string six = string.Join("、", r.SixPiece.Select(p => $"{Content.PowerName(p.Power)} {p.Value.ToString(CultureInfo.InvariantCulture)}"));
            sb.AppendLine($"| {r.Name} | `{r.Id}` | {Fmt(r.Gain2)} | {Fmt(r.Gain3)} | {Fmt(r.Gain6)} | {Fmt(r.SixBonusGain)} | {Ratio(r, median)} | {Pct(r.SixShare)} | {six} | {JaVerdict(SetBalance.Verdict(r.SixBonusGain, median))} |");
        }
        var outliers = results.Where(r => SetBalance.Verdict(r.SixBonusGain, median) != "ok").ToList();
        sb.AppendLine();
        sb.AppendLine($"- 基準外: {outliers.Count}組" + (outliers.Count > 0
            ? "（" + string.Join("、", outliers.Select(r => r.Id + " " + JaVerdict(SetBalance.Verdict(r.SixBonusGain, median)))) + "）"
            : string.Empty));
        sb.AppendLine($"- 計測時間: {elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}ms（Content と引数が同じなら同じ結果）");
        return sb.ToString();
    }

    internal static string JaVerdict(string verdict) => verdict switch
    {
        "strong" => "強すぎ",
        "weak" => "弱すぎ",
        _ => "基準内",
    };

    private static string Fmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Ratio(SetBalanceResult r, double median) =>
        median > 0 ? (r.SixBonusGain / median).ToString("0.00", CultureInfo.InvariantCulture) : "—";

    private static string Pct(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}

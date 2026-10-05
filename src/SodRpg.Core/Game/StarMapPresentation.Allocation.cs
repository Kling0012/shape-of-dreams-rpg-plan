using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace SodRpg.Core.Game
{
    public static partial class StarMapPresentation
    {
        private static readonly HashSet<string> DescriptionWarnings = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Regex RichText = new Regex("<[^>]*>");
        private static readonly string[] SummarySeparators = { "（数値は1段あたり", " (values per rank;", "／", " when ", " while " };
        private static readonly char[] ChangeSigns = { '+', '＋', '−' };

        private static string DescriptionFallback(TalentDef star, Exception error)
        {
            string id = star?.Id ?? "unknown";
            lock (DescriptionWarnings)
                if (DescriptionWarnings.Add(id))
                    Trace.TraceWarning("Star description unavailable ({0}): {1}", id, error);
            return Loc.T("効果の詳細を表示できません。この星の取得・効果は変更されません。",
                "Effect details are unavailable. This star's allocation and effects are unchanged.");
        }

        public static string EffectSummary(TalentDef star, int chosen = -1)
        {
            if (star == null) return Loc.T("始まりの星", "Starting star");
            if (star.IsChoice)
            {
                if (chosen >= 0 && chosen < star.Choices.Count) return EffectSummary(star.Choices[chosen]);
                return string.Join(Loc.T(" ／ ", " / "), star.Choices.Select(s => EffectSummary(s)));
            }
            return EffectSummary(EffectDescription(star));
        }

        public static string EffectSummary(string description)
        {
            string text = RichText.Replace(description ?? "", "");
            foreach (string line in text.Split('\n'))
                if (!string.IsNullOrWhiteSpace(line) && line != Loc.T("効果", "Effect"))
                {
                    string summary = line.Trim();
                    foreach (string separator in SummarySeparators)
                    {
                        int end = summary.IndexOf(separator, StringComparison.Ordinal);
                        if (end >= 0) summary = summary.Substring(0, end).Trim();
                    }
                    int detail = summary.IndexOf(Loc.T("：", ": "), StringComparison.Ordinal);
                    if (detail >= 0)
                    {
                        string heading = summary.Substring(0, detail);
                        bool hasChange = heading.IndexOfAny(ChangeSigns) >= 0;
                        for (int i = 0; !hasChange && i + 1 < heading.Length; i++)
                            hasChange = heading[i] == '-' && char.IsDigit(heading[i + 1]);
                        if (hasChange) return heading.Trim();
                    }
                    return summary.TrimEnd('。');
                }
            return Loc.T("星の効果", "Star effect");
        }

        public static string NavigationLabel(TalentDef star, int rank, bool available, string summary)
        {
            string status = rank > 0 ? Loc.T("取得済み", "Acquired")
                : available ? Loc.T("取得可能", "Available") : Loc.T("条件不足", "Requirements missing");
            return "<b>" + star.Name + "</b>  〈" + status + "〉\n" + summary
                + Loc.T($"\n{rank}/{star.MaxRank}段", $"\nRank {rank}/{star.MaxRank}");
        }

        // Uses the same graph/prerequisite state as allocation; never creates a second unlock rule.
        public static string MissingRequirements(HeroTreeLayout layout, int index, HeroState state, int freePoints, bool connected)
        {
            var star = layout.Nodes[index].Talent;
            if (star == null) return "";
            int rank = star.IsKeystone ? state.HasKeystone(star.Id) ? 1 : 0
                : state.Talents.TryGetValue(star.Id, out int owned) ? owned : 0;
            if (rank >= star.MaxRank) return Loc.T("最大段まで取得済みです。", "Acquired at maximum rank.");
            var reasons = new List<string>();
            bool Has(string id) => state.HasKeystone(id) || state.Talents.TryGetValue(id, out int value) && value > 0;
            string Name(string id)
            {
                foreach (var node in layout.Nodes) if (node.Id == id) return node.Talent?.Name.ToString() ?? Loc.T("始まりの星", "Starting star");
                return Loc.T("前提の星", "prerequisite star");
            }
            var authored = star.AuthoredStar;
            if (authored != null)
            {
                foreach (string id in authored.RequiredStarIds)
                    if (!Has(id)) reasons.Add(Loc.T($"前提の星『{Name(id)}』が未取得です。", $"Prerequisite star “{Name(id)}” is not acquired."));
                if (authored.RequiredAnyStarIds.Count > 0 && !authored.RequiredAnyStarIds.Any(Has))
                {
                    string names = string.Join(Loc.T(" または ", " or "), authored.RequiredAnyStarIds.Select(Name));
                    reasons.Add(Loc.T($"前提の星『{names}』のいずれかを取得してください。", $"Acquire one prerequisite star: {names}."));
                }
            }
            if (!connected)
            {
                string names = string.Join(Loc.T("、", ", "), layout.Nodes[index].Neighbors.Select(i => layout.Nodes[i].Talent?.Name.ToString() ?? Loc.T("始まりの星", "Starting star")));
                reasons.Add(Loc.T($"隣の星が未取得です：{names}。始まりにつながる隣の星を取得してください。",
                    $"Adjacent stars are not acquired: {names}. Acquire a neighbor connected to the start."));
            }
            if (star.IsKeystone)
            {
                int ranks = star.HeroKey != null ? Rules.TreeRanks(state, star.HeroKey) : Rules.RouteRanks(state, star.Route);
                if (ranks < Content.KeystoneRouteRequirement)
                    reasons.Add(Loc.T($"星の取得段数が不足しています：{ranks}/{Content.KeystoneRouteRequirement}段。",
                        $"Not enough acquired star ranks: {ranks}/{Content.KeystoneRouteRequirement}."));
                int mastery = Mastery.Level(state.Kills);
                if (star.HeroKey != null && mastery < HeroSigils.KeystoneMastery)
                    reasons.Add(Loc.T($"熟練度が不足しています：{mastery}/{HeroSigils.KeystoneMastery}。",
                        $"Not enough mastery: {mastery}/{HeroSigils.KeystoneMastery}."));
                if (!state.HasKeystone(star.Id) && state.KeystoneCount >= state.KeystoneSlotCount)
                    reasons.Add(Loc.T($"刻印の枠がいっぱいです：{state.KeystoneCount}/{state.KeystoneSlotCount}。",
                        $"Keystone slots are full: {state.KeystoneCount}/{state.KeystoneSlotCount}.") + NextSlotText(StarProgression.Points(state.StarXp)));
            }
            int cost = star.IsKeystone ? star.KeystoneDefinition?.Cost ?? Content.KeystoneCost : star.RankCost;
            if (freePoints < cost)
                reasons.Add(Loc.T($"ポイント不足：必要{cost}・残り{freePoints}・あと{cost - freePoints}。",
                    $"Not enough points: cost {cost}, remaining {freePoints}, short by {cost - freePoints}."));
            return string.Join("\n", reasons);
        }
    }
}

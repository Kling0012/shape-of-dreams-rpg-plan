using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>One displayed line and the talent ids (stars) that contribute to it.</summary>
    public sealed class StarSummaryLine
    {
        public string Text { get; set; } = "";
        public List<string> StarIds { get; } = new List<string>();
    }

    /// <summary>Lines grouped under a heading (a memory name, or a generic section).</summary>
    public sealed class StarSummaryGroup
    {
        public string Title { get; set; } = "";
        public List<StarSummaryLine> Lines { get; } = new List<StarSummaryLine>();
    }

    /// <summary>
    /// 星図で取得した効果の一覧（星だけの寄与。装備や契約は含めない）。
    /// 表示のたびではなく、配分・刻印・言語・装着が変わったときにだけ作る。
    /// </summary>
    public sealed class StarSummary
    {
        public int Spent { get; set; }
        public int Total { get; set; }
        public List<StarSummaryLine> Stats { get; } = new List<StarSummaryLine>();
        public List<StarSummaryLine> Powers { get; } = new List<StarSummaryLine>();
        public List<StarSummaryGroup> Memories { get; } = new List<StarSummaryGroup>();
        public List<StarSummaryLine> Choices { get; } = new List<StarSummaryLine>();
        public List<StarSummaryLine> Keystone { get; } = new List<StarSummaryLine>();

        public bool IsEmpty => Stats.Count == 0 && Powers.Count == 0 && Memories.Count == 0
            && Choices.Count == 0 && Keystone.Count == 0;

        /// <param name="isEquipped">記憶の型名が装着中か。null なら印を付けない。</param>
        public static StarSummary Compute(Profile p, string heroKey, Func<string, bool> isEquipped = null)
        {
            var summary = new StarSummary();
            var h = p.Hero(heroKey);
            summary.Spent = Rules.SpentPoints(h, heroKey);
            summary.Total = p.TalentPoints(heroKey);
            var definitions = HeroSigils.TreeFor(heroKey).ToDictionary(t => t.Id, StringComparer.Ordinal);

            var stats = new Dictionary<Stat, StarSummaryLine>();
            var statValues = new Dictionary<Stat, int>();
            var powers = new Dictionary<Power, StarSummaryLine>();
            var powerValues = new Dictionary<Power, int>();
            var memoryOrder = new List<string>();
            var memoryGroups = new Dictionary<string, StarSummaryGroup>(StringComparer.Ordinal);
            var linkLines = new Dictionary<string, KeyValuePair<StarSummaryLine, LinkDef>>(StringComparer.Ordinal);
            var linkOrder = new List<KeyValuePair<string, string>>(); // memory, link key

            StarSummaryGroup Group(string memory)
            {
                string key = memory ?? "";
                if (memoryGroups.TryGetValue(key, out var g)) return g;
                g = new StarSummaryGroup
                {
                    Title = memory == null ? Loc.T("その他", "Other") : Links.Name(memory).ToString(),
                };
                memoryGroups.Add(key, g);
                memoryOrder.Add(key);
                return g;
            }

            var pairLines = new List<KeyValuePair<string, StarSummaryLine>>();
            foreach (var kv in h.Talents.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value <= 0 || !definitions.TryGetValue(kv.Key, out var star) || star.IsKeystone
                    || !Rules.BelongsTo(star, heroKey)) continue;
                int rank = Math.Min(kv.Value, star.MaxRank);
                var t = star;
                if (star.IsChoice)
                {
                    if (!h.TalentChoices.TryGetValue(star.Id, out int choice) || choice < 0 || choice >= star.Choices.Count) continue;
                    t = star.Choices[choice];
                    var chosen = new StarSummaryLine
                    {
                        Text = star.Name + Loc.T("：", ": ") + t.Name + Loc.T($"（{rank}段）", $" ({rank} {(rank == 1 ? "rank" : "ranks")})")
                            + "\n" + Trim(t.Describe()),
                    };
                    chosen.StarIds.Add(star.Id);
                    summary.Choices.Add(chosen);
                }
                string memory = star.RouteMemory ?? t.RouteMemory;
                var pair = PairCombos.ForBridge(star.Id);
                if (pair != null)
                {
                    var entry = PairCombos.Activate(pair, h, rank);
                    bool a = isEquipped != null && isEquipped(pair.RouteA), b = isEquipped != null && isEquipped(pair.RouteB);
                    string mark = isEquipped == null ? "" : a && b ? "✓ " : "・ ";
                    var line = new StarSummaryLine
                    {
                        Text = mark + pair.Name + Loc.T($"（{rank}段）", $" ({rank} {(rank == 1 ? "rank" : "ranks")})")
                            + (entry == null
                                ? Loc.T("　<color=#888>両隣の4番目の星が未取得</color>", "  <color=#888>adjacent fourth stars not acquired</color>")
                                : "\n" + PairCombos.Describe(pair, rank))
                            + (isEquipped == null ? "" : "\n" + (a ? "✓ " : "・ ") + Links.Name(pair.RouteA) + Loc.T("を装着", " equipped")
                                + "\n" + (b ? "✓ " : "・ ") + Links.Name(pair.RouteB) + Loc.T("を装着", " equipped")),
                    };
                    line.StarIds.Add(star.Id);
                    pairLines.Add(new KeyValuePair<string, StarSummaryLine>(pair.RouteA, line));
                    continue;
                }
                if (t.Gimmick != null && t.Gimmick.Value > 0)
                {
                    string text = Gimmicks.Describe(t.Gimmick, memory, rank);
                    if (text.Length > 0)
                    {
                        var line = new StarSummaryLine { Text = text };
                        line.StarIds.Add(star.Id);
                        Group(memory).Lines.Add(line);
                    }
                }
                if (t.LinkPerRank != null)
                {
                    var link = new LinkDef { Requires = t.LinkPerRank.Requires, Kind = t.LinkPerRank.Kind, ValueMilli = t.LinkPerRank.ValueMilli * rank };
                    if (Links.Validate(link))
                    {
                        string linkMemory = memory ?? link.Requires[0];
                        string key = linkMemory + "|" + BuildAggregation.LinkKey(link);
                        if (linkLines.TryGetValue(key, out var existing))
                        {
                            existing.Value.ValueMilli += link.ValueMilli;
                            existing.Key.StarIds.Add(star.Id);
                        }
                        else
                        {
                            var line = new StarSummaryLine();
                            line.StarIds.Add(star.Id);
                            linkLines.Add(key, new KeyValuePair<StarSummaryLine, LinkDef>(line, link));
                            linkOrder.Add(new KeyValuePair<string, string>(linkMemory, key));
                        }
                    }
                }
                else if (t.IsPowerNode)
                {
                    powerValues.TryGetValue(t.RankPower, out int v);
                    powerValues[t.RankPower] = v + t.PerRank * rank;
                    if (!powers.TryGetValue(t.RankPower, out var line)) powers[t.RankPower] = line = new StarSummaryLine();
                    line.StarIds.Add(star.Id);
                }
                else if (t.PerRank != 0)
                {
                    statValues.TryGetValue(t.Stat, out int v);
                    statValues[t.Stat] = v + t.PerRank * rank;
                    if (!stats.TryGetValue(t.Stat, out var line)) stats[t.Stat] = line = new StarSummaryLine();
                    line.StarIds.Add(star.Id);
                }
                else if (t.ScopedModifier != null || t.NativeModifier != null || t.GimmickBoost > 0 || t.GimmickParameter.HasValue)
                {
                    var line = new StarSummaryLine
                    {
                        Text = Trim(t.Describe()) + (rank > 1 ? Loc.T($"　×{rank}段", $"  x{rank} ranks") : ""),
                    };
                    line.StarIds.Add(star.Id);
                    Group(memory).Lines.Add(line);
                }
            }

            foreach (var kv in stats.OrderBy(k => (int)k.Key))
            {
                kv.Value.Text = Content.FormatStat(kv.Key, statValues[kv.Key]);
                if (statValues[kv.Key] != 0) summary.Stats.Add(kv.Value);
            }
            foreach (var kv in powers.OrderBy(k => (int)k.Key))
            {
                kv.Value.Text = Content.FormatPower(kv.Key, powerValues[kv.Key]);
                summary.Powers.Add(kv.Value);
            }
            foreach (var item in linkOrder)
            {
                var entry = linkLines[item.Value];
                entry.Key.Text = Links.Describe(entry.Value, isEquipped);
                Group(item.Key).Lines.Add(entry.Key);
            }
            foreach (var item in pairLines) Group(item.Key).Lines.Add(item.Value);
            foreach (string key in memoryOrder.OrderBy(k => k.Length == 0 ? 1 : 0).ThenBy(k => memoryGroups[k].Title, StringComparer.Ordinal))
                summary.Memories.Add(memoryGroups[key]);

            if (h.Keystone != null && definitions.TryGetValue(h.Keystone, out var keystone) && keystone.IsKeystone
                && Rules.BelongsTo(keystone, heroKey))
            {
                var line = new StarSummaryLine { Text = keystone.Name + "\n" + keystone.Describe() };
                line.StarIds.Add(keystone.Id);
                summary.Keystone.Add(line);
            }
            return summary;
        }

        // Describe() appends "(max N ranks; ...)" which is noise in an aggregated list.
        private static string Trim(string text)
        {
            if (text == null) return "";
            foreach (string marker in new[] { "（最大", " (maximum " })
            {
                int i = text.LastIndexOf(marker, StringComparison.Ordinal);
                if (i > 0) text = text.Substring(0, i);
            }
            return text;
        }
    }
}

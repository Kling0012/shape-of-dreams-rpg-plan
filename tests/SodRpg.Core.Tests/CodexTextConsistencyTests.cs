using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 図鑑・記録まわりに出る説明文（日英）の機械的な一貫性を検査する（issue #256）。
    /// 数値は単位（%・パーセントポイント・秒・m）つきの値として集めて比較する。日本語で同じ値を
    /// 重複して書く表現差は許容し、片方の言語にだけある値・単位の違い・数値のずれを検出する。
    /// あわせて、プレースホルダ残り・全角記号・内部ID・効果説明の「です・ます」調を検査する。
    /// </summary>
    public sealed partial class CodexTextConsistencyTests
    {
        private sealed class Entry
        {
            public string Id, Kind, Ja, En;
        }

        [Fact]
        public void Codex_texts_match_between_languages_and_carry_no_placeholders()
        {
            var problems = new List<string>();

            foreach (var e in Corpus())
            {
                if (Regex.IsMatch(e.Ja, @"（\{|\{\d[^}]*\}|\{0\}|TODO|FIXME|\bTBD\b|�"))
                    problems.Add($"[placeholder] {e.Kind} {e.Id}: {e.Ja}");
                if (Regex.IsMatch(e.Ja, @"[＋−]"))
                    problems.Add($"[fullwidth-sign] {e.Kind} {e.Id}: {e.Ja}");
                if (Regex.IsMatch(e.Ja, @"\b(Mon_|St_|Gem_|Se_)[A-Za-z]"))
                    problems.Add($"[raw-id] {e.Kind} {e.Id}: {e.Ja}");
                // 効果説明は常体（#211）。星の二択の案内「選んでください」は操作の促しなので許す。
                if (!e.Kind.StartsWith("star", StringComparison.Ordinal) && PoliteEnding().IsMatch(e.Ja))
                    problems.Add($"[polite] {e.Kind} {e.Id}: {e.Ja}");
                CompareNumbers(e, problems);
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        // ---------------------------------------------------------------- 採集

        /// <summary>現在の言語を切り替えて両言語の文字列を1組にする。</summary>
        private static Entry E(string id, string kind, Func<string> text)
        {
            bool prev = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                string ja = text();
                Loc.Japanese = false;
                string en = text();
                return new Entry { Id = id, Kind = kind, Ja = ja, En = en };
            }
            finally { Loc.Japanese = prev; }
        }

        private static IEnumerable<Entry> Corpus()
        {
            foreach (var b in Content.Bases)
                yield return E(b.Id, "base", () => Content.FormatStat(b.ImplicitStat, b.ImplicitValue));
            foreach (var u in Content.Uniques)
            {
                yield return E(u.Id, "unique", () => Content.FormatPowerBullets(u.Powers.Count > 0 ? u.Powers[0].Power : Power.None, u.Powers.Count > 0 ? u.Powers[0].Value : 0));
                if (u.BossMove != null)
                    yield return E(u.Id, "unique.boss", () => EffectLayout.Bullets(BossProfiles.DescribeMove(u.BossMove)));
                if (u.Link != null)
                    yield return E(u.Id, "unique.link", () => Links.Describe(u.Link));
            }
            foreach (var s in Content.Sets)
                yield return E(s.Id, "set", s.Describe);
            foreach (Power p in Enum.GetValues(typeof(Power)).Cast<Power>())
            {
                if (p == Power.None || Content.PowerName(p) == "-") continue;
                if (CodexQuery.TryPowerRange(p, out int min, out int max))
                {
                    yield return E(p.ToString(), "power.min", () => Content.FormatPower(p, min));
                    yield return E(p.ToString(), "power.max", () => Content.FormatPower(p, max));
                }
            }
            foreach (var n in NamedItems.All)
                yield return E(n.Id, "named", () => Content.FormatPowerBullets(n.Powers.Count > 0 ? n.Powers[0].Power : Power.None, n.Powers.Count > 0 ? n.Powers[0].Value : 0));
            foreach (var ms in NamedItems.MiniSets)
                yield return E(ms.Id, "miniset", ms.Describe);
            foreach (var v in Variants.All)
                yield return E(v.Id ?? v.MonsterType, "variant", () => v.Description.ToString());
            foreach (NightmareAffix a in Enum.GetValues(typeof(NightmareAffix)).Cast<NightmareAffix>())
                if (a != NightmareAffix.None && Nightmares.AffixName(a) != "")
                    yield return E(a.ToString(), "nightmare", () => Nightmares.AffixDescription(a));
            foreach (var w in Waypoints.All)
                yield return E(w.Id.ToString(), "waypoint", () => w.Description.ToString());
            var profile = new Profile();
            foreach (DreamEvent ev in Enum.GetValues(typeof(DreamEvent)).Cast<DreamEvent>())
            {
                if (ev == DreamEvent.None) continue;
                yield return E(ev.ToString(), "event.action", () => DreamEvents.ActionLabel(ev));
                yield return E(ev.ToString(), "event", () => DreamEvents.Describe(ev, profile));
            }
            foreach (var d in DailyDream.All)
                yield return E(d.Id.ToString(), "daily", () => d.Description);
            foreach (var f in Feats.All)
                yield return E(f.Id, "feat", () => Feats.Describe(f));
            foreach (var pd in Pacts.All)
                yield return E(pd.Id.ToString(), "pact", () => Pacts.Describe(pd));
            foreach (BountyKind k in Enum.GetValues(typeof(BountyKind)).Cast<BountyKind>())
                yield return E(k.ToString(), "bounty", () => new Bounty { Kind = k, Target = 3 }.Describe());
            foreach (var t in HeroSigils.All)
            {
                int maxRank = t.MaxRank > 0 ? t.MaxRank : 1;
                yield return E(t.Id, "star", () => StarMapPresentation.DisplayDescription(t, 1));
                if (maxRank > 1)
                    yield return E(t.Id, "star.max", () => StarMapPresentation.DisplayDescription(t, maxRank));
                if (t.IsChoice)
                    foreach (var c in t.Choices)
                        yield return E(t.Id, "star.choice", () => StarMapPresentation.DisplayDescription(c, 1));
            }
        }

        [GeneratedRegex(@"です。|でした。|ました。|ません。|ください。|[いきぎしにみりえけせてねれめ]ます。")]
        private static partial Regex PoliteEnding();

        // ------------------------------------------------------------------ 数値

        private static void CompareNumbers(Entry e, List<string> problems)
        {
            var jaSet = new HashSet<string>(UnitValues(e.Ja, true).Select(p => p.value + p.unit));
            var enSet = new HashSet<string>(UnitValues(e.En, false).Select(p => p.value + p.unit));
            // 同じ値の重複は表現の差として許容し、値の集合で比較する（片方にだけある値・ずれた値・単位の違いは検出）。
            if (!jaSet.SetEquals(enSet))
            {
                var onlyJa = string.Join(",", jaSet.Except(enSet).OrderBy(x => x));
                var onlyEn = string.Join(",", enSet.Except(jaSet).OrderBy(x => x));
                problems.Add($"[numbers] {e.Kind} {e.Id}: ja-only({onlyJa}) en-only({onlyEn})\n  ja: {e.Ja}\n  en: {e.En}");
            }
        }

        private static IEnumerable<(string value, string unit)> UnitValues(string text, bool ja)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            if (!ja)
            {
                // 英語の "at 0, 0.2, and 0.4 seconds" のように、リストの最後だけに単位が付く形は各値に割り付ける。
                text = Regex.Replace(text,
                    @"(\d[\d.]*(?:\s*(?:,|and)\s*(?:and\s+)?\d[\d.]*)+)[-\s]*(seconds?|s)\b",
                    m => string.Join(", ", Regex.Split(m.Groups[1].Value, @"\s*(?:,|and)\s*(?:and\s+)?").Select(n => n + " second")));
            }
            foreach (Match m in Regex.Matches(text, ja
                ? @"(\d+(?:\.\d+)?)(%|パーセントポイント|秒|m)"
                : @"(\d+(?:\.\d+)?)[-\s]*(%|percentage points|seconds?|s\b|m\b)"))
            {
                string unit = m.Groups[2].Value switch
                {
                    "%" => "%",
                    "パーセントポイント" or "percentage points" => "pp",
                    "秒" or "second" or "seconds" or "s" => "s",
                    _ => "m",
                };
                yield return (m.Groups[1].Value, unit);
            }
        }
    }
}

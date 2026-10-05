using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>装備と星図の説明文が docs/description-style.md の機械的な規則を守っていることを、日本語・英語の両方で検査する。</summary>
    public sealed class DescriptionStyleTests
    {
        private static IEnumerable<(string Source, string Text)> GearTexts()
        {
            foreach (var power in Enum.GetValues(typeof(Power)).Cast<Power>().Where(p => p != Power.None))
            {
                int value = Math.Max(1, Math.Min(5, Content.PowerCap(power) > 0 ? Content.PowerCap(power) : 5));
                string text;
                try { text = Content.FormatPower(power, value); }
                catch (Exception) { continue; }
                yield return ("power " + power, text);
            }
            foreach (var unique in Content.Uniques)
            {
                foreach (var part in unique.Powers) yield return ("unique " + unique.Id + " " + part.Power, Content.FormatPower(part.Power, part.Value));
                if (unique.BossMove != null) yield return ("boss move " + unique.Id, BossProfiles.DescribeMove(unique.BossMove));
                if (unique.Link != null) yield return ("unique link " + unique.Id, Links.Describe(unique.Link));
            }
            foreach (var set in Content.Sets) yield return ("set " + set.Id, set.Describe());
        }

        private static IEnumerable<(string Source, string Text)> StarTexts()
        {
            foreach (var star in HeroSigils.All)
            {
                if (star.IsChoice)
                {
                    yield return ("choice " + star.Id, StarMapPresentation.ChoiceDescription(star, -1, 0));
                    continue;
                }
                yield return ("star " + star.Id, StarMapPresentation.EffectDescription(star));
            }
        }

        private static List<string> Violations(bool japanese, Func<IEnumerable<(string Source, string Text)>> texts)
        {
            bool previous = Loc.Japanese;
            var problems = new List<string>();
            try
            {
                Loc.Japanese = japanese;
                foreach (var (source, text) in texts().ToList())
                {
                    if (string.IsNullOrEmpty(text)) { problems.Add(source + ": empty description"); continue; }
                    foreach (var rule in Rules(japanese))
                    {
                        var match = rule.Pattern.Match(text);
                        if (match.Success)
                        {
                            int from = Math.Max(0, match.Index - 20);
                            problems.Add(source + ": " + rule.Name + " … " + text.Substring(from, Math.Min(text.Length - from, match.Length + 40)).Replace("\n", "\\n"));
                        }
                    }
                }
            }
            finally { Loc.Japanese = previous; }
            return problems.Distinct().ToList();
        }

        private static IEnumerable<(string Name, Regex Pattern)> Rules(bool japanese)
        {
            yield return ("全角の＋−を使わない", new Regex("[＋−]"));
            yield return ("内部IDを出さない", new Regex(@"\b(Mon|St|Gem|Se)_[A-Za-z0-9_]+"));
            yield return ("連続した空白を使わない", new Regex(@"[ 　]{2,}"));
            if (japanese)
            {
                yield return ("です・ますで終えない", new Regex("(です|ます)[。）（\n]|(です|ます)$"));
                yield return ("0%の確率を出さない", new Regex(@"(?<![0-9.])0%の確率"));
                yield return ("「さらに0%」を出さない", new Regex(@"さらに0%"));
                yield return ("単位のない上限", new Regex(@"上限[0-9.]+(?![0-9.%m秒回体つ段個倍])"));
                yield return ("「 を」「。 」の空白", new Regex("[ 　]を|。[ 　]"));
                yield return ("属性の数え方は「つ」", new Regex("(火|冷気|光|闇)(付与 ?\\+?|を)[0-9]+個"));
            }
            else
            {
                yield return ("unitless cap", new Regex(@"(?:capped at|cap:?) [0-9]+(?:\.[0-9]+)?(?![0-9]|\.[0-9])(?=[).;,])"));
                yield return ("0% chance", new Regex(@"(?<![0-9.])0% chance"));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Gear_descriptions_follow_the_style_rules(bool japanese)
        {
            var problems = Violations(japanese, GearTexts);
            Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)) + "\n(" + problems.Count + " violations)");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Star_descriptions_follow_the_style_rules(bool japanese)
        {
            var problems = Violations(japanese, StarTexts);
            Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)) + "\n(" + problems.Count + " violations)");
        }
    }
}

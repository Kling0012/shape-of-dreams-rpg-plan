using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>コード見直し5周目：装備比較の固有効果、工房の「次の段」表示、案内文の数値。</summary>
    public sealed class ReviewRound5Tests
    {
        [Fact]
        public void Comparison_counts_the_power_added_by_severance()
        {
            var cur = Loot.RollRelic(new Rng(501), Rarity.Epic, 10);
            var sel = cur.Clone();
            var added = Enum.GetValues(typeof(Power)).Cast<Power>()
                .First(p => p != Power.None && sel.Powers.All(x => x.Power != p));
            sel.Forbidden = new ForbiddenMark
            {
                Pattern = ForbiddenPattern.Severance,
                RemovedStat = (int)sel.Affixes[0].Stat,
                AddedPower = added,
                AddedValue = 10,
            };

            var diff = RelicComparison.Compare(sel, cur);

            Assert.Equal(new[] { added }, diff.GainedPowers);
            Assert.Empty(diff.LostPowers);
            Assert.Equal(new[] { added }, RelicComparison.Compare(cur, sel).LostPowers);
        }

        [Fact]
        public void Comparison_stat_deltas_skip_zero_and_follow_stat_order()
        {
            var a = Loot.RollRelic(new Rng(502), Rarity.Epic, 10);
            var b = Loot.RollRelic(new Rng(503), Rarity.Epic, 10);
            var diff = RelicComparison.Compare(a, b);
            Assert.All(diff.StatDeltas, d => Assert.NotEqual(0, d.Value));
            Assert.Equal(diff.StatDeltas.Select(d => d.Stat).OrderBy(s => s), diff.StatDeltas.Select(d => d.Stat));
            Assert.Empty(RelicComparison.Compare(a, a.Clone()).StatDeltas);
        }

        [Fact]
        public void Workshop_level_helpers_match_the_profile_values()
        {
            var p = Profile.CreateNew(504);
            for (int level = 0; level <= 3; level++)
            {
                p.Upgrades[Upgrade.BigSatchel] = level;
                p.Upgrades[Upgrade.BountyReroll] = level;
                Assert.Equal(Workshop.SatchelCapacity(p), Workshop.SatchelCapacityAt(level));
                Assert.Equal(Workshop.RerollsPerRun(p), Workshop.RerollsAt(level));
            }
        }

        [Fact]
        public void Keystone_hint_uses_the_route_requirement_in_both_languages()
        {
            var hint = Onboarding.All.Single(h => h.Id == Hint.KeystoneReady);
            Assert.Contains(Content.KeystoneRouteRequirement + "ポイント", hint.Body.Ja);
            Assert.Contains(Content.KeystoneRouteRequirement + "+ points", hint.Body.En);
        }
    }
}

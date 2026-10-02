using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    /// <summary>遊ぶのに十分な量があるか（内容を削りすぎたら落ちる）。数は出力に記録する。</summary>
    public class ContentCountTests
    {
        private readonly ITestOutputHelper _out;
        public ContentCountTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Content_volume_is_enough_for_many_runs()
        {
            var slots = (Slot[])Enum.GetValues(typeof(Slot));
            int affixes = slots.Sum(s => Content.AffixPool(s).Count);
            int powers = slots.Sum(s => Content.PowerPool(s).Count);
            var counts = new (string Name, int Count, int Min)[]
            {
                ("bases", Content.Bases.Count, 150),
                ("uniques", Content.Uniques.Count, 298),
                ("sets", Content.Sets.Count, 12),
                ("talents", Content.Talents.Count, 15),
                ("keystones", Content.Talents.Count(t => t.IsKeystone), 3),
                ("heroSigils", HeroSigils.All.Count, 4),
                ("affixes", affixes, 32),
                ("powers", powers, 9),
                ("pacts", Pacts.All.Count, 20),
                ("nightmareAffixes", Nightmares.AllAffixes.Length, 4),
                ("dailyDreams", DailyDream.All.Count, 30),
                ("dreamEvents", Enum.GetValues(typeof(DreamEvent)).Length, 3),
                ("bountyKinds", Enum.GetValues(typeof(BountyKind)).Length, 6),
                ("hints", Onboarding.All.Count, 5),
                ("workshop", Workshop.All.Count, 3),
            };
            foreach (var c in counts)
            {
                _out.WriteLine($"{c.Name}: {c.Count}");
                Assert.True(c.Count >= c.Min, $"{c.Name} {c.Count} < {c.Min}");
            }
        }

        [Fact]
        public void Every_set_has_weapon_armor_and_charm_and_covers_all_four_elements()
        {
            foreach (var set in Content.Sets)
            {
                var pieces = Content.Uniques.Where(u => u.SetId == set.Id).ToList();
                Assert.Equal(3, pieces.Count);
                // v1.22：新しい枠を使うセットもある。どのセットも3つの別々の枠。
                var slots = pieces.Select(u => { Assert.True(Content.TryGetBase(u.BaseId, out var b)); return b.Slot; }).ToList();
                Assert.Equal(3, slots.Distinct().Count());
            }
            var elements = Content.Sets.SelectMany(s => s.ThreePiece).Select(pw => pw.Power).ToHashSet();
            Assert.Subset(elements, new[] { Power.Ember, Power.Frost, Power.Radiance, Power.Umbra }.ToHashSet());
        }
    }
}

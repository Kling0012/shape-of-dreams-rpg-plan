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
                ("bases", Content.Bases.Count, 360),
                ("uniques", Content.Uniques.Count, 1190), // Includes all reviewed P37 content.
                ("sets", Content.Sets.Count, 48), // Includes all reviewed P37 content.
                ("talents", Content.Talents.Count, 15),
                ("keystones", Content.Talents.Count(t => t.IsKeystone), 3),
                ("heroSigils", HeroSigils.All.Count, 4),
                ("affixes", affixes, 32),
                ("powers", powers, 9),
                ("pacts", Pacts.All.Count, 40),
                ("nightmareAffixes", Nightmares.AllAffixes.Length, 20),
                ("monsterVariants", Variants.All.Count, 30),
                ("dailyDreams", DailyDream.All.Count, 60),
                ("dreamEvents", Enum.GetValues(typeof(DreamEvent)).Length, 26),
                ("bountyKinds", Enum.GetValues(typeof(BountyKind)).Length, 44),
                ("hints", Onboarding.All.Count, 5),
                ("feats", Feats.All.Count, 90),
                ("workshop", Workshop.All.Count, 6),
            };
            foreach (var c in counts)
            {
                _out.WriteLine($"{c.Name}: {c.Count}");
                Assert.True(c.Count >= c.Min, $"{c.Name} {c.Count} < {c.Min}");
            }
        }

    }
}

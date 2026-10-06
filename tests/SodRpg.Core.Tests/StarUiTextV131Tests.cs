using System;
using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarUiTextV131Tests
    {
        private static readonly Regex Japanese = new Regex(@"[぀-ヿ一-鿿]");
        private static readonly string[] Heroes = HeroSigils.All.Select(t => t.HeroKey).Distinct().ToArray();


        private static void WithLanguage(bool japanese, Action action)
        {
            bool previous = Loc.Japanese;
            try { Loc.Japanese = japanese; action(); }
            finally { Loc.Japanese = previous; }
        }




        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_registered_cluster_of_every_hero_has_a_readable_name(bool japanese) => WithLanguage(japanese, () =>
        {
            foreach (string hero in Heroes)
                foreach (var cluster in StarMapClusters.Build(HeroTreeLayout.ForHero(hero)))
                {
                    string name = cluster.DisplayName.ToString();
                    Assert.False(string.IsNullOrWhiteSpace(name), hero + " " + cluster.Id);
                    Assert.DoesNotContain(".cluster", name, StringComparison.Ordinal);
                    Assert.DoesNotContain(".migration", name, StringComparison.Ordinal);
                    Assert.DoesNotContain("h.", name, StringComparison.Ordinal);
                    if (japanese) Assert.Matches(Japanese, name);
                }
        });






        [Fact]
        public void Label_cells_with_explicit_size_separate_wide_names_and_keep_the_default_unchanged()
        {
            Assert.Equal(StarMapMath.LabelCell(100f, 40f), StarMapMath.LabelCell(100f, 40f, 90f, 18f));
            Assert.Equal(StarMapMath.LabelCell(5f, 5f, 112f, 22f), StarMapMath.LabelCell(100f, 20f, 112f, 22f));
            Assert.NotEqual(StarMapMath.LabelCell(5f, 5f, 112f, 22f), StarMapMath.LabelCell(115f, 5f, 112f, 22f));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarMapMath.LabelCell(0f, 0f, 0f, 22f));
        }



    }
}

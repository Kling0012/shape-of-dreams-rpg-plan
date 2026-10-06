using System;
using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>星図の画面テキスト：星団の表示名、刻印の条件文、重複しない状態文、拒否メッセージの日本語化。</summary>
    public class StarUiTextV131Tests
    {
        private static readonly Regex Japanese = new Regex(@"[぀-ヿ一-鿿]");
        private static readonly string[] Heroes = HeroSigils.All.Select(t => t.HeroKey).Distinct().ToArray();

        private static HeroTreeLayout CetusExamples()
        {
            var baseline = HeroSigils.TreeFor("Hero_Cetus").Where(t => t.Cluster == null).ToArray();
            var definitions = StarClusters.All.Where(c => c.HeroKey == "Hero_Cetus").Select(c => new StarClusterDef
            {
                Id = c.Id, HeroKey = c.HeroKey, Anchor = c.Anchor, Shape = c.Shape, Stars = c.Stars,
                Region = new ClusterRegion { Kind = c.Region.Kind, Id = c.Region.Id }
            }).ToArray();
            return HeroTreeLayout.ForTalents(baseline.Concat(StarClusters.Generate(definitions, baseline)).ToArray());
        }

        private static void WithLanguage(bool japanese, Action action)
        {
            bool previous = Loc.Japanese;
            try { Loc.Japanese = japanese; action(); }
            finally { Loc.Japanese = previous; }
        }



        [Fact]
        public void Explicit_cluster_name_wins_over_the_derived_one()
        {
            var layout = CetusExamples();
            var def = layout.Nodes.First(n => n.Talent?.Cluster != null).Talent.Cluster;
            var named = new StarClusterDef { Id = def.Id, HeroKey = def.HeroKey, Region = def.Region, Anchor = def.Anchor,
                Name = new Txt("氷の血脈", "Icy Veins") };
            Assert.Equal("氷の血脈", StarMapClusters.DisplayNameFor(layout, named, new Txt("あ", "A")).Ja);
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
        public void Keystone_requirement_states_the_real_numbers()
        {
            WithLanguage(true, () =>
            {
                string text = StarMapPresentation.KeystoneRequirement(true, 6, 3, 3, 10, true);
                Assert.Contains("合計6段", text);
                Assert.Contains("熟練度3", text);
                Assert.Contains("今は星3段・熟練度10", text);
                Assert.DoesNotContain("つながっている必要", text);
                Assert.Contains("つながっている必要", StarMapPresentation.KeystoneRequirement(true, 6, 6, 3, 3, false));
                string route = StarMapPresentation.KeystoneRequirement(false, 6, 2, 0, 0, true);
                Assert.DoesNotContain("熟練度", route);
                Assert.Contains("今は星2段", route);
            });
            WithLanguage(false, () =>
            {
                string text = StarMapPresentation.KeystoneRequirement(true, 6, 3, 3, 10, true);
                Assert.Contains("6 ranks", text);
                Assert.Contains("mastery 3", text);
                Assert.Contains("mastery 10", text);
            });
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_keystone_in_every_tree_displays_effect_and_cost_without_drawback_sections(bool japanese) => WithLanguage(japanese, () =>
        {
            int keys = 0;
            foreach (string hero in Heroes)
                foreach (var node in HeroTreeLayout.ForHero(hero).Nodes.Where(n => n.Talent != null && n.Talent.IsKeystone))
                {
                    keys++;
                    string text = StarMapPresentation.KeystoneDescription(node.Talent);
                    Assert.DoesNotContain("代償", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("Drawback", text, StringComparison.OrdinalIgnoreCase);
                    if (node.Talent.KeystoneDefinition == null)
                        Assert.Contains(Content.FormatPower(node.Talent.Power, node.Talent.PowerValue), text);
                    // 必要ポイントの行は1回だけ（ツリー側の説明で繰り返さない）。
                    Assert.Single(Regex.Matches(text, japanese ? "必要ポイント" : "Cost:").Cast<Match>());
                }
            Assert.True(keys > 0);
        });


        [Fact]
        public void Choice_option_body_has_name_and_effect_without_state_marker()
        {
            WithLanguage(true, () =>
            {
                var choice = CetusExamples().Nodes.Select(n => n.Talent).First(t => t != null && t.IsChoice);
                for (int option = 0; option < 2; option++)
                {
                    string body = StarMapPresentation.ChoiceOptionBody(choice, option);
                    Assert.Contains(choice.Choices[option].Name.Ja, body);
                    Assert.DoesNotContain("選択中", body);
                    Assert.DoesNotContain("未選択", body);
                }
                Assert.Throws<ArgumentOutOfRangeException>(() => StarMapPresentation.ChoiceOptionBody(choice, 2));
            });
        }

        [Fact]
        public void Label_cells_with_explicit_size_separate_wide_names_and_keep_the_default_unchanged()
        {
            Assert.Equal(StarMapMath.LabelCell(100f, 40f), StarMapMath.LabelCell(100f, 40f, 90f, 18f));
            Assert.Equal(StarMapMath.LabelCell(5f, 5f, 112f, 22f), StarMapMath.LabelCell(100f, 20f, 112f, 22f));
            Assert.NotEqual(StarMapMath.LabelCell(5f, 5f, 112f, 22f), StarMapMath.LabelCell(115f, 5f, 112f, 22f));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarMapMath.LabelCell(0f, 0f, 0f, 22f));
        }

        [Fact]
        public void Rule_message_table_has_japanese_and_english_for_every_entry()
        {
            Assert.NotEmpty(RuleMessages.All);
            foreach (var message in RuleMessages.All)
            {
                Assert.Matches(Japanese, message.Ja);
                Assert.False(string.IsNullOrWhiteSpace(message.En));
                Assert.DoesNotMatch(Japanese, message.En);
            }
        }

        [Fact]
        public void Rejections_reach_the_player_in_japanese()
        {
            WithLanguage(true, () =>
            {
                var p = Profile.CreateNew(1);
                var layout = HeroTreeLayout.ForHero("Hero_Cetus");
                var keystone = layout.Nodes.First(n => n.Talent != null && n.Talent.IsKeystone).Talent;
                var error = Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, "Hero_Cetus", keystone.Id));
                Assert.Matches(Japanese, error.Message);
                var star = layout.Nodes.First(n => n.Talent != null && !n.Talent.IsKeystone && !n.Talent.IsChoice && n.Talent.MaxRank > 0).Talent;
                var unreachable = Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "Hero_Cetus", star.Id));
                Assert.Matches(Japanese, unreachable.Message);
            });
        }

    }
}

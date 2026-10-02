using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.26：記憶・エッセンス・旅人に連携する固有品の仕組み。</summary>
    public class LinksV126Tests
    {
        private static LinkDef Link(LinkKind kind, int value, params string[] requires) =>
            new LinkDef { Kind = kind, Value = value, Requires = requires };

        [Fact]
        public void Single_memory_link_is_satisfied_only_while_that_memory_is_equipped()
        {
            var link = Link(LinkKind.Attune, 20, "St_L_Blizzard");
            Assert.True(Links.Satisfied(link, "Hero_Vesper", new[] { "St_L_Blizzard" }, null));
            Assert.True(Links.Satisfied(link, "Hero_Vesper", new[] { "St_Q_CruelSun", "St_L_Blizzard" }, null));
            Assert.False(Links.Satisfied(link, "Hero_Vesper", new[] { "St_Q_CruelSun" }, null));
            Assert.False(Links.Satisfied(link, "Hero_Vesper", Array.Empty<string>(), null));
        }

        [Fact]
        public void Pair_needs_both_a_memory_and_an_essence()
        {
            var link = Link(LinkKind.Attune, 22, "St_L_LightExplosion", "Gem_L_PureWhite");
            Assert.False(Links.Satisfied(link, "Hero_Vesper", new[] { "St_L_LightExplosion" }, Array.Empty<string>()));
            Assert.False(Links.Satisfied(link, "Hero_Vesper", Array.Empty<string>(), new[] { "Gem_L_PureWhite" }));
            Assert.True(Links.Satisfied(link, "Hero_Vesper",
                new[] { "St_L_LightExplosion", "St_R_Parry" }, new[] { "Gem_L_Perfect", "Gem_L_PureWhite" }));
        }

        [Fact]
        public void Triple_needs_the_traveler_and_both_traveler_memories()
        {
            var link = Link(LinkKind.Guard, 18, "Hero_Mist", "St_Q_Fleche", "St_R_Parry");
            Assert.True(Links.Satisfied(link, "Hero_Mist", new[] { "St_R_Parry", "St_Q_Fleche" }, null));
            Assert.False(Links.Satisfied(link, "Hero_Vesper", new[] { "St_R_Parry", "St_Q_Fleche" }, null));
            Assert.False(Links.Satisfied(link, "Hero_Mist", new[] { "St_Q_Fleche" }, null));
        }

        [Fact]
        public void Traveler_conditions_follow_the_hero_type_name()
        {
            var link = Link(LinkKind.MemorySurge, 30, "Hero_Lacerta", "St_D_DoubleTap");
            Assert.True(Links.Satisfied(link, "Hero_Lacerta", new[] { "St_D_DoubleTap" }, null));
            Assert.False(Links.Satisfied(link, "Hero_Husk", new[] { "St_D_DoubleTap" }, null));
            // 旅人とエッセンスの組み合わせも同じ判定で扱う。
            var gem = Link(LinkKind.Attune, 20, "Hero_Yubar", "Gem_L_Perfect");
            Assert.True(Links.Satisfied(gem, "Hero_Yubar", null, new[] { "Gem_L_Perfect" }));
            Assert.False(Links.Satisfied(gem, "Hero_Yubar", null, new[] { "Gem_L_MetalCrystal" }));
        }

        [Fact]
        public void Guiding_compass_counts_charged_and_not_charged_as_one_essence()
        {
            var link = Link(LinkKind.Attune, 15, "Gem_U_GuidingCompass_NotCharged");
            Assert.True(Links.Satisfied(link, "Hero_Vesper", null, new[] { "Gem_U_GuidingCompass_Charged" }));
            Assert.True(Links.Satisfied(link, "Hero_Vesper", null, new[] { "Gem_U_GuidingCompass_NotCharged" }));
            // 条件に充電後の型名を書いても、充電前で装着していれば満たす。
            var charged = Link(LinkKind.Attune, 15, "Gem_U_GuidingCompass_Charged");
            Assert.True(Links.Satisfied(charged, "Hero_Vesper", null, new[] { "Gem_U_GuidingCompass_NotCharged" }));
            Assert.True(Links.IsEssence("Gem_U_GuidingCompass_Charged"));
            Assert.Equal(Links.Compass, Links.Canon("Gem_U_GuidingCompass_Charged"));
            // 充電前後は1つなので、両方を条件に書くと重複になる。
            Assert.False(Links.Validate(Link(LinkKind.Attune, 15, "Gem_U_GuidingCompass_NotCharged", "Gem_U_GuidingCompass_Charged")));
        }

        [Fact]
        public void Validate_rejects_unknown_targets_wrong_counts_duplicates_and_memoryless_memory_kinds()
        {
            Assert.True(Links.Validate(Link(LinkKind.Attune, 20, "St_L_Blizzard")));
            Assert.True(Links.Validate(Link(LinkKind.Guard, 20, "Gem_L_Perfect"))); // 守りは記憶を要さない
            Assert.False(Links.Validate(Link(LinkKind.Attune, 20, "St_X_Unknown")));
            Assert.False(Links.Validate(Link(LinkKind.Attune, 20, "Hero_Vesper", "Hero_Mist", "Hero_Husk", "Hero_Husk")));
            Assert.False(Links.Validate(Link(LinkKind.Attune, 20, "St_L_Blizzard", "St_L_Blizzard")));
            Assert.False(Links.Validate(Link(LinkKind.MemoryHaste, 30, "Gem_L_PureWhite"))); // 記憶が要る
            Assert.False(Links.Validate(Link(LinkKind.MemorySurge, 30, "Hero_Lacerta")));
            Assert.False(Links.Validate(Link(LinkKind.None, 30, "St_L_Blizzard")));
            Assert.False(Links.Validate(null));
        }

        [Theory]
        [InlineData(LinkKind.Attune, 1, 25)]
        [InlineData(LinkKind.Attune, 2, 40)]
        [InlineData(LinkKind.Attune, 3, 55)]
        [InlineData(LinkKind.Guard, 1, 25)]
        [InlineData(LinkKind.Guard, 2, 40)]
        [InlineData(LinkKind.Guard, 3, 55)]
        [InlineData(LinkKind.MemoryHaste, 1, 50)]
        [InlineData(LinkKind.MemoryHaste, 2, 80)]
        [InlineData(LinkKind.MemoryHaste, 3, 110)]
        [InlineData(LinkKind.MemorySurge, 1, 40)]
        [InlineData(LinkKind.MemorySurge, 2, 64)]
        [InlineData(LinkKind.MemorySurge, 3, 88)]
        public void Caps_grow_with_the_requirement_count(LinkKind kind, int count, int expected)
        {
            Assert.Equal(expected, Links.Cap(kind, count));
            Assert.Equal(0, Links.Cap(LinkKind.None, count));
        }


        [Fact]
        public void Build_encode_and_decode_round_trip_keeps_valid_links()
        {
            var b = new Build();
            b.Links.Add(Link(LinkKind.MemoryHaste, 30, "St_L_Blizzard"));
            b.Links.Add(Link(LinkKind.Attune, 22, "St_L_LightExplosion", "Gem_L_PureWhite"));
            b.Links.Add(Link(LinkKind.Guard, 18, "Hero_Mist", "St_Q_Fleche", "St_R_Parry"));
            var d = Build.Decode(b.Encode());
            Assert.NotNull(d);
            Assert.Equal(3, d.Links.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(b.Links[i].Kind, d.Links[i].Kind);
                Assert.Equal(b.Links[i].Value, d.Links[i].Value);
                Assert.Equal(b.Links[i].Requires, d.Links[i].Requires);
            }
        }

        [Fact]
        public void Decode_drops_unknown_targets_invalid_links_and_overflow()
        {
            // 未知の対象と、記憶を要する効果に記憶がない物は捨てる。
            var d = Build.Decode("s:;p:;h:0;l:3:20:St_L_Blizzard,3:20:St_X_Unknown,1:30:Gem_L_PureWhite,0:10:St_L_Blizzard");
            Assert.NotNull(d);
            Assert.Single(d.Links);
            Assert.Equal(LinkKind.Attune, d.Links[0].Kind);

            // 値は条件の数に対する上限で切る。
            var clamped = Build.Decode("s:;p:;h:0;l:3:999:St_L_Blizzard");
            Assert.Equal(Links.EquippedCap(LinkKind.Attune, 1), clamped.Links[0].Value); // 覚醒Ⅲまでを含む上限（issue #14）

            // 旅人ルートを含む上限までは保ち、それ以上は受信しない。
            var many = new Build();
            foreach (string m in HeroSigils.All.Where(t => t.RouteMemory != null).Select(t => t.RouteMemory).Distinct().Take(Links.MaxLinks + 1))
                many.Links.Add(Link(LinkKind.Attune, 10, m));
            Assert.Equal(Links.MaxLinks + 1, many.Links.Count);
            var trimmed = Build.Decode(many.Encode());
            Assert.Equal(Links.MaxLinks, trimmed.Links.Count);

            // 形式が壊れた l 区間は Build 全体として拒否する（s:p: と同じ扱い）。
            Assert.Null(Build.Decode("s:;p:;h:0;l:3:20"));
        }

        [Fact]
        public void Awakening_multiplies_the_link_value_but_enhance_does_not()
        {
            var unique = Content.Uniques.First(u => u.Id == "unique.link2_penniless_tycoon");
            var relic = Loot.RollUnique(new Rng(7), unique, 1);
            var p = Profile.CreateNew(12);
            p.Stash.Add(relic);
            Rules.Equip(p, "Hero_Vesper", relic.Uid);
            Rules.BeginRun(p, "Hero_Vesper");

            var plain = Build.Compute(p, "Hero_Vesper", 0);
            var link = Assert.Single(plain.Links);
            Assert.Equal(26, link.Value);

            relic.Enhance = 3; // 強化は連携の値を伸ばさない
            Assert.Equal(26, Assert.Single(Build.Compute(p, "Hero_Vesper", 0).Links).Value);

            relic.Awakened = true; // 覚醒（旧来の覚醒と同じ覚醒Ⅱ）は 150% を掛ける
            Assert.Equal(26 * Content.AwakenPowerPctAt(relic.AwakenLevel) / 100, Assert.Single(Build.Compute(p, "Hero_Vesper", 0).Links).Value);
        }

        [Fact]
        public void Sample_link_uniques_are_valid_and_well_formed()
        {
            var linked = Content.Uniques.Where(u => u.Link != null).ToList();
            Assert.Equal(121, linked.Count);
            Assert.All(linked, u => Assert.True(Links.Validate(u.Link), u.Id));
            Assert.All(linked, u => Assert.True(u.Link.Value <= Links.Cap(u.Link.Kind, u.Link.Requires.Length), u.Id));
            Assert.All(linked, u => Assert.Equal(2, u.Powers.Count)); // 通常どおり2つの固有効果
            // 樹形図のすべての枝に1つ以上（M=記憶、E=エッセンス、T=旅人）
            string Branch(LinkDef l) => string.Concat(l.Requires.Select(t => t.StartsWith("St_") ? "M" : t.StartsWith("Gem_") ? "E" : "T").OrderBy(c => c));
            var branches = linked.Select(u => Branch(u.Link)).ToHashSet();
            foreach (string br in new[] { "M", "E", "T", "MM", "EM", "EE", "MT", "ET", "EMT", "MMT", "EMM", "EEM" })
                Assert.Contains(br, branches);
            foreach (var kind in new[] { LinkKind.Attune, LinkKind.Guard, LinkKind.MemoryHaste, LinkKind.MemorySurge })
                Assert.Contains(linked, u => u.Link.Kind == kind);
        }
    }
}

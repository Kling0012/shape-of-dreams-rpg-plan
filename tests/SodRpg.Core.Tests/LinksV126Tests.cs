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
                Assert.Equal(b.Links[i].Requires.OrderBy(x => x, StringComparer.Ordinal),
                    d.Links[i].Requires.OrderBy(x => x, StringComparer.Ordinal));
            }
        }

        [Fact]
        public void Decode_rejects_unknown_targets_invalid_links_and_overflow_atomically()
        {
            var d = Build.Decode("s:;p:;h:0;l:3:20:St_L_Blizzard,3:20:St_X_Unknown,1:30:Gem_L_PureWhite,0:10:St_L_Blizzard");
            Assert.Null(d);

            var sum = Build.Decode("s:;p:;h:0;l:3:999000:St_L_Blizzard");
            Assert.Equal(999m, Assert.Single(sum.Links).Value);
            Assert.Null(Build.Decode("l:3:2147483647:St_L_Blizzard"));

            // 形式が壊れた l 区間は Build 全体として拒否する（s:p: と同じ扱い）。
            Assert.Null(Build.Decode("s:;p:;h:0;l:3:20"));
        }

    }
}

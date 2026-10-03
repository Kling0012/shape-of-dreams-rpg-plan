using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.29 パス1：既存の効果と属性の反応だけで組んだ固有品52個とセット4種。第2回入力の全件照合は ContentTableV129Tests。</summary>
    public class UniquesV129Tests
    {
        /// <summary>パス1で取り込んだ固有品（両方の効果が既存の Power か反応4つ）。</summary>
        private static readonly HashSet<string> Pass1Ids = new HashSet<string>
        {
            "unique.w_palmcannon",
            "unique.w_gnaw_cane",
            "unique.w_fireball_rod",
            "unique.w_steam_katar",
            "unique.w_boiling_halberd",
            "unique.w_waning_scythe",
            "unique.w_dimmed_cudgel",
            "unique.w_embercoal_sword",
            "unique.w_ashfall_blades",
            "unique.w_icicrystal_pike",
            "unique.w_dawnfrost_scepter",
            "unique.w_everwick_katar",
            "unique.w_glassfrost_staff",
            "unique.w_umbral_ember_bow",
            "unique.hd_steam_hood",
            "unique.hd_eclipse_hood",
            "unique.hd_cinder_hood",
            "unique.hd_icicrystal_crown",
            "unique.a_steam_robe",
            "unique.a_eclipse_cloak",
            "unique.a_cinder_coat",
            "unique.a_icicrystal_robe",
            "unique.a_icicrystal_cuirass",
            "unique.a_cinder_robe",
            "unique.a_steam_harness",
            "unique.h_baptism_sunfists",
            "unique.h_hellfire_grips",
            "unique.h_chillward_mitts",
            "unique.h_eclipse_tales",
            "unique.h_steam_mitts",
            "unique.h_geyser_grips",
            "unique.h_eclipse_wraps",
            "unique.h_eclipse_fangs",
            "unique.h_cinder_veil",
            "unique.h_cinder_wildfire",
            "unique.h_cinder_shatter",
            "unique.h_icicrystal_mitts",
            "unique.h_icicrystal_light",
            "unique.h_icicrystal_star",
            "unique.f_steam_trail_boots",
            "unique.f_steam_dancers",
            "unique.f_steam_greaves",
            "unique.f_eclipse_footfalls",
            "unique.f_eclipse_dawnmist",
            "unique.f_cinderstep_boots",
            "unique.f_ashrun_boots",
            "unique.f_icecrystal_skates",
            "unique.f_crystalstep_shoes",
            "unique.c_steam_charm",
            "unique.c_eclipse_charm",
            "unique.c_cinder_charm",
            "unique.c_icicrystal_charm",
        };

        private static readonly string[] Pass1SetIds = { "set.steamweave", "set.eclipserite", "set.cinderfall", "set.icicanticle" };

        [Fact]
        public void Pass1_entered_52_uniques_and_4_sets()
        {
            Assert.Equal(52, Pass1Ids.Count);
            Assert.All(Pass1Ids, id => Assert.True(Content.TryGetUnique(id, out _), id));
            int bySlot(Slot slot) => Content.Uniques.Count(u => Pass1Ids.Contains(u.Id) && Content.GetBase(u.BaseId).Slot == slot);
            Assert.Equal(14, bySlot(Slot.Weapon));
            Assert.Equal(4, bySlot(Slot.Head));
            Assert.Equal(7, bySlot(Slot.Armor));
            Assert.Equal(14, bySlot(Slot.Hands));
            Assert.Equal(9, bySlot(Slot.Feet));
            Assert.Equal(4, bySlot(Slot.Charm));
            Assert.Equal(1171, Content.Uniques.Count); // v1.29 pass 2; P37 content remains deferred.
            Assert.Equal(1030, Content.Uniques.Count(u => u.SetId == null)); // v1.29 pass 2; P37 content remains deferred.
            Assert.Equal(350, Content.Uniques.Count(u => u.Link != null)); // v1.29 pass 2; P37 content remains deferred.
            Assert.Equal(47, Content.Sets.Count); // v1.29 pass 2; P37 content remains deferred.
            Assert.Equal(12, Content.Uniques.Count(u => u.SetId != null && Pass1SetIds.Contains(u.SetId)));
        }

        [Fact]
        public void Every_pass1_unique_has_bilingual_name_lore_and_powers_inside_the_enum()
        {
            foreach (var id in Pass1Ids)
            {
                Assert.True(Content.TryGetUnique(id, out var u), id);
                Assert.False(string.IsNullOrWhiteSpace(u.Name.Ja), id);
                Assert.False(string.IsNullOrWhiteSpace(u.Name.En), id);
                Assert.False(string.IsNullOrWhiteSpace(u.Lore.Ja), id);
                Assert.False(string.IsNullOrWhiteSpace(u.Lore.En), id);
                Assert.Equal(2, u.Powers.Count);
                foreach (var line in u.Powers)
                {
                    Assert.NotEqual(Power.None, line.Power);
                    Assert.True(Enum.IsDefined(typeof(Power), line.Power), $"{id}: {line.Power}");
                    Assert.InRange(line.Value, 1, Content.PowerCap(line.Power));
                }
                if (u.Link != null)
                {
                    Assert.True(Links.Validate(u.Link), id);
                    Assert.True(u.Link.Value <= Links.Cap(u.Link.Kind, u.Link.Requires.Length), id);
                }
            }
        }

        [Fact]
        public void Pass1_sets_have_stat_two_piece_reaction_three_piece_and_three_pieces()
        {
            foreach (var setId in Pass1SetIds)
            {
                var set = Content.GetSet(setId);
                Assert.NotNull(set);
                Assert.Equal(2, set.TwoPiece.Length);
                Assert.All(set.TwoPiece, s => Assert.True(Content.StatCap(s.Stat) > 0, setId));
                Assert.Equal(3, set.ThreePiece.Length);
                Assert.All(set.ThreePiece, p => Assert.True(Enum.IsDefined(typeof(Power), p.Power), setId));
                Assert.All(set.ThreePiece, p => Assert.InRange(p.Value, 1, Content.PowerCap(p.Power)));
                var piecesOf = Content.Uniques.Where(u => u.SetId == setId).ToList();
                Assert.Equal(3, piecesOf.Count);
                Assert.Equal(3, piecesOf.Select(u => Content.GetBase(u.BaseId).Slot).Distinct().Count());
                Assert.All(piecesOf, u => Assert.False(string.IsNullOrWhiteSpace(u.Name.En), u.Id));
            }
        }
    }
}

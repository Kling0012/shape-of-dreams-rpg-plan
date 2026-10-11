using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

// レベル連動のエッセンス枠。設定の計算は Core、枠の書き込みは製品の HostAuthority.LevelGemSlots.cs をそのまま使う。
namespace SodRpg.Mod
{
    internal sealed partial class EntityStatus { public int level = 1; }
    internal sealed partial class HostAuthority
    {
        internal void ApplyLevelForTest(Hero hero, LevelEssenceSettings settings) => ApplyLevelGemSlotsFor(hero, settings);
        internal void DetachLevelForTest() => DetachLevelGemSlots();
    }
}

namespace SodRpg.Core.Tests
{
    public class LevelEssenceSlotsTests
    {
        private static LevelEssenceSettings Make(string stages = "10:1, 20:2", bool q = true, bool w = true, bool e = true,
            bool enabled = true, int ceiling = 4, bool r = true) => LevelEssenceSettings.Create(enabled, q, w, e, r, stages, ceiling);

        // ---- 設定の計算 ----

        [Theory]
        [InlineData(1, 0)]
        [InlineData(9, 0)]
        [InlineData(10, 1)]
        [InlineData(19, 1)]
        [InlineData(20, 2)]
        [InlineData(30, 2)]
        public void Default_stages_add_one_at_level_10_and_two_in_total_at_level_20(int level, int expected)
        {
            var settings = LevelEssenceSettings.Default;
            // R も既定で対象（古い設定に levelEssenceSlotsR の欄がなくても、欄の初期値 true でこの既定になる）。
            foreach (int target in new[] { LevelEssenceSettings.TargetQ, LevelEssenceSettings.TargetW, LevelEssenceSettings.TargetE, LevelEssenceSettings.TargetR })
                Assert.Equal(expected, settings.Desired(target, level));
        }

        [Fact]
        public void Only_selected_memories_and_only_when_enabled_get_slots()
        {
            var onlyW = Make(w: true, q: false, e: false, r: false);
            Assert.Equal(0, onlyW.Desired(LevelEssenceSettings.TargetQ, 20));
            Assert.Equal(2, onlyW.Desired(LevelEssenceSettings.TargetW, 20));
            Assert.Equal(0, onlyW.Desired(LevelEssenceSettings.TargetE, 20));
            Assert.Equal(0, Make(enabled: false).Desired(LevelEssenceSettings.TargetQ, 99));
            Assert.Equal(0, Make(q: false, w: false, e: false, r: false).Desired(LevelEssenceSettings.TargetQ, 99));
        }

        [Fact]
        public void Any_number_of_stages_are_accepted_and_sorted_by_level()
        {
            var settings = Make("30:3, 5:1, 15:2");
            Assert.Equal(new[] { 5, 15, 30 }, settings.Stages.Select(s => s.Level));
            Assert.Equal(new[] { 0, 1, 2, 3 }, new[] { 4, 5, 15, 30 }.Select(l => settings.TotalAtLevel(l)));
        }

        [Fact]
        public void Stage_text_accepts_common_spellings_and_full_width_characters()
        {
            var settings = Make("Lv10:+1; 20=2、Ｌｖ３０：＋３ 40/4");
            Assert.Equal(new[] { (10, 1), (20, 2), (30, 3), (40, 4) }, settings.Stages.Select(s => (s.Level, s.Total)));
            Assert.Empty(settings.Rejected);
        }

        [Fact]
        public void Unreadable_stages_are_dropped_and_reported_without_discarding_the_rest()
        {
            var settings = Make("10:1, abc, 0:2, 20:, :3, 100:1, 30:-1, 40:2");
            Assert.Equal(new[] { (10, 1), (40, 2) }, settings.Stages.Select(s => (s.Level, s.Total)));
            Assert.Equal(new[] { "abc", "0:2", "20:", ":3", "100:1", "30:-1" }, settings.Rejected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_stage_text_adds_nothing(string text)
        {
            var settings = Make(text);
            Assert.Empty(settings.Stages);
            Assert.Equal(0, settings.Desired(LevelEssenceSettings.TargetQ, 99));
        }

        [Fact]
        public void A_repeated_level_uses_the_later_entry_and_totals_never_go_down_with_level()
        {
            var settings = Make("10:1, 10:3, 20:1");
            Assert.Equal(3, settings.TotalAtLevel(10));
            Assert.Equal(3, settings.TotalAtLevel(25));
        }

        [Fact]
        public void Totals_and_stage_count_are_bounded()
        {
            Assert.Equal(LevelEssenceSettings.MaxStageTotal, Make("10:99").TotalAtLevel(10));
            var many = string.Join(",", Enumerable.Range(1, 40).Select(i => $"{i}:1"));
            var settings = Make(many);
            Assert.Equal(LevelEssenceSettings.MaxStages, settings.Stages.Count);
            Assert.Equal(40 - LevelEssenceSettings.MaxStages, settings.Rejected.Count);
        }

        [Theory]
        [InlineData(-3, 4)]
        [InlineData(4, 4)]
        [InlineData(6, 6)]
        [InlineData(50, 8)]
        public void Ceiling_is_clamped_to_the_supported_range(int requested, int expected) =>
            Assert.Equal(expected, Make(ceiling: requested).Ceiling);

        [Fact]
        public void Description_names_the_stages_the_targets_and_the_ceiling()
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                var text = Make(q: true, w: false, e: true).Describe();
                Assert.Contains("Lv10:+1", text);
                Assert.Contains("Lv20:+2", text);
                Assert.Contains("Q・E", text);
                Assert.Contains("最大4枠", text);
                Assert.Contains("オフ", Make(enabled: false).Describe());
            }
            finally { Loc.Japanese = previous; }
        }

        // ---- 台帳の上限 ----

        [Fact]
        public void Ledger_stops_at_the_hud_ceiling_by_default_and_at_a_raised_ceiling_when_asked()
        {
            var ledger = new GemSlotLedger(3);
            var defaultDecision = ledger.Decide(3, 2, 0);
            Assert.Equal(4, defaultDecision.Target);
            Assert.Equal(1, defaultDecision.Contribution);
            var raised = ledger.Decide(3, 2, 0, 5);
            Assert.Equal(5, raised.Target);
            Assert.Equal(2, raised.Contribution);
            Assert.Equal(5, ledger.Decide(3, 8, 0, 5).Target);
        }

        // ---- 製品のアダプター ----

        private static Hero Traveler(int level)
        {
            var hero = new Hero();
            hero.Skill.hero = hero;
            hero.Status.level = level;
            foreach (var location in new[] { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R })
                hero.Skill.SetMaxGemCount(location, 3);
            hero.Skill.Writes = 0;
            return hero;
        }

        private static int[] Caps(Hero hero) => new[] { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R }
            .Select(l => hero.Skill.GetMaxGemCount(l)).ToArray();

        [Fact]
        public void Slots_follow_the_hero_level_up_to_the_hud_ceiling_and_leave_other_locations_alone()
        {
            var host = new HostAuthority(); var hero = Traveler(1); var settings = Make();
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 3, 3, 3, 3 }, Caps(hero));
            Assert.Equal(0, hero.Skill.Writes);

            hero.Status.level = 10;
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 4, 4, 4, 4 }, Caps(hero));

            hero.Status.level = 20;
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 4, 4, 4, 4 }, Caps(hero)); // 本体の枠並びが描けるのは4まで
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);  // 星図の枠（アイデンティティ・回避）には触れない
        }

        [Fact]
        public void R_grows_at_the_same_stages_as_QWE_by_default_and_the_star_map_slots_are_left_alone()
        {
            var host = new HostAuthority(); var hero = Traveler(1); var settings = Make(ceiling: 5);
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 3, 3, 3, 3 }, Caps(hero));
            hero.Status.level = 10;
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 4, 4, 4, 4 }, Caps(hero)); // R も Q・W・E と同じ Lv10 の +1
            hero.Status.level = 20;
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 5, 5, 5, 5 }, Caps(hero)); // 上限5なら Lv20 の +2 も R に入る
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);  // 回避・アイデンティティ（星図の枠）は増やさない
        }

        [Fact]
        public void A_raised_ceiling_lets_the_second_stage_add_a_second_slot()
        {
            var host = new HostAuthority(); var hero = Traveler(20); var settings = Make(ceiling: 5);
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 5, 5, 5, 5 }, Caps(hero));
        }

        [Fact]
        public void Only_the_chosen_memories_get_slots()
        {
            var host = new HostAuthority(); var hero = Traveler(20);
            host.ApplyLevelForTest(hero, Make(q: false, w: true, e: false, r: false));
            Assert.Equal(new[] { 3, 4, 3, 3 }, Caps(hero));
        }

        [Fact]
        public void Turning_off_R_only_stops_R_from_growing()
        {
            var host = new HostAuthority(); var hero = Traveler(20);
            host.ApplyLevelForTest(hero, Make(r: false, ceiling: 5));
            Assert.Equal(new[] { 5, 5, 5, 3 }, Caps(hero)); // Q・W・E は伸びて、R だけ元の3枠のまま
        }

        [Fact]
        public void Reapplying_does_not_accumulate_or_write_again()
        {
            var host = new HostAuthority(); var hero = Traveler(10); var settings = Make();
            host.ApplyLevelForTest(hero, settings);
            int writes = hero.Skill.Writes;
            for (int i = 0; i < 5; i++) host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 4, 4, 4, 4 }, Caps(hero));
            Assert.Equal(writes, hero.Skill.Writes);
        }

        [Fact]
        public void Slots_another_source_already_added_count_toward_the_ceiling_instead_of_stacking()
        {
            // 混沌の聖堂などで先に4枠になっているときは、こちらは足さない。
            var host = new HostAuthority(); var hero = Traveler(20);
            hero.Skill.SetMaxGemCount(HeroSkillLocation.Q, 4); hero.Skill.Writes = 0;
            host.ApplyLevelForTest(hero, Make());
            Assert.Equal(new[] { 4, 4, 4, 4 }, Caps(hero));
            Assert.Equal(3, hero.Skill.Writes); // W・E・R だけ
            host.DetachLevelForTest();
            Assert.Equal(new[] { 4, 3, 3, 3 }, Caps(hero)); // Q の外部の4枠は残る
        }

        [Fact]
        public void Turning_the_setting_off_removes_only_our_slots_and_drops_the_overflowing_essence()
        {
            var host = new HostAuthority(); var hero = Traveler(10);
            host.ApplyLevelForTest(hero, Make());
            hero.Skill.Fill(HeroSkillLocation.Q);
            var fourth = hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Q, index = 3 }];
            host.ApplyLevelForTest(hero, Make(enabled: false));
            Assert.Equal(new[] { 3, 3, 3, 3 }, Caps(hero));
            Assert.Equal(new[] { fourth }, hero.Skill.Dropped);
            Assert.False(hero.Skill.gems.ContainsKey(new GemLocation { skill = HeroSkillLocation.Q, index = 3 }));
            Assert.Equal(3, hero.Skill.gems.Count(kv => kv.Key.skill == HeroSkillLocation.Q));
        }

        [Fact]
        public void Detach_restores_the_original_slots()
        {
            var host = new HostAuthority(); var hero = Traveler(20);
            host.ApplyLevelForTest(hero, Make(ceiling: 5));
            host.DetachLevelForTest();
            Assert.Equal(new[] { 3, 3, 3, 3 }, Caps(hero));
        }

        [Fact]
        public void A_reloaded_copy_of_the_mod_does_not_add_the_same_slots_again()
        {
            var host = new HostAuthority(); var hero = Traveler(20); var settings = Make(ceiling: 5);
            host.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 5, 5, 5, 5 }, Caps(hero));
            // 再読み込み後のアセンブリコピーは作業台帳が空だが、所有の記録は AppDomain に残る。
            var field = typeof(HostAuthority).GetField("LevelGemSlotLedgers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var table = field.GetValue(null);
            table.GetType().GetMethod("Clear").Invoke(table, null);
            var reloaded = new HostAuthority();
            reloaded.ApplyLevelForTest(hero, settings);
            Assert.Equal(new[] { 5, 5, 5, 5 }, Caps(hero));
            reloaded.DetachLevelForTest();
            Assert.Equal(new[] { 3, 3, 3, 3 }, Caps(hero));
        }

        [Fact]
        public void An_inactive_or_missing_hero_is_skipped()
        {
            var host = new HostAuthority();
            host.ApplyLevelForTest(null, Make());
            var hero = Traveler(20); hero.isActive = false;
            host.ApplyLevelForTest(hero, Make());
            Assert.Equal(new[] { 3, 3, 3, 3 }, Caps(hero));
            Assert.Equal(0, hero.Skill.Writes);
        }
    }
}

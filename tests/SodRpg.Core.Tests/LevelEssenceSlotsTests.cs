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
        private static LevelEssenceSettings Make(int every = 10, bool q = true, bool w = true, bool e = true,
            bool enabled = true, int ceiling = 4, bool r = true) => LevelEssenceSettings.Create(enabled, q, w, e, r, every, ceiling);

        // ---- 設定の計算 ----

        [Theory]
        [InlineData(1, 0)]
        [InlineData(9, 0)]
        [InlineData(10, 1)]
        [InlineData(19, 1)]
        [InlineData(20, 2)]
        [InlineData(25, 2)]
        [InlineData(30, 3)]
        public void Default_interval_adds_one_every_ten_levels(int level, int expected)
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
        public void An_interval_of_one_adds_a_slot_every_level()
        {
            var everyLevel = Make(every: 1);
            Assert.Equal(1, everyLevel.Desired(LevelEssenceSettings.TargetQ, 1));
            Assert.Equal(7, everyLevel.Desired(LevelEssenceSettings.TargetW, 7));
            Assert.Equal(99, everyLevel.Desired(LevelEssenceSettings.TargetE, 99));
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(10, 10)]
        [InlineData(150, 99)]
        public void Interval_is_clamped_to_the_supported_range(int requested, int expected) =>
            Assert.Equal(expected, Make(requested).EveryLevels);

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
                Assert.Contains("10レベルごとに +1", text);
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
        public void A_chaos_raised_memory_gets_only_the_room_left_under_the_cap()
        {
            // 混沌でQが既に4枠。Lv20 の候補は +2 だが、cap=5 なら +1 まで。
            var host = new HostAuthority();
            var raised = Traveler(20);
            raised.Skill.SetMaxGemCount(HeroSkillLocation.Q, 4);
            host.ApplyLevelForTest(raised, Make(ceiling: 5));
            Assert.Equal(new[] { 5, 5, 5, 5 }, Caps(raised)); // Q は 4+1（5で頭打ち）、他は 3+2
            // cap=4 ならQに余地はなく、レベル分は足さない（書き込みもない）。
            var closed = Traveler(20);
            closed.Skill.SetMaxGemCount(HeroSkillLocation.Q, 4); closed.Skill.Writes = 0;
            host.ApplyLevelForTest(closed, Make(ceiling: 4));
            Assert.Equal(new[] { 4, 4, 4, 4 }, Caps(closed));
            Assert.Equal(3, closed.Skill.Writes); // Q には書かない（W・E・R だけ）
            host.DetachLevelForTest();
            Assert.Equal(new[] { 4, 3, 3, 3 }, Caps(closed)); // 混沌の4枠は減らない
        }

        [Fact]
        public void Off_a_lower_cap_or_a_lower_level_remove_only_the_mods_slots_and_never_chaos_slots()
        {
            var host = new HostAuthority(); var hero = Traveler(20);
            hero.Skill.SetMaxGemCount(HeroSkillLocation.Q, 4); // 混沌でQが4枠
            host.ApplyLevelForTest(hero, Make(ceiling: 6));
            Assert.Equal(new[] { 6, 5, 5, 5 }, Caps(hero)); // Q は混沌4+2、他は3+2
            host.ApplyLevelForTest(hero, Make(enabled: false, ceiling: 6));
            Assert.Equal(new[] { 4, 3, 3, 3 }, Caps(hero)); // OFF：自分の分だけ戻り、混沌の4は残る
            host.ApplyLevelForTest(hero, Make(ceiling: 5));
            Assert.Equal(new[] { 5, 5, 5, 5 }, Caps(hero)); // cap 低下：Q は 4+1 まで、他は 3+2
            hero.Status.level = 10;
            host.ApplyLevelForTest(hero, Make(ceiling: 5));
            Assert.Equal(new[] { 5, 4, 4, 4 }, Caps(hero)); // レベル低下：他は +1 へ。Q は混沌4+1 のまま
            hero.Status.level = 5;
            host.ApplyLevelForTest(hero, Make(ceiling: 5));
            Assert.Equal(new[] { 4, 3, 3, 3 }, Caps(hero)); // レベル分がなくなっても混沌の4は減らない
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);  // 回避・アイデンティティ（星図の枠）は変わらない
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

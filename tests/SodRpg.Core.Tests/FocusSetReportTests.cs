using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class FocusSetReportTests
    {
        [Fact]
        public void Focus_doubles_the_chance_of_its_line()
        {
            double Share(Line? focus)
            {
                var rng = new Rng(17);
                int hit = 0, normal = 0;
                const int n = 30000;
                for (int i = 0; i < n; i++)
                {
                    var r = Loot.RollRelic(rng, Rarity.Rare, 10, null, focus);
                    if (r.NamedId != null) continue; // 銘品を除けば通常品の土台は狙い系統どおり（銘品は NamedItemsDataV132Tests で）
                    normal++;
                    if (r.Base.Line == Line.Guard) hit++;
                }
                return (double)hit / normal;
            }
            double baseShare = Share(null);
            double focused = Share(Line.Guard);
            int guards = Content.Bases.Count(b => b.Line == Line.Guard);
            double expectedBase = (double)guards / Content.Bases.Count;
            double expectedFocus = (double)(guards * Loot.FocusWeight) /
                (Content.Bases.Count + guards * (Loot.FocusWeight - 1));
            Assert.InRange(baseShare, expectedBase - 0.03, expectedBase + 0.03);
            Assert.InRange(focused, expectedFocus - 0.03, expectedFocus + 0.03);
        }

        [Fact]
        public void Focus_also_weights_legendaries()
        {
            double Share(Line? focus)
            {
                var rng = new Rng(3);
                int offense = 0;
                for (int i = 0; i < 8000; i++)
                    if (Loot.RollRelic(rng, Rarity.Legendary, 10, null, focus).Base.Line == Line.Offense) offense++;
                return offense / 8000.0;
            }
            Assert.True(Share(Line.Offense) > Share(null) * 1.3);
        }

        [Fact]
        public void Kills_use_profile_focus()
        {
            var p = Profile.CreateNew(5);
            Rules.SetFocus(p, Line.Resonance);
            Rules.BeginRun(p, "f");
            var rng = new Rng(p.RngState);
            var expected = Loot.RollKill(rng, MonsterTier.Boss, 10, 0, Line.Resonance, null,
                p.Stash, p.Run.Satchel, p.Codex); // Rules.OnKill と同じ引数（銘品の重みは図鑑・所持で変わる）
            Rules.OnKill(p, MonsterTier.Boss, 10);
            Assert.Equal(expected.Relics.Select(r => r.Uid + r.BaseId), p.Run.Satchel.Select(r => r.Uid + r.BaseId));
        }

        [Fact]
        public void Focus_cannot_change_during_a_run()
        {
            var p = Profile.CreateNew(5);
            Rules.SetFocus(p, Line.Offense);
            Rules.BeginRun(p, "x");
            Assert.Throws<InvalidOperationException>(() => Rules.SetFocus(p, Line.Guard));
            Assert.Equal(Line.Offense, p.Focus);
            Rules.EndRun(p, true);
            Rules.SetFocus(p, null);
            Assert.Null(p.Focus);
        }

        private static Relic Make(Profile p, string baseId)
        {
            var r = new Relic { Uid = "u" + p.Stash.Count, BaseId = baseId, Rarity = Rarity.Common, ItemLevel = 1 };
            p.Stash.Add(r);
            return r;
        }

        [Fact]
        public void Line_bonuses_accumulate_through_six_equipped_slots()
        {
            var p = Profile.CreateNew(1);
            Rules.Equip(p, "H", Make(p, "weapon.shield_maul").Uid);      // 守勢
            var b1 = Build.Compute(p, "H", 0);
            Assert.Equal(0, b1.Get(Stat.Armor));

            Rules.Equip(p, "H", Make(p, "armor.thorn_mail").Uid);         // 守勢（基礎は最大HP）
            var b2 = Build.Compute(p, "H", 0);
            Assert.Equal(8, b2.Get(Stat.Armor));
            Assert.Equal(2, b2.Lines[Line.Guard]);

            Rules.Equip(p, "H", Make(p, "charm.chain_necklace").Uid);     // 守勢
            var b3 = Build.Compute(p, "H", 0);
            Assert.Equal(8, b3.Get(Stat.Armor));
            Assert.Equal(b2.Get(Stat.MaxHealthPct) + 6, b3.Get(Stat.MaxHealthPct));

            Rules.Equip(p, "H", Make(p, "head.iron_helm").Uid);
            var b4 = Build.Compute(p, "H", 0);
            Assert.Equal(4, b4.Lines[Line.Guard]);
            Assert.Equal(b3.Get(Stat.Armor) + 6, b4.Get(Stat.Armor));
            Assert.Equal(b3.Get(Stat.Tenacity) + 12, b4.Get(Stat.Tenacity));

            Rules.Equip(p, "H", Make(p, "hands.iron_gauntlets").Uid);
            var b5 = Build.Compute(p, "H", 0);
            Assert.Equal(5, b5.Lines[Line.Guard]);
            Assert.Equal(b4.Get(Stat.Armor) + 6, b5.Get(Stat.Armor));
            Assert.Equal(b4.Get(Stat.Tenacity), b5.Get(Stat.Tenacity));

            Rules.Equip(p, "H", Make(p, "feet.iron_greaves").Uid);
            var b6 = Build.Compute(p, "H", 0);
            Assert.Equal(6, b6.Lines[Line.Guard]);
            Assert.Equal(b5.Get(Stat.Armor) + 6 + 12, b6.Get(Stat.Armor));
            Assert.Equal(b5.Get(Stat.MaxHealthPct) + 6, b6.Get(Stat.MaxHealthPct));
        }

        [Fact]
        public void Mixed_lines_give_no_set_bonus()
        {
            var p = Profile.CreateNew(1);
            Rules.Equip(p, "H", Make(p, "weapon.chain_sword").Uid);    // 攻勢
            Rules.Equip(p, "H", Make(p, "armor.guardian_plate").Uid);  // 守勢
            Rules.Equip(p, "H", Make(p, "charm.tailwind_ring").Uid);   // 共鳴
            var b = Build.Compute(p, "H", 0);
            Assert.All(b.Lines.Values, n => Assert.Equal(1, n));
            Assert.Equal(0, b.Get(Stat.Haste));
            Assert.Equal(0, b.Get(Stat.PowerPct));
        }


        [Fact]
        public void Report_summarizes_a_victory()
        {
            var p = Profile.CreateNew(9);
            Rules.BeginRun(p, "r");
            for (int i = 0; i < 5; i++) Rules.OnKill(p, MonsterTier.Boss, 10);
            Rules.ReachSecurePoint(p);
            Rules.Delve(p);
            Rules.Delve(p);
            for (int i = 0; i < 3; i++) Rules.OnKill(p, MonsterTier.Boss, 10);
            int found = p.Run.RelicsFound;
            Rules.EndRun(p, victory: true);
            var r = p.LastReport;
            Assert.True(r.Victory);
            Assert.Equal(8, r.Kills);
            Assert.Equal(found, r.RelicsFound);
            Assert.Equal(found, r.RelicsSecured);
            Assert.Equal(0, r.RelicsLost);
            Assert.Equal(2, r.PeakHeat);
            Assert.True(r.LevelAfter >= r.LevelBefore);
        }

        [Fact]
        public void Report_summarizes_a_defeat_after_a_secure()
        {
            var p = Profile.CreateNew(9);
            Rules.BeginRun(p, "r");
            for (int i = 0; i < 3; i++) Rules.OnKill(p, MonsterTier.Boss, 10);
            int securedRelics = p.Run.Satchel.Count;
            Rules.Secure(p);
            for (int i = 0; i < 2; i++) Rules.OnKill(p, MonsterTier.Boss, 10);
            int lost = p.Run.Satchel.Count;
            Rules.EndRun(p, victory: false);
            var r = p.LastReport;
            Assert.False(r.Victory);
            Assert.Equal(securedRelics, r.RelicsSecured);
            Assert.Equal(lost, r.RelicsLost);
            Assert.Equal(1, r.SecuredCount);
            Assert.True(r.EchoShards > 0);
        }

        [Fact]
        public void Focus_and_run_counters_roundtrip_through_codec()
        {
            var p = Profile.CreateNew(2);
            Rules.SetFocus(p, Line.Guard);
            Rules.BeginRun(p, "c");
            for (int i = 0; i < 3; i++) Rules.OnKill(p, MonsterTier.Boss, 10);
            Rules.Secure(p);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(Line.Guard, q.Focus);
            Assert.Equal(p.Run.RelicsFound, q.Run.RelicsFound);
            Assert.Equal(p.Run.RelicsSecured, q.Run.RelicsSecured);
            Assert.Equal(p.Run.ShardsSecured, q.Run.ShardsSecured);

            var none = Profile.CreateNew(2);
            Assert.Null(ProfileCodec.Read(ProfileCodec.Write(none), new List<string>()).Focus);
        }

        [Fact]
        public void Room_counter_with_baseline_counts_the_first_room()
        {
            var c = new RoomCounter();
            c.Reset(0);
            Assert.Equal(1, c.Observe(1));
            c.Reset(2); // ライブリロード・途中参加：既に2部屋突破済み
            Assert.Equal(1, c.Observe(3));
        }

        [Fact]
        public void Recovering_a_lost_relic_respects_satchel_capacity()
        {
            var p = Profile.CreateNew(4);
            var rng = new Rng(4);
            p.LostAndFound.Add(Loot.RollRelic(rng, Rarity.Legendary, 10));
            Rules.BeginRun(p, "cap");
            for (int i = 0; i < Content.SatchelCapacity; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            int shards = p.Run.SatchelShards;
            Rules.OnRoomsCleared(p, Content.RoomsToRecoverLost);
            Assert.Equal(Content.SatchelCapacity, p.Run.Satchel.Count);
            Assert.Contains(p.Run.Satchel, r => r.Rarity == Rarity.Legendary);
            Assert.Equal(shards, p.Run.SatchelShards);
        }

        [Fact]
        public void Report_level_before_is_the_level_at_run_start()
        {
            var p = Profile.CreateNew(6);
            Rules.BeginRun(p, "lv");
            int start = p.DreamLevel;
            for (int i = 0; i < 10; i++) Rules.OnKill(p, MonsterTier.Boss, 5);
            Assert.True(p.DreamLevel > start);
            Rules.EndRun(p, victory: false);
            Assert.Equal(start, p.LastReport.LevelBefore);
            Assert.Equal(p.DreamLevel, p.LastReport.LevelAfter);
        }

        [Fact]
        public void Overflowed_relics_do_not_count_as_secured()
        {
            var p = Profile.CreateNew(6);
            var rng = new Rng(1006);
            for (int i = 0; i < Content.StashCapacity - 1; i++) p.Stash.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            Rules.BeginRun(p, "of");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.Collector, Target = 4, RewardShards = 5, RewardXp = 1 });
            for (int i = 0; i < 4; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            Rules.Secure(p);
            Assert.Equal(1, p.Run.RelicsSecured);
            Assert.Equal(1, p.Run.Bounties[0].Progress);
            Assert.False(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void Bounty_rewards_paid_during_secure_count_as_secured_shards()
        {
            var p = Profile.CreateNew(6);
            Rules.BeginRun(p, "bs");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.Collector, Target = 1, RewardShards = 20, RewardXp = 1 });
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(77), Rarity.Common, 1));
            p.Run.SatchelShards = 100;
            Rules.Secure(p);
            Assert.Equal(120, p.Material(Materials.Shard));
            Assert.Equal(120, p.Run.ShardsSecured);
        }
    }
}

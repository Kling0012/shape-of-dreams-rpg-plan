using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>部屋の兆し（RoomOmens）の最小検証: 抽選・バランス表・報酬・係数の既定。</summary>
    public class RoomOmensTests
    {
        private static JsonElement Balance() => EquipmentBalanceInputs.Raw("tools/balance/room-omens.json");

        [Fact]
        public void Roll_is_deterministic_for_the_same_seed_and_room()
        {
            for (ulong seed = 1; seed < 200; seed++)
                for (int room = 0; room < 20; room++)
                {
                    var first = RoomOmens.Roll(seed, room);
                    Assert.NotEqual(RoomOmen.None, first);
                    Assert.Equal(first, RoomOmens.Roll(seed, room));
                }
            Assert.Equal(RoomOmen.None, RoomOmens.Roll(42, -1));
        }

        [Fact]
        public void Consecutive_rooms_never_draw_the_same_omen()
        {
            for (ulong seed = 0; seed < 400; seed++)
            {
                var previous = RoomOmen.None;
                for (int room = 0; room < 30; room++)
                {
                    var current = RoomOmens.Roll(seed, room);
                    Assert.NotEqual(previous, current);
                    previous = current;
                }
            }
        }

        [Fact]
        public void Blessing_share_stays_close_to_the_balance_table()
        {
            double chance = Balance().GetProperty("blessingChance").GetDouble();
            int blessings = 0, rooms = 0;
            for (ulong seed = 0; seed < 400; seed++)
                for (int room = 0; room < 40; room++)
                {
                    if (!RoomOmens.IsTrial(RoomOmens.Roll(seed, room))) blessings++;
                    rooms++;
                }
            Assert.InRange((double)blessings / rooms, chance - 0.02, chance + 0.02);
        }

        [Fact]
        public void Every_omen_is_drawn_and_classified_as_blessing_or_trial()
        {
            var seen = new HashSet<RoomOmen>();
            for (ulong seed = 0; seed < 400; seed++)
                for (int room = 0; room < 30; room++)
                    seen.Add(RoomOmens.Roll(seed, room));
            var all = Enum.GetValues(typeof(RoomOmen)).Cast<RoomOmen>().Where(o => o != RoomOmen.None).ToList();
            Assert.Equal(17, all.Count);
            // マルチ専用の兆しは別の乱数列。従来のソロ抽選は16種を維持する。
            var solo = all.Where(o => o != RoomOmen.ResonantWard).ToList();
            Assert.True(solo.All(seen.Contains), "未出現の兆し: " + string.Join(",", solo.Where(o => !seen.Contains(o))));
            Assert.Equal(9, all.Count(RoomOmens.IsTrial));
            foreach (var omen in all)
            {
                Assert.True(RoomOmens.IsValid(omen));
                Assert.False(string.IsNullOrEmpty(RoomOmens.Describe(omen)));
            }
            Assert.True(RoomOmens.IsValid(RoomOmen.None));
            Assert.False(RoomOmens.IsValid((RoomOmen)99));
        }

        [Fact]
        public void Released_omen_numbers_are_not_renumbered()
        {
            // ネットワークで送る番号。先に出した12種の並びは変えない。
            Assert.Equal(1, (int)RoomOmen.Vigor);
            Assert.Equal(6, (int)RoomOmen.Fortune);
            Assert.Equal(7, (int)RoomOmen.Sluggishness);
            Assert.Equal(12, (int)RoomOmen.SturdyEnemies);
        }

        [Fact]
        public void Blessings_help_and_trials_hurt()
        {
            foreach (RoomOmen omen in Enum.GetValues(typeof(RoomOmen)))
            {
                // 共鳴の盾は単一の恒常数値ではなく、敵の被ダメージを条件付きで切り替える。
                if (omen == RoomOmen.None || omen == RoomOmen.ResonantWard) continue;
                bool enemySide = omen == RoomOmen.RestlessEnemies || omen == RoomOmen.SturdyEnemies;
                if (RoomOmens.IsTrial(omen)) Assert.True(enemySide ? RoomOmens.Value(omen) > 0 : RoomOmens.Value(omen) < 0, omen.ToString());
                else Assert.True(RoomOmens.Value(omen) > 0, omen.ToString());
            }
        }

        [Fact]
        public void Omen_values_match_the_balance_table()
        {
            foreach (var property in Balance().GetProperty("effects").EnumerateObject())
            {
                var omen = (RoomOmen)Enum.Parse(typeof(RoomOmen), property.Name);
                Assert.True(RoomOmens.IsValid(omen));
                Assert.Equal(property.Value.GetDouble(), RoomOmens.Value(omen), 10);
            }
            Assert.Equal(0, RoomOmens.Value(RoomOmen.None));
        }

        [Fact]
        public void Trials_add_the_relic_drop_bonus_from_the_balance_table()
        {
            double trialBonus = Balance().GetProperty("trialDropBonus").GetDouble();
            foreach (RoomOmen omen in Enum.GetValues(typeof(RoomOmen)))
            {
                // 試練は全種が同じ補正、恵みは幸運のみ、それ以外は付かない。
                double expected = RoomOmens.IsTrial(omen) ? trialBonus
                    : omen == RoomOmen.Fortune ? RoomOmens.Value(omen) / 100 : 0;
                Assert.Equal(expected, RoomOmens.DropBonus(omen), 10);
                string id = RoomOmens.EncodeKillEventId("kill-1", omen);
                if (expected > 0)
                {
                    Assert.Contains("|omenDrop:", id);
                    Assert.Equal(expected, RoomOmens.DropBonusFromEventId(id), 10);
                }
                else
                {
                    Assert.Equal("kill-1", id);
                    Assert.Equal(0, RoomOmens.DropBonusFromEventId(id));
                }
            }
            Assert.Equal(0, RoomOmens.DropBonusFromEventId(null));
            Assert.Equal(0, RoomOmens.DropBonusFromEventId("kill-1|omenDrop:9|")); // 範囲外の補正は無効
        }

        [Fact]
        public void Kill_modifiers_include_the_room_omen_drop_bonus()
        {
            var p = Profile.CreateNew(11);
            Rules.BeginRun(p, "r", heroKey: "hero");
            double trialBonus = Balance().GetProperty("trialDropBonus").GetDouble();
            var plain = Rules.KillModifiers(p.Run);
            var withOmen = Rules.KillModifiers(p.Run, roomOmenDropBonus: trialBonus);
            Assert.Equal(plain.DropBonus + trialBonus, withOmen.DropBonus, 10);
        }

        [Fact]
        public void Traveler_trial_scale_defaults_to_one()
        {
            Assert.Equal(1.0, Balance().GetProperty("defaultTrialScale").GetDouble(), 10);
            var overridden = Balance().GetProperty("trialScales").EnumerateObject().Select(s => s.Name).ToHashSet();
            string[] travelers = { "Hero_Auror", "Hero_Vapor", "Hero_Brute", "Hero_Mist", null };
            foreach (var key in travelers)
                if (!overridden.Contains(key))
                    Assert.Equal(1.0, RoomOmens.TrialScale(key), 10);
        }

        [Fact]
        public void RollCoop_never_replaces_a_solo_or_disabled_party()
        {
            // 人間1人または設定OFFでは置換が起きないため、部屋列は従来の Roll と完全に一致する。
            for (ulong seed = 0; seed < 200; seed++)
                for (int room = 0; room < 20; room++)
                {
                    Assert.Equal(RoomOmen.None, RoomOmens.RollCoop(seed, room, 1, true));
                    Assert.Equal(RoomOmen.None, RoomOmens.RollCoop(seed, room, 5, false));
                    var solo = RoomOmen.None;
                    for (int r = 0; r <= room; r++)
                        solo = RoomOmens.RollCoop(seed, r, 1, true) != RoomOmen.None ? RoomOmen.ResonantWard : RoomOmens.Roll(seed, r);
                    Assert.Equal(RoomOmens.Roll(seed, room), solo);
                }
            Assert.Equal(RoomOmen.None, RoomOmens.RollCoop(42, -1, 4, true));
        }

        [Fact]
        public void RollCoop_is_deterministic_and_leaves_the_solo_stream_untouched()
        {
            for (ulong seed = 0; seed < 300; seed++)
            {
                var solo = new RoomOmen[20];
                for (int room = 0; room < 20; room++) solo[room] = RoomOmens.Roll(seed, room);
                for (int room = 0; room < 20; room++)
                {
                    var first = RoomOmens.RollCoop(seed, room, 2, true);
                    Assert.Equal(first, RoomOmens.RollCoop(seed, room, 2, true));
                    Assert.True(first == RoomOmen.None || first == RoomOmen.ResonantWard);
                    // ホストの置換後の列: 共鳴の盾の部屋以外は従来の結果のまま。
                    if (first == RoomOmen.None) Assert.Equal(solo[room], RoomOmens.Roll(seed, room));
                    else Assert.NotEqual(RoomOmen.ResonantWard, solo[room]); // 通常抽選は共鳴の盾を出さない
                    // 置換があっても次の部屋の通常の結果は変わらない。
                    if (room + 1 < 20) Assert.Equal(solo[room + 1], RoomOmens.Roll(seed, room + 1));
                }
            }
        }

        [Fact]
        public void RollCoop_hits_share_stays_close_to_the_balance_table()
        {
            double chance = Balance().GetProperty("coop").GetProperty("coopOmenChance").GetDouble();
            int hits = 0, rooms = 0;
            // ホストと同じ runId由来の種で測る（連続する小さな整数の種は最初の1回の抽選に偏る）。
            for (int i = 0; i < 400; i++)
            {
                ulong seed = Rng.SeedFrom("coop-omen-share-" + i);
                for (int room = 0; room < 40; room++)
                {
                    if (RoomOmens.RollCoop(seed, room, 2, true) == RoomOmen.ResonantWard) hits++;
                    rooms++;
                }
            }
            Assert.InRange((double)hits / rooms, chance - 0.02, chance + 0.02);
            // 部屋番号は乱数列に入る: 同じseedでも部屋ごとに独立した結果になる。
            Assert.Contains(Enumerable.Range(0, 40),
                room => RoomOmens.RollCoop(7, room, 2, true) != RoomOmens.RollCoop(7, 0, 2, true)
                    || RoomOmens.RollCoop(11, room, 2, true) != RoomOmens.RollCoop(11, 5, 2, true));
        }
    }
}

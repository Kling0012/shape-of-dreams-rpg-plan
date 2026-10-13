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
    }
}

using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.16：遠征中の装着品の分解は確保地点でのみ許可する。</summary>
    public class SalvageLockV116Tests
    {
        private static Relic GiveEquipped(Profile p)
        {
            var r = Loot.RollRelic(new Rng(99), Rarity.Epic, 3);
            p.Stash.Add(r);
            Rules.Equip(p, "Hero_A", r.Uid);
            return r;
        }

        [Fact]
        public void Mid_run_salvage_rejects_equipped_relic_without_mutation()
        {
            var p = Profile.CreateNew(12);
            var r = GiveEquipped(p);
            Rules.BeginRun(p, "Hero_A");
            int shards = p.Material(Materials.Shard);
            int tuning = p.Material(Materials.Tuning);

            Assert.False(p.Run.AwaitingChoice);
            Assert.True(Rules.LoadoutLocked(p, true));
            Assert.False(Rules.LoadoutLocked(p, false));
            Assert.Throws<InvalidOperationException>(() => Rules.Salvage(p, r.Uid, loadoutLocked: true));

            Assert.Contains(r, p.Stash);
            Assert.Equal(r.Uid, p.Hero("Hero_A").Equipped[(int)r.Slot]);
            Assert.True(p.IsEquippedAnywhere(r.Uid));
            Assert.Equal(shards, p.Material(Materials.Shard));
            Assert.Equal(tuning, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Mid_run_salvage_allows_the_relic_after_unequipping()
        {
            var p = Profile.CreateNew(12);
            var r = GiveEquipped(p);
            Rules.BeginRun(p, "Hero_A");
            Rules.Unequip(p, "Hero_A", r.Slot);
            int shards = p.Material(Materials.Shard);
            int tuning = p.Material(Materials.Tuning);

            Assert.True(Rules.LoadoutLocked(p, true));
            Rules.Salvage(p, r.Uid, loadoutLocked: true);

            Assert.DoesNotContain(r, p.Stash);
            Assert.False(p.IsEquippedAnywhere(r.Uid));
            Assert.Equal(shards + Rules.SalvageValue(r), p.Material(Materials.Shard));
            Assert.Equal(tuning + Content.SalvageTuning(r.Rarity), p.Material(Materials.Tuning));
        }

        [Fact]
        public void Secure_point_salvage_allows_equipped_relic_and_clears_slot()
        {
            var p = Profile.CreateNew(12);
            var r = GiveEquipped(p);
            Rules.BeginRun(p, "Hero_A");
            Rules.ReachSecurePoint(p);
            int shards = p.Material(Materials.Shard);

            Assert.True(p.Run.AwaitingChoice);
            Assert.False(Rules.LoadoutLocked(p, true));
            Rules.Salvage(p, r.Uid, loadoutLocked: Rules.LoadoutLocked(p, true));

            Assert.DoesNotContain(r, p.Stash);
            Assert.Null(p.Hero("Hero_A").Equipped[(int)r.Slot]);
            Assert.False(p.IsEquippedAnywhere(r.Uid));
            Assert.Equal(shards + Rules.SalvageValue(r), p.Material(Materials.Shard));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void No_run_leaves_loadout_unlocked(bool inGame)
        {
            var p = Profile.CreateNew(12);

            Assert.Null(p.Run);
            Assert.False(Rules.LoadoutLocked(p, inGame));
        }
    }
}

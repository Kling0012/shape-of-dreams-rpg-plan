using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EconomyV131Tests
    {
        private static Profile WithAllRarities(int each)
        {
            var p = Profile.CreateNew(5);
            var rng = new Rng(777);
            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
                for (int i = 0; i < each; i++) p.Stash.Add(Loot.RollRelic(rng, r, 5 + i));
            return p;
        }


        [Fact]
        public void Bulk_threshold_keeps_exclusions()
        {
            var p = WithAllRarities(3);
            Rules.SetBulkSalvageMaxRarity(p, Rarity.Epic);
            var rares = p.Stash.Where(x => x.Rarity == Rarity.Rare).ToList();
            rares[0].Locked = true;
            Rules.Equip(p, "H", rares[1].Uid);
            var c = Rules.BulkSalvageCandidates(p);
            Assert.DoesNotContain(c, x => x.Uid == rares[0].Uid || x.Uid == rares[1].Uid);
            Assert.Contains(c, x => x.Uid == rares[2].Uid);
            Rules.BulkSalvage(p);
            Assert.Equal(3, p.Stash.Count(x => x.Rarity == Rarity.Legendary));
        }

        [Fact]
        public void Bulk_threshold_round_trips_and_old_saves_get_default()
        {
            var p = Profile.CreateNew(9);
            Rules.SetBulkSalvageMaxRarity(p, Rarity.Rare);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(Rarity.Rare, q.BulkSalvageMaxRarity);
            Assert.Equal(Rarity.Rare, p.Clone().BulkSalvageMaxRarity);

            var d = Profile.CreateNew(9);
            Rules.SetBulkSalvageMaxRarity(d, Rarity.Epic);
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(d));
            Assert.True(root.TryGet("body", out object body));
            var oldBody = new JsonObject();
            foreach (var kv in ((JsonObject)body).Properties)
                if (kv.Key != "bulkSalvageMax") oldBody.Add(kv.Key, kv.Value);
            string checksum = "sha256:" + LedgerSerializer.Sha256Hex(Json.Write(oldBody));
            var oldSave = new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", checksum).Add("body", oldBody);
            var o = ProfileCodec.Read(Json.Write(oldSave), new List<string>());
            Assert.Equal(Rarity.Uncommon, o.BulkSalvageMaxRarity);
        }


        private static Profile Stocked(Rarity r, int count, int shards, int tuning)
        {
            var p = Profile.CreateNew(21);
            var rng = new Rng(2100);
            for (int i = 0; i < count; i++) p.Stash.Add(Loot.RollRelic(rng, r, 5 + i));
            p.AddMaterial(Materials.Shard, shards);
            if (tuning > 0) p.AddMaterial(Materials.Tuning, tuning);
            return p;
        }

        [Fact]
        public void Transmute_needs_exact_count_and_spends_materials()
        {
            foreach (Rarity r in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare })
            {
                int n = ForgeBalanceTests.At("synthesis", "inputs", (int)r);
                int cost = ForgeBalanceTests.At("synthesis", "shardCosts", (int)r);
                var few = Stocked(r, n - 1, cost, 0);
                Assert.Throws<InvalidOperationException>(() => Rules.Transmute(few, r));
                Assert.Equal(n - 1, few.Stash.Count);
                Assert.Equal(cost, few.Material(Materials.Shard));
                var poor = Stocked(r, n, cost - 1, 0);
                Assert.Throws<InvalidOperationException>(() => Rules.Transmute(poor, r));
                Assert.Equal(n, poor.Stash.Count);
                Assert.Equal(cost - 1, poor.Material(Materials.Shard));
                var p = Stocked(r, n, cost, 0);
                Rules.Transmute(p, r);
                Assert.Single(p.Stash);
                Assert.Equal(r + 1, p.Stash[0].Rarity);
                Assert.Equal(0, p.Material(Materials.Shard));
            }
        }

        [Fact]
        public void Legendary_transmute_costs_shards_and_tuning_and_fails_without_tuning()
        {
            int n = ForgeBalanceTests.At("synthesis", "inputs", (int)Rarity.Epic);
            int shards = ForgeBalanceTests.At("synthesis", "shardCosts", (int)Rarity.Epic);
            int tuning = ForgeBalanceTests.At("synthesis", "tuningCosts", (int)Rarity.Epic);
            var few = Stocked(Rarity.Epic, n - 1, shards, tuning);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(few, Rarity.Epic));
            Assert.Equal(n - 1, few.Stash.Count);
            Assert.Equal(shards, few.Material(Materials.Shard));
            Assert.Equal(tuning, few.Material(Materials.Tuning));

            var noTuning = Stocked(Rarity.Epic, n, shards, tuning - 1);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(noTuning, Rarity.Epic));
            Assert.Equal(n, noTuning.Stash.Count);
            Assert.Equal(shards, noTuning.Material(Materials.Shard));
            Assert.Equal(tuning - 1, noTuning.Material(Materials.Tuning));

            var poor = Stocked(Rarity.Epic, n, shards - 1, tuning);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(poor, Rarity.Epic));
            Assert.Equal(n, poor.Stash.Count);
            Assert.Equal(shards - 1, poor.Material(Materials.Shard));
            Assert.Equal(tuning, poor.Material(Materials.Tuning));

            var p = Stocked(Rarity.Epic, n, shards, tuning);
            Rules.Transmute(p, Rarity.Epic);
            Assert.Equal(Rarity.Legendary, Assert.Single(p.Stash).Rarity);
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Equal(0, p.Material(Materials.Tuning));

            int targeted = shards * ForgeBalanceTests.Number("synthesis", "targetCostPercent") / 100;
            var tgt = Stocked(Rarity.Epic, n, targeted, tuning);
            Rules.Transmute(tgt, Rarity.Epic, target: Slot.Weapon);
            var result = Assert.Single(tgt.Stash);
            Assert.Equal(Rarity.Legendary, result.Rarity);
            Assert.Equal(Slot.Weapon, result.Slot);
            Assert.Equal(0, tgt.Material(Materials.Shard));
            Assert.Equal(0, tgt.Material(Materials.Tuning));
        }

        [Fact]
        public void Loaded_epic_pity_counter_is_ignored_and_never_forces_an_epic()
        {
            // 旧セーブの救済カウンタ（epicPity）は保存互換で読み書きするが、天井は撤廃済みで抽選には使わない。
            var p = Profile.CreateNew(7);
            Rules.BeginRun(p, "test");
            p.EpicPity = 1000; // 旧実装なら毎回確定した値
            const int kills = 3000;
            int epicMains = 0;
            for (int i = 0; i < kills; i++)
            {
                // 鞄は容量で捨てられるため、主報酬＝その撃破の最初のドロップイベントで判定する。
                var main = Rules.OnKill(p, MonsterTier.Boss, 10).FirstOrDefault(e => e.Kind == EventKind.Drop);
                if (main != null && main.Rarity >= Rarity.Epic) epicMains++;
            }
            double expected = LootEconomyInputs.DropChance(MonsterTier.Boss, 0)
                * (LootEconomyInputs.RarityProbability(MonsterTier.Boss, 0, Rarity.Epic)
                    + LootEconomyInputs.RarityProbability(MonsterTier.Boss, 0, Rarity.Legendary));
            double tolerance = LootEconomyInputs.SamplingTolerance(expected * (1 - expected), kills);
            Assert.InRange((double)epicMains / kills, expected - tolerance, expected + tolerance);
        }

    }
}

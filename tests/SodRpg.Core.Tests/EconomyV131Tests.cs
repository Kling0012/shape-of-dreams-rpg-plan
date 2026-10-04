using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
        public void Bulk_threshold_selects_candidates_and_never_legendary()
        {
            var p = WithAllRarities(2);
            Assert.Equal(Rarity.Uncommon, p.BulkSalvageMaxRarity);
            foreach (Rarity max in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic })
            {
                Rules.SetBulkSalvageMaxRarity(p, max);
                var c = Rules.BulkSalvageCandidates(p);
                Assert.Equal(((int)max + 1) * 2, c.Count);
                Assert.All(c, x => Assert.True(x.Rarity <= max && x.Rarity != Rarity.Legendary));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => Rules.SetBulkSalvageMaxRarity(p, Rarity.Legendary));
            Assert.Equal(Rarity.Epic, p.BulkSalvageMaxRarity);
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
            string checksum;
            using (var sha = SHA256.Create())
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(oldBody)))).Replace("-", "").ToLowerInvariant();
            var oldSave = new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", checksum).Add("body", oldBody);
            var o = ProfileCodec.Read(Json.Write(oldSave), new List<string>());
            Assert.Equal(Rarity.Uncommon, o.BulkSalvageMaxRarity);
        }

        [Fact]
        public void Transmute_inputs_and_costs_per_rarity()
        {
            Assert.Equal(5, Content.TransmuteInputs(Rarity.Common));
            Assert.Equal(5, Content.TransmuteInputs(Rarity.Uncommon));
            Assert.Equal(12, Content.TransmuteInputs(Rarity.Rare));
            Assert.Equal(16, Content.TransmuteInputs(Rarity.Epic));
            Assert.Equal(10, Rules.TransmuteCost(Rarity.Common));
            Assert.Equal(20, Rules.TransmuteCost(Rarity.Uncommon));
            Assert.Equal(60, Rules.TransmuteCost(Rarity.Rare));
            Assert.Equal(300, Rules.TransmuteCost(Rarity.Epic));
            Assert.Equal(90, Rules.TransmuteCost(Rarity.Rare, true));
            Assert.Equal(450, Rules.TransmuteCost(Rarity.Epic, true));
            Assert.Equal(0, Rules.TransmuteTuning(Rarity.Rare));
            Assert.Equal(4, Rules.TransmuteTuning(Rarity.Epic));
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
                int n = r == Rarity.Rare ? 12 : 5;
                int cost = r == Rarity.Common ? 10 : r == Rarity.Uncommon ? 20 : 60;
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
            const int n = 16;
            var few = Stocked(Rarity.Epic, n - 1, 300, 4);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(few, Rarity.Epic));
            Assert.Equal(n - 1, few.Stash.Count);
            Assert.Equal(300, few.Material(Materials.Shard));
            Assert.Equal(4, few.Material(Materials.Tuning));

            var noTuning = Stocked(Rarity.Epic, n, 300, 3);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(noTuning, Rarity.Epic));
            Assert.Equal(n, noTuning.Stash.Count);
            Assert.Equal(300, noTuning.Material(Materials.Shard));
            Assert.Equal(3, noTuning.Material(Materials.Tuning));

            var poor = Stocked(Rarity.Epic, n, 299, 4);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(poor, Rarity.Epic));
            Assert.Equal(n, poor.Stash.Count);
            Assert.Equal(299, poor.Material(Materials.Shard));
            Assert.Equal(4, poor.Material(Materials.Tuning));

            var p = Stocked(Rarity.Epic, n, 300, 4);
            Rules.Transmute(p, Rarity.Epic);
            Assert.Equal(Rarity.Legendary, Assert.Single(p.Stash).Rarity);
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Equal(0, p.Material(Materials.Tuning));

            var tgt = Stocked(Rarity.Epic, n, 450, 4);
            Rules.Transmute(tgt, Rarity.Epic, target: Slot.Weapon);
            var result = Assert.Single(tgt.Stash);
            Assert.Equal(Rarity.Legendary, result.Rarity);
            Assert.Equal(Slot.Weapon, result.Slot);
            Assert.Equal(0, tgt.Material(Materials.Shard));
            Assert.Equal(0, tgt.Material(Materials.Tuning));
        }

        [Fact]
        public void Pity_chance_values()
        {
            Assert.Equal(0.025, Loot.EpicPityChance(0), 6);
            Assert.Equal(0.025 + 0.00875 * 3, Loot.EpicPityChance(3), 6);
            Assert.Equal(0.99625, Loot.EpicPityChance(111), 6);
            Assert.Equal(1.0, Loot.EpicPityChance(112), 6);
            Assert.Equal(1.0, Loot.EpicPityChance(1000), 6);
            Assert.True(Loot.EpicPityChance(1) > Loot.EpicPityChance(0));
        }

        [Fact]
        public void Epic_plus_share_is_lower_than_before_and_distribution_is_monotonic()
        {
            const int N = 200000;
            var rng = new Rng(4242);
            int epicPlus = 0;
            var counts = new int[5];
            for (int i = 0; i < N; i++)
            {
                var r = Loot.RollRarity(rng, 0, true);
                counts[(int)r]++;
                if (r >= Rarity.Epic) epicPlus++;
            }
            double before = 47.0 / 1987.0; // 旧い重みでの理論上の割合
            Assert.True(epicPlus / (double)N < before);
            Assert.True(counts[0] > counts[1] && counts[1] > counts[2] && counts[2] > counts[3] && counts[3] > counts[4]);
        }
    }
}

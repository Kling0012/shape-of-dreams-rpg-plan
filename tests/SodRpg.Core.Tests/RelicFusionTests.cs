using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class RelicFusionTests
    {
        [Fact]
        public void Unique_candidates_require_the_same_unique_id_and_rarity()
        {
            var p = Profile.CreateNew(7);
            var target = new Relic { Uid = "target", BaseId = "weapon.chain_sword", UniqueId = "unique.endless_dance", Rarity = Rarity.Legendary, ItemLevel = 5 };
            var same = target.Clone();
            same.Uid = "same";
            var otherUnique = same.Clone();
            otherUnique.Uid = "other-unique";
            otherUnique.UniqueId = "unique.dreameater";
            var otherRarity = same.Clone();
            otherRarity.Uid = "other-rarity";
            otherRarity.Rarity = Rarity.Epic;
            var nonUnique = same.Clone();
            nonUnique.Uid = "non-unique";
            nonUnique.UniqueId = null;
            p.Stash.AddRange(new[] { target, same, otherUnique, otherRarity, nonUnique });

            Assert.Equal(new[] { same }, Rules.FusionCandidates(p, target));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("named.weapon.002")]
        public void Non_unique_candidates_require_the_same_base_named_id_and_rarity(string namedId)
        {
            var p = Profile.CreateNew(7);
            var target = new Relic { Uid = "target", BaseId = "weapon.chain_sword", NamedId = namedId, Rarity = Rarity.Uncommon, ItemLevel = 5 };
            var same = target.Clone();
            same.Uid = "same";
            var otherBase = same.Clone();
            otherBase.Uid = "other-base";
            otherBase.BaseId = "weapon.bone_cleaver";
            var otherName = same.Clone();
            otherName.Uid = "other-name";
            otherName.NamedId = namedId == null ? "named.weapon.002" : null;
            var otherRarity = same.Clone();
            otherRarity.Uid = "other-rarity";
            otherRarity.Rarity = Rarity.Rare;
            p.Stash.AddRange(new[] { target, same, otherBase, otherName, otherRarity });

            Assert.Equal(new[] { same }, Rules.FusionCandidates(p, target));
        }

        [Fact]
        public void Locked_equipped_and_trade_reserved_relics_cannot_be_materials()
        {
            var p = Profile.CreateNew(7);
            var target = new Relic { Uid = "target", BaseId = "weapon.chain_sword", Rarity = Rarity.Rare, ItemLevel = 5 };
            var usable = target.Clone();
            usable.Uid = "usable";
            var locked = target.Clone();
            locked.Uid = "locked";
            locked.Locked = true;
            var equipped = target.Clone();
            equipped.Uid = "equipped";
            p.Hero("Hero_Lacerta").Equipped[(int)Slot.Weapon] = equipped.Uid;
            var reserved = target.Clone();
            reserved.Uid = "reserved";
            var trades = new TradeLedger();
            trades.Begin(TradeKind.SalvageForDust, 0, 0, 0, reserved.Uid, now: 1);
            p.Stash.AddRange(new[] { target, usable, locked, equipped, reserved });
            p.AddMaterial(Materials.Shard, 1000);

            Assert.Equal(new[] { usable }, Rules.FusionCandidates(p, target, trades));
            foreach (var material in new[] { locked, equipped, reserved })
                Assert.Throws<InvalidOperationException>(() => Rules.Fuse(p, target.Uid, material.Uid, trades));
        }

        [Theory]
        [InlineData(null, Rarity.Rare)]
        [InlineData("unique.endless_dance", Rarity.Legendary)]
        public void Fuse_advances_one_level_consumes_the_material_and_charges_the_cost(string uniqueId, Rarity rarity)
        {
            var p = Profile.CreateNew(7);
            var target = new Relic { Uid = "target", BaseId = "weapon.chain_sword", UniqueId = uniqueId, Rarity = rarity, ItemLevel = 5, FusionLevel = 1 };
            var material = target.Clone();
            material.Uid = "material";
            p.Stash.AddRange(new[] { target, material });
            p.AddMaterial(Materials.Shard, 1000);
            p.AddMaterial(Materials.Tuning, 10);
            int shards = FusionBalance.ShardsPerRarity * ((int)rarity + 1);
            int tuning = uniqueId == null ? 0 : FusionBalance.UniqueTuningCost;

            var result = Rules.Fuse(p, target.Uid, material.Uid);

            Assert.Equal(EventKind.Fused, result.Kind);
            Assert.Equal(2, target.FusionLevel);
            Assert.Same(target, Assert.Single(p.Stash));
            Assert.Equal(1000 - shards, p.Material(Materials.Shard));
            Assert.Equal(10 - tuning, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Fusion_reaches_five_and_refuses_to_advance_further()
        {
            var p = Profile.CreateNew(7);
            var target = new Relic { Uid = "target", BaseId = "weapon.chain_sword", Rarity = Rarity.Rare, ItemLevel = 5, FusionLevel = FusionBalance.MaxLevel - 1 };
            var material = target.Clone();
            material.Uid = "material";
            var nextMaterial = target.Clone();
            nextMaterial.Uid = "next-material";
            p.Stash.AddRange(new[] { target, material, nextMaterial });
            p.AddMaterial(Materials.Shard, 1000);

            Rules.Fuse(p, target.Uid, material.Uid);
            Assert.Equal(5, target.FusionLevel);
            string before = ProfileCodec.Write(p);

            Assert.Throws<InvalidOperationException>(() => Rules.Fuse(p, target.Uid, nextMaterial.Uid));
            Assert.Equal(before, ProfileCodec.Write(p));
        }

        [Fact]
        public void Fusion_scales_final_stats_by_the_balance_bonus_without_scaling_powers()
        {
            var relic = new Relic
            {
                Uid = "target", BaseId = "weapon.chain_sword", UniqueId = "unique.endless_dance",
                Rarity = Rarity.Legendary, ItemLevel = 5, Enhance = 3, AwakenLevel = 1,
                Forbidden = new ForbiddenMark { Pattern = ForbiddenPattern.Pact, Variant = 0 }
            };
            relic.Affixes.Add(new StatLine(Stat.AttackFlat, 37));
            relic.Powers.Add(new PowerLine(Power.Momentum, 6));
            var stats = relic.EffectiveStats().ToList();
            var powers = relic.EffectivePowers().Select(x => (x.Power, x.Value)).ToList();

            relic.FusionLevel = 5;

            Assert.Contains(relic.EffectiveStats(), x => x.Stat == Stat.MaxHealthPct && x.Value == -65);
            int pct = 100 + FusionBalance.StatBonusPerLevelPct * relic.FusionLevel;
            Assert.Equal(stats.Select(x => (x.Stat, Relic.Scale(x.Value, pct))),
                relic.EffectiveStats().Select(x => (x.Stat, x.Value)));
            Assert.NotEmpty(powers);
            Assert.Equal(powers, relic.EffectivePowers().Select(x => (x.Power, x.Value)).ToList());
        }

        [Fact]
        public void Zero_fusion_preserves_the_legacy_relic_save_bytes()
        {
            var p = Profile.CreateNew(7);
            p.Stash.Add(new Relic { Uid = "legacy", BaseId = "weapon.chain_sword", Rarity = Rarity.Rare, ItemLevel = 5, FusionLevel = 0 });
            const string legacy = "{\"uid\":\"legacy\",\"base\":\"weapon.chain_sword\",\"unique\":null,\"named\":null,\"rarity\":2,\"ilvl\":5,"
                + "\"enhance\":0,\"retunes\":0,\"affixRerolls\":0,\"locked\":false,\"awaken\":0,\"awakened\":false,\"awakenLevel\":0,"
                + "\"milestones\":0,\"limitBreaks\":0,\"milestonePowerApplied\":false,\"developerGranted\":false,\"infinityFreeSupply\":false,\"affixes\":[],\"powers\":[]}";

            using (var saved = JsonDocument.Parse(ProfileCodec.Write(p)))
            {
                string actual = saved.RootElement.GetProperty("body").GetProperty("stash")[0].GetRawText();
                Assert.Equal(Encoding.UTF8.GetBytes(legacy), Encoding.UTF8.GetBytes(actual));
            }
        }

        [Fact]
        public void Save_roundtrip_preserves_a_positive_fusion_level()
        {
            var p = Profile.CreateNew(7);
            var relic = new Relic { Uid = "fused", BaseId = "weapon.chain_sword", Rarity = Rarity.Rare, ItemLevel = 5, FusionLevel = 3 };
            p.Stash.Add(relic);

            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());

            Assert.Equal(relic.FusionLevel, loaded.FindStash(relic.Uid).FusionLevel);
        }

        [Fact]
        public void Insufficient_currency_or_a_locked_expedition_leaves_the_profile_unchanged()
        {
            var p = Profile.CreateNew(7);
            var target = new Relic { Uid = "target", BaseId = "weapon.chain_sword", UniqueId = "unique.endless_dance", Rarity = Rarity.Legendary, ItemLevel = 5 };
            var material = target.Clone();
            material.Uid = "material";
            p.Stash.AddRange(new[] { target, material });
            int shards = FusionBalance.ShardsPerRarity * ((int)target.Rarity + 1);
            int tuning = FusionBalance.UniqueTuningCost;
            p.AddMaterial(Materials.Shard, shards - 1);
            p.AddMaterial(Materials.Tuning, tuning);
            string before = ProfileCodec.Write(p);

            Assert.Throws<InvalidOperationException>(() => Rules.Fuse(p, target.Uid, material.Uid));
            Assert.Equal(before, ProfileCodec.Write(p));

            p.AddMaterial(Materials.Shard, 1);
            p.AddMaterial(Materials.Tuning, -1);
            before = ProfileCodec.Write(p);
            Assert.Throws<InvalidOperationException>(() => Rules.Fuse(p, target.Uid, material.Uid));
            Assert.Equal(before, ProfileCodec.Write(p));

            p.AddMaterial(Materials.Tuning, 1);
            before = ProfileCodec.Write(p);
            Assert.Throws<InvalidOperationException>(() => Rules.Fuse(p, target.Uid, material.Uid, loadoutLocked: true));
            Assert.Equal(before, ProfileCodec.Write(p));
        }
    }
}

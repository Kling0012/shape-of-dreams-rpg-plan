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
    /// <summary>v1.31：特性の洗い直し（遺物の特性を全部まとめて引き直す）。</summary>
    public class AffixRerollV131Tests
    {
        private static (Profile P, Relic R) WithRelic(Rarity rarity, int seed = 7, int shards = 5000, int tuning = 50)
        {
            var p = Profile.CreateNew((ulong)seed);
            var r = Loot.RollRelic(new Rng((ulong)seed), rarity, 5, Slot.Weapon);
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, shards);
            p.AddMaterial(Materials.Tuning, tuning);
            return (p, r);
        }

        [Fact]
        public void Epic_cost_after_35_rerolls_is_exact_and_not_negative()
        {
            // Issue #30：480×3^35 は long を超えるが、費用そのものは int に収まる
            var r = new Relic { Rarity = Rarity.Epic, AffixRerolls = 35 };
            Assert.Equal((698_932_611, 23_297_754), Rules.AffixRerollCost(r));
        }

        [Fact]
        public void Reroll_deducts_both_materials_and_raises_the_counter_and_next_cost()
        {
            var (p, r) = WithRelic(Rarity.Rare);
            var (shards, tuning) = Rules.AffixRerollCost(r);
            Assert.Equal((180, 6), (shards, tuning));
            var ev = Rules.AffixReroll(p, r.Uid);
            Assert.Equal(EventKind.Info, ev.Kind);
            Assert.Equal(5000 - shards, p.Material(Materials.Shard));
            Assert.Equal(50 - tuning, p.Material(Materials.Tuning));
            Assert.Equal(1, r.AffixRerolls);
            Assert.Equal((270, 9), Rules.AffixRerollCost(r));
            Rules.AffixReroll(p, r.Uid);
            Assert.Equal(2, r.AffixRerolls);
            Assert.Equal((405, 14), Rules.AffixRerollCost(r)); // 調律石は13.5の切り上げ
        }

        [Fact]
        public void Reroll_count_survives_save_and_load_and_clone()
        {
            var (p, r) = WithRelic(Rarity.Epic);
            Rules.AffixReroll(p, r.Uid);
            Rules.AffixReroll(p, r.Uid);
            string json = ProfileCodec.Write(p);
            Assert.Contains("affixRerolls", json);
            var back = ProfileCodec.Read(json, new List<string>());
            Assert.Equal(2, back.FindStash(r.Uid).AffixRerolls);
            Assert.Equal(2, p.Clone().FindStash(r.Uid).AffixRerolls);
        }

        [Fact]
        public void Old_saves_without_the_key_default_to_zero_rerolls()
        {
            var (p, r) = WithRelic(Rarity.Rare);
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(p));
            var body = (JsonObject)root.Properties.First(x => x.Key == "body").Value;
            var stash = (List<object>)body.Properties.First(x => x.Key == "stash").Value;
            for (int i = 0; i < stash.Count; i++)
            {
                var old = (JsonObject)stash[i];
                var stripped = new JsonObject();
                foreach (var kv in old.Properties) if (kv.Key != "affixRerolls") stripped.Add(kv.Key, kv.Value);
                stash[i] = stripped;
            }
            string bodyJson = Json.Write(body);
            string oldSave = Json.Write(new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)3)
                .Add("checksum", "sha256:" + Sha256(bodyJson)).Add("body", body));
            var loaded = ProfileCodec.Read(oldSave, new List<string>());
            Assert.Equal(0, loaded.FindStash(r.Uid).AffixRerolls);
        }

        [Fact]
        public void Reroll_keeps_affix_count_and_everything_else()
        {
            var (p, r) = WithRelic(Rarity.Epic, tuning: 500);
            r.Enhance = 3; // +3の節目で特性が1行増える（節目ぶん、新品より多い）
            Rules.GrantEnhanceMilestones(new Rng(7), r);
            int count = r.Affixes.Count;
            Assert.True(count > Content.AffixCount(r.Rarity));
            var powers = r.Powers.Select(x => (x.Power, x.Value)).ToList();
            string uid = r.Uid, baseId = r.BaseId, uniqueId = r.UniqueId;
            var rarity = r.Rarity;
            int ilvl = r.ItemLevel, enhance = r.Enhance, breaks = r.LimitBreaks, milestones = r.EnhanceMilestones, retunes = r.Retunes;
            int awakenPoints = r.AwakenPoints, awakenLevel = r.AwakenLevel;
            for (int i = 0; i < 4; i++) Rules.AffixReroll(p, r.Uid);
            Assert.Equal(uid, r.Uid);
            Assert.Equal(baseId, r.BaseId);
            Assert.Equal(uniqueId, r.UniqueId);
            Assert.Equal(rarity, r.Rarity);
            Assert.Equal(ilvl, r.ItemLevel);
            Assert.Equal(enhance, r.Enhance);
            Assert.Equal(breaks, r.LimitBreaks);
            Assert.Equal(milestones, r.EnhanceMilestones);
            Assert.Equal(retunes, r.Retunes);
            Assert.Equal(awakenPoints, r.AwakenPoints);
            Assert.Equal(awakenLevel, r.AwakenLevel);
            Assert.Equal(powers, r.Powers.Select(x => (x.Power, x.Value)).ToList());
            Assert.Equal(count, r.Affixes.Count);
            Assert.Equal(4, r.AffixRerolls);
            // 新品と同じ抽選：能力値は重ならず、土台の暗黙値とも重ならず、値は範囲内
            Assert.Equal(r.Affixes.Count, r.Affixes.Select(a => a.Stat).Distinct().Count());
            Assert.DoesNotContain(r.Affixes, a => a.Stat == r.Base.ImplicitStat);
            foreach (var a in r.Affixes)
            {
                var def = Content.AffixPool(r.Slot).First(x => x.Stat == a.Stat);
                int level = Content.LevelScalePct(a.Stat, r.ItemLevel);
                int pct = Content.RarityValuePct(r.Rarity) * level / 100;
                Assert.InRange(a.Value, Relic.Scale(def.Min, pct), Relic.Scale(def.Max, pct));
            }
        }

        [Fact]
        public void Reroll_is_deterministic_with_a_seeded_profile()
        {
            var (p1, r1) = WithRelic(Rarity.Rare, seed: 11);
            var (p2, r2) = WithRelic(Rarity.Rare, seed: 11);
            ulong stateBefore = p1.RngState;
            Rules.AffixReroll(p1, r1.Uid);
            Rules.AffixReroll(p2, r2.Uid);
            Assert.Equal(r2.Affixes.Select(a => (a.Stat, a.Value)).ToList(), r1.Affixes.Select(a => (a.Stat, a.Value)).ToList());
            Assert.NotEqual(stateBefore, p1.RngState); // プロフィールの乱数を進めた
            Assert.Equal(p2.RngState, p1.RngState);
        }

        [Fact]
        public void Locked_relic_is_refused_without_any_change()
        {
            var (p, r) = WithRelic(Rarity.Uncommon);
            r.Locked = true;
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToList();
            Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid));
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)).ToList());
            Assert.Equal(0, r.AffixRerolls);
            Assert.Equal(5000, p.Material(Materials.Shard));
            Assert.Equal(50, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Equipped_relic_is_refused_without_any_change()
        {
            var (p, r) = WithRelic(Rarity.Uncommon);
            p.Hero("Hero_Lacerta").Equipped[(int)r.Slot] = r.Uid;
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToList();
            Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid));
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)).ToList());
            Assert.Equal(0, r.AffixRerolls);
            Assert.Equal(5000, p.Material(Materials.Shard));
            Assert.Equal(50, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Trade_reserved_relic_is_refused_without_any_change()
        {
            var (p, r) = WithRelic(Rarity.Uncommon);
            var trades = new TradeLedger();
            trades.Begin(TradeKind.SalvageForDust, 0, 0, 0, r.Uid, now: 1);
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToList();
            Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid, trades));
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)).ToList());
            Assert.Equal(0, r.AffixRerolls);
            Assert.Equal(5000, p.Material(Materials.Shard));
            Assert.Equal(50, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Relic_with_a_pending_retune_offer_is_refused()
        {
            var (p, r) = WithRelic(Rarity.Uncommon);
            Rules.Retune(p, r.Uid, 0);
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToList();
            Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid));
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)).ToList());
            Assert.Equal(0, r.AffixRerolls);
        }

        [Fact]
        public void Missing_materials_are_refused_with_the_real_costs_in_both_languages()
        {
            var (p, r) = WithRelic(Rarity.Legendary, shards: 100, tuning: 5); // 欠片600・調律石20がいる
            var affixes = r.Affixes.Select(a => (a.Stat, a.Value)).ToList();
            var ex = Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid));
            Assert.Contains("600", ex.Message);
            Assert.Contains("20", ex.Message);
            Loc.Japanese = false;
            try
            {
                ex = Assert.Throws<InvalidOperationException>(() => Rules.AffixReroll(p, r.Uid));
                Assert.Contains("600 shards and 20 tuning stones needed", ex.Message);
            }
            finally { Loc.Japanese = true; }
            Assert.Equal(affixes, r.Affixes.Select(a => (a.Stat, a.Value)).ToList());
            Assert.Equal(0, r.AffixRerolls);
            Assert.Equal(100, p.Material(Materials.Shard));
            Assert.Equal(5, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Uniques_keep_their_fixed_powers()
        {
            var (p, r) = WithRelic(Rarity.Legendary, seed: 9, shards: 100000, tuning: 10000);
            Assert.NotNull(r.UniqueId);
            var powers = r.Powers.Select(x => (x.Power, x.Value)).ToList();
            for (int i = 0; i < 3; i++) Rules.AffixReroll(p, r.Uid);
            Assert.Equal(powers, r.Powers.Select(x => (x.Power, x.Value)).ToList());
            Assert.Equal(Content.AffixCount(Rarity.Legendary), r.Affixes.Count);
            Assert.Equal(3, r.AffixRerolls);
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text))) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

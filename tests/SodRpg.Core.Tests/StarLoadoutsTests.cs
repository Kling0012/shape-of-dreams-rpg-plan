using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 星図ロードアウト（旅人ごと5枠）。経験・ポイント・装備は共通で、割り振り・選択・刻印だけが枠ごとに保存される。
    /// 既存データは枠1になり、未使用なら保存バイト列は従来のまま。遠征中は切り替えられない。
    /// 不正な枠は既存の無料払い戻しで有効な割り振りに落ち、協力取引・チェックポイントでも枠は失われない。
    /// </summary>
    public sealed class StarLoadoutsTests
    {
        private const string HeroKey = "Hero_Cetus";

        /// <summary>枠1に星を買い込んだ（未使用ロードアウトの）プロフィール。ポイントは使い切り、武器を装備する。</summary>
        private static Profile ProfileWithAllocation()
        {
            var p = Profile.CreateNew(1012);
            var hero = p.Hero(HeroKey);
            hero.StarXp = StarProgression.TotalXpForPoints(100);
            BuyStars(p);
            // 刻印「凍てつく潮」の費用（3）だけ残して経験を締め、習熟度を満たして選び、ポイントを使い切る。
            hero.Kills = 20000;
            hero.StarXp = StarProgression.TotalXpForPoints(Rules.SpentPoints(hero, HeroKey) + 3);
            Rules.SetKeystone(p, HeroKey, "h.cetus.key");
            Assert.Equal(0, Rules.FreePoints(p, HeroKey));
            var relic = Loot.RollRelic(new Rng(1012), Rarity.Common, 1, slot: Slot.Weapon);
            p.Stash.Add(relic);
            Rules.Equip(p, HeroKey, relic.Uid);
            return p;
        }

        /// <summary>ツリーを順に最大段階まで買い、買えなくなったら止める。skip で並びの後半から買い、別の割り振りを作る。</summary>
        private static void BuyStars(Profile p, int skip = 0)
        {
            var tree = HeroSigils.TreeFor(HeroKey)
                .OrderBy(n => n.Id.Contains(".cluster.") || n.Id.Contains(".outer.") ? 0 : 1).ToList();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var node in tree.Skip(skip))
                {
                    if (node.IsKeystone) continue;
                    try
                    {
                        if (!p.Hero(HeroKey).Talents.ContainsKey(node.Id)) TreeTestPaths.Connect(p, HeroKey, node.Id);
                        while (p.Hero(HeroKey).Talents.GetValueOrDefault(node.Id) < node.MaxRank)
                        {
                            if (Rules.FreePoints(p, HeroKey) < node.RankCost) break;
                            Rules.AddTalentRank(p, HeroKey, node.Id, node.IsChoice ? (int?)(node.Id.GetHashCode() & 1) : null);
                            progress = true;
                        }
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }

        private static int MaxRankOf(string id) => HeroSigils.TreeFor(HeroKey).First(n => n.Id == id).MaxRank;
        private static Dictionary<string, int> TalentsOf(HeroState h) => new Dictionary<string, int>(h.Talents);
        private static Dictionary<string, int> TalentsOf(StarLoadout slot) => new Dictionary<string, int>(slot.Talents);
        private static Dictionary<string, int> ChoicesOf(HeroState h) => new Dictionary<string, int>(h.TalentChoices);

        private static Relic StashRelic(string uid)
        {
            var relic = Loot.RollRelic(new Rng(17), Rarity.Common, 1);
            relic.Uid = uid;
            return relic;
        }

        [Fact]
        public void Unused_loadouts_save_legacy_bytes_and_existing_data_becomes_slot_1()
        {
            var p = ProfileWithAllocation();
            string saved = ProfileCodec.Write(p);
            Assert.DoesNotContain("starLoadouts", saved); // 未使用なら鍵を出さず従来の保存形式のまま

            var loaded = ProfileCodec.Read(saved, new List<string>());
            var hero = loaded.Hero(HeroKey);
            Assert.Equal(0, hero.ActiveStarLoadout); // 既存の割り振りは枠1として読み込まれる
            Assert.False(StarLoadouts.HasSavedState(hero));
            Assert.Equal(saved, ProfileCodec.Write(loaded)); // 読み込み直後の再保存も同じバイト列
        }

        [Fact]
        public void Switching_slots_restores_each_allocation_and_keeps_names_across_save_load()
        {
            var p = ProfileWithAllocation();
            var hero = p.Hero(HeroKey);
            var firstTalents = TalentsOf(hero);
            var firstChoices = ChoicesOf(hero);
            var firstKeystones = (string[])hero.Keystones.Clone();
            StarLoadouts.Rename(p, HeroKey, 0, "本命");

            StarLoadouts.Switch(p, HeroKey, 1); // 枠2は空
            Assert.Equal(1, hero.ActiveStarLoadout);
            Assert.Empty(hero.Talents);
            BuyStars(p, skip: 40); // 枠2に別の割り振り
            var secondTalents = TalentsOf(hero);
            Assert.NotEqual(firstTalents, secondTalents);
            StarLoadouts.Rename(p, HeroKey, 1, "実験");

            StarLoadouts.Switch(p, HeroKey, 0); // 枠1へ戻すと元の割り振りに戻る
            Assert.Equal(0, hero.ActiveStarLoadout);
            Assert.Equal(firstTalents, TalentsOf(hero));
            Assert.Equal(firstChoices, ChoicesOf(hero));
            Assert.Equal(firstKeystones, hero.Keystones);

            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>()); // 保存→読み込み
            var loadedHero = loaded.Hero(HeroKey);
            Assert.Equal(0, loadedHero.ActiveStarLoadout);
            Assert.Equal("本命", loadedHero.StarLoadouts[0].Name);
            Assert.Equal("実験", loadedHero.StarLoadouts[1].Name);
            Assert.Equal(firstTalents, TalentsOf(loadedHero));
            StarLoadouts.Switch(loaded, HeroKey, 1); // 読み込み後も退避した枠2へ切り替えられる
            Assert.Equal(secondTalents, TalentsOf(loaded.Hero(HeroKey)));
        }

        [Fact]
        public void Switching_keeps_shared_star_points_and_equipment()
        {
            var p = ProfileWithAllocation();
            var hero = p.Hero(HeroKey);
            hero.ExtraPoints = 7;
            int starXp = hero.StarXp;
            var equipped = (string[])hero.Equipped.Clone();

            StarLoadouts.Switch(p, HeroKey, 1);
            Assert.Empty(hero.Talents);
            Assert.Equal(starXp, hero.StarXp);
            Assert.Equal(7, hero.ExtraPoints);
            Assert.Equal(equipped, hero.Equipped);

            BuyStars(p, skip: 40);
            StarLoadouts.Switch(p, HeroKey, 0);
            Assert.Equal(starXp, hero.StarXp);
            Assert.Equal(7, hero.ExtraPoints);
            Assert.Equal(equipped, hero.Equipped);
        }

        [Fact]
        public void Switching_is_blocked_during_an_expedition()
        {
            var p = ProfileWithAllocation();
            var hero = p.Hero(HeroKey);
            var before = TalentsOf(hero);
            p.Run = new RunState { RunId = "run-1", HeroKey = HeroKey };
            Assert.Throws<InvalidOperationException>(() => StarLoadouts.Switch(p, HeroKey, 1));
            Assert.Equal(0, hero.ActiveStarLoadout);
            Assert.Equal(before, TalentsOf(hero));
            Assert.Null(hero.StarLoadouts[1]);
        }

        [Fact]
        public void Corrupted_and_over_budget_slots_load_as_valid_allocations()
        {
            var p = ProfileWithAllocation();
            var hero = p.Hero(HeroKey);
            var allocation = TalentsOf(hero);
            var keystones = (string[])hero.Keystones.Clone();

            var corrupted = StarLoadout.Capture(hero); // 正しい割り振りに、存在しない星と上限を超えた段階を混ぜる
            corrupted.Talents["no.such.star"] = 3;
            string inflated = hero.Talents.First(kv => kv.Value == MaxRankOf(kv.Key)).Key;
            corrupted.Talents[inflated] = 99;
            corrupted.Keystones[1] = "not.a.keystone";
            hero.StarLoadouts[1] = corrupted;
            var notes = new List<string>();
            StarLoadouts.Switch(p, HeroKey, 1, notes); // 落ちずに有効な割り振りになる
            Assert.Equal(allocation, TalentsOf(hero));
            Assert.Equal(keystones, hero.Keystones);
            Assert.True(Rules.FreePoints(p, HeroKey) >= 0);
            Assert.Contains(notes, n => n.Contains("ロードアウト2"));
            Assert.Equal(allocation, TalentsOf(hero.StarLoadouts[0])); // 元の枠1は退避されている

            var overBudget = StarLoadout.Capture(hero); // ポイント上限を超える構成
            foreach (var node in HeroSigils.TreeFor(HeroKey).Where(n => !n.IsKeystone && !n.IsChoice))
                overBudget.Talents[node.Id] = node.MaxRank;
            hero.StarLoadouts[2] = overBudget;
            notes.Clear();
            StarLoadouts.Switch(p, HeroKey, 2, notes);
            Assert.True(Rules.FreePoints(p, HeroKey) >= 0);
            Assert.All(hero.Talents, rank => Assert.InRange(rank.Value, 1, MaxRankOf(rank.Key)));
            Assert.True(Rules.AllocationValidationForHero(HeroKey).AllocationsConnected(hero));
            Assert.Contains(notes, n => n.Contains("ロードアウト3"));
            Assert.Equal(allocation, TalentsOf(hero.StarLoadouts[1]));
        }

        [Fact]
        public void Copying_the_current_allocation_fills_another_slot()
        {
            var p = ProfileWithAllocation();
            var hero = p.Hero(HeroKey);
            var talents = TalentsOf(hero);
            var choices = ChoicesOf(hero);
            var keystones = (string[])hero.Keystones.Clone();
            StarLoadouts.Rename(p, HeroKey, 1, "予備");

            StarLoadouts.CopyCurrent(p, HeroKey, 1); // UI の「現在の構成をこの枠にコピー」相当
            Assert.Equal("予備", hero.StarLoadouts[1].Name);
            Assert.Equal(talents, TalentsOf(hero.StarLoadouts[1]));
            Assert.Equal(choices, new Dictionary<string, int>(hero.StarLoadouts[1].TalentChoices));
            Assert.Equal(keystones, hero.StarLoadouts[1].Keystones);

            StarLoadouts.Switch(p, HeroKey, 1); // 複製した枠へ切り替えても同じ構成になる
            Assert.Equal(talents, TalentsOf(hero));
            Assert.Equal(keystones, hero.Keystones);
            Assert.Equal(0, Rules.FreePoints(p, HeroKey));
        }

        [Fact]
        public void Checkpoint_restore_keeps_the_latest_loadouts()
        {
            var p = ProfileWithAllocation();
            var hero = p.Hero(HeroKey);
            var firstTalents = TalentsOf(hero);
            StarLoadouts.Rename(p, HeroKey, 0, "本命");
            StarLoadouts.Switch(p, HeroKey, 1);
            BuyStars(p, skip: 40);
            var secondTalents = TalentsOf(hero);
            StarLoadouts.Rename(p, HeroKey, 1, "実験");

            p.Run = new RunState { RunId = "run-1", HeroKey = HeroKey };
            var checkpoint = RunCheckpoint.Capture(p, "cp-1");
            hero.StarLoadouts[0].Name = "改名"; // チェックポイントより後のロビーでの変更
            checkpoint.Restore(p, notes: new List<string>());
            hero = p.Hero(HeroKey); // Restore は現在のプロフィールへ書き戻す
            Assert.Equal(1, hero.ActiveStarLoadout);
            Assert.Equal(secondTalents, TalentsOf(hero));
            Assert.Equal("改名", hero.StarLoadouts[0].Name); // 最新の枠と名前が優先される
            Assert.Equal("実験", hero.StarLoadouts[1].Name);
            Assert.Equal(firstTalents, TalentsOf(hero.StarLoadouts[0]));
            Assert.True(Rules.FreePoints(p, HeroKey) >= 0);
        }

        [Fact]
        public void Coop_trade_keeps_loadouts_through_refreshed_checkpoints()
        {
            var first = ProfileWithAllocation();
            first.Stash.Add(StashRelic("first-relic"));
            first.AddMaterial(Materials.Shard, 100);
            first.AddMaterial(Materials.Tuning, 10);
            var hero = first.Hero(HeroKey);
            var firstTalents = TalentsOf(hero);
            StarLoadouts.Rename(first, HeroKey, 0, "本命");
            StarLoadouts.Switch(first, HeroKey, 1);
            BuyStars(first, skip: 40);
            var secondTalents = TalentsOf(first.Hero(HeroKey));
            StarLoadouts.Rename(first, HeroKey, 1, "実験");

            first.Run = new RunState { RunId = "trade-run", HeroKey = HeroKey };
            first.ContinueCheckpoints.Add(RunCheckpoint.Capture(first, "native"));
            var second = Profile.CreateNew(18);
            second.Stash.Add(StashRelic("second-relic"));
            second.AddMaterial(Materials.Shard, 100);
            second.AddMaterial(Materials.Tuning, 10);
            var receipt = CoopTradeRules.CreateReceipt("trade-1", "host", "first", first,
                new CoopTradeOffer { RelicUids = new List<string> { "first-relic" }, Shards = 30, Tuning = 2 },
                "second", second,
                new CoopTradeOffer { RelicUids = new List<string> { "second-relic" }, Shards = 7, Tuning = 1 });
            CoopTradeRules.Prepare(first, receipt.Id, receipt.HostKey, receipt.FirstOffer);
            CoopTradeRules.Prepare(second, receipt.Id, receipt.HostKey, receipt.SecondOffer);
            Assert.True(CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.True(CoopTradeRules.Resolve(second, receipt, "second"));

            first.ContinueCheckpoints.Single().Restore(first); // 取引で書き換えたチェックポイントから続きを再開
            hero = first.Hero(HeroKey);
            Assert.Equal(1, hero.ActiveStarLoadout);
            Assert.Equal(secondTalents, TalentsOf(hero));
            Assert.Equal("本命", hero.StarLoadouts[0].Name);
            Assert.Equal("実験", hero.StarLoadouts[1].Name);
            Assert.Equal(firstTalents, TalentsOf(hero.StarLoadouts[0]));
            Assert.NotNull(first.FindStash("second-relic")); // 取引結果はそのまま
            Assert.True(Rules.FreePoints(first, HeroKey) >= 0);
        }
    }
}

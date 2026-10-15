using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #427：禁忌の核（Materials.NemesisCore）は書き込み側が正規素材として保存するのに、
    /// 読み込み側（ProfileCodec.ReadBody）の許可一覧が Shard/Tuning のため「未知の素材」として
    /// 除外される。通常保存・チェックポイントの往復で核が0になり、核を含む経済チェックポイントは
    /// Continue（RunCheckpoint.Restore）で「協力取引の経済保存情報が不正です」と拒否される。
    /// 既知素材は保存・読み込みで量が維持され、除外 notes を出さないこと。
    /// </summary>
    public sealed class NemesisCoreSaveIssue427Tests
    {
        private static Relic Relic(string uid)
        {
            var relic = Loot.RollRelic(new Rng(17), Rarity.Common, 1);
            relic.Uid = uid;
            return relic;
        }

        private static Profile WithAllMaterials()
        {
            var p = Profile.CreateNew(17);
            p.AddMaterial(Materials.Shard, 120);
            p.AddMaterial(Materials.Tuning, 12);
            p.AddMaterial(Materials.NemesisCore, 3);
            return p;
        }

        private static void AssertKnownMaterialsKept(Profile p)
        {
            Assert.Equal(120, p.Material(Materials.Shard));
            Assert.Equal(12, p.Material(Materials.Tuning));
            Assert.Equal(3, p.Material(Materials.NemesisCore));
        }

        [Fact]
        public void Known_materials_survive_profile_write_read_roundtrip()
        {
            var p = WithAllMaterials();
            var notes = new List<string>();
            var restored = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            AssertKnownMaterialsKept(restored);
            Assert.DoesNotContain(notes, note => note.Contains("未知の素材を除外"));
        }

        [Fact]
        public void Known_materials_survive_checkpoint_write_read_roundtrip()
        {
            var p = WithAllMaterials();
            var notes = new List<string>();
            var restored = ProfileCodec.ReadCheckpointProfile(ProfileCodec.WriteCheckpointProfile(p), notes);
            AssertKnownMaterialsKept(restored);
            Assert.DoesNotContain(notes, note => note.Contains("未知の素材を除外"));
        }

        [Fact]
        public void Unknown_materials_are_still_excluded_on_read()
        {
            // 既知素材を増やしても、未知の素材の除外と負数・上限の維持は変えない。
            var p = WithAllMaterials();
            p.Materials["futureOre"] = 5;
            var notes = new List<string>();
            var restored = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Equal(0, restored.Material("futureOre"));
            Assert.Contains(notes, note => note.Contains("未知の素材を除外: futureOre"));
        }

        /// <summary>
        /// 核を所持したまま協力取引を完了し（ローカルの Prepare/Finish が CoopTradeEconomy に核を残す）、
        /// 保存→再読み込み後に Continue しても経済チェックポイントが「不正」と拒否されず、
        /// 素材・交換した遺物・二重適用防止状態（CoopTradeExecuted）が正しく復元されること。
        /// ホストは受領書をネットワークで受け取ったプロフィールから作る（HostAuthority.CoopTrade の
        /// ReadCheckpointProfile 経由）ので、ここでも同じ変換を通す。
        /// </summary>
        [Fact]
        public void Continue_after_coop_trade_with_nemesis_core_restores_economy()
        {
            var first = Profile.CreateNew(17);
            first.Run = new RunState { RunId = "run", HeroKey = "default" };
            first.Stash.Add(Relic("first-relic"));
            first.AddMaterial(Materials.Shard, 100);
            first.AddMaterial(Materials.Tuning, 10);
            first.AddMaterial(Materials.NemesisCore, 3);
            var native = RunCheckpoint.Capture(first, "native-before-trade");

            var second = Profile.CreateNew(18);
            second.Stash.Add(Relic("second-relic"));
            second.AddMaterial(Materials.Shard, 50);
            second.AddMaterial(Materials.Tuning, 5);
            var receipt = CoopTradeRules.CreateReceipt("trade", "host", "first",
                ProfileCodec.ReadCheckpointProfile(ProfileCodec.WriteCheckpointProfile(first)),
                new CoopTradeOffer { RelicUids = new List<string> { "first-relic" }, Shards = 30, Tuning = 2 },
                "second",
                ProfileCodec.ReadCheckpointProfile(ProfileCodec.WriteCheckpointProfile(second)),
                new CoopTradeOffer { RelicUids = new List<string> { "second-relic" }, Shards = 7, Tuning = 1 });
            CoopTradeRules.Prepare(first, receipt.Id, receipt.HostKey, receipt.FirstOffer);
            CoopTradeRules.Prepare(second, receipt.Id, receipt.HostKey, receipt.SecondOffer);
            Assert.True(CoopTradeRules.Resolve(first, receipt, "first"));
            Assert.True(CoopTradeRules.Resolve(second, receipt, "second"));
            Assert.Equal(3, first.Material(Materials.NemesisCore));
            Assert.Contains("nemesisCore", first.CoopTradeEconomy, StringComparison.Ordinal);

            // 保存して再読み込み（再起動）し、そこから Continue する。
            first = ProfileCodec.Read(ProfileCodec.Write(first), new List<string>());

            native.Restore(first);

            Assert.Equal(3, first.Material(Materials.NemesisCore));
            Assert.Equal(100 - 30 + 7, first.Material(Materials.Shard));
            Assert.Equal(10 - 2 + 1, first.Material(Materials.Tuning));
            Assert.NotNull(first.FindStash("second-relic"));
            Assert.Null(first.FindStash("first-relic"));
            Assert.Contains("trade", first.CoopTradeExecuted);
        }
    }
}

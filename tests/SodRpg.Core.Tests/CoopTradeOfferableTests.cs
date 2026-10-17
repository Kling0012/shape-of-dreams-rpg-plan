using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 提示した遺物を後から再調律すると、ホストの検証（Validate）は「別の処理の応答待ち」で断る。
    /// UI が提示から外す判定（RelicOfferable）も同じ条件で、警告と確認ボタンの無効化に使われる。
    /// </summary>
    public sealed class CoopTradeOfferableTests
    {
        [Fact]
        public void Relic_with_a_pending_retune_is_not_offerable_in_ui_or_host_validation()
        {
            var p = Profile.CreateNew(77);
            p.AddMaterial(Materials.Shard, 100000);
            p.AddMaterial(Materials.Tuning, 1000);
            var r = Loot.RollRelic(new Rng(77), Rarity.Epic, 40);
            Assert.NotEmpty(r.Affixes);
            p.Stash.Add(r);
            var offer = new CoopTradeOffer();
            offer.RelicUids.Add(r.Uid);
            Assert.True(CoopTradeRules.RelicOfferable(p, r.Uid));
            Assert.Null(CoopTradeRules.Validate(p, offer));

            Rules.Retune(p, r.Uid, 0);
            Assert.Equal(r.Uid, p.RetuneOffer?.Uid);
            Assert.NotNull(CoopTradeRules.Validate(p, offer));
            Assert.False(CoopTradeRules.RelicOfferable(p, r.Uid));
        }

        [Fact]
        public void Equipped_or_missing_relics_are_not_offerable()
        {
            var p = Profile.CreateNew(78);
            var r = Loot.RollRelic(new Rng(78), Rarity.Rare, 20);
            p.Stash.Add(r);
            Assert.True(CoopTradeRules.RelicOfferable(p, r.Uid));
            Rules.Equip(p, "Hero_Vesper", r.Uid);
            Assert.False(CoopTradeRules.RelicOfferable(p, r.Uid));
            Assert.False(CoopTradeRules.RelicOfferable(p, "missing"));
        }
    }
}

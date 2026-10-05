using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Profile.Clone が保留中の取引の全フィールドを写すこと。公開フィールドを反射で列挙して突き合わせるので、
    /// PendingTrade にフィールドを足しても Clone に並べ忘れればここが落ちる（StartedAt・Queries など保存に含まれない
    /// 一時的なフィールドも、写しとしては同じ値を持つ）。
    /// </summary>
    public sealed class ProfileCloneTests
    {
        [Fact]
        public void Clone_copies_every_pending_trade_field()
        {
            var p = Profile.CreateNew(1);
            p.PendingTrades.Add(new PendingTrade
            {
                Token = 12, Kind = TradeKind.MerchantGold, SpendGold = 60, SpendDust = 3, EarnDust = 4,
                Uid = "relic-uid", StartedAt = 9.5, Unresolved = true, Queries = 2, NextQueryAt = 11.5,
                LedgerId = 77, Lost = true, Heat = 5, MerchantOfferId = "offer-1", Batches = 2,
                Rarity = (int)Rarity.Epic, Enhance = 3,
            });

            var copy = p.Clone();

            var original = p.PendingTrades.Single();
            var cloned = copy.PendingTrades.Single();
            Assert.NotSame(original, cloned);
            foreach (var field in typeof(PendingTrade).GetFields(BindingFlags.Instance | BindingFlags.Public))
                Assert.Equal(field.GetValue(original), field.GetValue(cloned));
        }
    }
}

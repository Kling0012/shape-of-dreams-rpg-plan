using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>遺失物の地図・残響ランタンを強化したら、ヒントと契約の説明の数値も強化後の値になる。</summary>
    public class WorkshopUpgradeTextTests
    {
        private static Profile Upgraded()
        {
            var p = Profile.CreateNew(1601);
            p.AddMaterial(Materials.Shard, 100000);
            p.AddMaterial(Materials.Tuning, 1000);
            Rules.BuyUpgrade(p, Upgrade.EchoLantern);
            Rules.BuyUpgrade(p, Upgrade.LostMap);
            return p;
        }

        [Fact]
        public void Defeat_hint_uses_the_upgraded_rooms_and_echo_percent()
        {
            var p = Upgraded();
            var def = Onboarding.Get(Hint.FirstDefeat);
            bool saved = Loc.Japanese;
            try
            {
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    string text = def.Text(p);
                    Assert.Contains($"{Workshop.EchoPercent(p)}%", text);
                    Assert.DoesNotContain($"{Workshop.EchoPercent(null)}%", text);
                    Assert.Contains(ja ? $"戦闘部屋を{Workshop.RoomsToRecover(p)}つ" : $"Clear {Workshop.RoomsToRecover(p)} combat rooms", text);
                    Assert.Equal(def.Body.ToString(), def.Text(null));
                }
            }
            finally { Loc.Japanese = saved; }
        }

        [Fact]
        public void No_echo_pact_text_uses_the_upgraded_echo_percent()
        {
            var p = Upgraded();
            var pact = Pacts.Get(Pact.CursedHoard);
            Assert.True(pact.NoEcho);
            bool saved = Loc.Japanese;
            try
            {
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    Assert.Contains($"{Workshop.EchoPercent(p)}%", pact.DescriptionFor(p));
                    Assert.Contains($"{Workshop.EchoPercent(null)}%", pact.Description);
                }
            }
            finally { Loc.Japanese = saved; }
        }
    }
}

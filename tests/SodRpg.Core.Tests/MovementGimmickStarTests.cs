using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 移動の記憶に付く仕掛けの星も買える（効果の確認で移動の記憶を通常の記憶として変換し、例外で止まっていた）。
    /// </summary>
    public sealed class MovementGimmickStarTests
    {
        [Theory]
        [InlineData("Hero_Cetus", "h.cetus.route.frost-charge.2")]
        [InlineData("Hero_Vesper", "h.vesper.route.charge.2")]
        public void Movement_memory_gimmick_star_can_be_bought(string hero, string id)
        {
            var p = Profile.CreateNew(17);
            p.Hero(hero).StarXp = StarProgression.TotalXpForPoints(40);
            TreeTestPaths.Connect(p, hero, id);
            Rules.AddTalentRank(p, hero, id);
            Assert.True(p.Hero(hero).Talents.TryGetValue(id, out int rank) && rank == 1);
        }

        [Fact]
        public void Acquired_effects_show_essence_slots_at_the_cap()
        {
            const string hero = "Hero_Cetus";
            var p = Profile.CreateNew(19);
            p.Hero(hero).StarXp = StarProgression.TotalXpForPoints(200);
            foreach (string id in new[] { "h.cetus.route.icy-veins.slot", "h.cetus.route.charged.slot" })
            {
                TreeTestPaths.Connect(p, hero, id);
                for (int i = 0; i < 4; i++) Rules.AddTalentRank(p, hero, id);
            }
            int cap = Content.StatCap(Stat.EssenceSlotIdentity);
            Assert.Equal(cap, Build.Compute(p, hero, 0).Get(Stat.EssenceSlotIdentity));
            bool ja = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                var line = Assert.Single(StarSummary.Compute(p, hero).Stats, l => l.Text.StartsWith("エッセンス枠"));
                Assert.StartsWith("エッセンス枠 +" + cap + "。", line.Text);
            }
            finally { Loc.Japanese = ja; }
        }
    }
}

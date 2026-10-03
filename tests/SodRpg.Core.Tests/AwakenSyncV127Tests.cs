using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>issue #14・#15：覚醒後の連携の値が表示とホストでそろうこと、段が上がるたびに送り直しが分かること。</summary>
    public class AwakenSyncV127Tests
    {
        public static IEnumerable<object[]> LinkedUniques()
        {
            // 条件の数1〜3、効果の種類ごとに実在する固有品を選ぶ
            var picked = Content.Uniques.Where(u => u.Link != null)
                .GroupBy(u => (u.Link.Kind, u.Link.Requires.Length))
                .Select(g => g.OrderByDescending(u => u.Link.Value).First());
            foreach (var u in picked)
                for (int level = 0; level <= Content.MaxAwakenLevel; level++)
                    yield return new object[] { u.Id, level };
        }

        [Theory]
        [MemberData(nameof(LinkedUniques))]
        public void Link_value_survives_encode_and_decode_at_every_awakening_level(string uniqueId, int level)
        {
            var unique = Content.Uniques.First(u => u.Id == uniqueId);
            var relic = Loot.RollUnique(new Rng(3), unique, 1);
            relic.AwakenLevel = level;
            var p = Profile.CreateNew(5);
            p.Stash.Add(relic);
            Rules.Equip(p, "Hero_Vesper", relic.Uid);

            var local = Build.Compute(p, "Hero_Vesper", 0);
            var link = Assert.Single(local.Links);
            Assert.InRange(link.Value, 0, 2.5m * Links.EquippedCap(link.Kind, link.Requires.Length));
            var host = Build.Decode(local.Encode());
            Assert.NotNull(host);
            Assert.Equal(link.Value, Assert.Single(host.Links).Value); // 表示（ローカル）とホストの実効値が同じ
        }

        [Fact]
        public void Haste_base_cap_remains_bounded_before_awakening()
        {
            for (int n = 1; n <= 3; n++)
                Assert.True(Links.EquippedCap(LinkKind.MemoryHaste, n) <= Links.MaxHaste);
        }

        [Fact]
        public void Every_awakening_step_is_visible_to_the_resend_check()
        {
            var unique = Content.Uniques.First(u => u.Link == null && u.SetId == null);
            var relic = Loot.RollUnique(new Rng(9), unique, 1);
            var p = Profile.CreateNew(7);
            p.Stash.Add(relic);
            Rules.Equip(p, "Hero_Husk", relic.Uid);
            Rules.BeginRun(p, "Hero_Husk");

            int[] thresholds = { 0, Content.AwakenThresholdFor(1), Content.AwakenThresholdFor(2), Content.AwakenThresholdFor(3) };
            for (int level = 1; level <= Content.MaxAwakenLevel; level++)
            {
                relic.AwakenPoints = thresholds[level] - 1;
                int before = Rules.EquippedAwakenLevels(p, "Hero_Husk");
                int feats = p.Stats.RelicsAwakened;
                Rules.OnKill(p, MonsterTier.Lesser, 1, NightmareAffix.None, "Hero_Husk");
                Assert.Equal(level, relic.AwakenLevel);
                Assert.True(Rules.EquippedAwakenLevels(p, "Hero_Husk") > before); // Ⅰ→Ⅱ・Ⅱ→Ⅲでも送り直す
                Assert.Equal(level == 1 ? feats + 1 : feats, p.Stats.RelicsAwakened); // 実績は最初の覚醒だけ
            }
        }
    }
}

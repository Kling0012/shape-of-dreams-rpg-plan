using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.32：銘品・組の本体データ（tools/lowrarity/named.json・minisets.json → NamedItems.Data.cs）。
    /// 登録数・ID・帯の固定値・レア度の規則・抽選の重み比・組の集計を確かめる（設計 3.2・3.4・試験2〜4・7）。
    /// </summary>
    public class NamedItemsDataV132Tests : IDisposable
    {
        public NamedItemsDataV132Tests()
        {
            // ほかの試験クラスが登録簿を差し替えていても、ここは常に本体データで試す。
            NamedItems.RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets);
        }

        public void Dispose() => NamedItems.RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets);

        [Fact]
        public void No_uncommon_or_rare_roll_ever_carries_attack_or_power_percent_or_conditionals()
        {
            // 抽選の結果（銘品も通常品も）に攻撃力%・魔力%の特性と条件付き攻撃力・魔力の固有効果は出ない（試験2）。
            for (int i = 0; i < 4000; i++)
            {
                var rarity = i % 2 == 0 ? Rarity.Uncommon : Rarity.Rare;
                var slot = (Slot)(i % 6);
                var focus = (Line?)(i % 3);
                var r = Loot.RollRelic(new Rng((ulong)(810000 + i)), rarity, 10 + i % 30, slot, focus);
                Assert.DoesNotContain(r.Affixes, a => a.Stat == Stat.AttackPct || a.Stat == Stat.PowerPct);
                Assert.DoesNotContain(r.Powers, p => NewPowersV129.IsConditionalAttribute(p.Power));
                if (r.NamedId != null)
                {
                    // 銘品の固有効果は定義どおりの固定値。
                    Assert.True(NamedItems.TryGetNamed(r.NamedId, out var def), r.NamedId);
                    Assert.Equal(def.Powers.Count, r.Powers.Count);
                    for (int j = 0; j < def.Powers.Count; j++)
                        Assert.Equal((def.Powers[j].Power, def.Powers[j].Value), (r.Powers[j].Power, r.Powers[j].Value));
                }
            }
        }

    }
}

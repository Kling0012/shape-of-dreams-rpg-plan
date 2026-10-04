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

        // 帯 → 固定値（gen_named_cs.py と同じ式。四捨五入・範囲内に丸める）。
        private static int BandValue(int min, int max, string band)
        {
            int num, den;
            switch (band)
            {
                case "low": num = 1; den = 6; break;
                case "mid": num = 2; den = 5; break;
                case "high": num = 13; den = 20; break;
                default: throw new ArgumentException("band");
            }
            int value = (min * (den - num) + max * num + den / 2) / den;
            return Math.Max(min, Math.Min(max, value));
        }

        /// <summary>固有効果の Min-Max。どの枠の池でも同じ値（gen_named_cs.py が確認する）。</summary>
        private static (int Min, int Max) RangeOf(Power power, Slot slot)
        {
            var own = Content.PowerPool(slot).FirstOrDefault(r => r.Power == power);
            if (own != null) return (own.Min, own.Max);
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
            {
                var r = Content.PowerPool(s).FirstOrDefault(x => x.Power == power);
                if (r != null) return (r.Min, r.Max);
            }
            throw new InvalidOperationException("固有効果の範囲が無い: " + power);
        }

        [Fact]
        public void All_360_named_and_30_mini_sets_are_registered_with_unique_ids()
        {
            Assert.Equal(360, NamedItems.All.Count);
            Assert.Equal(30, NamedItems.MiniSets.Count);
            Assert.Equal(360, NamedItems.All.Select(d => d.Id).Distinct().Count());
            Assert.Equal(30, NamedItems.MiniSets.Select(s => s.Id).Distinct().Count());
            Assert.All(NamedItems.All, d => Assert.True(NamedItems.TryGetNamed(d.Id, out var back) && ReferenceEquals(back, d)));
            Assert.All(NamedItems.MiniSets, s => Assert.True(NamedItems.TryGetMiniSet(s.Id, out var back) && ReferenceEquals(back, s)));
            Assert.Equal(360, CodexQuery.Entries(CodexCategory.Named).Count);
            Assert.Equal(30, CodexQuery.Entries(CodexCategory.MiniSets).Count);

            // 設計 3.2 の表：各枠アンコモン24・レア24・エピック12。
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                Assert.Equal(24, NamedItems.All.Count(d => d.Rarity == Rarity.Uncommon && Content.GetBase(d.BaseId).Slot == slot));
                Assert.Equal(24, NamedItems.All.Count(d => d.Rarity == Rarity.Rare && Content.GetBase(d.BaseId).Slot == slot));
                Assert.Equal(12, NamedItems.All.Count(d => d.Rarity == Rarity.Epic && Content.GetBase(d.BaseId).Slot == slot));
            }
        }

        [Fact]
        public void Every_power_value_is_the_fixed_band_value_inside_its_range()
        {
            foreach (var def in NamedItems.All)
            {
                Assert.InRange(def.Powers.Count, 1, 2);
                var baseDef = Content.GetBase(def.BaseId);
                for (int i = 0; i < def.Powers.Count; i++)
                {
                    var line = def.Powers[i];
                    var (min, max) = RangeOf(line.Power, baseDef.Slot);
                    Assert.True(min <= max, def.Id + " " + line.Power);
                    Assert.InRange(line.Value, min, max); // その固有効果の範囲内

                    // 帯どおりの固定値（設計 3.2）：
                    // アンコモン=下3分の1、レア=下半分、エピック2つ目=下半分。
                    // （エピック1つ目の帯は元データで mid|high。設計書の「6〜7割」に当たるのは high の方。）
                    int span = max - min;
                    if (def.Rarity == Rarity.Uncommon)
                    {
                        Assert.Equal(BandValue(min, max, "low"), line.Value);
                        Assert.InRange(line.Value, min, min + span / 3);
                        Assert.False(NewPowersV129.IsConditionalAttribute(line.Power), def.Id + " に条件付き攻撃力・魔力");
                    }
                    else if (def.Rarity == Rarity.Rare)
                    {
                        Assert.True(line.Value == BandValue(min, max, "low") || line.Value == BandValue(min, max, "mid"), def.Id);
                        Assert.InRange(line.Value, min, min + span / 2);
                        Assert.False(NewPowersV129.IsConditionalAttribute(line.Power), def.Id + " に条件付き攻撃力・魔力");
                    }
                    else if (i == 0)
                    {
                        Assert.True(line.Value == BandValue(min, max, "mid") || line.Value == BandValue(min, max, "high"), def.Id);
                    }
                    else
                    {
                        Assert.True(line.Value == BandValue(min, max, "low") || line.Value == BandValue(min, max, "mid"), def.Id);
                        Assert.InRange(line.Value, min, min + span / 2);
                    }
                }
            }
        }

        [Fact]
        public void Mini_sets_use_only_known_pieces_stats_and_low_band_three_piece_powers()
        {
            var byId = NamedItems.All.ToDictionary(d => d.Id);
            foreach (var set in NamedItems.MiniSets)
            {
                Assert.InRange(set.PieceCount, 2, 3);
                Assert.Equal(set.PieceCount, set.PieceIds.Distinct().Count());
                Assert.All(set.PieceIds, p => Assert.True(byId.ContainsKey(p), set.Id + " " + p));

                bool allEpic = set.PieceIds.All(p => byId[p].Rarity == Rarity.Epic);
                if (!allEpic)
                {
                    // 設計 3.2：アンコモン・レアの部位を含む組は攻撃力%・魔力%と条件付き攻撃力・魔力を使わない。
                    Assert.NotEqual(Stat.AttackPct, set.TwoPiece.Stat);
                    Assert.NotEqual(Stat.PowerPct, set.TwoPiece.Stat);
                    if (set.ThreePiece != null)
                        Assert.False(NewPowersV129.IsConditionalAttribute(set.ThreePiece.Power), set.Id + " 3点効果が条件付き");
                }

                if (set.ThreePiece != null)
                {
                    Assert.Equal(3, set.PieceCount); // 2点の組に3点効果は無い
                    var slots = set.PieceIds.Select(p => Content.GetBase(byId[p].BaseId).Slot).ToList();
                    var (min, max) = RangeOf(set.ThreePiece.Power, slots[0]);
                    Assert.Equal(BandValue(min, max, "low"), set.ThreePiece.Value); // 3点 = low の帯
                    Assert.InRange(set.ThreePiece.Value, min, max);
                }
                else
                {
                    Assert.Equal(2, set.PieceCount);
                }
            }
        }

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

        [Fact]
        public void Loot_simulation_matches_the_weight_ratios_per_slot_and_rarity()
        {
            // 設計 3.4：土台（重み1・各枠60種）と銘品（レア度ごとの定数×その枠・そのレア度の銘品数）の重みどおりの割合。
            // 設計書の「約3分の1・約5分の2」は土台が各枠100種の段階の数値。いまは60種なので
            // アンコモン 48/108、レア・エピック 72/132 が正確な期待値になる（土台が100種になれば設計書どおり）。
            const int n = 16000;
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                int bases = Content.Bases.Count(b => b.Slot == slot);
                foreach (var (rarity, weight) in new[] { (Rarity.Uncommon, 2), (Rarity.Rare, 3), (Rarity.Epic, 6) })
                {
                    int namedCount = NamedItems.All.Count(d => d.Rarity == rarity && Content.GetBase(d.BaseId).Slot == slot);
                    Assert.NotEqual(0, namedCount);
                    double expected = (double)(namedCount * weight) / (bases + namedCount * weight);
                    int hits = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var r = Loot.RollRelic(new Rng((ulong)(820000 + (int)slot * 10000 + (int)rarity * 1000 + i)), rarity, 25, slot);
                        if (r.NamedId != null) hits++;
                    }
                    double share = (double)hits / n;
                    Assert.InRange(share, expected - 0.015, expected + 0.015);
                    if (rarity == Rarity.Uncommon) Assert.True(share < 0.5, "アンコモンの銘品割合が半分を超えた");
                }
            }
        }

        [Fact]
        public void Mini_set_two_and_three_piece_bonuses_apply_in_build_and_respect_percent_caps()
        {
            foreach (var set in NamedItems.MiniSets)
            {
                var pieces = set.PieceIds.Select(id => NamedItems.TryGetNamed(id, out var d) ? d : null).ToList();
                Assert.DoesNotContain(pieces, p => p == null);

                Relic Piece(NamedDef d, string uid) => new Relic
                {
                    Uid = uid, BaseId = d.BaseId, NamedId = d.Id, Rarity = d.Rarity, ItemLevel = 10,
                };

                // 同じ装備から NamedId だけを外したものが基準（組の集計だけの差を見る）。
                Build With(bool asNamed)
                {
                    var p = Profile.CreateNew(8300);
                    var hero = p.Hero("Hero_Vesper");
                    var used = new HashSet<Slot>();
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        var def = pieces[i];
                        var slot = Content.GetBase(def.BaseId).Slot;
                        if (!used.Add(slot)) continue; // 同じ枠には1つ（2つ目は装着できない）
                        var relic = Piece(def, "u-" + i);
                        if (!asNamed) relic.NamedId = null;
                        p.Stash.Add(relic);
                        hero.Equipped[(int)slot] = relic.Uid;
                    }
                    return Build.Compute(p, "Hero_Vesper", 0);
                }

                var plain = With(false);
                var worn = With(true);
                Assert.Equal(set.PieceCount, worn.MiniSets[set.Id]);
                Assert.Equal(worn.Get(set.TwoPiece.Stat) - plain.Get(set.TwoPiece.Stat), set.TwoPiece.Value); // 2点＝能力値1行
                if (set.ThreePiece != null)
                    Assert.Equal(worn.Get(set.ThreePiece.Power) - plain.Get(set.ThreePiece.Power), set.ThreePiece.Value); // 3点＝弱い固有効果1行

                // 能力値の合計上限は組があっても変わらない。ほぼ上限まで稼いだ保持持ちと合わせて超えない。
                int cap = Content.StatCap(set.TwoPiece.Stat);
                Assert.True(cap >= set.TwoPiece.Value, set.Id + " " + set.TwoPiece.Stat);
                var pieceSlots = pieces.Select(p => Content.GetBase(p.BaseId).Slot).ToHashSet();
                var holderSlot = Enum.GetValues(typeof(Slot)).Cast<Slot>().First(s => !pieceSlots.Contains(s));
                var holderBase = Content.Bases.First(b => b.Slot == holderSlot);
                var holder = new Relic
                {
                    Uid = "u-holder", BaseId = holderBase.Id, Rarity = Rarity.Epic, ItemLevel = 10,
                    Affixes = { new StatLine(set.TwoPiece.Stat, cap - set.TwoPiece.Value) },
                };
                var hp = Profile.CreateNew(8301);
                hp.Stash.Add(holder);
                var hhero = hp.Hero("Hero_Vesper");
                hhero.Equipped[(int)holderSlot] = holder.Uid;
                for (int i = 0; i < pieces.Count; i++)
                {
                    var slot = Content.GetBase(pieces[i].BaseId).Slot;
                    var relic = Piece(pieces[i], "u-p" + i);
                    hp.Stash.Add(relic);
                    hhero.Equipped[(int)slot] = relic.Uid;
                }
                var capped = Build.Compute(hp, "Hero_Vesper", 0);
                Assert.Equal(cap, capped.Get(set.TwoPiece.Stat)); // (cap-値) + 組の値 は上限で止まる
            }
        }
    }
}

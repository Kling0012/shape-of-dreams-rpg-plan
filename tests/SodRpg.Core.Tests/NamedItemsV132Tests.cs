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
    /// <summary>
    /// v1.32 stage 2b の回帰：銘品が1つも登録されていないとき（=登録簿を空に差し替えたとき）、
    /// 抽選は stage 2a の時点と完全に同じ遺物を同じ乱数列で出す。
    /// 古い RollRelic は凍結コピー（ここは変更しないこと）。
    /// 本体のデータ（360銘品・30組）が登録されていても、コモンと伝説の経路は変わらない。
    /// </summary>
    public class NamedItemsZeroDefsV132Tests : IDisposable
    {
        public NamedItemsZeroDefsV132Tests()
        {
            NamedItems.RegisterForTests(null, null); // 空の登録簿で試す
        }

        public void Dispose() => NamedItems.RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets);

        private static readonly Rarity[] Rarities = { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };

        private static T OldPickWeighted<T>(Rng rng, List<T> items, Func<T, Line> lineOf, Line? focus)
        {
            if (focus == null) return items[rng.Range(0, items.Count - 1)];
            int total = 0;
            foreach (var i in items) total += lineOf(i) == focus.Value ? 2 : 1;
            int x = rng.Range(0, total - 1);
            foreach (var i in items)
            {
                int w = lineOf(i) == focus.Value ? 2 : 1;
                if (x < w) return i;
                x -= w;
            }
            return items[items.Count - 1];
        }

        private static bool OldIsMissingSetPiece(UniqueDef candidate, IReadOnlyList<Relic> ownedRelics, IReadOnlyList<Relic> unsecuredRelics)
        {
            if (candidate.SetId == null) return false;
            bool started = false;
            for (int source = 0; source < 2; source++)
            {
                var relics = source == 0 ? ownedRelics : unsecuredRelics;
                if (relics == null) continue;
                for (int i = 0; i < relics.Count; i++)
                {
                    string id = relics[i].UniqueId;
                    if (id == candidate.Id) return false;
                    if (!started && Content.TryGetUnique(id, out var unique) && unique.SetId == candidate.SetId)
                        started = true;
                }
            }
            return started;
        }

        private static Relic OldRollRelic(Rng rng, Rarity rarity, int itemLevel, Slot? slot = null, Line? focus = null,
            IReadOnlyList<Relic> ownedRelics = null, IReadOnlyList<Relic> unsecuredRelics = null)
        {
            if (rarity == Rarity.Legendary)
            {
                var candidates = new List<(UniqueDef Unique, int Weight)>();
                int total = 0;
                foreach (var u in Content.Uniques)
                {
                    if (slot != null && Content.GetBase(u.BaseId).Slot != slot.Value) continue;
                    if (BossSets.IsExclusive(u)) continue; // #48 stage A: boss pieces left the generic pool in both paths
                    int weight = focus != null && Content.GetBase(u.BaseId).Line == focus.Value ? 2 : 1;
                    if (u.SetId != null) weight *= 4;
                    if (OldIsMissingSetPiece(u, ownedRelics, unsecuredRelics)) weight *= 60;
                    candidates.Add((u, weight));
                    total += weight;
                }
                if (candidates.Count > 0)
                {
                    int x = rng.Range(0, total - 1);
                    var chosen = candidates[candidates.Count - 1].Unique;
                    foreach (var candidate in candidates)
                    {
                        if (x < candidate.Weight) { chosen = candidate.Unique; break; }
                        x -= candidate.Weight;
                    }
                    return Loot.RollUnique(rng, chosen, itemLevel);
                }
                rarity = Rarity.Epic;
            }
            var bases = new List<BaseDef>();
            foreach (var b in Content.Bases)
                if (slot == null || b.Slot == slot.Value) bases.Add(b);
            var baseDef = OldPickWeighted(rng, bases, b => b.Line, focus);
            return Loot.RollBaseRelic(rng, baseDef, rarity, itemLevel);
        }

        private static string Describe(Relic r) =>
            r.Uid + "|" + r.BaseId + "|" + r.NamedId + "|" + r.Rarity + "|" + r.ItemLevel
            + "|" + string.Join(",", r.Affixes.Select(a => a.Stat + "=" + a.Value))
            + "|" + string.Join(",", r.Powers.Select(p => p.Power + "=" + p.Value));

        [Fact]
        public void Zero_registered_named_defs_rolls_exactly_like_the_frozen_copy()
        {
            Assert.Empty(NamedItems.All);
            Assert.Empty(NamedItems.MiniSets);
            Assert.Empty(CodexQuery.Entries(CodexCategory.Named));

            // セット収集補助が働くように、伝説のセット品と通常品を混ぜた所持リストも渡す。
            var owned = new List<Relic> { Loot.RollUnique(new Rng(7001), FirstUniqueWithSet(), 10) };
            var unsecured = new List<Relic> { Loot.RollRelic(new Rng(7002), Rarity.Epic, 10) };
            var codex = new HashSet<string> { owned[0].UniqueId, unsecured[0].BaseId };

            for (int i = 0; i < 4000; i++)
            {
                var rarity = Rarities[i % Rarities.Length];
                Slot? slot = i % 7 == 0 ? (Slot?)null : (Slot)(i % 6);
                Line? focus = i % 4 == 0 ? (Line?)null : (Line)(i % 3);
                int level = 1 + (i * 13) % Content.MaxItemLevel;
                bool withLists = i % 5 == 0;
                var oldRng = new Rng((ulong)(100000 + i));
                var newRng = new Rng((ulong)(100000 + i));
                var oldRelic = OldRollRelic(oldRng, rarity, level, slot, focus,
                    withLists ? owned : null, withLists ? unsecured : null);
                var newRelic = Loot.RollRelic(newRng, rarity, level, slot, focus,
                    withLists ? owned : null, withLists ? unsecured : null, withLists ? codex : null);
                Assert.Equal(Describe(oldRelic), Describe(newRelic));
                Assert.Equal(oldRng.State, newRng.State); // 乱数の消費も同じ
            }
        }

        [Fact]
        public void Common_and_legendary_rolls_are_unchanged_with_the_production_data_registered()
        {
            NamedItems.RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets); // 本体のデータ（360・30）
            Assert.Equal(360, NamedItems.All.Count);

            // セット収集補助が働くように、伝説のセット品と通常品を混ぜた所持リストも渡す。
            var owned = new List<Relic> { Loot.RollUnique(new Rng(7101), FirstUniqueWithSet(), 10) };
            var unsecured = new List<Relic> { Loot.RollRelic(new Rng(7102), Rarity.Epic, 10) };
            var codex = new HashSet<string> { owned[0].UniqueId, unsecured[0].BaseId };
            codex.Add(NamedItems.CodexId(NamedItemsData.Named[0].Id)); // 銘品の図鑑が混ざっていても影響しない

            for (int i = 0; i < 2000; i++)
            {
                var rarity = i % 2 == 0 ? Rarity.Common : Rarity.Legendary; // コモン=土台だけ、伝説=今のまま
                Slot? slot = i % 7 == 0 ? (Slot?)null : (Slot)(i % 6);
                Line? focus = i % 4 == 0 ? (Line?)null : (Line)(i % 3);
                int level = 1 + (i * 13) % Content.MaxItemLevel;
                bool withLists = i % 5 == 0;
                var oldRng = new Rng((ulong)(300000 + i));
                var newRng = new Rng((ulong)(300000 + i));
                var oldRelic = OldRollRelic(oldRng, rarity, level, slot, focus,
                    withLists ? owned : null, withLists ? unsecured : null);
                var newRelic = Loot.RollRelic(newRng, rarity, level, slot, focus,
                    withLists ? owned : null, withLists ? unsecured : null, withLists ? codex : null);
                Assert.Equal(Describe(oldRelic), Describe(newRelic));
                Assert.Equal(oldRng.State, newRng.State);
                Assert.Null(newRelic.NamedId); // コモンと伝説に銘品は出ない
            }
        }

        [Fact]
        public void Passing_a_codex_set_changes_nothing_without_named_defs()
        {
            var codex = new HashSet<string> { "n:named.test.w1" };
            for (int i = 0; i < 500; i++)
            {
                var a = new Rng((ulong)(200000 + i));
                var b = new Rng((ulong)(200000 + i));
                var rarity = Rarities[i % Rarities.Length];
                var first = Loot.RollRelic(a, rarity, 10, null, null, null, null, null);
                var second = Loot.RollRelic(b, rarity, 10, null, null, null, null, codex);
                Assert.Equal(Describe(first), Describe(second));
            }
        }

        internal static UniqueDef FirstUniqueWithSet() =>
            Content.Uniques.First(u => u.SetId != null && Content.TryGetBase(u.BaseId, out _));
    }

    /// <summary>v1.32 stage 2b：銘品・組のエンジン（設計 3.2/3.4/3.5/4、試験3〜8）。</summary>
    public class NamedItemsV132Tests : IDisposable
    {
        // 土台は実在のものを使う（weapon.chain_sword=攻撃/連なり、weapon.calming_staff=共鳴、armor.guardian_plate=守り、
        // armor.flowing_cloak=共鳴、head.iron_helm=守り）。固有効果の値は各枠の PowerRange の範囲内の固定値。
        private static readonly NamedDef[] Sample =
        {
            new NamedDef("named.test.w1", "weapon.chain_sword", Rarity.Uncommon,
                new Txt("霜誓の剣", "Frostoath Blade"), new Txt("北の誓いを刻んだ刃。", "A blade carved with a northern oath."),
                "miniset.test.frost", Power.Momentum, 3),
            new NamedDef("named.test.a1", "armor.guardian_plate", Rarity.Uncommon,
                new Txt("霜誓の胸当て", "Frostoath Plate"), new Txt("誓いを守る鋼。", "Steel that keeps the oath."),
                "miniset.test.frost", Power.Barrier, 7),
            new NamedDef("named.test.h1", "head.iron_helm", Rarity.Rare,
                new Txt("霜誓の兜", "Frostoath Helm"), new Txt("頭を守る誓いの証。", "Proof of the oath that guards the head."),
                "miniset.test.frost", Power.CoStar, 14),
            new NamedDef("named.test.w2", "weapon.calming_staff", Rarity.Rare,
                new Txt("静水の杖", "Stillwater Staff"), new Txt("淀まぬ水の記憶。", "A memory of still water."),
                "miniset.test.duo", Power.Lifesteal, 6),
            new NamedDef("named.test.a2", "armor.flowing_cloak", Rarity.Rare,
                new Txt("静水の外套", "Stillwater Cloak"), new Txt("流れのようにさやぐ。", "It rustles like a stream."),
                "miniset.test.duo", Power.WatchfulHand, 17),
            new NamedDef("named.test.w3", "weapon.chain_sword", Rarity.Epic,
                new Txt("烈焔の大剣", "Blazing Greatblade"), new Txt("燃えさかる誓いの名残。", "The embers of a blazing oath."),
                "miniset.test.epic", Power.Blaze, 58, Power.Ember, 30),
            new NamedDef("named.test.a3", "armor.guardian_plate", Rarity.Epic,
                new Txt("烈焔の胸当て", "Blazing Plate"), new Txt("炎をまとった鋼。", "Steel wrapped in flame."),
                "miniset.test.epic", Power.StarShield, 12),
        };

        private static readonly MiniSetDef[] SampleSets =
        {
            new MiniSetDef("miniset.test.frost", new Txt("霜誓の記章", "Frostoath Insignia"),
                new[] { "named.test.w1", "named.test.a1", "named.test.h1" },
                new StatLine(Stat.ColdAmp, 8), new PowerLine(Power.Frost, 30)),
            new MiniSetDef("miniset.test.duo", new Txt("静水の約束", "Stillwater Pact"),
                new[] { "named.test.w2", "named.test.a2" },
                new StatLine(Stat.Armor, 12)),
            new MiniSetDef("miniset.test.epic", new Txt("烈焔の二連", "Blazing Duo"),
                new[] { "named.test.w3", "named.test.a3" },
                new StatLine(Stat.AttackPct, 25)),
        };

        public NamedItemsV132Tests()
        {
            Register(Sample, SampleSets);
        }

        public void Dispose() => NamedItems.RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets);

        private static void Register(NamedDef[] named, MiniSetDef[] sets) => NamedItems.RegisterForTests(named, sets);

        private static NamedDef Def(string id) => NamedItems.TryGetNamed(id, out var d) ? d : null;


        private static Relic NormalRelic(Rarity rarity, Slot slot, string uid = null, int seed = 11)
        {
            var baseDef = Content.Bases.First(b => b.Slot == slot);
            var r = Loot.RollBaseRelic(new Rng((ulong)seed), baseDef, rarity, 10);
            if (uid != null) r.Uid = uid;
            return r;
        }


        private static Relic NamedRelic(string id, string uid = null)
        {
            var def = Def(id);
            var relic = new Relic
            {
                Uid = uid ?? ("u-" + id), BaseId = def.BaseId, NamedId = def.Id, Rarity = def.Rarity, ItemLevel = 10,
                Enhance = 3,
                Affixes = { new StatLine(Stat.Haste, 4) },
            };
            foreach (var power in def.Powers) relic.Powers.Add(power);
            return relic;
        }

        private static Profile ProfileAtEvent(DreamEvent e, ulong seed = 5)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "run", heroKey: "Hero_Vesper");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = e;
            return p;
        }

        // ───────── 試験3：銘品の抽選（設計 3.4）─────────

        [Fact]
        public void Named_weights_follow_the_spec_constants_and_multipliers()
        {
            var w1 = Def("named.test.w1");
            var w2 = Def("named.test.w2");
            var w3 = Def("named.test.w3");
            var w1Base = Content.GetBase(w1.BaseId);
            var w2Base = Content.GetBase(w2.BaseId);
            Assert.Equal(Line.Offense, w1Base.Line);
            Assert.Equal(Line.Resonance, w2Base.Line);

            // レア度ごとの定数（土台 = 1 に対して アンコモン2・レア3・エピック6）。
            var inCodex = new HashSet<string>(Sample.Select(n => NamedItems.CodexId(n.Id)));
            Assert.Equal(2, Loot.NamedWeight(w1, w1Base, null, inCodex, null, null));
            Assert.Equal(3, Loot.NamedWeight(w2, w2Base, null, inCodex, null, null));
            Assert.Equal(6, Loot.NamedWeight(w3, Content.GetBase(w3.BaseId), null, inCodex, null, null));

            // 図鑑を受け取らない呼び出しでは3倍を掛けない（未所持か分からないため）。
            Assert.Equal(2, Loot.NamedWeight(w1, w1Base, null, null, null, null));
            var partial = new HashSet<string>(inCodex.Where(id => id != NamedItems.CodexId(w1.Id)));
            Assert.Equal(2 * NamedItems.NotInCodexMultiplier, Loot.NamedWeight(w1, w1Base, null, partial, null, null));

            // 狙い系統は土台と同じく2倍（系統は銘品の土台の系統）。
            Assert.Equal(3 * 2, Loot.NamedWeight(w2, w2Base, Line.Resonance, inCodex, null, null));
            Assert.Equal(3, Loot.NamedWeight(w2, w2Base, Line.Offense, inCodex, null, null));
            Assert.Equal(3 * 2, Loot.NamedWeight(w2, w2Base, Line.Resonance, null, null, null)); // 図鑑を受け取らない呼び出しは3倍なし

            // 始めた組の未所持部位は4倍。同じ部位を既に持っていれば掛からない。組を始めていなければ掛からない。
            var otherPiece = new List<Relic> { NamedRelic("named.test.a1") };
            Assert.Equal(2 * NamedItems.MissingMiniSetPieceMultiplier, Loot.NamedWeight(w1, w1Base, null, inCodex, otherPiece, null));
            var satchelStart = new List<Relic> { NamedRelic("named.test.h1") };
            Assert.Equal(2 * NamedItems.MissingMiniSetPieceMultiplier, Loot.NamedWeight(w1, w1Base, null, inCodex, null, satchelStart));
            var ownedPiece = new List<Relic> { NamedRelic("named.test.w1") };
            Assert.Equal(2, Loot.NamedWeight(w1, w1Base, null, inCodex, ownedPiece, null));
            var unrelated = new List<Relic> { NamedRelic("named.test.w2") };
            Assert.Equal(2, Loot.NamedWeight(w1, w1Base, null, inCodex, unrelated, null));
            Assert.Equal(3, Loot.NamedWeight(w2, w2Base, null, inCodex, unrelated, null)); // 自分を持っていれば組の4倍は付かない

            // 全部掛かった場合（2 × 2 × 3 × 4）。
            var empty = new HashSet<string>();
            Assert.Equal(2 * 2 * NamedItems.NotInCodexMultiplier * NamedItems.MissingMiniSetPieceMultiplier,
                Loot.NamedWeight(w1, w1Base, Line.Offense, empty, otherPiece, null));
        }

        [Fact]
        public void Named_items_roll_only_at_their_rarity_and_slot_and_keep_their_definition()
        {
            var weaponBases = Content.Bases.Count(b => b.Slot == Slot.Weapon);
            Assert.True(weaponBases > 0);
            int namedW2 = 0;
            for (int i = 0; i < 4000; i++)
            {
                var r = Loot.RollRelic(new Rng((ulong)(300000 + i)), Rarity.Rare, 10, Slot.Weapon);
                if (r.NamedId == null) continue;
                Assert.Equal("named.test.w2", r.NamedId); // 武器・レアの銘品は w2 だけ
                Assert.Equal("weapon.calming_staff", r.BaseId);
                namedW2++;
            }
            Assert.True(namedW2 > 0, "銘品が1つも出ないのは重みが効いていない");

            for (int i = 0; i < 1500; i++)
            {
                Assert.Null(Loot.RollRelic(new Rng((ulong)(310000 + i)), Rarity.Common, 10).NamedId); // コモンは土台だけ
                Assert.Null(Loot.RollRelic(new Rng((ulong)(320000 + i)), Rarity.Legendary, 10).NamedId); // 伝説は今のまま
                var head = Loot.RollRelic(new Rng((ulong)(330000 + i)), Rarity.Rare, 10, Slot.Head);
                if (head.NamedId != null) Assert.Equal("named.test.h1", head.NamedId);
            }
        }

        [Fact]
        public void Named_share_of_low_rarity_rolls_matches_the_spec_weight_ratio()
        {
            int weaponBases = Content.Bases.Count(b => b.Slot == Slot.Weapon);
            int resonanceWeight = Content.Bases.Where(b => b.Slot == Slot.Weapon).Sum(b => b.Line == Line.Resonance ? 2 : 1);

            int hits = 0;
            const int n = 30000;
            for (int i = 0; i < n; i++)
            {
                var r = Loot.RollRelic(new Rng((ulong)(400000 + i)), Rarity.Rare, 10, Slot.Weapon);
                if (r.NamedId != null) hits++;
            }
            double expected = (double)NamedItems.RareWeight / (weaponBases + NamedItems.RareWeight);
            Assert.InRange((double)hits / n, expected - 0.012, expected + 0.012);

            // 図鑑に載っている銘品は3倍が外れるので割合が下がる。
            var codex = new HashSet<string>(Sample.Select(x => NamedItems.CodexId(x.Id)));
            hits = 0;
            for (int i = 0; i < n; i++)
            {
                var r = Loot.RollRelic(new Rng((ulong)(410000 + i)), Rarity.Rare, 10, Slot.Weapon, null, null, null, codex);
                if (r.NamedId != null) hits++;
            }
            double expectedInCodex = (double)NamedItems.RareWeight / (weaponBases + NamedItems.RareWeight);
            Assert.InRange((double)hits / n, expectedInCodex - 0.012, expectedInCodex + 0.012);

            // 狙い系統（レア・武器・共鳴）は土台も銘品も2倍。
            hits = 0;
            for (int i = 0; i < n; i++)
            {
                var r = Loot.RollRelic(new Rng((ulong)(420000 + i)), Rarity.Rare, 10, Slot.Weapon, Line.Resonance);
                if (r.NamedId != null) hits++;
            }
            double expectedFocus = (double)(NamedItems.RareWeight * 2) / (resonanceWeight + NamedItems.RareWeight * 2);
            Assert.InRange((double)hits / n, expectedFocus - 0.012, expectedFocus + 0.012);
        }

        // ───────── 試験4：銘品の生成（設計 3.2・4）─────────

        [Fact]
        public void Rolled_named_relics_carry_fixed_powers_and_rarity_affix_count()
        {
            foreach (var def in Sample)
            {
                var baseDef = Content.GetBase(def.BaseId);
                var combos = new HashSet<string>();
                for (int seed = 0; seed < 60; seed++)
                {
                    var r = Loot.RollNamed(new Rng((ulong)(500000 + seed)), def, 12);
                    Assert.Equal(def.Id, r.NamedId);
                    Assert.Equal(def.BaseId, r.BaseId);
                    Assert.Equal(def.Rarity, r.Rarity);
                    Assert.Equal(12, r.ItemLevel);
                    Assert.Null(r.UniqueId);
                    // 固有効果は定義どおりの固定値。
                    Assert.Equal(def.Powers.Count, r.Powers.Count);
                    for (int i = 0; i < def.Powers.Count; i++)
                    {
                        Assert.Equal(def.Powers[i].Power, r.Powers[i].Power);
                        Assert.Equal(def.Powers[i].Value, r.Powers[i].Value);
                        // 値はその枠の PowerRange の範囲内。
                        var range = Content.PowerPool(baseDef.Slot).First(x => x.Power == def.Powers[i].Power);
                        Assert.InRange(def.Powers[i].Value, range.Min, range.Max);
                    }
                    // 特性はレア度の個数・通常どおりの抽選（implicit と重複なし）。
                    Assert.Equal(Content.AffixCount(def.Rarity), r.Affixes.Count);
                    Assert.Equal(baseDef.ImplicitStat, r.Base.ImplicitStat);
                    Assert.Equal(r.Affixes.Count, r.Affixes.Select(a => a.Stat).Distinct().Count());
                    Assert.DoesNotContain(r.Affixes, a => a.Stat == baseDef.ImplicitStat);
                    combos.Add(string.Join(",", r.Affixes.Select(a => a.Stat + "=" + a.Value)));
                }
                Assert.True(combos.Count >= 3, def.Id + " の特性に個体差がない");
            }
        }

        // ───────── 試験5：保存と互換（設計 4）─────────

        private static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static JsonObject FindRelic(JsonObject body, string uid)
        {
            foreach (var key in new[] { "stash", "lostAndFound", "satchel", "deferredWaypointRelics" })
            {
                if (!body.TryGet(key, out object listObj) || !(listObj is List<object> list)) continue;
                foreach (var item in list)
                    if (item is JsonObject j && j.TryGet("uid", out object u) && u as string == uid)
                        return j;
            }
            if (body.TryGet("pendingSalvage", out object pending) && pending is List<object> pendings)
                foreach (var item in pendings)
                    if (item is JsonObject j && j.TryGet("uid", out object u) && u as string == uid)
                        return j;
            return null;
        }

        /// <summary>保存テキストの遺物1件を書き換え（キーの削除・変更）て、チェックサムを作り直す。</summary>
        private static string EditRelic(string save, string uid, Func<JsonObject, JsonObject> edit)
        {
            var root = (JsonObject)Json.Parse(save);
            Assert.True(root.TryGet("body", out object bodyObj) && bodyObj is JsonObject, "body がない");
            var body = (JsonObject)bodyObj;
            var relic = FindRelic(body, uid);
            Assert.NotNull(relic);
            JsonObject edited = edit(relic);
            void Replace(JsonObject parent, string key)
            {
                if (!parent.TryGet(key, out object listObj) || !(listObj is List<object> list)) return;
                for (int i = 0; i < list.Count; i++)
                    if (ReferenceEquals(list[i], relic)) list[i] = edited;
            }
            foreach (var key in new[] { "stash", "lostAndFound", "satchel", "deferredWaypointRelics", "pendingSalvage" }) Replace(body, key);
            string checksum = "sha256:" + Sha256Hex(Json.Write(body));
            return Json.Write(new JsonObject()
                .Add("format", ProfileCodec.Format)
                .Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", checksum)
                .Add("body", body));
        }

        private static JsonObject WithoutKey(JsonObject o, string key)
        {
            var n = new JsonObject();
            foreach (var kv in o.Properties)
                if (kv.Key != key) n.Add(kv.Key, kv.Value);
            return n;
        }

        private static JsonObject WithKey(JsonObject o, string key, object value)
        {
            var n = new JsonObject();
            bool set = false;
            foreach (var kv in o.Properties)
            {
                if (kv.Key == key) { n.Add(key, value); set = true; }
                else n.Add(kv.Key, kv.Value);
            }
            if (!set) n.Add(key, value);
            return n;
        }

        [Fact]
        public void Named_id_round_trips_and_old_saves_read_as_normal_relics()
        {
            var p = Profile.CreateNew(6001);
            Rules.BeginRun(p, "named-save", heroKey: "Hero_Vesper");
            var enhanced = NamedRelic("named.test.w3"); // エピック（限界突破ができるのはレア以上なので）
            enhanced.Enhance = Content.MaxEnhanceFor(Rarity.Epic, 1); // 限界突破1回ぶんの上限（読み込みで切られるので最初から上限にする）
            enhanced.LimitBreaks = 1;
            enhanced.Retunes = 2;
            enhanced.AffixRerolls = 3;
            enhanced.AwakenLevel = 2;
            enhanced.AwakenPoints = Content.AwakenThresholdFor(2);
            enhanced.EnhanceMilestones = 5;
            p.Stash.Add(enhanced);
            p.Hero("Hero_Vesper").Equipped[(int)Slot.Weapon] = enhanced.Uid;
            p.Run.Satchel.Add(NamedRelic("named.test.w1", "u-satchel"));
            p.LostAndFound.Add(NormalRelic(Rarity.Rare, Slot.Head, "u-lost"));

            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var stashNamed = loaded.Stash.Single(r => r.Uid == enhanced.Uid);
            Assert.Equal("named.test.w3", stashNamed.NamedId);
            Assert.Equal(Content.MaxEnhanceFor(Rarity.Epic, 1), stashNamed.Enhance);
            Assert.Equal(1, stashNamed.LimitBreaks);
            Assert.Equal(2, stashNamed.Retunes);
            Assert.Equal(3, stashNamed.AffixRerolls);
            Assert.Equal(2, stashNamed.AwakenLevel);
            Assert.Equal(5, stashNamed.EnhanceMilestones);
            Assert.Equal("named.test.w1", loaded.Run.Satchel.Single(r => r.Uid == "u-satchel").NamedId);
            Assert.Equal(enhanced.Uid, loaded.Hero("Hero_Vesper").Equipped[(int)Slot.Weapon]);
            Assert.Null(loaded.LostAndFound.Single(r => r.Uid == "u-lost").NamedId);

            string save = ProfileCodec.Write(p);

            // 古い保存（"named" が無い）→ null として読む。
            var oldSave = EditRelic(save, enhanced.Uid, j => WithoutKey(j, "named"));
            var oldLoaded = ProfileCodec.Read(oldSave, new List<string>());
            var oldRelic = oldLoaded.Stash.Single(r => r.Uid == enhanced.Uid);
            Assert.Null(oldRelic.NamedId);
            Assert.Equal(enhanced.BaseId, oldRelic.BaseId);
            Assert.Equal(Content.MaxEnhanceFor(Rarity.Epic, 1), oldRelic.Enhance);

            // 未知の銘品ID → 例外にせず通常の遺物として読む（落とさない）。
            var unknownSave = EditRelic(save, enhanced.Uid, j => WithKey(j, "named", "named.test.gone"));
            var unknownLoaded = ProfileCodec.Read(unknownSave, new List<string>());
            var unknownRelic = unknownLoaded.Stash.Single(r => r.Uid == enhanced.Uid);
            Assert.Null(unknownRelic.NamedId);
            Assert.Equal(enhanced.BaseId, unknownRelic.BaseId);

            // 未知の土台ID → 今のとおり例外（項目として落とす）。
            var badBase = EditRelic(save, enhanced.Uid, j => WithKey(j, "base", "weapon.nope"));
            var notes = new List<string>();
            var badLoaded = ProfileCodec.Read(badBase, notes);
            Assert.Empty(badLoaded.Stash);
            Assert.Contains(notes, t => t.Contains("未知の基礎ID"));
        }

        // ───────── 名前・一言・組の進み（ツールチップ・図鑑の中身）─────────

        [Fact]
        public void Plain_name_uses_the_named_name_and_skips_epithets()
        {
            var w3 = NamedRelic("named.test.w3");
            bool old = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                Assert.Equal("烈焔の大剣", w3.PlainName); // エピックでも銘は付かない
                Loc.Japanese = false;
                Assert.Equal("Blazing Greatblade", w3.PlainName);
            }
            finally { Loc.Japanese = old; }

            var normal = NormalRelic(Rarity.Rare, Slot.Weapon);
            Assert.Equal(normal.Base.Name.ToString(), normal.PlainName); // 通常品は今のまま（エピックの銘も今のまま）
            var w1 = NamedRelic("named.test.w1");
            Assert.Equal("霜誓の剣", w1.PlainName);
        }

        [Fact]
        public void Lore_and_mini_set_lines_are_natural_japanese_and_english()
        {
            var w1 = NamedRelic("named.test.w1");
            var normal = NormalRelic(Rarity.Rare, Slot.Weapon);
            Assert.Null(NamedItems.LoreLine(normal));
            Assert.Null(NamedItems.MiniSetLine(normal, 2));

            bool old = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                Assert.Equal("北の誓いを刻んだ刃。", NamedItems.LoreLine(w1));
                Assert.Equal("《霜誓の記章》 装着中 2/3", NamedItems.MiniSetLine(w1, 2));
                Assert.Equal("《霜誓の記章》 装着中 3/3", NamedItems.MiniSetLine(w1, 99));
                Assert.Equal("《霜誓の記章》 発見 1/3", NamedItems.MiniSetProgress("miniset.test.frost", new HashSet<string> { "n:named.test.h1" }));
                Assert.Equal("《静水の約束》 発見 0/2", NamedItems.MiniSetProgress("miniset.test.duo", new HashSet<string>()));
                Assert.True(NamedItems.TryGetMiniSet("miniset.test.frost", out var frost));
                Assert.True(NamedItems.TryGetMiniSet("miniset.test.duo", out var duo));
                Assert.Equal("2つ装着：" + Content.FormatStat(Stat.ColdAmp, 8) + "\n3つ装着：\n" + Content.FormatPowerBullets(Power.Frost, 30, "　"), frost.Describe());
                Assert.Equal("2つ装着：" + Content.FormatStat(Stat.Armor, 12), duo.Describe());

                Loc.Japanese = false;
                Assert.Equal("A blade carved with a northern oath.", NamedItems.LoreLine(w1));
                Assert.Equal("\"Frostoath Insignia\" 2/3 worn", NamedItems.MiniSetLine(w1, 2));
                Assert.Equal("\"Frostoath Insignia\" 1/3 found", NamedItems.MiniSetProgress("miniset.test.frost", new HashSet<string> { "n:named.test.h1" }));
                Assert.Equal("\"Stillwater Pact\" 0/2 found", NamedItems.MiniSetProgress("miniset.test.duo", new HashSet<string>()));
                Assert.Equal("2 pieces: " + Content.FormatStat(Stat.ColdAmp, 8) + "\n3 pieces:\n" + Content.FormatPowerBullets(Power.Frost, 30, "　"), frost.Describe());
                Assert.Equal("2 pieces: " + Content.FormatStat(Stat.Armor, 12), duo.Describe());
            }
            finally { Loc.Japanese = old; }
        }

        // ───────── 試験6：出来事（設計 3.4）─────────

        [Fact]
        public void Power_swapping_events_treat_named_relics_like_uniques()
        {
            // 記憶の井戸：装着中の銘品だけでは対象なし。
            var p = ProfileAtEvent(DreamEvent.MemoryWell, 6002);
            var named = NamedRelic("named.test.w1");
            p.Stash.Add(named);
            p.Hero("Hero_Vesper").Equipped[(int)Slot.Weapon] = named.Uid;
            p.AddMaterial(Materials.Tuning, 1);
            Assert.Null(DreamEvents.TradeTarget(p, DreamEvent.MemoryWell));
            Assert.False(DreamEvents.CanUse(p, DreamEvent.MemoryWell, out _));

            // 通常のレア（固有効果1つ）なら対象になる。
            var normal = NormalRelic(Rarity.Rare, Slot.Weapon, "u-normal-rare", 21);
            p.Stash.Add(normal);
            p.Hero("Hero_Vesper").Equipped[(int)Slot.Weapon] = normal.Uid;
            Assert.Equal(normal.Uid, DreamEvents.TradeTarget(p, DreamEvent.MemoryWell)?.Uid);

            // るつぼ：鞄に銘品エピック（固有効果2つ）だけでは対象なし。通常のエピックなら対象になる。
            var q = ProfileAtEvent(DreamEvent.PowerCrucible, 6003);
            var namedEpic = NamedRelic("named.test.w3");
            Assert.Equal(2, namedEpic.Powers.Count); // 対象になりうる個数は持っている
            q.Run.Satchel.Add(namedEpic);
            Assert.Null(DreamEvents.TradeTarget(q, DreamEvent.PowerCrucible));
            var normalEpic = NormalRelic(Rarity.Epic, Slot.Weapon, "u-normal-epic", 31);
            q.Run.Satchel.Add(normalEpic);
            Assert.Equal(normalEpic.Uid, DreamEvents.TradeTarget(q, DreamEvent.PowerCrucible)?.Uid);
        }

        [Fact]
        public void Twin_mirror_and_wager_reroll_named_relics_as_normal_ones()
        {
            // 双子の鏡：同じ土台・同じレア度で、銘品ではなく通常の遺物を作り直す。
            var p = ProfileAtEvent(DreamEvent.TwinMirror, 6004);
            var named = NamedRelic("named.test.w3");
            p.Run.Satchel.Add(named);
            p.Run.SatchelShards = 60;
            Rules.UseEvent(p, DreamEvent.TwinMirror);
            Assert.Equal(0, p.Run.SatchelShards);
            var copy = p.Run.Satchel.Single(r => r.Uid != named.Uid);
            Assert.Equal(named.BaseId, copy.BaseId);
            Assert.Equal(Rarity.Epic, copy.Rarity);
            Assert.Null(copy.NamedId);
            Assert.Null(copy.UniqueId);

            // 賭場：銘品を賭けて勝てば、1つ上のレア度の「通常の遺物」に交換される。
            for (ulong seed = 6005; seed < 6060; seed++)
            {
                var q = ProfileAtEvent(DreamEvent.RelicWager, seed);
                var wager = NamedRelic("named.test.w1");
                q.Run.Satchel.Add(wager);
                Rules.UseEvent(p: q, e: DreamEvent.RelicWager);
                var replacement = q.Run.Satchel.SingleOrDefault(r => r.Uid != wager.Uid);
                if (replacement == null) continue; // この乱数では負けた。勝つ乱数まで確定で進む。
                Assert.Equal(wager.BaseId, replacement.BaseId);
                Assert.Equal(Rarity.Rare, replacement.Rarity);
                Assert.Null(replacement.NamedId);
                Assert.Null(replacement.UniqueId);
                return;
            }
            Assert.True(false, "どの乱数でも賭けに勝たなかった（確率1/2が40回以上続くのは異常）");
        }

        [Fact]
        public void Named_relics_salvage_and_lock_like_normal_relics()
        {
            var p = Profile.CreateNew(6061);
            var named = NamedRelic("named.test.w2");
            p.Stash.Add(named);
            named.Locked = true;
            Assert.Throws<InvalidOperationException>(() => Rules.Salvage(p, named.Uid)); // 鍵は通常どおり効く
            named.Locked = false;
            int before = p.Material(Materials.Shard);
            Rules.Salvage(p, named.Uid);
            Assert.Empty(p.Stash.Where(r => r.Uid == named.Uid));
            Assert.Equal(before + Rules.SalvageValue(named), p.Material(Materials.Shard));
        }

        // ───────── 試験7：組の集計（設計 3.2・4）─────────

        private static Build BuildWith(Profile p, params Relic[] relics)
        {
            for (int i = 0; i < relics.Length; i++) p.Stash.Add(relics[i]);
            var hero = p.Hero("Hero_Vesper");
            for (int i = 0; i < relics.Length; i++) hero.Equipped[(int)relics[i].Slot] = relics[i].Uid;
            return Build.Compute(p, "Hero_Vesper", 0);
        }

        [Fact]
        public void Mini_sets_aggregate_beside_sets_without_double_counting()
        {
            var p = Profile.CreateNew(6071);
            var one = BuildWith(p, NamedRelic("named.test.w1"));
            Assert.Equal(1, one.MiniSets["miniset.test.frost"]);
            Assert.Equal(0, one.Get(Stat.ColdAmp)); // 1部位ではボーナスなし

            var two = BuildWith(Profile.CreateNew(6072), NamedRelic("named.test.w1"), NamedRelic("named.test.a1"));
            Assert.Equal(2, two.MiniSets["miniset.test.frost"]);
            Assert.Equal(8, two.Get(Stat.ColdAmp)); // 2点＝能力値1行
            Assert.Equal(0, two.Get(Power.Frost)); // 3点の固有効果はまだ
            Assert.Empty(two.Sets); // SetDef の集計とは別

            var three = BuildWith(Profile.CreateNew(6073), NamedRelic("named.test.w1"), NamedRelic("named.test.a1"), NamedRelic("named.test.h1"));
            Assert.Equal(3, three.MiniSets["miniset.test.frost"]);
            Assert.Equal(8, three.Get(Stat.ColdAmp));
            Assert.Equal(30, three.Get(Power.Frost)); // 3点＝弱い固有効果1つ

            // 同じ銘品を2箇所に装着しても1部位として数える（重複して数えない）。
            var twin = Profile.CreateNew(6074);
            var original = NamedRelic("named.test.w1");
            var duplicate = NamedRelic("named.test.w1", "u-duplicate");
            var fakeOtherSlot = new Relic { Uid = duplicate.Uid, BaseId = "armor.guardian_plate", NamedId = "named.test.w1", Rarity = Rarity.Uncommon, ItemLevel = 10 };
            twin.Stash.Add(original);
            twin.Stash.Add(fakeOtherSlot);
            var heroTwin = twin.Hero("Hero_Vesper");
            heroTwin.Equipped[(int)Slot.Weapon] = original.Uid;
            heroTwin.Equipped[(int)Slot.Armor] = fakeOtherSlot.Uid;
            var twinBuild = Build.Compute(twin, "Hero_Vesper", 0);
            Assert.Equal(1, twinBuild.MiniSets["miniset.test.frost"]);
            Assert.Equal(0, twinBuild.Get(Stat.ColdAmp));

            // 伝説のセット部位は組の集計に入らない（逆も混ざらない）。
            var legend = Profile.CreateNew(6075);
            var unique = Loot.RollUnique(new Rng(6075), NamedItemsZeroDefsV132Tests.FirstUniqueWithSet(), 10);
            legend.Stash.Add(unique);
            legend.Hero("Hero_Vesper").Equipped[(int)unique.Slot] = unique.Uid;
            var legendBuild = Build.Compute(legend, "Hero_Vesper", 0);
            Assert.Empty(legendBuild.MiniSets);
            Assert.Single(legendBuild.Sets);

            // 2点だけの組には3点効果が無い。
            var duo = BuildWith(Profile.CreateNew(6076), NamedRelic("named.test.w2"), NamedRelic("named.test.a2"));
            Assert.Equal(2, duo.MiniSets["miniset.test.duo"]);
            Assert.Equal(12, duo.Get(Stat.Armor));
        }

        [Fact]
        public void Mini_set_stat_lines_share_the_existing_percent_caps()
        {
            int cap = Content.StatCap(Stat.AttackPct);
            Assert.True(cap > 0);

            // 攻撃力%を大きく持つ通常のエピック + 組の2点ボーナス（攻撃力%25）。
            var holder = new Relic
            {
                Uid = "u-holder", BaseId = "charm.hunters_seal", Rarity = Rarity.Epic, ItemLevel = 10,
                Affixes = { new StatLine(Stat.AttackPct, cap - 10) },
            };
            var p = Profile.CreateNew(6081);
            var control = BuildWith(p, holder);
            Assert.Equal(cap - 10, control.Get(Stat.AttackPct));

            var withSet = BuildWith(Profile.CreateNew(6082), holder, NamedRelic("named.test.w3"), NamedRelic("named.test.a3"));
            // (cap-10) + 25 は cap を超えられない（合計上限は変わらない）。
            Assert.Equal(cap, withSet.Get(Stat.AttackPct));
        }

        // ───────── 試験8：図鑑（設計 3.5・4）─────────

        [Fact]
        public void Codex_has_named_and_mini_set_categories_with_search_and_found_state()
        {
            var namedEntries = CodexQuery.Entries(CodexCategory.Named);
            Assert.Equal(Sample.Length, namedEntries.Count);
            Assert.All(namedEntries, e => Assert.StartsWith("n:", e.Id, StringComparison.Ordinal));
            // 固有品・土台のIDと衝突しない。
            Assert.True(Content.Bases.Select(b => b.Id).Concat(Content.Uniques.Select(u => u.Id)).All(id => !id.StartsWith("n:", StringComparison.Ordinal)));
            Assert.Equal(SampleSets.Length, CodexQuery.Entries(CodexCategory.MiniSets).Count);

            var codex = new HashSet<string> { NamedItems.CodexId("named.test.w1") };
            var state = new CodexState(codex, new HashSet<Power>());
            var w1Entry = namedEntries.Single(e => e.Id == NamedItems.CodexId("named.test.w1"));
            Assert.True(state.IsFound(w1Entry));
            Assert.False(state.IsFound(namedEntries.Single(e => e.Id == NamedItems.CodexId("named.test.w2"))));
            var frostEntry = CodexQuery.Entries(CodexCategory.MiniSets).Single(e => e.Id == "miniset.test.frost");
            Assert.True(state.IsFound(frostEntry)); // 部位を1つでも見つけていれば組が見つかった状態

            // 検索：見つけた銘品は名前（日英）・効果・一言で引っかかる。見つけていない銘品は隠す。
            var filter = new CodexFilter { Category = CodexCategory.Named, Text = "frostoath" };
            var result = CodexQuery.Filter(state, filter);
            Assert.Equal(Sample.Length, result.CategoryTotal[(int)CodexCategory.Named]);
            Assert.Single(result.Items);
            Assert.Equal(NamedItems.CodexId("named.test.w1"), result.Items[0].Id);
            var hidden = CodexQuery.Filter(state, new CodexFilter { Category = CodexCategory.Named, Text = "stillwater" });
            Assert.Empty(hidden.Items); // 未発見は検索に出ない

            // 図鑑の文字列は Relic.CodexId と同じ形。
            Assert.Equal(NamedItems.CodexId("named.test.w1"), NamedRelic("named.test.w1").CodexId);
            Assert.Equal("weapon.chain_sword", NormalRelic(Rarity.Rare, Slot.Weapon).CodexId);
            var unique = Loot.RollUnique(new Rng(6091), NamedItemsZeroDefsV132Tests.FirstUniqueWithSet(), 10);
            Assert.Equal(unique.UniqueId, unique.CodexId);
        }

        [Fact]
        public void Known_powers_include_named_powers_and_mini_set_three_piece_powers()
        {
            var p = Profile.CreateNew(6092);
            p.Codex.Add(NamedItems.CodexId("named.test.w1"));
            var known = CodexQuery.KnownPowers(p);
            Assert.Contains(Power.Momentum, known); // w1 の固定の固有効果

            p.Codex.Add(NamedItems.CodexId("named.test.h1"));
            known = CodexQuery.KnownPowers(p);
            Assert.Contains(Power.Frost, known); // 組の3点効果

            p.Codex.Clear();
            Assert.DoesNotContain(Power.Momentum, CodexQuery.KnownPowers(p));
        }

        [Fact]
        public void Registering_nothing_returns_the_codex_and_rolls_to_the_empty_state()
        {
            NamedItems.RegisterForTests(null, null);
            Assert.Empty(NamedItems.All);
            Assert.Empty(CodexQuery.Entries(CodexCategory.Named));
            Assert.Empty(CodexQuery.Entries(CodexCategory.MiniSets));
            for (int i = 0; i < 200; i++)
                Assert.Null(Loot.RollRelic(new Rng((ulong)(700000 + i)), Rarity.Epic, 10).NamedId);
        }
    }
}

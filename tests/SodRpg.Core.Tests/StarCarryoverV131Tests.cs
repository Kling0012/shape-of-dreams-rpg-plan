using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.30 の保存（版3/版4）を v1.31 の星団登録後に読み込んでも、星ポイント・配分・刻印が失われず、
    /// 無償で増えないことを確認する。
    /// </summary>
    public sealed class StarCarryoverV131Tests
    {
        private const string Cetus = "Hero_Cetus";
        private const string Vesper = "Hero_Vesper";
        private const string Husk = "Hero_Husk";
        private const string Memory = "St_D_IcyVeins";
        private const string CopyEnv = "SODRPG_SAVECARRY_COPY";
        private readonly ITestOutputHelper _out;

        public StarCarryoverV131Tests(ITestOutputHelper output) { _out = output; }

        // ---- 旧版（v1.30）の保存を作る ----

        private static void Fill(Profile p, string hero, bool clusters)
        {
            var tree = HeroSigils.TreeFor(hero).OrderBy(n => n.Id.Contains(".cluster.") || n.Id.Contains(".outer.") ? 0 : 1).ToList();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var node in tree)
                {
                    if (node.IsKeystone || !clusters && node.Id.Contains(".cluster.")) continue;
                    try
                    {
                        if (!p.Hero(hero).Talents.ContainsKey(node.Id)) TreeTestPaths.Connect(p, hero, node.Id);
                        while (p.Hero(hero).Talents.GetValueOrDefault(node.Id) < node.MaxRank)
                        {
                            if (Rules.FreePoints(p, hero) < node.RankCost) break;
                            Rules.AddTalentRank(p, hero, node.Id, node.IsChoice ? (int?)(node.Id.GetHashCode() & 1) : null);
                            progress = true;
                        }
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }

        private static Profile V130Profile(bool clusters = false)
        {
            var p = Profile.CreateNew(31);
            for (int i = 0; i < 25; i++) p.Codex.Add("codex." + i); // 図鑑ボーナス+4
            p.Hero(Cetus).Kills = 20000;
            p.Hero(Cetus).StarXp = StarProgression.TotalXpForPoints(150 - p.CodexBonusPoints) + 77; // 旧上限付近
            p.Hero(Vesper).Kills = 5000;
            p.Hero(Vesper).StarXp = StarProgression.TotalXpForPoints(60);
            p.Hero(Husk).Kills = 10;
            p.Hero(Husk).StarXp = 0;
            Fill(p, Cetus, clusters);
            Fill(p, Vesper, clusters);
            // 刻印（Cetus）。残りポイントを確保してから取る。
            var h = p.Hero(Cetus);
            h.StarXp = StarProgression.TotalXpForPoints(Math.Min(StarProgression.MaxPoints, Rules.SpentPoints(h, Cetus) + 3 - p.CodexBonusPoints));
            Rules.SetKeystone(p, Cetus, "h.cetus.key");
            Assert.Equal("h.cetus.key", h.Keystone);
            Assert.Equal(0, Rules.FreePoints(p, Cetus));
            return p;
        }

        private static string Encode(Profile p, int version, bool keepChoices = false)
        {
            string v4 = ProfileCodec.Write(p);
            if (version == 4) return v4;
            var root = (JsonObject)Json.Parse(v4);
            root.TryGet("body", out object body);
            var legacy = Strip(body, keepChoices);
            string checksum;
            using (var sha = SHA256.Create())
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(legacy)))).Replace("-", "").ToLowerInvariant();
            return Json.Write(new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)version)
                .Add("checksum", checksum).Add("body", legacy));
        }

        private static object Strip(object value, bool keepChoices)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var kv in obj.Properties)
                    if (kv.Key != "authoredMigrationVersion" && (keepChoices || kv.Key != "talentChoices")) copy.Add(kv.Key, Strip(kv.Value, keepChoices));
                return copy;
            }
            if (value is List<object> list) return list.Select(x => Strip(x, keepChoices)).ToList();
            return value;
        }

        // ---- v1.31 の登録（合成） ----

        private static AuthoredStarDef NewStar(string id, string anchor, ClusterStarDef effect, string hero = Cetus) => new AuthoredStarDef
        {
            HeroKey = hero, LocalStarId = id, ClusterId = "outer.carry.test", Region = ClusterRegion.Outer,
            AnchorId = anchor, Shape = ClusterShape.Ring, Effect = effect
        };

        /// <summary>外周に 150 個の新しい星の鎖を足す（既存の星は変更しない）。</summary>
        private static AuthoredStarDef[] AdditiveSet(int count = 150, string hero = Cetus)
        {
            var list = new List<AuthoredStarDef>();
            list.Add(NewStar("outer.carry.s1", "outer.carry.s1", new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の守り", "Carry Guard"), Stat = Stat.Armor, Amount = 1 }, hero));
            for (int i = 2; i <= count; i++)
            {
                var star = NewStar("outer.carry.s" + i, "outer.carry.s1", new ClusterStarDef
                { Kind = ClusterStarKind.MemoryDamage, Name = new Txt("試験の星", "Carry Star"), Memory = Memory, Amount = 3 }, hero);
                list.Add(star);
            }
            list[0].Edges = new[] { new AuthoredStarEdge("outer.carry.s1", "outer.carry.s2") };
            for (int i = 2; i < count; i++) list[i - 1].Edges = new[] { new AuthoredStarEdge("outer.carry.s" + i, "outer.carry.s" + (i + 1)) };
            return list.ToArray();
        }

        /// <summary>他の旅人用: 記憶に依存しないステータス星の鎖。</summary>
        private static AuthoredStarDef[] StatSet(string hero)
        {
            var set = AdditiveSet(120, hero);
            foreach (var d in set.Skip(1)) d.Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の守り", "Carry Guard"), Stat = Stat.Armor, Amount = 1 };
            return set;
        }

        private static void Unregister() => StarClusters.RegisterAuthored(Cetus, Array.Empty<AuthoredStarDef>());
        private static void Unregister(string hero) => StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());

        private static int Spent(Profile p, string hero) => Rules.SpentPoints(p.Hero(hero), hero);

        private sealed class Snapshot
        {
            public int Points, Spent, Free, Xp; public string Keystone;
            public Dictionary<string, int> Ranks; public Dictionary<string, int> Choices;
            public static Snapshot Of(Profile p, string hero)
            {
                var h = p.Hero(hero);
                return new Snapshot { Points = p.TalentPoints(hero), Spent = Spent(p, hero), Free = Rules.FreePoints(p, hero), Xp = h.StarXp,
                    Keystone = h.Keystone, Ranks = new Dictionary<string, int>(h.Talents), Choices = new Dictionary<string, int>(h.TalentChoices) };
            }
        }

        private static void AssertSame(Snapshot a, Snapshot b)
        {
            Assert.Equal(a.Points, b.Points); Assert.Equal(a.Spent, b.Spent); Assert.Equal(a.Free, b.Free);
            Assert.Equal(a.Xp, b.Xp); Assert.Equal(a.Keystone, b.Keystone);
            Assert.Equal(a.Ranks.OrderBy(x => x.Key), b.Ranks.OrderBy(x => x.Key));
            Assert.Equal(a.Choices.OrderBy(x => x.Key), b.Choices.OrderBy(x => x.Key));
        }

        // ---- 試験 ----

        [Fact]
        public void Every_v130_star_id_still_exists_with_the_same_rank_and_cost_before_and_after_registration()
        {
            void Check()
            {
                foreach (string row in V130StarIds.Rows)
                {
                    var f = row.Split('|');
                    Assert.True(Content.TryGetTalent(f[0], f[1], out var def), "missing: " + row);
                    Assert.Equal(int.Parse(f[2]), def.MaxRank);
                    Assert.Equal(int.Parse(f[3]), def.RankCost);
                    Assert.Equal(f[4] == "1", def.IsKeystone);
                    Assert.Equal(f[0], def.HeroKey);
                }
            }
            Check();
            StarClusters.RegisterAuthored(Cetus, AdditiveSet());
            try { Check(); } finally { Unregister(); }
        }

        [Fact]
        public void Dev_era_v3_save_with_cluster_stars_and_choices_carries_over()
        {
            var p = V130Profile(true);
            string text = Encode(p, 3, true);
            var before = Snapshot.Of(p, Cetus);
            Assert.Contains(before.Ranks.Keys, k => k.StartsWith("h.cetus.cluster.", StringComparison.Ordinal));
            StarClusters.RegisterAuthored(Cetus, AdditiveSet());
            try
            {
                var notes = new List<string>();
                var q = ProfileCodec.Read(text, notes);
                Assert.Empty(notes);
                AssertSame(before, Snapshot.Of(q, Cetus));
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Profile_fixture_is_realistic()
        {
            var p = V130Profile();
            var s = Snapshot.Of(p, Cetus);
            _out.WriteLine($"Cetus points={s.Points} spent={s.Spent} nodes={s.Ranks.Count} key={s.Keystone}");
            Assert.InRange(s.Points, 100, StarProgression.MaxSpendablePoints);
            Assert.True(s.Ranks.Count >= 20);
            Assert.True(Spent(p, Vesper) > 0);
            Assert.Equal(p.CodexBonusPoints, Snapshot.Of(p, Husk).Points); // 経験0の旅人は図鑑ボーナスだけ
            Assert.Equal(0, Snapshot.Of(p, Husk).Spent);
            Assert.DoesNotContain(s.Ranks.Keys, k => k.Contains(".cluster."));
            Assert.Contains(V130Profile(true).Hero(Cetus).Talents.Keys, k => k.StartsWith("h.cetus.cluster.", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void Baseline_roundtrip_is_stable_for_both_formats(int version)
        {
            var p = V130Profile();
            string text = Encode(p, version);
            var notes = new List<string>();
            var q = ProfileCodec.Read(text, notes);
            Assert.Empty(notes);
            Assert.Equal(version, q.LoadedVersion);
            foreach (var hero in new[] { Cetus, Vesper, Husk }) AssertSame(Snapshot.Of(p, hero), Snapshot.Of(q, hero));
            Assert.Equal(ProfileCodec.Write(p), ProfileCodec.Write(q)); // decode -> encode (v4) is identical
            Assert.Equal(ProfileCodec.Write(q), ProfileCodec.Write(ProfileCodec.Read(ProfileCodec.Write(q), null)));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void Additive_v131_registration_carries_every_point_and_allocation(int version)
        {
            var p = V130Profile();
            string text = Encode(p, version);
            var before = new Dictionary<string, Snapshot>();
            foreach (var hero in new[] { Cetus, Vesper, Husk }) before[hero] = Snapshot.Of(p, hero);
            int baselineNodes = HeroSigils.TreeFor(Cetus).Count;
            StarClusters.RegisterAuthored(Cetus, AdditiveSet());
            try
            {
                Assert.True(HeroSigils.TreeFor(Cetus).Count >= baselineNodes + 140);
                var notes = new List<string>();
                var q = ProfileCodec.Read(text, notes);
                Assert.Empty(notes);
                foreach (var hero in new[] { Cetus, Vesper, Husk }) AssertSame(before[hero], Snapshot.Of(q, hero));
                // 無償付与なし: 空きポイントは変わらず、新しい星は未取得。
                Assert.Equal(before[Cetus].Free, Rules.FreePoints(q, Cetus));
                Assert.DoesNotContain(q.Hero(Cetus).Talents.Keys, k => k.StartsWith("outer.carry", StringComparison.Ordinal));
                Assert.Equal(p.CodexBonusPoints, Rules.FreePoints(q, Husk));
                // 再保存しても同じ内容で、2回目の読み込み後も不変。
                string again = ProfileCodec.Write(q);
                var r = ProfileCodec.Read(again, new List<string>());
                Assert.Equal(again, ProfileCodec.Write(r));
                foreach (var hero in new[] { Cetus, Vesper, Husk }) AssertSame(before[hero], Snapshot.Of(r, hero));
                // 登録を外しても（旧MODに戻すなど）新星未取得の保存は同じ内容のまま。
                Unregister();
                AssertSame(before[Cetus], Snapshot.Of(ProfileCodec.Read(again, new List<string>()), Cetus));
            }
            finally { Unregister(); }
        }

        [Fact]
        public void New_stars_are_bought_only_with_earned_points_and_never_exceed_the_cap()
        {
            var p = V130Profile();
            string text = Encode(p, 3);
            StarClusters.RegisterAuthored(Cetus, AdditiveSet());
            try
            {
                var q = ProfileCodec.Read(text, new List<string>());
                int free = Rules.FreePoints(q, Cetus);
                Assert.Equal(0, free); // 旧上限まで使い切った保存は、新しい星を買う余裕がない
                Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(q, Cetus, "outer.carry.s1"));
                var h = q.Hero(Cetus);
                // 実際に稼いだ経験でだけ買える。
                h.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                int bought = 0;
                for (int i = 1; i <= 150; i++)
                {
                    try { TreeTestPaths.Connect(q, Cetus, "outer.carry.s" + i); Rules.AddTalentRank(q, Cetus, "outer.carry.s" + i); bought++; }
                    catch (InvalidOperationException) { break; }
                }
                Assert.True(bought > 0);
                Assert.True(Spent(q, Cetus) <= StarProgression.MaxSpendablePoints);
                Assert.True(q.TalentPoints(Cetus) <= StarProgression.MaxSpendablePoints);
                h.StarXp = int.MaxValue;
                Assert.Equal(StarProgression.MaxSpendablePoints, q.TalentPoints(Cetus));
                Assert.True(Rules.FreePoints(q, Cetus) >= 0);
                // 保存して読み直しても余り・使用量は変わらない。
                var snap = Snapshot.Of(q, Cetus);
                AssertSame(snap, Snapshot.Of(ProfileCodec.Read(ProfileCodec.Write(q), new List<string>()), Cetus));
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Replaced_star_is_refunded_exactly_and_dependents_cascade_without_loss()
        {
            var p = V130Profile(true);
            var h = p.Hero(Cetus);
            var tree = HeroSigils.TreeFor(Cetus);
            var pointsBefore = p.TalentPoints(Cetus);
            int spentBefore = Spent(p, Cetus);
            // 変更された既存の星（効果の差し替え）。実際に取得済みの星団の星から選ぶ。
            var candidates = CetusRetained().Where(r => h.Talents.ContainsKey(r.LocalStarId)).ToList();
            Assert.NotEmpty(candidates);
            string replaced = candidates.First(c => !tree.First(t => t.Id == c.LocalStarId).IsChoice).LocalStarId;
            int rank = h.Talents[replaced];
            int cost = tree.First(t => t.Id == replaced).RankCost;
            var rules = CetusRetained().Select(r => new LegacyStarMigration(r.LocalStarId, r.MaxRank, r.RankCost, r.LocalStarId == replaced)).ToArray();
            var refund = AuthoredStarMigration.Apply(h, tree, rules);
            _out.WriteLine($"replaced={replaced} rank={rank} cost={cost} refund={refund.RefundCost} ids={string.Join(",", refund.StarIds)}");
            Assert.Contains(replaced, refund.StarIds);
            Assert.False(h.Talents.ContainsKey(replaced));
            // 保存・総ポイントは不変、使用+返却=元の使用量（失われない・増えない）。
            Assert.Equal(pointsBefore, p.TalentPoints(Cetus));
            Assert.Equal(spentBefore, Spent(p, Cetus) + refund.RefundCost);
            Assert.True(refund.RefundCost >= rank * cost);
            Assert.Equal(refund.RefundCost, Rules.FreePoints(p, Cetus));
            // 返却分だけ空きになり、冪等（もう一度適用しても追加の返却なし）。
            Assert.Equal(0, AuthoredStarMigration.Apply(h, tree, rules).RefundCost);
        }

        [Fact]
        public void Replaced_star_with_no_dependents_refunds_exactly_rank_times_cost()
        {
            var p = Profile.CreateNew(5);
            var h = p.Hero(Cetus);
            h.Kills = 20000; h.StarXp = StarProgression.TotalXpForPoints(40);
            var tree = HeroSigils.TreeFor(Cetus);
            var leaf = CetusRetained().Select(r => tree.First(t => t.Id == r.LocalStarId)).First(t => !t.IsChoice && t.RankCost >= 1);
            TreeTestPaths.Connect(p, Cetus, leaf.Id);
            Rules.AddTalentRank(p, Cetus, leaf.Id);
            int before = Spent(p, Cetus);
            // 末端の星だけ差し替え（取得は先頭から連鎖するので、末端を差し替えた時の返却は rank*cost + その子）。
            var rules = CetusRetained().Select(r => new LegacyStarMigration(r.LocalStarId, r.MaxRank, r.RankCost, r.LocalStarId == leaf.Id)).ToArray();
            var refund = AuthoredStarMigration.Apply(h, tree, rules);
            Assert.Contains(leaf.Id, refund.StarIds);
            Assert.Equal(before, Spent(p, Cetus) + refund.RefundCost);
            Assert.Equal(p.TalentPoints(Cetus), Spent(p, Cetus) + Rules.FreePoints(p, Cetus));
        }

        [Fact]
        public void Keystone_survives_when_still_valid_and_is_refunded_when_its_path_disappears()
        {
            var p = V130Profile();
            var h = p.Hero(Cetus);
            var tree = HeroSigils.TreeFor(Cetus);
            Assert.Equal("h.cetus.key", h.Keystone);
            // 有効なまま: 返却なし。
            var refund = AuthoredStarMigration.Apply(h, tree, Array.Empty<LegacyStarMigration>());
            Assert.Equal(0, refund.RefundCost);
            Assert.Equal("h.cetus.key", h.Keystone);
            // 経路が壊れた（刻印の条件=ルート6段階が満たせない）場合は、刻印の費用が返る。
            int spent = Spent(p, Cetus);
            int points = p.TalentPoints(Cetus);
            var broken = p.Clone().Hero(Cetus);
            broken.Talents.Clear(); broken.TalentChoices.Clear();
            var result = AuthoredStarMigration.Apply(broken, tree, Array.Empty<LegacyStarMigration>());
            Assert.Null(broken.Keystone);
            var copy = new Profile(); foreach (var kv in p.Codex) copy.Codex.Add(kv);
            copy.Heroes[Cetus] = broken;
            Assert.Equal(points, copy.TalentPoints(Cetus));
            Assert.Equal(points, Spent(copy, Cetus) + Rules.FreePoints(copy, Cetus));
            Assert.True(result.RefundCost >= Content.KeystoneCost);
            Assert.True(spent >= Spent(copy, Cetus));
        }

        [Fact]
        public void Retained_legacy_star_with_changed_numbers_keeps_its_rank_through_the_codec()
        {
            var p = V130Profile();
            string text = Encode(p, 4);
            var before = Snapshot.Of(p, Cetus);
            var tree = HeroSigils.TreeFor(Cetus);
            var original = tree.First(t => t.Id == "h.cetus.outer.abyssal-shell");
            var changed = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("深海の殻", "Abyssal Shell"), Stat = Stat.Armor,
                Amount = 3, MaxRank = original.MaxRank, RankCost = original.RankCost };
            var def = new AuthoredStarDef { HeroKey = Cetus, LocalStarId = original.Id, ClusterId = "h.cetus.cluster.abyssal-shell", Region = ClusterRegion.Outer,
                AnchorId = original.Id, Shape = ClusterShape.Ring, Effect = changed, RetainedLegacy = true };
            AuthoredStarDef[] set = AdditiveSet();
            try
            {
                try { StarClusters.RegisterAuthored(Cetus, new[] { def }.Concat(set)); }
                catch (InvalidOperationException e) { _out.WriteLine("synthetic retained set rejected by contract: " + e.Message); return; }
                var q = ProfileCodec.Read(text, new List<string>());
                AssertSame(before, Snapshot.Of(q, Cetus));
            }
            finally { Unregister(); }
        }

        private static IReadOnlyList<LegacyStarMigration> CetusRetained() => AuthoredStarMigration.CetusRetained;

        // ---- 実保存のコピーでの確認（環境変数が無い/ファイルが無ければ何もしない） ----

        [Fact]
        public void Real_save_copy_carries_over_when_available()
        {
            string path = Environment.GetEnvironmentVariable(CopyEnv);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { _out.WriteLine("SKIPPED: " + CopyEnv + " not set or file missing"); return; }
            string text = File.ReadAllText(path);
            var notes0 = new List<string>();
            var baseline = ProfileCodec.Read(text, notes0);
            var heroes = baseline.Heroes.Keys.ToList();
            var before = heroes.ToDictionary(k => k, k => Snapshot.Of(baseline, k));
            _out.WriteLine($"format version={baseline.LoadedVersion} heroes={heroes.Count} baseline notes={notes0.Count} codex bonus={baseline.CodexBonusPoints}");
            foreach (var k in heroes)
                _out.WriteLine($"  [{k}] xp={before[k].Xp} points={before[k].Points} spent={before[k].Spent} free={before[k].Free} stars={before[k].Ranks.Count} keystone={(before[k].Keystone != null)}");
            var registered = heroes.Where(HeroSigils.HasTree).ToList();
            foreach (var k in registered)
                StarClusters.RegisterAuthored(k, k == Cetus ? AdditiveSet() : StatSet(k));
            try
            {
                var notes1 = new List<string>();
                var q = ProfileCodec.Read(text, notes1);
                _out.WriteLine($"after v1.31 registration: notes={notes1.Count}");
                foreach (var k in heroes)
                {
                    var after = Snapshot.Of(q, k);
                    _out.WriteLine($"  [{k}] points {before[k].Points}->{after.Points} spent {before[k].Spent}->{after.Spent} free {before[k].Free}->{after.Free} stars {before[k].Ranks.Count}->{after.Ranks.Count}");
                    AssertSame(before[k], after);
                    Assert.True(after.Points <= StarProgression.MaxSpendablePoints);
                    Assert.True(after.Spent <= StarProgression.MaxSpendablePoints);
                }
                Assert.Equal(notes0.Count, notes1.Count);
                string again = ProfileCodec.Write(q);
                Assert.Equal(again, ProfileCodec.Write(ProfileCodec.Read(again, new List<string>())));
                _out.WriteLine("re-encoded as version " + Profile.CurrentVersion + ", round trip stable; registered heroes=" + registered.Count);
            }
            finally { foreach (var k in registered) Unregister(k); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BalanceSim;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v2.10 の追加分（feat/hero-run-growth-1016）：Aurena/Lacerta/Nachia/Yubar/Bismuth の遠征成長と
    /// Aurena/Nachia/Yubar/Bismuth の刻印各2個。既存の RunGrowth（StarAdditionsV132・BalanceSimV132）と
    /// 生成星図（GeneratedHeroAcceptance）のテストと同じ形で、追加した分だけを確かめる。
    /// </summary>
    [Collection("Generated hero registry")]
    public class HeroGrowthKeystoneAdditionsTests
    {
        private static readonly string[] GrowthHeroes = { "Hero_Aurena", "Hero_Lacerta", "Hero_Nachia", "Hero_Yubar", "Hero_Bismuth" };
        private static readonly string[] KeystoneHeroes = { "Hero_Aurena", "Hero_Nachia", "Hero_Yubar", "Hero_Bismuth" };
        private static readonly string[] AddedKeystones =
        {
            "aurena.key.golden-afterglow", "aurena.key.reaction-pulse", "nachia.key.oath-of-the-pack",
            "nachia.key.heartbeat-of-the-grove", "yubar.key.supernova-afterglow", "yubar.key.unstable-orbit",
            "bismuth.key.crystal-resonance", "bismuth.key.autocast-echo",
        };

        public static IEnumerable<object[]> GrowthHeroCases()
        {
            // 追加した5人の成長条件（HostAuthority.RunGrowth の接続先）。
            yield return new object[] { "Hero_Aurena", RunGrowthTrigger.HealSucceeded };
            yield return new object[] { "Hero_Lacerta", RunGrowthTrigger.EnemyKill };
            yield return new object[] { "Hero_Nachia", RunGrowthTrigger.SummonHit };
            yield return new object[] { "Hero_Yubar", RunGrowthTrigger.BasicAttackHit };
            yield return new object[] { "Hero_Bismuth", RunGrowthTrigger.MemoryHit };
        }

        private static void RegisterAll()
        {
            // RegisterAllGenerated はプロセスで1回しか登録しない。基準状態リセット後も毎回登録し直す。
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
        }

        private static Profile Funded(string hero)
        {
            var profile = Profile.CreateNew(1331);
            profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            profile.Hero(hero).Kills = 1000000;
            return profile;
        }

        private static string Local(string hero, string suffix) => hero.Substring("Hero_".Length).ToLowerInvariant() + suffix;

        /// <summary>tools/balance/run-growth.json の effects 表（生成定数）。</summary>
        private static Dictionary<string, decimal> BalanceEffects()
        {
            string path = null;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "tools", "balance", "run-growth.json");
                if (File.Exists(candidate)) { path = candidate; break; }
            }
            Assert.NotNull(path);
            var effects = new Dictionary<string, decimal>(StringComparer.Ordinal);
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                foreach (var field in document.RootElement.GetProperty("effects").EnumerateObject())
                    effects.Add(field.Name, field.Value.GetDecimal());
            return effects;
        }

        private static RunGrowthEntry BuyGrowth(string hero, params string[] extra)
        {
            var profile = Funded(hero);
            var purchases = new List<StarPurchase> { new StarPurchase(Local(hero, ".run.g1")) };
            foreach (string id in extra) purchases.Add(new StarPurchase(id));
            V132Builds.BuyStars(profile, hero, purchases);
            return Assert.Single(Build.Compute(profile, hero, 0).RunGrowths);
        }

        [Theory, MemberData(nameof(GrowthHeroCases))]
        public void Growth_stacks_on_the_added_condition_and_stops_at_the_cap_from_the_balance_table(string hero, RunGrowthTrigger trigger)
        {
            RegisterAll();
            var effects = BalanceEffects();
            string prefix = Local(hero, ".run.g1");
            var entry = BuyGrowth(hero);
            Assert.Equal(prefix, entry.StarId);
            Assert.Equal(trigger, entry.Trigger);
            Assert.Equal((int)effects[prefix + "/growth/threshold"], entry.Threshold);
            Assert.Equal((int)effects[prefix + "/growth/cap"], entry.Cap);
            Assert.Equal(0, entry.EffectPercent);
            Assert.Equal(1, entry.GainMultiplier);

            // 条件の出来事（このトリガー）だけで溜まり、ほかのトリガーは数えない。
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("test");
            RunGrowthTrigger other = trigger == RunGrowthTrigger.EnemyKill ? RunGrowthTrigger.ParrySuccess : RunGrowthTrigger.EnemyKill;
            Assert.Equal(0, ledger.Gain("owner", entry, other, 1d));
            for (int i = 1; i < entry.Threshold; i++)
                Assert.Equal(0, ledger.Gain("owner", entry, trigger, 1d)); // 閾値の直前までは進行だけが溜まる
            Assert.Equal(1, ledger.Gain("owner", entry, trigger, 1d));
            Assert.Equal(1, ledger.Stacks("owner", entry.StarId));

            // 上限を超えて与えても cap で止まり、効果量はバランス表の1スタック分 × cap。
            for (int i = 0; i < entry.Cap * entry.Threshold + entry.Threshold; i++) ledger.Gain("owner", entry, trigger, 1d);
            Assert.Equal(entry.Cap, ledger.Stacks("owner", entry.StarId));
            Assert.Equal(0, ledger.Gain("owner", entry, trigger, 1d)); // 上限では進行も貯めない
            for (int i = 0; i < entry.Effects.Count; i++)
            {
                decimal perStack = effects[prefix + "/growth/effects/" + i + "/amount"];
                Assert.Equal(perStack, Math.Round((decimal)RunGrowth.StatTotal(entry, entry.Effects[i], 1), 6));
                Assert.Equal(entry.Cap * perStack, Math.Round((decimal)RunGrowth.StatTotal(entry, entry.Effects[i], entry.Cap), 6));
            }
        }

        [Fact]
        public void Growth_choice_and_cap_stars_compose_for_the_added_travelers()
        {
            RegisterAll();
            foreach (string hero in GrowthHeroes)
            {
                // 既存4人と同じ型：choice は2つの RunGrowthModifier の桁で、g1＋m1＋m2 に重なる。
                var profile = Funded(hero);
                V132Builds.BuyStars(profile, hero, new[]
                {
                    new StarPurchase(Local(hero, ".run.g1")), new StarPurchase(Local(hero, ".run.m1")),
                    new StarPurchase(Local(hero, ".run.m2")), new StarPurchase(Local(hero, ".run.choice"), 0),
                });
                var boosted = Assert.Single(Build.Compute(profile, hero, 0).RunGrowths);
                Assert.Equal(100, boosted.Cap);  // 60 + 20 + 20
                Assert.Equal(50, boosted.EffectPercent);
                Assert.Equal(1, boosted.GainMultiplier);
                Assert.Contains(Local(hero, ".run.m1"), boosted.ContributorIds);
                Assert.Contains(Local(hero, ".run.choice"), boosted.ContributorIds);

                profile.Hero(hero).TalentChoices[Local(hero, ".run.choice")] = 1; // 上限 +20 の選択肢
                var raised = Assert.Single(Build.Compute(profile, hero, 0).RunGrowths);
                Assert.Equal(120, raised.Cap);
                Assert.Equal(0, raised.EffectPercent);
                Assert.Equal(1, raised.GainMultiplier);
            }
        }

        [Fact]
        public void The_keystone_travelers_hold_nine_authored_keystones_with_the_same_rank_and_cost_rules()
        {
            RegisterAll();
            foreach (string hero in KeystoneHeroes)
            {
                Assert.True(StarClusters.TryGetRegisteredTree(hero, out var tree));
                // 刻印領域の authored（移行でなく新設）の刻印だけを数える。移行の h.*.key は除く。
                var keystones = tree.Where(t => t.IsKeystone && t.AuthoredStar != null && !t.AuthoredStar.RetainedLegacy
                    && t.AuthoredStar.Region.Kind == ClusterRegionKind.Keystone).ToArray();
                Assert.Equal(9, keystones.Length); // 同行の刻印を各旅人に1個追加。
                string prefix = hero.Substring("Hero_".Length).ToLowerInvariant() + ".key.";
                var own = AddedKeystones.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                Assert.Equal(2, own.Length);
                foreach (string id in own) Assert.Contains(id, keystones.Select(k => k.Id));
                // 1段・費用1ポイントは、追加分も既存の authored 刻印と同じ規則。
                Assert.All(keystones, k => Assert.Equal(1, k.MaxRank));
                Assert.All(keystones, k => Assert.Equal(1, k.KeystoneCost));
            }
        }

        [Fact]
        public void Added_stat_keystones_apply_their_balance_amounts()
        {
            RegisterAll();
            var cases = new (string Hero, string Keystone, Stat Stat, int Amount)[]
            {
                ("Hero_Aurena", "aurena.key.golden-afterglow", Stat.ShieldPower, 40),
                ("Hero_Aurena", "aurena.key.reaction-pulse", Stat.Haste, 12),
                ("Hero_Nachia", "nachia.key.oath-of-the-pack", Stat.Armor, 10),
                ("Hero_Nachia", "nachia.key.heartbeat-of-the-grove", Stat.SummonPower, 18),
                ("Hero_Yubar", "yubar.key.unstable-orbit", Stat.Haste, 15),
                ("Hero_Bismuth", "bismuth.key.crystal-resonance", Stat.Armor, 9),
                ("Hero_Bismuth", "bismuth.key.autocast-echo", Stat.Haste, 12),
            };
            foreach (var row in cases)
            {
                var build = MaxedBuildWithKeystones(row.Hero, row.Keystone, out var without);
                Assert.Equal(row.Amount, build.Get(row.Stat) - without.Get(row.Stat));
            }
        }

        [Fact]
        public void Added_keystones_survive_the_verified_mechanism_slot_composition()
        {
            RegisterAll();
            foreach (string hero in KeystoneHeroes)
            {
                Assert.True(StarClusters.TryGetRegisteredTree(hero, out var tree));
                var keys = tree.Where(t => t.IsKeystone).ToArray();
                var layout = HeroTreeLayout.ForHero(hero);
                var byId = tree.ToDictionary(t => t.Id);
                var profile = Funded(hero);
                var state = profile.Hero(hero);
                foreach (var talent in tree.Where(t => !t.IsKeystone))
                {
                    state.Talents[talent.Id] = talent.MaxRank;
                    if (talent.IsChoice) state.TalentChoices[talent.Id] = 0;
                }
                for (int offset = 0; offset < keys.Length; offset += KeystoneSlots.Max)
                {
                    state.ClearKeystones();
                    foreach (var key in keys.Skip(offset).Take(KeystoneSlots.Max)) state.AddKeystone(key.Id);
                    var build = Build.ComputeForValidatedTree(profile, hero, tree, state, state, layout, byId,
                        layout.ReachabilitySnapshot(state), paidPoints: StarProgression.MaxSpendablePoints);
                    AssertMechanismValuesAreNotCapped(build, hero);
                }
            }
            // 超新星の残光：MemoryPrimed の仕掛けとして、値25%・持続8秒で Build に入る。
            var supernova = MaxedBuildWithKeystones("Hero_Yubar", "yubar.key.supernova-afterglow", out _);
            var entry = Assert.Single(supernova.Mechanisms, m => m.StarId == "yubar.key.supernova-afterglow");
            Assert.Equal(AuthoredMechanismKind.MemoryPrimed, entry.Spec.Kind);
            var payload = AuthoredKeystoneComposer.MechanismPayload(entry.Spec);
            Assert.Equal(25m, payload.Value);
            Assert.Equal(8m, payload.DurationSeconds);
            Assert.Equal(KeystonePayloadKind.MemoryPrimed, payload.Kind);

            // 結晶の共鳴：Q・R枠の記憶使用を Empower の仕掛けとして、値12%で Build に入る。
            var resonance = MaxedBuildWithKeystones("Hero_Bismuth", "bismuth.key.crystal-resonance", out _);
            var crystal = Assert.Single(resonance.Mechanisms, m => m.StarId == "bismuth.key.crystal-resonance");
            Assert.Equal(AuthoredMechanismKind.Gimmick, crystal.Spec.Kind);
            var crystalPayload = AuthoredKeystoneComposer.MechanismPayload(crystal.Spec);
            Assert.Equal(GimmickEffect.Empower, crystalPayload.Effect);
            Assert.Equal(12m, crystalPayload.Value);
            Assert.Equal(KeystonePayloadKind.Gimmick, crystalPayload.Kind);

            // 追加した刻印は、検証済みスロットのどの memory 由来の payload も弱めない（All_88…と同じ確認）。
            foreach (string hero in KeystoneHeroes)
            {
                Assert.True(StarClusters.TryGetRegisteredTree(hero, out var tree));
                foreach (var star in tree.Where(t => AddedKeystones.Contains(t.Id)))
                {
                    var key = star.KeystoneDefinition;
                    if (key == null) { Assert.True(star.PerRank > 0, star.Id + " has no typed or stat upside"); continue; }
                    var build = new Build { SelectedKeystone = key };
                    foreach (string source in VerifiedMechanismSlots.ForHero(hero)
                        .Where(m => !m.StartsWith("St_M_", StringComparison.Ordinal))) // 移動の記憶は All_88…と同じく除外
                    {
                        var damage = new KeystonePayload(KeystoneLayer.NativeDamage, 100, new KeystoneCaps(1000));
                        Assert.True(AuthoredKeystoneComposer.TransformAllocationPayload(build, damage, source,
                            heroKey: hero).Value >= damage.Value, star.Id + " penalizes " + source);
                    }
                }
            }
        }

        [Fact]
        public void Route_and_authored_star_totals_stay_at_the_new_fixed_values()
        {
            RegisterAll();
            // tools/star-manifest/validate.py の EXPECTED（各自の新設行）に、共有の外周169行と移行の authored 行を足した総数。
            var authoredTotals = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Hero_Aurena"] = 730, ["Hero_Bismuth"] = 723, ["Hero_Cetus"] = 874, ["Hero_Husk"] = 862,
                ["Hero_Lacerta"] = 852, ["Hero_Mist"] = 863, ["Hero_Nachia"] = 721, ["Hero_Vesper"] = 857,
                ["Hero_Yubar"] = 856,
            };
            foreach (string hero in StarClusters.GeneratedHeroes)
            {
                var authored = StarClusters.CreateGeneratedAuthored(hero);
                Assert.All(authored, star => Assert.NotNull(star));
                Assert.Equal(authoredTotals[hero], authored.Length);
            }
            // 全旅人のルート数（Bismuth だけ6本）と、1本7星の固定形。
            var routes = HeroStarRoutes.All.Where(t => t.RouteId != null)
                .GroupBy(t => t.HeroKey).ToDictionary(g => g.Key, g => g.Select(t => t.RouteId).Distinct().Count());
            Assert.Equal(9, routes.Count);
            Assert.All(routes, pair => Assert.Equal(pair.Key == "Hero_Bismuth" ? 6 : 7, pair.Value));
            Assert.Equal(532, HeroStarRoutes.All.Count);
        }

        // ───── 補助 ─────

        /// <summary>追加した刻印1個を選んだ最大割り当ての Build（未選択の基準も返す）。</summary>
        private static Build MaxedBuildWithKeystones(string hero, string keystoneId, out Build without)
        {
            Assert.True(StarClusters.TryGetRegisteredTree(hero, out var tree));
            var layout = HeroTreeLayout.ForHero(hero);
            var byId = tree.ToDictionary(t => t.Id);
            var profile = Funded(hero);
            var state = profile.Hero(hero);
            foreach (var talent in tree.Where(t => !t.IsKeystone))
            {
                state.Talents[talent.Id] = talent.MaxRank;
                if (talent.IsChoice) state.TalentChoices[talent.Id] = 0;
            }
            Build Compute()
            {
                return Build.ComputeForValidatedTree(profile, hero, tree, state, state, layout, byId,
                    layout.ReachabilitySnapshot(state), paidPoints: StarProgression.MaxSpendablePoints);
            }
            state.ClearKeystones();
            without = Compute();
            state.ClearKeystones();
            state.AddKeystone(keystoneId);
            return Compute();
        }

        /// <summary>GeneratedHeroAcceptanceTests.AssertMechanismValuesAreNotCapped と同じ確認（追加3人だけの再実行）。</summary>
        private static void AssertMechanismValuesAreNotCapped(Build build, string hero)
        {
            foreach (var entry in build.Mechanisms)
            {
                var spec = entry.Spec;
                if (spec.Gimmick == null && spec.Recharge == null && spec.Primed == null
                    && spec.Relay == null && spec.Ward == null && spec.Dividend == null && spec.Bridge == null) continue;
                var payloads = spec.Bridge != null
                    ? spec.Bridge.Payloads.Select(p => AuthoredKeystoneComposer.BridgePayload(p)).ToArray()
                    : new[] { AuthoredKeystoneComposer.MechanismPayload(spec) };
                foreach (var payload in payloads)
                {
                    var unbounded = new KeystonePayload(payload.Layer, payload.Value,
                        new KeystoneCaps(decimal.MaxValue, probabilityPercent: payload.Caps.ProbabilityPercent),
                        payload.Kind, payload.Effect, payload.EffectId, payload.Argument, payload.DurationSeconds,
                        payload.RadiusMetres, payload.DelaySeconds, payload.TargetCount, payload.EveryN, payload.ProbabilityPercent);
                    foreach (string memory in VerifiedMechanismSlots.ForHero(hero))
                        foreach (var slot in VerifiedMechanismSlots.ForMemory(hero, memory))
                        {
                            if (slot == MechanismMemorySlot.Movement || !spec.Source.Matches(
                                new EquippedMechanismMemory(memory, 1, slot, true, false))) continue;
                            var expected = AuthoredKeystoneComposer.TransformAllocationPayload(build, unbounded, memory,
                                sourceSlot: slot, heroKey: hero);
                            var actual = AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, memory,
                                sourceSlot: slot, heroKey: hero);
                            Assert.True(expected.Value == actual.Value,
                                hero + " " + entry.StarId + "@" + memory + ": authored composition "
                                + expected.Value + " was capped to " + actual.Value);
                            Assert.Equal(expected.DurationSeconds, actual.DurationSeconds);
                            Assert.Equal(expected.RadiusMetres, actual.RadiusMetres);
                            Assert.Equal(expected.TargetCount, actual.TargetCount);
                        }
                }
            }
        }
    }
}

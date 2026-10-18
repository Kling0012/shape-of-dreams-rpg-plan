using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v2.10 で追加した刻印と、タンク以外の遠征の鍛錬を外したあとの星図を確かめる。
    /// </summary>
    [Collection("Generated hero registry")]
    public class HeroGrowthKeystoneAdditionsTests : IClassFixture<StarRegistryCleanupFixture>
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

        [Theory, MemberData(nameof(GrowthHeroCases))]
        public void Growth_is_absent_for_the_former_added_condition(string hero, RunGrowthTrigger trigger)
        {
            RegisterAll();
            string prefix = Local(hero, ".run.");
            Assert.DoesNotContain(HeroSigils.TreeFor(hero), node => node.Id.StartsWith(prefix, StringComparison.Ordinal));
            Assert.Empty(Build.Compute(Funded(hero), hero, 0).RunGrowths);
        }

        [Fact]
        public void Growth_choice_and_cap_stars_are_absent_for_the_removed_travelers()
        {
            RegisterAll();
            foreach (string hero in GrowthHeroes.Concat(new[] { "Hero_Husk", "Hero_Mist" }))
            {
                var tree = HeroSigils.TreeFor(hero);
                foreach (string suffix in new[] { ".run.g1", ".run.m1", ".run.m2", ".run.choice" })
                    Assert.DoesNotContain(tree, node => node.Id == Local(hero, suffix));
                Assert.Empty(Build.Compute(Funded(hero), hero, 0).RunGrowths);
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
                ["Hero_Aurena"] = 726, ["Hero_Bismuth"] = 719, ["Hero_Cetus"] = 874, ["Hero_Husk"] = 858,
                ["Hero_Lacerta"] = 848, ["Hero_Mist"] = 859, ["Hero_Nachia"] = 717, ["Hero_Vesper"] = 857,
                ["Hero_Yubar"] = 852,
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

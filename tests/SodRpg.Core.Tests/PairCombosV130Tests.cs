using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class PairCombosV130Tests
    {
        // Independent loadout order from the approved pair design, not registry insertion order.
        private static readonly Dictionary<string, string[]> Branches = new Dictionary<string, string[]>
        {
            ["Hero_Vesper"] = new[] { "resolve", "cruel-sun", "sanctuary", "charge", "mercy", "discipline", "baptism" },
            ["Hero_Lacerta"] = new[] { "powder", "hand-cannon", "quick-trigger", "nimble-dodge", "double-tap", "incendiary", "precision" },
            ["Hero_Cetus"] = new[] { "icy-veins", "embrace-chill", "back-off", "frost-charge", "charged", "boreal-chunk", "frozen-fists" },
            ["Hero_Yubar"] = new[] { "exotic-matter", "ethereal", "cataclysm", "flicker", "converging-stars", "supernova", "tranquility" },
            ["Hero_Husk"] = new[] { "killing-flow", "laceration", "annihilation", "flash-step", "wind-scar", "death-mark", "deception" },
            ["Hero_Mist"] = new[] { "en-garde", "lunge", "determination", "fast-feet", "priorite", "fleche", "parry" },
            ["Hero_Nachia"] = new[] { "pack-heart", "sylvan-call", "natures-whisper", "dreamy-waltz", "circle-life", "moonlight-pact", "serpent-blessing" },
            ["Hero_Aurena"] = new[] { "claw", "golden-burst", "dangerous-theory", "feathery-dash", "beautiful-threat", "reduction", "chain-reaction" },
            ["Hero_Bismuth"] = new[] { "prismatic-eyes", "innocence", "distorting-sprint", "infernal-tales", "valiant-heart", "distorted-mind" },
        };
        public static IEnumerable<object[]> Heroes => Branches.Keys.Select(h => new object[] { h });
        public static IEnumerable<object[]> Definitions => PairCombos.All.Select(d => new object[] { d.Id });
        private static PairComboDef Def(string hero, int index) => PairCombos.Get("h." + hero.ToLowerInvariant() + ".pair." + index);
        private static PairComboEntry Entry(PairComboDef def, int ranks = 1) => new PairComboEntry { Def = def, Ranks = ranks };
        private static PairComboRuntime Runtime(params PairComboEntry[] entries)
        {
            var runtime = new PairComboRuntime(); runtime.SetBuild(entries); return runtime;
        }
        private static List<GimmickRequest> Fire(PairComboRuntime runtime, PairComboTrigger trigger, string memory,
            float now, int victim, ICollection<string> equipped, float damage = 100, bool generated = false, bool summons = true,
            object activation = null, PairComboHitKind hitKind = PairComboHitKind.Any)
        {
            var requests = new List<GimmickRequest>();
            runtime.Fire(trigger, memory, now, victim, damage, generated, equipped, summons, requests, activation, hitKind);
            return requests;
        }
        private static string[] Equipped(PairComboDef def) => new[] { def.RouteA, def.RouteB };
        private static void Start(PairComboRuntime runtime, PairComboDef def, ICollection<string> equipped, float now = 0, int victim = 10,
            bool generated = false, bool summons = true) => Fire(runtime, def.Trigger, def.TriggerMemory, now, victim, equipped, generated: generated, summons: summons);
        private static List<GimmickRequest> Pay(PairComboRuntime runtime, PairComboDef def, ICollection<string> equipped,
            float now = 1, int victim = 10, bool generated = false, bool summons = true, object activation = null) =>
            def.Step == PairComboStep.None ? Fire(runtime, def.Trigger, def.TriggerMemory, now, victim, equipped,
                generated: generated, summons: summons, activation: activation ?? new object())
                : Fire(runtime, def.PayoffTrigger, def.PayoffMemory, now, victim, equipped,
                    generated: generated, summons: summons, activation: activation ?? new object(), hitKind: def.PayoffHitKind);

        [Theory]
        [MemberData(nameof(Heroes))]
        public void Approved_branch_order_and_bridge_neighbors_support_actual_loadouts(string hero)
        {
            var tree = HeroTreeLayout.ForHero(hero);
            string prefix = "h." + hero.Substring(5).ToLowerInvariant() + ".route.";
            var routes = tree.Nodes.Where(n => n.Talent?.RouteId != null).GroupBy(n => n.Talent.RouteId).ToArray();
            Assert.Equal(Branches[hero].Select(s => prefix + s), routes.Select(r => r.Key));
            var pairs = PairCombos.All.Where(d => d.HeroKey == hero).OrderBy(d => d.BridgeIndex).ToArray();
            Assert.Equal(Branches[hero].Length, pairs.Length);
            for (int i = 0; i < pairs.Length; i++)
            {
                var def = pairs[i];
                Assert.Equal(prefix + Branches[hero][i] + ".4", def.StarA);
                Assert.Equal(prefix + Branches[hero][(i + 1) % pairs.Length] + ".4", def.StarB);
                var bridge = tree.Nodes.Single(n => n.Id == def.BridgeId);
                Assert.Equal(new[] { def.StarA, def.StarB }.OrderBy(x => x), bridge.Neighbors.Select(n => tree.Nodes[n].Id).OrderBy(x => x));
                Assert.Equal(3, bridge.Talent.MaxRank);
                Assert.Equal(0, bridge.Talent.PerRank);
                Assert.NotEqual(def.RouteA, def.RouteB);
                string slotA = def.RouteA.Split('_')[1], slotB = def.RouteB.Split('_')[1];
                Assert.True(slotA != slotB || hero == "Hero_Bismuth" && slotA == "QR", def.Id);
            }
            Assert.Equal(hero == "Hero_Bismuth" ? 2 : 1,
                tree.Nodes.Count(n => n.Talent?.IsDreamRing == true && PairCombos.ForBridge(n.Id) == null && n.Talent.MaxRank == 5));
        }

        [Fact]
        public void Revised_table_rank_values_and_common_rule_counts_are_exact()
        {
            Assert.Equal(62, PairCombos.All.Count);
            Assert.Equal(62, PairCombos.All.Select(d => d.Id).Distinct().Count());
            var heal = Def("Vesper", 3);
            Assert.Equal(new[] { 3, 5, 7 }, Enumerable.Range(1, 3).Select(r => Entry(heal, r).Value));
            var element = Def("Vesper", 1);
            Assert.Equal(new[] { 100, 150, 200 }, element.TableRankValues);
            Assert.Equal(200, Entry(element, 3).Value);
            Assert.Equal(new[] { 2, 3, 5 }, Def("Nachia", 4).TableRankValues);
            Assert.Equal(5, Entry(Def("Nachia", 4), 3).Value);
            Assert.DoesNotContain(PairCombos.All, d => d.MovementOrigin);
            Assert.Equal(2, PairCombos.All.Count(d => d.Cooldown > 0));
            Assert.Equal(28, PairCombos.All.Count(d => d.OncePerActivation));
            Assert.Equal(6, PairCombos.All.Count(d => d.KillByPayoffMemory));
            Assert.Equal(1.5f, Def("Nachia", 2).Cooldown);
            Assert.Equal(1f, Def("Nachia", 5).Cooldown);
        }

        [Theory]
        [MemberData(nameof(Definitions))]
        public void Every_definition_enforces_both_equips_and_supported_two_step_payoff(string id)
        {
            var def = PairCombos.Get(id);
            string[] equipped = Equipped(def);
            var runtime = Runtime(Entry(def, 3));
            if (def.Step != PairComboStep.None)
            {
                Assert.Empty(Pay(runtime, def, equipped));
                Assert.Empty(Fire(runtime, def.Trigger, def.TriggerMemory, 0, 10, equipped));
            }
            var outputs = Pay(runtime, def, equipped);
            if (def.MovementOrigin) { Assert.Empty(outputs); return; }
            var output = Assert.Single(outputs);
            Assert.Equal(def.Id, output.Entry.StarId);
            Assert.Equal(Entry(def, 3).Value, output.Entry.Def.Value);
            Assert.Equal(def.Effect, output.Entry.Def.Effect);
            Assert.NotEqual(GimmickEffect.Expose, output.Entry.Def.Effect);
            string target = def.RechargeMemory ?? def.PayoffMemory ?? def.TriggerMemory;
            Assert.Equal(target, output.Entry.Memory);
            foreach (string missing in new[] { def.RouteA, def.RouteB })
            {
                runtime = Runtime(Entry(def));
                var incomplete = equipped.Where(m => m != missing).ToArray();
                Assert.Empty(Fire(runtime, def.Trigger, def.TriggerMemory, 0, 10, incomplete));
                if (def.Step != PairComboStep.None)
                    Assert.Empty(Pay(runtime, def, equipped)); // Missing equipment cannot plant a latent mark/window.
                Start(runtime, def, equipped);
                Assert.Empty(Pay(runtime, def, incomplete)); // Equipment is checked again at payoff time.
            }
        }

        [Theory]
        [MemberData(nameof(Heroes))]
        public void Both_fourth_stars_and_bridge_rank_are_required_without_old_ring_stats(string hero)
        {
            var def = PairCombos.All.First(d => d.HeroKey == hero);
            var p = Profile.CreateNew(130);
            var h = p.Hero(hero);
            h.StarXp = StarProgression.TotalXpForPoints(150);
            h.Talents[def.StarA] = 1; h.Talents[def.StarB] = 1;
            Assert.Null(PairCombos.Activate(def, h, 0));
            Assert.Equal(3, PairCombos.Activate(def, h, int.MaxValue).Ranks);
            h.Talents.Remove(def.StarA);
            Assert.Null(PairCombos.Activate(def, h, 1));
            h.Talents[def.StarA] = 1; h.Talents[def.StarB] = -1;
            Assert.Null(PairCombos.Activate(def, h, 1));
            h.Talents.Clear(); h.Talents[def.BridgeId] = 3;
            Assert.Empty(Build.Compute(p, hero, 0).PairCombos); // Disconnected saved allocation.
            h.Talents.Clear();
            TreeTestPaths.Connect(p, hero, def.StarA); h.Talents[def.StarA] = 1;
            TreeTestPaths.Connect(p, hero, def.StarB); h.Talents[def.StarB] = 1;
            var before = Build.Compute(p, hero, 0);
            h.Talents[def.BridgeId] = 3;
            var build = Build.Compute(p, hero, 0);
            Assert.Equal(3, Assert.Single(build.PairCombos, e => e.Def.Id == def.Id).Ranks);
            Assert.Equal(before.Stats.ToArray(), build.Stats.ToArray());
            Assert.Equal(before.Powers.ToArray(), build.Powers.ToArray());
            h.Talents.Remove(def.StarB);
            Assert.DoesNotContain(Build.Compute(p, hero, 0).PairCombos, e => e.Def.Id == def.Id);
            Assert.Empty(Build.Compute(p, hero == "Hero_Cetus" ? "Hero_Mist" : "Hero_Cetus", 0).PairCombos);
        }

        [Theory]
        [MemberData(nameof(Heroes))]
        public void QR_movement_pairs_work_with_either_unrelated_identity(string hero)
        {
            if (hero == "Hero_Bismuth") return;
            var routes = HeroStarRoutes.All.Where(t => t.HeroKey == hero && t.RouteOrder == 1).ToArray();
            var ids = routes.Where(t => t.RouteMemory.StartsWith("St_D_", StringComparison.Ordinal)).Select(t => t.RouteMemory);
            var nonIdentity = PairCombos.All.Where(d => d.HeroKey == hero && !d.RouteA.StartsWith("St_D_", StringComparison.Ordinal)
                && !d.RouteB.StartsWith("St_D_", StringComparison.Ordinal) && !d.MovementOrigin);
            foreach (var def in nonIdentity)
                foreach (string identity in ids)
                {
                    var equipped = Equipped(def).Concat(new[] { identity }).ToArray();
                    var runtime = Runtime(Entry(def));
                    if (def.Step != PairComboStep.None) Start(runtime, def, equipped);
                    Assert.Single(Pay(runtime, def, equipped));
                }
        }

        [Fact]
        public void All_six_Bismuth_QR_loadouts_allow_only_equipped_pair_bridges()
        {
            string[] qr = { "St_QR_Innocence", "St_QR_InfernalTales", "St_QR_ValiantHeart", "St_QR_DistortedMind" };
            foreach (int bridge in new[] { 4, 5 })
            {
                var def = Def("Bismuth", bridge); int active = 0;
                for (int i = 0; i < qr.Length; i++)
                    for (int j = i + 1; j < qr.Length; j++)
                    {
                        var equipped = new[] { "St_D_PrismaticEyes", "St_M_Sprint", qr[i], qr[j] };
                        var runtime = Runtime(Entry(def)); Start(runtime, def, equipped);
                        int count = Pay(runtime, def, equipped).Count;
                        Assert.Equal(equipped.Contains(def.RouteA) && equipped.Contains(def.RouteB) ? 1 : 0, count);
                        active += count;
                    }
                Assert.Equal(1, active);
            }
            // Collection membership is independent of whether Pure Soul is selected in Q or R.
            var identityPair = Def("Bismuth", 1);
            foreach (var equipped in new[] {
                new[] { "St_QR_Innocence", "St_QR_ValiantHeart", "St_D_PrismaticEyes" },
                new[] { "St_QR_ValiantHeart", "St_QR_Innocence", "St_D_PrismaticEyes" } })
            {
                var runtime = Runtime(Entry(identityPair)); Start(runtime, identityPair, equipped);
                Assert.Single(Pay(runtime, identityPair, equipped));
            }
        }

        [Fact]
        public void Pair_marks_are_independent_per_victim_pair_and_expire_or_refresh_without_damage_amp()
        {
            var a = Def("Vesper", 1); var b = Def("Vesper", 2);
            var equipped = Equipped(a).Concat(Equipped(b)).Distinct().ToArray();
            var runtime = Runtime(Entry(a), Entry(b));
            Assert.Empty(Fire(runtime, a.Trigger, a.TriggerMemory, 0, 10, equipped)); // A marker emits no effect or damage-amplification request.
            Assert.Empty(Pay(runtime, a, equipped, victim: 11));
            Assert.Empty(Pay(runtime, b, equipped, victim: 10));
            var output = Assert.Single(Pay(runtime, a, equipped, now: 2.9f));
            Assert.Equal(GimmickEffect.Element, output.Entry.Def.Effect);
            Assert.Equal(100f, output.Damage);
            Start(runtime, a, equipped, now: 3, victim: 10);
            Assert.Single(Pay(runtime, a, equipped, now: 6.9f));
            Assert.Empty(Pay(runtime, a, equipped, now: 7));
        }

        [Fact]
        public void Windows_refresh_without_stats_and_global_pair_cooldowns_survive_build_reset_or_rank_change()
        {
            var def = Def("Nachia", 2); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def)); Start(runtime, def, equipped);
            Assert.Single(Pay(runtime, def, equipped, now: 0.1f));
            runtime.SetBuild(new[] { Entry(def) });
            Assert.Empty(Pay(runtime, def, equipped, now: 1, victim: 20));
            runtime.SetBuild(new[] { Entry(def, 2) });
            Start(runtime, def, equipped, now: 1);
            Assert.Single(Pay(runtime, def, equipped, now: 1.6f, victim: 20));
            Start(runtime, def, equipped, now: 4);
            Assert.Single(Pay(runtime, def, equipped, now: 7.9f));
            Assert.Empty(Pay(runtime, def, equipped, now: 8));
            runtime.SetBuild(Array.Empty<PairComboEntry>());
            runtime.SetBuild(new[] { Entry(def) });
            Assert.Empty(Pay(runtime, def, equipped, now: 9));
        }

        [Fact]
        public void Generated_damage_cannot_create_refresh_complete_or_consume_pair_state()
        {
            var def = Def("Vesper", 6); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def));
            Start(runtime, def, equipped, generated: true);
            Assert.Empty(Pay(runtime, def, equipped));
            Start(runtime, def, equipped);
            Assert.Empty(Pay(runtime, def, equipped, generated: true));
            Assert.Single(Pay(runtime, def, equipped));
            Start(runtime, def, equipped, now: 3, generated: true);
            Assert.Empty(Pay(runtime, def, equipped, now: 4));
            var death = Def("Lacerta", 4); runtime = Runtime(Entry(death)); equipped = Equipped(death);
            Start(runtime, death, equipped);
            Assert.Empty(Pay(runtime, death, equipped, generated: true));
            Assert.Single(Pay(runtime, death, equipped));
            Assert.Empty(Pay(runtime, death, equipped, now: 2)); // Death consumes the marker.
            var window = Def("Yubar", 2); runtime = Runtime(Entry(window)); equipped = Equipped(window);
            Start(runtime, window, equipped, generated: true);
            Assert.Empty(Pay(runtime, window, equipped));
            Start(runtime, window, equipped);
            Assert.Empty(Pay(runtime, window, equipped, generated: true));
            Assert.Single(Pay(runtime, window, equipped));
            Start(runtime, window, equipped, now: 3, generated: true);
            Assert.Empty(Pay(runtime, window, equipped, now: 12)); // Generated use cannot extend the ultimate window.
        }

        [Fact]
        public void Marked_death_is_not_an_attributed_kill_and_does_not_erase_source_specific_kill_marker()
        {
            var global = Def("Lacerta", 4); var specific = Def("Lacerta", 6);
            var equipped = Equipped(global).Concat(Equipped(specific)).Distinct().ToArray();
            var runtime = Runtime(Entry(global), Entry(specific));
            Start(runtime, global, equipped); Start(runtime, specific, equipped);
            Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, "St_Q_HandCannon", 0.1f, 10, equipped));
            Start(runtime, global, equipped); Start(runtime, specific, equipped);
            Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, null, 0.2f, 10, equipped));
            Assert.Equal(specific.Id, Assert.Single(Pay(runtime, specific, equipped, now: 0.3f)).Entry.StarId);
            Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, null, 0.4f, 10, equipped));
        }

        [Theory]
        [InlineData("Lacerta", 4)]
        [InlineData("Yubar", 4)]
        [InlineData("Husk", 4)]
        [InlineData("Mist", 4)]
        [InlineData("Aurena", 4)]
        [InlineData("Bismuth", 3)]
        public void Revised_marked_kills_require_receiver_damage(string hero, int bridge)
        {
            var def = Def(hero, bridge); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def));
            Start(runtime, def, equipped);
            Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, null, 0.1f, 10, equipped));
            Assert.Single(Fire(runtime, PairComboTrigger.OnKill, def.TriggerMemory, 0.2f, 10, equipped));
            Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, def.TriggerMemory, 0.3f, 10, equipped));
            foreach (string unrelated in new[] { "St_Q_HandCannon", "St_Q_SylvanCall", "St_D_PrismaticEyes" })
            {
                runtime = Runtime(Entry(def));
                Start(runtime, def, equipped);
                Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, unrelated, 0.1f, 10, equipped));
                Assert.Empty(Fire(runtime, PairComboTrigger.OnKill, def.TriggerMemory, 0.2f, 10, equipped));
            }
        }

        [Fact]
        public void Both_Nachia_basic_attack_rows_require_summons_but_not_a_hit()
        {
            foreach (int bridge in new[] { 4, 5 })
            {
                var def = Def("Nachia", bridge); var equipped = Equipped(def); var runtime = Runtime(Entry(def));
                if (def.Step != PairComboStep.None) Start(runtime, def, equipped);
                Assert.Empty(Fire(runtime, PairComboTrigger.OnUse, "St_D_CircleOfLife", 0.1f, 10, equipped));
                Assert.Empty(Fire(runtime, PairComboTrigger.OnBasicAttack, "St_D_HeartOfThePack", 0.2f, 10, equipped));
                Assert.Empty(Fire(runtime, PairComboTrigger.OnBasicAttack, "St_D_CircleOfLife", 0.3f, 10, equipped, summons: false));
                if (def.Step != PairComboStep.None) Start(runtime, def, equipped, now: 0.4f);
                Assert.Equal(GimmickTrigger.OnHit, Assert.Single(Fire(runtime, PairComboTrigger.OnBasicAttack, null, 0.5f, 10, equipped)).Entry.Def.Trigger);
                Assert.Single(Fire(runtime, PairComboTrigger.OnBasicAttack, "St_D_CircleOfLife", 1.5f, 0, equipped));
            }
        }

        [Fact]
        public void Revised_recharge_targets_restore_the_other_memory()
        {
            foreach (var def in new[] { Def("Vesper", 2), Def("Cetus", 1), Def("Cetus", 5),
                Def("Yubar", 1), Def("Yubar", 5), Def("Nachia", 1), Def("Bismuth", 4) })
            {
                var runtime = Runtime(Entry(def, 3)); var equipped = Equipped(def);
                Start(runtime, def, equipped);
                var output = Assert.Single(Pay(runtime, def, equipped));
                Assert.Equal(GimmickEffect.Recharge, output.Entry.Def.Effect);
                Assert.Equal(def.TriggerMemory, output.Entry.Memory);
                Assert.NotEqual(def.PayoffMemory, output.Entry.Memory);
                Assert.Equal(def.TableRankValues[2], output.Entry.Def.Value);
            }
        }

        [Theory]
        [MemberData(nameof(Heroes))]
        public void Every_standard_eight_choice_loadout_only_runs_bridges_with_both_selected_memories(string hero)
        {
            if (hero == "Hero_Bismuth") return; // Its six QR selections are covered separately.
            var routes = HeroStarRoutes.All.Where(t => t.HeroKey == hero && t.RouteOrder == 1).Select(t => t.RouteMemory).ToArray();
            var identities = routes.Where(m => m.StartsWith("St_D_", StringComparison.Ordinal)).ToArray();
            var qs = routes.Where(m => m.StartsWith("St_Q_", StringComparison.Ordinal)).ToArray();
            var rs = routes.Where(m => m.StartsWith("St_R_", StringComparison.Ordinal)).ToArray();
            var movement = routes.Single(m => m.StartsWith("St_M_", StringComparison.Ordinal));
            Assert.Equal(2, identities.Length); Assert.Equal(2, qs.Length); Assert.Equal(2, rs.Length);
            foreach (string identity in identities)
                foreach (string q in qs)
                    foreach (string r in rs)
                    {
                        var equipped = new[] { identity, q, r, movement };
                        foreach (var def in PairCombos.All.Where(d => d.HeroKey == hero))
                        {
                            var runtime = Runtime(Entry(def));
                            if (def.Step != PairComboStep.None) Start(runtime, def, equipped);
                            var outputs = Pay(runtime, def, equipped);
                            Assert.Equal(!def.MovementOrigin && equipped.Contains(def.RouteA) && equipped.Contains(def.RouteB) ? 1 : 0, outputs.Count);
                        }
                    }
        }

        [Fact]
        public void Wire_roundtrip_reconstructs_canonical_definitions_clamps_and_rejects_invalid_or_duplicate_entries()
        {
            var build = new Build();
            foreach (var def in PairCombos.All) build.PairCombos.Add(Entry(def, 3));
            build.Stats[Stat.Armor] = 17;
            var decoded = Build.Decode(build.Encode());
            Assert.Equal(17, decoded.Get(Stat.Armor));
            Assert.Equal(PairCombos.All.Select(d => d.Id), decoded.PairCombos.Select(e => e.Def.Id));
            Assert.All(decoded.PairCombos, e => { Assert.Equal(3, e.Ranks); Assert.Same(PairCombos.Get(e.Def.Id), e.Def); });
            var defA = Def("Vesper", 1); var defB = Def("Mist", 5);
            decoded = Build.Decode("c:unknown:3," + defA.Id + ":0," + defA.Id + ":-1," + defA.Id + ":bogus," + defA.Id + ":1:999;c:");
            Assert.NotNull(decoded); Assert.Empty(decoded.PairCombos);
            decoded = Build.Decode("c:" + defA.Id + ":2147483647;c:;c:" + defA.Id + ":1," + defB.Id + ":2");
            Assert.Equal(new[] { 3, 2 }, decoded.PairCombos.Select(e => e.Ranks));
            Assert.Null(Build.Decode(new string('x', 16385)));
            build.PairCombos.Add(Entry(defA, 1));
            Assert.Equal(62, Build.Decode(build.Encode()).PairCombos.Count);
        }

        public static IEnumerable<object[]> GuardedDefinitions =>
            PairCombos.All.Where(d => d.OncePerActivation).Select(d => new object[] { d.Id });

        [Theory]
        [MemberData(nameof(GuardedDefinitions))]
        public void One_activation_pays_once_across_hits_victims_and_interleaved_activations(string id)
        {
            var def = PairCombos.Get(id); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def)); var first = new object(); var second = new object();
            Start(runtime, def, equipped, victim: 10);
            Start(runtime, def, equipped, victim: 11);
            Start(runtime, def, equipped, victim: 12);
            Assert.Single(Pay(runtime, def, equipped, now: 0.1f, victim: 10, activation: first));
            Assert.Empty(Pay(runtime, def, equipped, now: 0.2f, victim: 11, activation: first));
            Assert.Single(Pay(runtime, def, equipped, now: 0.2f, victim: 12, activation: second));
            Start(runtime, def, equipped, now: 2, victim: 13);
            runtime.SetBuild(new[] { Entry(def, 3) });
            Assert.Empty(Pay(runtime, def, equipped, now: 2.1f, victim: 13, activation: first));
            Start(runtime, def, equipped, now: 2.1f, victim: 14);
            Assert.Single(Pay(runtime, def, equipped, now: 2.1f, victim: 14, activation: new object()));
        }

        [Theory]
        [MemberData(nameof(GuardedDefinitions))]
        public void Guard_requires_activation_identity_and_generated_hits_do_not_consume_it(string id)
        {
            var def = PairCombos.Get(id); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def)); var activation = new object();
            Start(runtime, def, equipped);
            Assert.Empty(Fire(runtime, def.PayoffTrigger, def.PayoffMemory, 0.1f, 10, equipped,
                hitKind: def.PayoffHitKind));
            Start(runtime, def, equipped, now: 0.1f, victim: 11);
            Assert.Empty(Pay(runtime, def, equipped, now: 0.2f, victim: 11, generated: true, activation: activation));
            Assert.Single(Pay(runtime, def, equipped, now: 0.2f, victim: 11, activation: activation));
        }

        [Theory]
        [InlineData("Vesper", 6, PairComboHitKind.InitialExplosion)]
        [InlineData("Vesper", 7, PairComboHitKind.InitialExplosion)]
        [InlineData("Cetus", 2, PairComboHitKind.TerminalExplosion)]
        public void Explosion_payoffs_reject_periodic_damage_and_wrong_explosion_phase(
            string hero, int bridge, PairComboHitKind phase)
        {
            var def = Def(hero, bridge); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def)); var activation = new object();
            Start(runtime, def, equipped);
            Assert.Empty(Fire(runtime, def.PayoffTrigger, def.PayoffMemory, 0.1f, 10, equipped, activation: activation));
            var wrong = phase == PairComboHitKind.InitialExplosion
                ? PairComboHitKind.TerminalExplosion : PairComboHitKind.InitialExplosion;
            Assert.Empty(Fire(runtime, def.PayoffTrigger, def.PayoffMemory, 0.2f, 10, equipped,
                activation: activation, hitKind: wrong));
            Assert.Single(Fire(runtime, def.PayoffTrigger, def.PayoffMemory, 0.2f, 10, equipped,
                activation: activation, hitKind: phase));
            Assert.Empty(Fire(runtime, def.PayoffTrigger, def.PayoffMemory, 0.3f, 10, equipped,
                activation: activation, hitKind: phase));
        }

        [Theory]
        [InlineData("Yubar", 2)]
        [InlineData("Nachia", 6)]
        public void Ultimate_windows_last_twelve_seconds_and_refresh_without_stat_requests(string hero, int bridge)
        {
            var def = Def(hero, bridge); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def));
            Assert.Empty(Fire(runtime, def.Trigger, def.TriggerMemory, 0, 0, equipped));
            Assert.Single(Pay(runtime, def, equipped, now: 11.99f));
            Assert.Empty(Pay(runtime, def, equipped, now: 12));
            Start(runtime, def, equipped, now: 12);
            Assert.Single(Pay(runtime, def, equipped, now: 23.99f));
            Assert.Empty(Pay(runtime, def, equipped, now: 24));
        }

        [Fact]
        public void En_garde_heals_once_per_marked_enemy_without_resetting_on_mark_refresh()
        {
            var def = Def("Mist", 1); var equipped = Equipped(def);
            var runtime = Runtime(Entry(def));
            Start(runtime, def, equipped);
            Assert.Single(Pay(runtime, def, equipped, now: 0.1f));
            Assert.Empty(Pay(runtime, def, equipped, now: 0.2f));
            Start(runtime, def, equipped, now: 1);
            runtime.SetBuild(new[] { Entry(def, 3) });
            Assert.Empty(Pay(runtime, def, equipped, now: 1.1f));
            Start(runtime, def, equipped, now: 1.2f, victim: 11);
            Assert.Single(Pay(runtime, def, equipped, now: 1.3f, victim: 11));
            Start(runtime, def, equipped, now: 5);
            Assert.Single(Pay(runtime, def, equipped, now: 5.1f));
        }

        [Theory]
        [InlineData("Vesper", 1, "燃え移る会心", "Kindling Crit", GimmickEffect.Element)]
        [InlineData("Vesper", 2, "陽光の継ぎ足し", "Sunlight Carry-Over", GimmickEffect.Recharge)]
        [InlineData("Cetus", 1, "凍土の呼び戻し", "Frozen Ground Recall", GimmickEffect.Recharge)]
        [InlineData("Cetus", 5, "氷塊に呼ぶ雷", "Thunder Called to Ice", GimmickEffect.Recharge)]
        [InlineData("Yubar", 1, "物質の呼び水", "Matter's Lure", GimmickEffect.Recharge)]
        [InlineData("Yubar", 5, "星の目印の再装填", "Marked-Star Reload", GimmickEffect.Recharge)]
        [InlineData("Yubar", 6, "凪の光", "Calm Light", GimmickEffect.Element)]
        [InlineData("Yubar", 7, "静かな爆ぜ", "Quiet Blast", GimmickEffect.Burst)]
        [InlineData("Mist", 2, "覚醒の光突き", "Awakened Light Thrust", GimmickEffect.Element)]
        [InlineData("Mist", 5, "追い立ての突き", "Driving Thrust", GimmickEffect.Burst)]
        [InlineData("Mist", 7, "返しの構え", "Riposte Stance", GimmickEffect.Recharge)]
        [InlineData("Nachia", 1, "群れの呼び声", "Pack's Call", GimmickEffect.Recharge)]
        [InlineData("Aurena", 2, "理論の黄金光", "Golden Light of Theory", GimmickEffect.Element)]
        [InlineData("Aurena", 6, "陣上の金片", "Shards on the Circle", GimmickEffect.Echo)]
        [InlineData("Bismuth", 4, "炎剣の呼び戻し", "Flame-Blade Recall", GimmickEffect.Recharge)]
        [InlineData("Bismuth", 5, "矢の癒やし", "Arrow Mending", GimmickEffect.Heal)]
        public void Revised_names_and_effects_match_the_design(string hero, int bridge, string ja, string en, GimmickEffect effect)
        {
            var def = Def(hero, bridge);
            Assert.Equal(ja, def.Name.Ja);
            Assert.Equal(en, def.Name.En);
            Assert.Equal(effect, def.Effect);
        }

        [Fact]
        public void Zone_transition_clears_marks_and_windows_but_keeps_the_native_interval()
        {
            var marked = Def("Vesper", 2); var equipped = Equipped(marked);
            var runtime = Runtime(Entry(marked));
            Start(runtime, marked, equipped);
            runtime.ClearTransient();
            Assert.Empty(Pay(runtime, marked, equipped, now: 0.1f));
            Start(runtime, marked, equipped, now: 0.2f);
            Assert.Single(Pay(runtime, marked, equipped, now: 0.3f));

            var window = Def("Nachia", 2); equipped = Equipped(window);
            runtime = Runtime(Entry(window));
            Start(runtime, window, equipped);
            Assert.Single(Pay(runtime, window, equipped, now: 0.1f));
            runtime.ClearTransient();
            Assert.Empty(Pay(runtime, window, equipped, now: 2f));
            Start(runtime, window, equipped, now: 0.2f);
            Assert.Empty(Pay(runtime, window, equipped, now: 0.3f));
            Assert.Single(Pay(runtime, window, equipped, now: 2f));
        }

    }
}

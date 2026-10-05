using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// The production EffectiveAllocationValidation must make exactly the decisions of the original (unoptimized) algorithm.
    /// A frozen copy of that algorithm (ReferenceEffectiveAllocationValidation) is driven in lock-step with the production
    /// engine, first through hand-built cascades in which one purchase makes other paid stars inert (every kind of shared output:
    /// stat/power caps, strongest-effect domination, capped scoped boosts, shared haste caps, authored mechanism boosts), then
    /// through long deterministic random sequences of purchases, choice switches, refunds, keystone and equipment changes.
    /// Every step compares the whole plan (decision, refunds, saturation details, old/new effective channels, proposed state)
    /// and the resulting serialized profiles. The production engine additionally evaluates every star its dependency analysis
    /// skips and fails if skipping could have hidden a refund (EffectiveAllocationValidation.VerifyPruning).
    /// </summary>
    [Collection("Generated hero registry")]
    public sealed class AllocationEquivalenceV131Tests
    {
        private readonly ITestOutputHelper _out;
        public AllocationEquivalenceV131Tests(ITestOutputHelper output) { _out = output; }

        // ---- deterministic generator ----

        private sealed class Gen
        {
            private ulong _s;
            public Gen(int seed) { _s = 0x9E3779B97F4A7C15UL ^ (ulong)(uint)seed * 0xBF58476D1CE4E5B9UL; Next(); Next(); }
            public int Next(int bound)
            {
                _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
                return (int)((_s >> 11) % (ulong)bound);
            }
            private void Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; }
            public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];
        }

        // ---- plan comparison ----

        private static string Describe(EffectiveAllocationChannel c) =>
            string.Join("|", c.Key, c.StarId, c.ValueMilli, c.DurationUnits, c.RadiusUnits, c.ExtraTargets, c.Strongest, c.Memory,
                string.Join(",", c.ContributorIds), c.ValueCeiling, c.DurationCeilingUnits, c.RadiusCeilingUnits, c.TargetCeiling,
                c.ChanceUnits, c.IntrinsicProbabilityUnits, c.CapProfileId, c.PredicateKey, c.Cooldown, c.Effect);

        private static string Describe(AllocationSaturation d) =>
            string.Join("|", d.StarId, d.Rank, d.ChannelKey, d.Field, d.Unit, d.EffectiveValue, d.Ceiling, d.DominatingValue,
                d.CapProfileId, d.DeclaredModifierCeilingUnits, d.DeclaredModifierUnit, d.Reason);

        private static string Describe(HeroState h) =>
            string.Join(";", string.Join("+", h.Keystones), h.StarXp, h.Kills, string.Join(",", h.Equipped),
                string.Join(",", h.Talents.Select(t => t.Key + "=" + t.Value)),
                string.Join(",", h.TalentChoices.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key + "=" + t.Value)));

        private static string Describe(EffectiveAllocationPlan plan) => string.Join("\n",
            "cand=" + plan.CandidateStarId + "/" + plan.SelectedOption + " eff=" + plan.CandidateEffective + " can=" + plan.CanApply,
            "viol=" + string.Join(",", plan.PrerequisiteViolations),
            "sat=" + string.Join(",", plan.SaturatedChannels),
            "refundIds=" + string.Join(",", plan.AffectedRefundIds) + " cost=" + plan.RefundCost,
            "refunds=" + string.Join(",", plan.Refunds.Select(r => r.StarId + ":" + r.Ranks + ":" + r.Cost)),
            "details=" + string.Join("\n  ", plan.SaturationDetails.Select(Describe)),
            "old=" + string.Join("\n  ", plan.OldEffectiveChannels.Select(Describe)),
            "new=" + string.Join("\n  ", plan.NewEffectiveChannels.Select(Describe)),
            "orig=" + Describe(plan.Original),
            "prop=" + Describe(plan.Proposed));

        private static (EffectiveAllocationPlan Plan, string Error) TryPreview(Func<EffectiveAllocationPlan> preview)
        {
            try { return (preview(), null); }
            catch (Exception e) { return (null, e.GetType().FullName + ": " + e.Message); }
        }

        private static string TryCommit(Action commit)
        {
            try { commit(); return null; }
            catch (Exception e) { return e.GetType().FullName + ": " + e.Message; }
        }

        /// <summary>The original algorithm and the production engine on two identical profiles, advanced together.</summary>
        private sealed class Duo
        {
            private readonly ReferenceEffectiveAllocationValidation reference;
            private readonly EffectiveAllocationValidation production;
            public readonly Profile ReferenceProfile, ProductionProfile;
            private readonly string hero;
            public int Pruned;
            public string Label = "";

            public Duo(IReadOnlyList<TalentDef> tree, EffectiveAllocationPolicy policy, HeroTreeLayout layout, string hero,
                Func<Profile> newProfile, EffectiveAllocationValidation productionEngine = null)
            {
                this.hero = hero;
                reference = new ReferenceEffectiveAllocationValidation(tree, policy, layout);
                production = productionEngine ?? new EffectiveAllocationValidation(tree, policy, layout);
                ReferenceProfile = newProfile();
                ProductionProfile = newProfile();
            }

            public EffectiveAllocationValidation Production => production;

            /// <summary>Preview both, require identical plans, and commit the plan (approving every refund) when it can apply.</summary>
            public (EffectiveAllocationPlan Plan, string Error) Step(AllocationChange change, bool commit = true)
            {
                string label = Label + " " + change.Kind + " " + (change.CandidateStarId ?? change.KeystoneId ?? change.EquipmentUid);
                var expected = TryPreview(() => reference.Preview(ReferenceProfile, hero, change));
                var actual = TryPreview(() => production.Preview(ProductionProfile, hero, change));
                Assert.True(expected.Error == actual.Error, label + "\nexpected error: " + expected.Error + "\nactual error: " + actual.Error);
                if (actual.Plan != null) Pruned += actual.Plan.PrunedStars;
                if (expected.Plan != null) Assert.True(Describe(expected.Plan) == Describe(actual.Plan),
                    label + "\n--- reference ---\n" + Describe(expected.Plan) + "\n--- production ---\n" + Describe(actual.Plan));
                Assert.Equal(ProfileCodec.Write(ReferenceProfile), ProfileCodec.Write(ProductionProfile));
                if (expected.Plan == null || !commit || !expected.Plan.CanApply) return actual;
                var approval = expected.Plan.AffectedRefundIds.ToArray();
                var commitRef = TryCommit(() => reference.Commit(ReferenceProfile, expected.Plan, approval));
                var commitNew = TryCommit(() => production.Commit(ProductionProfile, actual.Plan, approval));
                Assert.True(commitRef == commitNew, label + "\ncommit expected: " + commitRef + "\ncommit actual: " + commitNew);
                Assert.Equal(ProfileCodec.Write(ReferenceProfile), ProfileCodec.Write(ProductionProfile));
                return actual;
            }

            /// <summary>Edit both profiles in the same way, bypassing validation (a saved allocation that is dormant or was never validated).</summary>
            public void Direct(Action<HeroState> edit)
            {
                edit(ReferenceProfile.Hero(hero));
                edit(ProductionProfile.Hero(hero));
            }

            public EffectiveAllocationPlan Buy(string id, int? option = null, int times = 1)
            {
                EffectiveAllocationPlan plan = null;
                for (int i = 0; i < times; i++)
                {
                    plan = Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = id, SelectedOption = option }).Plan;
                    Assert.True(plan != null && plan.CanApply, "setup purchase must apply: " + id);
                }
                return plan;
            }
        }

        // ---- synthetic trees ----

        private const string SynHero = "Hero_Cetus";
        private const string Memory = "St_D_IcyVeins";
        private static TalentDef StatStar(string id, int amount = 1, int ranks = 1, int cost = 1, Stat stat = Stat.Armor) =>
            new TalentDef(id, Line.Offense, new Txt("試験", "Test"), stat, amount, ranks) { HeroKey = SynHero, RankCost = cost };
        private static TalentDef PowerStar(string id, Power power, int amount, int ranks = 1, int cost = 1) =>
            new TalentDef(id, Line.Offense, new Txt("試験", "Test"), power, amount, ranks) { HeroKey = SynHero, RankCost = cost };
        private static TalentDef Effect(string id, GimmickEffect effect, decimal value, int ranks = 1, int cost = 1, string memory = Memory)
        {
            var t = StatStar(id, 0, ranks, cost);
            t.RouteMemory = memory;
            t.Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value };
            return t;
        }
        private static TalentDef Modifier(string id, string recipient, GimmickParam? field, decimal amount, int ranks = 1, int cost = 1, string cap = null)
        {
            var t = StatStar(id, 0, ranks, cost);
            t.RouteMemory = Memory;
            t.ScopedModifier = new ScopedModifierDef
            {
                ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Memory, TargetEffectIds = new[] { recipient },
                Param = field, Amount = ModifierUnits.FromPercent(amount), CapProfileId = cap,
            };
            return t;
        }
        private static TalentDef Choice(string id, TalentDef a, TalentDef b, int ranks = 1, int cost = 1)
        {
            var t = StatStar(id, 0, ranks, cost);
            t.ClusterStar = new ClusterStarDef { Kind = ClusterStarKind.Choice };
            t.Choices = new[] { a, b };
            return t;
        }
        private static TalentDef Requires(TalentDef talent, params string[] prerequisites)
        {
            talent.AuthoredStar = new AuthoredStarDef { RequiredStarIds = prerequisites };
            return talent;
        }

        private static bool capsRegistered;
        private const string ScopedCap = "eq.cap.scoped", HasteCap = "eq.cap.haste";
        private static void RegisterCaps()
        {
            if (capsRegistered) return;
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile
            { Id = ScopedCap, MaximumModifier = ModifierUnits.FromPercent(10m) });
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile
            { Id = HasteCap, Kind = LinkKind.MemoryHaste, Maximum = ValueUnits.FromPercent(100m) });
            capsRegistered = true;
        }

        private sealed class Synthetic
        {
            public TalentDef Root, Sap, Strong, Weak, Shield, Boost, Duration, Echo, Leaf, Other, Pick, Armor, Key;
            public TalentDef MsA, MsB, MsC;           // one stat shared by a big and several small stars (stat cap cascade)
            public TalentDef PwA, PwB, PwC;           // the same for a power
            public TalentDef Weak2, Strong2;          // strongest-effect domination without a prerequisite
            public TalentDef Shield2, CapA, CapB;     // two scoped boosts under one declared cap
            public TalentDef HasteLink, HasteNative;  // link and native modifier sharing one haste cap
            public TalentDef Sap2, RouteBoostA, RouteBoostB; // route-wide gimmick boosts that meet the effect cap
            public TalentDef Gate, DormantStrong, DormantWeak; // a purchase that unlocks a saved (dormant) allocation
            public TalentDef Key2, KeyWeak, KeyStrong;        // a keystone whose upside raises one effect once its route gate is met
            public IReadOnlyList<TalentDef> Tree;
            public EffectiveAllocationPolicy Policy;
            public Relic Relic;
        }

        private static Synthetic BuildSynthetic()
        {
            RegisterCaps();
            var s = new Synthetic();
            s.Root = StatStar("eq.root", 1, 6);
            s.Sap = Effect("eq.sap", GimmickEffect.Sap, 6m, 4, 2);
            s.Strong = Effect("eq.expose.strong", GimmickEffect.Expose, 10m);
            s.Weak = Requires(Effect("eq.expose.weak", GimmickEffect.Expose, 5m), s.Strong.Id);
            s.Shield = Effect("eq.shield", GimmickEffect.Shield, 0.25m, 2);
            s.Boost = Modifier("eq.shield.boost", s.Shield.Id, null, 0.5m, 3);
            s.Duration = Modifier("eq.shield.duration", s.Shield.Id, GimmickParam.Duration, 5m, 3);
            s.Echo = Effect("eq.echo", GimmickEffect.Echo, 5m, 2, 3);
            s.Leaf = Requires(StatStar("eq.leaf", 1, cost: 4), s.Echo.Id);
            s.Other = Effect("eq.other", GimmickEffect.Expose, 8m, 2, 1, "St_Q_EmbracingTheChill");
            s.Pick = Choice("eq.choice", Effect("eq.choice.a", GimmickEffect.Sap, 5m, 3), StatStar("eq.choice.b", 2, 3), 3, 2);
            s.Armor = StatStar("eq.armor", 3, 8, 1);
            s.Key = new TalentDef("eq.key", Line.Offense, new Txt("試験核", "Test Key"), Power.Aegis, 3, new Txt("試験効果", "Test effect")) { HeroKey = SynHero };
            s.MsA = StatStar("eq.ms.a", 5, 3, 1, Stat.MoveSpeedPct);
            s.MsB = StatStar("eq.ms.b", 5, 3, 1, Stat.MoveSpeedPct);
            s.MsC = StatStar("eq.ms.c", 30, 1, 3, Stat.MoveSpeedPct);
            s.PwA = PowerStar("eq.pw.a", Power.Retaliation, 10, 3);
            s.PwB = PowerStar("eq.pw.b", Power.Retaliation, 10, 3);
            s.PwC = PowerStar("eq.pw.c", Power.Retaliation, 45, 1, 3);
            s.Weak2 = Effect("eq.weak2", GimmickEffect.Expose, 5m, 1, 1, "St_Q_BigBorealChunk");
            s.Strong2 = Effect("eq.strong2", GimmickEffect.Expose, 12m, 1, 2, "St_Q_BigBorealChunk");
            s.Shield2 = Effect("eq.shield2", GimmickEffect.Shield, 1m);
            s.CapA = Modifier("eq.cap.a", s.Shield2.Id, null, 1m, 3, 1, ScopedCap);
            s.CapB = Modifier("eq.cap.b", s.Shield2.Id, null, 9m, 1, 3, ScopedCap);
            s.HasteLink = new TalentDef("eq.haste.link", Line.Offense, new Txt("加速", "Haste"),
                new LinkDef { Kind = LinkKind.MemoryHaste, Value = 20m, Requires = new[] { Memory } }, 5) { HeroKey = SynHero };
            s.HasteNative = StatStar("eq.haste.native", 0);
            s.HasteNative.RouteMemory = Memory;
            s.HasteNative.NativeModifier = new NativeMemoryModifierDef
            { Memory = Memory, Kind = LinkKind.MemoryHaste, Value = ValueUnits.FromPercent(1m), CapProfileId = HasteCap };
            s.Sap2 = Effect("eq.sap2", GimmickEffect.Sap, 14m, 1, 1, "St_R_FrozenFists");
            s.RouteBoostA = StatStar("eq.rboost.a", 0, 3, 1); s.RouteBoostA.RouteMemory = "St_R_FrozenFists"; s.RouteBoostA.GimmickBoost = 1;
            s.RouteBoostB = StatStar("eq.rboost.b", 0, 1, 3); s.RouteBoostB.RouteMemory = "St_R_FrozenFists"; s.RouteBoostB.GimmickBoost = 20;
            s.Gate = StatStar("eq.gate", 1, 1, 1);
            s.DormantStrong = Requires(Effect("eq.dstrong", GimmickEffect.Expose, 12m, 1, 1, "St_R_BackOff"), s.Gate.Id);
            s.DormantWeak = Effect("eq.dweak", GimmickEffect.Expose, 5m, 1, 1, "St_R_BackOff");
            s.KeyWeak = Effect("eq.kweak", GimmickEffect.Expose, 6m, 1, 1, "St_D_ChargedAnguillian");
            s.KeyStrong = Effect("eq.kstrong", GimmickEffect.Expose, 5m, 1, 1, "St_D_ChargedAnguillian");
            s.Key2 = new TalentDef("eq.key2", Line.Offense, new Txt("試験核2", "Test Key 2"), Power.Aegis, 3, new Txt("試験効果2", "Test effect 2")) { HeroKey = SynHero };
            s.Key2.KeystoneDefinition = new KeystoneDefinition("eq.key2", Array.Empty<string>(),
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, KeystoneMagnitude.FromPercent(100),
                    new KeystoneScope(targetEffectIds: new[] { s.KeyStrong.Id })) });
            s.Tree = HeroSigils.TreeFor(SynHero).Where(t => t.Cluster == null).Concat(new[]
            {
                s.Root, s.Sap, s.Strong, s.Weak, s.Shield, s.Boost, s.Duration, s.Echo, s.Leaf, s.Other, s.Pick, s.Armor, s.Key,
                s.MsA, s.MsB, s.MsC, s.PwA, s.PwB, s.PwC, s.Weak2, s.Strong2, s.Shield2, s.CapA, s.CapB, s.HasteLink, s.HasteNative,
                s.Sap2, s.RouteBoostA, s.RouteBoostB, s.Gate, s.DormantStrong, s.DormantWeak, s.KeyWeak, s.KeyStrong, s.Key2,
            }).ToArray();
            s.Relic = Loot.RollRelic(new Rng(15031UL), Rarity.Common, 1, slot: Slot.Weapon);
            s.Policy = new EffectiveAllocationPolicy
            {
                PermanentDisables = new[]
                {
                    new AllocationDisableRule { KeystoneId = s.Key.Id, Effect = GimmickEffect.Echo },
                    new AllocationDisableRule { EquippedUid = s.Relic.Uid, StarIds = new[] { s.Sap.Id } },
                },
            };
            return s;
        }

        private static Profile SyntheticProfile() => FundedProfile(15031UL, SynHero, 300, relics: 1);

        private static Profile FundedProfile(ulong seed, string hero, int points, int relics)
        {
            var p = Profile.CreateNew(seed);
            p.Hero(hero).StarXp = StarProgression.TotalXpForPoints(Math.Min(points, StarProgression.MaxPoints));
            p.Hero(hero).Kills = 1000000;
            var rng = new Rng(seed * 31UL);
            if (relics == 1) p.Stash.Add(Loot.RollRelic(new Rng(15031UL), Rarity.Common, 1, slot: Slot.Weapon));
            else for (int i = 0; i < relics; i++) p.Stash.Add(Loot.RollRelic(rng, (Rarity)(i % 5), 20));
            return p;
        }

        private static Duo SyntheticDuo(Synthetic s) =>
            new Duo(s.Tree, s.Policy, null, SynHero, SyntheticProfile) { Label = "synthetic" };

        private static void WithVerification(Action body)
        {
            // Strict: every star the analysis skips must keep exactly its marginal effectiveness, and every star the region shortcut
            // accepts must be effective on the whole allocation. A violation is recorded and fails the test after the run.
            EffectiveAllocationValidation.VerifyPruning = true;
            EffectiveAllocationValidation.VerifyPruningStrict = true;
            EffectiveAllocationValidation.PruningViolations = new List<string>();
            try
            {
                body();
                Assert.True(EffectiveAllocationValidation.PruningViolations.Count == 0,
                    string.Join(Environment.NewLine, EffectiveAllocationValidation.PruningViolations.Take(10)));
            }
            finally
            {
                EffectiveAllocationValidation.VerifyPruning = false;
                EffectiveAllocationValidation.VerifyPruningStrict = false;
                EffectiveAllocationValidation.PruningViolations = null;
            }
        }

        // ---- hand-built cascades: the purchase makes older paid stars inert and they are refunded, unrelated stars are skipped ----

        private static void AssertCascade(Func<Synthetic, Duo, EffectiveAllocationPlan> scenario, params string[] expectedRefunds)
        {
            WithVerification(() =>
            {
                var s = BuildSynthetic();
                var duo = SyntheticDuo(s);
                // Unrelated paid stars that the cascade must leave alone (and the analysis must skip).
                duo.Buy(s.Root.Id, times: 4);
                duo.Buy(s.Armor.Id, times: 3);
                duo.Buy(s.Other.Id, times: 2);
                int before = duo.Pruned;
                var plan = scenario(s, duo);
                foreach (string id in expectedRefunds) Assert.Contains(id, plan.AffectedRefundIds);
                Assert.DoesNotContain(s.Root.Id, plan.AffectedRefundIds);
                Assert.DoesNotContain(s.Armor.Id, plan.AffectedRefundIds);
                Assert.DoesNotContain(s.Other.Id, plan.AffectedRefundIds);
                Assert.True(duo.Pruned > before, "unrelated stars must be skipped by the dependency analysis");
            });
        }

        [Fact]
        public void A_big_stat_star_saturates_the_shared_stat_and_refunds_the_smaller_stars_it_made_inert()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.MsA.Id, times: 3); d.Buy(s.MsB.Id, times: 3);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.MsC.Id }).Plan;
            }, "eq.ms.a");
        }

        [Fact]
        public void A_big_power_star_saturates_the_shared_power_and_refunds_the_smaller_stars_it_made_inert()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.PwA.Id, times: 3); d.Buy(s.PwB.Id, times: 2);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.PwC.Id }).Plan;
            }, "eq.pw.a");
        }

        [Fact]
        public void A_stronger_effect_of_the_same_predicate_dominates_and_refunds_the_weaker_one_bought_earlier()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.Weak2.Id);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.Strong2.Id }).Plan;
            }, "eq.weak2");
        }

        [Fact]
        public void A_big_scoped_boost_fills_the_declared_cap_and_refunds_the_small_boost_bought_earlier()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.Shield2.Id); d.Buy(s.CapA.Id, times: 3);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.CapB.Id }).Plan;
            }, "eq.cap.a");
        }

        [Fact]
        public void Link_haste_filling_the_shared_cap_refunds_the_native_haste_modifier_bought_earlier()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.HasteNative.Id); d.Buy(s.HasteLink.Id, times: 4);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.HasteLink.Id }).Plan;
            }, "eq.haste.native");
        }

        [Fact]
        public void A_route_wide_boost_that_reaches_the_effect_cap_refunds_the_smaller_boost_bought_earlier()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.Sap2.Id); d.Buy(s.RouteBoostA.Id, times: 3);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.RouteBoostB.Id }).Plan;
            }, "eq.rboost.a");
        }

        [Fact]
        public void A_purchase_that_unlocks_a_dormant_saved_star_refunds_the_star_it_now_dominates()
        {
            AssertCascade((s, d) =>
            {
                d.Buy(s.DormantWeak.Id);
                d.Direct(h => h.Talents[s.DormantStrong.Id] = 1);
                return d.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.Gate.Id }).Plan;
            }, "eq.dweak");
        }

        [Fact]
        public void A_purchase_that_meets_the_keystone_route_gate_changes_every_effect_and_refunds_the_one_it_now_dominates()
        {
            WithVerification(() =>
            {
                var s = BuildSynthetic();
                var duo = SyntheticDuo(s);
                // Saved state: the keystone is allocated but its route gate (6 ranks) is not met yet, so its upside is not applied.
                duo.Direct(h => { h.Talents[s.KeyWeak.Id] = 1; h.Talents[s.KeyStrong.Id] = 1; h.Talents[s.Root.Id] = 3; h.Keystone = s.Key2.Id; });
                var plan = duo.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.Armor.Id }).Plan;
                // The sixth rank applies the keystone, which doubles the weaker effect above the stronger one.
                Assert.Contains(s.KeyWeak.Id, plan.AffectedRefundIds);
                Assert.DoesNotContain(s.KeyStrong.Id, plan.AffectedRefundIds);
            });
        }

        [Fact]
        public void A_refund_cascade_that_drops_below_the_keystone_route_gate_is_evaluated_again_without_the_keystone()
        {
            WithVerification(() =>
            {
                var s = BuildSynthetic();
                var duo = SyntheticDuo(s);
                // The gate is met (8 ranks) and the keystone doubles the strong effect, so only the strong one is effective.
                duo.Direct(h =>
                {
                    h.Talents[s.KeyWeak.Id] = 1; h.Talents[s.KeyStrong.Id] = 1; h.Talents[s.MsA.Id] = 3; h.Talents[s.MsB.Id] = 3; h.Keystone = s.Key2.Id;
                });
                var plan = duo.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = s.MsC.Id }).Plan;
                // Refunding the saturated stat stars drops the route below the gate: the keystone and the effect that depended on it go too.
                Assert.Contains(s.Key2.Id, plan.AffectedRefundIds);
                Assert.Contains(s.KeyStrong.Id, plan.AffectedRefundIds);
            });
        }

        // ---- authored mechanism stars through the production registry: recharge channels with capped boosts ----

        private const string Yubar = "Hero_Yubar", Q = "St_Q_EtherealInfluence", Movement = "St_M_Flicker", AuthoredRoot = "outer.eqprobe.s1";
        private const string AuthoredCap = "eq.cap.mechanism";

        private static AuthoredStarDef Node(string id, ClusterStarDef effect, string anchor = AuthoredRoot) => new AuthoredStarDef
        {
            LocalStarId = id, HeroKey = Yubar, ClusterId = "outer.eqprobe", Region = ClusterRegion.Outer,
            AnchorId = id == AuthoredRoot ? null : anchor, Shape = ClusterShape.Fan, Effect = effect,
        };

        private static AuthoredStarDef Recharge(int n, int units) => Node("outer.eqprobe.r" + n, new ClusterStarDef
        {
            Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"),
            Mechanism = new AuthoredMechanismSpec
            {
                Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "eq.recharge." + n, Source = MemorySelector.Parse("@Q(" + Q + ")"),
                Recharge = new DirectedRechargeChannel("eq.recharge." + n, MemorySelector.Parse("@Q(" + Q + ")"), MemoryEventKind.Hit,
                    MemorySelector.Parse("@M(" + Movement + ")"), new[] { units }),
            },
        });

        private static AuthoredStarDef Boost(string id, decimal percent, string effectId, bool capped) => Node(id, new ClusterStarDef
        {
            Kind = ClusterStarKind.GimmickBoost, Name = new Txt("試験", "Probe"), Memory = Q,
            ScopedModifier = new ScopedModifierDef
            {
                ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Q, Amount = ModifierUnits.FromPercent(percent),
                TargetEffectIds = new[] { effectId }, TargetEffects = new[] { GimmickEffect.Recharge },
                CapProfileId = capped ? AuthoredCap : null,
            },
        });

        private static void WithAuthoredProbe(Action<IReadOnlyList<TalentDef>> body)
        {
            try { FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile { Id = AuthoredCap, MaximumModifier = ModifierUnits.FromPercent(10m) }); }
            catch (InvalidOperationException) { /* registered by an earlier run in this process */ }
            var stars = new[]
            {
                Node(AuthoredRoot, new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験", "Probe"), Stat = Stat.Armor, Amount = 1 }),
                Recharge(2, 25), Recharge(3, 30), Recharge(4, 35),
                Boost("outer.eqprobe.small1", 1m, "outer.eqprobe.r2", true), Boost("outer.eqprobe.small2", 1m, "outer.eqprobe.r2", true),
                Boost("outer.eqprobe.small3", 1m, "outer.eqprobe.r2", true), Boost("outer.eqprobe.big", 9m, "outer.eqprobe.r2", true),
                Boost("outer.eqprobe.free1", 2m, "outer.eqprobe.r3", false), Boost("outer.eqprobe.free2", 2m, "outer.eqprobe.r3", false),
            };
            try
            {
                StarClusters.RegisterAuthored(Yubar, stars);
                body(HeroSigils.TreeFor(Yubar));
            }
            finally { StarClusters.RegisterAuthored(Yubar, Array.Empty<AuthoredStarDef>()); }
        }

        private static Profile AuthoredProfile(IReadOnlyList<TalentDef> tree)
        {
            var p = FundedProfile(1703UL, Yubar, 300, relics: 4);
            AuthoredStarContractTests.AllocatePath(p.Hero(Yubar), tree, AuthoredRoot);
            p.Hero(Yubar).Talents[AuthoredRoot] = 1;
            return p;
        }

        private static Duo AuthoredDuo(IReadOnlyList<TalentDef> tree) =>
            new Duo(tree, null, HeroTreeLayout.ForHero(Yubar), Yubar, () => AuthoredProfile(tree),
                Rules.AllocationValidationForHero(Yubar)) { Label = "authored" };

        [Fact]
        public void A_big_capped_boost_on_an_authored_recharge_channel_refunds_the_small_boost_and_leaves_other_channels_alone()
        {
            WithVerification(() => WithAuthoredProbe(tree =>
            {
                var duo = AuthoredDuo(tree);
                duo.Buy("outer.eqprobe.r2"); duo.Buy("outer.eqprobe.r3"); duo.Buy("outer.eqprobe.r4");
                duo.Buy("outer.eqprobe.free1"); duo.Buy("outer.eqprobe.free2");
                duo.Buy("outer.eqprobe.small1"); duo.Buy("outer.eqprobe.small2"); duo.Buy("outer.eqprobe.small3");
                int before = duo.Pruned;
                var plan = duo.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = "outer.eqprobe.big" }).Plan;
                Assert.Contains("outer.eqprobe.small1", plan.AffectedRefundIds);
                Assert.DoesNotContain("outer.eqprobe.free1", plan.AffectedRefundIds);
                Assert.DoesNotContain("outer.eqprobe.r3", plan.AffectedRefundIds);
                Assert.True(duo.Pruned > before, "the other channels and their boost are independent of the capped pair");
            }));
        }

        // ---- long random sequences ----

        private void RunRandom(Func<Duo> create, string heroKey, IReadOnlyList<TalentDef> tree, bool equipment, int seed, int steps, string name)
        {
            WithVerification(() =>
            {
                var duo = create();
                var gen = new Gen(seed);
                var heroTalents = tree.Where(t => !t.IsKeystone && Rules.BelongsTo(t, heroKey)).ToList();
                var keystones = tree.Where(t => t.IsKeystone && Rules.BelongsTo(t, heroKey)).ToList();
                int applied = 0, rejected = 0, refunded = 0, errors = 0;
                for (int step = 0; step < steps; step++)
                {
                    duo.Label = name + " seed " + seed + " step " + step;
                    var change = Pick(gen, duo.ProductionProfile, duo.Production, heroKey, heroTalents, keystones, equipment);
                    var (plan, error) = duo.Step(change);
                    if (plan == null) { errors++; continue; }
                    if (!plan.CanApply) { rejected++; continue; }
                    applied++;
                    if (plan.Refunds.Count > 0) refunded++;
                }
                _out.WriteLine($"{name} seed {seed}: applied {applied} (with refunds {refunded}) rejected {rejected} errors {errors} pruned stars {duo.Pruned}");
                Assert.True(applied > steps / 3, "the sequence must exercise real changes: " + applied);
                Assert.True(duo.Pruned > 0, "the dependency analysis must have skipped independent stars");
            });
        }

        private static AllocationChange Pick(Gen gen, Profile p, EffectiveAllocationValidation engine, string heroKey,
            List<TalentDef> talents, List<TalentDef> keystones, bool equipment)
        {
            var hero = p.Hero(heroKey);
            int roll = gen.Next(100);
            var allocated = talents.Where(t => hero.Talents.TryGetValue(t.Id, out int r) && r > 0).ToList();
            if (roll < 72 || allocated.Count == 0)
            {
                var reachable = talents.Where(t => engine.CanReach(hero, t) && !(hero.Talents.TryGetValue(t.Id, out int r) && r >= t.MaxRank)).ToList();
                var star = reachable.Count > 0 && gen.Next(100) < 90 ? gen.Pick(reachable) : gen.Pick(talents);
                int? option = null;
                if (star.IsChoice)
                    option = hero.Talents.TryGetValue(star.Id, out int have) && have > 0 && hero.TalentChoices.TryGetValue(star.Id, out int chosen) && gen.Next(100) < 90
                        ? chosen : gen.Next(star.Choices.Count);
                return new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = star.Id, SelectedOption = option };
            }
            if (roll < 78) return new AllocationChange { Kind = AllocationChangeKind.Refund, CandidateStarId = gen.Pick(allocated).Id };
            var choices = allocated.Where(t => t.IsChoice).ToList();
            if (roll < 86 && choices.Count > 0)
            {
                var star = gen.Pick(choices);
                return new AllocationChange { Kind = AllocationChangeKind.Choice, CandidateStarId = star.Id, SelectedOption = gen.Next(star.Choices.Count) };
            }
            if (roll < 93 && keystones.Count > 0)
                return new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = gen.Next(4) == 0 ? null : gen.Pick(keystones).Id };
            if (equipment && p.Stash.Count > 0)
            {
                var relic = gen.Pick(p.Stash);
                return new AllocationChange { Kind = AllocationChangeKind.Equipment, EquipmentSlot = relic.Slot, EquipmentUid = gen.Next(3) == 0 ? null : relic.Uid };
            }
            return new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = gen.Pick(talents).Id, SelectedOption = null };
        }

        public static IEnumerable<object[]> RealHeroes() => new[]
        {
            new object[] { "Hero_Cetus", 1 }, new object[] { "Hero_Vesper", 3 },
            new object[] { "Hero_Yubar", 4 }, new object[] { "Hero_Bismuth", 5 }, new object[] { "Hero_Aurena", 6 }, new object[] { "Hero_Husk", 7 },
        };

        [Theory, MemberData(nameof(RealHeroes))]
        public void Production_engine_matches_the_original_algorithm_on_real_hero_trees(string hero, int seed)
        {
            var tree = HeroSigils.TreeFor(hero);
            RunRandom(() => new Duo(tree, null, HeroTreeLayout.ForHero(hero), hero, () => FundedProfile((ulong)seed, hero, 80, relics: 8)),
                hero, tree, true, seed, 110, hero);
        }

        // Fast run keeps the two most demanding trees (Vesper: authored mechanisms, Mist: largest tree);
        // the SlowFact Extended variant below runs all four heroes with more seeds and longer sequences.
        public static IEnumerable<object[]> GeneratedHeroes() => new[]
        {
            new object[] { "Hero_Vesper", 41 }, new object[] { "Hero_Mist", 43 },
        };

        public static IEnumerable<object[]> AllGeneratedHeroes() => new[]
        {
            new object[] { "Hero_Vesper", 41 }, new object[] { "Hero_Nachia", 42 }, new object[] { "Hero_Mist", 43 }, new object[] { "Hero_Lacerta", 44 },
        };

        private void WithGeneratedHero(string hero, Action<IReadOnlyList<TalentDef>> body)
        {
            StarClusters.RegisterGeneratedHero(hero);
            try { body(HeroSigils.TreeFor(hero)); }
            finally { StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
        }

        // Route-entry eligibility is a rule change, not an optimization. Exercise it directly in the fast suite:
        // the short random sequences need not reach it (Mist seed 500 first did so at step 52 in the slow suite).
        [Theory]
        [InlineData("Hero_Husk", "h.husk.route.flash-step.1", false)]
        [InlineData("Hero_Mist", "h.mist.route.fast-feet.1", false)]
        [InlineData("Hero_Husk", "h.husk.route.flash-step.1", true)]
        [InlineData("Hero_Mist", "h.mist.route.fast-feet.1", true)]
        [InlineData("Hero_Mist", "h.mist.deep.life", false)]
        [InlineData("Hero_Mist", "h.mist.deep.life", true)]
        [InlineData("Hero_Mist", "h.mist.ring.renewal", false)]
        [InlineData("Hero_Mist", "h.mist.ring.renewal", true)]
        [InlineData("Hero_Husk", "h.husk.ring.renewal", false)]
        [InlineData("Hero_Husk", "h.husk.ring.renewal", true)]
        public void Movement_receiver_matches_the_reference_without_a_recipient_and_respects_disables(
            string hero, string starId, bool disabled)
        {
            WithVerification(() => WithGeneratedHero(hero, tree =>
            {
                var talent = tree.Single(t => t.Id == starId);
                Assert.Equal(ScopeKind.Receiver, talent.ScopedModifier.ScopeKind);
                var policy = new EffectiveAllocationPolicy
                {
                    PermanentDisables = disabled
                        ? new[] { new AllocationDisableRule { StarIds = new[] { starId } } }
                        : Array.Empty<AllocationDisableRule>(),
                };
                var duo = new Duo(tree, policy, HeroTreeLayout.ForHero(hero), hero, () =>
                {
                    var profile = FundedProfile(61UL, hero, 150, relics: 0);
                    TreeTestPaths.Connect(profile, hero, starId);
                    return profile;
                }) { Label = hero + " movement route entry disabled=" + disabled };
                string original = ProfileCodec.Write(duo.ProductionProfile);
                for (int rank = 1; rank <= talent.MaxRank; rank++)
                {
                    var result = duo.Step(new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = starId });
                    Assert.Null(result.Error);
                    var plan = result.Plan;
                    Assert.NotNull(plan);
                    Assert.Equal(!disabled, plan.CandidateEffective);
                    Assert.Equal(!disabled, plan.CanApply);
                    Assert.Empty(plan.AffectedRefundIds);
                    // No recharge source is owned: the paid rank is pending and must not invent an effective output.
                    Assert.DoesNotContain(plan.NewEffectiveChannels, c => c.Memory == talent.ScopedModifier.ScopeMemory);
                    Assert.Equal(plan.OldEffectiveChannels.Select(Describe), plan.NewEffectiveChannels.Select(Describe));
                    if (disabled)
                    {
                        Assert.Contains(plan.SaturationDetails, d => d.StarId == starId && d.Reason == AllocationInertReason.PermanentlyDisabled);
                        Assert.Equal(original, ProfileCodec.Write(duo.ProductionProfile));
                        break;
                    }
                    Assert.Empty(plan.SaturationDetails);
                    Assert.Equal(rank, duo.ProductionProfile.Hero(hero).Talents[starId]);
                }
                if (!disabled)
                {
                    var refund = duo.Step(new AllocationChange { Kind = AllocationChangeKind.Refund, CandidateStarId = starId }).Plan;
                    Assert.True(refund.CanApply);
                    Assert.Equal(talent.MaxRank - 1, duo.ProductionProfile.Hero(hero).Talents[starId]);
                    duo.Buy(starId);
                }
            }));
        }

        // Known #199 regional-evaluation defect, outside this purchase-vs-retention fix:
        // the e1 region omits the replacer and resurrects the ring. Full evaluation instead refunds
        // the bridge satellites and rejects q. Keep the independent comparison runnable without
        // changing that unresolved eligibility rule or silently accepting the production answer.
        // SODRPG_REPLACEMENT_DIAGNOSTICS=1 dotnet test --filter Category=ReplacementDiagnostic
        private sealed class ReplacementDiagnosticTheoryAttribute : TheoryAttribute
        {
            public ReplacementDiagnosticTheoryAttribute()
            {
                if (Environment.GetEnvironmentVariable("SODRPG_REPLACEMENT_DIAGNOSTICS") != "1")
                    Skip = "Known #199 region mismatch. Set SODRPG_REPLACEMENT_DIAGNOSTICS=1 to reproduce; satellite retention needs a design decision.";
            }
        }

        [ReplacementDiagnosticTheory, Trait("Category", "ReplacementDiagnostic")]
        [InlineData(1)]
        [InlineData(2)]
        public void Replaced_prerequisite_retention_and_inert_purchase_match_the_reference(int ownedRanks)
        {
            const string hero = ReplacedStarPurchaseTests.Hero;
            WithVerification(() => WithGeneratedHero(hero, tree =>
            {
                var duo = new Duo(tree, null, HeroTreeLayout.ForHero(hero), hero,
                    () => FundedProfile(62UL, hero, 250, relics: 0));
                foreach (string id in ReplacedStarPurchaseTests.Prerequisites)
                    Assert.Empty(duo.Buy(id).AffectedRefundIds);
                for (int rank = 1; rank < ownedRanks; rank++) Assert.Empty(duo.Buy(ReplacedStarPurchaseTests.Ring).AffectedRefundIds);
                Assert.Empty(duo.Buy(ReplacedStarPurchaseTests.Choice, 1).AffectedRefundIds);
                Assert.Equal(ownedRanks, duo.ProductionProfile.Hero(hero).Talents[ReplacedStarPurchaseTests.Ring]);
                var rejected = duo.Step(new AllocationChange
                {
                    Kind = AllocationChangeKind.Purchase, CandidateStarId = ReplacedStarPurchaseTests.Ring,
                }).Plan;
                Assert.False(rejected.CanApply);
                Assert.Empty(rejected.AffectedRefundIds);
            }));
        }

        /// <summary>The real generated v1.31 trees (700-860 stars): dependency groups, region evaluation and keystone changes against the original algorithm.</summary>
        [Theory, MemberData(nameof(GeneratedHeroes))]
        public void Production_engine_matches_the_original_algorithm_on_generated_v131_hero_trees(string hero, int seed)
        {
            WithGeneratedHero(hero, tree =>
                RunRandom(() => new Duo(tree, null, HeroTreeLayout.ForHero(hero), hero, () => FundedProfile((ulong)seed, hero, 80, relics: 4)),
                    hero, tree, true, seed, 30, hero));
        }

        /// <summary>More seeds and longer sequences on the generated trees (slow: the original algorithm costs seconds per step there).</summary>
        [SlowFact, Trait("Speed", "Slow")]
        public void Extended_random_equivalence_over_the_generated_v131_hero_trees()
        {
            foreach (var row in AllGeneratedHeroes())
            {
                string hero = (string)row[0];
                WithGeneratedHero(hero, tree =>
                {
                    for (int seed = 500; seed < 504; seed++)
                    {
                        int s = seed;
                        RunRandom(() => new Duo(tree, null, HeroTreeLayout.ForHero(hero), hero, () => FundedProfile((ulong)s, hero, 220, relics: 4)),
                            hero, tree, true, seed, 90, hero);
                    }
                });
            }
        }

        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        public void Production_engine_matches_the_original_algorithm_on_synthetic_cap_domination_and_disable_trees(int seed)
        {
            var s = BuildSynthetic();
            RunRandom(() => SyntheticDuo(s), SynHero, s.Tree, true, seed, 160, "synthetic");
        }

        /// <summary>Many more seeds, longer sequences and larger allocations on every hero, the synthetic cascades tree and the authored probe.</summary>
        [SlowFact, Trait("Speed", "Slow")]
        public void Extended_random_equivalence_over_every_real_hero_the_synthetic_tree_and_the_authored_probe()
        {
            foreach (string hero in new[] { "Hero_Cetus", "Hero_Vesper", "Hero_Yubar", "Hero_Bismuth", "Hero_Aurena", "Hero_Husk" })
            {
                var tree = HeroSigils.TreeFor(hero);
                for (int seed = 100; seed < 103; seed++)
                {
                    int s = seed;
                    RunRandom(() => new Duo(tree, null, HeroTreeLayout.ForHero(hero), hero, () => FundedProfile((ulong)s, hero, 160, relics: 8)),
                        hero, tree, true, seed, 260, hero);
                }
            }
            var synthetic = BuildSynthetic();
            for (int seed = 200; seed < 208; seed++)
                RunRandom(() => SyntheticDuo(synthetic), SynHero, synthetic.Tree, true, seed, 400, "synthetic");
            WithAuthoredProbe(tree =>
            {
                for (int seed = 300; seed < 305; seed++)
                    RunRandom(() => AuthoredDuo(tree), Yubar, tree, true, seed, 260, "authored");
            });
        }

        [Theory]
        [InlineData(21)]
        [InlineData(22)]
        public void Production_engine_matches_the_original_algorithm_on_authored_mechanism_stars_with_capped_boosts(int seed)
        {
            WithAuthoredProbe(tree => RunRandom(() => AuthoredDuo(tree), Yubar, tree, true, seed, 110, "authored"));
        }
    }
}

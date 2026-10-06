using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class LegacyGimmickPrecapTests
    {
        private const string Hero = "Hero_Cetus", Memory = "St_D_IcyVeins";
        private const string Root = "outer.precap.root", EffectId = "outer.precap.effect", ModifierId = "outer.precap.modifier";

        private static AuthoredStarDef Outer(string id, ClusterStarDef effect) => new AuthoredStarDef
        {
            HeroKey = Hero, LocalStarId = id, ClusterId = "outer.precap", Region = ClusterRegion.Outer,
            AnchorId = id == Root ? null : Root, Shape = ClusterShape.Fan, Effect = effect
        };
        private static Build Allocated(GimmickDef def, decimal boost, AuthoredKeystoneSpec transform,
            GimmickParam? parameter = null, decimal amount = 0, string capProfile = null)
        {
            var scope = new KeystoneScope(targetEffectIds: new[] { EffectId });
            transform.Scope = scope;
            var key = AuthoredKeystoneCompiler.Compile("test.precap.key", new[] { Memory }, new[] {
                transform,
                new AuthoredKeystoneSpec { Grant = new AuthoredMechanismSpec {
                    Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.precap.granted-heal",
                    Source = MemorySelector.Parse(Memory), Trigger = MemoryEventKind.Hit,
                    Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Heal, Value = 1m }
                } }
            });
            var anchor = HeroSigils.TreeFor(Hero).First(t => t.RouteMemory == Memory && t.RouteOrder == 7);
            var stars = new List<AuthoredStarDef>
            {
                Outer(Root, new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験", "Probe"), Stat = Stat.Armor, Amount = 1 }),
                Outer(EffectId, new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"), Memory = Memory, Gimmick = def }),
                Outer(ModifierId, new ClusterStarDef { Kind = parameter.HasValue ? ClusterStarKind.GimmickParam : ClusterStarKind.GimmickBoost,
                    Name = new Txt("試験", "Probe"), Memory = Memory, MaxRank = 1,
                    ScopedModifier = new ScopedModifierDef
                    {
                        ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Memory, Param = parameter,
                        Amount = ModifierUnits.FromPercent(parameter.HasValue ? parameter == GimmickParam.Chance || parameter == GimmickParam.ExtraTargets ? 0 : amount : boost),
                        Probability = parameter == GimmickParam.Chance ? ProbabilityUnits.FromPercent(Math.Min(100, amount)) : default,
                        ExtraTargets = parameter == GimmickParam.ExtraTargets ? (int)amount : 0,
                        TargetEffectIds = new[] { EffectId }, TargetEffects = new[] { def.Effect }, CapProfileId = capProfile
                    } }),
                new AuthoredStarDef { HeroKey = Hero, LocalStarId = key.KeystoneId, ClusterId = "test.precap.keys",
                    Region = new ClusterRegion { Kind = ClusterRegionKind.Keystone }, AnchorId = anchor.Id, Shape = ClusterShape.Fan,
                    KeystoneDefinition = key, Effect = new ClusterStarDef { Kind = ClusterStarKind.Keystone,
                        Name = new Txt("試験", "Probe"), KeystoneDefinition = key, RankCost = key.Cost } }
            };
            var modifierIds = new List<string> { ModifierId };
            if (parameter == GimmickParam.Chance && amount > 100)
            {
                const string extraId = ModifierId + ".extra";
                modifierIds.Add(extraId);
                stars.Add(Outer(extraId, new ClusterStarDef { Kind = ClusterStarKind.GimmickParam,
                    Name = new Txt("試験", "Probe"), Memory = Memory,
                    ScopedModifier = new ScopedModifierDef { ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Memory,
                        Param = parameter, Probability = ProbabilityUnits.FromPercent(amount - 100),
                        TargetEffectIds = new[] { EffectId }, TargetEffects = new[] { def.Effect }, CapProfileId = capProfile } }));
            }
            try
            {
                var tree = StarClusters.RegisterAuthored(Hero, stars).TreeFor(Hero);
                var profile = new Profile(); var hero = profile.Hero(Hero);
                hero.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints); hero.Kills = 1000000;
                foreach (string id in new[] { Root, EffectId })
                {
                    TreeTestPaths.Connect(profile, Hero, id);
                    Rules.AddTalentRank(profile, Hero, id);
                }
                TreeTestPaths.Connect(profile, Hero, anchor.Id);
                if (!hero.Talents.TryGetValue(anchor.Id, out int anchorRank) || anchorRank <= 0)
                    Rules.AddTalentRank(profile, Hero, anchor.Id);
                var change = new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = key.KeystoneId };
                var plan = Rules.PreviewAllocationChange(profile, Hero, change);
                Rules.ApplyAllocationChange(profile, Hero, change, plan.AffectedRefundIds);
                foreach (string id in modifierIds)
                {
                    TreeTestPaths.Connect(profile, Hero, id);
                    Rules.AddTalentRank(profile, Hero, id);
                }
                var decoded = Build.Decode(Build.ComputeForTree(profile, Hero, 0, tree).Encode());
                Assert.Equal(key.KeystoneId, decoded.SelectedKeystone.KeystoneId);
                return decoded;
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }
        private static GimmickRequest Fire(Build build)
        {
            var entry = build.Gimmicks.Single(e => e.StarId == EffectId);
            var runtime = new GimmickRuntime(); runtime.SetBuild(new[] { entry });
            var requests = new List<GimmickRequest>();
            runtime.Fire(entry.Def.Trigger, Memory, 1, 7, 100, false, requests, activationId: 1,
                transform: configured => AuthoredKeystoneComposer.EffectiveGimmick(configured.Def,
                    AuthoredKeystoneComposer.TransformAllocationPayload(build,
                        AuthoredKeystoneComposer.GimmickPayload(configured.Def, configured.StarId), Memory)));
            return Assert.Single(requests);
        }

        [Fact]
        public void Allocated_ordinary_boost_and_key_apply_native_primed_cap_after_scaling()
        {
            var build = Allocated(new GimmickDef { Trigger = GimmickTrigger.OnUse, Effect = GimmickEffect.Primed, Value = 40 },
                300, new AuthoredKeystoneSpec { Percent = 50 });
            var request = Fire(build);
            decimal expected = 120m * (1m + 1.5m * build.SpentStarPoints / 500);
            Assert.Equal(expected, request.Entry.Def.EffectiveValueOrAuthored);
            Assert.Equal((float)expected, request.Entry.Def.ValuePercent);
        }

        [Theory]
        [InlineData(GimmickEffect.Empower, GimmickParam.Duration, KeystoneField.Duration, 400, 16)]
        [InlineData(GimmickEffect.Burst, GimmickParam.Radius, KeystoneField.Radius, 400, 16)]
        [InlineData(GimmickEffect.Ricochet, GimmickParam.ExtraTargets, KeystoneField.TargetCount, 30, 2 + Gimmicks.MaxExtraTargets)]
        [InlineData(GimmickEffect.Element, GimmickParam.Chance, KeystoneField.Probability, 100, 100)]
        public void Allocated_parameters_transform_raw_then_apply_native_final_cap(GimmickEffect effect, GimmickParam parameter,
            KeystoneField field, decimal amount, decimal expected)
        {
            var build = Allocated(new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = 1,
                Arg = effect == GimmickEffect.Ricochet ? 2 : 0 }, 0,
                field == KeystoneField.TargetCount ? new AuthoredKeystoneSpec { Field = field, Delta = 1 }
                    : new AuthoredKeystoneSpec { Field = field, Percent = 50 }, parameter, amount);
            var def = Fire(build).Entry.Def;
            if (parameter == GimmickParam.Duration) Assert.Equal((float)expected, Gimmicks.Duration(def, 4));
            else if (parameter == GimmickParam.Radius) Assert.Equal((float)expected, Gimmicks.Radius(def, effect == GimmickEffect.Ricochet ? 8 : 4));
            else if (parameter == GimmickParam.ExtraTargets) Assert.Equal((int)expected, Gimmicks.TargetLimit(def));
            else Assert.Equal(expected * 100, Gimmicks.ChanceProbabilityUnits(def));
        }

        [Fact]
        public void Ordinary_declared_parameter_cap_is_not_replaced_by_native_final_cap()
        {
            const string cap = "test.precap.duration";
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile
                { Id = cap, Param = GimmickParam.Duration, MaximumModifier = ModifierUnits.FromPercent(50) });
            var build = Allocated(new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Empower, Value = 1 }, 0,
                new AuthoredKeystoneSpec { Field = KeystoneField.Duration, Percent = 25 }, GimmickParam.Duration, 100, cap);
            Assert.Equal(7.5f, Gimmicks.Duration(Fire(build).Entry.Def, 4));
        }

        [Fact]
        public void Two_percentage_transforms_keep_exact_decimal_until_the_real_element_stack_roll()
        {
            var build = Allocated(new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Element, Value = .25m },
                .5m, new AuthoredKeystoneSpec { Percent = .5m });
            var def = Fire(build).Entry.Def;
            Assert.Equal(.25250625m, def.EffectiveValueOrAuthored);
            Assert.Equal(1, Gimmicks.ElementStacks(def, .002525062));
            Assert.Equal(0, Gimmicks.ElementStacks(def, .002525063));
        }

        [Theory]
        [InlineData(GimmickEffect.Crescendo)]
        [InlineData(GimmickEffect.Sap)]
        [InlineData(GimmickEffect.Weakspot)]
        public void Retained_stack_and_victim_windows_consume_exact_effective_coefficients(GimmickEffect effect)
        {
            var build = Allocated(new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = .25m },
                .5m, new AuthoredKeystoneSpec { Percent = .5m });
            var entry = build.Gimmicks.Single(e => e.StarId == EffectId);
            var effective = AuthoredKeystoneComposer.EffectiveGimmick(entry.Def,
                AuthoredKeystoneComposer.TransformAllocationPayload(build,
                    AuthoredKeystoneComposer.GimmickPayload(entry.Def, entry.StarId), Memory));
            var runtime = new GimmickRuntime(); runtime.SetBuild(new[] { entry });
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 1, 7, 100, false, requests, activationId: 1, transform: _ => effective);
            if (effect == GimmickEffect.Crescendo)
            {
                runtime.Fire(GimmickTrigger.OnHit, Memory, 1.5f, 7, 100, false, requests, activationId: 2, transform: _ => effective);
                Assert.Equal((float)(.25250625m * 2), runtime.CrescendoPercent(Memory, 2));
                Assert.Equal(0f, runtime.CrescendoPercent("St_Q_GlacialSpike", 2));
                Assert.Equal(0f, runtime.CrescendoPercent(Memory, 20));
            }
            else if (effect == GimmickEffect.Sap)
            {
                Assert.Equal((float).25250625m, runtime.SapPercent(7, 2, false));
                Assert.Equal((float)(.25250625m / 2), runtime.SapPercent(7, 2, true));
                Assert.Equal(0f, runtime.SapPercent(8, 2, false));
                Assert.Equal(0f, runtime.SapPercent(7, 20, false));
            }
            else
            {
                Assert.Equal((float).25250625m, runtime.WeakspotPercent(7, 2));
                Assert.Equal(0f, runtime.WeakspotPercent(8, 2));
                Assert.Equal(0f, runtime.WeakspotPercent(7, 20));
            }
        }

        [Fact]
        public void Keyless_finer_ordinary_value_survives_wire_and_native_boundary()
        {
            var def = Gimmicks.ApplyModifierUnits(new GimmickDef
                { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Element, Value = .0000001m }, 1, 0, 0, 0, 0);
            var build = new Build(); build.Gimmicks.Add(new GimmickEntry { StarId = EffectId, Memory = Memory, Def = def });
            var runtime = new GimmickRuntime(); runtime.SetBuild(Build.Decode(build.Encode()).Gimmicks);
            var requests = new List<GimmickRequest>(); runtime.Fire(GimmickTrigger.OnHit, Memory, 1, 7, 100, false, requests);
            var effective = Assert.Single(requests).Entry.Def;
            Assert.Equal(.00000010001m, effective.EffectiveValueOrAuthored);
            Assert.Equal((float).00000010001m, effective.ValuePercent);
            Assert.Equal(1, Gimmicks.ElementStacks(effective, .00000000100005));
            Assert.Equal(0, Gimmicks.ElementStacks(effective, .00000000100015));
        }

        [Fact]
        public void Malformed_duplicate_unknown_and_oversized_raw_rows_reject_the_whole_build()
        {
            var build = Allocated(new GimmickDef { Trigger = GimmickTrigger.OnUse, Effect = GimmickEffect.Primed, Value = 40 },
                300, new AuthoredKeystoneSpec { Percent = 50 });
            string wire = build.Encode();
            string raw = wire.Split(';').Single(section => section.StartsWith("r:", StringComparison.Ordinal));
            Assert.Null(Build.Decode(wire.Replace(raw, raw + "," + raw.Substring(2))));
            Assert.Null(Build.Decode(wire.Replace(raw, "r:unknown.effect:" + raw.Substring(raw.LastIndexOf(':') + 1))));
            Assert.Null(Build.Decode(wire.Replace(raw, "r:" + EffectId + ":" + Convert.ToBase64String(new byte[50]))));
            Assert.Null(Build.Decode(wire.Replace(raw, "r:" + EffectId + ":" + Convert.ToBase64String(new byte[] { 32 }))));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)1); writer.Write(-1m); writer.Flush();
                Assert.Null(Build.Decode(wire.Replace(raw, "r:" + EffectId + ":" + Convert.ToBase64String(stream.ToArray()))));
            }
            Assert.Null(Build.Decode(string.Join(";", wire.Split(';').Where(section => !section.StartsWith("k:", StringComparison.Ordinal)))));
        }
    }
}

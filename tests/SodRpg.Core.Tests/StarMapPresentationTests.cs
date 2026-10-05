using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class StarMapPresentationTests
    {
        private const string Memory = "St_D_IcyVeins";
        private const string OtherMemory = "St_Q_EmbracingTheChill";
        private static TalentDef Star() => new TalentDef("test.presentation", Line.Guard, new Txt("試験", "Probe"), Stat.Armor, 0, 1);

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Example_choice_retains_both_effects_and_moves_selection_without_hiding_the_alternative(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var star = HeroSigils.TreeFor("Hero_Cetus").Single(t => t.Id == "h.cetus.cluster.icy-veins.5");
                string unselected = StarMapPresentation.ChoiceDescription(star, -1, 0);
                foreach (var option in star.Choices)
                {
                    Assert.Contains(option.Name.ToString(), unselected);
                    Assert.Contains(option.Describe(), unselected);
                }
                string marker = japanese ? "［選択中］" : "[Chosen] ";
                string first = StarMapPresentation.ChoiceDescription(star, 0, 1);
                string second = StarMapPresentation.ChoiceDescription(star, 1, 1);
                Assert.Contains(marker + star.Choices[0].Name, first);
                Assert.DoesNotContain(marker + star.Choices[1].Name, first);
                Assert.Contains(marker + star.Choices[1].Name, second);
                Assert.DoesNotContain(marker + star.Choices[0].Name, second);
                Assert.Contains(star.Choices[0].Describe(), second);
                Assert.Contains(star.Choices[1].Describe(), first);
                Assert.Contains(japanese ? "未選択" : "Unselected", unselected);
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.ChoiceDescription(star, -1, 1));
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.ChoiceDescription(star, 2, 0));
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.ChoiceDescription(star, -2, 0));
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.ChoiceDescription(star, 0, 2));
                Assert.Throws<ArgumentOutOfRangeException>(() => StarMapPresentation.ChoiceOptionLabel(star, 2, 0));
                var invalid = Star();
                invalid.ClusterStar = new ClusterStarDef { Kind = ClusterStarKind.Choice };
                invalid.Choices = new[] { star.Choices[0] };
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.ChoiceDescription(invalid, -1, 0));
            }
            finally { Loc.Japanese = previous; }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Compiled_key_displays_typed_effect_scope_magnitude_and_actual_cost(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var key = AuthoredKeystoneCompiler.Compile("test.presentation.key", new[] { Memory },
                    new[] { new AuthoredKeystoneSpec { Percent = 100, Scope = new KeystoneScope(
                        targetMemorySet: new[] { Memory }, targetEffectSet: new[] { GimmickEffect.Echo }) } }, cost: 7);
                var star = Star();
                star.KeystoneDefinition = key;
                string text = StarMapPresentation.KeystoneDescription(star);
                Assert.Contains("100%", text);
                Assert.Contains(Links.Name(Memory).ToString(), text);
                Assert.Contains(japanese ? "必要ポイント：7" : "Cost: 7 points", text);
                if (japanese)
                {
                    Assert.DoesNotContain("NativeDamage", text);
                    Assert.DoesNotContain("ModEffect", text);
                    Assert.DoesNotContain("Scale", text);
                }
            }
            finally { Loc.Japanese = previous; }
        }

        private static IEnumerable<AuthoredMechanismSpec> Mechanisms()
        {
            const string id = "test.presentation.channel";
            var source = MemorySelector.Parse(Memory);
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = id,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 10 } };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = id,
                Recharge = new DirectedRechargeChannel(id, source, MemoryEventKind.Hit, MemorySelector.Parse(OtherMemory), new[] { 100 }, AttributionBudget.PerActivation) };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess, ChannelId = id,
                Bridge = new BridgeSuccessDefinition("test.presentation.pair", new[] {
                    new BridgeEndpointRequirement("test.presentation.a", Memory), new BridgeEndpointRequirement("test.presentation.b", OtherMemory) },
                    1, BridgeGateKind.Mark, source, MemoryEventKind.Hit, MemorySelector.Parse(OtherMemory), MemoryEventKind.Hit,
                    new BridgePayload(id, BridgePayloadKind.Damage, new[] { 100 }), Array.Empty<BridgePayload>()) };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.MemoryPrimed, ChannelId = id,
                Primed = new MemoryPrimedDefinition(id, Memory, MemoryEventKind.Hit, 100) };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.RelayWindow, ChannelId = id,
                Relay = new RelayWindowDefinition(id, OtherMemory, 100) };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.SacrificeShield, ChannelId = id };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.AlliedWard, ChannelId = id,
                Ward = new AlliedWardDefinition(id, WardRecipientKind.AlliedTravelers, WardAmountBasis.RecipientMaxHP, ModShieldPoolKind.Ordinary, 100, true) };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.PressureDividend, ChannelId = id,
                Trigger = MemoryEventKind.Kill, Budget = AttributionBudget.PerKill,
                Dividend = new PressureDividendChannel(new[] { new PressureDividendContribution("test.presentation", Memory, 100) }) };
            yield return new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.StunSourceFilter, ChannelId = id };
            yield return IdentityStrikeTests.Spec(IdentityStrikeTests.WindStrike(id));
            yield return IdentityStrikeTests.Spec(MemoryTuningDefinition.KeepSpeed(id, 4000));
        }

        [Fact]
        public void Typed_mechanisms_remain_distinct_from_each_other_and_from_barrier_in_both_languages()
        {
            bool previous = Loc.Japanese;
            try
            {
                var mechanisms = Mechanisms().ToArray();
                Assert.Equal(Enum.GetValues(typeof(AuthoredMechanismKind)).Cast<AuthoredMechanismKind>().OrderBy(k => k), mechanisms.Select(m => m.Kind).OrderBy(k => k));
                foreach (bool japanese in new[] { true, false })
                {
                    Loc.Japanese = japanese;
                    var labels = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var mechanism in mechanisms)
                    {
                        var star = Star(); star.Mechanism = mechanism;
                        Assert.True(labels.Add(StarMapPresentation.MechanismLabel(star)), "Different mechanisms must not collapse into one category.");
                    }
                    var barrier = new TalentDef("test.barrier", Line.Guard, new Txt("障壁", "Barrier"), Power.Barrier, 2, 1);
                    Assert.Equal(Content.PowerName(Power.Barrier), StarMapPresentation.MechanismLabel(barrier));
                    Assert.DoesNotContain(StarMapPresentation.MechanismLabel(barrier), labels);
                }
                var invalid = Star();
                invalid.Mechanism = new AuthoredMechanismSpec { Kind = (AuthoredMechanismKind)999, ChannelId = "test.invalid" };
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.MechanismLabel(invalid));
                invalid.Mechanism.Kind = AuthoredMechanismKind.DirectedRecharge;
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.MechanismLabel(invalid));
                invalid.Mechanism.Gimmick = mechanisms[0].Gimmick;
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.MechanismLabel(invalid));
                invalid.Mechanism = null;
                invalid.GimmickParameter = (GimmickParam)999;
                Assert.Throws<InvalidOperationException>(() => StarMapPresentation.MechanismLabel(invalid));
            }
            finally { Loc.Japanese = previous; }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Directed_recharge_keeps_recipient_probability_and_cadence_distinct(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var star = Star();
                star.Mechanism = new AuthoredMechanismSpec {
                    Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "test.presentation.recharge",
                    Source = MemorySelector.Parse(Memory),
                    Recharge = new DirectedRechargeChannel("test.presentation.recharge", MemorySelector.Parse(Memory),
                        MemoryEventKind.Hit, MemorySelector.Parse(OtherMemory), new[] { 500 },
                        probabilityUnits: 2500, everyN: 3)
                };
                string description = StarMapPresentation.EffectDescription(star);
                Assert.Contains(Links.Name(Memory).ToString(), description);
                Assert.Contains(Links.Name(OtherMemory).ToString(), description);
                Assert.Contains("5%", description);
                Assert.Contains("25%", description);
                Assert.Contains("3", description);
                if (japanese)
                {
                    Assert.DoesNotContain("PerActivation", description);
                    Assert.DoesNotContain("St_Q_", description);
                }
            }
            finally { Loc.Japanese = previous; }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Ordinary_ward_text_scopes_non_stacking_and_cap_to_the_same_caster_and_recipient(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                StarClusters.RegisterGeneratedHero("Hero_Yubar");
                // 爆発の加護：味方への通常枠の障壁。プールは付与者・受け手ごとなので、説明もその限定を示す。
                var star = HeroSigils.TreeFor("Hero_Yubar").Single(t => t.Id == "yubar.mem.exotic-matter.c3.n2");
                Assert.Equal(ModShieldPoolKind.Ordinary, star.Mechanism.Ward.PoolKind);
                string description = StarMapPresentation.EffectDescription(star);
                Assert.Contains(japanese
                    ? "同じ付与者から同じ受け手への通常の星の障壁とは重ならず"
                    : "Does not stack with ordinary star shields from the same caster to the same recipient", description);
                Assert.Contains(japanese
                    ? "付与者と受け手の組み合わせごとに、受け手の最大HPの15%まで"
                    : "up to 15% of the recipient's maximum HP per caster and recipient", description);
            }
            finally
            {
                Loc.Japanese = previous;
                StarClusters.RegisterAuthored("Hero_Yubar", Array.Empty<AuthoredStarDef>());
            }
        }
    }
}

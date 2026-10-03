using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class VerifiedMechanismSlotsConsumerTests
    {
        private const string Hero = "Hero_Yubar";
        private static Build KeyBuild(string selector, bool receiver = false)
        {
            var key = AuthoredKeystoneCompiler.Compile("test.verified.slots.key", Array.Empty<string>(),
                new[] { new AuthoredKeystoneSpec { Percent = 100,
                    Scope = new KeystoneScope(
                        sourceSelectors: receiver ? null : new[] { MemorySelector.Parse(selector) },
                        receiverSelectors: receiver ? new[] { MemorySelector.Parse(selector) } : null) } },
                new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -10 } });
            return new Build { SelectedKeystone = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key)) };
        }

        private static AuthoredMechanismEntry Echo(string source)
        {
            var entry = new AuthoredMechanismEntry
            {
                StarId = "test.verified.slots.echo",
                ContributorIds = new[] { "test.verified.slots.echo" },
                Spec = new AuthoredMechanismSpec
                {
                    Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.verified.slots.echo",
                    Source = MemorySelector.Parse(source), Trigger = MemoryEventKind.Hit,
                    Budget = AttributionBudget.PerActivationVictim,
                    Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 20m }
                }
            };
            return AuthoredMechanismCodec.Decode(AuthoredMechanismCodec.Encode(entry));
        }

        [Fact]
        public void Exact_ultimate_projection_applies_QR_key_at_real_R_without_original_slot_fiction()
        {
            const string memory = "St_R_Cataclysm";
            var channels = AuthoredMechanisms.EffectiveChannels(Echo(memory), KeyBuild("@Q|@R"), Hero).ToArray();
            Assert.Contains(channels, c => c.Memory == memory && c.ValueMilli == 40000m);
            Assert.Contains(channels, c => c.Memory == memory && c.ValueMilli == 20000m);

            var rPayload = new KeystonePayload(KeystoneLayer.ModEffect, 20m, new KeystoneCaps(100m), effect: GimmickEffect.Echo);
            var actual = new MechanismEquipment(1, 1, new[]
            {
                new EquippedMechanismMemory(memory, 101, MechanismMemorySlot.R, false, true)
            });
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(KeyBuild("@Q|@R"), rPayload,
                memory, equipment: actual).Value);
        }

        [Fact]
        public void Fixed_identity_cannot_gain_Q_key_effect_or_satisfy_filtered_Q_source()
        {
            const string identity = "St_D_ConvergencePoint";
            var channels = AuthoredMechanisms.EffectiveChannels(Echo(identity), KeyBuild("@Q"), Hero).ToArray();
            Assert.All(channels, c => Assert.Equal(20000m, c.ValueMilli));
            Assert.Contains(channels, c => c.Memory == identity);
            Assert.Empty(AuthoredMechanisms.EffectiveChannels(Echo("@Q(" + identity + ")"), KeyBuild("@Q"), Hero));
        }

        [Fact]
        public void Common_normal_projection_can_use_editable_Q_but_not_fixed_identity()
        {
            const string normal = "St_C_BeamOfLight";
            var channels = AuthoredMechanisms.EffectiveChannels(Echo(normal), KeyBuild("@Q"), Hero).ToArray();
            Assert.Contains(channels, c => c.Memory == normal && c.ValueMilli == 40000m);
            Assert.Contains(channels, c => c.Memory == normal && c.ValueMilli == 20000m);
            Assert.Empty(AuthoredMechanisms.EffectiveChannels(Echo("@ID(" + normal + ")"), KeyBuild("@ID"), Hero));
        }

        [Theory]
        [InlineData("St_R_ChainReaction")]
        public void Foreign_or_unknown_sources_cannot_invent_attainable_projection(string memory)
        {
            Assert.Empty(AuthoredMechanisms.EffectiveChannels(Echo(memory), KeyBuild("@Q|@R"), Hero));
        }

        [Theory]
        [InlineData("St_R_UnverifiedMemory")]
        [InlineData("St_C_UnverifiedMemory")]
        public void Unknown_native_names_are_rejected_before_projection(string memory)
        {
            Assert.Throws<ArgumentException>(() => MemorySelector.Parse(memory));
        }

        [Fact]
        public void Actual_slot_and_verified_category_are_independent_in_key_payload_matching()
        {
            const string normal = "St_C_BeamOfLight", ultimate = "St_U_ShoutOfOblivion";
            Assert.True(VerifiedMechanismSlots.TryGetCategory(Hero, normal, out var normalCategory));
            Assert.True(VerifiedMechanismSlots.TryGetCategory(Hero, ultimate, out var ultimateCategory));
            Assert.Contains(MechanismMemorySlot.R, VerifiedMechanismSlots.ForMemory(Hero, normal));
            Assert.Contains(MechanismMemorySlot.Q, VerifiedMechanismSlots.ForMemory(Hero, ultimate));
            var normalAtR = new EquippedMechanismMemory(normal, 101, MechanismMemorySlot.R,
                normalCategory == VerifiedMechanismMemoryCategory.Normal, normalCategory == VerifiedMechanismMemoryCategory.Ultimate);
            var ultimateAtQ = new EquippedMechanismMemory(ultimate, 102, MechanismMemorySlot.Q,
                ultimateCategory == VerifiedMechanismMemoryCategory.Normal, ultimateCategory == VerifiedMechanismMemoryCategory.Ultimate);
            var equipment = new MechanismEquipment(1, 1, new[] { normalAtR, ultimateAtQ });
            var payload = new KeystonePayload(KeystoneLayer.ModEffect, 20m, new KeystoneCaps(100m), effect: GimmickEffect.Echo);
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(KeyBuild("@OTHER", receiver: true), payload,
                ultimate, normal, equipment: equipment).Value);
            Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(KeyBuild("@OTHER", receiver: true), payload,
                normal, ultimate, equipment: equipment).Value);
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(KeyBuild("@R"), payload,
                normal, equipment: equipment, sourceSlot: MechanismMemorySlot.Q).Value);
            Assert.Equal(20m, AuthoredKeystoneComposer.TransformAllocationPayload(KeyBuild("@R"), payload,
                ultimate, equipment: equipment, sourceSlot: MechanismMemorySlot.R).Value);
        }

        [Fact]
        public void C15_other_normal_recipient_can_attain_R_and_never_admits_an_ultimate()
        {
            AuthoredMechanismEntry Recharge(string recipient)
            {
                var source = MemorySelector.Parse("St_R_Cataclysm");
                var channel = new DirectedRechargeChannel("test.verified.slots.recharge", source, MemoryEventKind.Hit,
                    MemorySelector.Parse(recipient), new[] { 1000 });
                return new AuthoredMechanismEntry
                {
                    StarId = "test.verified.slots.recharge", ContributorIds = new[] { "test.verified.slots.recharge" },
                    Spec = new AuthoredMechanismSpec
                    {
                        Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = channel.ChannelId, Source = source,
                        Trigger = MemoryEventKind.Hit, Budget = AttributionBudget.PerActivation, Recharge = channel
                    }
                };
            }

            var key = KeyBuild("@R", receiver: true);
            var channels = AuthoredMechanisms.EffectiveChannels(Recharge("@OTHER(St_C_BeamOfLight)"), key, Hero).ToArray();
            Assert.Contains(channels, c => c.ValueMilli == 20000m);
            Assert.Contains(channels, c => c.ValueMilli == 10000m);
            Assert.Empty(AuthoredMechanisms.EffectiveChannels(Recharge("@OTHER(St_U_ShoutOfOblivion)"), key, Hero));
        }

        [Fact]
        public void Original_R_normal_remains_other_normal_and_movement_cannot_be_Q()
        {
            const string normal = "St_R_Tranquility", movement = "St_M_Flicker";
            Assert.True(VerifiedMechanismSlots.TryGetCategory(Hero, normal, out var category));
            var actual = new EquippedMechanismMemory(normal, 101, MechanismMemorySlot.R,
                category == VerifiedMechanismMemoryCategory.Normal, category == VerifiedMechanismMemoryCategory.Ultimate);
            var equipment = new MechanismEquipment(1, 1, new[]
            {
                actual, new EquippedMechanismMemory("St_R_Cataclysm", 102, MechanismMemorySlot.Q, false, true)
            });
            var payload = new KeystonePayload(KeystoneLayer.ModEffect, 20m, new KeystoneCaps(100m), effect: GimmickEffect.Echo);
            Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(KeyBuild("@OTHER", receiver: true), payload,
                "St_R_Cataclysm", normal, equipment: equipment).Value);
            Assert.Empty(AuthoredMechanisms.EffectiveChannels(Echo("@Q(" + movement + ")"), KeyBuild("@Q"), Hero));
            Assert.Throws<InvalidOperationException>(() => Echo(movement));
        }
    }
}

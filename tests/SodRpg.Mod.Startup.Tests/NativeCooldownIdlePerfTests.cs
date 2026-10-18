using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // The build extracts the production patch unchanged from NativePerfPatches.cs.
    // AbilityTrigger is the same arithmetic boundary used by NativeCooldownSafetyTests.
    public sealed class NativeCooldownIdlePerfTests : IDisposable
    {
        private readonly Harmony owner = new Harmony("cooldown-idle-tests." + Guid.NewGuid().ToString("N"));
        private readonly bool previousOption = NativePerfOptions.CooldownIdleMultiplier;
        private readonly bool previousServer = Mirror.NetworkServer.active;
        private readonly bool previousClient = Mirror.NetworkClient.active;
        private readonly AbilityTrigger previousCurrent = NativeCooldownSafety.Current;
        private static readonly MethodInfo Multiplier = AccessTools.DeclaredMethod(typeof(AbilityTrigger),
            nameof(AbilityTrigger.GetCooldownTimeMultiplier), new[] { typeof(int) });
        private static readonly MethodInfo Tick = AccessTools.Method(typeof(NativeCooldownIdlePerf), "TickMultiplier");
        private static readonly MethodInfo Transpile = AccessTools.Method(typeof(NativeCooldownIdlePerf), "Transpiler");

        public NativeCooldownIdlePerfTests()
        {
            NativePerfOptions.CooldownIdleMultiplier = true;
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = false;
            NativeCooldownSafety.Current = null;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZeroRemainingReturnsOneWithoutCallingTheVirtualMultiplier(bool negativeZero)
        {
            float remaining = negativeZero ? BitConverter.Int32BitsToSingle(int.MinValue) : 0f;
            var trigger = NewTrigger(remaining, 0.375f);

            Assert.Equal(1f, ReadTick(trigger, 1));

            Assert.Empty(trigger.ReadIndices);
            Assert.Equal(new[] { 9f, remaining, 8f }, trigger.currentUnscaledCooldownTimes);
        }

        [Theory]
        [InlineData(7f)]
        [InlineData(-7f)]
        [InlineData(float.Epsilon)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void NonZeroRemainingCallsTheVirtualMultiplierWithTheSameIndex(float remaining)
        {
            var trigger = NewTrigger(remaining, 0.375f);
            float expected = trigger.GetCooldownTimeMultiplier(1);
            trigger.ReadIndices.Clear();

            Assert.Equal(expected, ReadTick(trigger, 1));

            Assert.Equal(new[] { 1 }, trigger.ReadIndices);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(7f)]
        public void DisabledOptionAlwaysCallsTheNativeMultiplier(float remaining)
        {
            NativePerfOptions.CooldownIdleMultiplier = false;
            var trigger = NewTrigger(remaining, 0.375f);

            Assert.Equal(0.375f, ReadTick(trigger, 1));

            Assert.Equal(new[] { 1 }, trigger.ReadIndices);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void AnOutOfRangeConfigIndexUsesTheNativeMultiplier(int index)
        {
            var trigger = NewTrigger(0f, 0.375f);

            Assert.Equal(0.375f, ReadTick(trigger, index));

            Assert.Equal(new[] { index }, trigger.ReadIndices);
        }

        [Fact]
        public void MissingRemainingTimesUseTheNativeMultiplier()
        {
            var trigger = NewTrigger(0f, 0.375f);
            trigger.currentUnscaledCooldownTimes = null;

            Assert.Equal(0.375f, ReadTick(trigger, 1));

            Assert.Equal(new[] { 1 }, trigger.ReadIndices);
        }

        [Theory]
        [InlineData(false, 0.005f)]
        [InlineData(false, 0.5f)]
        [InlineData(false, 3f)]
        [InlineData(true, 0.005f)]
        [InlineData(true, 0.5f)]
        [InlineData(true, 3f)]
        [InlineData(false, 0f)]
        [InlineData(false, -1f)]
        [InlineData(false, float.NaN)]
        [InlineData(false, float.PositiveInfinity)]
        [InlineData(true, 0f)]
        [InlineData(true, -1f)]
        [InlineData(true, float.NaN)]
        [InlineData(true, float.PositiveInfinity)]
        public void PatchedLogicUpdateKeepsRemainingTimesWithSafetyOnHostAndClient(bool client, float multiplier)
        {
            Mirror.NetworkServer.active = !client;
            Mirror.NetworkClient.active = client;
            foreach (var patchClass in new[]
            {
                typeof(NativeCooldownTickSafety), typeof(NativeCooldownMultiplierSafety),
                typeof(NativeBaseCooldownMultiplierSafety)
            })
                owner.CreateClassProcessor(patchClass).Patch();

            var baseline = NewTrigger(0f, multiplier);
            baseline.LogicUpdate(0.1f);
            Assert.Equal(new[] { 0, 1, 2 }, baseline.ReadIndices);
            Assert.Equal(0f, baseline.currentUnscaledCooldownTimes[1]);

            InstallIdlePatch();
            var patched = NewTrigger(0f, multiplier);
            patched.LogicUpdate(0.1f);

            Assert.Equal(baseline.currentUnscaledCooldownTimes, patched.currentUnscaledCooldownTimes);
            Assert.Equal(baseline.currentCharges, patched.currentCharges);
            Assert.Equal(baseline.RpcLog, patched.RpcLog);
            Assert.Equal(new[] { 0, 2 }, patched.ReadIndices);
            Assert.Equal(1f, patched.UsedMultipliers[1]);
            Assert.Equal(baseline.UsedMultipliers[0], patched.UsedMultipliers[0]);
            Assert.Equal(baseline.UsedMultipliers[2], patched.UsedMultipliers[2]);
            Assert.Null(NativeCooldownSafety.Current);
        }

        [Fact]
        public void DisabledOptionRestoresTheOriginalLogicUpdatePathWithoutUnpatching()
        {
            var baseline = NewTrigger(0f, 0.375f);
            baseline.LogicUpdate(0.1f);
            InstallIdlePatch();
            var enabled = NewTrigger(0f, 0.375f);
            enabled.LogicUpdate(0.1f);
            Assert.Equal(new[] { 0, 2 }, enabled.ReadIndices);

            NativePerfOptions.CooldownIdleMultiplier = false;
            var disabled = NewTrigger(0f, 0.375f);
            disabled.LogicUpdate(0.1f);

            Assert.Equal(baseline.ReadIndices, disabled.ReadIndices);
            Assert.Equal(baseline.UsedMultipliers, disabled.UsedMultipliers);
            Assert.Equal(baseline.currentUnscaledCooldownTimes, disabled.currentUnscaledCooldownTimes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(3)]
        public void AnUnexpectedCallsiteCountLeavesEveryInstructionUnchanged(int count)
        {
            var code = new List<CodeInstruction> { new CodeInstruction(OpCodes.Nop) };
            for (int i = 0; i < count; i++)
                code.Add(new CodeInstruction(OpCodes.Callvirt, Multiplier));
            // A different virtual override must not count as the base callsite.
            code.Add(new CodeInstruction(OpCodes.Callvirt,
                AccessTools.DeclaredMethod(typeof(SkillTrigger), nameof(SkillTrigger.GetCooldownTimeMultiplier))));
            var opcodes = code.Select(instruction => instruction.opcode).ToArray();
            var operands = code.Select(instruction => instruction.operand).ToArray();

            var result = Rewrite(code);

            Assert.Equal(code.Count, result.Count);
            for (int i = 0; i < code.Count; i++)
            {
                Assert.Same(code[i], result[i]);
                Assert.Equal(opcodes[i], result[i].opcode);
                Assert.Equal(operands[i], result[i].operand);
            }
            Assert.DoesNotContain(result, instruction => instruction.Calls(Tick));
        }

        [Fact]
        public void ASingleCallsiteIsReplacedOnceAndPreservesLabelsAndExceptionBlocks()
        {
            var generator = new DynamicMethod("cooldown-label", typeof(void), Type.EmptyTypes).GetILGenerator();
            var label = generator.DefineLabel();
            var block = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
            var call = new CodeInstruction(OpCodes.Callvirt, Multiplier);
            call.labels.Add(label);
            call.blocks.Add(block);
            var code = new[] { new CodeInstruction(OpCodes.Nop), call, new CodeInstruction(OpCodes.Ret) };

            var result = Rewrite(code);

            Assert.Equal(3, result.Count);
            Assert.Same(code[0], result[0]);
            Assert.Same(code[2], result[2]);
            Assert.Equal(OpCodes.Call, result[1].opcode);
            Assert.Equal(Tick, result[1].operand);
            Assert.Equal(new[] { label }, result[1].labels);
            Assert.Equal(new[] { block }, result[1].blocks);
            Assert.Single(result, instruction => instruction.Calls(Tick));
            Assert.DoesNotContain(result, instruction => instruction.Calls(Multiplier));
            Assert.Equal(result, Rewrite(result));
        }

        private void InstallIdlePatch()
        {
            Assert.NotEmpty(owner.CreateClassProcessor(typeof(NativeCooldownIdlePerf)).Patch());
            var target = AccessTools.Method(typeof(AbilityTrigger), nameof(AbilityTrigger.LogicUpdate));
            Assert.Contains(Harmony.GetPatchInfo(target).Transpilers,
                patch => patch.owner == owner.Id && patch.PatchMethod == Transpile);
        }

        private static float ReadTick(AbilityTrigger trigger, int configIndex)
            => (float)Tick.Invoke(null, new object[] { trigger, configIndex });

        private static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code)
            => ((IEnumerable<CodeInstruction>)Transpile.Invoke(null, new object[] { code })).ToList();

        private static CountingTrigger NewTrigger(float remaining, float multiplier) => new CountingTrigger
        {
            multiplier = multiplier,
            configs = new[] { new TriggerConfig(), new TriggerConfig(), new TriggerConfig() },
            currentUnscaledCooldownTimes = new[] { 9f, remaining, 8f },
            currentCharges = new[] { 0, 1, 0 }
        };

        private sealed class CountingTrigger : AbilityTrigger
        {
            internal readonly List<int> ReadIndices = new List<int>();

            [MethodImpl(MethodImplOptions.NoInlining)]
            public override float GetCooldownTimeMultiplier(int configIndex)
            {
                ReadIndices.Add(configIndex);
                return base.GetCooldownTimeMultiplier(configIndex);
            }
        }

        public void Dispose()
        {
            foreach (var target in owner.GetPatchedMethods().ToArray())
                owner.Unpatch(target, HarmonyPatchType.All, owner.Id);
            NativePerfOptions.CooldownIdleMultiplier = previousOption;
            Mirror.NetworkServer.active = previousServer;
            Mirror.NetworkClient.active = previousClient;
            NativeCooldownSafety.Current = previousCurrent;
        }
    }
}

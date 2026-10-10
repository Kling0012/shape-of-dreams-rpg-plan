using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class ClientTickIsolationTests : IDisposable
    {
        private readonly Action<string> _previous = Log.ErrorSink;
        private readonly float _time = Time.unscaledTime;
        private readonly List<string> _errors = new List<string>();
        public ClientTickIsolationTests() { Log.ErrorSink = _errors.Add; Time.unscaledTime = 100f; }
        public void Dispose() { Log.ErrorSink = _previous; Time.unscaledTime = _time; }

        [Fact]
        public void Every_registered_step_has_a_matching_diagnostic_name()
        {
            var session = new ClientSession();
            var actions = session.PrepareTickIsolationProbe();
            Assert.Equal(actions.Length, session.TickNames.Length);
            Assert.Equal("TickDirectStash", actions[17].Method.Name);
            Assert.Equal("direct stash save", session.TickNames[17]);
            Assert.Equal("TickKillSync", actions[18].Method.Name);
            Assert.Equal("kill sync", session.TickNames[18]);
            Assert.Equal("TickCoopTrade", actions[19].Method.Name);
            Assert.Equal("coop trade", session.TickNames[19]);
            Assert.Equal("TickRoomVisualSweep", actions[20].Method.Name);
            Assert.Equal("room visual sweep", session.TickNames[20]);
        }

        [Theory]
        [InlineData(17, "direct stash save")]
        [InlineData(18, "kill sync")]
        [InlineData(19, "coop trade")]
        [InlineData(20, "room visual sweep")]
        public void Tail_step_failure_keeps_original_error_and_continues_with_throttled_logging(int index, string name)
        {
            var session = new ClientSession();
            session.PrepareTickIsolationProbe();
            int completed = 0;
            session.SetTickProbe(index, () => throw new InvalidOperationException("original failure"));
            // Append a sentinel after even the last production step to exercise the actual loop's isolation.
            session.AppendTickProbe(() => completed++);
            session.Tick();
            Assert.Equal(1, completed);
            string error = Assert.Single(_errors);
            Assert.Contains("Client tick (" + name + ")", error);
            Assert.Contains("original failure", error);
            Time.unscaledTime = 101f;
            session.Tick();
            Assert.Equal(2, completed);
            Assert.Single(_errors);
            Time.unscaledTime = 110f;
            session.Tick();
            Assert.Equal(3, completed);
            Assert.Equal(2, _errors.Count);
        }
    }
}

namespace SodRpg.Mod
{
    // Only Tick is extracted unchanged from production. Step bodies are replaced after registration,
    // before they execute, so this tests dispatch/error isolation without simulating native gameplay.
    internal sealed partial class ClientSession
    {
        private Action[] _tickSteps;
        private string[] _tickStepNames;
        private float[] _tickStepNextLog;
        private Action _tickRegistrationProbe;
        internal string[] TickNames => _tickStepNames;
        internal Action[] PrepareTickIsolationProbe()
        {
            Action[] registered = null;
            _tickRegistrationProbe = () =>
            {
                registered = (Action[])_tickSteps.Clone();
                for (int i = 0; i < _tickSteps.Length; i++) _tickSteps[i] = () => { };
            };
            Tick();
            return registered;
        }
        internal void SetTickProbe(int index, Action step) => _tickSteps[index] = step;
        internal void AppendTickProbe(Action step)
        {
            _tickSteps = _tickSteps.Concat(new[] { step }).ToArray();
            _tickStepNames = _tickStepNames.Concat(new[] { "test sentinel" }).ToArray();
            Array.Resize(ref _tickStepNextLog, _tickSteps.Length);
        }
        private void TickGemSlotHudProbe() => _tickRegistrationProbe?.Invoke();
        private void TickProfileSlots() { }
        private void Wire() { }
        private void TickInfinitySettings() { }
        private void UpdateVariantVisuals() { }
        private void UpdateMonsterCues() { }
        private void TickBossDisplay() { }
        private void TickCurseResync() { }
        private void TickSalvageExpiry() { }
        private void TickHello() { }
        private void TickDirectStash() { }
        private void TickCoopTrade() { }
        private void TickRoomVisualSweep() { }
    }
}

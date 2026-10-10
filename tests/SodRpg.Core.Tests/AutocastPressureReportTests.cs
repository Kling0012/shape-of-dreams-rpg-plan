using System;
using System.Collections.Generic;
using PressureReportUnderTest;
using Xunit;

namespace PressureReportUnderTest
{
    // Native API doubles; the report and ResetHello bodies are extracted from production.
    internal static class Time { internal static float unscaledTime; }
    internal static class Mathf { internal static int RoundToInt(float value) => (int)Math.Round(value); }
    internal static class AutocastPressure
    {
        internal static float Over;
        internal static float SampleFrame() => Over;
    }
    internal struct DreamforgePerfPressureMsg { internal byte over; }
    internal sealed class NetworkWriter { internal void WriteByte(byte value) { } }
    internal static class Writer<T> { internal static Action<NetworkWriter, T> write; }
    internal static class NetworkServer { internal static bool active; }
    internal static class NetworkClient
    {
        internal static bool active, isConnected, FailSend;
        internal static readonly List<(float Time, byte Over)> Sent = new List<(float, byte)>();
        internal static int Attempts;
        internal static void Send(DreamforgePerfPressureMsg message)
        {
            Attempts++;
            if (FailSend) throw new InvalidOperationException("Simulated send failure");
            Sent.Add((Time.unscaledTime, message.over));
        }
    }
    internal sealed partial class ClientSession
    {
        private bool _hostAutocastPressureCapability;
        private float _nextAutocastPressureReport;
        private object _clientRpcOn = new object();
        private float _helloFirstSent, _nextHello, _nextBuildInputProbe;
        private bool _helloAnswered, _hostCompatibilityWarned, _hostInfinityAvailable;
        private bool _hostBuildInputCapability, _hostNetLite, _netLiteReceiveReady;
        private object _sentContinueReceiptActor;
        private string _sentContinueReceiptRunId, _sentContinueReceiptCheckpointId, _sentContinueReceiptResumeSession;
        private string HostVersionWarning;
        private void ResetOverflowBonusConnection() { }
        internal void Enable() => _hostAutocastPressureCapability = true;
        internal void ResetConnection() => ResetHello();
        internal void Tick(float now, float over)
        {
            Time.unscaledTime = now;
            AutocastPressure.Over = over;
            TickAutocastPressureReport();
        }
    }
}

namespace SodRpg.Core.Tests
{
    public sealed class AutocastPressureReportTests
    {
        private static ClientSession Start()
        {
            NetworkServer.active = false;
            NetworkClient.active = NetworkClient.isConnected = true;
            NetworkClient.FailSend = false;
            NetworkClient.Sent.Clear();
            NetworkClient.Attempts = 0;
            var client = new ClientSession();
            client.Enable();
            return client;
        }

        [Theory]
        [InlineData(1f, 255)]
        [InlineData(0f, 0)]
        [InlineData(0.1f, 26)]
        public void Stable_pressure_is_refreshed_before_host_expiry(float over, byte expected)
        {
            var client = Start();
            float lastReceived = float.NegativeInfinity;
            // Twelve seconds, well beyond the host's five-second packet lifetime.
            for (int frame = 0; frame <= 120; frame++)
            {
                float now = frame / 10f;
                client.Tick(now, over);
                if (NetworkClient.Sent.Count > 0) lastReceived = NetworkClient.Sent[NetworkClient.Sent.Count - 1].Time;
                Assert.True(now - lastReceived < 5f, "An unchanged pressure report expired on the host");
            }
            Assert.True(NetworkClient.Sent.Count > 2);
            for (int i = 0; i < NetworkClient.Sent.Count; i++)
            {
                Assert.Equal(expected, NetworkClient.Sent[i].Over);
                if (i > 0) Assert.True(NetworkClient.Sent[i].Time - NetworkClient.Sent[i - 1].Time >= 1f);
            }
        }

        [Fact]
        public void Pressure_transitions_obey_the_one_second_rate_limit()
        {
            var client = Start();
            client.Tick(0f, 0f);
            client.Tick(0.5f, 1f);
            client.Tick(1f, 1f);
            client.Tick(1.5f, 0f);
            client.Tick(2f, 0f);
            Assert.Equal(new byte[] { 0, 255, 0 }, NetworkClient.Sent.ConvertAll(packet => packet.Over));
        }

        [Fact]
        public void Reset_requires_new_capability_and_reports_immediately_after_negotiation()
        {
            var client = Start();
            client.Tick(10f, 1f);
            client.ResetConnection();
            client.Tick(10.1f, 1f);
            Assert.Single(NetworkClient.Sent);
            client.Enable();
            client.Tick(10.2f, 1f);
            Assert.Equal(2, NetworkClient.Sent.Count);
            Assert.Equal(255, NetworkClient.Sent[1].Over);
        }

        [Fact]
        public void Failed_heartbeat_is_retried_without_exceeding_the_rate_limit()
        {
            var client = Start();
            client.Tick(0f, 1f);
            NetworkClient.FailSend = true;
            Assert.Throws<InvalidOperationException>(() => client.Tick(1f, 1f));
            NetworkClient.FailSend = false;
            client.Tick(1.5f, 1f);
            Assert.Equal(2, NetworkClient.Attempts);
            client.Tick(2f, 1f);
            Assert.Equal(3, NetworkClient.Attempts);
            Assert.Equal(2, NetworkClient.Sent.Count);
        }

        [Fact]
        public void Only_connected_participants_report()
        {
            var client = Start();
            NetworkClient.isConnected = false;
            client.Tick(0f, 1f);
            NetworkClient.isConnected = true;
            NetworkClient.active = false;
            client.Tick(1f, 1f);
            NetworkClient.active = true;
            NetworkServer.active = true;
            client.Tick(2f, 1f);
            Assert.Empty(NetworkClient.Sent);
        }
    }
}

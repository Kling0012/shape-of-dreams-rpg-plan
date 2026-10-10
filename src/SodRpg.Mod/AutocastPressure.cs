using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Optional binary packet: exactly one payload byte, with Mirror's normal message/batch headers.
    public struct DreamforgePerfPressureMsg : NetworkMessage
    {
        public byte over;
    }

    internal static class AutocastPressure
    {
        private struct PeerPressure
        {
            internal DewPlayer Player;
            internal byte Over;
            internal float Seen;
        }

        private static readonly Dictionary<NetworkConnectionToClient, PeerPressure> Peers = new Dictionary<NetworkConnectionToClient, PeerPressure>();
        private static readonly Action<NetworkConnectionToClient, DreamforgePerfPressureMsg> ReceivePacket = Receive;
        private static readonly Action<NetworkConnectionToClient, DreamforgePerfPressureMsg> IgnorePacket = (connection, message) => { };
        private static readonly Action<NetworkConnectionToClient> Disconnected = OnDisconnected;
        private static readonly Action<DewPlayer> RosterChanged = OnRosterChanged;
        private static HostAuthority _host;
        private static Actor _actor;
        private static bool _failed;
        private static int _sampleFrame = -1;
        private static float _frameTimeEma;
        private static float _localOver;
        private static float _targetFrameTime = 1f / 60f;
        private static float _nextTargetRefresh;
        private static float _nextRefresh;
        private static bool _rttInitialized;
        private static float _rttBaseline;
        private static Transport _queueTransport;
        private static Func<int> _sendQueueCount;

        internal static bool AllPeersSupported { get; private set; }
        internal static float RemoteOver { get; private set; }
        internal static float NetOver { get; private set; }

        internal static float SampleFrame()
        {
            if (_sampleFrame == Time.frameCount) return _localOver;
            _sampleFrame = Time.frameCount;
            float now = Time.unscaledTime;
            if (now >= _nextTargetRefresh)
            {
                _nextTargetRefresh = now + 1f;
                _targetFrameTime = GetTargetFrameTime();
            }
            float frameTime = Time.unscaledDeltaTime;
            _frameTimeEma = _frameTimeEma > 0f ? Mathf.Lerp(_frameTimeEma, frameTime, 0.1f) : frameTime;
            _localOver = Mathf.Clamp01(_frameTimeEma / _targetFrameTime - 1f);
            return _localOver;
        }

        private static float GetTargetFrameTime()
        {
            try
            {
                var settings = DewSave.platformSettings?.graphics;
                if (settings == null) return 1f / 60f;
                int refreshRate = (int)Screen.currentResolution.refreshRateRatio.value;
                int frameLimit = settings.gameFrameLimit == -1 ? int.MaxValue : settings.gameFrameLimit;
                float desiredFrameRate = Mathf.Min(Mathf.Min(refreshRate, frameLimit), 90);
                return desiredFrameRate > 0f ? 1f / desiredFrameRate : 1f / 60f;
            }
            catch { return 1f / 60f; }
        }

        internal static void TickHost(HostAuthority host, Actor actor)
        {
            SampleFrame();
            if (actor == null)
            {
                DetachHost(host);
                return;
            }
            if (_failed) return;
            try
            {
                if (!ReferenceEquals(host, _host) || !ReferenceEquals(actor, _actor))
                {
                    DetachHost(_host);
                    _host = host;
                    _actor = actor;
                    Writer<DreamforgePerfPressureMsg>.write = (writer, message) => writer.WriteByte(message.over);
                    Reader<DreamforgePerfPressureMsg>.read = reader => new DreamforgePerfPressureMsg { over = reader.ReadByte() };
                    NetworkServer.ReplaceHandler(ReceivePacket);
                    NetworkServer.OnDisconnectedEvent += Disconnected;
                    DewPlayer.onHumanPlayerAdded += RosterChanged;
                    DewPlayer.onHumanPlayerRemoved += RosterChanged;
                    host.AutocastPressureReady = true;
                }
                float now = Time.unscaledTime;
                if (now < _nextRefresh) return;
                _nextRefresh = now + 0.25f;
                bool supported = host.AutocastPressureReady;
                float remote = 0f;
                var players = DewPlayer.allHumanPlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == null || player == DewPlayer.local) continue;
                    if (!host.SupportsAutocastPressure(player)) supported = false;
                    var connection = player.connectionToClient;
                    if (connection != null && Peers.TryGetValue(connection, out var pressure)
                        && pressure.Player == player && now - pressure.Seen < 5f)
                        remote = Mathf.Max(remote, pressure.Over / 255f);
                }
                AllPeersSupported = supported;
                RemoteOver = remote;
                if (players.Count <= 1)
                {
                    NetOver = 0f;
                    _rttInitialized = false;
                    return;
                }
                float rtt = (float)NetworkTime.rtt;
                if (float.IsNaN(rtt) || float.IsInfinity(rtt) || rtt < 0f) rtt = 0f;
                if (!_rttInitialized)
                {
                    _rttBaseline = rtt;
                    _rttInitialized = true;
                }
                NetOver = Mathf.Clamp01((rtt - _rttBaseline - 0.06f) / 0.2f);
                _rttBaseline = Mathf.Lerp(_rttBaseline, rtt, 0.05f);
                NetOver = Mathf.Max(NetOver, SampleSendQueue());
            }
            catch (Exception ex)
            {
                _failed = true;
                DetachHost(host);
                Log.Warn("Autocast pressure reporting disabled; keeping native co-op casting: " + ex.Message);
            }
        }

        private static float SampleSendQueue()
        {
            var transport = Transport.active;
            if (!ReferenceEquals(transport, _queueTransport))
            {
                _queueTransport = transport;
                _sendQueueCount = null;
                if (transport != null)
                {
                    // Use an exposed counter only. Native KCP's private LINQ sum allocates,
                    // and a transport without a public allocation-free counter contributes zero.
                    var getter = transport.GetType().GetProperty("SendQueueCount", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
                    if (getter != null && getter.ReturnType == typeof(int))
                        try { _sendQueueCount = AccessTools.MethodDelegate<Func<int>>(getter, transport); } catch (Exception) { }
                }
            }
            if (_sendQueueCount == null) return 0f;
            try { return Mathf.Clamp01(_sendQueueCount() / 200f); }
            catch { _sendQueueCount = null; return 0f; }
        }

        private static void Receive(NetworkConnectionToClient connection, DreamforgePerfPressureMsg message)
        {
            var host = _host;
            if (host == null || !host.AutocastPressureReady || connection == null) return;
            var player = connection.GetPlayer();
            if (player == null || !player.isHumanPlayer || player == DewPlayer.local
                || !host.SupportsAutocastPressure(player)) return;
            Peers[connection] = new PeerPressure { Player = player, Over = message.over, Seen = Time.unscaledTime };
            _nextRefresh = 0f;
        }

        private static void OnDisconnected(NetworkConnectionToClient connection)
        {
            if (Peers.TryGetValue(connection, out var pressure)) _host?.ForgetAutocastPressure(pressure.Player);
            else
            {
                var player = connection.GetPlayer();
                if (player != null) _host?.ForgetAutocastPressure(player);
            }
            Peers.Remove(connection);
            AllPeersSupported = false;
            RemoteOver = 0f;
            _nextRefresh = 0f;
        }

        private static void OnRosterChanged(DewPlayer player)
        {
            AllPeersSupported = false;
            _nextRefresh = 0f;
            if (player != null && !DewPlayer.allHumanPlayers.Contains(player))
            {
                _host?.ForgetAutocastPressure(player);
                if (player.connectionToClient != null) Peers.Remove(player.connectionToClient);
            }
        }

        internal static void DetachHost(HostAuthority host)
        {
            if (host != null) host.AutocastPressureReady = false;
            if (!ReferenceEquals(host, _host)) return;
            if (_host != null)
            {
                // Keep queued optional packets consumable until Mirror's own shutdown.
                if (NetworkServer.active) NetworkServer.ReplaceHandler(IgnorePacket);
                NetworkServer.OnDisconnectedEvent -= Disconnected;
                DewPlayer.onHumanPlayerAdded -= RosterChanged;
                DewPlayer.onHumanPlayerRemoved -= RosterChanged;
            }
            _host = null;
            _actor = null;
            Peers.Clear();
            AllPeersSupported = false;
            RemoteOver = NetOver = 0f;
            _rttInitialized = false;
            _nextRefresh = 0f;
            _queueTransport = null;
            _sendQueueCount = null;
        }
    }

    internal sealed partial class ClientSession
    {
        private void TickAutocastPressureReport()
        {
            float over = AutocastPressure.SampleFrame();
            if (NetworkServer.active || !NetworkClient.active || !NetworkClient.isConnected
                || !_hostAutocastPressureCapability || _clientRpcOn == null) return;
            float now = Time.unscaledTime;
            if (now < _nextAutocastPressureReport) return;
            _nextAutocastPressureReport = now + 1f;
            byte quantized = (byte)Mathf.RoundToInt(over * 255f);
            if (quantized == _lastAutocastPressure) return;
            Writer<DreamforgePerfPressureMsg>.write = (writer, message) => writer.WriteByte(message.over);
            NetworkClient.Send(new DreamforgePerfPressureMsg { over = quantized });
            _lastAutocastPressure = quantized;
        }
    }
}

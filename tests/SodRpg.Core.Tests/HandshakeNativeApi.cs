using System;
using System.Collections.Generic;

namespace SodRpg.Mod
{
    internal static partial class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        public static void Warn(string message) => Warnings.Add(message);
    }

    internal partial class Actor
    {
        internal readonly Dictionary<Type, object> ServerHandlers = new Dictionary<Type, object>();
        internal readonly List<object> ClientMessages = new List<object>();
        internal readonly List<DewPlayer> ClientRecipients = new List<DewPlayer>();
        public void CustomRpc_RegisterServerMessageHandler<T>(string name, Action<T, DewPlayer> handler)
            => ServerHandlers.Add(typeof(T), handler);
        public void CustomRpc_UnregisterServerMessageHandler<T>(Action<T, DewPlayer> handler)
        {
            if (ServerHandlers.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, handler))
                ServerHandlers.Remove(typeof(T));
        }
        public void CustomRpc_SendMessageToClient<T>(DewPlayer recipient, T message)
        {
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));
            ClientMessages.Add(message);
            ClientRecipients.Add(recipient);
        }
    }

    internal sealed partial class HostAuthority
    {
        private Actor _registeredOn;
        internal Actor RegisterNegotiation()
        {
            _registeredOn = new Actor();
            RegisterHello(_registeredOn);
            return _registeredOn;
        }
        // Kill-sync peers live in HostAuthority.KillSync.cs, which is not linked here.
        private void BindKillObservationSession(DewPlayer player, string observationSessionId) { }
        // Infinity's game-scene lifecycle is exercised by SodRpg.Mod.Startup.Tests.
        internal static void CheckInfinityRunCompatibility() { }
        internal void ReceiveNegotiation(DreamforgeHelloMsg message, DewPlayer player)
            => ((Action<DreamforgeHelloMsg, DewPlayer>)_registeredOn.ServerHandlers[typeof(DreamforgeHelloMsg)])(message, player);
        internal void DetachNegotiation() => UnregisterHello(_registeredOn);
    }

    internal sealed partial class DewPlayer
    {
        public static readonly List<DewPlayer> lobbyPlayers = new List<DewPlayer>();
    }

    // Product ClientSession exposes the native continue point in its hello reply.
    internal sealed partial class ClientSession
    {
        internal static string ContinueRunId, ContinueCheckpointId, ContinueResumeSession;
    }
}

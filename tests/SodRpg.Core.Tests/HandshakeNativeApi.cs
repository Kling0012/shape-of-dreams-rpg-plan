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
        internal void ReceiveNegotiation(DreamforgeHelloMsg message, DewPlayer player)
            => ((Action<DreamforgeHelloMsg, DewPlayer>)_registeredOn.ServerHandlers[typeof(DreamforgeHelloMsg)])(message, player);
        internal bool AcceptsNegotiatedBuild(DewPlayer player) => MechanismHandshakeAccepted(player);
        internal void DetachNegotiation() => UnregisterHello(_registeredOn);
    }
}

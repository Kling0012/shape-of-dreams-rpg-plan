using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Retain the optional wire message for older clients, clearing their obsolete warning.
        internal void ClearGemSlotConflict(Hero hero)
        {
            if (!NetworkServer.active || _registeredOn == null || hero == null || hero.netId == 0) return;
            var owner = hero.owner;
            // The local client runs this same mod build, whose handler decodes the message and
            // acts on nothing (it always carries our own Protocol.Version), so addressing it to
            // ourselves only paid a JSON serialize plus Mirror's local TargetRpc loopback and a
            // matching deserialize on every build application.
            if (owner == null || owner == DewPlayer.local || !owner.isHumanPlayer || owner.hero != hero) return;
            _registeredOn.CustomRpc_SendMessageToClient(owner, new DreamforgeGemSlotConflictMsg
            {
                heroNetId = hero.netId,
                protocol = Protocol.Version,
                disabled = false,
            });
        }
    }
}

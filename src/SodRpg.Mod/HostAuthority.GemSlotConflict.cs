using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Retain the optional wire message for older clients, clearing their obsolete warning.
        internal void ClearGemSlotConflict(Hero hero)
        {
            if (!NetworkServer.active || _registeredOn == null || hero == null || hero.netId == 0) return;
            var owner = hero.owner;
            if (owner == null || !owner.isHumanPlayer || owner.hero != hero) return;
            _registeredOn.CustomRpc_SendMessageToClient(owner, new DreamforgeGemSlotConflictMsg
            {
                heroNetId = hero.netId,
                protocol = Protocol.Version,
                disabled = false,
            });
        }
    }
}

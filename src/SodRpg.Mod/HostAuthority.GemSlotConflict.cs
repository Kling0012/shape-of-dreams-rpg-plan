using Mirror;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Send on every application so owners that register late receive the current latch.
        internal void SendGemSlotConflict(Hero hero, bool disabled)
        {
            if (!NetworkServer.active || _registeredOn == null || hero == null || hero.netId == 0) return;
            var owner = hero.owner;
            if (owner == null || !owner.isHumanPlayer || owner.hero != hero) return;
            _registeredOn.CustomRpc_SendMessageToClient(owner, new DreamforgeGemSlotConflictMsg
            {
                heroNetId = hero.netId,
                protocol = Protocol.Version,
                disabled = disabled,
            });
        }
    }
}

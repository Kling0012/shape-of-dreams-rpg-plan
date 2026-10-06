using System;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private Action<DreamforgeGemSlotConflictMsg> _onGemSlotConflict;

        private void RegisterGemSlotConflict(Actor actor)
        {
            if (_onGemSlotConflict == null) _onGemSlotConflict = OnGemSlotConflict;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeGemSlotConflictMsg>(_onGemSlotConflict);
        }

        private void UnregisterGemSlotConflict(Actor actor)
        {
            if (_onGemSlotConflict != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeGemSlotConflictMsg>(_onGemSlotConflict); } catch (Exception) { }
        }

        private void OnGemSlotConflict(DreamforgeGemSlotConflictMsg msg)
        {
            if (msg == null || msg.heroNetId == 0) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeGemSlotConflictMsg));
            // Older hosts may still send this optional message. Decode it for wire
            // compatibility, but a cap mismatch no longer disables or hides extra slots.
        }
    }
}

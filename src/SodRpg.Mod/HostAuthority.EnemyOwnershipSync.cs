using System;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private long _ownershipSyncRevision;
        private bool _ownershipSyncFailed;

        partial void RefreshMonsterOwnership(MonsterRuntime runtime)
        {
            string participant = null;
            int slot = 0;
            bool protectedState = false;
            if (!_ownershipSyncFailed)
            {
                try
                {
                    if (TryOwnership(runtime.Monster, OwnershipPart.Protection, out var state))
                    {
                        participant = state.Owner.guid;
                        slot = DewPlayer.gamePlayers.IndexOf(state.Owner) + 1;
                        protectedState = !string.IsNullOrEmpty(participant) && slot >= 1 && slot <= 4
                            && OwnershipProtected(state);
                    }
                }
                catch (Exception ex)
                {
                    _ownershipSyncFailed = true;
                    participant = null;
                    slot = 0;
                    protectedState = false;
                    Log.Warn("Enemy ownership display sync disabled; host rules continue: " + ex.Message);
                }
            }
            var previous = runtime.Ownership;
            if (previous.ParticipantId == participant && previous.PlayerSlot == slot && previous.Protected == protectedState) return;
            runtime.Ownership = new MonsterOwnership(participant, ++_ownershipSyncRevision, protectedState, slot);
        }

        partial void ScanMonsterOwnershipSync()
        {
            try
            {
                // Reuse the ownership scan's 0.25s cadence and KillSync's coalesced frame budget.
                foreach (var runtime in _monsters.Values) SendMonsterClassification(runtime);
            }
            catch (Exception ex)
            {
                if (!_ownershipSyncFailed)
                    Log.Warn("Enemy ownership display sync failed; host rules continue: " + ex.Message);
                _ownershipSyncFailed = true;
            }
        }
    }
}

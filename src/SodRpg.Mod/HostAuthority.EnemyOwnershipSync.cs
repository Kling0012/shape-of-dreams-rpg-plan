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
            BossShareDisplay[] shares = null;
            if (!_ownershipSyncFailed)
            {
                try
                {
                    shares = BossShareSnapshot(runtime.Monster, runtime.Ownership.BossShares);
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
                    shares = null;
                    Log.Warn("Enemy ownership display sync disabled; host rules continue: " + ex.Message);
                }
            }
            var previous = runtime.Ownership;
            if (previous.ParticipantId == participant && previous.PlayerSlot == slot && previous.Protected == protectedState
                && MonsterOwnership.SameShares(previous.BossShares, shares)) return;
            runtime.Ownership = new MonsterOwnership(participant, ++_ownershipSyncRevision, protectedState, slot, shares);
        }

        private BossShareDisplay[] BossShareSnapshot(Monster monster, BossShareDisplay[] previous)
        {
            EnemyOwnership state = null;
            try
            {
                if (!TryBossShares(monster, out state, out var shares)) return null;
                return BossShareSnapshot(state, shares, previous);
            }
            catch (Exception ex) { FailBossShares(state, ex); return null; }
        }

        private static int BossShareFraction(BossShares shares, int index)
        {
            if (shares.Capacity[index] <= 0 || shares.Remaining[index] <= 0) return 0;
            // Reserve zero for an exhausted share, even below the wire's 0.1% precision.
            return Math.Max(1, (int)Math.Round(Math.Min(1, shares.Remaining[index] / shares.Capacity[index]) * 1000));
        }

        private static BossShareDisplay[] BossShareSnapshot(EnemyOwnership state, BossShares shares, BossShareDisplay[] previous)
        {
            ReconcileBossShares(state, shares);
            int count = 0;
            bool same = previous != null;
            for (int i = 0; i < shares.Players.Length; i++)
            {
                var player = shares.Players[i];
                if (player == null) continue;
                int slot = DewPlayer.gamePlayers.IndexOf(player) + 1;
                int fraction = BossShareFraction(shares, i);
                if (previous == null || count >= previous.Length || previous[count].participantId != player.guid
                    || previous[count].playerSlot != slot || previous[count].remaining != fraction) same = false;
                count++;
            }
            if (same && previous.Length == count) return previous;
            var result = new BossShareDisplay[count];
            int next = 0;
            for (int i = 0; i < shares.Players.Length; i++)
            {
                var player = shares.Players[i];
                if (player == null) continue;
                result[next++] = new BossShareDisplay { participantId = player.guid,
                    playerSlot = DewPlayer.gamePlayers.IndexOf(player) + 1,
                    remaining = BossShareFraction(shares, i) };
            }
            return result;
        }

        partial void ScanMonsterOwnershipSync()
        {
            try
            {
                // Reuse the ownership scan's 0.25s cadence and KillSync's coalesced frame budget.
                foreach (var runtime in _monsters.Values) SendMonsterClassification(runtime);
                SyncDynamicDifficultyLegend();
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

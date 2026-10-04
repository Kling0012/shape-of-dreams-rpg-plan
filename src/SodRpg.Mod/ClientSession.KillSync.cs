using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private readonly MonsterAuthorityState _monsterAuthority = new MonsterAuthorityState();
        private readonly KillClassificationLedger _killClassifications = new KillClassificationLedger();
        private bool HasPendingKillClassification => _killClassifications.PendingCount > 0;
        internal static IEnumerable<AuthoritativeRunKill> ReplayableHostKillFacts =>
            _hostSession != null ? _hostSession._killClassifications.Facts : Array.Empty<AuthoritativeRunKill>();

        private bool ObserveMonsterAuthority(ulong generation)
        {
            if (!_monsterAuthority.Observe(generation, out bool changed)) return false;
            if (!changed) return true;
            Nightmare.Clear();
            NightmareSeenAt.Clear();
            ClearVariants();
            ClearMonsterCues();
            _buildDirty = true;
            return true;
        }

        private void ResetMonsterAuthorityConnection() => _monsterAuthority.ResetConnection();

        private void OnMonsterKill(DreamforgeMonsterKillMsg msg)
        {
            if (msg == null || msg.protocol != Protocol.Version || msg.netId == 0
                || string.IsNullOrEmpty(msg.runId) || string.IsNullOrEmpty(msg.eventId)) return;
            string gameRunId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(gameRunId) || msg.runId != gameRunId || msg.runId == _completedRunId) return;
            if (!ObserveMonsterAuthority(msg.authorityGeneration)) return;
            ReceiveAuthoritativeKill(msg.ToFact());
        }

        internal static void PublishHostKillFact(AuthoritativeRunKill fact)
        {
            if (NetworkServer.active && _hostSession != null) _hostSession.ReceiveAuthoritativeKill(fact);
        }

        private void ReceiveAuthoritativeKill(AuthoritativeRunKill fact)
        {
            if (!_killClassifications.ReceiveFact(fact)) return;
            ResolveClassifiedDeaths();
            MarkDirty(false);
            SaveNow();
        }

        private void CaptureNativeKill(uint monsterNetId, PendingRunKill kill)
        {
            if (!_killClassifications.ObserveDeath(new PendingMonsterDeath(monsterNetId, kill))) return;
            ResolveClassifiedDeaths();
            MarkDirty(false);
            SaveNow();
        }

        private void ResolveClassifiedDeaths()
        {
            while (_killClassifications.TryResolve(out var kill)) _pendingRunRewards.Add(kill);
            FlushPendingRunRewards();
            TryFinishSecureArrival();
            TryConcludeRun();
        }

        private void CaptureKillClassification() => Profile.KillClassification = _killClassifications.Capture();
        private void RestoreKillClassification()
        {
            _killClassifications.Restore(Profile.KillClassification);
            // A restored eligible death and its already-cached fact may have arrived in either order.
            while (_killClassifications.TryResolve(out var kill)) _pendingRunRewards.Add(kill);
        }

    }
}

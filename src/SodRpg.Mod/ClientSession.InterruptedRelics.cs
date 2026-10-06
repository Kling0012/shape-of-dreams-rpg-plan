using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private List<GameEvent> _interruptedRelicNotices;
        private long _interruptedRelicSaveRevision;
        private float _nextInterruptedRelicSave;

        public bool HasInterruptedRelics => InGame && Profile.Run != null
            && Profile.Run.RunId == ActiveRunId
            && Profile.Run.RunId == NetworkedManagerBase<GameManager>.softInstance?.runId
            && Profile.InterruptedRelics.Count != 0;

        public bool CanClaimInterruptedRelics => HasInterruptedRelics && RunActive && !LobbyReturnPending
            && _interruptedRelicNotices == null && !HasPendingTrades && !_coopNativeSaving
            && LocalHero != null && LocalHero.isActive && !LocalHero.isKnockedOut
            && (_zone == null || !_zone.isInAnyTransition)
            && Rules.CanClaimInterruptedRelics(Profile);

        public string ClaimInterruptedRelics()
        {
            Profile before = null;
            bool submitted = false;
            try
            {
                if (!CanClaimInterruptedRelics)
                    return Loc.T("今は中断した遠征の遺物を受け取れません。再開・保存・取引の完了を待ってください。",
                        "Interrupted expedition relics are unavailable now. Wait for restoration, saving or trade to finish.");
                before = Profile.Clone();
                var notices = Rules.ClaimInterruptedRelics(Profile, _trades.ReservedSalvageUids());
                bool saved = SaveNow(true);
                long revision = Profile.Revision;
                submitted = _writer != null && revision > before.Revision && _writer.EnqueuedRevision >= revision;
                if (saved)
                {
                    Emit(notices);
                    return null;
                }
                if (submitted && !_writer.HasFailedRevision(revision))
                {
                    // A confirmation timeout does not cancel the worker. Do not restore rights
                    // that may already be on disk, or roll back unrelated play on a later tick.
                    _interruptedRelicNotices = notices;
                    _interruptedRelicSaveRevision = revision;
                    _nextInterruptedRelicSave = Time.unscaledTime + 5f;
                    return InterruptedRelicSavePending();
                }
                RestoreInterruptedRelicClaim(before);
                return InterruptedRelicSaveFailed();
            }
            catch (Exception ex)
            {
                Log.Error("Interrupted relic claim: " + ex);
                if (before != null && !submitted) RestoreInterruptedRelicClaim(before);
                return Loc.T("遺物の受け取りに失敗しました：", "Could not claim interrupted relics: ") + ex.Message;
            }
        }

        private void RestoreInterruptedRelicClaim(Profile before)
        {
            // The writer's revisions must remain monotonic even when the economic mutation fails.
            long revision = Profile.Revision;
            Profile.RestoreFrom(before);
            Profile.Revision = Math.Max(revision, Profile.Revision);
            _dirty = true;
            _buildCacheFrame = -1;
            DeferSave();
        }

        private static string InterruptedRelicSaveFailed() => Loc.T(
            "受け取りを保存できませんでした。遺物は受け取る前の状態に戻しました。保存先を確認して再試行してください。",
            "The claim could not be saved. Your relics were restored. Check the save location and retry.");

        private static string InterruptedRelicSavePending() => Loc.T(
            "受け取りの保存確認を待っています。遺物はこの鞄に保持し、保存を再試行します。再度の受け取りはできません。",
            "Waiting to confirm the claim save. Relics remain in this satchel while saving retries; claiming again is blocked.");

        private void TickInterruptedRelics()
        {
            if (_interruptedRelicNotices == null) return;
            if (_writer != null && _writer.WrittenRevision >= _interruptedRelicSaveRevision)
            {
                var notices = _interruptedRelicNotices;
                _interruptedRelicNotices = null;
                Emit(notices);
                return;
            }
            if (Time.unscaledTime < _nextInterruptedRelicSave) return;
            _nextInterruptedRelicSave = Time.unscaledTime + 5f;
            // Keep the consumed receipt and current gameplay together on uncertain/late failures.
            SaveNow();
        }
    }
}

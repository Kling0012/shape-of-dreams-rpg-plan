using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private List<GameEvent> _directStashNotices;
        private long _directStashSaveRevision;
        private float _nextDirectStashSave;

        public bool HasDirectStash => InGame && Profile.Run != null
            && Profile.Run.RunId == ActiveRunId
            && Profile.Run.RunId == NetworkedManagerBase<GameManager>.softInstance?.runId;

        public bool DirectStashUsedThisRun => Profile.Run != null
            && Profile.DirectStashUsedRunIds.Contains(Profile.Run.RunId);

        public bool CanStashSatchelNow => HasDirectStash && !DirectStashUsedThisRun && RunActive && !LobbyReturnPending
            && _interruptedRelicNotices == null && _directStashNotices == null && !HasPendingTrades && !_coopNativeSaving
            && LocalHero != null && LocalHero.isActive && !LocalHero.isKnockedOut
            && (_zone == null || !_zone.isInAnyTransition)
            // Live salvage reservations imply HasPendingTrades (already blocked above), so no set is built per frame.
            && Rules.CanStashSatchelNow(Profile, null);

        public string StashSatchelNow()
        {
            Profile before = null;
            bool submitted = false;
            try
            {
                if (!CanStashSatchelNow)
                    return Loc.T("今は鞄の遺物を保管庫へ送れません。再開・保存・取引の完了を待ってください。",
                        "Satchel relics cannot be sent to the stash now. Wait for restoration, saving or trade to finish.");
                before = Profile.Clone();
                var notices = Rules.StashSatchelNow(Profile, _trades.ReservedSalvageUids());
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
                    _directStashNotices = notices;
                    _directStashSaveRevision = revision;
                    _nextDirectStashSave = Time.unscaledTime + 5f;
                    return DirectStashSavePending();
                }
                RestoreDirectStash(before);
                return DirectStashSaveFailed();
            }
            catch (Exception ex)
            {
                Log.Error("Direct stash: " + ex);
                if (before != null && !submitted) RestoreDirectStash(before);
                return Loc.T("鞄の遺物を保管庫へ送れませんでした：", "Could not send satchel relics to the stash: ") + ex.Message;
            }
        }

        private void RestoreDirectStash(Profile before)
        {
            // The writer's revisions must remain monotonic even when the economic mutation fails.
            long revision = Profile.Revision;
            Profile.RestoreFrom(before);
            Profile.Revision = Math.Max(revision, Profile.Revision);
            _dirty = true;
            _buildCacheFrame = -1;
            DeferSave();
        }

        private static string DirectStashSaveFailed() => Loc.T(
            "保管庫への送信を保存できませんでした。遺物は鞄に戻しました。保存先を確認して再試行してください。",
            "The transfer could not be saved. Your relics were restored to the satchel. Check the save location and retry.");

        private static string DirectStashSavePending() => Loc.T(
            "保管庫への送信の保存確認を待っています。遺物は保管庫に保持し、保存を再試行します。もう一度送ることはできません。",
            "Waiting to confirm the transfer save. Relics remain in the stash while saving retries; sending again is blocked.");

        private void TickDirectStash()
        {
            if (_directStashNotices == null) return;
            if (_writer != null && _writer.WrittenRevision >= _directStashSaveRevision)
            {
                var notices = _directStashNotices;
                _directStashNotices = null;
                Emit(notices);
                return;
            }
            if (Time.unscaledTime < _nextDirectStashSave) return;
            _nextDirectStashSave = Time.unscaledTime + 5f;
            // Keep the consumed use and current gameplay together on uncertain/late failures.
            SaveNow();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>An immutable, non-recursive MOD snapshot paired with a native continue point.</summary>
    public sealed class RunCheckpoint
    {
        public const int MaximumHistory = 2;
        public string Id { get; }
        public string RunId { get; }
        public string Snapshot { get; }

        internal RunCheckpoint(string id, string runId, string snapshot)
        {
            Id = id;
            RunId = runId;
            Snapshot = snapshot;
        }

        public static RunCheckpoint Capture(Profile profile, string id)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Checkpoint ID is required.", nameof(id));
            if (string.IsNullOrEmpty(profile.Run?.RunId)) throw new InvalidOperationException("There is no active expedition to capture.");
            return new RunCheckpoint(id, profile.Run.RunId, ProfileCodec.WriteCheckpointProfile(profile));
        }

        /// <summary>
        /// Restores rewards and reservations together, retaining the slot's Profile reference.
        /// Only a stable lobby baseline can distinguish lobby edits from later expedition rewards.
        /// Economic edits replay as one transaction; missing checkpoint inputs revert that transaction.
        /// Retained random lobby edits keep the live RNG cursor, including any post-save expedition draws.
        /// </summary>
        public Profile Restore(Profile current, Profile lobbyBaseline = null, List<string> notes = null)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            var restored = ProfileCodec.ReadCheckpointProfile(Snapshot, notes);
            if (restored.Run?.RunId != RunId) throw new LedgerFormatException("Checkpoint expedition does not match its ID.");
            if (lobbyBaseline == null && !string.IsNullOrEmpty(current.ContinueLobbyBaseline))
                lobbyBaseline = ProfileCodec.ReadCheckpointProfile(current.ContinueLobbyBaseline, notes);
            if (lobbyBaseline != null)
            {
                if (!ReplayLobbyEconomy(restored, lobbyBaseline, current))
                    notes?.Add(Loc.T("続きの保存にない遺物や素材を使ったため、ロビーでの鍛冶・取引の変更を戻しました。",
                        "Lobby crafting and trade changes were reverted because their relics or materials are not available at the continue point."));
                ReplayLobbyHeroes(restored, lobbyBaseline, current);
            }
            // Preferences are not expedition earnings. Revision remains monotonic for disk reconciliation.
            restored.Revision = current.Revision;
            restored.Japanese = current.Japanese;
            restored.HintsOff = current.HintsOff;
            restored.BulkSalvageMaxRarity = current.BulkSalvageMaxRarity;
            restored.Focus = current.Focus;
            restored.StartDepth = current.StartDepth;
            restored.LastDreamDepth = current.LastDreamDepth;
            restored.LastInfinityEnabled = current.LastInfinityEnabled;
            restored.LastInfinityInterval = current.LastInfinityInterval;
            restored.SeenHints.UnionWith(current.SeenHints);
            restored.LobbyReturnedRunIds.UnionWith(current.LobbyReturnedRunIds);
            restored.ContinueCheckpoints.AddRange(current.ContinueCheckpoints);
            restored.ContinueLobbyBaseline = null;
            restored.ContinueResumeSession = current.ContinueResumeSession;
            foreach (var hero in restored.Heroes.Values)
                for (int slot = 0; slot < hero.Equipped.Length; slot++)
                {
                    var relic = restored.FindStash(hero.Equipped[slot]);
                    if (relic == null || (int)relic.Slot != slot
                        || restored.PendingTrades.Any(trade => trade.Uid == relic.Uid)) hero.Equipped[slot] = null;
                }
            current.RestoreFrom(restored);
            return current;
        }

        private static bool ReplayLobbyEconomy(Profile restored, Profile baseline, Profile current)
        {
            bool changed = !baseline.Materials.SequenceEqual(current.Materials)
                || !baseline.Upgrades.SequenceEqual(current.Upgrades)
                || !baseline.Stash.Select(ProfileCodec.CheckpointRelicKey).SequenceEqual(current.Stash.Select(ProfileCodec.CheckpointRelicKey))
                || !baseline.LostAndFound.Select(ProfileCodec.CheckpointRelicKey).SequenceEqual(current.LostAndFound.Select(ProfileCodec.CheckpointRelicKey))
                || !baseline.FeatsClaimed.SetEquals(current.FeatsClaimed)
                || !baseline.Codex.SetEquals(current.Codex)
                || baseline.Stats.RelicsAwakened != current.Stats.RelicsAwakened
                || baseline.Stats.RelicsFound != current.Stats.RelicsFound
                || baseline.Stats.LegendariesFound != current.Stats.LegendariesFound
                || ProfileCodec.CheckpointRetuneKey(baseline.RetuneOffer) != ProfileCodec.CheckpointRetuneKey(current.RetuneOffer)
                || ProfileCodec.CheckpointTradeKey(baseline.PendingTrades) != ProfileCodec.CheckpointTradeKey(current.PendingTrades)
                || ProfileCodec.CheckpointSalvageKey(baseline.PendingSalvage) != ProfileCodec.CheckpointSalvageKey(current.PendingSalvage);
            if (!changed) return true;
            // Never overlay one half of an unresolved reservation or import a live request into an old save.
            string baselineTrades = ProfileCodec.CheckpointTradeKey(baseline.PendingTrades);
            string baselineSalvage = ProfileCodec.CheckpointSalvageKey(baseline.PendingSalvage);
            if (baselineTrades != ProfileCodec.CheckpointTradeKey(current.PendingTrades)
                || baselineTrades != ProfileCodec.CheckpointTradeKey(restored.PendingTrades)
                || baselineSalvage != ProfileCodec.CheckpointSalvageKey(current.PendingSalvage)
                || baselineSalvage != ProfileCodec.CheckpointSalvageKey(restored.PendingSalvage)) return false;

            var balances = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var materialIds = new HashSet<string>(baseline.Materials.Keys, StringComparer.Ordinal);
            materialIds.UnionWith(current.Materials.Keys);
            foreach (string id in materialIds)
            {
                long balance = (long)restored.Material(id) + current.Material(id) - baseline.Material(id);
                if (balance < 0 || balance > int.MaxValue) return false;
                balances[id] = (int)balance;
            }
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relic in restored.Stash) occupied.Add(relic.Uid);
            foreach (var relic in restored.LostAndFound) occupied.Add(relic.Uid);
            if (restored.Run != null)
            {
                foreach (var relic in restored.Run.Satchel) occupied.Add(relic.Uid);
                foreach (var relic in restored.Run.DeferredWaypointRelics) occupied.Add(relic.Uid);
            }
            foreach (var pending in restored.PendingSalvage) occupied.Add(pending.Relic.Uid);
            if (!CanReplayInventory(restored.Stash, baseline.Stash, current.Stash, occupied)
                || !CanReplayInventory(restored.LostAndFound, baseline.LostAndFound, current.LostAndFound, occupied)) return false;
            foreach (string id in current.FeatsClaimed)
                if (!baseline.FeatsClaimed.Contains(id)
                    && (!restored.Feats.Contains(id) || restored.FeatsClaimed.Contains(id))) return false;
            foreach (var pair in baseline.Upgrades)
                if (Upgrade(current, pair.Key) != pair.Value && Upgrade(restored, pair.Key) != pair.Value) return false;
            foreach (var pair in current.Upgrades)
                if (Upgrade(baseline, pair.Key) != pair.Value && Upgrade(restored, pair.Key) != Upgrade(baseline, pair.Key)) return false;

            foreach (var pair in balances) restored.Materials[pair.Key] = pair.Value;
            ReplayInventory(restored.Stash, baseline.Stash, current.Stash);
            ReplayInventory(restored.LostAndFound, baseline.LostAndFound, current.LostAndFound);
            var upgradeIds = new HashSet<Upgrade>(baseline.Upgrades.Keys);
            upgradeIds.UnionWith(current.Upgrades.Keys);
            foreach (var id in upgradeIds)
                if (Upgrade(current, id) != Upgrade(baseline, id)) restored.Upgrades[id] = Upgrade(current, id);
            foreach (string id in current.FeatsClaimed)
                if (!baseline.FeatsClaimed.Contains(id)) restored.FeatsClaimed.Add(id);
            foreach (string id in current.Codex)
                if (!baseline.Codex.Contains(id)) restored.Codex.Add(id);
            restored.Stats.RelicsAwakened = AddDelta(restored.Stats.RelicsAwakened, baseline.Stats.RelicsAwakened, current.Stats.RelicsAwakened);
            restored.Stats.RelicsFound = AddDelta(restored.Stats.RelicsFound, baseline.Stats.RelicsFound, current.Stats.RelicsFound);
            restored.Stats.LegendariesFound = AddDelta(restored.Stats.LegendariesFound, baseline.Stats.LegendariesFound, current.Stats.LegendariesFound);
            if (ProfileCodec.CheckpointRetuneKey(baseline.RetuneOffer) != ProfileCodec.CheckpointRetuneKey(current.RetuneOffer))
                restored.RetuneOffer = current.RetuneOffer?.Clone();
            // Results and their RNG consumption must survive together. Do not add draw counts:
            // the live cursor can also include expedition draws after this checkpoint.
            // Non-random lobby edits and rejected transactions keep the checkpoint cursor.
            if (current.RngState != baseline.RngState) restored.RngState = current.RngState;
            if (restored.RetuneOffer != null && restored.FindStash(restored.RetuneOffer.Uid) == null) restored.RetuneOffer = null;
            restored.StarterUids.RemoveAll(uid => restored.FindStash(uid) == null);
            return true;
        }

        private static bool CanReplayInventory(List<Relic> restored, List<Relic> baseline, List<Relic> current, HashSet<string> occupied)
        {
            foreach (var original in baseline)
            {
                var edited = current.Find(relic => relic.Uid == original.Uid);
                if (SameRelic(original, edited)) continue;
                if (!SameRelic(original, restored.Find(relic => relic.Uid == original.Uid))) return false;
            }
            foreach (var added in current)
                if (!baseline.Any(relic => relic.Uid == added.Uid) && !occupied.Add(added.Uid)) return false;
            return true;
        }

        private static void ReplayInventory(List<Relic> restored, List<Relic> baseline, List<Relic> current)
        {
            foreach (var original in baseline)
            {
                var edited = current.Find(relic => relic.Uid == original.Uid);
                if (SameRelic(original, edited)) continue;
                int index = restored.FindIndex(relic => relic.Uid == original.Uid);
                if (edited == null) restored.RemoveAt(index);
                else restored[index] = edited.Clone();
            }
            foreach (var added in current)
                if (!baseline.Any(relic => relic.Uid == added.Uid)) restored.Add(added.Clone());
        }

        private static bool SameRelic(Relic left, Relic right) =>
            ProfileCodec.CheckpointRelicKey(left) == ProfileCodec.CheckpointRelicKey(right);

        private static int Upgrade(Profile profile, Upgrade id) => profile.Upgrades.TryGetValue(id, out int level) ? level : 0;
        private static int AddDelta(int value, int baseline, int current) =>
            (int)Math.Max(0, Math.Min(int.MaxValue, (long)value + current - baseline));

        private static void ReplayLobbyHeroes(Profile restored, Profile baseline, Profile current)
        {
            foreach (var pair in current.Heroes)
            {
                var before = baseline.Heroes.TryGetValue(pair.Key, out var old) ? old : new HeroState();
                var target = restored.Hero(pair.Key);
                var edited = pair.Value;
                for (int slot = 0; slot < target.Equipped.Length; slot++)
                    if (before.Equipped[slot] != edited.Equipped[slot]) target.Equipped[slot] = edited.Equipped[slot];
            }
        }
    }
}

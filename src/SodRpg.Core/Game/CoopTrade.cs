using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    public sealed class CoopTradeOffer
    {
        public List<string> RelicUids { get; set; } = new List<string>();
        public int Shards { get; set; }
        public int Tuning { get; set; }

        public CoopTradeOffer Clone() => new CoopTradeOffer
        {
            RelicUids = new List<string>(RelicUids), Shards = Shards, Tuning = Tuning,
        };
    }

    public sealed class CoopTradeReceipt
    {
        public string Id { get; set; }
        public string HostKey { get; set; }
        public string FirstKey { get; set; }
        public string SecondKey { get; set; }
        public CoopTradeOffer FirstOffer { get; set; }
        public CoopTradeOffer SecondOffer { get; set; }
        // Canonical, frozen BEFORE-escrow snapshots. Neither is an instruction to overwrite a live profile.
        public string FirstProfile { get; set; }
        public string SecondProfile { get; set; }
    }

    public enum CoopTradeReturnTarget { Stash, Satchel }

    public sealed class CoopTradeReservedRelic
    {
        public Relic Relic { get; set; }
        public CoopTradeReturnTarget Target { get; set; }
        public string RunId { get; set; }
        public CoopTradeReservedRelic Clone() => new CoopTradeReservedRelic
        {
            Relic = Relic.Clone(), Target = Target, RunId = RunId,
        };
    }

    public sealed class CoopTradeReservation
    {
        public string Id { get; set; }
        public string HostKey { get; set; }
        public CoopTradeOffer Offer { get; set; }
        public int MaterialShards { get; set; }
        public int MaterialTuning { get; set; }
        public string CurrencyRunId { get; set; }
        public List<CoopTradeReservedRelic> Relics { get; } = new List<CoopTradeReservedRelic>();
        public CoopTradeReservation Clone()
        {
            var copy = new CoopTradeReservation
            {
                Id = Id, HostKey = HostKey, Offer = Offer.Clone(),
                MaterialShards = MaterialShards, MaterialTuning = MaterialTuning, CurrencyRunId = CurrencyRunId,
            };
            foreach (var relic in Relics) copy.Relics.Add(relic.Clone());
            return copy;
        }
    }

    public static class CoopTradeRules
    {
        public static long Shards(Profile profile) => (long)profile.Material(Materials.Shard) + (profile.Run?.SatchelShards ?? 0);
        public static long Tuning(Profile profile) => (long)profile.Material(Materials.Tuning) + (profile.Run?.SatchelTuning ?? 0);

        public static string Validate(Profile profile, CoopTradeOffer offer)
        {
            if (profile == null || offer == null || offer.RelicUids == null) return Loc.T("取引の提示がありません。", "Trade offer is missing.");
            if (profile.CoopTradePending != null) return Loc.T("協力取引の品をすでに預かっています。", "A cooperative trade is already reserved.");
            if (offer.Shards < 0 || offer.Tuning < 0) return Loc.T("取引の素材数に負の値は指定できません。", "Trade amounts cannot be negative.");
            if (profile.Material(Materials.Shard) < 0 || profile.Material(Materials.Tuning) < 0
                || (profile.Run?.SatchelShards ?? 0) < 0 || (profile.Run?.SatchelTuning ?? 0) < 0)
                return Loc.T("取引の素材残高が不正です。", "Trade material balances are invalid.");
            if (Shards(profile) < offer.Shards || Tuning(profile) < offer.Tuning)
                return Loc.T("取引に必要な素材が足りません。", "Not enough trade materials.");
            string inventoryError = ValidateInventory(profile);
            if (inventoryError != null) return inventoryError;
            var offered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string uid in offer.RelicUids)
            {
                if (string.IsNullOrEmpty(uid) || !offered.Add(uid)) return Loc.T("同じ遺物を二度提示できません。", "Trade relic IDs must be unique.");
                if (FindOffered(profile, uid) == null) return Loc.T("提示した遺物が保管庫や鞄にありません。", "An offered relic is not in your stash or satchel.");
                if (profile.IsEquippedAnywhere(uid)) return Loc.T("装着中の遺物は取引できません。", "An offered relic is equipped.");
                if (profile.PendingTrades.Any(t => t.Uid == uid || t.Relic?.Uid == uid)
                    || profile.RetuneOffer?.Uid == uid) return Loc.T("提示した遺物は別の処理の応答待ちです。", "An offered relic has another pending operation.");
            }
            return null;
        }

        public static CoopTradeReceipt CreateReceipt(string id, string hostKey, string firstKey, Profile first,
            CoopTradeOffer firstOffer, string secondKey, Profile second, CoopTradeOffer secondOffer)
        {
            RequireKeys(id, hostKey, firstKey, secondKey);
            first = first?.Clone();
            second = second?.Clone();
            Rules.SettleSatchelOverflow(first);
            Rules.SettleSatchelOverflow(second);
            RequireValid(Validate(first, firstOffer));
            RequireValid(Validate(second, secondOffer));
            RequireValid(ValidateExchange(first, firstOffer, second, secondOffer));
            RequireValid(ValidateExchange(second, secondOffer, first, firstOffer));
            if (first.CoopTradeExecuted.Contains(id) || second.CoopTradeExecuted.Contains(id))
                throw new InvalidOperationException(Loc.T("この協力取引はすでに完了しています。", "This cooperative trade has already executed."));
            var receipt = new CoopTradeReceipt
            {
                Id = id, HostKey = hostKey, FirstKey = firstKey, SecondKey = secondKey,
                FirstOffer = firstOffer.Clone(), SecondOffer = secondOffer.Clone(),
                FirstProfile = ProfileCodec.Write(first), SecondProfile = ProfileCodec.Write(second),
            };
            ValidateReceipt(receipt, out _, out _);
            return receipt;
        }

        public static bool Prepare(Profile profile, string id, string hostKey, CoopTradeOffer own)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(hostKey)) throw new InvalidOperationException(Loc.T("取引の識別情報がありません。", "Trade identity is missing."));
            if (profile.CoopTradeExecuted.Contains(id)) return false;
            if (profile.CoopTradePending != null)
            {
                if (profile.CoopTradePending.Id == id && profile.CoopTradePending.HostKey == hostKey
                    && SameOffer(profile.CoopTradePending.Offer, own)) return false;
                throw new InvalidOperationException(Loc.T("別の協力取引の品をすでに預かっています。", "Another cooperative trade is already reserved."));
            }
            var next = profile.Clone();
            Rules.SettleSatchelOverflow(next);
            RequireValid(Validate(next, own));
            var reservation = new CoopTradeReservation
            {
                Id = id, HostKey = hostKey, Offer = own.Clone(),
                MaterialShards = Math.Min(next.Material(Materials.Shard), own.Shards),
                MaterialTuning = Math.Min(next.Material(Materials.Tuning), own.Tuning),
                CurrencyRunId = next.Run?.RunId,
            };
            foreach (string uid in own.RelicUids)
            {
                var relic = next.FindStash(uid);
                bool stash = relic != null;
                if (!stash) relic = next.Run.Satchel.Find(r => r.Uid == uid);
                reservation.Relics.Add(new CoopTradeReservedRelic
                {
                    Relic = relic.Clone(), Target = stash ? CoopTradeReturnTarget.Stash : CoopTradeReturnTarget.Satchel,
                    RunId = stash ? null : next.Run.RunId,
                });
                if (stash) next.Stash.Remove(relic);
                else next.Run.Satchel.Remove(relic);
            }
            next.Materials[Materials.Shard] = next.Material(Materials.Shard) - reservation.MaterialShards;
            next.Materials[Materials.Tuning] = next.Material(Materials.Tuning) - reservation.MaterialTuning;
            if (next.Run != null)
            {
                next.Run.SatchelShards -= own.Shards - reservation.MaterialShards;
                next.Run.SatchelTuning -= own.Tuning - reservation.MaterialTuning;
            }
            next.CoopTradePending = reservation;
            Finish(profile, next);
            return true;
        }

        public static bool Resolve(Profile profile, CoopTradeReceipt receipt, string playerKey)
        {
            if (profile == null || receipt == null) throw new ArgumentNullException(profile == null ? nameof(profile) : nameof(receipt));
            if (profile.CoopTradeExecuted.Contains(receipt.Id)) return false;
            ValidateReceipt(receipt, out var first, out var second);
            bool isFirst = playerKey == receipt.FirstKey;
            if (!isFirst && playerKey != receipt.SecondKey) throw new InvalidOperationException(Loc.T("この取引の受領書は別のプレイヤーのものです。", "Receipt does not belong to this player."));
            var own = isFirst ? receipt.FirstOffer : receipt.SecondOffer;
            var incoming = isFirst ? receipt.SecondOffer : receipt.FirstOffer;
            var before = isFirst ? first : second;
            var sender = isFirst ? second : first;
            var pending = profile.CoopTradePending;
            if (pending == null || pending.Id != receipt.Id || pending.HostKey != receipt.HostKey || !SameOffer(pending.Offer, own))
                throw new InvalidOperationException(Loc.T("取引の受領書と保存済みの預かり品が一致しません。", "Receipt does not match durable escrow."));
            ValidateReservation(pending);
            if (pending.MaterialShards != Math.Min(before.Material(Materials.Shard), own.Shards)
                || pending.MaterialTuning != Math.Min(before.Material(Materials.Tuning), own.Tuning)
                || pending.CurrencyRunId != before.Run?.RunId)
                throw new InvalidOperationException(Loc.T("受領書の素材と保存済みの預かり品が一致しません。", "Receipt materials do not match durable escrow."));
            foreach (var reserved in pending.Relics)
                if (ProfileCodec.CheckpointRelicKey(reserved.Relic) != ProfileCodec.CheckpointRelicKey(FindOffered(before, reserved.Relic.Uid))
                    || (reserved.Target == CoopTradeReturnTarget.Stash) != (before.FindStash(reserved.Relic.Uid) != null)
                    || HasOwnedUid(profile, reserved.Relic.Uid)
                    || reserved.Target == CoopTradeReturnTarget.Satchel && reserved.RunId != before.Run?.RunId)
                    throw new InvalidOperationException(Loc.T("受領書の遺物と保存済みの預かり品が一致しません。", "Receipt relic does not match durable escrow."));
            if (pending.Relics.Count != own.RelicUids.Count) throw new InvalidOperationException(Loc.T("預かり品の遺物数が不正です。", "Escrow relic count is invalid."));
            RequireValid(ValidateInventory(profile));
            RequireValid(ValidateIncoming(profile, incoming, sender));
            var next = profile.Clone();
            foreach (string uid in incoming.RelicUids) next.Stash.Add(FindOffered(sender, uid).Clone());
            next.Materials[Materials.Shard] = checked(next.Material(Materials.Shard) + incoming.Shards);
            next.Materials[Materials.Tuning] = checked(next.Material(Materials.Tuning) + incoming.Tuning);
            next.CoopTradePending = null;
            next.CoopTradeExecuted.Add(receipt.Id);
            Finish(profile, next);
            return true;
        }

        public static bool Abort(Profile profile, string id)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var pending = profile.CoopTradePending;
            if (pending == null || pending.Id != id) return false;
            if (profile.CoopTradeExecuted.Contains(id)) throw new InvalidOperationException(Loc.T("完了した取引は取り消せません。", "Executed trade cannot be aborted."));
            ValidateReservation(pending);
            RequireValid(ValidateInventory(profile));
            bool sameCurrencyRun = profile.Run != null && profile.Run.RunId == pending.CurrencyRunId;
            int materialShards = sameCurrencyRun ? pending.MaterialShards : pending.Offer.Shards;
            int materialTuning = sameCurrencyRun ? pending.MaterialTuning : pending.Offer.Tuning;
            int satchelShards = pending.Offer.Shards - materialShards, satchelTuning = pending.Offer.Tuning - materialTuning;
            if ((long)profile.Material(Materials.Shard) + materialShards > int.MaxValue
                || (long)profile.Material(Materials.Tuning) + materialTuning > int.MaxValue
                || (long)(profile.Run?.SatchelShards ?? 0) + satchelShards > int.MaxValue
                || (long)(profile.Run?.SatchelTuning ?? 0) + satchelTuning > int.MaxValue)
                throw new InvalidOperationException(Loc.T("預かり素材を返すと所持数の上限を超えます。", "Returning reserved materials would overflow."));
            int stashCount = profile.Stash.Count, satchelCount = profile.Run?.Satchel.Count ?? 0;
            foreach (var reserved in pending.Relics)
            {
                if (HasOwnedUid(profile, reserved.Relic.Uid)) throw new InvalidOperationException(Loc.T("預かり遺物がすでに所持品にあります。", "Reserved relic already exists."));
                if (ReturnToSatchel(profile, reserved)) satchelCount++;
                else stashCount++;
            }
            if (stashCount > Workshop.StashCapacity(profile) || satchelCount > Workshop.SatchelCapacity(profile))
                throw new InvalidOperationException(Loc.T("預かり遺物を返すための空きがありません。", "There is no room to return reserved relics."));
            var next = profile.Clone();
            foreach (var reserved in pending.Relics)
                (ReturnToSatchel(next, reserved) ? next.Run.Satchel : next.Stash).Add(reserved.Relic.Clone());
            next.Materials[Materials.Shard] = next.Material(Materials.Shard) + materialShards;
            next.Materials[Materials.Tuning] = next.Material(Materials.Tuning) + materialTuning;
            if (next.Run != null)
            {
                next.Run.SatchelShards += satchelShards;
                next.Run.SatchelTuning += satchelTuning;
            }
            next.CoopTradePending = null;
            Finish(profile, next);
            return true;
        }

        private static bool ReturnToSatchel(Profile profile, CoopTradeReservedRelic reserved) =>
            reserved.Target == CoopTradeReturnTarget.Satchel && profile.Run != null && profile.Run.RunId == reserved.RunId;

        private static void ValidateReservation(CoopTradeReservation pending)
        {
            if (pending.Offer == null || pending.Offer.RelicUids == null
                || pending.Offer.Shards < 0 || pending.Offer.Tuning < 0
                || pending.MaterialShards < 0 || pending.MaterialShards > pending.Offer.Shards
                || pending.MaterialTuning < 0 || pending.MaterialTuning > pending.Offer.Tuning
                || (pending.MaterialShards < pending.Offer.Shards || pending.MaterialTuning < pending.Offer.Tuning)
                    && string.IsNullOrEmpty(pending.CurrencyRunId)
                || pending.Relics.Count != pending.Offer.RelicUids.Count)
                throw new InvalidOperationException(Loc.T("保存済みの取引預かり品が不正です。", "Durable trade escrow is invalid."));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reserved in pending.Relics)
                if (reserved?.Relic == null || string.IsNullOrEmpty(reserved.Relic.Uid) || !seen.Add(reserved.Relic.Uid)
                    || reserved.Target != CoopTradeReturnTarget.Stash && reserved.Target != CoopTradeReturnTarget.Satchel
                    || reserved.Target == CoopTradeReturnTarget.Satchel && string.IsNullOrEmpty(reserved.RunId)
                    || reserved.Target == CoopTradeReturnTarget.Stash && reserved.RunId != null)
                    throw new InvalidOperationException(Loc.T("保存済みの取引預かり品に不正な遺物があります。", "Durable trade escrow contains invalid relics."));
            if (!seen.SetEquals(pending.Offer.RelicUids)) throw new InvalidOperationException(Loc.T("保存済みの預かり品と取引の提示が一致しません。", "Durable trade escrow does not match its offer."));
        }

        internal static void ValidateReceipt(CoopTradeReceipt receipt, out Profile first, out Profile second)
        {
            if (receipt == null) throw new InvalidOperationException(Loc.T("取引の受領書がありません。", "Trade receipt is missing."));
            RequireKeys(receipt.Id, receipt.HostKey, receipt.FirstKey, receipt.SecondKey);
            first = ReadFrozenProfile(receipt.FirstProfile);
            second = ReadFrozenProfile(receipt.SecondProfile);
            RequireValid(Validate(first, receipt.FirstOffer));
            RequireValid(Validate(second, receipt.SecondOffer));
            if (first.CoopTradeExecuted.Contains(receipt.Id) || second.CoopTradeExecuted.Contains(receipt.Id))
                throw new InvalidOperationException(Loc.T("受領書の保存時点ですでに取引が完了しています。", "Receipt snapshot has already executed this trade."));
            RequireValid(ValidateExchange(first, receipt.FirstOffer, second, receipt.SecondOffer));
            RequireValid(ValidateExchange(second, receipt.SecondOffer, first, receipt.FirstOffer));
        }

        private static Profile ReadFrozenProfile(string text)
        {
            var notes = new List<string>();
            var profile = ProfileCodec.Read(text, notes);
            if (notes.Count != 0 || ProfileCodec.WriteCoopTradeReceiptProfile(profile.Clone()) != text)
                throw new InvalidOperationException(Loc.T("取引のプロフィールに読み取れない所持品や不正な保存情報があります。", "Trade profile snapshot is not canonical or contains unreadable assets."));
            return profile;
        }

        private static void RequireKeys(string id, string host, string first, string second)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(host) || string.IsNullOrEmpty(first)
                || string.IsNullOrEmpty(second) || first == second)
                throw new InvalidOperationException(Loc.T("取引の参加者または識別情報が不正です。", "Trade participants or identity are invalid."));
        }

        internal static bool SameOffer(CoopTradeOffer first, CoopTradeOffer second) =>
            first != null && second != null && first.RelicUids != null && second.RelicUids != null
            && first.Shards == second.Shards && first.Tuning == second.Tuning
            && first.RelicUids.Count == second.RelicUids.Count
            && new HashSet<string>(first.RelicUids, StringComparer.Ordinal).SetEquals(second.RelicUids);

        public static bool SameOfferedRelics(Profile before, Profile after, CoopTradeOffer offer)
        {
            foreach (string uid in offer.RelicUids)
            {
                var oldRelic = before == null ? null : FindOffered(before, uid);
                var newRelic = after == null ? null : FindOffered(after, uid);
                if (oldRelic == null || newRelic == null
                    || ProfileCodec.CheckpointRelicKey(oldRelic) != ProfileCodec.CheckpointRelicKey(newRelic)) return false;
            }
            return true;
        }

        private static Relic FindOffered(Profile profile, string uid) =>
            profile.FindStash(uid) ?? profile.Run?.Satchel.Find(r => r.Uid == uid);

        private static string ValidateExchange(Profile receiver, CoopTradeOffer outgoing, Profile sender, CoopTradeOffer incoming)
        {
            long shards = (long)receiver.Material(Materials.Shard) - Math.Min(receiver.Material(Materials.Shard), outgoing.Shards) + incoming.Shards;
            long tuning = (long)receiver.Material(Materials.Tuning) - Math.Min(receiver.Material(Materials.Tuning), outgoing.Tuning) + incoming.Tuning;
            if (shards < 0 || tuning < 0 || shards > int.MaxValue || tuning > int.MaxValue) return Loc.T("取引後の素材数が所持上限を超えます。", "Trade material balance would overflow.");
            int removedStash = outgoing.RelicUids.Count(uid => receiver.FindStash(uid) != null);
            if ((long)receiver.Stash.Count - removedStash + incoming.RelicUids.Count > Workshop.StashCapacity(receiver))
                return Loc.T("受け取る遺物を入れる保管庫の空きがありません。", "There is no room for received relics in the stash.");
            foreach (string uid in incoming.RelicUids)
                if (HasOwnedUid(receiver, uid)) return Loc.T("受け取る遺物と同じ個体IDの遺物をすでに所持しています。", "Received relic ID already belongs to the recipient.");
            return null;
        }

        private static string ValidateIncoming(Profile receiver, CoopTradeOffer incoming, Profile sender)
        {
            if (receiver.Material(Materials.Shard) < 0 || receiver.Material(Materials.Tuning) < 0
                || (long)receiver.Material(Materials.Shard) + incoming.Shards > int.MaxValue
                || (long)receiver.Material(Materials.Tuning) + incoming.Tuning > int.MaxValue) return Loc.T("受け取る素材が所持上限を超えます。", "Received materials would overflow.");
            if ((long)receiver.Stash.Count + incoming.RelicUids.Count > Workshop.StashCapacity(receiver)) return Loc.T("保管庫が一杯です。", "Your stash is full.");
            foreach (string uid in incoming.RelicUids)
                if (FindOffered(sender, uid) == null || HasOwnedUid(receiver, uid)
                    || receiver.CoopTradePending.Relics.Any(r => r.Relic.Uid == uid)) return Loc.T("受け取る遺物の個体IDが所持品と重複しています。", "Received relic ID conflicts with owned assets.");
            return null;
        }

        private static bool HasOwnedUid(Profile profile, string uid) =>
            profile.ContainsRelicUid(uid, includeCoopReservation: false) || profile.IsEquippedAnywhere(uid)
            || profile.PendingTrades.Any(pending => pending.Uid == uid || pending.Relic?.Uid == uid);

        private static string ValidateInventory(Profile profile)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relic in AllInventory(profile))
                if (relic == null || string.IsNullOrEmpty(relic.Uid) || !seen.Add(relic.Uid)) return Loc.T("所持品に重複した、または不正な遺物の個体IDがあります。", "Profile contains duplicate or invalid relic IDs.");
            return null;
        }

        private static IEnumerable<Relic> AllInventory(Profile profile)
        {
            foreach (var relic in profile.Stash) yield return relic;
            foreach (var relic in profile.LostAndFound) yield return relic;
            foreach (var relic in profile.InterruptedRelics) yield return relic;
            foreach (var pending in profile.PendingSalvage) yield return pending.Relic;
            foreach (var pending in profile.PendingTrades)
                if (pending.Relic != null) yield return pending.Relic;
            if (profile.Run != null)
            {
                foreach (var relic in profile.Run.Satchel) yield return relic;
                foreach (var relic in profile.Run.DeferredWaypointRelics) yield return relic;
            }
        }

        private static void RequireValid(string error)
        {
            if (error != null) throw new InvalidOperationException(error);
        }

        private static void Finish(Profile target, Profile next)
        {
            // The durable overlay has no nested Continue data and no reference to an older overlay.
            next.CoopTradeEconomy = null;
            next.CoopTradeEconomy = ProfileCodec.WriteCheckpointProfile(next);
            RefreshCheckpoints(next);
            CopyTradeState(target, next);
        }

        private static void RefreshCheckpoints(Profile profile)
        {
            // Decode and encode every replacement before publishing any of them. Run progress is retained;
            // economic state is irreversible across native Continue after exchanging with another player.
            var refreshed = new List<RunCheckpoint>(profile.ContinueCheckpoints.Count);
            foreach (var checkpoint in profile.ContinueCheckpoints)
            {
                var snapshot = ProfileCodec.ReadCheckpointProfile(checkpoint.Snapshot);
                CopyEconomics(snapshot, profile);
                CopyDurableState(snapshot, profile);
                refreshed.Add(new RunCheckpoint(checkpoint.Id, checkpoint.RunId, ProfileCodec.WriteCheckpointProfile(snapshot)));
            }
            string baseline = null;
            if (!string.IsNullOrEmpty(profile.ContinueLobbyBaseline))
            {
                var snapshot = ProfileCodec.ReadCheckpointProfile(profile.ContinueLobbyBaseline);
                CopyEconomics(snapshot, profile);
                CopyDurableState(snapshot, profile);
                baseline = ProfileCodec.WriteCheckpointProfile(snapshot);
            }
            profile.ContinueCheckpoints.Clear();
            profile.ContinueCheckpoints.AddRange(refreshed);
            profile.ContinueLobbyBaseline = baseline;
        }

        internal static void CopyDurableState(Profile target, Profile source)
        {
            target.CoopTradePending = source.CoopTradePending?.Clone();
            target.CoopTradeExecuted.Clear();
            target.CoopTradeExecuted.UnionWith(source.CoopTradeExecuted);
            target.CoopTradeEconomy = source.CoopTradeEconomy;
            CopyRelics(target.InterruptedRelics, source.InterruptedRelics);
            target.InterruptedRelicsId = source.InterruptedRelicsId;
            target.InterruptedRelicsRunId = source.InterruptedRelicsRunId;
            target.InterruptedRelicsExecuted.Clear();
            target.InterruptedRelicsExecuted.UnionWith(source.InterruptedRelicsExecuted);
            target.InterruptedRelicsClaimedRunIds.Clear();
            target.InterruptedRelicsClaimedRunIds.UnionWith(source.InterruptedRelicsClaimedRunIds);
            target.InterruptedRelicsRetiredSourceRunIds.Clear();
            target.InterruptedRelicsRetiredSourceRunIds.UnionWith(source.InterruptedRelicsRetiredSourceRunIds);
        }

        internal static void CopyEconomics(Profile target, Profile source)
        {
            // Outgoing identities may now belong to another player; do not rewind their generator.
            target.RngState = source.RngState;
            // Preserve usage from both runs when Continue retains transferred economic state.
            target.DirectStashUsedRunIds.UnionWith(source.DirectStashUsedRunIds);
            target.Materials.Clear();
            foreach (var pair in source.Materials) target.Materials.Add(pair.Key, pair.Value);
            CopyRelics(target.Stash, source.Stash);
            CopyRelics(target.LostAndFound, source.LostAndFound);
            target.PendingSalvage.Clear();
            foreach (var pending in source.PendingSalvage) target.PendingSalvage.Add(pending.Clone());
            target.PendingTrades.Clear();
            foreach (var pending in source.PendingTrades) target.PendingTrades.Add(pending.Clone());
            target.RetuneOffer = source.RetuneOffer?.Clone();
            target.UncreditedSatchelOverflowShards = source.UncreditedSatchelOverflowShards;
            target.SatchelOverflowShards = source.SatchelOverflowShards;
            target.SatchelOverflowCount = source.SatchelOverflowCount;
            target.SatchelOverflowDiscarded = source.SatchelOverflowDiscarded;
            target.OverflowBonusPendingRunId = source.OverflowBonusPendingRunId;
            target.OverflowBonusPendingLedgerId = source.OverflowBonusPendingLedgerId;
            target.OverflowBonusPendingTotal = source.OverflowBonusPendingTotal;
            target.InfinityRewardBudget = source.InfinityRewardBudget.Clone();
            target.Upgrades.Clear();
            foreach (var pair in source.Upgrades) target.Upgrades.Add(pair.Key, pair.Value);
            target.Codex.Clear();
            target.Codex.UnionWith(source.Codex);
            target.FeatsClaimed.Clear();
            target.FeatsClaimed.UnionWith(source.FeatsClaimed);
            target.Stats.RelicsFound = source.Stats.RelicsFound;
            target.Stats.RelicsAwakened = source.Stats.RelicsAwakened;
            target.Stats.LegendariesFound = source.Stats.LegendariesFound;
            if (target.Run != null)
            {
                CopyRelics(target.Run.Satchel, source.Run?.Satchel);
                CopyRelics(target.Run.DeferredWaypointRelics, source.Run?.DeferredWaypointRelics);
                target.Run.SatchelShards = source.Run?.SatchelShards ?? 0;
                target.Run.SatchelTuning = source.Run?.SatchelTuning ?? 0;
                target.Run.DeferredWaypointShards = source.Run?.DeferredWaypointShards ?? 0;
                target.Run.DeferredWaypointTuning = source.Run?.DeferredWaypointTuning ?? 0;
                target.Run.OverflowDreamDustTotal = source.Run?.OverflowDreamDustTotal ?? 0;
                target.Run.OverflowDreamDustLedgerId = source.Run?.OverflowDreamDustLedgerId ?? 0;
            }
        }

        private static void CopyRelics(List<Relic> target, IEnumerable<Relic> source)
        {
            target.Clear();
            if (source != null) foreach (var relic in source) target.Add(relic.Clone());
        }

        /// <summary>Restores economic and durable trade state after a failed profile save, without rolling back run progress.</summary>
        public static void CopyTradeState(Profile target, Profile source)
        {
            if (target == null || source == null) throw new ArgumentNullException(target == null ? nameof(target) : nameof(source));
            if (ReferenceEquals(target, source)) return;
            CopyEconomics(target, source);
            CopyDurableState(target, source);
            target.ContinueCheckpoints.Clear();
            target.ContinueCheckpoints.AddRange(source.ContinueCheckpoints);
            target.ContinueLobbyBaseline = source.ContinueLobbyBaseline;
        }
    }
}

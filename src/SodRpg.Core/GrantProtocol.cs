using System;
using System.Collections.Generic;
using System.IO;

namespace SodRpg.Core
{
    /// <summary>
    /// 報酬ID（冪等キー）。同じ出来事・同じ受取人からは、何度計算しても同じIDになる。
    /// ホストが再起動しても同じIDが再現できるため、再送・再発行は受取側の applied 判定で無害化される。
    /// </summary>
    public static class GrantId
    {
        public static string Compute(string runId, string triggerId, string rewardDefId, string recipientKey)
        {
            // 区切り文字の曖昧さを避けるため、各要素を長さ付きで連結する
            return LedgerSerializer.Sha256Hex(
                Part(runId) + Part(triggerId) + Part(rewardDefId) + Part(recipientKey));
        }

        private static string Part(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("報酬IDの要素が空です。");
            return s.Length + ":" + s + "|";
        }
    }

    /// <summary>ホスト→クライアント。内容は rewardDefId で表し、数量などは受取側の定義から引く。</summary>
    public sealed class GrantMessage
    {
        public GrantMessage(string grantId, string runId, string rewardDefId, string recipientKey)
        {
            GrantId = grantId;
            RunId = runId;
            RewardDefId = rewardDefId;
            RecipientKey = recipientKey;
        }

        public string GrantId { get; }
        public string RunId { get; }
        public string RewardDefId { get; }
        public string RecipientKey { get; }
    }

    public enum AckResult
    {
        /// <summary>今回付与して保存した。</summary>
        Applied,
        /// <summary>すでに付与済み。再付与せず、確定済みとして応答のみ返す。</summary>
        AlreadyApplied,
        /// <summary>この受取側では付与できない（未定義の報酬、上限超過など）。再送しても成功しない。</summary>
        Refused,
    }

    public sealed class AckMessage
    {
        public AckMessage(string grantId, string recipientKey, AckResult result, string detail = null)
        {
            GrantId = grantId;
            RecipientKey = recipientKey;
            Result = result;
            Detail = detail ?? string.Empty;
        }

        public string GrantId { get; }
        public string RecipientKey { get; }
        public AckResult Result { get; }
        public string Detail { get; }
    }

    public enum HostGrantState
    {
        /// <summary>ホストが確定した。ackが来るまで再送する。</summary>
        Committed,
        /// <summary>受取人が保存してackを返した。</summary>
        Closed,
        /// <summary>受取人が付与を拒否した。再送しない。</summary>
        Refused,
    }

    /// <summary>ホスト側の未配布リスト（outbox）。報酬の確定と配布状況をGrantId単位で持つ。</summary>
    public sealed class HostGrantBook
    {
        private sealed class Entry
        {
            public GrantMessage Message;
            public HostGrantState State;
            public string RefusalDetail;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        /// <summary>
        /// 同じ出来事に対する報酬を受取人ごとに確定する。同じ報酬IDがすでにあれば何もしない（二重発火の吸収）。
        /// 戻り値は新しく確定した件数。
        /// </summary>
        public int Issue(string runId, string triggerId, string rewardDefId, IEnumerable<string> recipientKeys)
        {
            if (recipientKeys == null) throw new ArgumentNullException(nameof(recipientKeys));
            int added = 0;
            foreach (string recipient in recipientKeys)
            {
                string id = GrantId.Compute(runId, triggerId, rewardDefId, recipient);
                if (_entries.ContainsKey(id)) continue;
                _entries.Add(id, new Entry
                {
                    Message = new GrantMessage(id, runId, rewardDefId, recipient),
                    State = HostGrantState.Committed,
                });
                _order.Add(id);
                added++;
            }
            return added;
        }

        /// <summary>この受取人宛てで、まだackが無い報酬。再接続時や定期再送でそのまま送り直す。</summary>
        public IReadOnlyList<GrantMessage> PendingFor(string recipientKey)
        {
            var list = new List<GrantMessage>();
            foreach (string id in _order)
            {
                Entry e = _entries[id];
                if (e.State == HostGrantState.Committed && e.Message.RecipientKey == recipientKey) list.Add(e.Message);
            }
            return list;
        }

        public IReadOnlyList<string> PendingRecipients()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>();
            foreach (string id in _order)
            {
                Entry e = _entries[id];
                if (e.State == HostGrantState.Committed && set.Add(e.Message.RecipientKey)) list.Add(e.Message.RecipientKey);
            }
            return list;
        }

        /// <summary>ackを受けて閉じる。未知のID・宛先の不一致は無視する。状態が変わったときtrue。</summary>
        public bool OnAck(AckMessage ack)
        {
            if (ack == null) throw new ArgumentNullException(nameof(ack));
            if (!_entries.TryGetValue(ack.GrantId, out Entry e)) return false;
            if (e.Message.RecipientKey != ack.RecipientKey) return false;
            if (e.State != HostGrantState.Committed) return false;
            if (ack.Result == AckResult.Refused)
            {
                e.State = HostGrantState.Refused;
                e.RefusalDetail = ack.Detail;
            }
            else
            {
                e.State = HostGrantState.Closed;
            }
            return true;
        }

        public bool IsKnown(string grantId) => grantId != null && _entries.ContainsKey(grantId);

        public GrantMessage MessageOf(string grantId)
        {
            return grantId != null && _entries.TryGetValue(grantId, out Entry e) ? e.Message : null;
        }

        public HostGrantState? StateOf(string grantId)
        {
            return grantId != null && _entries.TryGetValue(grantId, out Entry e) ? e.State : (HostGrantState?)null;
        }

        public int Count(HostGrantState state)
        {
            int n = 0;
            foreach (Entry e in _entries.Values)
                if (e.State == state) n++;
            return n;
        }

        public IReadOnlyList<string> AllGrantIds => _order;
    }

    public enum ReceiveStatus
    {
        Applied,
        AlreadyApplied,
        Refused,
        /// <summary>別のプロフィール宛て。何も変更せず、ackも返さない。</summary>
        WrongRecipient,
        /// <summary>保存に失敗した。ackを返さない（ホストの再送で再試行される）。</summary>
        SaveFailed,
    }

    public sealed class ReceiveResult
    {
        internal ReceiveResult(ReceiveStatus status, AckMessage ack, string detail)
        {
            Status = status;
            Ack = ack;
            Detail = detail ?? string.Empty;
        }

        public ReceiveStatus Status { get; }
        /// <summary>送り返すべきack。null のときは何も返さない。</summary>
        public AckMessage Ack { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// クライアント側の受取処理。付与の確定点は「台帳への保存完了」で、ackは保存後にのみ返す。
    /// アイテムまたは素材の変更と appliedGrants への追記は、同一の保存（Mutate）で行う。
    /// </summary>
    public sealed class ClientGrantReceiver
    {
        private readonly LedgerStore _store;
        private readonly Catalog _catalog;

        public ClientGrantReceiver(LedgerStore store, Catalog catalog)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ReceiveResult Receive(GrantMessage msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!string.Equals(msg.RecipientKey, _store.ProfileKey, StringComparison.Ordinal))
                return new ReceiveResult(ReceiveStatus.WrongRecipient, null, "宛先のプロフィールが一致しません。");

            if (_store.State.HasApplied(msg.GrantId))
                return new ReceiveResult(ReceiveStatus.AlreadyApplied, new AckMessage(msg.GrantId, msg.RecipientKey, AckResult.AlreadyApplied), null);

            if (!_catalog.TryGetReward(msg.RewardDefId, out RewardDef def))
                return Refuse(msg, "未定義の報酬です: " + msg.RewardDefId);

            if (def.Kind == RewardKind.Material)
            {
                if (!_catalog.TryGetMaterialCap(def.TargetId, out int cap))
                    return Refuse(msg, "未定義の素材です: " + def.TargetId);
                if ((long)_store.State.MaterialCount(def.TargetId) + def.Quantity > cap)
                    return Refuse(msg, "所持上限を超えます: " + def.TargetId);
            }

            try
            {
                _store.Mutate(s =>
                {
                    if (def.Kind == RewardKind.Material)
                    {
                        s.AddMaterial(def.TargetId, def.Quantity);
                    }
                    else
                    {
                        // 同じ報酬は同じインスタンスIDになる（再生成しても別物にならない）
                        string instanceId = LedgerSerializer.Sha256Hex("instance|" + msg.GrantId).Substring(0, 32);
                        s.AddItem(new ItemRecord(def.TargetId, instanceId, msg.GrantId));
                    }
                    s.RecordGrant(new AppliedGrant(msg.GrantId, msg.RewardDefId, def.Quantity));
                });
            }
            catch (IOException e)
            {
                return ResyncAfterFailure(e.Message);
            }

            return new ReceiveResult(ReceiveStatus.Applied, new AckMessage(msg.GrantId, msg.RecipientKey, AckResult.Applied), null);
        }

        private ReceiveResult Refuse(GrantMessage msg, string detail)
        {
            return new ReceiveResult(ReceiveStatus.Refused, new AckMessage(msg.GrantId, msg.RecipientKey, AckResult.Refused, detail), detail);
        }

        private ReceiveResult ResyncAfterFailure(string reason)
        {
            // 失敗時は一時ファイルに新しい内容が残り得るため、ディスクの状態を読み直す
            try
            {
                _store.Load();
            }
            catch (Exception e) when (e is LedgerCorruptException || e is IOException)
            {
                return new ReceiveResult(ReceiveStatus.SaveFailed, null, "保存に失敗し、再読込もできません: " + e.Message);
            }
            return new ReceiveResult(ReceiveStatus.SaveFailed, null, "保存に失敗しました: " + reason);
        }
    }
}

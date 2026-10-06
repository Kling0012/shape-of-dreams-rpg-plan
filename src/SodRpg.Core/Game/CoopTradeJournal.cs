using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    /// <summary>One durable host decision for both players. An unreadable journal is never replaced by an older backup.</summary>
    public sealed class CoopTradeJournal
    {
        private const string Format = "sodrpg.coop-trade-journal";
        private readonly IFileSystem _fs;
        private readonly string _path;
        private readonly SortedDictionary<string, string> _receipts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _cancelled = new SortedSet<string>(StringComparer.Ordinal);
        private readonly object _sync = new object();
        private bool _faulted;

        public CoopTradeJournal(IFileSystem fs, string path)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException(Loc.T("協力取引の識別情報がありません。", "Trade journal path is required."), nameof(path));
            _path = path;
            if (!fs.Exists(path)) return;
            var root = CoopTradeCodec.Object(Json.Parse(fs.ReadAllText(path)));
            if (CoopTradeCodec.String(root, "format") != Format || !(CoopTradeCodec.Value(root, "version") is long version) || version != 1)
                throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Unsupported cooperative trade journal."));
            foreach (object value in CoopTradeCodec.Array(root, "receipts"))
            {
                var receipt = CoopTradeCodec.ParseReceipt(CoopTradeCodec.Object(value));
                CoopTradeRules.ValidateReceipt(receipt, out _, out _);
                if (_receipts.ContainsKey(receipt.Id)) throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Duplicate cooperative trade journal decision."));
                _receipts.Add(receipt.Id, CoopTradeCodec.WriteReceipt(receipt));
            }
            foreach (object value in CoopTradeCodec.Array(root, "cancelled"))
            {
                if (!(value is string id) || string.IsNullOrEmpty(id) || !_cancelled.Add(id) || _receipts.ContainsKey(id))
                    throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid cooperative trade cancellation tombstone."));
            }
        }

        public bool TryGet(string id, out CoopTradeReceipt receipt)
        {
            lock (_sync)
            {
                EnsureHealthy();
                if (id != null && _receipts.TryGetValue(id, out string encoded))
                {
                    // Callers cannot mutate the decision retained by the authority.
                    receipt = CoopTradeCodec.ReadReceipt(encoded);
                    return true;
                }
                receipt = null;
                return false;
            }
        }

        public bool IsCancelled(string id)
        {
            lock (_sync)
            {
                EnsureHealthy();
                return id != null && _cancelled.Contains(id);
            }
        }

        public bool Commit(CoopTradeReceipt receipt)
        {
            lock (_sync)
            {
                EnsureHealthy();
                // Recheck BOTH sides before the only persistent write, even for caller-constructed receipts.
                CoopTradeRules.ValidateReceipt(receipt, out _, out _);
                string encoded = CoopTradeCodec.WriteReceipt(receipt);
                if (_receipts.TryGetValue(receipt.Id, out string existing))
                {
                    if (existing != encoded) throw new InvalidOperationException(Loc.T("同じ取引IDに別の確定結果が保存されています。", "Trade ID has a different committed decision."));
                    return false;
                }
                if (_cancelled.Contains(receipt.Id)) throw new InvalidOperationException(Loc.T("この取引はすでに取り消されています。", "Trade was durably cancelled."));
                Persist(encoded, null);
                _receipts.Add(receipt.Id, encoded);
                return true;
            }
        }

        public bool Cancel(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException(Loc.T("協力取引の識別情報がありません。", "Trade ID is required."), nameof(id));
            lock (_sync)
            {
                EnsureHealthy();
                // A committed receipt is always replayed; a cancellation cannot revoke exchanged assets.
                if (_receipts.ContainsKey(id) || _cancelled.Contains(id)) return false;
                Persist(null, id);
                _cancelled.Add(id);
                return true;
            }
        }

        private void Persist(string encoded, string cancelledId)
        {
            var receipts = new List<object>(_receipts.Count + (encoded == null ? 0 : 1));
            foreach (string text in _receipts.Values) receipts.Add(Json.Parse(text));
            if (encoded != null) receipts.Add(Json.Parse(encoded));
            var cancelled = _cancelled.Select(id => (object)id).ToList();
            if (cancelledId != null) cancelled.Add(cancelledId);
            string contents = Json.Write(new JsonObject().Add("format", Format).Add("version", 1L)
                .Add("receipts", receipts).Add("cancelled", cancelled));
            try
            {
                _fs.WriteAllText(_path + ".tmp", contents);
                _fs.Replace(_path + ".tmp", _path, null);
            }
            catch
            {
                // Replacement failures may be ambiguous. Never use stale in-memory decisions afterwards.
                _faulted = true;
                throw;
            }
        }

        private void EnsureHealthy()
        {
            if (_faulted) throw new InvalidOperationException(Loc.T("協力取引の台帳を保存できませんでした。台帳を読み直すまで取引できません。", "Cooperative trade journal persistence failed; reload the journal before trading."));
        }
    }
}

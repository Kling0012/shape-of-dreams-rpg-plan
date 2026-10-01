using System;
using System.Collections.Generic;

namespace SodRpg.Core
{
    public sealed class ItemRecord
    {
        public ItemRecord(string itemId, string instanceId, string acquiredBy)
        {
            ItemId = itemId;
            InstanceId = instanceId;
            AcquiredBy = acquiredBy ?? string.Empty;
        }

        public string ItemId { get; }
        public string InstanceId { get; }
        /// <summary>入手元の報酬ID（grantId）。報酬以外の入手では空または任意の識別子。</summary>
        public string AcquiredBy { get; }
    }

    /// <summary>付与済みの報酬。重複排除の根拠であり、アイテム・素材の変更と同一の保存で書く。</summary>
    public sealed class AppliedGrant
    {
        public AppliedGrant(string grantId, string defId, int quantity)
        {
            GrantId = grantId;
            DefId = defId;
            Quantity = quantity;
        }

        public string GrantId { get; }
        public string DefId { get; }
        /// <summary>実際に台帳へ加えた数量。</summary>
        public int Quantity { get; }
    }

    /// <summary>読み込み時に取り除いた（隔離した）エントリ。内容は捨てずに残し、利用者へ表示する。</summary>
    public sealed class QuarantineEntry
    {
        public QuarantineEntry(string kind, string reason, string raw)
        {
            Kind = kind ?? string.Empty;
            Reason = reason ?? string.Empty;
            Raw = raw ?? string.Empty;
        }

        public string Kind { get; }
        public string Reason { get; }
        public string Raw { get; }
    }

    /// <summary>
    /// プロフィールごとの保存内容。「所持」だけを持ち、「今回装着中」は保存しない。
    /// 変更は LedgerStore.Mutate 経由で行う（複製に対して変更し、保存成功後に差し替える）。
    /// </summary>
    public sealed class LedgerState
    {
        public const int CurrentSchemaVersion = 1;

        private readonly List<ItemRecord> _items = new List<ItemRecord>();
        private readonly SortedDictionary<string, int> _materials = new SortedDictionary<string, int>(StringComparer.Ordinal);
        private readonly List<AppliedGrant> _grants = new List<AppliedGrant>();
        private readonly HashSet<string> _grantIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<QuarantineEntry> _quarantine = new List<QuarantineEntry>();

        public LedgerState(string profileKey, long revision = 0)
        {
            if (string.IsNullOrEmpty(profileKey)) throw new ArgumentException("プロフィールキーが空です。", nameof(profileKey));
            ProfileKey = profileKey;
            Revision = revision;
        }

        public string ProfileKey { get; }

        /// <summary>保存のたびに+1。巻き戻り（古いファイルの復元）の検出と、複数候補からの選択に使う。</summary>
        public long Revision { get; internal set; }

        public IReadOnlyList<ItemRecord> Items => _items;
        public IReadOnlyDictionary<string, int> Materials => _materials;
        public IReadOnlyList<AppliedGrant> AppliedGrants => _grants;
        public IReadOnlyList<QuarantineEntry> Quarantine => _quarantine;

        public bool HasApplied(string grantId) => grantId != null && _grantIds.Contains(grantId);

        public int MaterialCount(string materialId)
        {
            return _materials.TryGetValue(materialId, out int n) ? n : 0;
        }

        public bool OwnsItem(string itemId)
        {
            foreach (var i in _items)
                if (i.ItemId == itemId) return true;
            return false;
        }

        public void AddItem(ItemRecord item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items.Add(item);
        }

        public void AddMaterial(string materialId, int quantity)
        {
            if (string.IsNullOrEmpty(materialId)) throw new ArgumentException("素材IDが空です。", nameof(materialId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            long next = (long)MaterialCount(materialId) + quantity;
            if (next > int.MaxValue) throw new OverflowException("素材の所持数が範囲を超えます。");
            _materials[materialId] = (int)next;
        }

        /// <summary>復元・読み込み用。上限や存在確認はしない（検証は Catalog 側）。</summary>
        internal void SetMaterial(string materialId, int quantity)
        {
            _materials[materialId] = quantity;
        }

        public void RecordGrant(AppliedGrant grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            if (!_grantIds.Add(grant.GrantId)) throw new InvalidOperationException("同じ報酬IDを二重に記録できません: " + grant.GrantId);
            _grants.Add(grant);
        }

        public void AddQuarantine(QuarantineEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _quarantine.Add(entry);
        }

        public LedgerState Clone()
        {
            var c = new LedgerState(ProfileKey, Revision);
            c._items.AddRange(_items);
            foreach (var kv in _materials) c._materials[kv.Key] = kv.Value;
            foreach (var g in _grants) { c._grants.Add(g); c._grantIds.Add(g.GrantId); }
            c._quarantine.AddRange(_quarantine);
            return c;
        }
    }
}

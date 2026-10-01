using System;
using System.Collections.Generic;

namespace SodRpg.Core
{
    public enum ModifierMode
    {
        Add,
        Multiply,
    }

    /// <summary>能力補正の定義。IDは「MOD接頭辞:アイテム:種別」の形で、他MODの補正と衝突させない。</summary>
    public sealed class ModifierSpec
    {
        public ModifierSpec(string id, string statKey, ModifierMode mode, double value)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("補正IDが空です。", nameof(id));
            if (string.IsNullOrEmpty(statKey)) throw new ArgumentException("能力値キーが空です。", nameof(statKey));
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("補正値が不正です。", nameof(value));
            Id = id;
            StatKey = statKey;
            Mode = mode;
            Value = value;
        }

        public string Id { get; }
        public string StatKey { get; }
        public ModifierMode Mode { get; }
        public double Value { get; }

        public bool SameAs(ModifierSpec other)
        {
            return other != null && Id == other.Id && StatKey == other.StatKey && Mode == other.Mode && Value.Equals(other.Value);
        }
    }

    public sealed class ItemDef
    {
        public ItemDef(string id, IEnumerable<ModifierSpec> modifiers = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("アイテムIDが空です。", nameof(id));
            Id = id;
            Modifiers = new List<ModifierSpec>(modifiers ?? new ModifierSpec[0]).AsReadOnly();
        }

        public string Id { get; }
        public IReadOnlyList<ModifierSpec> Modifiers { get; }
    }

    public enum RewardKind
    {
        Material,
        Item,
    }

    /// <summary>報酬の定義。メッセージにはこのIDだけを載せ、内容は各自の定義から引く。</summary>
    public sealed class RewardDef
    {
        public RewardDef(string id, RewardKind kind, string targetId, int quantity)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("報酬IDが空です。", nameof(id));
            if (string.IsNullOrEmpty(targetId)) throw new ArgumentException("対象IDが空です。", nameof(targetId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (kind == RewardKind.Item && quantity != 1) throw new ArgumentException("アイテム報酬の数量は1です。", nameof(quantity));
            Id = id;
            Kind = kind;
            TargetId = targetId;
            Quantity = quantity;
        }

        public string Id { get; }
        public RewardKind Kind { get; }
        public string TargetId { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// 台帳が扱ってよいIDと上限の一覧。台帳の検証（存在しない参照・上限外の拒否）の根拠になる。
    /// </summary>
    public sealed class Catalog
    {
        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _materialCaps = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, RewardDef> _rewards = new Dictionary<string, RewardDef>(StringComparer.Ordinal);

        public Catalog AddItem(ItemDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (_items.ContainsKey(def.Id)) throw new ArgumentException("アイテムIDが重複しています: " + def.Id);
            _items.Add(def.Id, def);
            return this;
        }

        public Catalog AddMaterial(string id, int maxCount)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("素材IDが空です。", nameof(id));
            if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (_materialCaps.ContainsKey(id)) throw new ArgumentException("素材IDが重複しています: " + id);
            _materialCaps.Add(id, maxCount);
            return this;
        }

        public Catalog AddReward(RewardDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (_rewards.ContainsKey(def.Id)) throw new ArgumentException("報酬IDが重複しています: " + def.Id);
            bool targetExists = def.Kind == RewardKind.Material ? _materialCaps.ContainsKey(def.TargetId) : _items.ContainsKey(def.TargetId);
            if (!targetExists) throw new ArgumentException("報酬の対象が未定義です: " + def.TargetId);
            _rewards.Add(def.Id, def);
            return this;
        }

        public bool HasItem(string id) => id != null && _items.ContainsKey(id);

        public bool TryGetItem(string id, out ItemDef def)
        {
            def = null;
            return id != null && _items.TryGetValue(id, out def);
        }

        public bool TryGetMaterialCap(string id, out int cap)
        {
            cap = 0;
            return id != null && _materialCaps.TryGetValue(id, out cap);
        }

        public bool TryGetReward(string id, out RewardDef def)
        {
            def = null;
            return id != null && _rewards.TryGetValue(id, out def);
        }
    }

    /// <summary>試作で使う最小の定義（保存アイテム1つ、素材1つ、協力報酬1つ、能力補正1つ）。</summary>
    public static class PrototypeCatalog
    {
        public const string CharmItemId = "test_charm";
        public const string ShardMaterialId = "dream_shard";
        public const string ShardRewardId = "reward.dream_shard.1";
        public const string CharmModifierId = "sodrpg:test_charm:stat";
        public const string TestStatKey = "TestStat";
        public const int ShardCap = 9999;

        public static Catalog Create()
        {
            return new Catalog()
                .AddItem(new ItemDef(CharmItemId, new[] { new ModifierSpec(CharmModifierId, TestStatKey, ModifierMode.Add, 10) }))
                .AddMaterial(ShardMaterialId, ShardCap)
                .AddReward(new RewardDef(ShardRewardId, RewardKind.Material, ShardMaterialId, 1));
        }
    }
}

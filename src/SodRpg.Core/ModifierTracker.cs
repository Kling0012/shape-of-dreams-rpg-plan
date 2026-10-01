using System;
using System.Collections.Generic;

namespace SodRpg.Core
{
    /// <summary>
    /// ゲーム側の能力値へ補正を出し入れする窓口。実機ではEntityStatus等へ接続する接続層が実装する。
    /// 純C#コアはこの2操作だけを前提にする。
    /// </summary>
    public interface IStatTarget
    {
        void ApplyModifier(ModifierSpec spec);
        void RemoveModifier(string modifierId);
    }

    /// <summary>
    /// 自分が付けた補正だけを追跡し、付与・除去を冪等にする。他MODの補正には触れない。
    /// </summary>
    public sealed class ModifierTracker
    {
        private readonly IStatTarget _target;
        private readonly Dictionary<string, ModifierSpec> _applied = new Dictionary<string, ModifierSpec>(StringComparer.Ordinal);

        public ModifierTracker(IStatTarget target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public IReadOnlyCollection<string> AppliedIds => _applied.Keys;

        public bool IsApplied(string modifierId) => _applied.ContainsKey(modifierId);

        /// <summary>同じ内容なら何もしない。同じIDで内容が違えば、いったん外してから付け直す。</summary>
        public void Apply(ModifierSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (_applied.TryGetValue(spec.Id, out ModifierSpec existing))
            {
                if (existing.SameAs(spec)) return;
                _target.RemoveModifier(spec.Id);
                _applied.Remove(spec.Id);
            }
            _target.ApplyModifier(spec);
            _applied[spec.Id] = spec;
        }

        /// <summary>付いていなければ何もしない。</summary>
        public void Remove(string modifierId)
        {
            if (!_applied.ContainsKey(modifierId)) return;
            _target.RemoveModifier(modifierId);
            _applied.Remove(modifierId);
        }

        /// <summary>解除・切断・ロード・MOD停止（OnDestroy）などで、自分の補正をすべて外す。</summary>
        public void RemoveAll()
        {
            foreach (string id in new List<string>(_applied.Keys))
                Remove(id);
        }

        /// <summary>望ましい補正の集合に合わせる。多いものは外し、足りないものは付ける。何度呼んでも結果は同じ。</summary>
        public void Reconcile(IEnumerable<ModifierSpec> desired)
        {
            var want = new Dictionary<string, ModifierSpec>(StringComparer.Ordinal);
            foreach (ModifierSpec spec in desired)
                want[spec.Id] = spec;

            foreach (string id in new List<string>(_applied.Keys))
                if (!want.ContainsKey(id)) Remove(id);

            foreach (ModifierSpec spec in want.Values)
                Apply(spec);
        }
    }

    public static class CharmRules
    {
        /// <summary>
        /// 台帳の所持と、今回装着している選択（保存しない）から、付けるべき補正を決める。
        /// 所持していなければ、装着の選択があっても補正は付かない。
        /// </summary>
        public static List<ModifierSpec> DesiredModifiers(LedgerState ledger, IEnumerable<string> equippedItemIds, Catalog catalog)
        {
            var result = new List<ModifierSpec>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string itemId in equippedItemIds)
            {
                if (!seen.Add(itemId)) continue;
                if (!ledger.OwnsItem(itemId)) continue;
                if (!catalog.TryGetItem(itemId, out ItemDef def)) continue;
                result.AddRange(def.Modifiers);
            }
            return result;
        }
    }
}

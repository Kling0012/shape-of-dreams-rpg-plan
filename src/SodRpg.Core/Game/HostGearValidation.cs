using System;

namespace SodRpg.Core.Game
{
    /// <summary>Validates client gear against its production source before computing a host build.</summary>
    public static class HostGearValidation
    {
        public static bool TryValidate(Relic source, out Relic validated)
        {
            validated = null;
            if (source == null || !Gimmicks.ValidStarId(source.Uid)
                || !Content.TryGetBase(source.BaseId, out var basis)
                || (int)basis.Slot < 0 || (int)basis.Slot >= Content.SlotCount
                || source.Rarity < Rarity.Common || source.Rarity > Rarity.Legendary
                || source.ItemLevel < 1 || source.ItemLevel > Content.MaxItemLevel
                || source.LimitBreaks < 0 || source.LimitBreaks > Content.MaxLimitBreaks(source.Rarity)
                || source.Enhance < 0 || source.Enhance > Content.MaxEnhanceFor(source)
                || source.Retunes < 0 || source.Retunes > Content.MaxRetunes
                || source.EnhanceMilestones < 0 || source.EnhanceMilestones > Content.MaxEnhanceMilestones
                || source.AwakenLevel < 0 || source.AwakenLevel > Content.MaxAwakenLevel
                || source.AwakenPoints < 0 || source.AwakenPoints > Content.AwakenThreshold)
                return false;

            UniqueDef unique = null;
            if (source.UniqueId != null)
            {
                if (source.Rarity != Rarity.Legendary || !Content.TryGetUnique(source.UniqueId, out unique)
                    || !string.Equals(unique.BaseId, source.BaseId, StringComparison.Ordinal))
                    return false;
            }
            else if (source.Rarity == Rarity.Legendary) return false;
            // 銘品（v1.32）も固有効果は定義どおり。通常の遺物と同じ土台・レア度でなければならない。
            NamedDef named = null;
            if (source.NamedId != null)
            {
                if (unique != null || !NamedItems.TryGetNamed(source.NamedId, out named)
                    || !string.Equals(named.BaseId, source.BaseId, StringComparison.Ordinal)
                    || named.Rarity != source.Rarity)
                    return false;
            }
            if (source.Rarity != Rarity.Legendary && (source.AwakenLevel != 0 || source.AwakenPoints != 0))
                return false;

            // LostMausoleum resets Enhance, not milestones, limit breaks or the +20 raw power bonus.
            // Historical milestones must be reachable with this source's limit breaks, but need not
            // match its current Enhance. Bounded awakening levels allow legacy imported awakening
            // metadata; their multipliers never exceed a legally reachable legendary awakening.
            int historicalCap = Content.MaxEnhanceFor(source);
            int maxMilestones = historicalCap >= Content.EnhanceMilestoneFifth ? 5
                : historicalCap >= Content.EnhanceMilestoneFourth ? 4
                : historicalCap >= Content.EnhanceMilestoneThird ? 3 : 2;
            if (source.EnhanceMilestones > maxMilestones) return false;
            if (source.MilestonePowerApplied && source.EnhanceMilestones < 5) return false;

            // Loot starts Rare/Epic with 1/2 powers, ordinary uniques and named items with their
            // authored powers, and Common/Uncommon/set pieces with none. +5 adds a power only to
            // the latter sources; otherwise it adds an affix. +3/+10/+15 each add an affix; +20 adds none.
            int initialPowers = unique != null ? unique.Powers.Count
                : named != null ? named.Powers.Count
                : source.Rarity == Rarity.Epic ? 2 : source.Rarity == Rarity.Rare ? 1 : 0;
            int maxPowers = initialPowers > 0 ? initialPowers : source.EnhanceMilestones >= 2 ? 1 : 0;
            int maxAffixes = Content.AffixCount(source.Rarity);
            if (source.EnhanceMilestones >= 1) maxAffixes++;
            if (source.EnhanceMilestones >= 2 && initialPowers > 0) maxAffixes++;
            if (source.EnhanceMilestones >= 3) maxAffixes++;
            if (source.EnhanceMilestones >= 4) maxAffixes++;
            if (source.Affixes.Count > maxAffixes || source.Powers.Count > maxPowers
                || ((unique != null || named != null) && initialPowers > 0 && source.Powers.Count != initialPowers))
                return false;

            var result = new Relic
            {
                Uid = source.Uid,
                BaseId = source.BaseId,
                UniqueId = source.UniqueId,
                NamedId = source.NamedId,
                Rarity = source.Rarity,
                ItemLevel = source.ItemLevel,
                Enhance = source.Enhance,
                LimitBreaks = source.LimitBreaks,
                Retunes = source.Retunes,
                Locked = source.Locked,
                EnhanceMilestones = source.EnhanceMilestones,
                MilestonePowerApplied = source.MilestonePowerApplied || source.EnhanceMilestones >= 5,
                AwakenPoints = source.AwakenPoints,
                AwakenLevel = source.AwakenLevel,
            };
            var affixPool = Content.AffixPool(basis.Slot);
            for (int i = 0; i < source.Affixes.Count; i++)
            {
                var line = source.Affixes[i];
                if (line == null || line.Stat == basis.ImplicitStat) return false;
                for (int j = 0; j < i; j++)
                    if (source.Affixes[j].Stat == line.Stat) return false;
                AffixDef definition = null;
                for (int j = 0; j < affixPool.Count; j++)
                    if (affixPool[j].Stat == line.Stat && source.Rarity >= affixPool[j].MinRarity)
                    { definition = affixPool[j]; break; }
                if (definition == null) return false;

                // Loot.RollAffix is also the source for milestone and retune/craft affixes. Raw
                // values include rarity/item-level rounding, NEVER enhancement/awakening again.
                int levelPct = Content.ScalesWithItemLevel(line.Stat) ? Content.LevelScalePct(source.ItemLevel) : 100;
                int cap = Relic.Scale(definition.Max, Content.RarityValuePct(source.Rarity) * levelPct / 100);
                result.Affixes.Add(new StatLine(line.Stat, ClampAmount(line.Value, cap)));
            }

            var powerPool = Content.PowerPool(basis.Slot);
            for (int i = 0; i < source.Powers.Count; i++)
            {
                var line = source.Powers[i];
                if (line == null || !Content.PowerAllowedForRarity(line.Power, source.Rarity)) return false;
                for (int j = 0; j < i; j++)
                    if (source.Powers[j].Power == line.Power) return false;
                int cap;
                if (unique != null && initialPowers > 0)
                {
                    // Unique powers and order are authored, not interchangeable slot-pool rolls.
                    if (line.Power != unique.Powers[i].Power) return false;
                    cap = unique.Powers[i].Value;
                }
                else if (named != null)
                {
                    // 銘品の固有効果も定義どおり（設計 3.2。土台の枠の池とは限らない）。
                    if (line.Power != named.Powers[i].Power) return false;
                    cap = named.Powers[i].Value;
                }
                else
                {
                    PowerRange definition = null;
                    for (int j = 0; j < powerPool.Count; j++)
                        if (powerPool[j].Power == line.Power) { definition = powerPool[j]; break; }
                    if (definition == null) return false;
                    // MemoryWell/PowerCrucible can raise the first power to the FULL slot range,
                    // including Rare and former +5 minimum-only powers. Epic's second power is
                    // never replaced: AddMinorPower limits it to the lower half of its range.
                    cap = i == 1 && unique == null
                        ? Math.Max(definition.Min, definition.Min + (definition.Max - definition.Min) / 2)
                        : unique != null ? definition.Min : definition.Max;
                }
                if (i == 0 && source.EnhanceMilestones >= 5)
                {
                    // The one-time source bonus is stored before enhancement/awakening.
                    // Aggregate caps belong to Build.Compute, not to individual source values.
                    cap = Math.Max(cap, Relic.Scale(cap, Content.LimitBreakPowerPct));
                }
                result.Powers.Add(new PowerLine(line.Power, ClampAmount(line.Value, cap)));
            }
            validated = result;
            return true;
        }

        // ProfileCodec may persist values reduced by aggregate caps. Keep these weakened rolls;
        // only nonnegative source-level maxima are security-relevant, not attainable roll minima.
        private static int ClampAmount(int value, int cap) => Math.Max(0, Math.Min(value, cap));
    }
}

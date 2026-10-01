using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>1回の撃破で得たもの。</summary>
    public sealed class KillReward
    {
        public List<Relic> Relics { get; } = new List<Relic>();
        public int Shards { get; set; }
        public int Tuning { get; set; }
        public int Xp { get; set; }

        public bool IsEmpty => Relics.Count == 0 && Shards == 0 && Tuning == 0;
    }

    /// <summary>
    /// 抽選の規則。数値は計画書 付録B5（ドロップ）と B4（エピックの救済）を、
    /// 本作の「1遠征で数百体を倒す」テンポに合わせて1体あたりの確率へ直したもの。
    /// </summary>
    public static class Loot
    {
        private static readonly int[] BaseRarityWeights = { 600, 270, 100, 26, 4 };

        /// <summary>夢の深度1あたりの装備ドロップ率の増分。</summary>
        public const double HeatDropBonus = 0.35;
        /// <summary>夢の深度1あたりの幸運（レア度の上振れ）。</summary>
        public const double HeatLuck = 0.3;

        public static double DropChance(MonsterTier tier, int heat)
        {
            double baseChance;
            switch (tier)
            {
                case MonsterTier.Lesser: baseChance = 0.006; break;
                case MonsterTier.Normal: baseChance = 0.02; break;
                case MonsterTier.MiniBoss: baseChance = 0.35; break;
                default: baseChance = 1.0; break;
            }
            return Math.Min(1.0, baseChance * (1.0 + HeatDropBonus * ClampHeat(heat)));
        }

        public static double TierLuck(MonsterTier tier)
        {
            switch (tier)
            {
                case MonsterTier.MiniBoss: return 1.0;
                case MonsterTier.Boss: return 2.0;
                default: return 0.0;
            }
        }

        /// <summary>救済の確率 p = min(1, 0.08 + 0.048k)。k は未取得が続いたボス撃破数（付録B4）。</summary>
        public static double EpicPityChance(int k) => Math.Min(1.0, 0.08 + 0.048 * Math.Max(0, k));

        public static Rarity RollRarity(Rng rng, double luck, bool allowLegendary, Rarity floor = Rarity.Common)
        {
            double f = 1.0 + 0.6 * Math.Max(0, luck);
            var w = new double[BaseRarityWeights.Length];
            double total = 0;
            for (int i = 0; i < w.Length; i++)
            {
                if (i < (int)floor || (!allowLegendary && i == (int)Rarity.Legendary)) continue;
                w[i] = BaseRarityWeights[i] * Math.Pow(f, i);
                total += w[i];
            }
            double x = rng.NextDouble() * total;
            for (int i = 0; i < w.Length; i++)
            {
                if (w[i] <= 0) continue;
                if (x < w[i]) return (Rarity)i;
                x -= w[i];
            }
            for (int i = w.Length - 1; i >= 0; i--)
                if (w[i] > 0) return (Rarity)i;
            return floor;
        }

        public static Relic RollRelic(Rng rng, Rarity rarity, int itemLevel, Slot? slot = null)
        {
            if (rarity == Rarity.Legendary)
            {
                var candidates = new List<UniqueDef>();
                foreach (var u in Content.Uniques)
                    if (slot == null || Content.GetBase(u.BaseId).Slot == slot.Value) candidates.Add(u);
                if (candidates.Count > 0) return RollUnique(rng, candidates[rng.Range(0, candidates.Count - 1)], itemLevel);
                rarity = Rarity.Epic;
            }

            var bases = new List<BaseDef>();
            foreach (var b in Content.Bases)
                if (slot == null || b.Slot == slot.Value) bases.Add(b);
            var baseDef = bases[rng.Range(0, bases.Count - 1)];

            var r = new Relic
            {
                Uid = rng.NextUid(),
                BaseId = baseDef.Id,
                Rarity = rarity,
                ItemLevel = ClampLevel(itemLevel),
            };
            RollAffixes(rng, r, Content.AffixCount(rarity));
            if (rarity == Rarity.Epic)
            {
                var pool = Content.PowerPool(baseDef.Slot);
                var pr = pool[rng.Range(0, pool.Count - 1)];
                r.Powers.Add(new PowerLine(pr.Power, rng.Range(pr.Min, pr.Max)));
            }
            return r;
        }

        public static Relic RollUnique(Rng rng, UniqueDef u, int itemLevel)
        {
            var r = new Relic
            {
                Uid = rng.NextUid(),
                BaseId = u.BaseId,
                UniqueId = u.Id,
                Rarity = Rarity.Legendary,
                ItemLevel = ClampLevel(itemLevel),
            };
            RollAffixes(rng, r, Content.AffixCount(Rarity.Legendary));
            r.Powers.AddRange(u.Powers);
            return r;
        }

        /// <summary>特性を count 個になるまで足す。同じ能力値と、基礎能力と同じ能力値は避ける。</summary>
        internal static void RollAffixes(Rng rng, Relic r, int count)
        {
            var used = new HashSet<Stat> { r.Base.ImplicitStat };
            foreach (var a in r.Affixes) used.Add(a.Stat);
            while (r.Affixes.Count < count)
            {
                var line = RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, used);
                if (line == null) break;
                used.Add(line.Stat);
                r.Affixes.Add(line);
            }
        }

        public static StatLine RollAffix(Rng rng, Slot slot, Rarity rarity, int itemLevel, ICollection<Stat> exclude)
        {
            var pool = Content.AffixPool(slot);
            int total = 0;
            foreach (var a in pool)
                if (!exclude.Contains(a.Stat)) total += a.Weight;
            if (total <= 0) return null;
            int x = rng.Range(0, total - 1);
            foreach (var a in pool)
            {
                if (exclude.Contains(a.Stat)) continue;
                if (x < a.Weight)
                {
                    int raw = rng.Range(a.Min, a.Max);
                    int pct = Content.RarityValuePct(rarity) * Content.LevelScalePct(itemLevel) / 100;
                    return new StatLine(a.Stat, Relic.Scale(raw, pct));
                }
                x -= a.Weight;
            }
            return null;
        }

        /// <summary>
        /// 1体の撃破に対する個人の報酬を抽選する。協力時は各プレイヤーが自分の分を独立に抽選する
        /// （計画書 第14章「確保と損失は個人ごと」）。epicPity はボス撃破でのみ進む。
        /// </summary>
        public static KillReward RollKill(Rng rng, MonsterTier tier, int itemLevel, int heat, ref int epicPity)
        {
            heat = ClampHeat(heat);
            var reward = new KillReward { Xp = Content.KillXp(tier) };
            double luck = TierLuck(tier) + HeatLuck * heat;
            bool allowLegendary = tier >= MonsterTier.MiniBoss;

            if (rng.Chance(DropChance(tier, heat)))
            {
                Rarity floor = tier == MonsterTier.Boss ? Rarity.Uncommon : Rarity.Common;
                bool pity = false;
                if (tier == MonsterTier.Boss && rng.Chance(EpicPityChance(epicPity)))
                {
                    floor = Rarity.Epic;
                    pity = true;
                }
                var rarity = RollRarity(rng, luck, allowLegendary, floor);
                reward.Relics.Add(RollRelic(rng, rarity, itemLevel));
                if (tier == MonsterTier.Boss)
                {
                    if (pity || rarity >= Rarity.Epic) epicPity = 0;
                    else epicPity++;
                    if (rng.Chance(0.6)) reward.Relics.Add(RollRelic(rng, RollRarity(rng, luck, true, Rarity.Uncommon), itemLevel));
                }
            }

            switch (tier)
            {
                case MonsterTier.Lesser:
                    if (rng.Chance(0.05)) reward.Shards = 1;
                    break;
                case MonsterTier.Normal:
                    if (rng.Chance(0.12)) reward.Shards = rng.Range(1, 2);
                    break;
                case MonsterTier.MiniBoss:
                    reward.Shards = rng.Range(5, 10);
                    if (rng.Chance(0.2)) reward.Tuning = 1;
                    break;
                default:
                    reward.Shards = rng.Range(20, 30);
                    reward.Tuning = 1;
                    break;
            }
            return reward;
        }

        public static int ClampHeat(int heat) => Math.Max(0, Math.Min(Content.MaxHeat, heat));

        public static int ClampLevel(int itemLevel) => Math.Max(1, Math.Min(Content.MaxItemLevel, itemLevel));
    }
}

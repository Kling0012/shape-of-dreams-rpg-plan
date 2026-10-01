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
        private static readonly int[] BaseRarityWeights = { 600, 270, 100, 26, 3 };

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
                case MonsterTier.MiniBoss: return 0.6;
                case MonsterTier.Boss: return 1.2;
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

        /// <summary>
        /// 遺物を抽選する。ownedRelics は保管庫（装着品を含む）、unsecuredRelics は未確保の鞄。
        /// 所持リストを省略すると、セット部位の収集補助は行わない。
        /// </summary>
        public static Relic RollRelic(Rng rng, Rarity rarity, int itemLevel, Slot? slot = null, Line? focus = null,
            IReadOnlyList<Relic> ownedRelics = null, IReadOnlyList<Relic> unsecuredRelics = null)
        {
            if (rarity == Rarity.Legendary)
            {
                var candidates = new List<(UniqueDef Unique, int Weight)>();
                int total = 0;
                foreach (var u in Content.Uniques)
                {
                    if (slot != null && Content.GetBase(u.BaseId).Slot != slot.Value) continue;
                    int weight = focus != null && Content.GetBase(u.BaseId).Line == focus.Value ? FocusWeight : 1;
                    if (u.SetId != null) weight *= SetPieceWeight;
                    if (IsMissingSetPiece(u, ownedRelics, unsecuredRelics)) weight *= SetCompletionWeight;
                    candidates.Add((u, weight));
                    total += weight;
                }
                if (candidates.Count > 0)
                {
                    int x = rng.Range(0, total - 1);
                    var chosen = candidates[candidates.Count - 1].Unique;
                    foreach (var candidate in candidates)
                    {
                        if (x < candidate.Weight) { chosen = candidate.Unique; break; }
                        x -= candidate.Weight;
                    }
                    return RollUnique(rng, chosen, itemLevel);
                }
                rarity = Rarity.Epic;
            }

            var bases = new List<BaseDef>();
            foreach (var b in Content.Bases)
                if (slot == null || b.Slot == slot.Value) bases.Add(b);
            var baseDef = PickWeighted(rng, bases, b => b.Line, focus);

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

        /// <summary>狙い系統の重み。狙った系統は2倍出やすい（計画書 付録B）。</summary>
        public const int FocusWeight = 2;

        /// <summary>所持しているセットの未所持部位の重み。狙い系統の重みと掛け合わせる。</summary>
        /// <summary>伝説の抽選でセット品そのものが選ばれやすくなる倍率。</summary>
        public const int SetPieceWeight = 2;

        public const int SetCompletionWeight = 6;

        private static bool IsMissingSetPiece(UniqueDef candidate, IReadOnlyList<Relic> ownedRelics, IReadOnlyList<Relic> unsecuredRelics)
        {
            if (candidate.SetId == null) return false;
            bool started = false;
            for (int source = 0; source < 2; source++)
            {
                var relics = source == 0 ? ownedRelics : unsecuredRelics;
                if (relics == null) continue;
                for (int i = 0; i < relics.Count; i++)
                {
                    string id = relics[i].UniqueId;
                    if (id == candidate.Id) return false;
                    if (!started && Content.TryGetUnique(id, out var unique) && unique.SetId == candidate.SetId)
                        started = true;
                }
            }
            return started;
        }

        private static T PickWeighted<T>(Rng rng, List<T> items, Func<T, Line> lineOf, Line? focus)
        {
            if (focus == null) return items[rng.Range(0, items.Count - 1)];
            int total = 0;
            foreach (var i in items) total += lineOf(i) == focus.Value ? FocusWeight : 1;
            int x = rng.Range(0, total - 1);
            foreach (var i in items)
            {
                int w = lineOf(i) == focus.Value ? FocusWeight : 1;
                if (x < w) return i;
                x -= w;
            }
            return items[items.Count - 1];
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
        public static KillReward RollKill(Rng rng, MonsterTier tier, int itemLevel, int heat, ref int epicPity, Line? focus = null, Pacts.Totals mods = null,
            IReadOnlyList<Relic> ownedRelics = null, IReadOnlyList<Relic> unsecuredRelics = null)
        {
            heat = ClampHeat(heat);
            var reward = new KillReward { Xp = Content.KillXp(tier) };
            double luck = TierLuck(tier) + HeatLuck * heat + (mods?.Luck ?? 0);
            bool allowLegendary = tier >= MonsterTier.MiniBoss;
            double chance = Math.Min(1.0, DropChance(tier, heat) * (1.0 + (mods?.DropBonus ?? 0)));

            if (rng.Chance(chance))
            {
                Rarity floor = tier == MonsterTier.Boss ? Rarity.Uncommon : Rarity.Common;
                bool pity = false;
                if (tier == MonsterTier.Boss && rng.Chance(EpicPityChance(epicPity)))
                {
                    floor = Rarity.Epic;
                    pity = true;
                }
                var rarity = RollRarity(rng, luck, allowLegendary, floor);
                reward.Relics.Add(RollRelic(rng, rarity, itemLevel, null, focus, ownedRelics, unsecuredRelics));
                if (tier == MonsterTier.Boss)
                {
                    if (pity || rarity >= Rarity.Epic) epicPity = 0;
                    else epicPity++;
                    if (rng.Chance(0.6)) reward.Relics.Add(RollRelic(rng, RollRarity(rng, luck, true, Rarity.Uncommon), itemLevel, null, focus, ownedRelics, unsecuredRelics));
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
            if (mods != null)
            {
                reward.Shards = (int)Math.Round(reward.Shards * mods.ShardMult);
                if (tier >= MonsterTier.MiniBoss) reward.Tuning += mods.TuningOnElite;
                reward.Xp = (int)Math.Round(reward.Xp * mods.XpMult);
            }
            return reward;
        }

        public static int ClampHeat(int heat) => Math.Max(0, Math.Min(Content.MaxHeat, heat));

        public static int ClampLevel(int itemLevel) => Math.Max(1, Math.Min(Content.MaxItemLevel, itemLevel));
    }
}

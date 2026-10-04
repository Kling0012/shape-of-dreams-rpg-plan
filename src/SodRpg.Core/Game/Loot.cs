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
        /// 抽選の規則。数値は計画書 付録B5（ドロップ）を、
        /// 本作の「1遠征で数百体を倒す」テンポに合わせて1体あたりの確率へ直したもの。
    /// </summary>
    public static class Loot
    {
        // Keep Common/Uncommon/Rare in the same proportions; Epic/Legendary have 40% of their former relative weight.
        private static readonly int[] BaseRarityWeights = { 3000, 1350, 500, 30, 2 };

        /// <summary>夢の深度1あたりの装備ドロップ率の増分。</summary>
        public const double HeatDropBonus = 0.35;
        /// <summary>夢の深度1あたりの幸運（レア度の上振れ）。</summary>
        public const double HeatLuck = 0.3;

        public static double DropChance(MonsterTier tier, int heat)
        {
            double baseChance;
            switch (tier)
            {
                case MonsterTier.Lesser: baseChance = 0.008; break; // v1.19：枠が6つに増えたので、約3割増し
                case MonsterTier.Normal: baseChance = 0.027; break;
                case MonsterTier.MiniBoss: baseChance = 0.45; break;
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
        /// <summary>
        /// 画面に出す「良い遺物の出やすさ」の%。レア度が1段上がるごとに、抽選の重みがこの%だけ多く掛かる（RollRarity の f = 1 + 0.6×luck）。
        /// </summary>
        public static double LuckPercent(double luck) => 60.0 * Math.Max(0, luck);

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
            IReadOnlyList<Relic> ownedRelics = null, IReadOnlyList<Relic> unsecuredRelics = null, ISet<string> codex = null)
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
            // v1.32（設計 3.4）：低レアは「その枠の土台」（重み1）と「その枠・そのレア度の銘品」（レア度ごとの定数）から抽選する。
            // 銘品が1つもなければ今までどおり土台だけの抽選（乱数列も同じ）。
            var named = NamedCandidates(rarity, slot);
            if (named.Count == 0)
            {
                var only = PickWeighted(rng, bases, b => b.Line, focus);
                return RollBaseRelic(rng, only, rarity, itemLevel);
            }
            var picks = new List<(BaseDef Base, NamedDef Named, int Weight)>(bases.Count + named.Count);
            int totalWeight = 0;
            foreach (var b in bases)
            {
                int w = focus != null && b.Line == focus.Value ? FocusWeight : 1;
                picks.Add((b, null, w));
                totalWeight += w;
            }
            foreach (var n in named)
            {
                var baseDef = Content.GetBase(n.BaseId);
                int w = NamedWeight(n, baseDef, focus, codex, ownedRelics, unsecuredRelics);
                picks.Add((baseDef, n, w));
                totalWeight += w;
            }
            int roll = rng.Range(0, totalWeight - 1);
            foreach (var pick in picks)
            {
                if (roll < pick.Weight)
                    return pick.Named != null ? RollNamed(rng, pick.Named, itemLevel) : RollBaseRelic(rng, pick.Base, rarity, itemLevel);
                roll -= pick.Weight;
            }
            var last = picks[picks.Count - 1];
            return last.Named != null ? RollNamed(rng, last.Named, itemLevel) : RollBaseRelic(rng, last.Base, rarity, itemLevel);
        }

        /// <summary>その枠・そのレア度の銘品の候補。土台が定義にない銘品は候補に入れない（データ検証で弾く前提）。</summary>
        private static List<NamedDef> NamedCandidates(Rarity rarity, Slot? slot)
        {
            if (rarity != Rarity.Uncommon && rarity != Rarity.Rare && rarity != Rarity.Epic) return new List<NamedDef>();
            var named = new List<NamedDef>();
            foreach (var n in NamedItems.All)
            {
                if (n.Rarity != rarity) continue;
                if (!Content.TryGetBase(n.BaseId, out var baseDef)) continue;
                if (slot != null && baseDef.Slot != slot.Value) continue;
                named.Add(n);
            }
            return named;
        }

        /// <summary>銘品1種の重み（設計 3.4）。レア度ごとの定数 × 狙い系統2倍 × 図鑑にまだない3倍 × 始めた組の未所持部位4倍。
        /// 図鑑を受け取らない呼び出しでは3倍を掛けない（分からないものを「未所持」とは扱わない）。</summary>
        internal static int NamedWeight(NamedDef def, BaseDef baseDef, Line? focus, ISet<string> codex,
            IReadOnlyList<Relic> ownedRelics, IReadOnlyList<Relic> unsecuredRelics)
        {
            int w = NamedItems.RarityWeight(def.Rarity);
            if (focus != null && baseDef.Line == focus.Value) w *= FocusWeight;
            if (codex != null && !codex.Contains(NamedItems.CodexId(def.Id))) w *= NamedItems.NotInCodexMultiplier;
            if (IsMissingMiniSetPiece(def, ownedRelics, unsecuredRelics)) w *= NamedItems.MissingMiniSetPieceMultiplier;
            return w;
        }

        /// <summary>銘品を新しく抽選する。固有効果は定義の固定値、特性はレア度どおりに個体ごとに抽選する（設計 3.2）。</summary>
        internal static Relic RollNamed(Rng rng, NamedDef def, int itemLevel)
        {
            var baseDef = Content.GetBase(def.BaseId);
            var r = new Relic
            {
                Uid = rng.NextUid(),
                BaseId = def.BaseId,
                NamedId = def.Id,
                Rarity = def.Rarity,
                ItemLevel = ClampLevel(itemLevel),
            };
            RollAffixes(rng, r, Content.AffixCount(def.Rarity), baseDef);
            r.Powers.AddRange(def.Powers);
            return r;
        }

        /// <summary>指定された土台の通常遺物を新しく抽選する（双子の鏡用）。</summary>
        internal static Relic RollBaseRelic(Rng rng, BaseDef baseDef, Rarity rarity, int itemLevel)
        {

            var r = new Relic
            {
                Uid = rng.NextUid(),
                BaseId = baseDef.Id,
                Rarity = rarity,
                ItemLevel = ClampLevel(itemLevel),
            };
            RollAffixes(rng, r, Content.AffixCount(rarity), baseDef);
            // v1.22：レア度にふさわしい固有効果。レアは弱いものを1つ、エピックは範囲どおりの1つと弱いもう1つ。
            var pool = Content.PowerPool(baseDef.Slot);
            if (rarity == Rarity.Epic)
            {
                var pr = PickPower(rng, pool, baseDef.Family, false, r);
                r.Powers.Add(new PowerLine(pr.Power, rng.Range(pr.Min, pr.Max)));
                AddMinorPower(rng, r, pool, baseDef.Family);
            }
            else if (rarity == Rarity.Rare)
            {
                AddMinorPower(rng, r, pool, baseDef.Family);
            }
            return r;
        }

        /// <summary>
        /// 固有効果の候補を重み付きで1つ選ぶ。minor=false はエピックの1つ目（従来どおり全プールから）、
        /// minor=true は「レア度で許可され、まだ持っていない」候補から。家系の好みは許可の判定のあとで重み2倍。
        /// Plain は全部の重みが1なので、従来の一様抽選（Range(0, n-1)）と同じ乱数列になる。
        /// </summary>
        private static PowerRange PickPower(Rng rng, IReadOnlyList<PowerRange> pool, Family family, bool minor, Relic r)
        {
            var candidates = new List<PowerRange>();
            foreach (var pr in pool)
            {
                if (minor && (!Content.PowerAllowedForRarity(pr.Power, r.Rarity) || r.Powers.Exists(x => x.Power == pr.Power))) continue;
                candidates.Add(pr);
            }
            if (candidates.Count == 0) return null;
            int total = 0;
            foreach (var pr in candidates) total += FamilyPrefs.PrefersPower(family, pr.Power) ? FamilyPrefs.PreferredWeightMultiplier : 1;
            int x = rng.Range(0, total - 1);
            foreach (var pr in candidates)
            {
                int w = FamilyPrefs.PrefersPower(family, pr.Power) ? FamilyPrefs.PreferredWeightMultiplier : 1;
                if (x < w) return pr;
                x -= w;
            }
            return candidates[candidates.Count - 1];
        }

        /// <summary>まだ持っていない固有効果を1つ、範囲の下半分の値で足す（レアと、エピックの2つ目）。</summary>
        private static void AddMinorPower(Rng rng, Relic r, IReadOnlyList<PowerRange> pool, Family family)
        {
            var pick = PickPower(rng, pool, family, true, r);
            if (pick == null) return;
            int top = Math.Max(pick.Min, pick.Min + (pick.Max - pick.Min) / 2);
            r.Powers.Add(new PowerLine(pick.Power, rng.Range(pick.Min, top)));
        }

        /// <summary>狙い系統の重み。狙った系統は2倍出やすい（計画書 付録B）。</summary>
        public const int FocusWeight = 2;

        /// <summary>所持しているセットの未所持部位の重み。狙い系統の重みと掛け合わせる。</summary>
        /// <summary>伝説の抽選でセット品そのものが選ばれやすくなる倍率。</summary>
        public const int SetPieceWeight = 4; // v1.22：セット24種。一般の固有品とのつり合いを保つ

        public const int SetCompletionWeight = 60;

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

        /// <summary>始めた組（小セット）の未所持部位か（設計 3.4）。既に持っている部位は false、組を始めていなければ false。</summary>
        private static bool IsMissingMiniSetPiece(NamedDef candidate, IReadOnlyList<Relic> ownedRelics, IReadOnlyList<Relic> unsecuredRelics)
        {
            if (candidate.MiniSetId == null) return false;
            bool started = false;
            for (int source = 0; source < 2; source++)
            {
                var relics = source == 0 ? ownedRelics : unsecuredRelics;
                if (relics == null) continue;
                for (int i = 0; i < relics.Count; i++)
                {
                    string id = relics[i].NamedId;
                    if (id == candidate.Id) return false;
                    if (!started && NamedItems.TryGetNamed(id, out var named) && named.MiniSetId == candidate.MiniSetId)
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
        internal static void RollAffixes(Rng rng, Relic r, int count) => RollAffixes(rng, r, count, r.Base);

        internal static void RollAffixes(Rng rng, Relic r, int count, BaseDef basis)
        {
            var used = new HashSet<Stat> { basis.ImplicitStat };
            foreach (var a in r.Affixes) used.Add(a.Stat);
            while (r.Affixes.Count < count)
            {
                var line = RollAffix(rng, basis.Slot, r.Rarity, r.ItemLevel, used, basis.Family);
                if (line == null) break;
                used.Add(line.Stat);
                r.Affixes.Add(line);
            }
        }

        private static int AffixWeight(AffixDef a, Family family) =>
            FamilyPrefs.PrefersStat(family, a.Stat) ? a.Weight * FamilyPrefs.PreferredWeightMultiplier : a.Weight;

        /// <summary>特性を1つ抽選する。family の好む能力値は、レア度の下限の判定のあとで重み2倍（Plain は変えない）。</summary>
        public static StatLine RollAffix(Rng rng, Slot slot, Rarity rarity, int itemLevel, ICollection<Stat> exclude, Family family = Family.Plain)
        {
            var pool = Content.AffixPool(slot);
            int total = 0;
            foreach (var a in pool)
                if (!exclude.Contains(a.Stat) && rarity >= a.MinRarity) total += AffixWeight(a, family);
            if (total <= 0) return null;
            int x = rng.Range(0, total - 1);
            foreach (var a in pool)
            {
                if (exclude.Contains(a.Stat) || rarity < a.MinRarity) continue;
                int weight = AffixWeight(a, family);
                if (x < weight)
                {
                    int raw = rng.Range(a.Min, a.Max);
                    int level = Content.ScalesWithItemLevel(a.Stat) ? Content.LevelScalePct(itemLevel) : 100;
                    int pct = Content.RarityValuePct(rarity) * level / 100;
                    return new StatLine(a.Stat, Relic.Scale(raw, pct));
                }
                x -= weight;
            }
            return null;
        }

        /// <summary>
        /// 1体の撃破に対する個人の報酬を抽選する。協力時は各プレイヤーが自分の分を独立に抽選する
        /// （計画書 第14章「確保と損失は個人ごと」）。天井（救済）はなく、主報酬は通常抽選のみ。
        /// </summary>
        public static KillReward RollKill(Rng rng, MonsterTier tier, int itemLevel, int heat, Line? focus = null, Pacts.Totals mods = null,
            IReadOnlyList<Relic> ownedRelics = null, IReadOnlyList<Relic> unsecuredRelics = null, ISet<string> codex = null)
        {
            heat = ClampHeat(heat);
            var reward = new KillReward { Xp = Content.KillXp(tier) };
            double luck = TierLuck(tier) + HeatLuck * heat + (mods?.Luck ?? 0);
            bool allowLegendary = tier >= MonsterTier.MiniBoss;
            double chance = Math.Min(1.0, DropChance(tier, heat) * (1.0 + (mods?.DropBonus ?? 0)));

            if (rng.Chance(chance))
            {
                Rarity floor = tier == MonsterTier.Boss ? Rarity.Uncommon : Rarity.Common;
                var rarity = RollRarity(rng, luck, allowLegendary, floor);
                reward.Relics.Add(RollRelic(rng, rarity, itemLevel, null, focus, ownedRelics, unsecuredRelics, codex));
                if (tier == MonsterTier.Boss && rng.Chance(0.6))
                    reward.Relics.Add(RollRelic(rng, RollRarity(rng, luck, true, Rarity.Uncommon), itemLevel, null, focus, ownedRelics, unsecuredRelics, codex));
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

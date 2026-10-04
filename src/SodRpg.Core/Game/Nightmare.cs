using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>悪夢化した敵の接頭効果（ビット集合）。複数を併せ持つことがある。</summary>
    [Flags]
    public enum NightmareAffix
    {
        None = 0,
        /// <summary>鋼殻：防御+60</summary>
        Ironclad = 1 << 0,
        /// <summary>狂暴：攻撃力+40%・攻撃速度+30%</summary>
        Berserk = 1 << 1,
        /// <summary>巨躯：最大HP+150%</summary>
        Colossal = 1 << 2,
        /// <summary>疾風：移動速度+35%・攻撃速度+20%</summary>
        Swift = 1 << 3,
        /// <summary>再生：毎秒 最大HPの2%回復</summary>
        Regenerating = 1 << 4,
        /// <summary>魔力：魔力+50%・スキル加速+40</summary>
        Arcane = 1 << 5,
        /// <summary>結界：出現時に最大HPの25%の障壁（v1.24）</summary>
        Warded = 1 << 6,
        /// <summary>棘皮：受けたダメージの20%を攻撃者へ返す（v1.24）</summary>
        Thorned = 1 << 7,
        /// <summary>飢渇：与えたダメージの15%だけ回復する（v1.24）</summary>
        Ravenous = 1 << 8,
        /// <summary>破甲：攻撃が当たると、相手の防御を4秒間 20 下げる（v1.24）</summary>
        Sundering = 1 << 9,
        Veiled = 1 << 10,
        Hollow = 1 << 11,
        Facing = 1 << 12,
        Packbound = 1 << 13,
        Beacon = 1 << 14,
        Pulsing = 1 << 15,
        Committed = 1 << 16,
        Skittish = 1 << 17,
        Recuperating = 1 << 18,
        LastStand = 1 << 19,
    }

    /// <summary>
    /// 悪夢化エリート（Diablo のチャンピオンに相当）。パーティの最大の潜行に応じて、ホストが一部の敵を悪夢化する。
    /// 悪夢化した敵は強く、倒すとエリート相当の戦利品・欠片・経験値が出る。
    /// </summary>
    public static class Nightmares
    {
        /// <summary>悪夢化の基礎体力の上乗せ（%）。見た目と専用攻撃は本体のエリート効果（MirageSkin）が担う。</summary>
        public const int BaseHealthPct = 40;

        public static readonly NightmareAffix[] AllAffixes =
        {
            NightmareAffix.Ironclad, NightmareAffix.Berserk, NightmareAffix.Colossal,
            NightmareAffix.Swift, NightmareAffix.Regenerating, NightmareAffix.Arcane,
            NightmareAffix.Warded, NightmareAffix.Thorned, NightmareAffix.Ravenous, NightmareAffix.Sundering,
            NightmareAffix.Veiled, NightmareAffix.Hollow, NightmareAffix.Facing, NightmareAffix.Packbound,
            NightmareAffix.Beacon, NightmareAffix.Pulsing, NightmareAffix.Committed, NightmareAffix.Skittish,
            NightmareAffix.Recuperating, NightmareAffix.LastStand,
        };

        // ───── v1.24：敵側のつり合い ─────

        /// <summary>結界：出現時の障壁（最大HPに対する%）。</summary>
        public const int WardShieldPct = 25;
        /// <summary>棘皮：受けたダメージを返す割合（%）。v1.31 で20→15、さらに1回の上限を設けた。</summary>
        public const int ThornsReflectPct = 15;
        /// <summary>棘皮：1回に返す量の上限（攻撃した旅人の最大HPに対する%、夢の圧・潜行の倍率を掛ける前）。</summary>
        public const float ThornsReflectMaxHealthPct = 1.5f;
        /// <summary>棘皮：同じ敵から同じ旅人へ返す間隔（秒）。多段攻撃・連打で何度も返さない。</summary>
        public const float ThornsReflectInterval = 0.4f;

        /// <summary>
        /// 棘皮で返す量。プレイヤーの火力は HP よりずっと伸びやすいので、与えたダメージの割合だけで返すと
        /// 強くなるほど自滅する（v1.31、利用者の指摘）。攻撃した旅人の最大HPの割合で頭打ちにする。
        /// </summary>
        public static float ThornsReflectAmount(float damageDealt, float attackerMaxHealth)
        {
            if (damageDealt <= 0f || attackerMaxHealth <= 0f) return 0f;
            return Math.Min(damageDealt * ThornsReflectPct / 100f, attackerMaxHealth * ThornsReflectMaxHealthPct / 100f);
        }
        /// <summary>飢渇：与えたダメージのうち回復する割合（%）。</summary>
        public const int RavenousLeechPct = 15;
        /// <summary>破甲：当たったプレイヤーの防御を下げる量と秒数。</summary>
        public const int SunderArmor = 20;
        public const float SunderSeconds = 4f;

        /// <summary>
        /// 深度に応じた、すべての敵（悪夢化していない敵を含む）への上乗せ。
        /// 通常の敵：深度1ごとに 最大HP+8%・攻撃力+4%、深度3から防御+10。ボス：深度4から 最大HP+10%/深度。
        /// </summary>
        public static List<StatLine> DepthBonus(MonsterTier tier, int depth)
        {
            depth = Loot.ClampHeat(depth);
            var list = new List<StatLine>();
            if (depth <= 0) return list;
            if (tier == MonsterTier.Boss)
            {
                if (depth >= 4) list.Add(new StatLine(Stat.MaxHealthPct, 10 * depth));
                return list;
            }
            list.Add(new StatLine(Stat.MaxHealthPct, 8 * depth));
            list.Add(new StatLine(Stat.AttackPct, 4 * depth));
            if (depth >= 3) list.Add(new StatLine(Stat.Armor, 10));
            return list;
        }

        /// <summary>
        /// 装備の強さから、悪夢化の確率の倍率を出す（1.0〜1.5）。強い装備で潜るほど手応えのある敵が増える。
        /// 強さ＝攻撃力%・魔力%の大きい方＋最大HP%の半分。100で+25%、200以上で+50%。
        /// </summary>
        public static double GearChanceMult(Build b)
        {
            if (b == null) return 1.0;
            int offense = Math.Max(b.Get(Stat.AttackPct), b.Get(Stat.PowerPct));
            int score = offense + b.Get(Stat.MaxHealthPct) / 2;
            return 1.0 + Math.Min(0.5, Math.Max(0, score) / 400.0);
        }

        /// <summary>悪夢化の確率。深度0では起きない。通常の敵は深度1で2%・以後+2%、エリートは深度1で20%・以後+8%。ボスは対象外。</summary>
        public static double Chance(MonsterTier tier, int depth)
        {
            depth = Loot.ClampHeat(depth);
            if (depth <= 0) return 0;
            switch (tier)
            {
                case MonsterTier.Lesser: return 0.01 * depth;
                case MonsterTier.Normal: return 0.02 * depth;
                case MonsterTier.MiniBoss: return 0.20 + 0.08 * (depth - 1);
                default: return 0;
            }
        }

        /// <summary>悪夢化するか抽選し、するなら接頭効果（深度3以上で2つ、5で3つ）を返す。</summary>
        public static NightmareAffix Roll(Rng rng, MonsterTier tier, int depth, double chanceMult = 1.0)
        {
            if (!rng.Chance(Math.Min(1.0, Chance(tier, depth) * chanceMult))) return NightmareAffix.None;
            int count = depth >= 5 ? 3 : depth >= 3 ? 2 : 1;
            var pool = new List<NightmareAffix>(AllAffixes);
            var result = NightmareAffix.None;
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int k = rng.Range(0, pool.Count - 1);
                var selected = pool[k];
                result |= selected;
                pool.RemoveAt(k);
                if (selected == NightmareAffix.Regenerating) pool.Remove(NightmareAffix.Recuperating);
                else if (selected == NightmareAffix.Recuperating) pool.Remove(NightmareAffix.Regenerating);
            }
            return result;
        }

        public static int Count(NightmareAffix a)
        {
            int n = 0;
            foreach (var x in AllAffixes)
                if ((a & x) != 0) n++;
            return n;
        }

        /// <summary>悪夢化した敵へ付ける能力（表示単位）。HP回復は割合なので別に返す。</summary>
        public static List<StatLine> MonsterStats(NightmareAffix a, out float regenPctPerSecond)
        {
            var list = new List<StatLine> { new StatLine(Stat.MaxHealthPct, BaseHealthPct) };
            regenPctPerSecond = 0;
            if ((a & NightmareAffix.Ironclad) != 0) list.Add(new StatLine(Stat.Armor, 60));
            if ((a & NightmareAffix.Berserk) != 0)
            {
                list.Add(new StatLine(Stat.AttackPct, 40));
                list.Add(new StatLine(Stat.AttackSpeedPct, 30));
            }
            if ((a & NightmareAffix.Colossal) != 0) list.Add(new StatLine(Stat.MaxHealthPct, 150));
            if ((a & NightmareAffix.Swift) != 0)
            {
                list.Add(new StatLine(Stat.MoveSpeedPct, 35));
                list.Add(new StatLine(Stat.AttackSpeedPct, 20));
            }
            if ((a & NightmareAffix.Regenerating) != 0) regenPctPerSecond = 2f;
            if ((a & NightmareAffix.Arcane) != 0)
            {
                list.Add(new StatLine(Stat.PowerPct, 50));
                list.Add(new StatLine(Stat.Haste, 40));
            }
            // v1.24：行動に関わる性質（障壁・反射・吸収・防御低下）はホストが処理する。能力値は控えめに足すだけ。
            if ((a & NightmareAffix.Warded) != 0) list.Add(new StatLine(Stat.Armor, 20));
            if ((a & NightmareAffix.Thorned) != 0) list.Add(new StatLine(Stat.Armor, 30));
            if ((a & NightmareAffix.Ravenous) != 0) list.Add(new StatLine(Stat.AttackPct, 15));
            if ((a & NightmareAffix.Sundering) != 0) list.Add(new StatLine(Stat.AttackPct, 10));
            return list;
        }

        public static string AffixName(NightmareAffix a)
        {
            switch (a)
            {
                case NightmareAffix.Ironclad: return Loc.T("鋼殻", "Ironclad");
                case NightmareAffix.Berserk: return Loc.T("狂暴", "Berserk");
                case NightmareAffix.Colossal: return Loc.T("巨躯", "Colossal");
                case NightmareAffix.Swift: return Loc.T("疾風", "Swift");
                case NightmareAffix.Regenerating: return Loc.T("再生", "Regenerating");
                case NightmareAffix.Arcane: return Loc.T("魔力", "Arcane");
                case NightmareAffix.Warded: return Loc.T("結界", "Warded");
                case NightmareAffix.Thorned: return Loc.T("棘皮", "Thorned");
                case NightmareAffix.Ravenous: return Loc.T("飢渇", "Ravenous");
                case NightmareAffix.Sundering: return Loc.T("破甲", "Sundering");
                case NightmareAffix.Veiled: return Loc.T("霞衣", "Veiled");
                case NightmareAffix.Hollow: return Loc.T("空洞", "Hollow");
                case NightmareAffix.Facing: return Loc.T("正面守り", "Facing");
                case NightmareAffix.Packbound: return Loc.T("群れの守り", "Packbound");
                case NightmareAffix.Beacon: return Loc.T("灯台", "Beacon");
                case NightmareAffix.Pulsing: return Loc.T("明滅", "Pulsing");
                case NightmareAffix.Committed: return Loc.T("大振り", "Committed");
                case NightmareAffix.Skittish: return Loc.T("臆病", "Skittish");
                case NightmareAffix.Recuperating: return Loc.T("傷繕い", "Recuperating");
                case NightmareAffix.LastStand: return Loc.T("最後の殻", "Last Stand");
                default: return "";
            }
        }

        public static bool HasBehavior(NightmareAffix a) => (a & (NightmareAffix)0xFFC00) != 0;

        public static string AffixDescription(NightmareAffix a)
        {
            switch (a)
            {
                case NightmareAffix.Ironclad: return Loc.T("防御+60。強い一撃で殻を突破。", "Armor +60; use strong hits to breach its shell.");
                case NightmareAffix.Berserk: return Loc.T("攻撃力+40%・攻撃速度+30%。連撃を避けて反撃。", "Attack +40%, attack speed +30%; dodge its flurry, then retaliate.");
                case NightmareAffix.Colossal: return Loc.T("最大HP+150%。長期戦に備え、攻撃を避け続ける。", "Max HP +150%; conserve resources and keep dodging.");
                case NightmareAffix.Swift: return Loc.T("移動+35%・攻撃速度+20%。直線逃走より回避で切り返す。", "Movement +35%, attack speed +20%; dodge and turn rather than flee straight.");
                case NightmareAffix.Regenerating: return Loc.T("毎秒最大HPの2%回復。攻撃を集中して倒す。", "Heals 2% max HP each second; focus damage to defeat it.");
                case NightmareAffix.Arcane: return Loc.T("魔力+50%・スキル加速+40。術を避け、発動後に攻める。", "Power +50%, haste +40; evade spells and punish after casting.");
                case NightmareAffix.Warded: return Loc.T("防御+20、出現時に最大HP25%の障壁。障壁を割って攻める。", "Armor +20 and a spawn shield of 25% max HP; break the barrier.");
                case NightmareAffix.Thorned: return Loc.T("防御+30。旅人から受けたダメージの15%をその旅人へ返す（1回につき最大HPの1.5%まで、夢の圧と潜行で増える。0.4秒に1回まで。召喚獣の攻撃は返さない）。", "Armor +30. Returns 15% of damage taken from a traveler to that traveler (at most 1.5% of their max HP per hit, raised by dream pressure and delve; once per 0.4 s; not to summons).");
                case NightmareAffix.Ravenous: return Loc.T("攻撃力+15%、与ダメージの15%回復。攻撃を避けて回復を防ぐ。", "Attack +15%; heals for 15% of damage dealt. Dodge to deny healing.");
                case NightmareAffix.Sundering: return Loc.T("攻撃力+10%、命中で防御-20を4秒。追撃を避ける。", "Attack +10%; hits reduce armor by 20 for 4s. Avoid follow-up hits.");
                case NightmareAffix.Veiled: return Loc.T("6mより遠い攻撃の被ダメージ-30%。6m以内へ近づく。", "Receives 30% less damage from beyond 6m; approach within 6m.");
                case NightmareAffix.Hollow: return Loc.T("3m未満からの被ダメージ-30%。3m以上離れて攻撃。", "Receives 30% less damage from within 3m; strike from at least 3m.");
                case NightmareAffix.Facing: return Loc.T("正面120度の被ダメージ-30%。側面や背後へ回る。", "Receives 30% less damage in its frontal 120-degree cone; flank or attack from behind.");
                case NightmareAffix.Packbound: return Loc.T("同区画5m以内に生存し活動中の味方がいると被ダメージ-30%。引き離すか仲間を倒す。", "Receives 30% less damage with a living awake ally within 5m in the same section; separate or clear allies.");
                case NightmareAffix.Beacon: return Loc.T("2秒の予告後、一生に一度、同区画5m以内の最寄りの活動中の非ボス味方に最大HP15%の障壁を6秒。重複不可。先に倒すか引き離す。", "After a 2s warning, once per life shields the nearest living awake nonboss ally within 5m in the same section for 15% target max HP for 6s; no stacking. Kill support first or separate.");
                case NightmareAffix.Pulsing: return Loc.T("最初の2秒は無防備。その後3秒間被ダメージ-30%、3秒間無防備を繰り返す。無防備の間に集中攻撃。", "Initially open for 2s, then alternates 3s of 30% damage reduction and 3s open; burst during openings.");
                case NightmareAffix.Committed: return Loc.T("詠唱中は被ダメージ-30%、攻撃発射後1.5秒は被ダメージ+20%が優先。回避後に反撃。", "Receives 30% less damage while channeling; 1.5s after firing an attack, takes 20% more instead. Dodge then punish recovery.");
                case NightmareAffix.Skittish: return Loc.T("未被弾時は移動+20%、ダメージを受けると2秒間移動-15%。一撃を当てて追う。", "Movement +20% while unhit; damaging hits slow movement by 15% for 2s. Tag then pursue.");
                case NightmareAffix.Recuperating: return Loc.T("4秒間無傷なら毎秒最大HP1%回復、一生の上限10%。満タンでは消費しない。攻撃を続ける。", "After 4s without damage heals 1% max HP per second, capped at 10% per life; no budget spent at full HP. Keep pressure.");
                case NightmareAffix.LastStand: return Loc.T("HP35%以下で2秒予告後、一生に一度、最大HP15%の障壁を6秒。予告中に倒すか殻を割る。", "At 35% HP or less, warns for 2s then shields itself for 15% max HP for 6s, once per life. Finish during warning or break the shell.");
                default: return "";
            }
        }

        /// <summary>頭上に出す名札。例：「悪夢・鋼殻・狂暴」。</summary>
        public static string Label(NightmareAffix a)
        {
            var parts = new List<string> { Loc.T("悪夢", "Nightmare") };
            foreach (var x in AllAffixes)
                if ((a & x) != 0) parts.Add(AffixName(x));
            return string.Join(Loc.T("・", " "), parts);
        }

        /// <summary>悪夢化した敵の撃破を、何の格として抽選するか。通常・雑魚はエリート相当、エリートはボス相当。</summary>
        public static MonsterTier RewardTier(MonsterTier tier)
        {
            switch (tier)
            {
                case MonsterTier.Lesser:
                case MonsterTier.Normal: return MonsterTier.MiniBoss;
                case MonsterTier.MiniBoss: return MonsterTier.Boss;
                default: return tier;
            }
        }

        /// <summary>通信用：接頭効果を検証して返す（未知のビットは落とす）。</summary>
        public static NightmareAffix Sanitize(int raw)
        {
            var all = NightmareAffix.None;
            foreach (var x in AllAffixes) all |= x;
            return (NightmareAffix)raw & all;
        }
    }
}

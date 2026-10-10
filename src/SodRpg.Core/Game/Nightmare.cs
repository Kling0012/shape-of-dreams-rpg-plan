using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>悪夢化した敵の接頭効果（ビット集合）。複数を併せ持つことがある。</summary>
    [Flags]
    public enum NightmareAffix
    {
        None = 0,
        /// <summary>鋼殻：防御を上乗せ</summary>
        Ironclad = 1 << 0,
        /// <summary>狂暴：攻撃力・攻撃速度を上乗せ</summary>
        Berserk = 1 << 1,
        /// <summary>巨躯：最大HPを上乗せ</summary>
        Colossal = 1 << 2,
        /// <summary>疾風：移動速度・攻撃速度を上乗せ</summary>
        Swift = 1 << 3,
        /// <summary>再生：毎秒、最大HPに応じて回復</summary>
        Regenerating = 1 << 4,
        /// <summary>魔力：魔力・スキル加速を上乗せ</summary>
        Arcane = 1 << 5,
        /// <summary>結界：出現時に最大HPに応じた障壁</summary>
        Warded = 1 << 6,
        /// <summary>棘皮：受けたダメージの一部を攻撃者へ返す</summary>
        Thorned = 1 << 7,
        /// <summary>飢渇：与えたダメージの一部だけ回復する</summary>
        Ravenous = 1 << 8,
        /// <summary>破甲：命中した相手の防御を一定時間下げる</summary>
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
        public const int BaseHealthPct = MonstersBalance.BaseHealthPct;

        public const int BossDepthMinimum = MonstersBalance.BossDepthMinimum;
        public const int BossHealthPctPerDepth = MonstersBalance.BossHealthPctPerDepth;
        public const int HealthPctPerDepth = MonstersBalance.HealthPctPerDepth;
        public const int AttackPctPerDepth = MonstersBalance.AttackPctPerDepth;
        public const int ArmorDepthMinimum = MonstersBalance.ArmorDepthMinimum;
        public const int DepthArmor = MonstersBalance.DepthArmor;
        public const int GearHealthDivisor = MonstersBalance.GearHealthDivisor;
        public const double GearMaximumChanceBonus = MonstersBalance.GearMaximumChanceBonus;
        public const double GearScoreDivisor = MonstersBalance.GearScoreDivisor;
        public const double LesserChancePerDepth = MonstersBalance.LesserChancePerDepth;
        public const double NormalChancePerDepth = MonstersBalance.NormalChancePerDepth;
        public const double MiniBossBaseChance = MonstersBalance.MiniBossBaseChance;
        public const double MiniBossChancePerDepth = MonstersBalance.MiniBossChancePerDepth;
        public const int TwoAffixDepth = MonstersBalance.TwoAffixDepth;
        public const int ThreeAffixDepth = MonstersBalance.ThreeAffixDepth;
        public const int BaseAffixCount = MonstersBalance.BaseAffixCount;
        public const int TwoAffixCount = MonstersBalance.TwoAffixCount;
        public const int ThreeAffixCount = MonstersBalance.ThreeAffixCount;
        public const int IroncladArmor = MonstersBalance.IroncladArmor;
        public const int BerserkAttackPct = MonstersBalance.BerserkAttackPct;
        public const int BerserkAttackSpeedPct = MonstersBalance.BerserkAttackSpeedPct;
        public const int ColossalHealthPct = MonstersBalance.ColossalHealthPct;
        public const int SwiftMoveSpeedPct = MonstersBalance.SwiftMoveSpeedPct;
        public const int SwiftAttackSpeedPct = MonstersBalance.SwiftAttackSpeedPct;
        public const float RegenerationPctPerSecond = MonstersBalance.RegenerationPctPerSecond;
        public const int ArcanePowerPct = MonstersBalance.ArcanePowerPct;
        public const int ArcaneHaste = MonstersBalance.ArcaneHaste;
        public const int WardedArmor = MonstersBalance.WardedArmor;
        public const int ThornedArmor = MonstersBalance.ThornedArmor;
        public const int RavenousAttackPct = MonstersBalance.RavenousAttackPct;
        public const int SunderingAttackPct = MonstersBalance.SunderingAttackPct;

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
        public const int WardShieldPct = MonstersBalance.WardShieldPct;
        /// <summary>棘皮：受けたダメージを返す割合（%）。v1.31 で20→15、さらに1回の上限を設けた。</summary>
        public const int ThornsReflectPct = MonstersBalance.ThornsReflectPct;
        /// <summary>棘皮：1回に返す量の上限（攻撃した旅人の最大HPに対する%、夢の圧・潜行の倍率を掛ける前）。</summary>
        public const float ThornsReflectMaxHealthPct = MonstersBalance.ThornsReflectMaxHealthPct;
        /// <summary>棘皮：同じ敵から同じ旅人へ返す間隔（秒）。多段攻撃・連打で何度も返さない。</summary>
        public const float ThornsReflectInterval = MonstersBalance.ThornsReflectInterval;

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
        public const int RavenousLeechPct = MonstersBalance.RavenousLeechPct;
        /// <summary>破甲：当たったプレイヤーの防御を下げる量と秒数。</summary>
        public const int SunderArmor = MonstersBalance.SunderArmor;
        public const float SunderSeconds = MonstersBalance.SunderSeconds;

        /// <summary>
        /// 深度に応じた、すべての敵（悪夢化していない敵を含む）への上乗せ。
        /// 通常の敵は最大HP・攻撃力と、一定深度から防御を上乗せ。ボスのHP補正は別の深度から始まる。
        /// </summary>
        public static List<StatLine> DepthBonus(MonsterTier tier, int depth)
        {
            depth = Loot.ClampHeat(depth);
            var list = new List<StatLine>();
            if (depth <= 0) return list;
            if (tier == MonsterTier.Boss)
            {
                if (depth >= BossDepthMinimum) list.Add(new StatLine(Stat.MaxHealthPct, BossHealthPctPerDepth * depth));
                return list;
            }
            list.Add(new StatLine(Stat.MaxHealthPct, HealthPctPerDepth * depth));
            list.Add(new StatLine(Stat.AttackPct, AttackPctPerDepth * depth));
            if (depth >= ArmorDepthMinimum) list.Add(new StatLine(Stat.Armor, DepthArmor));
            return list;
        }

        /// <summary>
        /// 装備の強さから、悪夢化の確率の倍率を出す。強い装備で潜るほど手応えのある敵が増える。
        /// 強さは攻撃力%・魔力%の大きい方に最大HP%の寄与を加え、表の係数で倍率へ変換する。
        /// </summary>
        public static double GearChanceMult(Build b)
        {
            if (b == null) return 1.0;
            int offense = Math.Max(b.Get(Stat.AttackPct), b.Get(Stat.PowerPct));
            int score = offense + b.Get(Stat.MaxHealthPct) / GearHealthDivisor;
            return 1.0 + Math.Min(GearMaximumChanceBonus, Math.Max(0, score) / GearScoreDivisor);
        }

        /// <summary>悪夢化の確率。深度0では起きない。各格の深度曲線は調整表を参照し、ボスは対象外。</summary>
        public static double Chance(MonsterTier tier, int depth)
        {
            depth = Loot.ClampHeat(depth);
            if (depth <= 0) return 0;
            switch (tier)
            {
                case MonsterTier.Lesser: return LesserChancePerDepth * depth;
                case MonsterTier.Normal: return NormalChancePerDepth * depth;
                case MonsterTier.MiniBoss: return MiniBossBaseChance + MiniBossChancePerDepth * (depth - 1);
                default: return 0;
            }
        }

        /// <summary>深度に応じた接頭効果の数。</summary>
        public static int AffixCount(int depth) => depth >= ThreeAffixDepth ? ThreeAffixCount
            : depth >= TwoAffixDepth ? TwoAffixCount : BaseAffixCount;

        public static NightmareAffix Roll(Rng rng, MonsterTier tier, int depth, double chanceMult = 1.0)
        {
            if (!rng.Chance(Math.Min(1.0, Chance(tier, depth) * chanceMult))) return NightmareAffix.None;
            int count = AffixCount(depth);
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

        /// <summary>
        /// 道標を踏まえた悪夢化の抽選（ホストの出現処理と同じ規則）。ボスは対象外（#127）。
        /// 明けない夜（全敵悪夢化）でもボスは悪夢化せず、抽選前に返すため乱数も消費しない。
        /// </summary>
        public static NightmareAffix RollWaypoint(Rng rng, MonsterTier tier, int depth, double chanceMult, Waypoints.Totals waypoint)
        {
            if (tier >= MonsterTier.Boss) return NightmareAffix.None;
            if (waypoint != null && waypoint.AllNightmares)
            {
                // One native nightmare trait keeps the opt-in rule bounded even at zero delve heat.
                return AllAffixes[rng.Range(0, AllAffixes.Length - 1)];
            }
            double waypointMult = waypoint != null ? waypoint.NightmareChanceMultiplier : 1.0;
            int selectionDepth = waypointMult > 1 ? Math.Max(1, depth) : depth;
            return Roll(rng, tier, selectionDepth, chanceMult * waypointMult);
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
            if ((a & NightmareAffix.Ironclad) != 0) list.Add(new StatLine(Stat.Armor, IroncladArmor));
            if ((a & NightmareAffix.Berserk) != 0)
            {
                list.Add(new StatLine(Stat.AttackPct, BerserkAttackPct));
                list.Add(new StatLine(Stat.AttackSpeedPct, BerserkAttackSpeedPct));
            }
            if ((a & NightmareAffix.Colossal) != 0) list.Add(new StatLine(Stat.MaxHealthPct, ColossalHealthPct));
            if ((a & NightmareAffix.Swift) != 0)
            {
                list.Add(new StatLine(Stat.MoveSpeedPct, SwiftMoveSpeedPct));
                list.Add(new StatLine(Stat.AttackSpeedPct, SwiftAttackSpeedPct));
            }
            if ((a & NightmareAffix.Regenerating) != 0) regenPctPerSecond = RegenerationPctPerSecond;
            if ((a & NightmareAffix.Arcane) != 0)
            {
                list.Add(new StatLine(Stat.PowerPct, ArcanePowerPct));
                list.Add(new StatLine(Stat.Haste, ArcaneHaste));
            }
            // v1.24：行動に関わる性質（障壁・反射・吸収・防御低下）はホストが処理する。能力値は控えめに足すだけ。
            if ((a & NightmareAffix.Warded) != 0) list.Add(new StatLine(Stat.Armor, WardedArmor));
            if ((a & NightmareAffix.Thorned) != 0) list.Add(new StatLine(Stat.Armor, ThornedArmor));
            if ((a & NightmareAffix.Ravenous) != 0) list.Add(new StatLine(Stat.AttackPct, RavenousAttackPct));
            if ((a & NightmareAffix.Sundering) != 0) list.Add(new StatLine(Stat.AttackPct, SunderingAttackPct));
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
                case NightmareAffix.Ironclad: return Loc.T($"防御+{IroncladArmor}。強い一撃で殻を突破。", $"Armor +{IroncladArmor}; use strong hits to breach its shell.");
                case NightmareAffix.Berserk: return Loc.T($"攻撃力+{BerserkAttackPct}%・攻撃速度+{BerserkAttackSpeedPct}%。連撃を避けて反撃。", $"Attack +{BerserkAttackPct}%, attack speed +{BerserkAttackSpeedPct}%; dodge its flurry, then retaliate.");
                case NightmareAffix.Colossal: return Loc.T($"最大HP+{ColossalHealthPct}%。長期戦に備え、攻撃を避け続ける。", $"Max HP +{ColossalHealthPct}%; conserve resources and keep dodging.");
                case NightmareAffix.Swift: return Loc.T($"移動+{SwiftMoveSpeedPct}%・攻撃速度+{SwiftAttackSpeedPct}%。直線逃走より回避で切り返す。", $"Movement +{SwiftMoveSpeedPct}%, attack speed +{SwiftAttackSpeedPct}%; dodge and turn rather than flee straight.");
                case NightmareAffix.Regenerating: return Loc.T($"毎秒最大HPの{MonsterBehavior.Number((decimal)RegenerationPctPerSecond)}%回復。攻撃を集中して倒す。", $"Heals {MonsterBehavior.Number((decimal)RegenerationPctPerSecond)}% max HP each second; focus damage to defeat it.");
                case NightmareAffix.Arcane: return Loc.T($"魔力+{ArcanePowerPct}%・スキル加速+{ArcaneHaste}。術を避け、発動後に攻める。", $"Power +{ArcanePowerPct}%, haste +{ArcaneHaste}; evade spells and punish after casting.");
                case NightmareAffix.Warded: return Loc.T($"防御+{WardedArmor}、出現時に最大HP{WardShieldPct}%の障壁。障壁を割って攻める。", $"Armor +{WardedArmor} and a spawn shield of {WardShieldPct}% max HP; break the barrier.");
                case NightmareAffix.Thorned: return Loc.T($"防御+{ThornedArmor}。旅人から受けたダメージの{ThornsReflectPct}%をその旅人へ返す（1回につき最大HPの{MonsterBehavior.Number((decimal)ThornsReflectMaxHealthPct)}%まで、夢の圧と潜行で増える。{MonsterBehavior.Number((decimal)ThornsReflectInterval)}秒に1回まで。召喚獣の攻撃は返さない）。", $"Armor +{ThornedArmor}. Returns {ThornsReflectPct}% of damage taken from a traveler to that traveler (at most {MonsterBehavior.Number((decimal)ThornsReflectMaxHealthPct)}% of their max HP per hit, raised by dream pressure and delve; once per {MonsterBehavior.Number((decimal)ThornsReflectInterval)} s; not to summons).");
                case NightmareAffix.Ravenous: return Loc.T($"攻撃力+{RavenousAttackPct}%、与ダメージの{RavenousLeechPct}%回復。攻撃を避けて回復を防ぐ。", $"Attack +{RavenousAttackPct}%; heals for {RavenousLeechPct}% of damage dealt. Dodge to deny healing.");
                case NightmareAffix.Sundering: return Loc.T($"攻撃力+{SunderingAttackPct}%、命中で防御-{SunderArmor}を{MonsterBehavior.Number((decimal)SunderSeconds)}秒。追撃を避ける。", $"Attack +{SunderingAttackPct}%; hits reduce armor by {SunderArmor} for {MonsterBehavior.Number((decimal)SunderSeconds)}s. Avoid follow-up hits.");
                case NightmareAffix.Veiled: return Loc.T($"{MonsterBehavior.Number((decimal)MonsterBehavior.Range)}mより遠い攻撃の被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%。{MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m以内へ近づく。", $"Receives {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% less damage from beyond {MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m; approach within {MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m.");
                case NightmareAffix.Hollow: return Loc.T($"{MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m未満からの被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%。{MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m以上離れて攻撃。", $"Receives {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% less damage from within {MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m; strike from at least {MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m.");
                case NightmareAffix.Facing: return Loc.T($"正面{MonsterBehavior.Number((decimal)(Math.Acos(MonsterBehavior.FacingDot) * 360.0 / Math.PI))}度の被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%。側面や背後へ回る。", $"Receives {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% less damage in its frontal {MonsterBehavior.Number((decimal)(Math.Acos(MonsterBehavior.FacingDot) * 360.0 / Math.PI))}-degree cone; flank or attack from behind.");
                case NightmareAffix.Packbound: return Loc.T($"同区画{MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m以内に生存し活動中の味方がいると被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%。引き離すか仲間を倒す。", $"Receives {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% less damage with a living awake ally within {MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m in the same section; separate or clear allies.");
                case NightmareAffix.Beacon: return Loc.T($"{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒の予告後、一生に一度、同区画{MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m以内の最寄りの活動中の非ボス味方に最大HP{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}%の障壁を{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}秒。重複不可。先に倒すか引き離す。", $"After a {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s warning, once per life shields the nearest living awake nonboss ally within {MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m in the same section for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}% target max HP for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}s; no stacking. Kill support first or separate.");
                case NightmareAffix.Pulsing: return Loc.T($"最初の{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒は無防備。その後{MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}秒間被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%、{MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}秒間無防備を繰り返す。無防備の間に集中攻撃。", $"Initially open for {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s, then alternates {MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}s of {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% damage reduction and {MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}s open; burst during openings.");
                case NightmareAffix.Committed: return Loc.T($"詠唱中は被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%、攻撃発射後{MonsterBehavior.Number((decimal)MonsterBehavior.RecoverySeconds)}秒は被ダメージ+{MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}%が優先。回避後に反撃。", $"Receives {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% less damage while channeling; {MonsterBehavior.Number((decimal)MonsterBehavior.RecoverySeconds)}s after firing an attack, takes {MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}% more instead. Dodge then punish recovery.");
                case NightmareAffix.Skittish: return Loc.T($"未被弾時は移動+{MonsterBehavior.Number((decimal)MonsterBehavior.UnhitMovementPct)}%、ダメージを受けると{MonsterBehavior.Number((decimal)MonsterBehavior.HitSlowSeconds)}秒間移動-{MonsterBehavior.Number(-(decimal)MonsterBehavior.HitMovementPct)}%。一撃を当てて追う。", $"Movement +{MonsterBehavior.Number((decimal)MonsterBehavior.UnhitMovementPct)}% while unhit; damaging hits slow movement by {MonsterBehavior.Number(-(decimal)MonsterBehavior.HitMovementPct)}% for {MonsterBehavior.Number((decimal)MonsterBehavior.HitSlowSeconds)}s. Tag then pursue.");
                case NightmareAffix.Recuperating: return Loc.T($"{MonsterBehavior.Number((decimal)MonsterBehavior.HealDelaySeconds)}秒間無傷なら毎秒最大HP{MonsterBehavior.Number((decimal)MonsterBehavior.HealPctPerSecond)}%回復、一生の上限{MonsterBehavior.Number((decimal)MonsterBehavior.HealBudgetPct)}%。満タンでは消費しない。攻撃を続ける。", $"After {MonsterBehavior.Number((decimal)MonsterBehavior.HealDelaySeconds)}s without damage heals {MonsterBehavior.Number((decimal)MonsterBehavior.HealPctPerSecond)}% max HP per second, capped at {MonsterBehavior.Number((decimal)MonsterBehavior.HealBudgetPct)}% per life; no budget spent at full HP. Keep pressure.");
                case NightmareAffix.LastStand: return Loc.T($"HP{MonsterBehavior.Number((decimal)MonsterBehavior.LastStandHealthRatio * 100m)}%以下で{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒予告後、一生に一度、最大HP{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}%の障壁を{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}秒。予告中に倒すか殻を割る。", $"At {MonsterBehavior.Number((decimal)MonsterBehavior.LastStandHealthRatio * 100m)}% HP or less, warns for {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s then shields itself for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}% max HP for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}s, once per life. Finish during warning or break the shell.");
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

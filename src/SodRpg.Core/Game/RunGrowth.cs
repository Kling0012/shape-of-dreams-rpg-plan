using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>遠征中の行動のうち、RunGrowth のスタックを溜める契機。値は通信・保存に使うので並びを変えない。</summary>
    public enum RunGrowthTrigger
    {
        /// <summary>実際に失ったHP（障壁で吸収した分を除く）が最大HPの Threshold% に達するたび。</summary>
        DamageTakenMaxHpPct = 1,
        /// <summary>障壁が吸収したダメージが最大HPの Threshold% に達するたび。</summary>
        ShieldAbsorbedMaxHpPct = 2,
        /// <summary>パリィ（ミストの Se_R_Parry_End の生成）の成功 Threshold 回ごと。</summary>
        ParrySuccess = 3,
        /// <summary>会心の基本攻撃でとどめを刺す Threshold 回ごと。</summary>
        CritBasicAttackKill = 4,
        /// <summary>自分または味方への回復が実際に成立した Threshold 回ごと。</summary>
        HealSucceeded = 5,
        /// <summary>敵を撃破した Threshold 回ごと。</summary>
        EnemyKill = 6,
        /// <summary>自分の本体召喚ユニットが敵に命中した Threshold 回ごと。</summary>
        SummonHit = 7,
        /// <summary>自分の通常攻撃が敵に命中した Threshold 回ごと。</summary>
        BasicAttackHit = 8,
        /// <summary>自分の記憶の本体ダメージが敵に命中した Threshold 回ごと。</summary>
        MemoryHit = 9,
    }

    /// <summary>1スタックあたりの能力値。AmountMilli は表示単位の1/1000（0.5% = 500、攻撃力+0.5 = 500）。</summary>
    public sealed class RunGrowthEffect
    {
        public RunGrowthEffect(Stat stat, int amountMilli)
        {
            Stat = stat;
            AmountMilli = amountMilli;
        }
        public Stat Stat { get; }
        public int AmountMilli { get; }
    }

    /// <summary>
    /// 遠征を通して成長する仕組み（RunGrowth）の定義。入口の重要な星が持つ。
    /// スタックはホストが旅人ごとに HeroRuntime の外で持ち、遠征の開始で0に戻る。
    /// </summary>
    public sealed class RunGrowthDef
    {
        public const int MaxCap = 500;
        public const int MaxThreshold = 1000;
        public const int MaxEffects = 4;
        public const int MaxAmountMilli = 1000000;

        public RunGrowthDef(RunGrowthTrigger trigger, int threshold, int cap, IEnumerable<RunGrowthEffect> effects)
        {
            Trigger = trigger;
            Threshold = threshold;
            Cap = cap;
            Effects = Array.AsReadOnly((effects ?? throw new ArgumentNullException(nameof(effects))).ToArray());
            Validate();
        }

        public RunGrowthTrigger Trigger { get; }
        /// <summary>HP系の契機は最大HPに対する%（1〜100）。出来事の契機は出来事の回数。</summary>
        public int Threshold { get; }
        /// <summary>0なら蓄積の上限なし。正の値は蓄積の上限。</summary>
        public int Cap { get; }
        public IReadOnlyList<RunGrowthEffect> Effects { get; }

        public static bool IsHealthTrigger(RunGrowthTrigger trigger)
            => trigger == RunGrowthTrigger.DamageTakenMaxHpPct || trigger == RunGrowthTrigger.ShieldAbsorbedMaxHpPct;

        /// <summary>ホストが旅人ごとに適用できる能力値。エッセンス枠・HPの消費軽減・整数の位置ずらしは対象外。</summary>
        public static bool SupportedStat(Stat stat)
        {
            switch (stat)
            {
                case Stat.EssenceSlotIdentity: case Stat.EssenceSlotMovement: case Stat.SacrificeReduction:
                case Stat.FourthAttackShift:
                    return false;
                default: return Enum.IsDefined(typeof(Stat), stat);
            }
        }

        private void Validate()
        {
            if (!Enum.IsDefined(typeof(RunGrowthTrigger), Trigger)) throw new ArgumentException("Unknown RunGrowth trigger.");
            if (Threshold < 1 || Threshold > (IsHealthTrigger(Trigger) ? 100 : MaxThreshold)) throw new ArgumentException("RunGrowth threshold is out of range.");
            if (Cap < 0 || Cap > MaxCap) throw new ArgumentException("RunGrowth cap is out of range.");
            if (Effects.Count < 1 || Effects.Count > MaxEffects) throw new ArgumentException("RunGrowth needs 1 to 4 stat effects.");
            var seen = new HashSet<Stat>();
            foreach (var effect in Effects)
            {
                if (effect == null || !SupportedStat(effect.Stat) || !seen.Add(effect.Stat)
                    || effect.AmountMilli < 1 || effect.AmountMilli > MaxAmountMilli)
                    throw new ArgumentException("Invalid RunGrowth stat effect.");
            }
        }
    }

    /// <summary>
    /// 他の星が RunGrowth に足す修飾（上限 +N／1スタックの効果 +X%／溜まる速さ2倍）。
    /// TargetStarId が null ならその旅人のすべての RunGrowth に、あればその星だけに掛かる。
    /// </summary>
    public sealed class RunGrowthModifierDef
    {
        public const int MaxCapBonus = 200;
        public const int MaxEffectPercent = 500;

        public RunGrowthModifierDef(string targetStarId, int capBonus, int effectPercent, bool doubleGain)
        {
            TargetStarId = targetStarId;
            CapBonus = capBonus;
            EffectPercent = effectPercent;
            DoubleGain = doubleGain;
            if (targetStarId != null && !Gimmicks.ValidStarId(targetStarId)) throw new ArgumentException("Invalid RunGrowth modifier target.");
            if (capBonus < 0 || capBonus > MaxCapBonus || effectPercent < 0 || effectPercent > MaxEffectPercent)
                throw new ArgumentException("RunGrowth modifier value is out of range.");
            if (capBonus == 0 && effectPercent == 0 && !doubleGain) throw new ArgumentException("A RunGrowth modifier must change something.");
        }

        public string TargetStarId { get; }
        public int CapBonus { get; }
        public int EffectPercent { get; }
        public bool DoubleGain { get; }
    }

    /// <summary>Build に組み上がった RunGrowth 1本分（修飾を反映済み）。</summary>
    public sealed class RunGrowthEntry
    {
        public string StarId { get; set; }
        public string[] ContributorIds { get; set; } = Array.Empty<string>();
        public RunGrowthTrigger Trigger { get; set; }
        public int Threshold { get; set; }
        /// <summary>0なら蓄積の上限なし。</summary>
        public int Cap { get; set; }
        /// <summary>1スタックの効果への上乗せ（%）。</summary>
        public int EffectPercent { get; set; }
        /// <summary>1回の閾値到達で増えるスタック数（1か2）。</summary>
        public int GainMultiplier { get; set; } = 1;
        public IReadOnlyList<RunGrowthEffect> Effects { get; set; } = Array.Empty<RunGrowthEffect>();

        public RunGrowthEntry Copy() => new RunGrowthEntry
        {
            StarId = StarId, ContributorIds = ContributorIds.ToArray(), Trigger = Trigger, Threshold = Threshold, Cap = Cap,
            EffectPercent = EffectPercent, GainMultiplier = GainMultiplier, Effects = Effects,
        };
    }

    /// <summary>Build への組み立て、通信形式、説明文。</summary>
    public static class RunGrowth
    {
        public const int MaxEntries = 16;

        private static readonly HashSet<string> RemovedStars = CreateRemovedStars();

        private static HashSet<string> CreateRemovedStars()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string hero in new[] { "aurena", "bismuth", "husk", "lacerta", "mist", "nachia", "yubar" })
                foreach (string suffix in new[] { "g1", "m1", "m2", "choice" }) ids.Add(hero + ".run." + suffix);
            return ids;
        }

        public static bool IsRemovedStar(string id) => id != null && RemovedStars.Contains(id);

        /// <summary>取得した星から RunGrowth と修飾を集め、Build.RunGrowths を作る。</summary>
        internal static void Compose(Build build, IReadOnlyList<KeyValuePair<TalentDef, int>> selected)
        {
            build.RunGrowths.Clear();
            var growths = new List<RunGrowthEntry>();
            foreach (var row in selected)
            {
                var def = row.Key.RunGrowth;
                if (def == null) continue;
                growths.Add(new RunGrowthEntry
                {
                    StarId = row.Key.Id, ContributorIds = new[] { row.Key.Id }, Trigger = def.Trigger, Threshold = def.Threshold,
                    Cap = def.Cap, Effects = def.Effects,
                });
                build.Dependencies?.Touch("W:growth", row.Key.Id);
            }
            if (growths.Count == 0) return;
            foreach (var row in selected)
            {
                var mod = row.Key.RunGrowthModifier;
                if (mod == null) continue;
                build.Dependencies?.Touch("W:growth", row.Key.Id);
                foreach (var entry in growths)
                {
                    if (mod.TargetStarId != null && mod.TargetStarId != entry.StarId) continue;
                    if (entry.Cap > 0) entry.Cap = Math.Min(RunGrowthDef.MaxCap, checked(entry.Cap + mod.CapBonus * row.Value));
                    entry.EffectPercent = Math.Min(RunGrowthModifierDef.MaxEffectPercent, checked(entry.EffectPercent + mod.EffectPercent * row.Value));
                    if (mod.DoubleGain) entry.GainMultiplier = 2;
                    var ids = new List<string>(entry.ContributorIds) { row.Key.Id };
                    entry.ContributorIds = ids.ToArray();
                }
            }
            growths.Sort((a, b) => string.CompareOrdinal(a.StarId, b.StarId));
            build.RunGrowths.AddRange(growths);
        }

        // ───── 通信形式（"w:" 節）─────
        // 1本 = StarId:trigger:threshold:cap:effectPercent:gain:stat=milli+stat=milli  （空のときは節ごと省く）

        internal static void Append(StringBuilder sb, Build build)
        {
            if (build.RunGrowths.Count == 0) return;
            if (build.RunGrowths.Count > MaxEntries) throw new InvalidOperationException("Too many RunGrowth entries.");
            sb.Append(";w:");
            bool first = true;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in build.RunGrowths)
            {
                ValidateEntry(entry);
                if (!ids.Add(entry.StarId)) throw new InvalidOperationException("Duplicate RunGrowth entry.");
                if (!first) sb.Append(',');
                first = false;
                sb.Append(entry.StarId).Append(':').Append(((int)entry.Trigger).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Threshold.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Cap.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.EffectPercent.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.GainMultiplier.ToString(CultureInfo.InvariantCulture)).Append(':');
                for (int i = 0; i < entry.Effects.Count; i++)
                {
                    if (i > 0) sb.Append('+');
                    sb.Append(((int)entry.Effects[i].Stat).ToString(CultureInfo.InvariantCulture)).Append('=')
                        .Append(entry.Effects[i].AmountMilli.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        internal static bool Read(string encoded, Build build)
        {
            int colon = encoded.IndexOf(':');
            if (IsRemovedStar(colon < 0 ? encoded : encoded.Substring(0, colon))) return true;
            string[] f = encoded.Split(':');
            if (f.Length != 7 || build.RunGrowths.Count >= MaxEntries) return false;
            var effects = new List<RunGrowthEffect>();
            foreach (string part in f[6].Split('+'))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2) return false;
                int stat = ParseInt(kv[0]);
                if (!Enum.IsDefined(typeof(Stat), stat)) return false;
                effects.Add(new RunGrowthEffect((Stat)stat, ParseInt(kv[1])));
            }
            var entry = new RunGrowthEntry
            {
                StarId = f[0], Trigger = (RunGrowthTrigger)ParseInt(f[1]), Threshold = ParseInt(f[2]), Cap = ParseInt(f[3]),
                EffectPercent = ParseInt(f[4]), GainMultiplier = ParseInt(f[5]), Effects = effects.AsReadOnly(),
                ContributorIds = new[] { f[0] },
            };
            if (!IsValidEntry(entry)) return false;
            foreach (var other in build.RunGrowths) if (other.StarId == entry.StarId) return false;
            build.RunGrowths.Add(entry);
            return true;
        }

        internal static void Validate(Build build)
        {
            if (build.RunGrowths.Count > MaxEntries) throw new InvalidOperationException("Too many RunGrowth entries.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in build.RunGrowths)
            {
                ValidateEntry(entry);
                if (!ids.Add(entry.StarId)) throw new InvalidOperationException("Duplicate RunGrowth entry.");
            }
        }

        private static void ValidateEntry(RunGrowthEntry entry)
        {
            if (!IsValidEntry(entry)) throw new InvalidOperationException("Invalid RunGrowth entry.");
        }

        private static bool IsValidEntry(RunGrowthEntry e)
        {
            if (e == null || !Gimmicks.ValidStarId(e.StarId) || !Enum.IsDefined(typeof(RunGrowthTrigger), e.Trigger)
                || e.Threshold < 1 || e.Threshold > (RunGrowthDef.IsHealthTrigger(e.Trigger) ? 100 : RunGrowthDef.MaxThreshold)
                || e.Cap < 0 || e.Cap > RunGrowthDef.MaxCap || e.EffectPercent < 0 || e.EffectPercent > RunGrowthModifierDef.MaxEffectPercent
                || e.GainMultiplier < 1 || e.GainMultiplier > 2 || e.Effects == null
                || e.Effects.Count < 1 || e.Effects.Count > RunGrowthDef.MaxEffects) return false;
            var seen = new HashSet<Stat>();
            foreach (var effect in e.Effects)
                if (effect == null || !RunGrowthDef.SupportedStat(effect.Stat) || !seen.Add(effect.Stat)
                    || effect.AmountMilli < 1 || effect.AmountMilli > RunGrowthDef.MaxAmountMilli) return false;
            return true;
        }

        private static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

        /// <summary>この結果は同じ入力なら同じ並びで同じ値。指紋・比較用。</summary>
        public static string Signature(RunGrowthDef def)
        {
            if (def == null) return "";
            return "g" + (int)def.Trigger + ":" + def.Threshold + ":" + def.Cap + ":"
                + string.Join("+", def.Effects.Select(x => (int)x.Stat + "=" + x.AmountMilli));
        }

        public static string Signature(RunGrowthModifierDef mod)
            => mod == null ? "" : "m" + mod.TargetStarId + ":" + mod.CapBonus + ":" + mod.EffectPercent + ":" + (mod.DoubleGain ? 1 : 0);

        // ───── 数値 ─────

        /// <summary>実際に適用・表示する蓄積数。上限なしでも int の範囲に収まる。</summary>
        public static int EffectiveStacks(RunGrowthEntry entry, int stacks)
            => Math.Max(0, entry.Cap == 0 ? stacks : Math.Min(stacks, entry.Cap));

        /// <summary>stacks（上限を超えない）に応じた能力値の合計。表示単位（% や固定値）で返す。</summary>
        public static double StatTotal(RunGrowthEntry entry, RunGrowthEffect effect, int stacks)
        {
            int effective = EffectiveStacks(entry, stacks);
            return effective * (effect.AmountMilli / 1000d) * (100 + entry.EffectPercent) / 100d;
        }

        /// <summary>HP系の契機で閾値に数える量（最大HPに対する%）。最大HPが不正なら0。</summary>
        public static double HealthPercent(float amount, float maxHealth)
            => maxHealth > 0f && amount > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount) ? amount / (double)maxHealth * 100d : 0d;

        // ───── 説明文 ─────

        public static string TriggerText(RunGrowthTrigger trigger, int threshold)
        {
            // 中国語の第3引数は補間を伴うため、中国語表示のときだけ組み立てる（HUD 経由で頻繁に呼ばれる）。
            bool zh = Loc.Language == LocLanguage.Chinese;
            switch (trigger)
            {
                case RunGrowthTrigger.DamageTakenMaxHpPct:
                    return Loc.T($"実際に失ったHP（障壁が吸収した分を除く）が最大HPの{threshold}%に達するたび", $"each time actual health lost (excluding shield absorption) totals {threshold}% of max health", zh ? $"每当实际损失的生命值（不含护盾吸收量）累计达到最大生命值的{threshold}%时" : null);
                case RunGrowthTrigger.ShieldAbsorbedMaxHpPct:
                    return Loc.T($"障壁が吸収した量が最大HPの{threshold}%に達するたび", $"each time damage absorbed by shields totals {threshold}% of max health", zh ? $"每当护盾吸收的伤害累计达到最大生命值的{threshold}%时" : null);
                case RunGrowthTrigger.ParrySuccess:
                    return threshold == 1 ? Loc.T("パリィに成功するたび", "each time you parry successfully", "每次成功招架时")
                        : Loc.T($"パリィに{threshold}回成功するたび", $"every {threshold} successful parries", zh ? $"每成功招架{threshold}次时" : null);
                case RunGrowthTrigger.HealSucceeded:
                    return (threshold == 1 ? Loc.T("自分の回復で自分または味方のHPが実際に回復するたび", "each time your healing restores health to yourself or an ally", "每次你的治疗实际恢复自身或队友的生命值时")
                        : Loc.T($"自分の回復で自分または味方のHPが実際に{threshold}回回復するたび", $"every {threshold} successful heals on yourself or allies", zh ? $"每对自身或队友完成{threshold}次有效治疗时" : null))
                        + Loc.T($"（味方への有効回復・障壁は{StarRankBalance.SupportAllyGrowthMultiplier}回分、自己回復は1回分。溢れ由来の障壁は除く）",
                            $" (effective ally healing/shields count {StarRankBalance.SupportAllyGrowthMultiplier} times; self-heals count once; overheal shields excluded)", zh ? $"（对队友的有效治疗和护盾计为{StarRankBalance.SupportAllyGrowthMultiplier}次，自我治疗计为1次；不含过量治疗生成的护盾）" : null);
                case RunGrowthTrigger.EnemyKill:
                    return threshold == 1 ? Loc.T("敵を撃破するたび", "each time you kill an enemy", "每次击杀敌人时")
                        : Loc.T($"敵を{threshold}体撃破するたび", $"every {threshold} enemies you kill", zh ? $"每击杀{threshold}名敌人时" : null);
                case RunGrowthTrigger.SummonHit:
                    return threshold == 1 ? Loc.T("自分の召喚獣の攻撃が敵に命中するたび", "each time your summon hits an enemy", "每次你的召唤物命中敌人时")
                        : Loc.T($"自分の召喚獣の攻撃が敵に{threshold}回命中するたび", $"every {threshold} hits by your summons on enemies", zh ? $"每当你的召唤物累计命中敌人{threshold}次时" : null);
                case RunGrowthTrigger.BasicAttackHit:
                    return threshold == 1 ? Loc.T("自分の通常攻撃が敵に命中するたび", "each time your basic attack hits an enemy", "每次你的普通攻击命中敌人时")
                        : Loc.T($"自分の通常攻撃が敵に{threshold}回命中するたび", $"every {threshold} basic attack hits on enemies", zh ? $"每当你的普通攻击累计命中敌人{threshold}次时" : null);
                case RunGrowthTrigger.MemoryHit:
                    return threshold == 1 ? Loc.T("自分の記憶が敵に命中するたび", "each time your memory hits an enemy", "每次你的记忆命中敌人时")
                        : Loc.T($"自分の記憶が敵に{threshold}回命中するたび", $"every {threshold} hits by your memories on enemies", zh ? $"每当你的记忆累计命中敌人{threshold}次时" : null);
                default:
                    return threshold == 1 ? Loc.T("会心の基本攻撃でとどめを刺すたび", "each time a critical basic attack lands the finishing blow", "每次以暴击普通攻击完成击杀时")
                        : Loc.T($"会心の基本攻撃でとどめを{threshold}回刺すたび", $"every {threshold} finishing blows by critical basic attacks", zh ? $"每以暴击普通攻击完成{threshold}次击杀时" : null);
            }
        }

        public static string StatText(Stat stat, double value)
        {
            string number = value.ToString("0.##", CultureInfo.InvariantCulture);
            // 中国語の第3引数は補間を伴うため、中国語表示のときだけ組み立てる（HUD 経由で頻繁に呼ばれる）。
            bool zh = Loc.Language == LocLanguage.Chinese;
            switch (stat)
            {
                case Stat.AttackFlat: return Loc.T($"攻撃力 +{number}", $"Attack Damage +{number}", zh ? $"攻击力 +{number}" : null);
                case Stat.PowerFlat: return Loc.T($"魔力 +{number}", $"Ability Power +{number}", zh ? $"法术强度 +{number}" : null);
                case Stat.Armor: return Loc.T($"防御 +{number}", $"Armor +{number}", zh ? $"护甲 +{number}" : null);
                case Stat.MaxHealthFlat: return Loc.T($"最大HP +{number}", $"Max Health +{number}", zh ? $"最大生命值 +{number}" : null);
                case Stat.MaxHealthPct: return Loc.T($"最大HP +{number}%", $"Max Health +{number}%", zh ? $"最大生命值 +{number}%" : null);
                case Stat.AttackPct: return Loc.T($"攻撃力 +{number}%", $"Attack Damage +{number}%", zh ? $"攻击力 +{number}%" : null);
                case Stat.PowerPct: return Loc.T($"魔力 +{number}%", $"Ability Power +{number}%", zh ? $"法术强度 +{number}%" : null);
                case Stat.AttackSpeedPct: return Loc.T($"攻撃速度 +{number}%", $"Attack Speed +{number}%", zh ? $"攻击速度 +{number}%" : null);
                case Stat.CritChancePct: return Loc.T($"会心率 +{number}%", $"Crit Chance +{number}%", zh ? $"暴击率 +{number}%" : null);
                case Stat.CritDamagePct: return Loc.T($"会心ダメージ +{number}%", $"Crit Damage +{number}%", zh ? $"暴击伤害 +{number}%" : null);
                case Stat.ShieldPower: return Loc.T($"シールドの強さ +{number}%", $"Shield Power +{number}%", zh ? $"护盾强度 +{number}%" : null);
                case Stat.HealPower: return Loc.T($"回復の強さ +{number}%", $"Heal Power +{number}%", zh ? $"治疗强度 +{number}%" : null);
                case Stat.HealingReceivedPct: return Loc.T($"受ける回復 +{number}%", $"Healing Received +{number}%", zh ? $"受到的治疗 +{number}%" : null);
                case Stat.SummonPower: return Loc.T($"召喚獣のダメージ +{number}%", $"Summon Damage +{number}%", zh ? $"召唤物伤害 +{number}%" : null);
                case Stat.HealthRegen: return Loc.T($"HP自然回復 +{number}", $"Health Regen +{number}", zh ? $"生命恢复 +{number}" : null);
                case Stat.Haste: return Loc.T($"クールダウン短縮 +{number}", $"Ability Haste +{number}", zh ? $"技能急速 +{number}" : null);
                case Stat.MoveSpeedPct: return Loc.T($"移動速度 +{number}%", $"Move Speed +{number}%", zh ? $"移动速度 +{number}%" : null);
                case Stat.Tenacity: return Loc.T($"不屈 +{number}", $"Tenacity +{number}", zh ? $"韧性 +{number}" : null);
                default: return Content.FormatStat(stat, (int)Math.Round(value));
            }
        }

        /// <summary>日本語の文章で使う能力値の名前（「攻撃力」「会心ダメージ」など）。</summary>
        private static string StatNameJa(Stat stat)
        {
            switch (stat)
            {
                case Stat.AttackFlat: case Stat.AttackPct: return "攻撃力";
                case Stat.PowerFlat: case Stat.PowerPct: return "魔力";
                case Stat.Armor: return "防御";
                case Stat.MaxHealthFlat: case Stat.MaxHealthPct: return "最大HP";
                case Stat.AttackSpeedPct: return "攻撃速度";
                case Stat.CritChancePct: return "会心率";
                case Stat.CritDamagePct: return "会心ダメージ";
                case Stat.ShieldPower: return "シールドの強さ";
                case Stat.HealPower: return "回復の強さ";
                case Stat.HealingReceivedPct: return "受ける回復量";
                case Stat.SummonPower: return "召喚獣のダメージ";
                case Stat.HealthRegen: return "HP自然回復";
                case Stat.Haste: return "クールダウン短縮";
                case Stat.MoveSpeedPct: return "移動速度";
                case Stat.Tenacity: return "不屈";
                default: return null;
            }
        }

        private static bool IsPercentStat(Stat stat)
        {
            switch (stat)
            {
                case Stat.MaxHealthPct: case Stat.AttackPct: case Stat.PowerPct: case Stat.AttackSpeedPct: case Stat.CritChancePct:
                case Stat.CritDamagePct: case Stat.ShieldPower: case Stat.HealPower: case Stat.HealingReceivedPct: case Stat.SummonPower: case Stat.MoveSpeedPct:
                    return true;
                default: return false;
            }
        }

        /// <summary>「攻撃力が5」「会心ダメージが12%」のような、能力値と量の組。未対応の能力値は従来の表記のまま。</summary>
        private static string StatAmountJa(Stat stat, double value)
        {
            string name = StatNameJa(stat);
            if (name == null) return StatText(stat, value);
            return name + "が" + value.ToString("0.##", CultureInfo.InvariantCulture) + (IsPercentStat(stat) ? "%" : "");
        }

        /// <summary>「攻撃力は5から6に」のような、変化前後の表記（修飾の星の説明用）。</summary>
        private static string StatChangeJa(Stat stat, double before, double after)
        {
            string name = StatNameJa(stat);
            string unit = IsPercentStat(stat) ? "%" : "";
            if (name == null) return StatText(stat, before) + "から" + StatText(stat, after) + "に";
            return name + "は" + before.ToString("0.##", CultureInfo.InvariantCulture) + unit + "から" + after.ToString("0.##", CultureInfo.InvariantCulture) + unit + "に";
        }

        /// <summary>「攻撃力が5上がり、会心ダメージが12%上がる」。</summary>
        private static string RiseJa(IList<string> amounts)
            => string.Join("、", amounts.Select((amount, i) => amount + (i == amounts.Count - 1 ? "上がる" : "上がり")));

        private static string PerStack(RunGrowthEntry entry)
            => string.Join(Loc.T("、", ", ", "、"), entry.Effects.Select(x => StatText(x.Stat, x.AmountMilli / 1000d * (100 + entry.EffectPercent) / 100d)));

        /// <summary>星図・一覧に出す説明（1スタックあたりと上限）。</summary>
        public static string Describe(RunGrowthEntry entry)
        {
            int gain = Math.Max(1, entry.GainMultiplier);
            string rise = RiseJa(entry.Effects.Select(x => StatAmountJa(x.Stat, x.AmountMilli / 1000d * (100 + entry.EffectPercent) / 100d)).ToList());
            string capJa = entry.Cap == 0 ? "蓄積の上限はない。" : $"鍛錬は最大{entry.Cap}つまで溜まり、";
            string capEn = entry.Cap == 0 ? "no stack cap" : $"up to {entry.Cap} stacks";
            // 中国語の文は中国語表示のときだけ組み立てる（Loc.T の引数は言語に関係なく評価されるため）。
            return Loc.T($"{TriggerText(entry.Trigger, entry.Threshold)}、鍛錬が{gain}つ溜まる。鍛錬1つにつき、{rise}。{capJa}遠征の間は死亡やゾーン移動でも失われず、遠征の開始で0に戻る。",
                $"{PerStack(entry)} per training stack: gain {gain} stack{(gain == 1 ? "" : "s")} {TriggerText(entry.Trigger, entry.Threshold)} ({capEn}; kept through death and zone changes for the whole expedition, reset when a new expedition begins)",
                Loc.Language == LocLanguage.Chinese ? $"每层锻炼提供{PerStack(entry)}；获得{gain}{(gain == 1 ? "层" : "层")}锻炼的条件为{TriggerText(entry.Trigger, entry.Threshold)}（{(entry.Cap == 0 ? "层数无上限" : $"最多{entry.Cap}层")}；整次远征中死亡或切换区域均保留，开始新远征时清零）" : null);
        }

        public static string Describe(RunGrowthDef def)
        {
            var entry = new RunGrowthEntry { Trigger = def.Trigger, Threshold = def.Threshold, Cap = def.Cap, Effects = def.Effects };
            return Describe(entry);
        }

        public static string Describe(RunGrowthModifierDef mod, string heroKey = null)
        {
            var targets = new List<TalentDef>();
            if (heroKey != null)
                targets.AddRange(HeroSigils.TreeFor(heroKey).Where(t => t.RunGrowth != null && (mod.TargetStarId == null || t.Id == mod.TargetStarId)));
            else if (mod.TargetStarId != null && Content.TryGetTalent(mod.TargetStarId, out var target))
                targets.Add(target);
            return Loc.Language switch
            {
                LocLanguage.Japanese => DescribeModifierJa(mod, targets),
                LocLanguage.Chinese => DescribeModifierZh(mod, targets),
                _ => DescribeModifierEn(mod, targets),
            };
        }

        private static string DescribeModifierJa(RunGrowthModifierDef mod, List<TalentDef> targets)
        {
            var sb = new StringBuilder();
            bool hasCappedTarget = targets.Count == 0 || targets.Any(t => t.RunGrowth.Cap > 0);
            if (mod.CapBonus > 0 && hasCappedTarget) sb.Append($"鍛錬の蓄積上限が{mod.CapBonus}増える。");
            if (mod.EffectPercent > 0) sb.Append(hasCappedTarget
                ? $"鍛錬1つごとの能力値の上昇量が{mod.EffectPercent}%増える。"
                : $"鍛錬の効果が{mod.EffectPercent}%増える。");
            if (mod.DoubleGain) sb.Append("鍛錬の獲得数が100%増え、条件を満たすたびに2つ溜まる。");
            if (targets.Count == 0) return sb.Append("対象はこの旅人のすべての遠征の鍛錬。").ToString();
            foreach (var talent in targets)
            {
                var growth = talent.RunGrowth;
                sb.Append($"対象は星『{talent.Name}』で、{TriggerText(growth.Trigger, growth.Threshold)}に溜まる鍛錬が強化される。");
                if (mod.EffectPercent == 0)
                    sb.Append("鍛錬1つにつき、" + RiseJa(growth.Effects.Select(effect => StatAmountJa(effect.Stat, effect.AmountMilli / 1000d)).ToList()) + "。");
                else
                    sb.Append("鍛錬1つあたりの上昇量が、" + string.Join("、", growth.Effects.Select(effect =>
                    {
                        double amount = effect.AmountMilli / 1000d;
                        return StatChangeJa(effect.Stat, amount, amount * (100 + mod.EffectPercent) / 100d);
                    })) + "変わる。");
                if (growth.Cap == 0) sb.Append("蓄積の上限はない。");
                else if (mod.CapBonus > 0) sb.Append($"鍛錬の蓄積上限は{growth.Cap}から{Math.Min(RunGrowthDef.MaxCap, growth.Cap + mod.CapBonus)}になる。");
                if (mod.EffectPercent > 0 || mod.CapBonus > 0) sb.Append("これはこの星だけを基本値に適用した場合の値である。");
            }
            return sb.ToString();
        }

        private static string DescribeModifierEn(RunGrowthModifierDef mod, List<TalentDef> targets)
        {
            var parts = new List<string>();
            if (mod.CapBonus > 0 && (targets.Count == 0 || targets.Any(t => t.RunGrowth.Cap > 0)))
                parts.Add($"training stack cap +{mod.CapBonus}");
            if (mod.EffectPercent > 0) parts.Add($"stat gain per training stack +{mod.EffectPercent}%");
            if (mod.DoubleGain) parts.Add("training stack gain +100% (2 stacks per completed condition)");
            string TargetText(TalentDef talent)
            {
                var growth = talent.RunGrowth;
                string gains = string.Join(", ", growth.Effects.Select(effect =>
                {
                    double amount = effect.AmountMilli / 1000d;
                    string before = StatText(effect.Stat, amount);
                    return mod.EffectPercent == 0 ? before
                        : before + " → " + StatText(effect.Stat, amount * (100 + mod.EffectPercent) / 100d);
                }));
                string detail = ". Per training stack: " + gains;
                if (growth.Cap == 0) detail += ". no stack cap";
                else if (mod.CapBonus > 0)
                    detail += $". Stack cap: {growth.Cap} → {Math.Min(RunGrowthDef.MaxCap, growth.Cap + mod.CapBonus)}";
                if (mod.EffectPercent > 0 || mod.CapBonus > 0)
                    detail += " (with only this star applied to the base values)";
                return "star “" + talent.Name + "”: "
                    + TriggerText(growth.Trigger, growth.Threshold) + detail;
            }
            string scope = targets.Count > 0
                ? string.Join("; ", targets.Select(TargetText))
                : "all expedition training for this traveler";
            return string.Join(", ", parts) + ". Applies to " + scope;
        }

        /// <summary>修飾の星の中国語の説明（#992）。日本語版（DescribeModifierJa）の組み立てにならう。用語は #960（鍛錬→锻炼）に合わせる。</summary>
        private static string DescribeModifierZh(RunGrowthModifierDef mod, List<TalentDef> targets)
        {
            var sb = new StringBuilder();
            bool hasCappedTarget = targets.Count == 0 || targets.Any(t => t.RunGrowth.Cap > 0);
            if (mod.CapBonus > 0 && hasCappedTarget) sb.Append($"锻炼层数上限增加{mod.CapBonus}。");
            if (mod.EffectPercent > 0) sb.Append(hasCappedTarget
                ? $"每层锻炼提供的属性值提升{mod.EffectPercent}%。"
                : $"锻炼的效果提升{mod.EffectPercent}%。");
            if (mod.DoubleGain) sb.Append("锻炼的获取数量增加100%，每次满足条件积累2层。");
            if (targets.Count == 0) return sb.Append("对象为该旅人所有远征的锻炼。").ToString();
            foreach (var talent in targets)
            {
                var growth = talent.RunGrowth;
                sb.Append($"对象为星“{talent.Name}”中随{TriggerText(growth.Trigger, growth.Threshold)}积累的锻炼。");
                if (mod.EffectPercent == 0)
                    sb.Append("每层锻炼：" + RiseZh(growth.Effects.Select(effect => StatText(effect.Stat, effect.AmountMilli / 1000d)).ToList()) + "。");
                else
                    sb.Append("每层锻炼的提升量变为：" + string.Join("、", growth.Effects.Select(effect =>
                    {
                        double amount = effect.AmountMilli / 1000d;
                        return StatText(effect.Stat, amount) + " → " + StatText(effect.Stat, amount * (100 + mod.EffectPercent) / 100d);
                    })) + "。");
                if (growth.Cap == 0) sb.Append("层数无上限。");
                else if (mod.CapBonus > 0) sb.Append($"锻炼层数上限从{growth.Cap}变为{Math.Min(RunGrowthDef.MaxCap, growth.Cap + mod.CapBonus)}。");
                if (mod.EffectPercent > 0 || mod.CapBonus > 0) sb.Append("这是仅将该星应用于基础数值时的值。");
            }
            return sb.ToString();
        }

        /// <summary>「攻击力 +5、暴击伤害 +12%提升」のような、上昇量の並び（中国語）。</summary>
        private static string RiseZh(IList<string> amounts) => string.Join("、", amounts) + "提升";

        /// <summary>HUDの小さな1行。</summary>
        public static string HudLine(RunGrowthEntry entry, int stacks)
        {
            int shown = EffectiveStacks(entry, stacks);
            // 中国語の文は中国語表示のときだけ組み立てる（HUD から頻繁に呼ばれるため、確保を避ける）。
            bool zh = Loc.Language == LocLanguage.Chinese;
            string count = entry.Cap == 0
                ? Loc.T($"鍛錬 {shown}（蓄積の上限はない）", $"Training {shown} (no stack cap)", zh ? $"锻炼 {shown}（层数无上限）" : null)
                : Loc.T($"鍛錬 {shown}/{entry.Cap}", $"Training {shown}/{entry.Cap}", zh ? $"锻炼 {shown}/{entry.Cap}" : null);
            return count + (shown > 0
                ? " <color=#c4c4dc>(" + string.Join(" ", entry.Effects.Select(x => StatText(x.Stat, StatTotal(entry, x, shown)))) + ")</color>" : "");
        }

        /// <summary>取得した効果の一覧に出す、現在のスタックの1行。</summary>
        public static string SummaryLine(RunGrowthEntry entry, int stacks)
        {
            int shown = EffectiveStacks(entry, stacks);
            // 中国語の文は中国語表示のときだけ組み立てる（Loc.T の引数は言語に関係なく評価されるため）。
            bool zh = Loc.Language == LocLanguage.Chinese;
            string count = entry.Cap == 0
                ? Loc.T($"現在 {shown}つ（蓄積の上限はない）", $"Now {shown} stacks (no stack cap)", zh ? $"当前{shown}层（层数无上限）" : null)
                : Loc.T($"現在 {shown}/{entry.Cap}つ", $"Now {shown}/{entry.Cap} stacks", zh ? $"当前{shown}/{entry.Cap}层" : null);
            return Describe(entry) + "\n" + count
                + (shown > 0 ? "（" + string.Join(Loc.T("、", ", ", "、"), entry.Effects.Select(x => StatText(x.Stat, StatTotal(entry, x, shown)))) + "）" : "");
        }
    }

    /// <summary>1人の1つの RunGrowth の保存形。</summary>
    public sealed class RunGrowthSave
    {
        public string Owner { get; set; }
        public string GrowthId { get; set; }
        public int Stacks { get; set; }
        public long ProgressMilli { get; set; }
    }

    /// <summary>
    /// ホストが持つ RunGrowth のスタック（旅人の持ち主ごと・RunGrowth の星ごと）。HeroRuntime の外にあり、
    /// 死亡・ゾーン移動・旅人の再生成・装備の付け直しでは変わらず、遠征の開始（RunId の変化）で0に戻る。
    /// </summary>
    public sealed class RunGrowthLedger
    {
        private sealed class Slot
        {
            public int Stacks;
            public long ProgressMilli;
        }

        private sealed class OwnerState
        {
            internal readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
            internal int Version;
        }

        private readonly Dictionary<string, OwnerState> _owners = new Dictionary<string, OwnerState>(StringComparer.Ordinal);

        public string RunId { get; private set; }
        /// <summary>スタックが変わるたびに増える。表示の作り直しの目印。</summary>
        public int Version { get; private set; }
        /// <summary>遠征の変更・リセット・復元のたびに増える。持ち主のスタックが同じでも再適用する。</summary>
        public int Generation { get; private set; }

        public int OwnerVersion(string owner)
            => owner != null && _owners.TryGetValue(owner, out var state) ? state.Version : 0;

        /// <summary>遠征の識別子が前と違えば全部を0に戻す。戻したら true。</summary>
        public bool EnsureRun(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return false;
            if (runId == RunId) return false;
            bool had = _owners.Count > 0;
            _owners.Clear();
            Generation++;
            RunId = runId;
            if (had) Version++;
            return true;
        }

        /// <summary>遠征の終わりなど、識別子に関わらず0へ戻す。</summary>
        public void Reset()
        {
            if (_owners.Count > 0) Version++;
            _owners.Clear();
            Generation++;
            RunId = null;
        }

        public int Stacks(string owner, string growthId)
            => owner != null && growthId != null && _owners.TryGetValue(owner, out var state) && state.Slots.TryGetValue(growthId, out var slot) ? slot.Stacks : 0;

        public long ProgressMilli(string owner, string growthId)
            => owner != null && growthId != null && _owners.TryGetValue(owner, out var state) && state.Slots.TryGetValue(growthId, out var slot) ? slot.ProgressMilli : 0;

        /// <summary>
        /// 出来事を数える。units は HP系なら最大HPに対する%（例 3.5）、それ以外は出来事の回数（1）。
        /// 増えたスタック数を返す（上限で打ち切る。溜まる速さ2倍は1回の到達で2スタック）。
        /// </summary>
        public int Gain(string owner, RunGrowthEntry entry, RunGrowthTrigger trigger, double units)
        {
            if (string.IsNullOrEmpty(owner) || entry == null || RunGrowth.IsRemovedStar(entry.StarId) || entry.Trigger != trigger || !(units > 0d) || double.IsInfinity(units)) return 0;
            if (!_owners.TryGetValue(owner, out var state)) _owners.Add(owner, state = new OwnerState());
            if (!state.Slots.TryGetValue(entry.StarId, out var slot)) state.Slots.Add(entry.StarId, slot = new Slot());
            // Cap == 0 は上限なし。int.MaxValue は整数の桁あふれを防ぐためだけに使う。
            int limit = entry.Cap == 0 ? int.MaxValue : entry.Cap;
            if (slot.Stacks >= limit)
            {
                // 上限では進行を貯めない（上限が後から増えても一気に増えない）。
                slot.ProgressMilli = 0;
                return 0;
            }
            long per = (long)entry.Threshold * 1000L;
            long progress = (long)Math.Round(Math.Min(units, 1000000d) * 1000d);
            slot.ProgressMilli = Math.Min(slot.ProgressMilli, long.MaxValue - progress) + progress;
            long steps = slot.ProgressMilli / per;
            slot.ProgressMilli -= steps * per;
            if (steps <= 0) return 0;
            long add = Math.Min((long)limit - slot.Stacks, steps * entry.GainMultiplier);
            slot.Stacks += (int)add;
            if (slot.Stacks >= limit) slot.ProgressMilli = 0;
            if (add > 0) { Version++; state.Version++; }
            return (int)add;
        }

        // ───── 保存（遠征の復帰）─────

        public IReadOnlyList<RunGrowthSave> Export()
        {
            var list = new List<RunGrowthSave>();
            foreach (var owner in _owners.OrderBy(x => x.Key, StringComparer.Ordinal))
                foreach (var slot in owner.Value.Slots.OrderBy(x => x.Key, StringComparer.Ordinal))
                    if (slot.Value.Stacks > 0 || slot.Value.ProgressMilli > 0)
                        list.Add(new RunGrowthSave { Owner = owner.Key, GrowthId = slot.Key, Stacks = slot.Value.Stacks, ProgressMilli = slot.Value.ProgressMilli });
            return list;
        }

        public void Capture(RunRecoveryState state)
        {
            state.GrowthRunId = RunId;
            state.Growth.Clear();
            state.Growth.AddRange(Export());
        }

        public void Restore(RunRecoveryState state)
        {
            _owners.Clear();
            Generation++;
            RunId = null;
            Version++;
            if (state == null || string.IsNullOrEmpty(state.GrowthRunId)) return;
            RunId = state.GrowthRunId;
            foreach (var save in state.Growth)
            {
                if (save == null || string.IsNullOrEmpty(save.Owner) || !Gimmicks.ValidStarId(save.GrowthId) || RunGrowth.IsRemovedStar(save.GrowthId)
                    || save.Stacks < 0 || save.ProgressMilli < 0) continue;
                if (!_owners.TryGetValue(save.Owner, out var owner)) _owners.Add(save.Owner, owner = new OwnerState());
                owner.Slots[save.GrowthId] = new Slot { Stacks = save.Stacks, ProgressMilli = save.ProgressMilli };
            }
        }
    }
}

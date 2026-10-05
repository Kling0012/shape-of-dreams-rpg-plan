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
            if (Cap < 1 || Cap > MaxCap) throw new ArgumentException("RunGrowth cap is out of range.");
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
                    entry.Cap = Math.Min(RunGrowthDef.MaxCap, checked(entry.Cap + mod.CapBonus * row.Value));
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
                || e.Cap < 1 || e.Cap > RunGrowthDef.MaxCap || e.EffectPercent < 0 || e.EffectPercent > RunGrowthModifierDef.MaxEffectPercent
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

        /// <summary>stacks（上限を超えない）に応じた能力値の合計。表示単位（% や固定値）で返す。</summary>
        public static double StatTotal(RunGrowthEntry entry, RunGrowthEffect effect, int stacks)
        {
            int effective = Math.Max(0, Math.Min(stacks, entry.Cap));
            return effective * (effect.AmountMilli / 1000d) * (100 + entry.EffectPercent) / 100d;
        }

        /// <summary>HP系の契機で閾値に数える量（最大HPに対する%）。最大HPが不正なら0。</summary>
        public static double HealthPercent(float amount, float maxHealth)
            => maxHealth > 0f && amount > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount) ? amount / (double)maxHealth * 100d : 0d;

        // ───── 説明文 ─────

        public static string TriggerText(RunGrowthTrigger trigger, int threshold)
        {
            switch (trigger)
            {
                case RunGrowthTrigger.DamageTakenMaxHpPct:
                    return Loc.T($"実際に失ったHP（障壁が吸収した分を除く）が最大HPの{threshold}%に達するたび", $"each time actual health lost (excluding shield absorption) totals {threshold}% of max health");
                case RunGrowthTrigger.ShieldAbsorbedMaxHpPct:
                    return Loc.T($"障壁が吸収した量が最大HPの{threshold}%に達するたび", $"each time damage absorbed by shields totals {threshold}% of max health");
                case RunGrowthTrigger.ParrySuccess:
                    return threshold == 1 ? Loc.T("パリィに成功するたび", "each time you parry successfully")
                        : Loc.T($"パリィに{threshold}回成功するたび", $"every {threshold} successful parries");
                default:
                    return threshold == 1 ? Loc.T("会心の基本攻撃でとどめを刺すたび", "each time a critical basic attack lands the finishing blow")
                        : Loc.T($"会心の基本攻撃でとどめを刺すことを{threshold}回行うたび", $"every {threshold} finishing blows by critical basic attacks");
            }
        }

        public static string StatText(Stat stat, double value)
        {
            string number = value.ToString("0.##", CultureInfo.InvariantCulture);
            switch (stat)
            {
                case Stat.AttackFlat: return Loc.T($"攻撃力 +{number}", $"Attack Damage +{number}");
                case Stat.PowerFlat: return Loc.T($"魔力 +{number}", $"Ability Power +{number}");
                case Stat.Armor: return Loc.T($"防御 +{number}", $"Armor +{number}");
                case Stat.MaxHealthFlat: return Loc.T($"最大HP +{number}", $"Max Health +{number}");
                case Stat.MaxHealthPct: return Loc.T($"最大HP +{number}%", $"Max Health +{number}%");
                case Stat.AttackPct: return Loc.T($"攻撃力 +{number}%", $"Attack Damage +{number}%");
                case Stat.PowerPct: return Loc.T($"魔力 +{number}%", $"Ability Power +{number}%");
                case Stat.AttackSpeedPct: return Loc.T($"攻撃速度 +{number}%", $"Attack Speed +{number}%");
                case Stat.CritChancePct: return Loc.T($"会心率 +{number}%", $"Crit Chance +{number}%");
                case Stat.CritDamagePct: return Loc.T($"会心ダメージ +{number}%", $"Crit Damage +{number}%");
                case Stat.ShieldPower: return Loc.T($"シールドの強さ +{number}%", $"Shield Power +{number}%");
                case Stat.HealPower: return Loc.T($"回復の強さ +{number}%", $"Heal Power +{number}%");
                case Stat.SummonPower: return Loc.T($"召喚獣のダメージ +{number}%", $"Summon Damage +{number}%");
                case Stat.HealthRegen: return Loc.T($"HP自然回復 +{number}", $"Health Regen +{number}");
                case Stat.Haste: return Loc.T($"クールダウン短縮 +{number}", $"Ability Haste +{number}");
                case Stat.MoveSpeedPct: return Loc.T($"移動速度 +{number}%", $"Move Speed +{number}%");
                case Stat.Tenacity: return Loc.T($"不屈 +{number}", $"Tenacity +{number}");
                default: return Content.FormatStat(stat, (int)Math.Round(value));
            }
        }

        private static string PerStack(RunGrowthEntry entry)
            => string.Join(Loc.T("、", ", "), entry.Effects.Select(x => StatText(x.Stat, x.AmountMilli / 1000d * (100 + entry.EffectPercent) / 100d)));

        /// <summary>星図・一覧に出す説明（1スタックあたりと上限）。</summary>
        public static string Describe(RunGrowthEntry entry)
        {
            int gain = Math.Max(1, entry.GainMultiplier);
            return Loc.T($"{PerStack(entry)}／鍛錬1つ：{TriggerText(entry.Trigger, entry.Threshold)}鍛錬が{gain}つ溜まる（最大{entry.Cap}。遠征の間は死亡・ゾーン移動でも失われず、遠征の開始で0に戻る）",
                $"{PerStack(entry)} per training stack: gain {gain} stack{(gain == 1 ? "" : "s")} {TriggerText(entry.Trigger, entry.Threshold)} (up to {entry.Cap} stacks; kept through death and zone changes for the whole expedition, reset when a new expedition begins)");
        }

        public static string Describe(RunGrowthDef def)
        {
            var entry = new RunGrowthEntry { Trigger = def.Trigger, Threshold = def.Threshold, Cap = def.Cap, Effects = def.Effects };
            return Describe(entry);
        }

        public static string Describe(RunGrowthModifierDef mod, string heroKey = null)
        {
            var parts = new List<string>();
            if (mod.CapBonus > 0) parts.Add(Loc.T($"鍛錬の蓄積上限 +{mod.CapBonus}", $"training stack cap +{mod.CapBonus}"));
            if (mod.EffectPercent > 0) parts.Add(Loc.T($"鍛錬1つごとの能力値増加量 +{mod.EffectPercent}%", $"stat gain per training stack +{mod.EffectPercent}%"));
            if (mod.DoubleGain) parts.Add(Loc.T("鍛錬の獲得数 +100%（条件1回につき2つ）", "training stack gain +100% (2 stacks per completed condition)"));
            var targets = new List<TalentDef>();
            if (heroKey != null)
                targets.AddRange(HeroSigils.TreeFor(heroKey).Where(t => t.RunGrowth != null && (mod.TargetStarId == null || t.Id == mod.TargetStarId)));
            else if (mod.TargetStarId != null && Content.TryGetTalent(mod.TargetStarId, out var target))
                targets.Add(target);
            string TargetText(TalentDef talent)
            {
                var growth = talent.RunGrowth;
                string gains = string.Join(Loc.T("、", ", "), growth.Effects.Select(effect =>
                {
                    double amount = effect.AmountMilli / 1000d;
                    string before = StatText(effect.Stat, amount);
                    return mod.EffectPercent == 0 ? before
                        : before + " → " + StatText(effect.Stat, amount * (100 + mod.EffectPercent) / 100d);
                }));
                string detail = Loc.T("。鍛錬1つごと：", ". Per training stack: ") + gains;
                if (mod.CapBonus > 0)
                    detail += Loc.T($"。蓄積上限：{growth.Cap} → {growth.Cap + mod.CapBonus}", $". Stack cap: {growth.Cap} → {growth.Cap + mod.CapBonus}");
                if (mod.EffectPercent > 0 || mod.CapBonus > 0)
                    detail += Loc.T("（この星だけを基本値へ適用した場合）", " (with only this star applied to the base values)");
                return Loc.T("星『", "star “") + talent.Name + Loc.T("』：", "”: ")
                    + TriggerText(growth.Trigger, growth.Threshold) + detail;
            }
            string scope = targets.Count > 0
                ? string.Join(Loc.T("、", "; "), targets.Select(TargetText))
                : Loc.T("この旅人のすべての遠征の鍛錬", "all expedition training for this traveler");
            return string.Join(Loc.T("、", ", "), parts) + Loc.T("。対象：", ". Applies to ") + scope;
        }

        /// <summary>HUDの小さな1行。</summary>
        public static string HudLine(RunGrowthEntry entry, int stacks)
        {
            int shown = Math.Max(0, Math.Min(stacks, entry.Cap));
            return Loc.T($"鍛錬 {shown}/{entry.Cap}", $"Training {shown}/{entry.Cap}") + (shown > 0
                ? " <color=#c4c4dc>(" + string.Join(" ", entry.Effects.Select(x => StatText(x.Stat, StatTotal(entry, x, shown)))) + ")</color>" : "");
        }

        /// <summary>取得した効果の一覧に出す、現在のスタックの1行。</summary>
        public static string SummaryLine(RunGrowthEntry entry, int stacks)
        {
            int shown = Math.Max(0, Math.Min(stacks, entry.Cap));
            return Describe(entry) + "\n" + Loc.T($"現在 {shown}/{entry.Cap}スタック", $"Now {shown}/{entry.Cap} stacks")
                + (shown > 0 ? "（" + string.Join(Loc.T("、", ", "), entry.Effects.Select(x => StatText(x.Stat, StatTotal(entry, x, shown)))) + "）" : "");
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

        private readonly Dictionary<string, Dictionary<string, Slot>> _owners = new Dictionary<string, Dictionary<string, Slot>>(StringComparer.Ordinal);

        public string RunId { get; private set; }
        /// <summary>スタックが変わるたびに増える。表示の作り直しの目印。</summary>
        public int Version { get; private set; }

        /// <summary>遠征の識別子が前と違えば全部を0に戻す。戻したら true。</summary>
        public bool EnsureRun(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return false;
            if (runId == RunId) return false;
            bool had = _owners.Count > 0;
            _owners.Clear();
            RunId = runId;
            if (had) Version++;
            return true;
        }

        /// <summary>遠征の終わりなど、識別子に関わらず0へ戻す。</summary>
        public void Reset()
        {
            if (_owners.Count > 0) Version++;
            _owners.Clear();
            RunId = null;
        }

        public int Stacks(string owner, string growthId)
            => owner != null && growthId != null && _owners.TryGetValue(owner, out var slots) && slots.TryGetValue(growthId, out var slot) ? slot.Stacks : 0;

        public long ProgressMilli(string owner, string growthId)
            => owner != null && growthId != null && _owners.TryGetValue(owner, out var slots) && slots.TryGetValue(growthId, out var slot) ? slot.ProgressMilli : 0;

        /// <summary>
        /// 出来事を数える。units は HP系なら最大HPに対する%（例 3.5）、それ以外は出来事の回数（1）。
        /// 増えたスタック数を返す（上限で打ち切る。溜まる速さ2倍は1回の到達で2スタック）。
        /// </summary>
        public int Gain(string owner, RunGrowthEntry entry, RunGrowthTrigger trigger, double units)
        {
            if (string.IsNullOrEmpty(owner) || entry == null || entry.Trigger != trigger || !(units > 0d) || double.IsInfinity(units)) return 0;
            if (!_owners.TryGetValue(owner, out var slots)) _owners.Add(owner, slots = new Dictionary<string, Slot>(StringComparer.Ordinal));
            if (!slots.TryGetValue(entry.StarId, out var slot)) slots.Add(entry.StarId, slot = new Slot());
            if (slot.Stacks >= entry.Cap)
            {
                // 上限では進行を貯めない（上限が後から増えても一気に増えない）。
                slot.ProgressMilli = 0;
                return 0;
            }
            long per = (long)entry.Threshold * 1000L;
            slot.ProgressMilli += (long)Math.Round(Math.Min(units, 1000000d) * 1000d);
            long steps = slot.ProgressMilli / per;
            slot.ProgressMilli -= steps * per;
            if (steps <= 0) return 0;
            long add = Math.Min((long)entry.Cap - slot.Stacks, steps * entry.GainMultiplier);
            slot.Stacks += (int)add;
            if (slot.Stacks >= entry.Cap) slot.ProgressMilli = 0;
            if (add > 0) Version++;
            return (int)add;
        }

        // ───── 保存（遠征の復帰）─────

        public IReadOnlyList<RunGrowthSave> Export()
        {
            var list = new List<RunGrowthSave>();
            foreach (var owner in _owners.OrderBy(x => x.Key, StringComparer.Ordinal))
                foreach (var slot in owner.Value.OrderBy(x => x.Key, StringComparer.Ordinal))
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
            RunId = null;
            Version++;
            if (state == null || string.IsNullOrEmpty(state.GrowthRunId)) return;
            RunId = state.GrowthRunId;
            foreach (var save in state.Growth)
            {
                if (save == null || string.IsNullOrEmpty(save.Owner) || !Gimmicks.ValidStarId(save.GrowthId)
                    || save.Stacks < 0 || save.Stacks > RunGrowthDef.MaxCap || save.ProgressMilli < 0) continue;
                if (!_owners.TryGetValue(save.Owner, out var slots)) _owners.Add(save.Owner, slots = new Dictionary<string, Slot>(StringComparer.Ordinal));
                slots[save.GrowthId] = new Slot { Stacks = save.Stacks, ProgressMilli = save.ProgressMilli };
            }
        }
    }
}

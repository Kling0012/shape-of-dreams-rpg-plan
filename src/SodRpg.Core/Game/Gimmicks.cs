using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>記憶の仕掛けのきっかけ（v1.28）。仕掛けは、その星のルートの記憶に反応する。</summary>
    public enum GimmickTrigger
    {
        None = 0,
        /// <summary>その記憶を使ったとき。</summary>
        OnUse = 1,
        /// <summary>その記憶のダメージが敵に当たったとき。</summary>
        OnHit = 2,
        /// <summary>その記憶のダメージで敵を倒したとき。</summary>
        OnKill = 3,
        /// <summary>その記憶のダメージが会心したとき。</summary>
        OnCrit = 4,
    }

    /// <summary>記憶の仕掛けで起きること（v1.28）。値の意味は docs/specs/v1.28-memory-gimmicks.md。</summary>
    public enum GimmickEffect
    {
        None = 0,
        /// <summary>属性を付ける。Arg：0 火・1 冷気・2 光・3 闇。Value：100 ごとに1つ、残りは%の確率でもう1つ。</summary>
        Element = 1,
        /// <summary>周り4mに、攻撃力・魔力の高い方の Value% の追加ダメージ。</summary>
        Burst = 2,
        /// <summary>自分に最大HPの Value% の障壁（4秒）。</summary>
        Shield = 3,
        /// <summary>自分を最大HPの Value% 回復。Arg=1 なら近くの味方も。</summary>
        Heal = 4,
        /// <summary>その記憶の残りクールダウンを Value% 縮める。</summary>
        Recharge = 5,
        /// <summary>4秒間、攻撃速度 +Value%（重ならず時間を延長）。</summary>
        Quicken = 6,
        /// <summary>4秒間、攻撃力・魔力 +Value%（重ならず時間を延長）。</summary>
        Empower = 7,
        /// <summary>当てた敵は4秒間、自分から受けるダメージが Value% 増える（重ならず時間を延長）。</summary>
        Expose = 8,
        /// <summary>当てたダメージの Value% を、0.3秒後にもう一度与える。</summary>
        Echo = 9,
        /// <summary>その記憶の使用回数を1回戻す。単発の記憶は残りクールダウンを全て戻す（v1.28）。</summary>
        Reload = 10,
        /// <summary>装着中のほかの記憶の残りクールダウンを Value% 縮める（回避・Ultimate・アイデンティティは対象外。v1.28）。</summary>
        RechargeOther = 11,
        Wound = 12,
        Daze = 13,
        Ricochet = 14,
        Siphon = 15,
        Rampart = 16,
        Primed = 17,
        Crescendo = 18,
        ElementEdge = 19,
        PackMend = 20,
        Sap = 21,
        Weakspot = 22,
    }

    /// <summary>記憶の仕掛け1つ分の定義（v1.28）。</summary>
    public sealed class GimmickDef
    {
        public GimmickTrigger Trigger { get; set; }
        public GimmickEffect Effect { get; set; }
        /// <summary>Exact effect magnitude in ten-millionths of one percentage point.</summary>
        public long ValuePrecise { get; set; }
        /// <summary>Legacy thousandth accessor; a finer value cannot be projected lossily.</summary>
        public int ValueMilli
        {
            get => ValuePrecise % 10000 == 0 ? checked((int)(ValuePrecise / 10000))
                : throw new InvalidOperationException("The effect value requires exact fine units.");
            set => ValuePrecise = (long)value * 10000;
        }
        public decimal Value
        {
            get => ValuePrecise / (decimal)Gimmicks.PreciseValueScale;
            set
            {
                decimal units = value * Gimmicks.PreciseValueScale;
                if (units != decimal.Truncate(units)) throw new ArgumentOutOfRangeException(nameof(value), "Effect values require exact fine units.");
                ValuePrecise = checked((long)units);
            }
        }
        /// <summary>Exact postordinary input retained when the legacy fields cannot carry it before C07.</summary>
        public decimal? UncappedValue { get; set; }
        public long? UncappedDurationUnits { get; set; }
        public long? UncappedRadiusUnits { get; set; }
        public long? UncappedExtraTargets { get; set; }
        public long? UncappedChanceUnits { get; set; }
        /// <summary>Runtime magnitude; unlike pristine authored fine units, composed decimals have no fixed scale.</summary>
        public decimal? EffectiveValue { get; set; }
        public decimal EffectiveValueOrAuthored => EffectiveValue ?? Value;
        /// <summary>Convert only at the native application boundary.</summary>
        public float ValuePercent => (float)EffectiveValueOrAuthored;
        /// <summary>効果の補足（属性の種類、味方も回復するか）。</summary>
        public int Arg { get; set; }
        /// <summary>内部の間隔（秒）。0 なら制限なし。</summary>
        public float Cooldown { get; set; }
        /// <summary>Additive cluster modifiers, bounded by the shared runtime contract.</summary>
        public int DurationUnits { get; set; }
        public int RadiusUnits { get; set; }
        public int DurationPercent { get => UnitConversion.WholePercent(DurationUnits); set => DurationUnits = checked(value * 100); }
        public int RadiusPercent { get => UnitConversion.WholePercent(RadiusUnits); set => RadiusUnits = checked(value * 100); }
        public int ExtraTargets { get; set; }
        public int ChanceUnits { get; set; }
        public int ChancePercent { get => UnitConversion.WholePercent(ChanceUnits); set => ChanceUnits = checked(value * 100); }
        /// <summary>Host-derived C07 values, after ordinary modifiers; never authored or transmitted.</summary>
        public float? EffectiveDurationSeconds { get; set; }
        public float? EffectiveRadiusMetres { get; set; }
        public float? EffectiveDelaySeconds { get; set; }
        public int? EffectiveTargetCount { get; set; }
        public bool EffectiveWoundTotal { get; set; }
        public decimal? EffectiveAllyValuePercent { get; set; }
        public decimal? EffectiveChanceProbabilityUnits { get; set; }
        internal GimmickDef Copy() => (GimmickDef)MemberwiseClone();
    }

    /// <summary>装着中の星の仕掛け。Def.Value は取得した段数を掛けた値。</summary>
    public sealed class GimmickEntry
    {
        public string StarId { get; set; }
        public string Memory { get; set; }
        public GimmickDef Def { get; set; }
        public EffectChannelDef Channel { get; set; }
        public string[] ContributorIds { get; set; } = Array.Empty<string>();
        /// <summary>Host-only channel: Fire requires an explicit C02 filter and resolves the actual source at admission.</summary>
        public bool AdmittedSource { get; set; }
    }

    /// <summary>ホストへ渡す発動。対象の解決と追加ダメージの連鎖防止はホストが行う。</summary>
    public struct GimmickRequest
    {
        public GimmickEntry Entry { get; set; }
        public int VictimId { get; set; }
        public float Damage { get; set; }
        public KeystoneSourceKind? SourceKind { get; set; }
        /// <summary>周囲へ作用する半径（m）。0 は単体への作用。</summary>
        public float AreaRadius { get; set; }
        /// <summary>範囲の中心が使用者か。false は VictimId の敵の発動時の位置。</summary>
        public bool AreaAroundHero { get; set; }
        /// <summary>Unique targets from this cast, capped by the entry's target limit, for Rampart.</summary>
        public int TargetCount { get; set; }
        public long ActivationId { get; set; }
        /// <summary>Element types present at the triggering hit, before deferred dispatch.</summary>
        public int ElementTypes { get; set; }
    }

    /// <summary>記憶の仕掛けの説明と、通信・計算で共用する上限。</summary>
    public static partial class Gimmicks
    {
        public const int ValueScale = BuildPrecision.Scale;
        public const long PreciseValueScale = 10000000;
        public static int MaxEntries => BuildLimits.MaxGimmickEntries;
        public const int MaxStarIdLength = BuildLimits.MaxStarIdLength;
        public const float MaxCooldown = 60f;
        public const float BuffDuration = 4f;
        public const float AreaRadius = 4f;

        /// <summary>Finite transport/runtime ceilings cover all authored ranks, ordinary boosts and keystone upsides.</summary>
        public static int Cap(GimmickEffect effect)
        {
            switch (effect)
            {
                case GimmickEffect.Element: return 1000;
                case GimmickEffect.Reload: return 1;
                case GimmickEffect.Burst: return 1000;
                case GimmickEffect.Echo: return 1500;
                case GimmickEffect.Empower: return 200;
                case GimmickEffect.Wound: return 1500;
                case GimmickEffect.Primed: return 120;
                case GimmickEffect.Daze: return 20;
                case GimmickEffect.Crescendo: return 8;
                case GimmickEffect.PackMend: return 15;
                case GimmickEffect.Ricochet: return 60;
                case GimmickEffect.Siphon: return 10;
                case GimmickEffect.Rampart: return 20;
                case GimmickEffect.ElementEdge: return 60;
                case GimmickEffect.Sap: return 25;
                case GimmickEffect.Weakspot: return 25;
                case GimmickEffect.Shield: return 300;
                case GimmickEffect.Heal:
                case GimmickEffect.Recharge:
                case GimmickEffect.RechargeOther:
                case GimmickEffect.Quicken:
                case GimmickEffect.Expose: return 100;
                default: return 0;
            }
        }

        public static bool ValidDef(GimmickDef def)
        {
            if (def == null || def.Trigger < GimmickTrigger.OnUse || def.Trigger > GimmickTrigger.OnCrit
                || Cap(def.Effect) == 0 || def.ValuePrecise <= 0 || !Finite(def.Cooldown) || def.Cooldown < 0
                || def.DurationUnits < 0 || def.RadiusUnits < 0 || def.ExtraTargets < 0 || def.ChanceUnits < 0
                || !GimmickRawCodec.Valid(def)) return false;
            if (IsV129(def.Effect)) return ValidV129Def(def);
            return def.Effect == GimmickEffect.Element ? def.Arg >= 0 && def.Arg <= 3
                : def.Effect == GimmickEffect.Heal ? def.Arg >= 0 && def.Arg <= 1 : def.Arg == 0;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>複数回の記憶は1回だけ補充。単発の記憶は回数を直接変えず、全クールダウンを戻す。</summary>
        public static bool TryReload(int currentCharges, int maxCharges, out int nextCharges, out bool resetCooldown)
        {
            nextCharges = currentCharges;
            resetCooldown = false;
            if (maxCharges <= 0 || currentCharges < 0 || currentCharges > maxCharges) return false;
            if (maxCharges == 1)
            {
                resetCooldown = true;
                return true;
            }
            if (currentCharges == maxCharges) return false;
            nextCharges = currentCharges + 1;
            return true;
        }

        /// <summary>残り時間の割合短縮を、本体APIの最大時間基準の比率へ換算する。</summary>
        public static float RemainingCooldownReductionRatio(float remaining, float maximum, float percent)
        {
            if (!Finite(remaining) || !Finite(maximum) || !Finite(percent) || remaining <= 0f || maximum <= 0f || percent <= 0) return 0f;
            float ratio = (float)((double)remaining / maximum * Math.Min(percent, 100) / 100);
            return Finite(ratio) ? ratio : 0f;
        }

        /// <summary>実際のスキル種別で判定する。枠や型名の Q/R では通常スキルかどうかを推測しない。</summary>
        public static bool CanRechargeOther(string sourceMemory, string targetMemory, bool isNormalSkill, bool isIdentity) =>
            isNormalSkill && !isIdentity && sourceMemory != targetMemory
            && Links.IsMemory(sourceMemory) && Links.IsMemory(targetMemory);

        /// <summary>通信で使う区切りを含まない、長さ1〜96の星の識別子。</summary>
        public static bool ValidStarId(string starId)
        {
            if (string.IsNullOrEmpty(starId) || starId.Length > MaxStarIdLength) return false;
            foreach (char c in starId)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')
                    && c != '_' && c != '.' && c != '-') return false;
            return true;
        }

        /// <summary>不正な条件は拒否し、効果量と間隔を共通上限へ収めた独立したコピーを返す。</summary>
        public static GimmickEntry Clamp(GimmickEntry entry)
        {
            if (entry == null || !ValidStarId(entry.StarId) || !ValidDef(entry.Def)
                || !entry.AdmittedSource && (!Links.IsMemory(entry.Memory) || !AllowedOnMemory(entry.Def.Effect, entry.Memory))
                || entry.Def.Effect == GimmickEffect.Primed && entry.StarId.StartsWith("h.bismuth.", StringComparison.Ordinal)) return null;
            return new GimmickEntry
            {
                StarId = entry.StarId,
                Memory = entry.Memory,
                Channel = entry.Channel,
                ContributorIds = entry.ContributorIds,
                AdmittedSource = entry.AdmittedSource,
                Def = new GimmickDef
                {
                    Trigger = entry.Def.Trigger,
                    Effect = entry.Def.Effect,
                    ValuePrecise = Math.Min(entry.Def.ValuePrecise, Cap(entry.Def.Effect) * PreciseValueScale),
                    UncappedValue = entry.Def.UncappedValue,
                    UncappedDurationUnits = entry.Def.UncappedDurationUnits,
                    UncappedRadiusUnits = entry.Def.UncappedRadiusUnits,
                    UncappedExtraTargets = entry.Def.UncappedExtraTargets,
                    UncappedChanceUnits = entry.Def.UncappedChanceUnits,
                    EffectiveValue = Math.Min(entry.Def.EffectiveValueOrAuthored, StarDamageScaling.EffectCeiling(entry.Def.Effect)),
                    Arg = entry.Def.Arg,
                    Cooldown = Math.Max(MinimumCooldown(entry.Def.Effect), Math.Min(entry.Def.Cooldown, MaxCooldown)),
                    DurationUnits = SupportsParameter(entry.Def, GimmickParam.Duration) ? Math.Min(entry.Def.DurationUnits, MaxParameterPercent * 100) : 0,
                    RadiusUnits = SupportsParameter(entry.Def, GimmickParam.Radius) ? Math.Min(entry.Def.RadiusUnits, MaxParameterPercent * 100) : 0,
                    ExtraTargets = SupportsParameter(entry.Def, GimmickParam.ExtraTargets) ? Math.Min(entry.Def.ExtraTargets, MaxExtraTargets) : 0,
                    ChanceUnits = SupportsParameter(entry.Def, GimmickParam.Chance) ? Math.Min(entry.Def.ChanceUnits, 10000) : 0,
                    EffectiveDurationSeconds = entry.Def.EffectiveDurationSeconds,
                    EffectiveRadiusMetres = entry.Def.EffectiveRadiusMetres,
                    EffectiveDelaySeconds = entry.Def.EffectiveDelaySeconds,
                    EffectiveTargetCount = entry.Def.EffectiveTargetCount,
                    EffectiveWoundTotal = entry.Def.EffectiveWoundTotal,
                    EffectiveAllyValuePercent = entry.Def.EffectiveAllyValuePercent,
                    EffectiveChanceProbabilityUnits = SupportsParameter(entry.Def, GimmickParam.Chance) ? ChanceProbabilityUnits(entry.Def) : (decimal?)null
                }
            };
        }

        /// <summary>1段の定義から、段数込みの効果・間隔・上限を説明する。</summary>
        public static string Describe(GimmickDef def, string memoryTypeName, int ranks = 1)
        {
            if (!ValidDef(def) || !Links.IsMemory(memoryTypeName) || ranks <= 0) return "";
            if (IsV129(def.Effect) && !AllowedOnMemory(def.Effect, memoryTypeName)) return "";
            return DescribeForSource(def, Links.ItemName(memoryTypeName), ranks);
        }

        internal static string DescribeForSource(GimmickDef def, string sourceText, int ranks = 1, string triggerText = null)
        {
            if (!ValidDef(def) || ranks <= 0) return "";
            if (IsV129(def.Effect)) return DescribeV129(def, sourceText, ranks, triggerText);
            decimal value = Math.Min(def.Value * ranks, Cap(def.Effect));
            string n = value.ToString("0.#######", CultureInfo.InvariantCulture);
            string duration = Duration(def, BuffDuration).ToString("0.#######", CultureInfo.InvariantCulture);
            string radius = Radius(def, AreaRadius).ToString("0.#######", CultureInfo.InvariantCulture);
            string healRadius = Radius(def, 10f).ToString("0.#######", CultureInfo.InvariantCulture);
            bool ja = Loc.Japanese;
            string memory = sourceText;
            string trigger;
            switch (def.Trigger)
            {
                case GimmickTrigger.OnUse: trigger = ja ? memory + "を使うと発動" : "Triggered when " + memory + " is used"; break;
                case GimmickTrigger.OnHit: trigger = ja ? memory + "が当たると発動" : "Triggered when " + memory + " hits"; break;
                case GimmickTrigger.OnKill: trigger = ja ? memory + "で敵を倒すと発動" : "Triggered when " + memory + " kills an enemy"; break;
                default: trigger = ja ? memory + "が会心すると発動" : "Triggered when " + memory + " critically hits"; break;
            }
            if (triggerText != null) trigger = triggerText;
            string effect;
            switch (def.Effect)
            {
                case GimmickEffect.Element:
                    string element = ja ? (def.Arg == 0 ? "火" : def.Arg == 1 ? "冷気" : def.Arg == 2 ? "光" : "闇")
                        : (def.Arg == 0 ? "fire" : def.Arg == 1 ? "cold" : def.Arg == 2 ? "light" : "darkness");
                    int whole = (int)(value / 100);
                    string chance = Math.Min(100, value % 100 + ChanceProbabilityUnits(def) / 100m).ToString("0.#######", CultureInfo.InvariantCulture);
                    string stacks = ja ? (whole == 0 ? chance + "%の確率で1つ"
                        : whole + "つ" + (chance == "0" ? "" : "（さらに" + chance + "%の確率でもう1つ）"))
                        : (whole == 0 ? "1 stack with a " + chance + "% chance"
                        : whole + (whole == 1 ? " stack" : " stacks") + (chance == "0" ? "" : " (plus a " + chance + "% chance of 1 more)"));
                    string targets = def.Trigger == GimmickTrigger.OnUse
                        ? (ja ? "自分の周り" + radius + "mの敵" : "enemies within " + radius + "m of yourself")
                        : def.Trigger == GimmickTrigger.OnKill
                            ? (ja ? "倒した敵の周り" + radius + "mの敵" : "enemies within " + radius + "m of the killed enemy")
                            : (ja ? "当てた敵" : "the hit enemy");
                    effect = ja ? element + "付与 +" + stacks + "：" + targets + "に付ける"
                        : element + " stacks +" + stacks + ": apply to " + targets;
                    break;
                case GimmickEffect.Burst:
                    string center = def.Trigger == GimmickTrigger.OnUse ? (ja ? "自分" : "yourself")
                        : def.Trigger == GimmickTrigger.OnKill ? (ja ? "倒した敵" : "the killed enemy") : (ja ? "当てた敵" : "the hit enemy");
                    effect = ja ? "範囲追加ダメージ +攻撃力・魔力の高い方の" + n + "%：" + center + "の周り" + radius + "mの敵に与える（魔力が高ければ魔法）"
                        : "Area damage +" + n + "% of the higher of attack damage or ability power: damage enemies within " + radius + "m of "
                            + center + " (magic damage if ability power is higher)";
                    break;
                case GimmickEffect.Shield:
                    effect = ja ? "障壁 +最大HPの" + n + "%：自分に張る（" + duration + "秒。星の障壁は付与者と受け手の組ごとに1つだけで、残量と新しい量の大きい方を保って時間を更新）"
                        : "Shield +" + n + "% of maximum health: shield yourself for " + duration + " seconds (one star shield per giver and recipient; keeps the larger of the remaining and new amounts and refreshes the duration)";
                    break;
                case GimmickEffect.Heal:
                    effect = ja ? "HP回復 +各自の最大HPの" + n + "%：自分" + (def.Arg == 1 ? "と" + healRadius + "m以内の味方" : "") + "を回復"
                        : "Healing +" + n + "% of each recipient's maximum health: heal yourself" + (def.Arg == 1 ? " and allies within " + healRadius + "m" : "");
                    break;
                case GimmickEffect.Recharge:
                    effect = ja ? "残りクールダウン -" + n + "%：その記憶の残り時間を短縮" : "Remaining cooldown -" + n + "%: shorten that memory's remaining cooldown";
                    break;
                case GimmickEffect.Reload:
                    effect = ja ? "使用回数 +1回：その記憶に戻す（最大使用回数を超えない。使用回数が1回の記憶は、代わりに残りクールダウンを全て戻す）"
                        : "Charges +1: restore to that memory, up to its maximum charges (single-charge memories fully reset their remaining cooldown instead)";
                    break;
                case GimmickEffect.RechargeOther:
                    effect = ja ? "ほかの通常記憶の残りクールダウン -" + n + "%：装着中の記憶が対象（移動・奥義・アイデンティティは対象外）"
                        : "Other normal memories' remaining cooldown -" + n + "%: affects equipped memories (excluding Movement, Ultimate, and Identity memories)";
                    break;
                case GimmickEffect.Quicken:
                    effect = ja ? "攻撃速度 +" + n + "%：" + duration + "秒間" : "Attack speed +" + n + "% for " + duration + " seconds";
                    break;
                case GimmickEffect.Empower:
                    effect = ja ? "攻撃力・魔力 +" + n + "%：" + duration + "秒間" : "Attack damage and ability power +" + n + "% for " + duration + " seconds";
                    break;
                case GimmickEffect.Expose:
                    effect = ja ? "自分から受けるダメージ +" + n + "%：当てた敵に" + duration + "秒間適用"
                        : "Damage taken from you +" + n + "%: applies to the hit enemy for " + duration + " seconds";
                    break;
                default:
                    effect = ja ? "追撃ダメージ +当てたダメージの" + n + "%：0.3秒後にもう一度与える" : "Echo damage +" + n + "% of the hit damage: deal again after 0.3 seconds";
                    break;
            }
            string cooldown = Math.Min(def.Cooldown, MaxCooldown).ToString("R", CultureInfo.InvariantCulture);
            string interval = def.Cooldown == 0
                ? (ja ? "間隔制限なし" : "no cooldown")
                : (ja ? "この星ごとに" + cooldown + "秒に1回" : "once every " + cooldown + (def.Cooldown == 1f ? " second" : " seconds") + " per star, shared across enemies and triggers");
            string cap = def.Effect == GimmickEffect.Element
                ? (ja ? "基本の上限は" + Cap(def.Effect) / 100 + "つ（確率補正で増やせるのは1つまで）"
                    : "base amount capped at " + Cap(def.Effect) / 100 + " stacks (chance modifiers can add at most 1 more)")
                : def.Effect == GimmickEffect.Reload
                    ? (ja ? "効果は最大1回分" : "capped at 1 charge")
                    : (ja ? "効果量は最大" + Cap(def.Effect) + "%" : "effect capped at " + Cap(def.Effect) + "%");
            string stacking = def.Effect == GimmickEffect.Quicken || def.Effect == GimmickEffect.Empower || def.Effect == GimmickEffect.Expose
                ? (ja ? "・同時に有効なのは最大の1つだけで、重ならず時間を延長" : "; only the strongest active value applies; it refreshes the duration without stacking") : "";
            return effect + (ja ? "。" : ". ") + trigger
                + (ja ? "（" + interval + "・" + cap + stacking + "。星の追加ダメージでは発動しない）"
                : " (" + interval + "; " + cap + stacking + "; not triggered by extra damage from stars).");
        }
    }

    /// <summary>Per-star intervals and non-stacking timed effects; clocks and victim lifetimes are supplied by the host.</summary>
    public sealed partial class GimmickRuntime
    {
        private sealed class ActiveEntry
        {
            public GimmickEntry Entry;
            public GimmickEntry ConfiguredEntry;
            public readonly Dictionary<string, ActiveEntry> SourceStates = new Dictionary<string, ActiveEntry>(StringComparer.Ordinal);
            public bool HasFired;
            public float LastFired;
            public bool BuffActive;
            public float BuffUntil;
            public List<VictimWindow> Victims;
            public readonly Dictionary<long, HashSet<int>> CastVictims = new Dictionary<long, HashSet<int>>();
            public readonly Dictionary<long, float> SeenCasts = new Dictionary<long, float>();
            public int Stacks;
        }

        private struct VictimWindow
        {
            public int VictimId;
            public float Until;
        }

        private List<ActiveEntry> _entries = new List<ActiveEntry>();

        /// <summary>同じ定義を再設定しても間隔は戻らない。外した星・変更した星の効果は解除する。</summary>
        public void SetBuild(IReadOnlyList<GimmickEntry> entries)
        {
            if (entries != null && entries.Count > Gimmicks.MaxEntries)
                throw new ArgumentOutOfRangeException(nameof(entries), "The build exceeds the legal gimmick count.");
            var next = new List<ActiveEntry>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i]?.Channel != null)
                    {
                        ScopedBuildCodec.ValidateGimmicks(entries);
                        break;
                    }
                for (int i = 0; i < entries.Count; i++)
                {
                    GimmickEntry entry = Gimmicks.Clamp(entries[i]);
                    if (entry == null) throw new ArgumentException("Invalid gimmick entry.", nameof(entries));
                    if (entry.Channel != null) FractionalScopedModifiers.ChannelKey(entry);
                    bool duplicate = false;
                    for (int j = 0; j < next.Count; j++)
                        if (next[j].Entry.StarId == entry.StarId) { duplicate = true; break; }
                    if (duplicate) throw new ArgumentException("Duplicate gimmick star ID.", nameof(entries));
                    ActiveEntry retained = null;
                    for (int j = 0; j < _entries.Count; j++)
                    {
                        ActiveEntry previous = _entries[j];
                        if (previous.Entry.StarId != entry.StarId) continue;
                        retained = Same(previous.ConfiguredEntry ?? previous.Entry, entry) ? previous : new ActiveEntry
                        {
                            Entry = entry,
                            ConfiguredEntry = entry,
                            HasFired = previous.HasFired,
                            LastFired = previous.LastFired
                        };
                        break;
                    }
                    next.Add(retained ?? new ActiveEntry { Entry = entry, ConfiguredEntry = entry });
                }
            }
            _entries = next;
            _hasExposeEntries = false;
            _hasSapEntries = false;
            _hasWeakspotEntries = false;
            _hasCrescendoEntries = false;
            foreach (var entry in _entries)
            {
                var effect = entry.Entry.Def.Effect;
                if (effect == GimmickEffect.Expose) _hasExposeEntries = true;
                else if (effect == GimmickEffect.Sap) _hasSapEntries = true;
                else if (effect == GimmickEffect.Weakspot) _hasWeakspotEntries = true;
                else if (effect == GimmickEffect.Crescendo) _hasCrescendoEntries = true;
            }

            _activeStates.Clear();
            foreach (var state in _entries)
            {
                _activeStates.Add(state);
                _activeStates.AddRange(state.SourceStates.Values);
            }
        }
        // 命中ごとに走る Expose/Sap/Weakspot 参照を、その効果が1つもないビルドでは全走査なしで返す（#161）。
        private bool _hasExposeEntries, _hasSapEntries, _hasWeakspotEntries, _hasCrescendoEntries;
        private readonly List<ActiveEntry> _activeStates = new List<ActiveEntry>();
        private IReadOnlyList<ActiveEntry> ActiveStates() => _activeStates;

        private static bool Same(GimmickEntry a, GimmickEntry b) =>
            a.AdmittedSource == b.AdmittedSource && BuildAggregation.GimmickStateKey(a) == BuildAggregation.GimmickStateKey(b)
            && a.Def.ValuePrecise == b.Def.ValuePrecise;

        /// <summary>期限ちょうどで効果を解除し、敵ごとの期限も取り除く。</summary>
        public void PruneExpired(float now)
        {
            if (!Gimmicks.Finite(now)) return;
            foreach (ActiveEntry state in ActiveStates())
            {
                if (state.BuffActive && now >= state.BuffUntil) state.BuffActive = false;
                if (state.Victims == null) continue;
                for (int j = state.Victims.Count - 1; j >= 0; j--)
                    if (now >= state.Victims[j].Until) state.Victims.RemoveAt(j);
            }
        }

        /// <summary>該当する発動を末尾へ追加。追加ダメージは起点にならず、間隔は星全体で共有する。</summary>
        public void Fire(GimmickTrigger trigger, string memory, float now, int victimId, float damage, bool generated, List<GimmickRequest> results,
            long activationId = 0, float memoryCooldown = 0f, bool directDamage = true, bool boss = false, int elementTypes = 0,
            Func<GimmickEntry, bool> filter = null, Func<GimmickEntry, bool> admit = null,
            Func<GimmickEntry, GimmickDef> transform = null)
        {
            if (generated || results == null || !Gimmicks.Finite(now) || trigger < GimmickTrigger.OnUse || trigger > GimmickTrigger.OnCrit) return;
            PruneExpired(now);
            for (int i = 0; i < _entries.Count; i++)
            {
                ActiveEntry parent = _entries[i];
                var configured = parent.ConfiguredEntry ?? parent.Entry;
                if (configured.Def.Trigger != trigger || !configured.AdmittedSource && configured.Memory != memory
                    || configured.AdmittedSource && filter == null) continue;
                if (filter != null && !filter(configured)) continue;
                var def = transform != null ? transform(configured) : configured.Def;
                if (def == null) continue;
                if (parent.HasFired && def.Cooldown > 0 && now < parent.LastFired + def.Cooldown) continue;
                ActiveEntry state = parent;
                if (configured.AdmittedSource && def.Effect == GimmickEffect.Crescendo)
                {
                    if (!parent.SourceStates.TryGetValue(memory, out state))
                    {
                        parent.SourceStates.Add(memory, state = new ActiveEntry());
                        _activeStates.Add(state);
                    }
                }
                state.Entry = configured.AdmittedSource || def != configured.Def ? new GimmickEntry
                    { StarId = configured.StarId, Memory = configured.AdmittedSource ? memory : configured.Memory,
                        Def = def, ContributorIds = configured.ContributorIds } : configured;
                if (!AcceptV129(state, now, victimId, damage, activationId, memoryCooldown, directDamage, boss, out int targetCount)) continue;
                if ((def.Effect == GimmickEffect.Expose || def.Effect == GimmickEffect.Echo) && victimId == 0) continue;
                if (def.Effect == GimmickEffect.Echo && (!Gimmicks.Finite(damage) || damage <= 0)) continue;
                if (admit != null && !admit(configured)) continue;
                state.HasFired = true;
                state.LastFired = now;
                parent.HasFired = true; parent.LastFired = now;
                if (def.Effect == GimmickEffect.Quicken || def.Effect == GimmickEffect.Empower)
                {
                    state.BuffActive = true;
                    state.BuffUntil = now + Gimmicks.Duration(def, Gimmicks.BuffDuration);
                }
                else if (def.Effect == GimmickEffect.Expose || def.Effect == GimmickEffect.Sap || def.Effect == GimmickEffect.Weakspot)
                {
                    if (state.Victims == null) state.Victims = new List<VictimWindow>();
                    int found = -1;
                    for (int j = 0; j < state.Victims.Count; j++)
                        if (state.Victims[j].VictimId == victimId) { found = j; break; }
                    var window = new VictimWindow { VictimId = victimId, Until = now + Gimmicks.Duration(def, Gimmicks.BuffDuration) };
                    if (found < 0) state.Victims.Add(window);
                    else state.Victims[found] = window;
                }
                results.Add(new GimmickRequest
                {
                    Entry = state.Entry,
                    VictimId = victimId,
                    Damage = Gimmicks.Finite(damage) && damage > 0 ? damage : 0,
                    AreaRadius = def.Effect == GimmickEffect.Burst || def.Effect == GimmickEffect.Element
                        && (trigger == GimmickTrigger.OnUse || trigger == GimmickTrigger.OnKill) ? Gimmicks.Radius(def, Gimmicks.AreaRadius) : 0f,
                    AreaAroundHero = trigger == GimmickTrigger.OnUse
                    ,TargetCount = targetCount,
                    ActivationId = activationId,
                    ElementTypes = Math.Max(0, Math.Min(4, elementTypes))
                });
            }
        }

        public float QuickenPercent(float now) => BuffPercent(GimmickEffect.Quicken, now);
        public float EmpowerPercent(float now) => BuffPercent(GimmickEffect.Empower, now);

        private float BuffPercent(GimmickEffect effect, float now)
        {
            if (!Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            decimal strongest = 0;
            foreach (var state in ActiveStates())
                if (state.BuffActive && state.Entry.Def.Effect == effect)
                    strongest = Math.Max(strongest, state.Entry.Def.EffectiveValueOrAuthored);
            return (float)strongest;
        }

        public float ExposePercent(int victimId, float now)
        {
            if (!_hasExposeEntries || !Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            decimal strongest = 0;
            foreach (ActiveEntry state in ActiveStates())
            {
                if (state.Entry.Def.Effect != GimmickEffect.Expose || state.Victims == null) continue;
                for (int j = 0; j < state.Victims.Count; j++)
                    if (state.Victims[j].VictimId == victimId) strongest = Math.Max(strongest, state.Entry.Def.EffectiveValueOrAuthored);
            }
            return (float)strongest;
        }
    }
}

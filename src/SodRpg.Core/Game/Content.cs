using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>日本語・英語の二言語テキスト。表示言語は Loc.Japanese で切り替える。</summary>
    public sealed class Txt
    {
        public Txt(string ja, string en)
        {
            Ja = ja;
            En = en;
        }

        public string Ja { get; }
        public string En { get; }

        public override string ToString() => Loc.Japanese ? Ja : En;
    }

    public static class Loc
    {
        // プロセス共有の言語設定（既定は日本語）と、スレッドごとの上書きの2層。
        // 実運用では UI スレッドで一度設定するだけなので、set が共有値にも書けば
        // AsyncProfileWriter など設定しない他スレッドは共有値を読んで従来どおり。
        // テストは各スレッドの上書きを読むので、並行実行でも干渉しない（#151: 並列化の妨げだった全体共有状態を解消）。
        private static bool _sharedJapanese = true;
        [ThreadStatic] private static bool? _japanese;

        public static bool Japanese
        {
            get => _japanese ?? _sharedJapanese;
            set { _sharedJapanese = value; _japanese = value; }
        }

        public static string T(string ja, string en) => Japanese ? ja : en;
    }

    /// <summary>特性（ランダムに付く能力値）の定義。値域は アイテムレベル1・レア度補正前。</summary>
    public sealed class AffixDef
    {
        public AffixDef(Stat stat, int min, int max, int weight = 10, Rarity minRarity = Rarity.Common)
        {
            Stat = stat;
            Min = min;
            Max = max;
            Weight = weight;
            MinRarity = minRarity;
        }

        /// <summary>この特性が付く最低のレア度（v1.28：攻撃力%・魔力%はエピック以上）。</summary>
        public Rarity MinRarity { get; }
        public Stat Stat { get; }
        public int Min { get; }
        public int Max { get; }
        public int Weight { get; }
    }

    public sealed class PowerRange
    {
        public PowerRange(Power power, int min, int max)
        {
            Power = power;
            Min = min;
            Max = max;
        }

        public Power Power { get; }
        public int Min { get; }
        public int Max { get; }
    }

    /// <summary>装備の基礎（種類）。固定の基礎能力（implicit）を1つ持つ。</summary>
    public sealed class BaseDef
    {
        public BaseDef(string id, Slot slot, Line line, Txt name, Stat implicitStat, int implicitValue)
            : this(id, slot, line, name, implicitStat, implicitValue, Family.Plain)
        {
        }

        public BaseDef(string id, Slot slot, Line line, Txt name, Stat implicitStat, int implicitValue, Family family)
        {
            Family = family;
            Id = id;
            Slot = slot;
            Line = line;
            Name = name;
            ImplicitStat = implicitStat;
            ImplicitValue = implicitValue;
        }

        public string Id { get; }
        public Slot Slot { get; }
        public Line Line { get; }
        public Txt Name { get; }
        public Stat ImplicitStat { get; }
        public int ImplicitValue { get; }
        /// <summary>家系（v1.32）。特性と固有効果の抽選の傾向だけに効く。Plain は偏りなし。</summary>
        public Family Family { get; }
    }

    /// <summary>固有品。基礎と2つの固有効果が固定で、特性は通常どおり3つ抽選する。</summary>
    public sealed class UniqueDef
    {
        public UniqueDef(string id, string baseId, Txt name, Txt lore, Power p1, int v1, Power p2, int v2)
        {
            Id = id;
            BaseId = baseId;
            Name = name;
            Lore = lore;
            Powers = new[] { new PowerLine(p1, v1), new PowerLine(p2, v2) };
        }

        /// <summary>セット遺物の部位（固有効果を持たない）。</summary>
        public UniqueDef(string id, string baseId, Txt name, string setId)
        {
            Id = id;
            BaseId = baseId;
            Name = name;
            Lore = new Txt("", "");
            SetId = setId;
            Powers = Array.Empty<PowerLine>();
        }

        /// <summary>A boss-exclusive authored move occupies one effect slot, never a generic Power.</summary>
        public UniqueDef(string id, string baseId, Txt name, string setId, string bossMove)
            : this(id, baseId, name, setId)
        {
            if (!BossProfiles.TryGetMove(bossMove, out var profile) || profile.SetId != setId)
                throw new ArgumentException("Unknown or mismatched boss move.", nameof(bossMove));
            BossMove = bossMove;
        }

        public string Id { get; }
        public string BaseId { get; }
        public Txt Name { get; }
        public Txt Lore { get; }
        public IReadOnlyList<PowerLine> Powers { get; }
        /// <summary>セット遺物ならセットID、それ以外は null。</summary>
        public string SetId { get; }
        public string BossMove { get; }
        /// <summary>連携（v1.26）を持つならその定義、それ以外は null。</summary>
        public LinkDef Link { get; set; }
    }

    public sealed class SetLinkStage
    {
        public SetLinkStage(int requiredPieces, LinkDef link)
        {
            RequiredPieces = requiredPieces;
            Link = link;
        }

        public int RequiredPieces { get; }
        public LinkDef Link { get; }
    }

    /// <summary>名前付きのセット装備。2点・3点でボーナス、6部位のセットは6点で追加効果（v1.31）。</summary>
    public sealed class SetDef
    {
        public string Id;
        public Txt Name;
        public StatLine[] TwoPiece;
        public PowerLine[] ThreePiece;
        /// <summary>6つ装着の効果（v1.31）。6部位化が済むまでは null / 空。4つ・5つ装着には効果を付けない。</summary>
        public PowerLine[] SixPiece;
        /// <summary>このセットだけを落とすボスの完全一致型名。通常セットは null。</summary>
        public string BossTypeName;
        public string BossReward;
        private IReadOnlyList<BossSetStage> bossStages = Array.Empty<BossSetStage>();
        public IReadOnlyList<BossSetStage> BossStages
        {
            get => bossStages;
            set
            {
                var stages = value?.ToArray() ?? Array.Empty<BossSetStage>();
                if (stages.Length != 0 && stages.Length != 3)
                    throw new ArgumentException("Boss stages require exactly 2/3/6 pieces.", nameof(value));
                int[] thresholds = { 2, 3, 6 };
                for (int i = 0; i < stages.Length; i++)
                    if (stages[i] == null || stages[i].RequiredPieces != thresholds[i]
                        || !BossProfiles.TryGetMove(stages[i].ProfileId, out var profile) || profile.SetId != Id)
                        throw new ArgumentException("Invalid boss set stage.", nameof(value));
                bossStages = Array.AsReadOnly(stages);
            }
        }
        private SetLinkStage[] linkStages = Array.Empty<SetLinkStage>();
        public SetLinkStage[] LinkStages
        {
            get => linkStages;
            set
            {
                var stages = value ?? Array.Empty<SetLinkStage>();
                if (stages.Length != 0 && stages.Length != 3)
                    throw new ArgumentException("Set links require exactly the 2/4/6-piece stages.", nameof(value));
                LinkDef first = null;
                decimal previous = 0;
                for (int i = 0; i < stages.Length; i++)
                {
                    var stage = stages[i];
                    var link = stage?.Link;
                    if (stage == null || stage.RequiredPieces != (i + 1) * 2 || !Links.Validate(link)
                        || link.Requires.Length != 1 || link.Value <= 0 || link.Value < previous
                        || link.Value > Links.Cap(link.Kind, 1)
                        || (first != null && (link.Kind != first.Kind || link.Requires[0] != first.Requires[0])))
                        throw new ArgumentException("Invalid set link stage.", nameof(value));
                    first = first ?? link;
                    previous = link.Value;
                    if (BossTypeName != null && (link.Kind != LinkKind.BossReward || link.Value != i + 1
                        || !BossProfiles.TryGetReward(BossReward, out var profile) || profile.SetId != Id || profile.Requires != link.Requires[0]))
                        throw new ArgumentException("Boss links must select their native reward profile.", nameof(value));
                }
                linkStages = stages;
            }
        }

        public SetLinkStage SelectLinkStage(int count)
        {
            for (int i = LinkStages.Length - 1; i >= 0; i--)
                if (count >= LinkStages[i].RequiredPieces) return LinkStages[i];
            return null;
        }

        /// <summary>6つ装着の効果を持つか（= 6部位のセットか）。</summary>
        public bool HasSixPiece => BossStages.Count == 3 || SixPiece != null && SixPiece.Length > 0;

        public string Describe()
        {
            string two = string.Join(Loc.T("、", ", "), TwoPiece.Select(s => Content.FormatStat(s.Stat, s.Value)));
            string three = string.Join("\n", ThreePiece.Select(p => Content.FormatPowerBullets(p.Power, p.Value, "　")));
            string text = Loc.T($"2つ装着：{two}\n3つ装着：\n{three}", $"2 pieces: {two}\n3 pieces:\n{three}");
            if (BossStages.Count > 0)
                text = string.Join("\n", BossStages.Select(s => Loc.T($"{s.RequiredPieces}つ装着：\n", $"{s.RequiredPieces} pieces:\n") + EffectLayout.Bullets(BossProfiles.DescribeMove(s.ProfileId), "　")));
            if (SixPiece != null && SixPiece.Length > 0)
            {
                string six = string.Join("\n", SixPiece.Select(p => Content.FormatPowerBullets(p.Power, p.Value, "　")));
                text += Loc.T($"\n6つ装着：\n{six}", $"\n6 pieces:\n{six}");
            }
            if (BossTypeName != null)
                text += Loc.T("\n入手先：対応するボスからのみ", "\nSource: only from the matching boss");
            foreach (var stage in LinkStages)
                text += Loc.T($"\n{stage.RequiredPieces}つ装着の任意連携：\n", $"\nOptional {stage.RequiredPieces}-piece link:\n")
                    + EffectLayout.Bullets(stage.Link.Kind == LinkKind.BossReward ? BossProfiles.DescribeReward(BossReward, (int)stage.Link.Value) : Links.Describe(stage.Link), "　");
            return text;
        }

        public string DescribeLinkProgress(int count, bool satisfied)
        {
            if (LinkStages.Length == 0) return "";
            var stage = SelectLinkStage(count);
            string target = Links.Name(LinkStages[0].Link.Requires[0]).ToString();
            string status = Loc.T(
                $"現在{count}つ装着。対象『{target}』：{(satisfied ? "装着済み" : "未装着")}。",
                $"{count} pieces equipped. Target {target}: {(satisfied ? "equipped" : "not equipped")}.");
            status += stage != null && satisfied
                ? Loc.T($"有効な連携：{stage.RequiredPieces}つ装着段階。", $"Active link: {stage.RequiredPieces}-piece stage.")
                : Loc.T("有効な連携：なし。", "Active link: none.");
            if (stage != null)
                status += Loc.T($"\n選択段階（{stage.RequiredPieces}つ装着）：", $"\nSelected {stage.RequiredPieces}-piece stage: ")
                    + (stage.Link.Kind == LinkKind.BossReward ? BossProfiles.DescribeReward(BossReward, (int)stage.Link.Value) : Links.Describe(stage.Link));
            foreach (var next in LinkStages)
            {
                if (next.RequiredPieces <= count) continue;
                int missing = next.RequiredPieces - count;
                return status + Loc.T($"\nあと{missing}つで{next.RequiredPieces}つ装着段階。", $"\n{missing} more piece(s) to the {next.RequiredPieces}-piece stage.");
            }
            return status + Loc.T("\n最終段階です。", "\nFinal stage reached.");
        }

        /// <summary>いま何点そろっているかと、次に何が起きるか（装着画面用）。</summary>
        public string Progress(int count)
        {
            if (HasSixPiece)
            {
                if (count >= 6) return Loc.T("6つそろっています。すべての効果が有効です。", "All 6 pieces equipped: every bonus is active.");
                if (count >= 3)
                {
                    int left = 6 - count;
                    return Loc.T($"あと{left}つで、6つ装着の効果が加わります。", left == 1 ? "One more piece adds the 6-piece bonus." : $"{left} more pieces add the 6-piece bonus.");
                }
            }
            else if (count >= 3) return Loc.T("3つそろっています。すべての効果が有効です。", "All 3 pieces equipped: every bonus is active.");
            if (count == 2) return Loc.T("あと1つで、3つ装着の効果が加わります。", "One more piece adds the 3-piece bonus.");
            if (count == 1) return Loc.T("あと1つで、2つ装着の効果が有効になります。", "One more piece activates the 2-piece bonus.");
            return Loc.T("あと2つで、2つ装着の効果が有効になります。", "Two more pieces activate the 2-piece bonus.");
        }
    }

    /// <summary>専門化（星図）のノード。小ノードは段階制、到達ノードは刻印として1つだけ有効。</summary>
    public sealed class TalentDef
    {
        public TalentDef(string id, Line route, Txt name, Stat stat, int perRank, int maxRank)
        {
            Id = id;
            Route = route;
            Name = name;
            Stat = stat;
            PerRank = perRank;
            MaxRank = maxRank;
        }

        public TalentDef(string id, Line route, Txt name, Power power, int perRank, int maxRank)
            : this(id, route, name, default(Stat), perRank, maxRank)
        {
            RankPower = power;
        }

        public TalentDef(string id, Line route, Txt name, LinkDef linkPerRank, int maxRank)
            : this(id, route, name, default(Stat), 0, maxRank)
        {
            LinkPerRank = linkPerRank;
        }

        public TalentDef(string id, Line route, Txt name, Power power, int powerValue, Txt description)
        {
            Id = id;
            Route = route;
            Name = name;
            IsKeystone = true;
            Power = power;
            PowerValue = powerValue;
            MaxRank = 1;
            Description = description;
        }

        public string Id { get; }
        public Line Route { get; }
        public Txt Name { get; }
        public Stat Stat { get; }
        public int PerRank { get; }
        public int MaxRank { get; }
        /// <summary>記憶ルート内の識別子と順番。核・夢の輪は RouteId が null。</summary>
        public string RouteId { get; set; }
        public int RouteOrder { get; set; }
        /// <summary>ルートに対応する記憶の型名。</summary>
        public string RouteMemory { get; set; }
        public bool IsDreamRing { get; set; }
        public bool IsOuterAnchor { get; set; }
        public StarClusterDef Cluster { get; set; }
        public ClusterStarDef ClusterStar { get; set; }
        public int ClusterOrder { get; set; }
        public AuthoredStarDef AuthoredStar { get; set; }
        public ScopedModifierDef ScopedModifier { get; set; }
        public EffectChannelDef EffectChannel { get; set; }
        public NativeMemoryModifierDef NativeModifier { get; set; }
        public AuthoredMechanismSpec Mechanism { get; set; }
        public KeystoneDefinition KeystoneDefinition { get; set; }
        /// <summary>v1.32 B：遠征を通して成長する仕組み（重要な星）。なければ null。</summary>
        public RunGrowthDef RunGrowth { get; set; }
        /// <summary>v1.32 B：RunGrowth への修飾（小さな星・選択肢）。なければ null。</summary>
        public RunGrowthModifierDef RunGrowthModifier { get; set; }
        public bool IsChoice => ClusterStar?.Kind == ClusterStarKind.Choice;
        public IReadOnlyList<TalentDef> Choices { get; set; } = Array.Empty<TalentDef>();
        public int GimmickBoost { get; set; }
        public GimmickParam? GimmickParameter { get; set; }
        public int GimmickParamAmount { get; set; }
        /// <summary>1段あたりの連携。能力値とは別に、装着条件をホストで判定する。</summary>
        public LinkDef LinkPerRank { get; set; }
        /// <summary>小ノードが1段ごとに伸ばす固有効果。能力値・連携ノードは None。</summary>
        public Power RankPower { get; set; }
        public bool IsPowerNode => !IsKeystone && LinkPerRank == null && RankPower != Power.None;
        /// <summary>1は核の手前の星、2は奥の星・記憶ルート・夢の輪。</summary>
        public int Tier { get; set; } = 1;
        /// <summary>記憶の仕掛け（v1.28）。ルートの記憶に反応する。なければ null。</summary>
        public GimmickDef Gimmick { get; set; }
        /// <summary>隣接する記憶をつなぐ橋の合わせ技。外側の夢の輪には付かない。</summary>
        public PairComboDef PairCombo => PairCombos.ForBridge(Id);
        /// <summary>1段に要るポイント（ふつうは1。連装・四の型のように強い星だけ高い）。</summary>
        public int RankCost { get; set; } = 1;
        public bool IsKeystone { get; }
        public Power Power { get; }
        public int PowerValue { get; }
        public Txt Description { get; }
        /// <summary>旅人の刻印なら旅人の型名（例：Hero_Vesper）。汎用ノードは null。</summary>
        public string HeroKey { get; set; }

        /// <summary>星図に表示する効果。小ノードは1段あたりの値。</summary>
        public string Describe()
        {
            if (Mechanism != null || KeystoneDefinition != null || IsKeystone || IsChoice)
                return StarMapPresentation.EffectDescription(this);
            string ranks = StarMapPresentation.RanksNote(this);
            if (RunGrowth != null) return DescribeRunGrowth(RunGrowth) + ranks;
            if (RunGrowthModifier != null) return global::SodRpg.Core.Game.RunGrowth.Describe(RunGrowthModifier, HeroKey) + ranks;
            if (PairCombo != null) return PairCombos.Describe(PairCombo);
            string effect = ScopedModifier != null ? FractionalScopedModifiers.Describe(ScopedModifier, HeroKey)
                : NativeModifier != null ? FractionalScopedModifiers.Describe(NativeModifier)
                : GimmickBoost > 0 ? DescribeGimmickBoost(RouteMemory, GimmickBoost, HeroKey)
                : GimmickParameter.HasValue ? DescribeGimmickParameter(RouteMemory, GimmickParameter.Value, GimmickParamAmount, Id, HeroKey)
                : LinkPerRank != null ? Links.Describe(LinkPerRank)
                : IsPowerNode ? Content.FormatPower(RankPower, PerRank)
                : Gimmick != null && PerRank == 0 ? "" : Content.FormatStat(Stat, PerRank);
            string gimmick = Gimmicks.Describe(Gimmick, RouteMemory);
            if (gimmick.Length > 0) effect = effect.Length == 0 ? gimmick : effect + "\n" + gimmick;
            return effect + ranks;
        }

        // The TalentDef.RunGrowth property hides the static type name inside this class.
        private static string DescribeRunGrowth(RunGrowthDef def) => global::SodRpg.Core.Game.RunGrowth.Describe(def);

        internal static string DescribeGimmickBoost(string routeMemory, int amount, string heroKey = null) =>
            DescribeRouteEffects(routeMemory, null, heroKey) + Loc.T($"の量 +{amount}%／", $" amount +{amount}% / ")
            + Links.ItemName(routeMemory) + Loc.T("を装着中、対応する星の追加効果が対象。各効果の元の量に対する増加で、最終上限は各効果に従う",
                " must be equipped; affects matching stars' additional effects. Increases each effect's original amount, subject to its final cap");

        internal static string DescribeGimmickParameter(string routeMemory, GimmickParam parameter, int amount, string id, string heroKey = null)
        {
            string effects = DescribeRouteEffects(routeMemory, parameter, heroKey);
            string field;
            switch (parameter)
            {
                case GimmickParam.Duration: field = Loc.T("持続時間", "duration"); break;
                case GimmickParam.WindowDuration: case GimmickParam.MarkDuration: throw new InvalidOperationException("A bridge gate duration is a typed scoped modifier, not a legacy gimmick parameter: " + id);
                case GimmickParam.Radius: field = Loc.T("範囲の半径", "radius"); break;
                case GimmickParam.ExtraTargets: field = Loc.T("対象数", "target count"); break;
                case GimmickParam.Chance: field = Loc.T("追加の属性付与確率", "extra elemental application chance"); break;
                default: throw new InvalidOperationException("Invalid gimmick parameter for " + id);
            }
            string unit = parameter == GimmickParam.ExtraTargets ? Loc.T("体", " targets")
                : parameter == GimmickParam.Chance ? Loc.T("パーセントポイント", " percentage points") : "%";
            string cap = parameter == GimmickParam.ExtraTargets ? Gimmicks.MaxExtraTargets + Loc.T("体", " targets")
                : parameter == GimmickParam.Chance ? "100%" : Gimmicks.MaxParameterPercent + "%";
            return effects + " " + field + " +" + amount + unit + Loc.T("／", " / ") + Links.ItemName(routeMemory)
                + Loc.T("を装着中、対応する星の追加効果が対象。", " must be equipped; affects matching stars' additional effects. ")
                + (parameter == GimmickParam.ExtraTargets || parameter == GimmickParam.Chance ? ""
                    : Loc.T("元の" + field + "に対する増加。", "Increases the original " + field + ". "))
                + Loc.T("同じ記憶の修飾を合算し、上限" + cap, "Combined modifiers for this memory capped at " + cap);
        }

        private static string DescribeRouteEffects(string routeMemory, GimmickParam? parameter, string heroKey)
        {
            IEnumerable<TalentDef> tree = heroKey == null ? HeroSigils.All : HeroSigils.TreeFor(heroKey);
            var labels = tree.SelectMany(t => t.IsChoice ? t.Choices : new[] { t })
                .Where(t => t.RouteMemory == routeMemory && t.Gimmick != null
                    && (!parameter.HasValue || Gimmicks.SupportsParameter(t.Gimmick, parameter.Value)))
                .Select(t => FractionalScopedModifiers.EffectLabel(t.Gimmick)).Distinct();
            string effects = string.Join(Loc.T("・", ", "), labels);
            if (effects.Length == 0) throw new InvalidOperationException("Cannot resolve route effect description: " + routeMemory);
            return effects;
        }
    }

    /// <summary>
    /// ゲーム内容の定義一式。数値は計画書 付録A・B の設計値を出発点に、
    /// 本MODで遊べる形へ丸めたもの（固有名はすべて仮称）。
    /// </summary>
    public static class Content
    {
        public const int SlotCount = 6;
        public static readonly IReadOnlyList<Slot> SlotOrder = new[]
        {
            Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm,
        };

        public const int MaxItemLevel = GearBalance.MaxItemLevel;
        public const int ItemLevelScalingCap = GearBalance.ItemLevelScalingCap;
        public const int StashCapacity = 80;
        public const int SatchelCapacity = 30;
        public const int LostAndFoundCapacity = 10;
        public const int MaxHeat = 5;
        public const int MaxEnhance = ForgeBalance.BaseCap;
        public const int MaxRetunes = ForgeBalance.MaxRetunes;
        public const int MaxDreamLevel = 30;
        public const int KeystoneRouteRequirement = 6;
        public const int KeystoneCost = 3;
        public const int RoomsToRecoverLost = 3;
        public const int CodexPerPoint = 6;
        public const int MaxCodexBonus = 4;
        /// <summary>強化の節目。保存に記録する節目の序数とは独立した調整値。</summary>
        public const int EnhanceMilestoneFirst = ForgeBalance.MilestoneFirst;
        public const int EnhanceMilestoneSecond = ForgeBalance.MilestoneSecond;
        public const int EnhanceStepPerBreak = ForgeBalance.StepPerBreak;
        public const int EnhanceMilestoneThird = ForgeBalance.MilestoneThird;
        public const int EnhanceMilestoneFourth = ForgeBalance.MilestoneFourth;
        public const int EnhanceMilestoneFifth = ForgeBalance.MilestoneFifth;
        /// <summary>保存される強化節目の序数の数（調整値ではない）。</summary>
        public const int MaxEnhanceMilestones = 5;
        /// <summary>最後の強化節目で固有効果に掛ける倍率（%）。</summary>
        public const int LimitBreakPowerPct = ForgeBalance.MilestonePowerPercent;
        public const int RetuneChoices = ForgeBalance.RetuneChoices;
        public const int TransmuteTargetCostPct = ForgeBalance.SynthesisTargetCostPercent;
        public static int TransmuteInputs(Rarity r)
            => ForgeBalance.SynthesisInputs[r == Rarity.Common ? 0 : r == Rarity.Uncommon ? 1 : r == Rarity.Rare ? 2 : 3];
        public const int MaxAwakenLevel = ForgeBalance.MaxAwakenLevel;
        /// <summary>v1.26 までに覚醒した遺物が入る段（倍率が当時と同じ）。</summary>
        public const int LegacyAwakenLevel = 2;
        /// <summary>覚醒の力の上限（最後の段に要る累計）。</summary>
        public static int AwakenThreshold => ForgeBalance.AwakenThresholds[MaxAwakenLevel];

        private static int AwakenClamp(int level) => Math.Max(0, Math.Min(MaxAwakenLevel, level));
        /// <summary>その段に上がるのに要る覚醒の力（累計）。</summary>
        public static int AwakenThresholdFor(int level) => ForgeBalance.AwakenThresholds[AwakenClamp(level)];
        /// <summary>その段の固有効果（と連携）の倍率（%）。</summary>
        public static int AwakenPowerPctAt(int level) => ForgeBalance.AwakenPowerPercents[AwakenClamp(level)];
        /// <summary>その段の特性の倍率（%）。</summary>
        public static int AwakenAffixPctAt(int level) => ForgeBalance.AwakenAffixPercents[AwakenClamp(level)];
        /// <summary>覚醒の力の累計から、届いている段。</summary>
        public static int AwakenLevelFor(int points)
        {
            int level = 0;
            while (level < MaxAwakenLevel && points >= ForgeBalance.AwakenThresholds[level + 1]) level++;
            return level;
        }
        public static string AwakenNumeral(int level) => level == 1 ? "Ⅰ" : level == 2 ? "Ⅱ" : level == 3 ? "Ⅲ" : "";

        /// <summary>v1.28 までの土台の数。v1.29 の2段目で新土台にも固有品が付くまでの判定に使う（GearVolumeV121Tests）。</summary>
        public const int PreV129BaseCount = 180;

        public static readonly IReadOnlyList<BaseDef> Bases = new[]
        {
            new BaseDef("weapon.chain_sword", Slot.Weapon, Line.Offense, new Txt("連なりの剣", "Chain Sword"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_chain_sword),
            new BaseDef("weapon.twin_fang", Slot.Weapon, Line.Offense, new Txt("双牙の短刀", "Twin Fang Daggers"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_weapon_twin_fang, Family.Gale),
            new BaseDef("weapon.calming_staff", Slot.Weapon, Line.Resonance, new Txt("鎮めの杖", "Calming Staff"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_calming_staff, Family.Mend),
            new BaseDef("weapon.longspike_bow", Slot.Weapon, Line.Offense, new Txt("長穂の弓", "Longspike Bow"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_longspike_bow, Family.Flame),
            new BaseDef("weapon.shield_maul", Slot.Weapon, Line.Guard, new Txt("盾打ちの槌", "Shieldbash Maul"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_weapon_shield_maul, Family.Guard),
            new BaseDef("weapon.blaze_greatsword", Slot.Weapon, Line.Offense, new Txt("烈火の大剣", "Blazing Greatsword"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_blaze_greatsword, Family.Flame),

            new BaseDef("armor.counter_gauntlets", Slot.Armor, Line.Guard, new Txt("反撃の籠手", "Counter Gauntlets"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_counter_gauntlets, Family.Guard),
            new BaseDef("armor.flowing_cloak", Slot.Armor, Line.Resonance, new Txt("流れの外套", "Flowing Cloak"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_armor_flowing_cloak, Family.Gale),
            new BaseDef("armor.guardian_plate", Slot.Armor, Line.Guard, new Txt("護りの胸当て", "Guardian Plate"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_guardian_plate, Family.Guard),
            new BaseDef("armor.resonant_robe", Slot.Armor, Line.Resonance, new Txt("共鳴の衣", "Resonant Robe"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_resonant_robe, Family.Memory),
            new BaseDef("armor.thorn_mail", Slot.Armor, Line.Guard, new Txt("棘の鎧", "Thorn Mail"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_armor_thorn_mail, Family.Guard),
            new BaseDef("armor.lampkeeper_mantle", Slot.Armor, Line.Resonance, new Txt("灯守の外衣", "Lampkeeper's Mantle"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_lampkeeper_mantle, Family.Mend),

            new BaseDef("charm.resonance_amulet", Slot.Charm, Line.Resonance, new Txt("共鳴の護符", "Resonance Amulet"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_resonance_amulet, Family.Light),
            new BaseDef("charm.tailwind_ring", Slot.Charm, Line.Resonance, new Txt("追い風の指輪", "Tailwind Ring"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_charm_tailwind_ring, Family.Gale),
            new BaseDef("charm.hunters_seal", Slot.Charm, Line.Offense, new Txt("狩人の印章", "Hunter's Seal"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_charm_hunters_seal),
            new BaseDef("charm.pulsing_core", Slot.Charm, Line.Guard, new Txt("脈打つ核", "Pulsing Core"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_charm_pulsing_core, Family.Guard),
            new BaseDef("charm.chain_necklace", Slot.Charm, Line.Guard, new Txt("鎖の首飾り", "Chain Necklace"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_charm_chain_necklace, Family.Guard),
            new BaseDef("charm.old_clock", Slot.Charm, Line.Offense, new Txt("古き時計", "Old Clock"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_old_clock, Family.Memory),
            new BaseDef("weapon.frost_spear", Slot.Weapon, Line.Guard, new Txt("霜穂の槍", "Frostspike Spear"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_weapon_frost_spear, Family.Frost),
            new BaseDef("weapon.dusk_scythe", Slot.Weapon, Line.Offense, new Txt("黄昏の大鎌", "Dusk Scythe"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_dusk_scythe, Family.Dark),
            new BaseDef("weapon.lantern_rod", Slot.Weapon, Line.Resonance, new Txt("灯火の杖", "Lantern Rod"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_weapon_lantern_rod, Family.Light),
            new BaseDef("armor.frost_coat", Slot.Armor, Line.Guard, new Txt("霜の上衣", "Frostweave Coat"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_armor_frost_coat, Family.Frost),
            new BaseDef("armor.dancer_garb", Slot.Armor, Line.Offense, new Txt("舞手の衣", "Dancer's Garb"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_armor_dancer_garb, Family.Flame),
            new BaseDef("armor.star_cloak", Slot.Armor, Line.Resonance, new Txt("星読みの外套", "Stargazer's Cloak"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_star_cloak, Family.Memory),
            new BaseDef("charm.ember_locket", Slot.Charm, Line.Offense, new Txt("残り火のロケット", "Ember Locket"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_charm_ember_locket, Family.Flame),
            new BaseDef("charm.moon_bell", Slot.Charm, Line.Resonance, new Txt("月の鈴", "Moon Bell"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_charm_moon_bell, Family.Frost),
            new BaseDef("charm.iron_feather", Slot.Charm, Line.Guard, new Txt("鉄の羽根", "Iron Feather"), Stat.Armor, EquipmentItemsBalanceValues.Base_charm_iron_feather, Family.Guard),
            new BaseDef("weapon.hunting_bow", Slot.Weapon, Line.Offense, new Txt("狩人の短弓", "Hunting Shortbow"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_hunting_bow),
            new BaseDef("weapon.war_axe", Slot.Weapon, Line.Offense, new Txt("戦斧", "War Axe"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_war_axe),
            new BaseDef("weapon.tower_lance", Slot.Weapon, Line.Guard, new Txt("城壁の槍", "Rampart Lance"), Stat.Armor, EquipmentItemsBalanceValues.Base_weapon_tower_lance, Family.Guard),
            new BaseDef("weapon.oath_mace", Slot.Weapon, Line.Guard, new Txt("誓いの戦棍", "Oath Mace"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_weapon_oath_mace, Family.Guard),
            new BaseDef("weapon.dream_wand", Slot.Weapon, Line.Resonance, new Txt("夢見の杖", "Dreamer's Wand"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_dream_wand, Family.Memory),
            new BaseDef("weapon.star_harp", Slot.Weapon, Line.Resonance, new Txt("星の竪琴", "Star Harp"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_star_harp, Family.Summon),
            new BaseDef("armor.hunter_leather", Slot.Armor, Line.Offense, new Txt("狩人の革鎧", "Hunter's Leathers"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_armor_hunter_leather),
            new BaseDef("armor.ember_plate", Slot.Armor, Line.Offense, new Txt("燠火の鎧", "Emberforged Plate"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_armor_ember_plate, Family.Flame),
            new BaseDef("armor.root_mail", Slot.Armor, Line.Guard, new Txt("根の鎖帷子", "Rootbound Mail"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_root_mail, Family.Memory),
            new BaseDef("armor.bastion_shell", Slot.Armor, Line.Guard, new Txt("甲羅の鎧", "Shell Armor"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_armor_bastion_shell, Family.Guard),
            new BaseDef("armor.mist_robe", Slot.Armor, Line.Resonance, new Txt("霧の法衣", "Mist Robe"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_armor_mist_robe, Family.Memory),
            new BaseDef("armor.prayer_shawl", Slot.Armor, Line.Resonance, new Txt("祈りの肩掛け", "Prayer Shawl"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_prayer_shawl, Family.Light),
            new BaseDef("charm.fang_necklace", Slot.Charm, Line.Offense, new Txt("牙の首飾り", "Fang Necklace"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_fang_necklace),
            new BaseDef("charm.war_drum", Slot.Charm, Line.Offense, new Txt("戦太鼓", "War Drum"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_charm_war_drum, Family.Flame),
            new BaseDef("charm.guardian_seal", Slot.Charm, Line.Guard, new Txt("守護の封印", "Guardian Seal"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_charm_guardian_seal, Family.Guard),
            new BaseDef("charm.stone_heart", Slot.Charm, Line.Guard, new Txt("石の心臓", "Stone Heart"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_charm_stone_heart, Family.Guard),
            new BaseDef("charm.dream_lens", Slot.Charm, Line.Resonance, new Txt("夢見の水晶", "Dreaming Lens"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_dream_lens, Family.Memory),
            new BaseDef("charm.shadow_mask", Slot.Charm, Line.Resonance, new Txt("影の仮面", "Shadow Mask"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_charm_shadow_mask, Family.Dark),
            new BaseDef("head.iron_helm", Slot.Head, Line.Guard, new Txt("鉄の兜", "Iron Helm"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_iron_helm, Family.Flame),
            new BaseDef("head.dream_circlet", Slot.Head, Line.Resonance, new Txt("夢見の額冠", "Dreamer's Circlet"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_dream_circlet, Family.Memory),
            new BaseDef("head.hunter_hood", Slot.Head, Line.Offense, new Txt("狩人の頭巾", "Hunter's Hood"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_head_hunter_hood),
            new BaseDef("head.horned_helm", Slot.Head, Line.Offense, new Txt("双角の兜", "Horned Helm"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_horned_helm, Family.Summon),
            new BaseDef("head.sage_hat", Slot.Head, Line.Resonance, new Txt("賢者のとんがり帽", "Sage's Hat"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_head_sage_hat),
            new BaseDef("head.mist_veil", Slot.Head, Line.Resonance, new Txt("霧のヴェール", "Veil of Mist"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_head_mist_veil, Family.Gale),
            new BaseDef("head.warden_visor", Slot.Head, Line.Guard, new Txt("番人の面頬", "Warden's Visor"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_head_warden_visor, Family.Guard),
            new BaseDef("head.ember_crown", Slot.Head, Line.Offense, new Txt("残り火の冠", "Ember Crown"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_head_ember_crown, Family.Light),
            new BaseDef("head.moon_hood", Slot.Head, Line.Resonance, new Txt("月影の頭巾", "Moonshadow Hood"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_moon_hood, Family.Frost),
            new BaseDef("head.healer_band", Slot.Head, Line.Guard, new Txt("癒し手の鉢巻", "Healer's Band"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_head_healer_band, Family.Mend),
            new BaseDef("hands.leather_gloves", Slot.Hands, Line.Offense, new Txt("革の手袋", "Leather Gloves"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_leather_gloves),
            new BaseDef("hands.iron_gauntlets", Slot.Hands, Line.Guard, new Txt("鉄の籠手", "Iron Gauntlets"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_iron_gauntlets),
            new BaseDef("hands.archer_bracers", Slot.Hands, Line.Offense, new Txt("射手の腕当て", "Archer's Bracers"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_archer_bracers),
            new BaseDef("hands.spell_gloves", Slot.Hands, Line.Resonance, new Txt("呪文の手袋", "Spellweave Gloves"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_hands_spell_gloves, Family.Memory),
            new BaseDef("hands.claw_gauntlets", Slot.Hands, Line.Offense, new Txt("獣爪の手甲", "Beastclaw Gauntlets"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_claw_gauntlets, Family.Summon),
            new BaseDef("hands.frost_mitts", Slot.Hands, Line.Resonance, new Txt("霜の指なし手袋", "Frost Mitts"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_hands_frost_mitts, Family.Frost),
            new BaseDef("hands.radiant_wraps", Slot.Hands, Line.Resonance, new Txt("光の手巻き", "Radiant Wraps"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_hands_radiant_wraps, Family.Frost),
            new BaseDef("hands.vigor_grips", Slot.Hands, Line.Guard, new Txt("活力の握り", "Grips of Vigor"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_hands_vigor_grips, Family.Frost),
            new BaseDef("hands.quick_fingers", Slot.Hands, Line.Resonance, new Txt("早業の手袋", "Quickfinger Gloves"), Stat.Haste, EquipmentItemsBalanceValues.Base_hands_quick_fingers, Family.Gale),
            new BaseDef("hands.reach_bracers", Slot.Hands, Line.Offense, new Txt("遠手の腕輪", "Bracers of Reach"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_hands_reach_bracers),
            new BaseDef("feet.travel_boots", Slot.Feet, Line.Resonance, new Txt("旅の長靴", "Traveler's Boots"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_travel_boots),
            new BaseDef("feet.iron_greaves", Slot.Feet, Line.Guard, new Txt("鉄の脛当て", "Iron Greaves"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_iron_greaves, Family.Guard),
            new BaseDef("feet.dancer_shoes", Slot.Feet, Line.Offense, new Txt("舞い手の靴", "Dancer's Shoes"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_feet_dancer_shoes, Family.Gale),
            new BaseDef("feet.wind_sandals", Slot.Feet, Line.Resonance, new Txt("風のサンダル", "Wind Sandals"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_wind_sandals, Family.Gale),
            new BaseDef("feet.stalker_boots", Slot.Feet, Line.Offense, new Txt("忍び足の靴", "Stalker's Boots"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_feet_stalker_boots, Family.Dark),
            new BaseDef("feet.rooted_boots", Slot.Feet, Line.Guard, new Txt("根を張る靴", "Rooted Boots"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_feet_rooted_boots, Family.Frost),
            new BaseDef("feet.ember_treads", Slot.Feet, Line.Offense, new Txt("残り火の足甲", "Ember Treads"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_feet_ember_treads, Family.Dark),
            new BaseDef("feet.pilgrim_boots", Slot.Feet, Line.Guard, new Txt("巡礼の靴", "Pilgrim's Boots"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_feet_pilgrim_boots, Family.Mend),
            new BaseDef("feet.stone_boots", Slot.Feet, Line.Guard, new Txt("岩の重靴", "Stone Boots"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_feet_stone_boots),
            new BaseDef("feet.star_steps", Slot.Feet, Line.Resonance, new Txt("星渡りの靴", "Starstep Boots"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_feet_star_steps, Family.Memory),
            new BaseDef("head.thorn_circlet", Slot.Head, Line.Guard, new Txt("茨の冠", "Thorn Circlet"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_head_thorn_circlet, Family.Guard),
            new BaseDef("head.frost_helm", Slot.Head, Line.Resonance, new Txt("霜の兜", "Frost Helm"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_head_frost_helm, Family.Frost),
            new BaseDef("head.radiant_halo", Slot.Head, Line.Resonance, new Txt("光輪", "Radiant Halo"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_head_radiant_halo, Family.Light),
            new BaseDef("head.scout_goggles", Slot.Head, Line.Offense, new Txt("斥候の遠眼鏡", "Scout's Goggles"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_head_scout_goggles, Family.Gale),
            new BaseDef("head.berserker_mask", Slot.Head, Line.Offense, new Txt("狂戦士の面", "Berserker's Mask"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_berserker_mask),
            new BaseDef("hands.ember_gauntlets", Slot.Hands, Line.Offense, new Txt("残り火の手甲", "Ember Gauntlets"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_hands_ember_gauntlets, Family.Flame),
            new BaseDef("hands.shadow_gloves", Slot.Hands, Line.Offense, new Txt("影縫いの手袋", "Shadowstitch Gloves"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_hands_shadow_gloves, Family.Dark),
            new BaseDef("hands.healer_hands", Slot.Hands, Line.Guard, new Txt("癒し手の手袋", "Healer's Gloves"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_hands_healer_hands, Family.Mend),
            new BaseDef("hands.stone_fists", Slot.Hands, Line.Guard, new Txt("岩の拳", "Stone Fists"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_hands_stone_fists, Family.Guard),
            new BaseDef("hands.duelist_gloves", Slot.Hands, Line.Offense, new Txt("決闘者の手袋", "Duelist's Gloves"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_duelist_gloves),
            new BaseDef("feet.frost_boots", Slot.Feet, Line.Resonance, new Txt("氷上の靴", "Ice Skimmers"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_feet_frost_boots, Family.Frost),
            new BaseDef("feet.shadow_slippers", Slot.Feet, Line.Offense, new Txt("影の上履き", "Shadow Slippers"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_feet_shadow_slippers, Family.Flame),
            new BaseDef("feet.spiked_boots", Slot.Feet, Line.Offense, new Txt("棘付きの長靴", "Spiked Boots"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_spiked_boots),
            new BaseDef("feet.sage_slippers", Slot.Feet, Line.Resonance, new Txt("賢者の室内履き", "Sage's Slippers"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_feet_sage_slippers, Family.Light),
            new BaseDef("feet.guard_sabatons", Slot.Feet, Line.Guard, new Txt("守りの鉄靴", "Guardian Sabatons"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_feet_guard_sabatons, Family.Mend),

            // v1.21：各枠 +10
            new BaseDef("weapon.moon_sickle", Slot.Weapon, Line.Resonance, new Txt("月鎌", "Moon Sickle"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_weapon_moon_sickle, Family.Dark),
            new BaseDef("weapon.ember_whip", Slot.Weapon, Line.Offense, new Txt("残り火の鞭", "Ember Whip"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_weapon_ember_whip, Family.Flame),
            new BaseDef("weapon.glacier_spear", Slot.Weapon, Line.Resonance, new Txt("凍て穂の槍", "Rimefrost Spear"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_weapon_glacier_spear, Family.Frost),
            new BaseDef("weapon.dawn_scepter", Slot.Weapon, Line.Resonance, new Txt("暁の笏", "Dawn Scepter"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_weapon_dawn_scepter, Family.Light),
            new BaseDef("weapon.iron_halberd", Slot.Weapon, Line.Guard, new Txt("鉄の斧槍", "Iron Halberd"), Stat.Armor, EquipmentItemsBalanceValues.Base_weapon_iron_halberd, Family.Guard),
            new BaseDef("weapon.dream_tome", Slot.Weapon, Line.Resonance, new Txt("夢綴じの書", "Dreambound Tome"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_dream_tome, Family.Memory),
            new BaseDef("weapon.bone_cleaver", Slot.Weapon, Line.Offense, new Txt("骨断ちの大包丁", "Bone Cleaver"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_bone_cleaver),
            new BaseDef("weapon.twin_rapier", Slot.Weapon, Line.Offense, new Txt("双子の細剣", "Twin Rapiers"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_twin_rapier, Family.Dark),
            new BaseDef("weapon.pilgrim_staff", Slot.Weapon, Line.Guard, new Txt("巡礼の錫杖", "Pilgrim's Staff"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_weapon_pilgrim_staff, Family.Mend),
            new BaseDef("weapon.thunder_hammer", Slot.Weapon, Line.Offense, new Txt("雷鳴の戦鎚", "Thunder Hammer"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_thunder_hammer, Family.Gale),
            new BaseDef("head.wolf_pelt", Slot.Head, Line.Offense, new Txt("狼の毛皮かぶり", "Wolf Pelt Hood"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_wolf_pelt, Family.Summon),
            new BaseDef("head.star_diadem", Slot.Head, Line.Resonance, new Txt("星の髪飾り", "Star Diadem"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_head_star_diadem, Family.Light),
            new BaseDef("head.plague_mask", Slot.Head, Line.Guard, new Txt("鳥嘴の面", "Beaked Mask"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_head_plague_mask, Family.Dark),
            new BaseDef("head.coral_crown", Slot.Head, Line.Resonance, new Txt("珊瑚の冠", "Coral Crown"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_head_coral_crown, Family.Summon),
            new BaseDef("head.knight_helm", Slot.Head, Line.Guard, new Txt("騎士の大兜", "Knight's Greathelm"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_head_knight_helm, Family.Guard),
            new BaseDef("head.ash_hood", Slot.Head, Line.Offense, new Txt("灰かぶりの頭巾", "Ashen Hood"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_head_ash_hood),
            new BaseDef("head.eye_patch", Slot.Head, Line.Offense, new Txt("片目の眼帯", "Marksman's Eyepatch"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_head_eye_patch, Family.Dark),
            new BaseDef("head.leaf_wreath", Slot.Head, Line.Guard, new Txt("若葉の花冠", "Leaf Wreath"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_head_leaf_wreath, Family.Summon),
            new BaseDef("head.void_helm", Slot.Head, Line.Offense, new Txt("虚ろの兜", "Hollow Helm"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_void_helm, Family.Summon),
            new BaseDef("head.sun_mask", Slot.Head, Line.Resonance, new Txt("日輪の面", "Sunwheel Mask"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_head_sun_mask, Family.Flame),
            new BaseDef("armor.ember_cuirass", Slot.Armor, Line.Offense, new Txt("残り火の胴鎧", "Ember Cuirass"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_armor_ember_cuirass, Family.Flame),
            new BaseDef("armor.frost_robe", Slot.Armor, Line.Resonance, new Txt("霜の法衣", "Frost Robe"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_armor_frost_robe, Family.Frost),
            new BaseDef("armor.scale_coat", Slot.Armor, Line.Guard, new Txt("竜鱗の外套", "Dragonscale Coat"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_scale_coat, Family.Flame),
            new BaseDef("armor.hunter_vest", Slot.Armor, Line.Offense, new Txt("狩人の胴衣", "Hunter's Vest"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_armor_hunter_vest, Family.Dark),
            new BaseDef("armor.star_mantle", Slot.Armor, Line.Resonance, new Txt("星屑の肩掛け", "Stardust Mantle"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_star_mantle, Family.Summon),
            new BaseDef("armor.monk_garb", Slot.Armor, Line.Offense, new Txt("修行者の道着", "Ascetic's Garb"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_armor_monk_garb, Family.Mend),
            new BaseDef("armor.shadow_cloak", Slot.Armor, Line.Offense, new Txt("影織りの外套", "Shadowweave Cloak"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_armor_shadow_cloak, Family.Dark),
            new BaseDef("armor.sun_plate", Slot.Armor, Line.Resonance, new Txt("陽光の鎧", "Sunlit Plate"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_armor_sun_plate, Family.Light),
            new BaseDef("armor.bark_mail", Slot.Armor, Line.Guard, new Txt("樹皮の鎧", "Barkmail"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_bark_mail, Family.Memory),
            new BaseDef("armor.traveler_coat", Slot.Armor, Line.Resonance, new Txt("旅人の外套", "Traveler's Coat"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_traveler_coat, Family.Gale),
            new BaseDef("hands.thorn_wraps", Slot.Hands, Line.Guard, new Txt("茨の手巻き", "Thorn Wraps"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_thorn_wraps, Family.Guard),
            new BaseDef("hands.alchemist_gloves", Slot.Hands, Line.Resonance, new Txt("錬金術師の手袋", "Alchemist's Gloves"), Stat.Haste, EquipmentItemsBalanceValues.Base_hands_alchemist_gloves, Family.Mend),
            new BaseDef("hands.bone_knuckles", Slot.Hands, Line.Offense, new Txt("骨の拳当て", "Bone Knuckles"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_bone_knuckles, Family.Summon),
            new BaseDef("hands.star_rings", Slot.Hands, Line.Resonance, new Txt("星環の指輪", "Star-Ring Bands"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_hands_star_rings, Family.Memory),
            new BaseDef("hands.oath_gauntlets", Slot.Hands, Line.Guard, new Txt("誓いの籠手", "Oath Gauntlets"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_hands_oath_gauntlets, Family.Light),
            new BaseDef("hands.tide_gloves", Slot.Hands, Line.Resonance, new Txt("潮の手袋", "Tide Gloves"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_hands_tide_gloves, Family.Light),
            new BaseDef("hands.flame_grips", Slot.Hands, Line.Offense, new Txt("炎の握り", "Flame Grips"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_hands_flame_grips, Family.Dark),
            new BaseDef("hands.void_claws", Slot.Hands, Line.Offense, new Txt("虚ろの爪", "Hollow Claws"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_hands_void_claws, Family.Memory),
            new BaseDef("hands.sling_bracers", Slot.Hands, Line.Offense, new Txt("投げ手の腕輪", "Thrower's Bracers"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_hands_sling_bracers, Family.Gale),
            new BaseDef("hands.prayer_beads", Slot.Hands, Line.Guard, new Txt("祈りの数珠", "Prayer Beads"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_hands_prayer_beads, Family.Mend),
            new BaseDef("feet.wolf_boots", Slot.Feet, Line.Offense, new Txt("狼の毛皮靴", "Wolfskin Boots"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_feet_wolf_boots, Family.Summon),
            new BaseDef("feet.tide_sandals", Slot.Feet, Line.Resonance, new Txt("潮のサンダル", "Tide Sandals"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_feet_tide_sandals, Family.Flame),
            new BaseDef("feet.knight_sabatons", Slot.Feet, Line.Guard, new Txt("騎士の鉄靴", "Knight's Sabatons"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_knight_sabatons, Family.Light),
            new BaseDef("feet.dawn_steps", Slot.Feet, Line.Resonance, new Txt("暁の足取り", "Dawnsteps"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_feet_dawn_steps, Family.Flame),
            new BaseDef("feet.ash_boots", Slot.Feet, Line.Offense, new Txt("灰踏みの靴", "Ashwalker Boots"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_feet_ash_boots),
            new BaseDef("feet.root_sandals", Slot.Feet, Line.Guard, new Txt("根のサンダル", "Root Sandals"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_feet_root_sandals, Family.Memory),
            new BaseDef("feet.mist_shoes", Slot.Feet, Line.Resonance, new Txt("霧の靴", "Mist Shoes"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_mist_shoes, Family.Dark),
            new BaseDef("feet.hunter_boots", Slot.Feet, Line.Offense, new Txt("追跡者の長靴", "Tracker's Boots"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_feet_hunter_boots),
            new BaseDef("feet.iron_clogs", Slot.Feet, Line.Guard, new Txt("鉄の木靴", "Iron Clogs"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_feet_iron_clogs),
            new BaseDef("feet.star_slippers", Slot.Feet, Line.Resonance, new Txt("星座の上履き", "Constellation Slippers"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_star_slippers, Family.Memory),
            new BaseDef("charm.hearthstone", Slot.Charm, Line.Offense, new Txt("炉端の石", "Hearthstone"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_charm_hearthstone, Family.Mend),
            new BaseDef("charm.frost_pendant", Slot.Charm, Line.Resonance, new Txt("霜の首飾り", "Frost Pendant"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_charm_frost_pendant, Family.Frost),
            new BaseDef("charm.sun_brooch", Slot.Charm, Line.Resonance, new Txt("陽光のブローチ", "Sun Brooch"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_charm_sun_brooch, Family.Light),
            new BaseDef("charm.shadow_ring", Slot.Charm, Line.Offense, new Txt("影の指輪", "Shadow Ring"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_charm_shadow_ring, Family.Dark),
            new BaseDef("charm.hunter_tooth", Slot.Charm, Line.Offense, new Txt("獲物の牙飾り", "Hunter's Fang"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_hunter_tooth, Family.Summon),
            new BaseDef("charm.oak_amulet", Slot.Charm, Line.Guard, new Txt("樫の護符", "Oak Amulet"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_charm_oak_amulet, Family.Mend),
            new BaseDef("charm.clockwork_charm", Slot.Charm, Line.Resonance, new Txt("ぜんまい仕掛けの飾り", "Clockwork Charm"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_clockwork_charm, Family.Memory),
            new BaseDef("charm.iron_seal", Slot.Charm, Line.Guard, new Txt("鉄の印章", "Iron Seal"), Stat.Armor, EquipmentItemsBalanceValues.Base_charm_iron_seal),
            new BaseDef("charm.feather_token", Slot.Charm, Line.Resonance, new Txt("風切り羽", "Windfeather Token"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_charm_feather_token, Family.Memory),
            new BaseDef("charm.war_horn", Slot.Charm, Line.Offense, new Txt("戦の角笛", "War Horn"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_war_horn),

            // v1.25：各枠 +5
            new BaseDef("weapon.storm_glaive", Slot.Weapon, Line.Offense, new Txt("嵐の薙刀", "Storm Glaive"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_storm_glaive, Family.Summon),
            new BaseDef("weapon.bone_flute", Slot.Weapon, Line.Resonance, new Txt("骨の笛", "Bone Flute"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_bone_flute, Family.Summon),
            new BaseDef("weapon.ember_katar", Slot.Weapon, Line.Offense, new Txt("熾火の短刃", "Ember Katar"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_weapon_ember_katar, Family.Dark),
            new BaseDef("weapon.tide_trident", Slot.Weapon, Line.Resonance, new Txt("潮騒の三叉槍", "Tidal Trident"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_weapon_tide_trident, Family.Summon),
            new BaseDef("weapon.vow_mace", Slot.Weapon, Line.Guard, new Txt("誓約の鎚矛", "Vow Mace"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_weapon_vow_mace, Family.Mend),
            new BaseDef("head.raven_mask", Slot.Head, Line.Offense, new Txt("鴉の面", "Raven Mask"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_raven_mask, Family.Dark),
            new BaseDef("head.lantern_hat", Slot.Head, Line.Resonance, new Txt("灯籠の笠", "Lantern Hat"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_head_lantern_hat, Family.Memory),
            new BaseDef("head.iron_coif", Slot.Head, Line.Guard, new Txt("鎖頭巾", "Chain Coif"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_iron_coif),
            new BaseDef("head.dream_veil", Slot.Head, Line.Resonance, new Txt("夢見の薄衣", "Dreamer's Veil"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_dream_veil, Family.Mend),
            new BaseDef("head.antler_crown", Slot.Head, Line.Offense, new Txt("大角の冠", "Antler Crown"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_antler_crown, Family.Flame),
            new BaseDef("armor.ink_robe", Slot.Armor, Line.Resonance, new Txt("墨染めの衣", "Ink-Dyed Robe"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_ink_robe, Family.Dark),
            new BaseDef("armor.chain_hauberk", Slot.Armor, Line.Guard, new Txt("鎖帷子", "Chain Hauberk"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_chain_hauberk),
            new BaseDef("armor.ember_jacket", Slot.Armor, Line.Offense, new Txt("火の粉の革衣", "Cinder Jacket"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_armor_ember_jacket),
            new BaseDef("armor.moon_silk", Slot.Armor, Line.Resonance, new Txt("月絹の衣", "Moonsilk Robe"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_armor_moon_silk, Family.Memory),
            new BaseDef("armor.spiked_plate", Slot.Armor, Line.Guard, new Txt("棘付きの板金鎧", "Spiked Plate"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_armor_spiked_plate),
            new BaseDef("hands.ice_bracers", Slot.Hands, Line.Resonance, new Txt("氷の腕輪", "Ice Bracers"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_hands_ice_bracers, Family.Mend),
            new BaseDef("hands.chain_wraps", Slot.Hands, Line.Offense, new Txt("鎖巻きの拳", "Chain-Wrapped Fists"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_chain_wraps, Family.Flame),
            new BaseDef("hands.sun_gauntlets", Slot.Hands, Line.Guard, new Txt("陽光の籠手", "Sunlit Gauntlets"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_hands_sun_gauntlets, Family.Light),
            new BaseDef("hands.thief_gloves", Slot.Hands, Line.Offense, new Txt("盗賊の手袋", "Thief's Gloves"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_thief_gloves, Family.Dark),
            new BaseDef("hands.monk_wraps", Slot.Hands, Line.Guard, new Txt("修行者の手巻き", "Ascetic's Wraps"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_hands_monk_wraps, Family.Gale),
            new BaseDef("feet.raven_boots", Slot.Feet, Line.Offense, new Txt("鴉羽の長靴", "Raven-Feather Boots"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_feet_raven_boots, Family.Memory),
            new BaseDef("feet.sun_sandals", Slot.Feet, Line.Resonance, new Txt("陽だまりのサンダル", "Sunwarm Sandals"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_feet_sun_sandals, Family.Light),
            new BaseDef("feet.chain_greaves", Slot.Feet, Line.Guard, new Txt("鎖の脛当て", "Chain Greaves"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_feet_chain_greaves, Family.Guard),
            new BaseDef("feet.storm_boots", Slot.Feet, Line.Offense, new Txt("嵐駆けの靴", "Stormrunner Boots"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_storm_boots, Family.Gale),
            new BaseDef("feet.ember_slippers", Slot.Feet, Line.Offense, new Txt("燠火の上履き", "Embered Slippers"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_feet_ember_slippers, Family.Flame),
            new BaseDef("charm.raven_feather", Slot.Charm, Line.Offense, new Txt("鴉の風切り羽", "Raven Quill"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_raven_feather, Family.Dark),
            new BaseDef("charm.lotus_seal", Slot.Charm, Line.Guard, new Txt("蓮の印", "Lotus Seal"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_charm_lotus_seal, Family.Mend),
            new BaseDef("charm.storm_bell", Slot.Charm, Line.Resonance, new Txt("嵐の鈴", "Storm Bell"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_storm_bell, Family.Dark),
            new BaseDef("charm.ice_heart", Slot.Charm, Line.Resonance, new Txt("氷の心臓", "Frozen Heart"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_charm_ice_heart, Family.Memory),
            new BaseDef("charm.ink_stone", Slot.Charm, Line.Offense, new Txt("墨の硯", "Ink Stone"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_ink_stone, Family.Dark),

            // v1.29：各枠 +30（土台を倍に。基礎能力は固定値か控えめな%。攻撃力%・魔力%は使わない）
            new BaseDef("weapon.tide_cutter", Slot.Weapon, Line.Offense, new Txt("波切りの刀", "Tidecutter Blade"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_tide_cutter, Family.Frost),
            new BaseDef("weapon.rockbreaker", Slot.Weapon, Line.Offense, new Txt("砕岩のハンマー", "Rockbreaker Hammer"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_rockbreaker, Family.Flame),
            new BaseDef("weapon.swallow_kodachi", Slot.Weapon, Line.Offense, new Txt("燕返しの小太刀", "Swallow-Turn Kodachi"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_swallow_kodachi, Family.Gale),
            new BaseDef("weapon.hooked_falchion", Slot.Weapon, Line.Offense, new Txt("鉤爪の曲刀", "Hooked Falchion"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_weapon_hooked_falchion),
            new BaseDef("weapon.severing_axe", Slot.Weapon, Line.Offense, new Txt("断ち切りの大斧", "Severing Greataxe"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_severing_axe, Family.Flame),
            new BaseDef("weapon.starlit_knives", Slot.Weapon, Line.Offense, new Txt("連星の投剣", "Starlit Throwing Knives"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_weapon_starlit_knives, Family.Light),
            new BaseDef("weapon.whiteheat_estoc", Slot.Weapon, Line.Offense, new Txt("白熱の刺突剣", "Whiteheat Estoc"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_weapon_whiteheat_estoc, Family.Flame),
            new BaseDef("weapon.northwind_axe", Slot.Weapon, Line.Offense, new Txt("北風の戦斧", "Northwind Battleaxe"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_weapon_northwind_axe, Family.Frost),
            new BaseDef("weapon.sunlit_blade", Slot.Weapon, Line.Offense, new Txt("陽射しの刃", "Sunlit Blade"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_weapon_sunlit_blade, Family.Light),
            new BaseDef("weapon.gloaming_dagger", Slot.Weapon, Line.Offense, new Txt("宵闇の短剣", "Gloaming Dagger"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_weapon_gloaming_dagger, Family.Dark),
            new BaseDef("weapon.lightning_pair", Slot.Weapon, Line.Offense, new Txt("雷光の双刃", "Lightning Paired Blades"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_lightning_pair, Family.Gale),
            new BaseDef("weapon.evilbreaker_lance", Slot.Weapon, Line.Offense, new Txt("破邪の槍", "Evil-Breaking Lance"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_evilbreaker_lance, Family.Light),
            new BaseDef("weapon.maelstrom_sword", Slot.Weapon, Line.Offense, new Txt("渦潮の剣", "Maelstrom Sword"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_maelstrom_sword, Family.Frost),
            new BaseDef("weapon.gatehouse_maul", Slot.Weapon, Line.Guard, new Txt("城門の大槌", "Gatehouse Maul"), Stat.Armor, EquipmentItemsBalanceValues.Base_weapon_gatehouse_maul, Family.Summon),
            new BaseDef("weapon.lighthouse_cudgel", Slot.Weapon, Line.Guard, new Txt("灯台の棍", "Lighthouse Cudgel"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_weapon_lighthouse_cudgel, Family.Mend),
            new BaseDef("weapon.tortoise_bokken", Slot.Weapon, Line.Guard, new Txt("亀甲の木刀", "Tortoiseshell Bokken"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_weapon_tortoise_bokken, Family.Guard),
            new BaseDef("weapon.thaw_hammer", Slot.Weapon, Line.Guard, new Txt("雪解けの鎚", "Thawhammer"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_weapon_thaw_hammer, Family.Frost),
            new BaseDef("weapon.anchor_cleaver", Slot.Weapon, Line.Guard, new Txt("錨の鉈", "Anchor Cleaver"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_weapon_anchor_cleaver, Family.Guard),
            new BaseDef("weapon.granite_cosh", Slot.Weapon, Line.Guard, new Txt("御影の戦棍", "Granite Cosh"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_weapon_granite_cosh, Family.Summon),
            new BaseDef("weapon.stillwater_blade", Slot.Weapon, Line.Guard, new Txt("静水の長刀", "Stillwater Longblade"), Stat.Armor, EquipmentItemsBalanceValues.Base_weapon_stillwater_blade, Family.Mend),
            new BaseDef("weapon.chanting_wand", Slot.Weapon, Line.Resonance, new Txt("詠唱の短杖", "Chanting Wand"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_chanting_wand),
            new BaseDef("weapon.astral_staff", Slot.Weapon, Line.Resonance, new Txt("星霜の儀杖", "Astral Ritual Staff"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_astral_staff, Family.Dark),
            new BaseDef("weapon.windhowl_staff", Slot.Weapon, Line.Resonance, new Txt("風唸りの杖", "Windhowl Staff"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_windhowl_staff, Family.Gale),
            new BaseDef("weapon.conch_scepter", Slot.Weapon, Line.Resonance, new Txt("法螺の聖杖", "Conch Scepter"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_weapon_conch_scepter, Family.Memory),
            new BaseDef("weapon.firefly_staff", Slot.Weapon, Line.Resonance, new Txt("蛍火の灯杖", "Firefly Staff"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_weapon_firefly_staff, Family.Memory),
            new BaseDef("weapon.evening_fan", Slot.Weapon, Line.Resonance, new Txt("夕薫の扇", "Evening Breeze Fan"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_evening_fan, Family.Gale),
            new BaseDef("weapon.starchart_scroll", Slot.Weapon, Line.Resonance, new Txt("星図の巻物", "Star Chart Scroll"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_starchart_scroll, Family.Memory),
            new BaseDef("weapon.dewclear_wand", Slot.Weapon, Line.Resonance, new Txt("露払いの細杖", "Dewclear Wand"), Stat.HealPower, EquipmentItemsBalanceValues.Base_weapon_dewclear_wand, Family.Mend),
            new BaseDef("weapon.starsinger_bow", Slot.Weapon, Line.Resonance, new Txt("奏星の弓", "Star-Singing Bow"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_starsinger_bow, Family.Light),
            new BaseDef("weapon.moonlit_cane", Slot.Weapon, Line.Resonance, new Txt("月明の杖", "Moonlit Cane"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_weapon_moonlit_cane, Family.Memory),

            new BaseDef("armor.stormfront_vest", Slot.Armor, Line.Offense, new Txt("嵐の胸当て", "Stormfront Vest"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_armor_stormfront_vest, Family.Gale),
            new BaseDef("armor.quicksilver_jacket", Slot.Armor, Line.Offense, new Txt("水銀の短上衣", "Quicksilver Jacket"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_armor_quicksilver_jacket, Family.Gale),
            new BaseDef("armor.volcanic_coat", Slot.Armor, Line.Offense, new Txt("火山岩の外套", "Volcanic Coat"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_armor_volcanic_coat, Family.Flame),
            new BaseDef("armor.glacier_harness", Slot.Armor, Line.Offense, new Txt("氷河の胸綱", "Glacier Harness"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_armor_glacier_harness, Family.Frost),
            new BaseDef("armor.dawnlight_corset", Slot.Armor, Line.Offense, new Txt("暁光の胴衣", "Dawnlight Corset"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_armor_dawnlight_corset, Family.Light),
            new BaseDef("armor.duskweave_jacket", Slot.Armor, Line.Offense, new Txt("黄昏織りの上着", "Duskweave Jacket"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_armor_duskweave_jacket, Family.Dark),
            new BaseDef("armor.ambush_vest", Slot.Armor, Line.Offense, new Txt("奇襲の胴衣", "Ambush Vest"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_armor_ambush_vest, Family.Dark),
            new BaseDef("armor.skirmisher_coat", Slot.Armor, Line.Offense, new Txt("遊撃手の外套", "Skirmisher's Coat"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_armor_skirmisher_coat),
            new BaseDef("armor.citadel_plate", Slot.Armor, Line.Guard, new Txt("城砦の板金鎧", "Citadel Plate"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_citadel_plate),
            new BaseDef("armor.rampart_jacket", Slot.Armor, Line.Guard, new Txt("土塁の上着", "Rampart Jacket"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_rampart_jacket, Family.Guard),
            new BaseDef("armor.deeproot_vest", Slot.Armor, Line.Guard, new Txt("深根の胴着", "Deeproot Vest"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_deeproot_vest, Family.Mend),
            new BaseDef("armor.tidebound_mail", Slot.Armor, Line.Guard, new Txt("潮縛の鎖帷子", "Tidebound Mail"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_armor_tidebound_mail, Family.Frost),
            new BaseDef("armor.basalt_cuirass", Slot.Armor, Line.Guard, new Txt("玄武の胴鎧", "Basalt Cuirass"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_armor_basalt_cuirass, Family.Flame),
            new BaseDef("armor.buckler_vest", Slot.Armor, Line.Guard, new Txt("小盾の胴衣", "Buckler Vest"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_buckler_vest, Family.Summon),
            new BaseDef("armor.oathplate_cuirass", Slot.Armor, Line.Guard, new Txt("誓板の胸当て", "Oathplate Cuirass"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_armor_oathplate_cuirass, Family.Summon),
            new BaseDef("armor.winterwool_coat", Slot.Armor, Line.Guard, new Txt("冬毛の上衣", "Winterwool Coat"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_armor_winterwool_coat, Family.Frost),
            new BaseDef("armor.ironbark_vest", Slot.Armor, Line.Guard, new Txt("鉄樹の胴衣", "Ironbark Vest"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_ironbark_vest, Family.Guard),
            new BaseDef("armor.twistedchain_ply", Slot.Armor, Line.Guard, new Txt("ねじれ鎖の腹当て", "Twisted-Chain Ply"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_armor_twistedchain_ply, Family.Dark),
            new BaseDef("armor.mirrorsilk_robe", Slot.Armor, Line.Resonance, new Txt("鏡絹の衣", "Mirrorsilk Robe"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_mirrorsilk_robe, Family.Light),
            new BaseDef("armor.aurora_wrap", Slot.Armor, Line.Resonance, new Txt("極光の肩掛け", "Aurora Wrap"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_aurora_wrap, Family.Frost),
            new BaseDef("armor.seafoam_gown", Slot.Armor, Line.Resonance, new Txt("海沫の長衣", "Seafoam Gown"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_armor_seafoam_gown, Family.Summon),
            new BaseDef("armor.emberweave_shawl", Slot.Armor, Line.Resonance, new Txt("織り火の肩掛け", "Emberweave Shawl"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_armor_emberweave_shawl, Family.Summon),
            new BaseDef("armor.candlelight_robe", Slot.Armor, Line.Resonance, new Txt("燭光の法衣", "Candlelight Robe"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_armor_candlelight_robe, Family.Mend),
            new BaseDef("armor.nightloom_robe", Slot.Armor, Line.Resonance, new Txt("夜織りの衣", "Nightloom Robe"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_armor_nightloom_robe, Family.Mend),
            new BaseDef("armor.swiftstep_coat", Slot.Armor, Line.Resonance, new Txt("疾歩の外套", "Swiftstep Coat"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_armor_swiftstep_coat, Family.Gale),
            new BaseDef("armor.healing_sash", Slot.Armor, Line.Resonance, new Txt("癒しの飾り帯", "Healing Sash"), Stat.HealPower, EquipmentItemsBalanceValues.Base_armor_healing_sash, Family.Mend),
            new BaseDef("armor.wardsigil_vest", Slot.Armor, Line.Resonance, new Txt("加護の胴衣", "Wardsigil Vest"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_armor_wardsigil_vest, Family.Light),
            new BaseDef("armor.summoners_vest", Slot.Armor, Line.Resonance, new Txt("喚び獣の胸当て", "Summoner's Vest"), Stat.SummonPower, EquipmentItemsBalanceValues.Base_armor_summoners_vest, Family.Summon),
            new BaseDef("armor.zephyr_robe", Slot.Armor, Line.Resonance, new Txt("西風の法衣", "Zephyr Robe"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_zephyr_robe, Family.Gale),
            new BaseDef("armor.morningdew_robe", Slot.Armor, Line.Resonance, new Txt("朝露の衣", "Morning Dew Robe"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_morningdew_robe, Family.Light),

            new BaseDef("head.talon_crown", Slot.Head, Line.Offense, new Txt("爪の冠", "Talon Crown"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_talon_crown, Family.Dark),
            new BaseDef("head.gale_hood", Slot.Head, Line.Offense, new Txt("疾風の頭巾", "Gale Hood"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_gale_hood, Family.Gale),
            new BaseDef("head.magma_band", Slot.Head, Line.Offense, new Txt("熔岩の鉢巻", "Magma Band"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_head_magma_band, Family.Flame),
            new BaseDef("head.rimebloom_hood", Slot.Head, Line.Offense, new Txt("霜華の頭巾", "Rimebloom Hood"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_head_rimebloom_hood, Family.Frost),
            new BaseDef("head.noonlight_circlet", Slot.Head, Line.Offense, new Txt("真昼の額冠", "Noonlight Circlet"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_head_noonlight_circlet, Family.Light),
            new BaseDef("head.eclipse_mask", Slot.Head, Line.Offense, new Txt("日蝕の面", "Eclipse Mask"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_eclipse_mask, Family.Dark),
            new BaseDef("head.hawkeye_band", Slot.Head, Line.Offense, new Txt("鷹目の鉢巻", "Hawkeye Band"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_head_hawkeye_band, Family.Gale),
            new BaseDef("head.longshot_cap", Slot.Head, Line.Offense, new Txt("遠撃の帽子", "Longshot Cap"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_head_longshot_cap),
            new BaseDef("head.crimsonlotus_hood", Slot.Head, Line.Offense, new Txt("紅蓮の頭巾", "Crimson Lotus Hood"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_head_crimsonlotus_hood, Family.Flame),
            new BaseDef("head.thunderveil_hood", Slot.Head, Line.Offense, new Txt("雷帷の頭巾", "Thunderveil Hood"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_thunderveil_hood, Family.Gale),
            new BaseDef("head.vulture_hood", Slot.Head, Line.Offense, new Txt("兀鷹のフード", "Vulture Hood"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_vulture_hood, Family.Dark),
            new BaseDef("head.sentry_visor", Slot.Head, Line.Guard, new Txt("歩哨の面頬", "Sentry Visor"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_sentry_visor, Family.Guard),
            new BaseDef("head.boulder_helm", Slot.Head, Line.Guard, new Txt("岩塊の兜", "Boulder Helm"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_head_boulder_helm, Family.Guard),
            new BaseDef("head.moss_crown", Slot.Head, Line.Guard, new Txt("苔の冠", "Moss Crown"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_head_moss_crown, Family.Mend),
            new BaseDef("head.icewall_helm", Slot.Head, Line.Guard, new Txt("氷壁の兜", "Icewall Helm"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_icewall_helm, Family.Frost),
            new BaseDef("head.oathring_circlet", Slot.Head, Line.Guard, new Txt("誓環の額冠", "Oathring Circlet"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_head_oathring_circlet, Family.Light),
            new BaseDef("head.sunkenbell_helm", Slot.Head, Line.Guard, new Txt("沈鐘の兜", "Sunken Bell Helm"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_head_sunkenbell_helm, Family.Frost),
            new BaseDef("head.pillar_crown", Slot.Head, Line.Guard, new Txt("柱石の冠", "Pillar Crown"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_head_pillar_crown, Family.Memory),
            new BaseDef("head.fortress_coif", Slot.Head, Line.Guard, new Txt("城塞の鎖頭巾", "Fortress Coif"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_fortress_coif, Family.Guard),
            new BaseDef("head.comet_diadem", Slot.Head, Line.Resonance, new Txt("彗星の髪飾り", "Comet Diadem"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_head_comet_diadem, Family.Memory),
            new BaseDef("head.whisper_veil", Slot.Head, Line.Resonance, new Txt("囁きのヴェール", "Whisper Veil"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_whisper_veil, Family.Gale),
            new BaseDef("head.tidal_circlet", Slot.Head, Line.Resonance, new Txt("潮汐の額冠", "Tidal Circlet"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_head_tidal_circlet, Family.Memory),
            new BaseDef("head.emberbloom_crown", Slot.Head, Line.Resonance, new Txt("火焔花の冠", "Emberbloom Crown"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_head_emberbloom_crown, Family.Flame),
            new BaseDef("head.sunbeam_hood", Slot.Head, Line.Resonance, new Txt("光条の頭巾", "Sunbeam Hood"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_head_sunbeam_hood, Family.Mend),
            new BaseDef("head.starless_veil", Slot.Head, Line.Resonance, new Txt("無星のヴェール", "Starless Veil"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_starless_veil, Family.Memory),
            new BaseDef("head.meditation_band", Slot.Head, Line.Resonance, new Txt("瞑想の鉢巻", "Meditation Band"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_meditation_band, Family.Mend),
            new BaseDef("head.kindly_circlet", Slot.Head, Line.Resonance, new Txt("慈手の額冠", "Kindly Circlet"), Stat.HealPower, EquipmentItemsBalanceValues.Base_head_kindly_circlet, Family.Mend),
            new BaseDef("head.wardband", Slot.Head, Line.Resonance, new Txt("護符の額帯", "Ward Band"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_head_wardband, Family.Light),
            new BaseDef("head.beastcaller_antlers", Slot.Head, Line.Resonance, new Txt("呼獣の飾り角", "Beastcaller Antlers"), Stat.SummonPower, EquipmentItemsBalanceValues.Base_head_beastcaller_antlers, Family.Summon),
            new BaseDef("head.moonlace_hood", Slot.Head, Line.Resonance, new Txt("月紗の頭巾", "Moonlace Hood"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_head_moonlace_hood, Family.Frost),

            new BaseDef("hands.ripgrip_gloves", Slot.Hands, Line.Offense, new Txt("裂握の手袋", "Ripgrip Gloves"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_ripgrip_gloves, Family.Flame),
            new BaseDef("hands.swiftpalm_gloves", Slot.Hands, Line.Offense, new Txt("風掌の手袋", "Swift-Palm Gloves"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_swiftpalm_gloves, Family.Gale),
            new BaseDef("hands.hunting_sinew", Slot.Hands, Line.Offense, new Txt("狩りの筋帯", "Hunting Sinew Wraps"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_hunting_sinew, Family.Summon),
            new BaseDef("hands.blazeknit_gloves", Slot.Hands, Line.Offense, new Txt("火織りの手袋", "Blazeknit Gloves"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_hands_blazeknit_gloves, Family.Flame),
            new BaseDef("hands.frostbite_knuckles", Slot.Hands, Line.Offense, new Txt("凍手の拳当て", "Frostbite Knuckles"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_hands_frostbite_knuckles, Family.Frost),
            new BaseDef("hands.sunfire_grips", Slot.Hands, Line.Offense, new Txt("陽炎の握り", "Sunfire Grips"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_hands_sunfire_grips, Family.Flame),
            new BaseDef("hands.nightpalm_gloves", Slot.Hands, Line.Offense, new Txt("夜掌の手袋", "Nightpalm Gloves"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_hands_nightpalm_gloves, Family.Dark),
            new BaseDef("hands.precise_fingerless", Slot.Hands, Line.Offense, new Txt("的確な指抜き", "Precise Fingerless Gloves"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_precise_fingerless, Family.Light),
            new BaseDef("hands.bowmaster_bracers", Slot.Hands, Line.Offense, new Txt("弓張りの腕当て", "Bowmaster's Bracers"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_hands_bowmaster_bracers),
            new BaseDef("hands.storm_knuckles", Slot.Hands, Line.Offense, new Txt("嵐の拳当て", "Storm Knuckles"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_storm_knuckles, Family.Gale),
            new BaseDef("hands.riven_fists", Slot.Hands, Line.Offense, new Txt("裂罅の拳", "Riven Fists"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_riven_fists, Family.Dark),
            new BaseDef("hands.duelist_bracers", Slot.Hands, Line.Offense, new Txt("決闘の腕当て", "Duelist's Bracers"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_duelist_bracers, Family.Dark),
            new BaseDef("hands.viperfang_claws", Slot.Hands, Line.Offense, new Txt("毒牙の爪", "Viper Fang Claws"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_viperfang_claws, Family.Summon),
            new BaseDef("hands.wardens_grips", Slot.Hands, Line.Guard, new Txt("番人の握り", "Warden's Grips"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_wardens_grips, Family.Guard),
            new BaseDef("hands.bark_knuckles", Slot.Hands, Line.Guard, new Txt("樹皮の拳当て", "Bark Knuckles"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_hands_bark_knuckles, Family.Mend),
            new BaseDef("hands.ironvein_gauntlets", Slot.Hands, Line.Guard, new Txt("鉄脈の籠手", "Ironvein Gauntlets"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_ironvein_gauntlets, Family.Guard),
            new BaseDef("hands.heavypalm_gloves", Slot.Hands, Line.Guard, new Txt("重掌の手袋", "Heavypalm Gloves"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_hands_heavypalm_gloves, Family.Guard),
            new BaseDef("hands.bulwark_wraps", Slot.Hands, Line.Guard, new Txt("壁の手巻き", "Bulwark Wraps"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_hands_bulwark_wraps, Family.Guard),
            new BaseDef("hands.reef_gauntlets", Slot.Hands, Line.Guard, new Txt("礁の籠手", "Reef Gauntlets"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_reef_gauntlets, Family.Frost),
            new BaseDef("hands.rootgrip_gloves", Slot.Hands, Line.Guard, new Txt("根握りの手袋", "Rootgrip Gloves"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_hands_rootgrip_gloves, Family.Memory),
            new BaseDef("hands.snowmelt_mitts", Slot.Hands, Line.Guard, new Txt("雪解の指なし", "Snowmelt Mitts"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_hands_snowmelt_mitts, Family.Frost),
            new BaseDef("hands.oathpalm_gloves", Slot.Hands, Line.Guard, new Txt("誓掌の手袋", "Oathpalm Gloves"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_hands_oathpalm_gloves, Family.Light),
            new BaseDef("hands.manuscript_gloves", Slot.Hands, Line.Resonance, new Txt("写本の手袋", "Manuscript Gloves"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_hands_manuscript_gloves, Family.Memory),
            new BaseDef("hands.chime_bracers", Slot.Hands, Line.Resonance, new Txt("鈴鳴りの腕輪", "Chime Bracers"), Stat.Haste, EquipmentItemsBalanceValues.Base_hands_chime_bracers, Family.Gale),
            new BaseDef("hands.tidecaller_wraps", Slot.Hands, Line.Resonance, new Txt("潮呼びの手巻き", "Tidecaller Wraps"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_hands_tidecaller_wraps, Family.Summon),
            new BaseDef("hands.cinderthread_wraps", Slot.Hands, Line.Resonance, new Txt("火糸の手巻き", "Cinderthread Wraps"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_hands_cinderthread_wraps, Family.Memory),
            new BaseDef("hands.lantern_fingerless", Slot.Hands, Line.Resonance, new Txt("灯火の指抜き", "Lantern Fingerless Gloves"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_hands_lantern_fingerless, Family.Flame),
            new BaseDef("hands.duskstitch_gloves", Slot.Hands, Line.Resonance, new Txt("黄昏縫いの手袋", "Duskstitch Gloves"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_hands_duskstitch_gloves, Family.Light),
            new BaseDef("hands.mender_palms", Slot.Hands, Line.Resonance, new Txt("癒し掌の手袋", "Mender's Palms"), Stat.HealPower, EquipmentItemsBalanceValues.Base_hands_mender_palms, Family.Mend),
            new BaseDef("hands.summoner_bands", Slot.Hands, Line.Resonance, new Txt("喚び手の指輪", "Summoner's Bands"), Stat.SummonPower, EquipmentItemsBalanceValues.Base_hands_summoner_bands, Family.Summon),

            new BaseDef("feet.blitz_treads", Slot.Feet, Line.Offense, new Txt("電光の足甲", "Blitz Treads"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_feet_blitz_treads, Family.Gale),
            new BaseDef("feet.emberdash_boots", Slot.Feet, Line.Offense, new Txt("火駆けの長靴", "Emberdash Boots"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_feet_emberdash_boots, Family.Flame),
            new BaseDef("feet.froststride_shoes", Slot.Feet, Line.Offense, new Txt("霜踏みの靴", "Froststride Shoes"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_feet_froststride_shoes, Family.Frost),
            new BaseDef("feet.sunspur_boots", Slot.Feet, Line.Offense, new Txt("陽蹴りの靴", "Sunspur Boots"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_feet_sunspur_boots, Family.Light),
            new BaseDef("feet.duskstep_boots", Slot.Feet, Line.Offense, new Txt("宵踏みの靴", "Duskstep Boots"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_feet_duskstep_boots, Family.Dark),
            new BaseDef("feet.hunter_striders", Slot.Feet, Line.Offense, new Txt("追撃の脚当て", "Hunter's Striders"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_feet_hunter_striders, Family.Gale),
            new BaseDef("feet.quicksilver_greaves", Slot.Feet, Line.Offense, new Txt("水銀の脛当て", "Quicksilver Greaves"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_quicksilver_greaves, Family.Summon),
            new BaseDef("feet.deadeye_leggings", Slot.Feet, Line.Offense, new Txt("必中の脚絆", "Deadeye Leggings"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_feet_deadeye_leggings, Family.Dark),
            new BaseDef("feet.wolfstride_boots", Slot.Feet, Line.Offense, new Txt("狼歩の長靴", "Wolfstride Boots"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_wolfstride_boots, Family.Summon),
            new BaseDef("feet.gale_greaves", Slot.Feet, Line.Offense, new Txt("烈風の脛当て", "Gale Greaves"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_feet_gale_greaves, Family.Gale),
            new BaseDef("feet.thorntread_sabatons", Slot.Feet, Line.Offense, new Txt("棘踏みの足甲", "Thorntread Sabatons"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_feet_thorntread_sabatons, Family.Summon),
            new BaseDef("feet.bastion_sabatons", Slot.Feet, Line.Guard, new Txt("城塞の鉄鞋", "Bastion Sabatons"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_bastion_sabatons, Family.Guard),
            new BaseDef("feet.deeproot_boots", Slot.Feet, Line.Guard, new Txt("深根の長靴", "Deeproot Boots"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_feet_deeproot_boots, Family.Mend),
            new BaseDef("feet.stoneguard_sabatons", Slot.Feet, Line.Guard, new Txt("石衛の鉄鞋", "Stoneguard Sabatons"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_feet_stoneguard_sabatons, Family.Mend),
            new BaseDef("feet.tortoiseshell_greaves", Slot.Feet, Line.Guard, new Txt("亀甲の脛当て", "Tortoiseshell Greaves"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_feet_tortoiseshell_greaves, Family.Guard),
            new BaseDef("feet.ironwave_greaves", Slot.Feet, Line.Guard, new Txt("鉄波の脛当て", "Ironwave Greaves"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_ironwave_greaves, Family.Frost),
            new BaseDef("feet.ballast_boots", Slot.Feet, Line.Guard, new Txt("沈錘の長靴", "Ballast Boots"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_feet_ballast_boots, Family.Guard),
            new BaseDef("feet.rampart_greaves", Slot.Feet, Line.Guard, new Txt("土塁の脛当て", "Rampart Greaves"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_rampart_greaves, Family.Guard),
            new BaseDef("feet.winterhide_boots", Slot.Feet, Line.Guard, new Txt("冬毛の長靴", "Winterhide Boots"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_feet_winterhide_boots, Family.Frost),
            new BaseDef("feet.wardstep_sandals", Slot.Feet, Line.Guard, new Txt("護りの草鞋", "Wardstep Sandals"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_feet_wardstep_sandals, Family.Light),
            new BaseDef("feet.mistral_sandals", Slot.Feet, Line.Resonance, new Txt("突風のサンダル", "Mistral Sandals"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_mistral_sandals, Family.Flame),
            new BaseDef("feet.starlit_moccasins", Slot.Feet, Line.Resonance, new Txt("星履の靴", "Starlit Moccasins"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_starlit_moccasins, Family.Memory),
            new BaseDef("feet.tidepool_sandals", Slot.Feet, Line.Resonance, new Txt("潮溜まりのサンダル", "Tidepool Sandals"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_feet_tidepool_sandals, Family.Summon),
            new BaseDef("feet.firebloom_slippers", Slot.Feet, Line.Resonance, new Txt("火華の上履き", "Firebloom Slippers"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_feet_firebloom_slippers, Family.Mend),
            new BaseDef("feet.dawnmist_shoes", Slot.Feet, Line.Resonance, new Txt("暁霧の靴", "Dawnmist Shoes"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_feet_dawnmist_shoes, Family.Frost),
            new BaseDef("feet.nightveil_slippers", Slot.Feet, Line.Resonance, new Txt("夜帳の上履き", "Nightveil Slippers"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_feet_nightveil_slippers, Family.Dark),
            new BaseDef("feet.mender_shoes", Slot.Feet, Line.Resonance, new Txt("癒しの靴", "Mender's Shoes"), Stat.HealPower, EquipmentItemsBalanceValues.Base_feet_mender_shoes, Family.Mend),
            new BaseDef("feet.cometstride_shoes", Slot.Feet, Line.Resonance, new Txt("彗星の靴", "Comet-Stride Shoes"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_cometstride_shoes, Family.Light),
            new BaseDef("feet.whisperweave_shoes", Slot.Feet, Line.Resonance, new Txt("囁き織りの靴", "Whisperweave Shoes"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_feet_whisperweave_shoes, Family.Memory),
            new BaseDef("feet.beastpaw_boots", Slot.Feet, Line.Resonance, new Txt("獣趾の長靴", "Beastpaw Boots"), Stat.SummonPower, EquipmentItemsBalanceValues.Base_feet_beastpaw_boots, Family.Summon),

            new BaseDef("charm.talon_pendant", Slot.Charm, Line.Offense, new Txt("爪の垂飾り", "Talon Pendant"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_talon_pendant, Family.Summon),
            new BaseDef("charm.blaze_brooch", Slot.Charm, Line.Offense, new Txt("焔のブローチ", "Blaze Brooch"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_charm_blaze_brooch, Family.Flame),
            new BaseDef("charm.glacier_charm", Slot.Charm, Line.Offense, new Txt("氷晶の飾り", "Glacier Charm"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_charm_glacier_charm, Family.Frost),
            new BaseDef("charm.sunspoke_pin", Slot.Charm, Line.Offense, new Txt("陽光的ピン", "Sunspoke Pin"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_charm_sunspoke_pin, Family.Light),
            new BaseDef("charm.duskbead_necklace", Slot.Charm, Line.Offense, new Txt("宵珠の首飾り", "Duskbead Necklace"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_charm_duskbead_necklace, Family.Dark),
            new BaseDef("charm.keeneye_charm", Slot.Charm, Line.Offense, new Txt("鋭眼の御守り", "Keen-Eye Charm"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_charm_keeneye_charm, Family.Light),
            new BaseDef("charm.stormcloud_locket", Slot.Charm, Line.Offense, new Txt("雷雲のロケット", "Stormcloud Locket"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_charm_stormcloud_locket, Family.Gale),
            new BaseDef("charm.pursuit_badge", Slot.Charm, Line.Offense, new Txt("追撃の徽章", "Pursuit Badge"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_pursuit_badge, Family.Gale),
            new BaseDef("charm.skirmish_ring", Slot.Charm, Line.Offense, new Txt("遊撃の指輪", "Skirmish Ring"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_skirmish_ring),
            new BaseDef("charm.longshot_charm", Slot.Charm, Line.Offense, new Txt("遠当ての御守り", "Longshot Charm"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_charm_longshot_charm),
            new BaseDef("charm.garnet_ring", Slot.Charm, Line.Offense, new Txt("紅玉の指輪", "Garnet Ring"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_garnet_ring, Family.Flame),
            new BaseDef("charm.hearthside_ring", Slot.Charm, Line.Guard, new Txt("囲炉裏の指輪", "Hearthside Ring"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_charm_hearthside_ring, Family.Mend),
            new BaseDef("charm.bulwark_seal", Slot.Charm, Line.Guard, new Txt("壁の印章", "Bulwark Seal"), Stat.Armor, EquipmentItemsBalanceValues.Base_charm_bulwark_seal, Family.Guard),
            new BaseDef("charm.deeproot_charm", Slot.Charm, Line.Guard, new Txt("深根の護符", "Deeproot Charm"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_charm_deeproot_charm, Family.Summon),
            new BaseDef("charm.tidewarden_brooch", Slot.Charm, Line.Guard, new Txt("潮衛のブローチ", "Tidewarden Brooch"), Stat.Armor, EquipmentItemsBalanceValues.Base_charm_tidewarden_brooch, Family.Frost),
            new BaseDef("charm.boulder_pendant", Slot.Charm, Line.Guard, new Txt("岩塊の首飾り", "Boulder Pendant"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_charm_boulder_pendant, Family.Summon),
            new BaseDef("charm.ironknot_ring", Slot.Charm, Line.Guard, new Txt("鉄結びの指輪", "Ironknot Ring"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_charm_ironknot_ring, Family.Gale),
            new BaseDef("charm.snowbloom_charm", Slot.Charm, Line.Guard, new Txt("雪華の御守り", "Snowbloom Charm"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_charm_snowbloom_charm, Family.Frost),
            new BaseDef("charm.winteroak_charm", Slot.Charm, Line.Guard, new Txt("冬樫の護符", "Winter Oak Charm"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_charm_winteroak_charm, Family.Frost),
            new BaseDef("charm.comet_pendant", Slot.Charm, Line.Resonance, new Txt("彗星の垂飾り", "Comet Pendant"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_comet_pendant, Family.Flame),
            new BaseDef("charm.seafoam_ring", Slot.Charm, Line.Resonance, new Txt("海沫の指輪", "Seafoam Ring"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_charm_seafoam_ring, Family.Summon),
            new BaseDef("charm.cindercore_locket", Slot.Charm, Line.Resonance, new Txt("火芯のロケット", "Cindercore Locket"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_charm_cindercore_locket, Family.Flame),
            new BaseDef("charm.halo_charm", Slot.Charm, Line.Resonance, new Txt("光環の御守り", "Halo Charm"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_charm_halo_charm, Family.Mend),
            new BaseDef("charm.eclipse_ring", Slot.Charm, Line.Resonance, new Txt("蝕の指輪", "Eclipse Ring"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_charm_eclipse_ring, Family.Memory),
            new BaseDef("charm.windchime_charm", Slot.Charm, Line.Resonance, new Txt("風鈴の飾り", "Wind Chime Charm"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_windchime_charm, Family.Gale),
            new BaseDef("charm.mender_locket", Slot.Charm, Line.Resonance, new Txt("癒しのロケット", "Mender's Locket"), Stat.HealPower, EquipmentItemsBalanceValues.Base_charm_mender_locket, Family.Mend),
            new BaseDef("charm.beasttongue_charm", Slot.Charm, Line.Resonance, new Txt("獣語の護符", "Beasttongue Charm"), Stat.SummonPower, EquipmentItemsBalanceValues.Base_charm_beasttongue_charm, Family.Summon),
            new BaseDef("charm.zephyr_ring", Slot.Charm, Line.Resonance, new Txt("西風の指輪", "Zephyr Ring"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_charm_zephyr_ring, Family.Gale),
            new BaseDef("charm.dawnsilk_band", Slot.Charm, Line.Resonance, new Txt("暁糸の腕輪", "Dawnsilk Band"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_dawnsilk_band, Family.Light),
            new BaseDef("charm.stardust_pendant", Slot.Charm, Line.Resonance, new Txt("星屑の垂飾り", "Stardust Pendant"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_stardust_pendant, Family.Light),
            new BaseDef("weapon.plain_edge", Slot.Weapon, Line.Offense, new Txt("無垢の直刀", "Unblemished Blade"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_plain_edge),
            new BaseDef("weapon.soldier_saber", Slot.Weapon, Line.Offense, new Txt("兵の軍刀", "Soldier's Saber"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_soldier_saber),
            new BaseDef("weapon.rough_cleaver", Slot.Weapon, Line.Offense, new Txt("無骨な鉈", "Unrefined Cleaver"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_rough_cleaver),
            new BaseDef("weapon.apprentice_wand", Slot.Weapon, Line.Resonance, new Txt("見習いの短杖", "Apprentice's Wand"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_apprentice_wand),
            new BaseDef("weapon.icevein_blade", Slot.Weapon, Line.Resonance, new Txt("氷脈の刀", "Icevein Blade"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_weapon_icevein_blade, Family.Frost),
            new BaseDef("weapon.hoarfrost_dirk", Slot.Weapon, Line.Offense, new Txt("霧氷の短剣", "Hoarfrost Dirk"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_weapon_hoarfrost_dirk, Family.Frost),
            new BaseDef("weapon.winterbrand_mace", Slot.Weapon, Line.Guard, new Txt("冬の戦鎚", "Winterbrand Mace"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_weapon_winterbrand_mace, Family.Frost),
            new BaseDef("weapon.frozen_oath_blade", Slot.Weapon, Line.Guard, new Txt("凍てつく誓いの剣", "Frozen-Oath Blade"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_weapon_frozen_oath_blade, Family.Frost),
            new BaseDef("weapon.inferno_lash", Slot.Weapon, Line.Offense, new Txt("業火の鞭", "Inferno Lash"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_weapon_inferno_lash, Family.Flame),
            new BaseDef("weapon.firedance_saber", Slot.Weapon, Line.Offense, new Txt("火舞の細剣", "Firedance Saber"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_firedance_saber, Family.Flame),
            new BaseDef("weapon.phoenix_plume_sword", Slot.Weapon, Line.Offense, new Txt("鳳羽の剣", "Phoenix-Plume Sword"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_weapon_phoenix_plume_sword, Family.Flame),
            new BaseDef("weapon.cauterizing_estoc", Slot.Weapon, Line.Offense, new Txt("焼灼の刺突剣", "Cauterizing Estoc"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_cauterizing_estoc, Family.Flame),
            new BaseDef("weapon.daybreak_blade", Slot.Weapon, Line.Resonance, new Txt("夜明けの刃", "Daybreak Blade"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_daybreak_blade, Family.Light),
            new BaseDef("weapon.halo_blade", Slot.Weapon, Line.Offense, new Txt("光環の剣", "Halo Blade"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_weapon_halo_blade, Family.Light),
            new BaseDef("weapon.seraph_rod", Slot.Weapon, Line.Resonance, new Txt("煌めく天使の杖", "Seraph's Rod"), Stat.HealPower, EquipmentItemsBalanceValues.Base_weapon_seraph_rod, Family.Light),
            new BaseDef("weapon.lamplighter_sword", Slot.Weapon, Line.Guard, new Txt("灯守の剣", "Lamplighter Sword"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_weapon_lamplighter_sword, Family.Light),
            new BaseDef("weapon.shadowfang_knife", Slot.Weapon, Line.Offense, new Txt("影牙の短刀", "Shadowfang Knife"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_weapon_shadowfang_knife, Family.Dark),
            new BaseDef("weapon.nightreaver_axe", Slot.Weapon, Line.Offense, new Txt("夜刈りの斧", "Nightreaver Axe"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_nightreaver_axe, Family.Dark),
            new BaseDef("weapon.eclipsed_rapier", Slot.Weapon, Line.Offense, new Txt("蝕刻の突剣", "Eclipsed Rapier"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_eclipsed_rapier, Family.Dark),
            new BaseDef("weapon.shadowstitch_kodachi", Slot.Weapon, Line.Offense, new Txt("影縫いの小太刀", "Shadowstitch Kodachi"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_shadowstitch_kodachi, Family.Dark),
            new BaseDef("weapon.sentry_halberd", Slot.Weapon, Line.Guard, new Txt("哨兵の斧槍", "Sentry's Halberd"), Stat.Armor, EquipmentItemsBalanceValues.Base_weapon_sentry_halberd, Family.Guard),
            new BaseDef("weapon.bulwark_greatsword", Slot.Weapon, Line.Guard, new Txt("壁衛の大剣", "Bulwark Greatsword"), Stat.Armor, EquipmentItemsBalanceValues.Base_weapon_bulwark_greatsword, Family.Guard),
            new BaseDef("weapon.oakheart_club", Slot.Weapon, Line.Guard, new Txt("樫心の棍", "Oakheart Club"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_weapon_oakheart_club, Family.Guard),
            new BaseDef("weapon.vanguard_hammer", Slot.Weapon, Line.Guard, new Txt("先陣の戦鎚", "Vanguard Hammer"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_weapon_vanguard_hammer, Family.Guard),
            new BaseDef("weapon.whirlwind_naginata", Slot.Weapon, Line.Offense, new Txt("旋風の薙刀", "Whirlwind Naginata"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_weapon_whirlwind_naginata, Family.Gale),
            new BaseDef("weapon.gale_tossed_blades", Slot.Weapon, Line.Offense, new Txt("疾風の投刃", "Gale-Tossed Blades"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_weapon_gale_tossed_blades, Family.Gale),
            new BaseDef("weapon.stormstring_bow", Slot.Weapon, Line.Offense, new Txt("風鳴りの弓", "Stormstring Bow"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_stormstring_bow, Family.Gale),
            new BaseDef("weapon.tailwind_saber", Slot.Weapon, Line.Resonance, new Txt("追風の細剣", "Tailwind Saber"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_tailwind_saber, Family.Gale),
            new BaseDef("weapon.mercy_staff", Slot.Weapon, Line.Resonance, new Txt("慈悲の杖", "Staff of Mercy"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_weapon_mercy_staff, Family.Mend),
            new BaseDef("weapon.herbalist_sickle", Slot.Weapon, Line.Resonance, new Txt("薬草鎌", "Herbalist's Sickle"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_herbalist_sickle, Family.Mend),
            new BaseDef("weapon.soothing_chime_staff", Slot.Weapon, Line.Guard, new Txt("癒し鈴の杖", "Soothing-Chime Staff"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_weapon_soothing_chime_staff, Family.Mend),
            new BaseDef("weapon.dewdrop_wand", Slot.Weapon, Line.Resonance, new Txt("露滴の細杖", "Dewdrop Wand"), Stat.Haste, EquipmentItemsBalanceValues.Base_weapon_dewdrop_wand, Family.Mend),
            new BaseDef("weapon.callers_greatsword", Slot.Weapon, Line.Resonance, new Txt("喚起の大剣", "Caller's Greatsword"), Stat.SummonPower, EquipmentItemsBalanceValues.Base_weapon_callers_greatsword, Family.Summon),
            new BaseDef("weapon.beast_tamer_whip", Slot.Weapon, Line.Offense, new Txt("獣使いの鞭", "Beast-Tamer's Whip"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_weapon_beast_tamer_whip, Family.Summon),
            new BaseDef("weapon.pact_dagger", Slot.Weapon, Line.Offense, new Txt("契約の短剣", "Pact Dagger"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_weapon_pact_dagger, Family.Summon),
            new BaseDef("weapon.spirit_call_lance", Slot.Weapon, Line.Offense, new Txt("霊呼びの槍", "Spirit-Call Lance"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_weapon_spirit_call_lance, Family.Summon),
            new BaseDef("weapon.remembrance_blade", Slot.Weapon, Line.Resonance, new Txt("追憶の剣", "Remembrance Blade"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_remembrance_blade, Family.Memory),
            new BaseDef("weapon.yesteryear_staff", Slot.Weapon, Line.Resonance, new Txt("在りし日の杖", "Yesteryear Staff"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_weapon_yesteryear_staff, Family.Memory),
            new BaseDef("weapon.dusk_memory_scythe", Slot.Weapon, Line.Resonance, new Txt("夕闇の追憶鎌", "Dusk-Memory Scythe"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_weapon_dusk_memory_scythe, Family.Memory),
            new BaseDef("weapon.relic_hunter_blade", Slot.Weapon, Line.Offense, new Txt("遺跡巡りの刀", "Relic-Hunter Blade"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_weapon_relic_hunter_blade, Family.Memory),
            new BaseDef("armor.sturdy_jerkin", Slot.Armor, Line.Guard, new Txt("頑丈な革胴", "Sturdy Jerkin"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_sturdy_jerkin),
            new BaseDef("armor.militia_cuirass", Slot.Armor, Line.Guard, new Txt("郷勇の胸当て", "Militia Cuirass"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_armor_militia_cuirass),
            new BaseDef("armor.roadworn_jacket", Slot.Armor, Line.Offense, new Txt("旅路の上着", "Roadworn Jacket"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_armor_roadworn_jacket),
            new BaseDef("armor.padded_vest", Slot.Armor, Line.Guard, new Txt("詰め物の胴着", "Padded Vest"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_padded_vest),
            new BaseDef("armor.hoarfrost_mail", Slot.Armor, Line.Resonance, new Txt("霧氷の鎖帷子", "Hoarfrost Mail"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_armor_hoarfrost_mail, Family.Frost),
            new BaseDef("armor.frozen_rampart", Slot.Armor, Line.Guard, new Txt("氷塁の胸当て", "Frozen Rampart"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_frozen_rampart, Family.Frost),
            new BaseDef("armor.snowdrift_coat", Slot.Armor, Line.Guard, new Txt("雪どけの上衣", "Snowdrift Coat"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_snowdrift_coat, Family.Frost),
            new BaseDef("armor.iceskimmer_wrap", Slot.Armor, Line.Resonance, new Txt("氷滑りの帯衣", "Iceskimmer Wrap"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_armor_iceskimmer_wrap, Family.Frost),
            new BaseDef("armor.cinderplate", Slot.Armor, Line.Offense, new Txt("燃殻の板金鎧", "Cinderplate"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_armor_cinderplate, Family.Flame),
            new BaseDef("armor.furnace_guard", Slot.Armor, Line.Guard, new Txt("火床の胸当て", "Furnace Guard"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_armor_furnace_guard, Family.Flame),
            new BaseDef("armor.ember_flare_jacket", Slot.Armor, Line.Offense, new Txt("火焔閃の上着", "Ember-Flare Jacket"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_armor_ember_flare_jacket, Family.Flame),
            new BaseDef("armor.slowfire_bandolier", Slot.Armor, Line.Guard, new Txt("燠りの帯衣", "Slowfire Bandolier"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_armor_slowfire_bandolier, Family.Flame),
            new BaseDef("armor.dawnweave_harness", Slot.Armor, Line.Resonance, new Txt("暁織りの胸綱", "Dawnweave Harness"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_armor_dawnweave_harness, Family.Light),
            new BaseDef("armor.sanctum_vest", Slot.Armor, Line.Resonance, new Txt("聖域の胴衣", "Sanctum Vest"), Stat.HealPower, EquipmentItemsBalanceValues.Base_armor_sanctum_vest, Family.Light),
            new BaseDef("armor.clearsky_corset", Slot.Armor, Line.Offense, new Txt("晴空の胴衣", "Clear-Sky Corset"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_armor_clearsky_corset, Family.Light),
            new BaseDef("armor.lamplight_sash", Slot.Armor, Line.Resonance, new Txt("灯火の飾り帯", "Lamplight Sash"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_lamplight_sash, Family.Light),
            new BaseDef("armor.midnight_hauberk", Slot.Armor, Line.Offense, new Txt("真夜中の鎖帷子", "Midnight Hauberk"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_armor_midnight_hauberk, Family.Dark),
            new BaseDef("armor.wraithweave_coat", Slot.Armor, Line.Offense, new Txt("幽織りの外套", "Wraithweave Coat"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_armor_wraithweave_coat, Family.Dark),
            new BaseDef("armor.nightprowler_jacket", Slot.Armor, Line.Offense, new Txt("夜盗の上着", "Nightprowler's Jacket"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_armor_nightprowler_jacket, Family.Dark),
            new BaseDef("armor.shadowstep_cloak", Slot.Armor, Line.Resonance, new Txt("影歩みの外套", "Shadowstep Cloak"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_armor_shadowstep_cloak, Family.Dark),
            new BaseDef("armor.keepers_plate", Slot.Armor, Line.Guard, new Txt("預かり人の板金鎧", "Keeper's Plate"), Stat.Armor, EquipmentItemsBalanceValues.Base_armor_keepers_plate, Family.Guard),
            new BaseDef("armor.deepwell_cuirass", Slot.Armor, Line.Guard, new Txt("深井戸の胴鎧", "Deepwell Cuirass"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_armor_deepwell_cuirass, Family.Guard),
            new BaseDef("armor.unyielding_ply", Slot.Armor, Line.Guard, new Txt("屈しぬ腹当て", "Unyielding Ply"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_armor_unyielding_ply, Family.Guard),
            new BaseDef("armor.warden_sigil_vest", Slot.Armor, Line.Guard, new Txt("番印の胴衣", "Warden-Sigil Vest"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_armor_warden_sigil_vest, Family.Guard),
            new BaseDef("armor.windcutter_vest", Slot.Armor, Line.Offense, new Txt("風切りの胸当て", "Windcutter Vest"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_armor_windcutter_vest, Family.Gale),
            new BaseDef("armor.skyclad_sash", Slot.Armor, Line.Resonance, new Txt("天衣の帯", "Skyclad Sash"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_skyclad_sash, Family.Gale),
            new BaseDef("armor.darting_wrap", Slot.Armor, Line.Offense, new Txt("疾走の帯衣", "Darting Wrap"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_armor_darting_wrap, Family.Gale),
            new BaseDef("armor.cyclone_cloak", Slot.Armor, Line.Offense, new Txt("竜巻の外套", "Cyclone Cloak"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_armor_cyclone_cloak, Family.Gale),
            new BaseDef("armor.herb_pouch_vest", Slot.Armor, Line.Resonance, new Txt("薬袋の胴着", "Herb-Pouch Vest"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_herb_pouch_vest, Family.Mend),
            new BaseDef("armor.gentle_rain_jacket", Slot.Armor, Line.Resonance, new Txt("慈雨の上着", "Gentle-Rain Jacket"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_gentle_rain_jacket, Family.Mend),
            new BaseDef("armor.bandage_wrap", Slot.Armor, Line.Guard, new Txt("包帯の巻衣", "Bandage Wrap"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_armor_bandage_wrap, Family.Mend),
            new BaseDef("armor.longevity_coat", Slot.Armor, Line.Resonance, new Txt("長命の上衣", "Longevity Coat"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_armor_longevity_coat, Family.Mend),
            new BaseDef("armor.whistle_call_vest", Slot.Armor, Line.Offense, new Txt("呼笛の胴衣", "Whistle-Call Vest"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_armor_whistle_call_vest, Family.Summon),
            new BaseDef("armor.lair_woven_mail", Slot.Armor, Line.Guard, new Txt("巣織りの帷子", "Lair-Woven Mail"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_armor_lair_woven_mail, Family.Summon),
            new BaseDef("armor.familiars_shawl", Slot.Armor, Line.Resonance, new Txt("使い魔の肩掛け", "Familiar's Shawl"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_familiars_shawl, Family.Summon),
            new BaseDef("armor.pack_leader_wrap", Slot.Armor, Line.Offense, new Txt("群れ率びの腹巻", "Pack-Leader Wrap"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_armor_pack_leader_wrap, Family.Summon),
            new BaseDef("armor.annals_robe", Slot.Armor, Line.Resonance, new Txt("年代記の法衣", "Annals Robe"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_armor_annals_robe, Family.Memory),
            new BaseDef("armor.afterglow_vest", Slot.Armor, Line.Resonance, new Txt("名残りの胴衣", "Afterglow Vest"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_armor_afterglow_vest, Family.Memory),
            new BaseDef("armor.keepsake_cloak", Slot.Armor, Line.Resonance, new Txt("形見の外套", "Keepsake Cloak"), Stat.Haste, EquipmentItemsBalanceValues.Base_armor_keepsake_cloak, Family.Memory),
            new BaseDef("armor.lingering_image_coat", Slot.Armor, Line.Guard, new Txt("面影の上衣", "Lingering-Image Coat"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_armor_lingering_image_coat, Family.Memory),
            new BaseDef("charm.copper_band", Slot.Charm, Line.Offense, new Txt("銅の腕輪", "Copper Band"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_copper_band),
            new BaseDef("charm.rough_hewn_charm", Slot.Charm, Line.Offense, new Txt("荒削りの御守り", "Rough-Hewn Charm"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_rough_hewn_charm),
            new BaseDef("charm.simple_sigil", Slot.Charm, Line.Resonance, new Txt("素朴な印章", "Simple Sigil"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_simple_sigil),
            new BaseDef("charm.journeythread_ring", Slot.Charm, Line.Resonance, new Txt("旅緒の指輪", "Journeythread Ring"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_charm_journeythread_ring),
            new BaseDef("charm.glacier_locket", Slot.Charm, Line.Guard, new Txt("氷晶のロケット", "Glacier Locket"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_charm_glacier_locket, Family.Frost),
            new BaseDef("charm.frostlock_ring", Slot.Charm, Line.Guard, new Txt("霜鎖の指輪", "Frostlock Ring"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_charm_frostlock_ring, Family.Frost),
            new BaseDef("charm.frost_breath_charm", Slot.Charm, Line.Guard, new Txt("白息の御守り", "Frost-Breath Charm"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_charm_frost_breath_charm, Family.Frost),
            new BaseDef("charm.north_sky_charm", Slot.Charm, Line.Resonance, new Txt("北空の飾り", "North-Sky Charm"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_north_sky_charm, Family.Frost),
            new BaseDef("charm.sparkcase_pendant", Slot.Charm, Line.Offense, new Txt("火種の垂飾り", "Sparkcase Pendant"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_sparkcase_pendant, Family.Flame),
            new BaseDef("charm.melt_ore_locket", Slot.Charm, Line.Offense, new Txt("熔鉱のロケット", "Melt-Ore Locket"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_melt_ore_locket, Family.Flame),
            new BaseDef("charm.flarewheel_brooch", Slot.Charm, Line.Offense, new Txt("火輪のブローチ", "Flarewheel Brooch"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_charm_flarewheel_brooch, Family.Flame),
            new BaseDef("charm.tinder_band", Slot.Charm, Line.Offense, new Txt("火口の腕輪", "Tinder Band"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_charm_tinder_band, Family.Flame),
            new BaseDef("charm.sunrise_locket", Slot.Charm, Line.Resonance, new Txt("朝焼けのロケット", "Sunrise Locket"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_charm_sunrise_locket, Family.Light),
            new BaseDef("charm.kindness_beads", Slot.Charm, Line.Resonance, new Txt("慈愛の数珠", "Beads of Kindness"), Stat.HealPower, EquipmentItemsBalanceValues.Base_charm_kindness_beads, Family.Light),
            new BaseDef("charm.sanctuary_seal", Slot.Charm, Line.Guard, new Txt("聖域の印章", "Sanctuary Seal"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_charm_sanctuary_seal, Family.Light),
            new BaseDef("charm.broad_day_charm", Slot.Charm, Line.Resonance, new Txt("白昼の御守り", "Broad-Daylight Charm"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_broad_day_charm, Family.Light),
            new BaseDef("charm.night_owl_charm", Slot.Charm, Line.Offense, new Txt("梟の御守り", "Night-Owl Charm"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_charm_night_owl_charm, Family.Dark),
            new BaseDef("charm.grave_bell_ring", Slot.Charm, Line.Offense, new Txt("墓鈴の指輪", "Grave-Bell Ring"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_charm_grave_bell_ring, Family.Dark),
            new BaseDef("charm.funeral_band", Slot.Charm, Line.Offense, new Txt("葬列の腕輪", "Funeral Band"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_funeral_band, Family.Dark),
            new BaseDef("charm.dusk_mist_band", Slot.Charm, Line.Resonance, new Txt("宵霧の腕輪", "Dusk-Mist Band"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_charm_dusk_mist_band, Family.Dark),
            new BaseDef("charm.heavy_chain_charm", Slot.Charm, Line.Guard, new Txt("重鎖の飾り", "Heavy-Chain Charm"), Stat.Armor, EquipmentItemsBalanceValues.Base_charm_heavy_chain_charm, Family.Guard),
            new BaseDef("charm.immovable_ring", Slot.Charm, Line.Guard, new Txt("不動の指輪", "Immovable Ring"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_charm_immovable_ring, Family.Guard),
            new BaseDef("charm.wellspring_amulet", Slot.Charm, Line.Guard, new Txt("湧き泉の護符", "Wellspring Amulet"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_charm_wellspring_amulet, Family.Guard),
            new BaseDef("charm.watchpost_badge", Slot.Charm, Line.Guard, new Txt("見張り番の徽章", "Watchpost Badge"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_charm_watchpost_badge, Family.Guard),
            new BaseDef("charm.tailwind_plume", Slot.Charm, Line.Offense, new Txt("追風の羽飾り", "Tailwind Plume"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_charm_tailwind_plume, Family.Gale),
            new BaseDef("charm.skylark_bell", Slot.Charm, Line.Resonance, new Txt("雲雀の鈴", "Skylark Bell"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_skylark_bell, Family.Gale),
            new BaseDef("charm.far_flight_charm", Slot.Charm, Line.Offense, new Txt("遠翔の御守り", "Far-Flight Charm"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_charm_far_flight_charm, Family.Gale),
            new BaseDef("charm.gust_band", Slot.Charm, Line.Offense, new Txt("疾風の腕輪", "Gust Band"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_gust_band, Family.Gale),
            new BaseDef("charm.healing_sachet", Slot.Charm, Line.Resonance, new Txt("癒しの香袋", "Healing Sachet"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_healing_sachet, Family.Mend),
            new BaseDef("charm.restful_heart_ring", Slot.Charm, Line.Resonance, new Txt("心休まる指輪", "Restful-Heart Ring"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_restful_heart_ring, Family.Mend),
            new BaseDef("charm.lotus_bloom_seal", Slot.Charm, Line.Guard, new Txt("蓮華の印", "Lotus-Bloom Seal"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_charm_lotus_bloom_seal, Family.Mend),
            new BaseDef("charm.salve_case_charm", Slot.Charm, Line.Guard, new Txt("薬盒の飾り", "Salve-Case Charm"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_charm_salve_case_charm, Family.Mend),
            new BaseDef("charm.packfang_pendant", Slot.Charm, Line.Offense, new Txt("群牙の垂飾り", "Packfang Pendant"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_charm_packfang_pendant, Family.Summon),
            new BaseDef("charm.bone_whistle_charm", Slot.Charm, Line.Resonance, new Txt("骨笛の飾り", "Bone-Whistle Charm"), Stat.Haste, EquipmentItemsBalanceValues.Base_charm_bone_whistle_charm, Family.Summon),
            new BaseDef("charm.wildheart_locket", Slot.Charm, Line.Offense, new Txt("野性のロケット", "Wildheart Locket"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_charm_wildheart_locket, Family.Summon),
            new BaseDef("charm.rein_ring", Slot.Charm, Line.Offense, new Txt("手綱の指輪", "Rein Ring"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_charm_rein_ring, Family.Summon),
            new BaseDef("charm.unaddressed_letter", Slot.Charm, Line.Resonance, new Txt("宛先なき手紙", "Unaddressed Letter"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_charm_unaddressed_letter, Family.Memory),
            new BaseDef("charm.old_coin_charm", Slot.Charm, Line.Resonance, new Txt("古銭の御守り", "Old-Coin Charm"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_charm_old_coin_charm, Family.Memory),
            new BaseDef("charm.forgotten_name_charm", Slot.Charm, Line.Resonance, new Txt("忘れ名の飾り", "Forgotten-Name Charm"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_forgotten_name_charm, Family.Memory),
            new BaseDef("charm.memory_bookmark", Slot.Charm, Line.Resonance, new Txt("思い出の栞", "Bookmark of Memories"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_charm_memory_bookmark, Family.Memory),
            new BaseDef("head.broad_brim_hat", Slot.Head, Line.Offense, new Txt("つばの広い帽子", "Broad-Brim Hat"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_broad_brim_hat),
            new BaseDef("head.cotton_hood", Slot.Head, Line.Offense, new Txt("木綿の頭巾", "Cotton Hood"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_head_cotton_hood),
            new BaseDef("head.straw_hat", Slot.Head, Line.Offense, new Txt("麦わら帽子", "Straw Hat"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_straw_hat),
            new BaseDef("head.sweatband", Slot.Head, Line.Guard, new Txt("汗の鉢巻", "Sweatband"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_head_sweatband),
            new BaseDef("head.aurora_circlet", Slot.Head, Line.Resonance, new Txt("極光の額冠", "Aurora Circlet"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_head_aurora_circlet, Family.Frost),
            new BaseDef("head.snowlight_hood", Slot.Head, Line.Guard, new Txt("雪明りの頭巾", "Snowlight Hood"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_head_snowlight_hood, Family.Frost),
            new BaseDef("head.everfrost_hood", Slot.Head, Line.Guard, new Txt("不凍の頭巾", "Everfrost Hood"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_head_everfrost_hood, Family.Frost),
            new BaseDef("head.icicle_crown", Slot.Head, Line.Guard, new Txt("氷柱の冠", "Icicle Crown"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_icicle_crown, Family.Frost),
            new BaseDef("head.brazier_crown", Slot.Head, Line.Offense, new Txt("火鉢の冠", "Brazier Crown"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_head_brazier_crown, Family.Flame),
            new BaseDef("head.flashfire_band", Slot.Head, Line.Offense, new Txt("火閃の鉢巻", "Flashfire Band"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_flashfire_band, Family.Flame),
            new BaseDef("head.scorched_mask", Slot.Head, Line.Offense, new Txt("焦土の面", "Scorched-Earth Mask"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_head_scorched_mask, Family.Flame),
            new BaseDef("head.forge_hood", Slot.Head, Line.Guard, new Txt("鍛冶場の頭巾", "Forge-Hood"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_head_forge_hood, Family.Flame),
            new BaseDef("head.hope_circlet", Slot.Head, Line.Resonance, new Txt("望みの額冠", "Circlet of Hope"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_head_hope_circlet, Family.Light),
            new BaseDef("head.nurses_veil", Slot.Head, Line.Resonance, new Txt("看護のヴェール", "Nurse's Veil"), Stat.HealPower, EquipmentItemsBalanceValues.Base_head_nurses_veil, Family.Light),
            new BaseDef("head.first_light_band", Slot.Head, Line.Resonance, new Txt("夜明けの鉢巻", "First-Light Band"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_first_light_band, Family.Light),
            new BaseDef("head.clear_gaze_glasses", Slot.Head, Line.Offense, new Txt("澄眼の眼鏡", "Clear-Gaze Glasses"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_head_clear_gaze_glasses, Family.Light),
            new BaseDef("head.nightmare_visage", Slot.Head, Line.Offense, new Txt("悪夢の面", "Nightmare Visage"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_nightmare_visage, Family.Dark),
            new BaseDef("head.funeral_crown", Slot.Head, Line.Offense, new Txt("葬列の冠", "Funeral Crown"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_head_funeral_crown, Family.Dark),
            new BaseDef("head.shroud_hood", Slot.Head, Line.Offense, new Txt("死装束の頭巾", "Shroud Hood"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_shroud_hood, Family.Dark),
            new BaseDef("head.moth_wing_mask", Slot.Head, Line.Resonance, new Txt("蛾翅の仮面", "Moth-Wing Mask"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_head_moth_wing_mask, Family.Dark),
            new BaseDef("head.siege_helm", Slot.Head, Line.Guard, new Txt("攻城戦の兜", "Siege Helm"), Stat.Armor, EquipmentItemsBalanceValues.Base_head_siege_helm, Family.Guard),
            new BaseDef("head.locked_visor", Slot.Head, Line.Guard, new Txt("閉扉の面頬", "Locked Visor"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_head_locked_visor, Family.Guard),
            new BaseDef("head.patrol_hood", Slot.Head, Line.Guard, new Txt("巡視の頭巾", "Patrol Hood"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_head_patrol_hood, Family.Guard),
            new BaseDef("head.heartwood_circlet", Slot.Head, Line.Guard, new Txt("心材の冠", "Heartwood Circlet"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_head_heartwood_circlet, Family.Guard),
            new BaseDef("head.sprinting_veil", Slot.Head, Line.Resonance, new Txt("疾走のヴェール", "Sprinting Veil"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_sprinting_veil, Family.Gale),
            new BaseDef("head.kite_mask", Slot.Head, Line.Offense, new Txt("凧の面", "Kite Mask"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_head_kite_mask, Family.Gale),
            new BaseDef("head.wind_crest_band", Slot.Head, Line.Offense, new Txt("風紋の鉢巻", "Wind-Crest Band"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_wind_crest_band, Family.Gale),
            new BaseDef("head.far_horizon_cap", Slot.Head, Line.Offense, new Txt("遠地平の帽子", "Far-Horizon Cap"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_head_far_horizon_cap, Family.Gale),
            new BaseDef("head.pilgrim_doctor_hat", Slot.Head, Line.Guard, new Txt("巡礼医の帽子", "Pilgrim-Doctor Hat"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_head_pilgrim_doctor_hat, Family.Mend),
            new BaseDef("head.balm_band", Slot.Head, Line.Resonance, new Txt("香膏の鉢巻", "Balm Band"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_head_balm_band, Family.Mend),
            new BaseDef("head.soothing_wreath", Slot.Head, Line.Guard, new Txt("癒やしの花冠", "Soothing Wreath"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_head_soothing_wreath, Family.Mend),
            new BaseDef("head.quiet_prayer_veil", Slot.Head, Line.Guard, new Txt("静祷のヴェール", "Quiet-Prayer Veil"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_head_quiet_prayer_veil, Family.Mend),
            new BaseDef("head.beast_ear_hood", Slot.Head, Line.Offense, new Txt("獣耳の頭巾", "Beast-Ear Hood"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_head_beast_ear_hood, Family.Summon),
            new BaseDef("head.falconers_hood", Slot.Head, Line.Offense, new Txt("鷹狩りの頭巾", "Falconer's Hood"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_head_falconers_hood, Family.Summon),
            new BaseDef("head.den_mother_circlet", Slot.Head, Line.Guard, new Txt("巣守りの額冠", "Den-Mother Circlet"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_head_den_mother_circlet, Family.Summon),
            new BaseDef("head.calling_whistle_band", Slot.Head, Line.Resonance, new Txt("呼び笛の鉢巻", "Calling-Whistle Band"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_calling_whistle_band, Family.Summon),
            new BaseDef("head.nostalgia_band", Slot.Head, Line.Resonance, new Txt("追憶の鉢巻", "Nostalgia Band"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_head_nostalgia_band, Family.Memory),
            new BaseDef("head.old_tales_hood", Slot.Head, Line.Resonance, new Txt("昔話の頭巾", "Old-Tales Hood"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_head_old_tales_hood, Family.Memory),
            new BaseDef("head.star_calendar_cap", Slot.Head, Line.Resonance, new Txt("星暦の帽子", "Star-Calendar Cap"), Stat.Haste, EquipmentItemsBalanceValues.Base_head_star_calendar_cap, Family.Memory),
            new BaseDef("head.relic_eye_monocle", Slot.Head, Line.Offense, new Txt("遺物眼の単眼鏡", "Relic-Eye Monocle"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_head_relic_eye_monocle, Family.Memory),
            new BaseDef("hands.work_gloves", Slot.Hands, Line.Offense, new Txt("作業手袋", "Work Gloves"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_work_gloves),
            new BaseDef("hands.tumblers_gloves", Slot.Hands, Line.Offense, new Txt("軽業の手袋", "Tumbler's Gloves"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_tumblers_gloves),
            new BaseDef("hands.iron_thumb_gauntlet", Slot.Hands, Line.Offense, new Txt("鉄親指の籠手", "Iron-Thumb Gauntlet"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_iron_thumb_gauntlet),
            new BaseDef("hands.cotton_wraps", Slot.Hands, Line.Resonance, new Txt("木綿の手巻き", "Cotton Wraps"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_hands_cotton_wraps),
            new BaseDef("hands.rimeguard_gauntlets", Slot.Hands, Line.Resonance, new Txt("霧氷の籠手", "Rimeguard Gauntlets"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_hands_rimeguard_gauntlets, Family.Frost),
            new BaseDef("hands.frozen_grip", Slot.Hands, Line.Guard, new Txt("氷結の握り", "Frozen Grip"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_hands_frozen_grip, Family.Frost),
            new BaseDef("hands.snow_weight_gloves", Slot.Hands, Line.Offense, new Txt("雪重りの手袋", "Snow-Weight Gloves"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_snow_weight_gloves, Family.Frost),
            new BaseDef("hands.winter_ready_gloves", Slot.Hands, Line.Guard, new Txt("冬支度の手袋", "Winter-Ready Gloves"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_hands_winter_ready_gloves, Family.Frost),
            new BaseDef("hands.volcanic_grips", Slot.Hands, Line.Offense, new Txt("火山岩の握り", "Volcanic Grips"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_hands_volcanic_grips, Family.Flame),
            new BaseDef("hands.fire_tong_mitts", Slot.Hands, Line.Offense, new Txt("火箸の指なし", "Fire-Tong Mitts"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_fire_tong_mitts, Family.Flame),
            new BaseDef("hands.slag_knuckles", Slot.Hands, Line.Offense, new Txt("鉱滓の拳当て", "Slag Knuckles"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_slag_knuckles, Family.Flame),
            new BaseDef("hands.brand_iron_fingerless", Slot.Hands, Line.Offense, new Txt("焼印の指抜き", "Brand-Iron Fingerless"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_brand_iron_fingerless, Family.Flame),
            new BaseDef("hands.morninglight_gloves", Slot.Hands, Line.Resonance, new Txt("朝光の手袋", "Morninglight Gloves"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_hands_morninglight_gloves, Family.Light),
            new BaseDef("hands.first_aid_mitts", Slot.Hands, Line.Resonance, new Txt("手当ての指なし", "First-Aid Mitts"), Stat.HealPower, EquipmentItemsBalanceValues.Base_hands_first_aid_mitts, Family.Light),
            new BaseDef("hands.gospel_gloves", Slot.Hands, Line.Resonance, new Txt("福音の手袋", "Gospel Gloves"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_hands_gospel_gloves, Family.Light),
            new BaseDef("hands.sunhold_bands", Slot.Hands, Line.Resonance, new Txt("日向の腕輪", "Sunhold Bands"), Stat.Haste, EquipmentItemsBalanceValues.Base_hands_sunhold_bands, Family.Light),
            new BaseDef("hands.gravediggers_claws", Slot.Hands, Line.Offense, new Txt("墓掘りの爪", "Gravedigger's Claws"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_hands_gravediggers_claws, Family.Dark),
            new BaseDef("hands.assassins_fingerless", Slot.Hands, Line.Offense, new Txt("暗殺者の指抜き", "Assassin's Fingerless"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_assassins_fingerless, Family.Dark),
            new BaseDef("hands.gloomfang_knuckles", Slot.Hands, Line.Offense, new Txt("闇牙の拳当て", "Gloomfang Knuckles"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_gloomfang_knuckles, Family.Dark),
            new BaseDef("hands.night_rose_gloves", Slot.Hands, Line.Resonance, new Txt("夜薔薇の手袋", "Night-Rose Gloves"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_hands_night_rose_gloves, Family.Dark),
            new BaseDef("hands.bastion_mitts", Slot.Hands, Line.Guard, new Txt("砦の指なし", "Bastion Mitts"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_bastion_mitts, Family.Guard),
            new BaseDef("hands.anvil_gauntlets", Slot.Hands, Line.Guard, new Txt("金床の籠手", "Anvil Gauntlets"), Stat.Armor, EquipmentItemsBalanceValues.Base_hands_anvil_gauntlets, Family.Guard),
            new BaseDef("hands.steadfast_grips", Slot.Hands, Line.Guard, new Txt("不動の握り", "Steadfast Grips"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_hands_steadfast_grips, Family.Guard),
            new BaseDef("hands.iron_will_wraps", Slot.Hands, Line.Guard, new Txt("鉄意志の手巻き", "Iron-Will Wraps"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_hands_iron_will_wraps, Family.Guard),
            new BaseDef("hands.quicksilver_bands", Slot.Hands, Line.Offense, new Txt("迅銀の腕輪", "Quicksilver Bands"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_quicksilver_bands, Family.Gale),
            new BaseDef("hands.galefinger_gloves", Slot.Hands, Line.Offense, new Txt("風指の手袋", "Galefinger Gloves"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_galefinger_gloves, Family.Gale),
            new BaseDef("hands.hawking_gloves", Slot.Hands, Line.Offense, new Txt("鷹寄せの手袋", "Hawking Gloves"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_hawking_gloves, Family.Gale),
            new BaseDef("hands.windstep_wraps", Slot.Hands, Line.Resonance, new Txt("風歩きの手巻き", "Windstep Wraps"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_hands_windstep_wraps, Family.Gale),
            new BaseDef("hands.repair_gloves", Slot.Hands, Line.Resonance, new Txt("繕いの手袋", "Repair Gloves"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_hands_repair_gloves, Family.Mend),
            new BaseDef("hands.supporting_gloves", Slot.Hands, Line.Guard, new Txt("支えの手袋", "Supporting Gloves"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_hands_supporting_gloves, Family.Mend),
            new BaseDef("hands.splint_wraps", Slot.Hands, Line.Guard, new Txt("副木の手巻き", "Splint Wraps"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_hands_splint_wraps, Family.Mend),
            new BaseDef("hands.spring_breeze_grips", Slot.Hands, Line.Resonance, new Txt("春風の握り", "Spring-Breeze Grips"), Stat.Haste, EquipmentItemsBalanceValues.Base_hands_spring_breeze_grips, Family.Mend),
            new BaseDef("hands.beasthide_gloves", Slot.Hands, Line.Offense, new Txt("獣皮の手袋", "Beasthide Gloves"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_hands_beasthide_gloves, Family.Summon),
            new BaseDef("hands.tamers_mitts", Slot.Hands, Line.Offense, new Txt("調教の指なし", "Tamer's Mitts"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_hands_tamers_mitts, Family.Summon),
            new BaseDef("hands.alpha_grips", Slot.Hands, Line.Offense, new Txt("頭領の握り", "Alpha Grips"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_hands_alpha_grips, Family.Summon),
            new BaseDef("hands.kennel_keeper_gloves", Slot.Hands, Line.Guard, new Txt("番犬飼いの手袋", "Kennel-Keeper Gloves"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_hands_kennel_keeper_gloves, Family.Summon),
            new BaseDef("hands.palimpsest_gloves", Slot.Hands, Line.Resonance, new Txt("重写本の手袋", "Palimpsest Gloves"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_hands_palimpsest_gloves, Family.Memory),
            new BaseDef("hands.keepsake_bands", Slot.Hands, Line.Resonance, new Txt("形見の腕輪", "Keepsake Bands"), Stat.Haste, EquipmentItemsBalanceValues.Base_hands_keepsake_bands, Family.Memory),
            new BaseDef("hands.tale_teller_gloves", Slot.Hands, Line.Offense, new Txt("昔語りの手袋", "Tale-Teller Gloves"), Stat.CritChancePct, EquipmentItemsBalanceValues.Base_hands_tale_teller_gloves, Family.Memory),
            new BaseDef("hands.duskthread_wraps", Slot.Hands, Line.Resonance, new Txt("夕糸の手巻き", "Duskthread Wraps"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_hands_duskthread_wraps, Family.Memory),
            new BaseDef("feet.hobnail_boots", Slot.Feet, Line.Guard, new Txt("鉄釘の長靴", "Hobnail Boots"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_hobnail_boots),
            new BaseDef("feet.everyday_shoes", Slot.Feet, Line.Offense, new Txt("日常の靴", "Everyday Shoes"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_everyday_shoes),
            new BaseDef("feet.work_boots", Slot.Feet, Line.Offense, new Txt("仕事靴", "Work Boots"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_feet_work_boots),
            new BaseDef("feet.earth_stained_sandals", Slot.Feet, Line.Resonance, new Txt("土の付いた草鞋", "Earth-Stained Sandals"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_earth_stained_sandals),
            new BaseDef("feet.blizzard_gaiters", Slot.Feet, Line.Resonance, new Txt("吹雪の脚絆", "Blizzard Gaiters"), Stat.ColdAmp, EquipmentItemsBalanceValues.Base_feet_blizzard_gaiters, Family.Frost),
            new BaseDef("feet.floe_skimmer_shoes", Slot.Feet, Line.Guard, new Txt("流氷滑りの靴", "Floe-Skimmer Shoes"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_feet_floe_skimmer_shoes, Family.Frost),
            new BaseDef("feet.ice_road_boots", Slot.Feet, Line.Guard, new Txt("氷道の長靴", "Ice-Road Boots"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_feet_ice_road_boots, Family.Frost),
            new BaseDef("feet.frostwind_slippers", Slot.Feet, Line.Resonance, new Txt("霜風の上履き", "Frostwind Slippers"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_frostwind_slippers, Family.Frost),
            new BaseDef("feet.scorching_footwear", Slot.Feet, Line.Offense, new Txt("灼熱の履き物", "Scorching Footwear"), Stat.FireAmp, EquipmentItemsBalanceValues.Base_feet_scorching_footwear, Family.Flame),
            new BaseDef("feet.firewalk_sandals", Slot.Feet, Line.Offense, new Txt("火渡りの草鞋", "Firewalk Sandals"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_feet_firewalk_sandals, Family.Flame),
            new BaseDef("feet.cinder_trail_shoes", Slot.Feet, Line.Offense, new Txt("燼跡の靴", "Cinder-Trail Shoes"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_cinder_trail_shoes, Family.Flame),
            new BaseDef("feet.smelter_boots", Slot.Feet, Line.Offense, new Txt("溶鉱の長靴", "Smelter Boots"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_feet_smelter_boots, Family.Flame),
            new BaseDef("feet.sunpath_sandals", Slot.Feet, Line.Resonance, new Txt("日の道の草鞋", "Sunpath Sandals"), Stat.LightAmp, EquipmentItemsBalanceValues.Base_feet_sunpath_sandals, Family.Light),
            new BaseDef("feet.pilgrim_light_shoes", Slot.Feet, Line.Resonance, new Txt("巡礼灯の靴", "Pilgrim-Light Shoes"), Stat.HealPower, EquipmentItemsBalanceValues.Base_feet_pilgrim_light_shoes, Family.Light),
            new BaseDef("feet.morningstar_shoes", Slot.Feet, Line.Resonance, new Txt("暁星の靴", "Morningstar Shoes"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_morningstar_shoes, Family.Light),
            new BaseDef("feet.beacon_steps", Slot.Feet, Line.Resonance, new Txt("灯台の足取り", "Beacon Steps"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_beacon_steps, Family.Light),
            new BaseDef("feet.graveshade_boots", Slot.Feet, Line.Offense, new Txt("墓影の長靴", "Graveshade Boots"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_feet_graveshade_boots, Family.Dark),
            new BaseDef("feet.soundless_shoes", Slot.Feet, Line.Offense, new Txt("無音の靴", "Soundless Shoes"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_soundless_shoes, Family.Dark),
            new BaseDef("feet.shadowstrider_boots", Slot.Feet, Line.Offense, new Txt("影渡りの靴", "Shadowstrider Boots"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_feet_shadowstrider_boots, Family.Dark),
            new BaseDef("feet.torchless_shoes", Slot.Feet, Line.Guard, new Txt("灯無しの靴", "Torchless Shoes"), Stat.Tenacity, EquipmentItemsBalanceValues.Base_feet_torchless_shoes, Family.Dark),
            new BaseDef("feet.gatehouse_sabatons", Slot.Feet, Line.Guard, new Txt("城門の鉄鞋", "Gatehouse Sabatons"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_gatehouse_sabatons, Family.Guard),
            new BaseDef("feet.mountain_boots", Slot.Feet, Line.Guard, new Txt("山岳の重靴", "Mountain Boots"), Stat.Armor, EquipmentItemsBalanceValues.Base_feet_mountain_boots, Family.Guard),
            new BaseDef("feet.night_watch_boots", Slot.Feet, Line.Guard, new Txt("夜警の長靴", "Night-Watch Boots"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_feet_night_watch_boots, Family.Guard),
            new BaseDef("feet.patrol_sandals", Slot.Feet, Line.Guard, new Txt("巡回兵の草鞋", "Patrol-Soldier Sandals"), Stat.HealthRegen, EquipmentItemsBalanceValues.Base_feet_patrol_sandals, Family.Guard),
            new BaseDef("feet.updraft_greaves", Slot.Feet, Line.Resonance, new Txt("上昇風の脛当て", "Updraft Greaves"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_updraft_greaves, Family.Gale),
            new BaseDef("feet.breeze_runners", Slot.Feet, Line.Resonance, new Txt("微風の走り靴", "Breeze Runners"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_breeze_runners, Family.Gale),
            new BaseDef("feet.swallow_flight_boots", Slot.Feet, Line.Offense, new Txt("燕飛びの靴", "Swallow-Flight Boots"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_swallow_flight_boots, Family.Gale),
            new BaseDef("feet.long_stride_boots", Slot.Feet, Line.Offense, new Txt("大股の長靴", "Long-Stride Boots"), Stat.AttackRangePct, EquipmentItemsBalanceValues.Base_feet_long_stride_boots, Family.Gale),
            new BaseDef("feet.hospice_slippers", Slot.Feet, Line.Resonance, new Txt("病院の上履き", "Hospice Slippers"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_feet_hospice_slippers, Family.Mend),
            new BaseDef("feet.comfort_sandals", Slot.Feet, Line.Guard, new Txt("安らぎの草鞋", "Comfort Sandals"), Stat.ShieldPower, EquipmentItemsBalanceValues.Base_feet_comfort_sandals, Family.Mend),
            new BaseDef("feet.herb_garden_shoes", Slot.Feet, Line.Resonance, new Txt("薬草園の靴", "Herb-Garden Shoes"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_herb_garden_shoes, Family.Mend),
            new BaseDef("feet.night_nurse_boots", Slot.Feet, Line.Guard, new Txt("夜看の長靴", "Night-Nurse Boots"), Stat.MaxHealthFlat, EquipmentItemsBalanceValues.Base_feet_night_nurse_boots, Family.Mend),
            new BaseDef("feet.pack_trot_boots", Slot.Feet, Line.Offense, new Txt("群れ駆けの長靴", "Pack-Trot Boots"), Stat.AttackSpeedPct, EquipmentItemsBalanceValues.Base_feet_pack_trot_boots, Family.Summon),
            new BaseDef("feet.beast_path_shoes", Slot.Feet, Line.Offense, new Txt("獣径の靴", "Beast-Path Shoes"), Stat.AttackFlat, EquipmentItemsBalanceValues.Base_feet_beast_path_shoes, Family.Summon),
            new BaseDef("feet.howling_greaves", Slot.Feet, Line.Offense, new Txt("遠吠えの脛当て", "Howling Greaves"), Stat.CritDamagePct, EquipmentItemsBalanceValues.Base_feet_howling_greaves, Family.Summon),
            new BaseDef("feet.den_warden_sabatons", Slot.Feet, Line.Guard, new Txt("巣番の鉄鞋", "Den-Warden Sabatons"), Stat.MaxHealthPct, EquipmentItemsBalanceValues.Base_feet_den_warden_sabatons, Family.Summon),
            new BaseDef("feet.old_road_boots", Slot.Feet, Line.Resonance, new Txt("旧道の長靴", "Old-Road Boots"), Stat.PowerFlat, EquipmentItemsBalanceValues.Base_feet_old_road_boots, Family.Memory),
            new BaseDef("feet.twilight_stroll_shoes", Slot.Feet, Line.Resonance, new Txt("黄昏散策の靴", "Twilight-Stroll Shoes"), Stat.DarkAmp, EquipmentItemsBalanceValues.Base_feet_twilight_stroll_shoes, Family.Memory),
            new BaseDef("feet.remembered_steps", Slot.Feet, Line.Resonance, new Txt("思い出の足取り", "Remembered Steps"), Stat.Haste, EquipmentItemsBalanceValues.Base_feet_remembered_steps, Family.Memory),
            new BaseDef("feet.once_worn_sandals", Slot.Feet, Line.Resonance, new Txt("昔履きの草鞋", "Once-Worn Sandals"), Stat.MoveSpeedPct, EquipmentItemsBalanceValues.Base_feet_once_worn_sandals, Family.Memory),
        };

        public static readonly IReadOnlyList<UniqueDef> Uniques = new[]
        {
            new UniqueDef("unique.endless_dance", "weapon.chain_sword", new Txt("終わらない舞", "Endless Dance"),
                new Txt("止まらなければ、夢は覚めない。", "As long as you never stop, the dream never ends."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_endless_dance_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_endless_dance_Power1),
            new UniqueDef("unique.dreameater", "weapon.blaze_greatsword", new Txt("夢喰いの大剣", "Dreameater"),
                new Txt("燃やした悪夢の分だけ、持ち主は満たされる。", "Every nightmare it burns feeds its wielder."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_dreameater_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_dreameater_Power1),
            new UniqueDef("unique.wrathscale", "armor.thorn_mail", new Txt("逆鱗の鎧", "Wrathscale"),
                new Txt("触れたものに、触れた代償を。", "Whoever touches it, pays for the touch."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_wrathscale_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_wrathscale_Power1),
            new UniqueDef("unique.unbroken", "armor.guardian_plate", new Txt("不落の胸当て", "The Unbroken"),
                new Txt("囲まれるほど、灯は強く燃える。", "The more they surround it, the brighter it burns."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_unbroken_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_unbroken_Power1),
            new UniqueDef("unique.starbinder", "charm.resonance_amulet", new Txt("星を結ぶ護符", "Starbinder"),
                new Txt("二つの灯が並ぶとき、星図は一つになる。", "When two lights stand together, their star maps become one."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_starbinder_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_starbinder_Power1),
            new UniqueDef("unique.headsman", "charm.hunters_seal", new Txt("処刑人の印章", "Headsman's Seal"),
                new Txt("弱った獲物を、狩人は見逃さない。", "A hunter never lets wounded prey escape."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_headsman_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_headsman_Power1),
            new UniqueDef("unique.thunder_fangs", "weapon.twin_fang", new Txt("雷鳴の双牙", "Thunderfangs"),
                new Txt("一つ斬れば、群れごと痺れる。", "Cut one, and the whole pack trembles."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_thunder_fangs_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_thunder_fangs_Power1),
            new UniqueDef("unique.shattered_star", "charm.pulsing_core", new Txt("砕けた星核", "Shattered Starcore"),
                new Txt("倒れた悪夢は、星屑になって弾ける。", "Fallen nightmares burst into stardust."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_shattered_star_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_shattered_star_Power1),
            new UniqueDef("unique.warding_spirit", "armor.resonant_robe", new Txt("守護霊の衣", "Shroud of the Warding Spirit"),
                new Txt("深い傷ほど、誰かがそっと手を添える。", "The deeper the wound, the gentler the hand that covers it."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_warding_spirit_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_warding_spirit_Power1),
            new UniqueDef("unique.bloodied_maul", "weapon.shield_maul", new Txt("血塗れの大槌", "Bloodied Maul"),
                new Txt("追い詰められた獣ほど、よく暴れる。", "A cornered beast fights the hardest."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_bloodied_maul_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_bloodied_maul_Power1),

            new UniqueDef("unique.prism_clock", "charm.old_clock", new Txt("四元の時計", "Prismatic Clock"),
                new Txt("四つの夢が重なる瞬間、時は砕ける。", "When four dreams overlap, time itself shatters."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_prism_clock_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_prism_clock_Power1),
            new UniqueDef("unique.afterimage_cloak", "armor.flowing_cloak", new Txt("残像の外套", "Afterimage Cloak"),
                new Txt("避けた先に、もう次の記憶が待っている。", "Where you dodge to, your next memory is already waiting."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_afterimage_cloak_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_afterimage_cloak_Power1),
            new UniqueDef("unique.dawnbreaker", "weapon.calming_staff", new Txt("暁を呼ぶ杖", "Dawncaller"),
                new Txt("三つ重なった光は、決して外れない。", "Light stacked thrice never misses."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_dawnbreaker_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_dawnbreaker_Power1),
            // 本体の旅人ごとの専用固有品（キットに効く固有効果の組み合わせ。誰でも装備できる）
            new UniqueDef("unique.sig.vesper", "weapon.blaze_greatsword", new Txt("審問官の誓剣", "Inquisitor's Oathblade"),
                new Txt("四度目の祈りは、必ず届く。（Vesper）", "The fourth prayer always lands. (Vesper)"),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_sig_vesper_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_sig_vesper_Power1),
            new UniqueDef("unique.sig.lacerta", "weapon.longspike_bow", new Txt("サラマンダーの銃身", "Salamander Barrel"),
                new Txt("火薬は多いほど良い。（Lacerta）", "More powder is always better. (Lacerta)"),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_sig_lacerta_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_sig_lacerta_Power1),
            new UniqueDef("unique.sig.cetus", "armor.guardian_plate", new Txt("深海の外殻", "Abyssal Carapace"),
                new Txt("凍てつく水底に、揺らがぬ殻がある。（Cetus）", "On the frozen seabed rests an unshaken shell. (Cetus)"),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_sig_cetus_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_sig_cetus_Power1),
            new UniqueDef("unique.sig.yubar", "charm.old_clock", new Txt("星屑の写本", "Stardust Codex"),
                new Txt("記憶を使うたび、星が集まる。（Yubar）", "Every memory used gathers another star. (Yubar)"),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_sig_yubar_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_sig_yubar_Power1),
            new UniqueDef("unique.sig.husk", "weapon.twin_fang", new Txt("空殻の牙", "Hollow Fang"),
                new Txt("影の中では、急所しか見えない。（空殻）", "In the shadows, only weak points are visible. (Husk)"),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_sig_husk_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_sig_husk_Power1),
            new UniqueDef("unique.sig.mist", "weapon.chain_sword", new Txt("霧払いの太刀", "Mistcutter"),
                new Txt("避けた一閃が、次の一閃を呼ぶ。（Mist）", "Each evaded strike calls the next. (Mist)"),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_sig_mist_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_sig_mist_Power1),
            new UniqueDef("unique.sig.nachia", "charm.resonance_amulet", new Txt("絆の鈴", "Bell of Bonds"),
                new Txt("呼べば応える。光が、仲間が。（Nachia）", "Call, and they answer: the light, and your friends. (Nachia)"),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_sig_nachia_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_sig_nachia_Power1),
            new UniqueDef("unique.sig.aurena", "armor.lampkeeper_mantle", new Txt("黄金の聖杯", "Golden Chalice"),
                new Txt("注いだ血は、光となって還る。（Aurena）", "The blood you pour returns as light. (Aurena)"),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_sig_aurena_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_sig_aurena_Power1),
            new UniqueDef("unique.sig.bismuth", "charm.pulsing_core", new Txt("四冊目の物語", "The Fourth Tale"),
                new Txt("三つの物語が揃えば、四つ目が始まる。（Bismuth）", "When three tales meet, the fourth begins. (Bismuth)"),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_sig_bismuth_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_sig_bismuth_Power1),
            new UniqueDef("unique.glacier_lance", "weapon.frost_spear", new Txt("氷河の槍", "Glacier Lance"),
                new Txt("凍りついた敵は、もう逃げられない。", "A frozen foe has nowhere left to run."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_glacier_lance_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_glacier_lance_Power1),
            new UniqueDef("unique.moon_reaper", "weapon.dusk_scythe", new Txt("月喰いの鎌", "Moon Reaper"),
                new Txt("欠けた月の夜にだけ、刃は研がれる。", "Its edge is honed only on waning-moon nights."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_moon_reaper_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_moon_reaper_Power1),
            new UniqueDef("unique.lighthouse", "weapon.lantern_rod", new Txt("夜明けの灯台", "Lighthouse of Dawn"),
                new Txt("迷った夢を、光が岸まで導く。", "Its light guides lost dreams back to shore."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_lighthouse_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_lighthouse_Power1),
            new UniqueDef("unique.winter_vow", "armor.frost_coat", new Txt("冬の誓い", "Winter's Vow"),
                new Txt("凍えるほど、守る意志は固くなる。", "The colder it gets, the firmer the resolve."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_winter_vow_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_winter_vow_Power1),
            new UniqueDef("unique.whirling_veil", "armor.dancer_garb", new Txt("渦巻く舞衣", "Whirling Veil"),
                new Txt("止まらない舞は、刃より速い。", "A dance that never stops outpaces any blade."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_whirling_veil_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_whirling_veil_Power1),
            new UniqueDef("unique.astral_mantle", "armor.star_cloak", new Txt("天球の外套", "Astral Mantle"),
                new Txt("星の巡りを読めば、切り札はいつでも手の中に。", "Read the turning stars, and your trump card is always at hand."),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_astral_mantle_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_astral_mantle_Power1),
            new UniqueDef("unique.phoenix_locket", "charm.ember_locket", new Txt("不死鳥のロケット", "Phoenix Locket"),
                new Txt("燃え尽きたと思ったときが、始まりだ。", "The moment you think you have burned out is when it begins."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_phoenix_locket_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_phoenix_locket_Power1),
            new UniqueDef("unique.moonlit_bell", "charm.moon_bell", new Txt("月夜の鈴", "Moonlit Bell"),
                new Txt("鳴るたびに、冷たい雷が走る。", "Each chime sends cold lightning running."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_moonlit_bell_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_moonlit_bell_Power1),
            new UniqueDef("unique.iron_wing", "charm.iron_feather", new Txt("鉄翼", "Iron Wing"),
                new Txt("羽ばたくたびに、刃を弾く。", "Every beat of its wings turns a blade aside."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_iron_wing_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_iron_wing_Power1),
            new UniqueDef("unique.storm_caller", "weapon.chain_sword", new Txt("嵐を呼ぶ剣", "Stormcaller"),
                new Txt("振るえば、空が応える。", "Swing it, and the sky answers."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_storm_caller_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_storm_caller_Power1),
            new UniqueDef("unique.hungering_dark", "armor.resonant_robe", new Txt("飢える闇", "Hungering Dark"),
                new Txt("闇は、与えた傷の分だけ満たされる。", "The dark is filled by every wound it gives."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_hungering_dark_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_hungering_dark_Power1),
            new UniqueDef("unique.last_bastion", "armor.guardian_plate", new Txt("最後の砦", "Last Bastion"),
                new Txt("倒れる寸前こそ、本当の戦いだ。", "The real fight begins just before you fall."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_last_bastion_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_last_bastion_Power1),
            new UniqueDef("unique.gale_bow", "weapon.hunting_bow", new Txt("疾風の弓", "Galestring Bow"),
                new Txt("風より速く、矢は獲物を見つける。", "Faster than the wind, the arrow finds its prey."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_gale_bow_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_gale_bow_Power1),
            new UniqueDef("unique.skypiercer", "weapon.longspike_bow", new Txt("天穿ち", "Skypiercer"),
                new Txt("狙った星は、必ず落ちる。", "Any star it aims at will fall."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_skypiercer_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_skypiercer_Power1),
            new UniqueDef("unique.headhunter_axe", "weapon.war_axe", new Txt("首狩りの斧", "Headhunter's Axe"),
                new Txt("斧は、弱った首から覚えていく。", "The axe learns the weakest necks first."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_headhunter_axe_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_headhunter_axe_Power1),
            new UniqueDef("unique.wildfire_axe", "weapon.war_axe", new Txt("野火の戦斧", "Wildfire Axe"),
                new Txt("一振りで、草原は炎の海になる。", "One swing turns the plains into a sea of fire."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_wildfire_axe_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_wildfire_axe_Power1),
            new UniqueDef("unique.rampart", "weapon.tower_lance", new Txt("城壁の守り槍", "Rampart Guard"),
                new Txt("槍の後ろには、誰一人通さない。", "No one passes behind this spear."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_rampart_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_rampart_Power1),
            new UniqueDef("unique.oathkeeper", "weapon.oath_mace", new Txt("誓いを守る者", "Oathkeeper"),
                new Txt("立てた誓いが、傷をふさぐ。", "The oaths you swore close your wounds."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_oathkeeper_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_oathkeeper_Power1),
            new UniqueDef("unique.judgement", "weapon.oath_mace", new Txt("裁きの戦棍", "Mace of Judgement"),
                new Txt("罪の重さだけ、一撃は重くなる。", "Each blow weighs as much as the sin."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_judgement_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_judgement_Power1),
            new UniqueDef("unique.daydream_wand", "weapon.dream_wand", new Txt("白昼夢の杖", "Daydream Wand"),
                new Txt("目を開けたまま、夢を振るう。", "It wields dreams with eyes wide open."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_daydream_wand_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_daydream_wand_Power1),
            new UniqueDef("unique.lullaby", "weapon.star_harp", new Txt("子守唄の竪琴", "Lullaby Harp"),
                new Txt("敵を眠らせる調べが、仲間を奮い立たせる。", "A melody that lulls foes to sleep rouses friends."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_lullaby_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_lullaby_Power1),
            new UniqueDef("unique.constellation", "weapon.star_harp", new Txt("星座を奏でる竪琴", "Constellation Harp"),
                new Txt("弦を弾くたび、星が一つ増える。", "Every plucked string adds a star to the sky."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_constellation_Power0, Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_constellation_Power1),
            new UniqueDef("unique.frost_fang", "weapon.twin_fang", new Txt("霜牙", "Frostfang"),
                new Txt("噛まれた傷は、凍えて塞がらない。", "Its bite freezes and never closes."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_frost_fang_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_frost_fang_Power1),
            new UniqueDef("unique.eclipse_scythe", "weapon.dusk_scythe", new Txt("日蝕の鎌", "Eclipse Scythe"),
                new Txt("光が消えた一瞬に、すべてを刈る。", "In the instant the light dies, it reaps all."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_eclipse_scythe_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_eclipse_scythe_Power1),
            new UniqueDef("unique.siegebreaker", "weapon.shield_maul", new Txt("攻城槌", "Siegebreaker"),
                new Txt("壁を砕く力は、守るためにある。", "The strength to break walls exists to protect."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_siegebreaker_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_siegebreaker_Power1),
            new UniqueDef("unique.phoenix_blade", "weapon.blaze_greatsword", new Txt("鳳凰の大剣", "Phoenix Greatsword"),
                new Txt("倒れても、炎は何度でも立ち上がる。", "Even fallen, the flame rises again and again."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_phoenix_blade_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_phoenix_blade_Power1),
            new UniqueDef("unique.tidal_sword", "weapon.chain_sword", new Txt("潮流の剣", "Tidal Sword"),
                new Txt("引いては寄せる、終わりのない連撃。", "Strikes that ebb and flow without end."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_tidal_sword_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_tidal_sword_Power1),
            new UniqueDef("unique.sunlit_rod", "weapon.lantern_rod", new Txt("陽光の杖", "Sunlit Rod"),
                new Txt("朝日は、どんな悪夢も照らし出す。", "The morning sun lays every nightmare bare."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_sunlit_rod_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_sunlit_rod_Power1),
            new UniqueDef("unique.mirror_staff", "weapon.calming_staff", new Txt("鏡の杖", "Mirror Staff"),
                new Txt("受けた力を、そのまま映し返す。", "It reflects back every force it receives."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_mirror_staff_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_mirror_staff_Power1),
            new UniqueDef("unique.void_wand", "weapon.dream_wand", new Txt("虚無の杖", "Void Wand"),
                new Txt("何もない場所から、闇があふれ出す。", "Darkness spills from where nothing was."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_void_wand_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_void_wand_Power1),
            new UniqueDef("unique.stalker_leather", "armor.hunter_leather", new Txt("追跡者の革鎧", "Stalker's Leathers"),
                new Txt("足音は、獲物が倒れるまで消えない。", "Its footsteps fade only when the prey falls."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_stalker_leather_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_stalker_leather_Power1),
            new UniqueDef("unique.blood_hide", "armor.hunter_leather", new Txt("血染めの毛皮", "Bloodstained Hide"),
                new Txt("浴びた血が、次の狩りを急かす。", "The blood it soaks up urges the next hunt."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_blood_hide_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_blood_hide_Power1),
            new UniqueDef("unique.forgeheart", "armor.ember_plate", new Txt("鍛冶場の心臓", "Forgeheart Plate"),
                new Txt("打たれるほど、熱く固くなる。", "The more it is struck, the hotter and harder it gets."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_forgeheart_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_forgeheart_Power1),
            new UniqueDef("unique.cinder_mail", "armor.ember_plate", new Txt("燃え殻の鎧", "Cinder Mail"),
                new Txt("炎が消えたあとにも、熱は残る。", "The heat remains after the fire dies."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_cinder_mail_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_cinder_mail_Power1),
            new UniqueDef("unique.ancient_root", "armor.root_mail", new Txt("古木の根", "Ancient Root"),
                new Txt("根は、倒れた者さえ支える。", "Roots hold up even the fallen."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_ancient_root_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_ancient_root_Power1),
            new UniqueDef("unique.mossveil", "armor.root_mail", new Txt("苔むした帷子", "Mossveil Mail"),
                new Txt("静かに、確かに、傷は癒える。", "Quietly and surely, wounds heal."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_mossveil_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_mossveil_Power1),
            new UniqueDef("unique.turtle_king", "armor.bastion_shell", new Txt("亀王の甲羅", "Shell of the Turtle King"),
                new Txt("千年の甲羅は、どんな一撃も忘れない。", "A thousand-year shell forgets no blow."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_turtle_king_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_turtle_king_Power1),
            new UniqueDef("unique.mistwalker", "armor.mist_robe", new Txt("霧を歩む者", "Mistwalker Robe"),
                new Txt("霧の中では、誰も影を踏めない。", "In the mist, no one can step on your shadow."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_mistwalker_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_mistwalker_Power1),
            new UniqueDef("unique.dew_robe", "armor.mist_robe", new Txt("朝露の法衣", "Morning Dew Robe"),
                new Txt("夜明けの雫が、仲間の傷を洗う。", "Drops of dawn wash your allies' wounds."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_dew_robe_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_dew_robe_Power1),
            new UniqueDef("unique.pilgrim_shawl", "armor.prayer_shawl", new Txt("巡礼の肩掛け", "Pilgrim's Shawl"),
                new Txt("歩いた道が長いほど、祈りは深くなる。", "The longer the road walked, the deeper the prayer."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_pilgrim_shawl_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_pilgrim_shawl_Power1),
            new UniqueDef("unique.saint_shawl", "armor.prayer_shawl", new Txt("聖女の肩掛け", "Saint's Shawl"),
                new Txt("祈りは、仲間を包む光になる。", "Prayer becomes a light that wraps your allies."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_saint_shawl_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_saint_shawl_Power1),
            new UniqueDef("unique.thundercloud", "armor.flowing_cloak", new Txt("雷雲の外套", "Thundercloud Cloak"),
                new Txt("風に乗って、雷は群れを渡る。", "Riding the wind, lightning leaps through the pack."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_thundercloud_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_thundercloud_Power1),
            new UniqueDef("unique.reprisal", "armor.counter_gauntlets", new Txt("報復の籠手", "Gauntlets of Reprisal"),
                new Txt("殴られた分だけ、殴り返す。", "It returns every blow it takes."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_reprisal_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_reprisal_Power1),
            new UniqueDef("unique.nightwatch", "armor.lampkeeper_mantle", new Txt("夜番の外衣", "Nightwatch Mantle"),
                new Txt("灯が消えるまで、見張りは終わらない。", "The watch ends only when the lamp goes out."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_nightwatch_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_nightwatch_Power1),
            new UniqueDef("unique.thorn_queen", "armor.thorn_mail", new Txt("茨の女王", "Thorn Queen"),
                new Txt("近づく者すべてに、棘の口づけを。", "A thorny kiss for all who come near."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_thorn_queen_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_thorn_queen_Power1),
            new UniqueDef("unique.flame_dancer", "armor.dancer_garb", new Txt("炎の舞衣", "Flame Dancer's Garb"),
                new Txt("舞うたびに、火の粉が散る。", "Sparks scatter with every step of the dance."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_flame_dancer_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_flame_dancer_Power1),
            new UniqueDef("unique.starfall_cloak", "armor.star_cloak", new Txt("星降る外套", "Starfall Cloak"),
                new Txt("夜空ごと、敵の上に降らせる。", "It brings the whole night sky down on foes."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_starfall_cloak_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_starfall_cloak_Power1),
            new UniqueDef("unique.blizzard_coat", "armor.frost_coat", new Txt("吹雪の上衣", "Blizzard Coat"),
                new Txt("吹雪の中では、すべてが凍りつく。", "In the blizzard, everything freezes."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_blizzard_coat_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_blizzard_coat_Power1),
            new UniqueDef("unique.wolf_fang", "charm.fang_necklace", new Txt("狼王の牙", "Wolf King's Fang"),
                new Txt("群れの長は、最後まで噛みつく。", "The pack leader bites to the very end."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_wolf_fang_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_wolf_fang_Power1),
            new UniqueDef("unique.viper_fang", "charm.fang_necklace", new Txt("毒蛇の牙", "Viper's Fang"),
                new Txt("小さな傷が、やがて命を奪う。", "A tiny wound in time takes a life."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_viper_fang_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_viper_fang_Power1),
            new UniqueDef("unique.battle_drum", "charm.war_drum", new Txt("鬨の太鼓", "Battle-Cry Drum"),
                new Txt("太鼓が鳴れば、足は止まらない。", "When the drum sounds, no foot stands still."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_battle_drum_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_battle_drum_Power1),
            new UniqueDef("unique.thunder_drum", "charm.war_drum", new Txt("雷鼓", "Thunder Drum"),
                new Txt("打つたびに、空が裂ける。", "Each beat splits the sky."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_thunder_drum_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_thunder_drum_Power1),
            new UniqueDef("unique.citadel_seal", "charm.guardian_seal", new Txt("城塞の封印", "Citadel Seal"),
                new Txt("封じたのは、敵ではなく恐れだ。", "What it seals is not the enemy, but fear."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_citadel_seal_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_citadel_seal_Power1),
            new UniqueDef("unique.mothers_seal", "charm.guardian_seal", new Txt("母なる封印", "Mother's Seal"),
                new Txt("見えない腕が、そっと抱きとめる。", "Unseen arms gently catch you."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_mothers_seal_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_mothers_seal_Power1),
            new UniqueDef("unique.golem_heart", "charm.stone_heart", new Txt("ゴーレムの心臓", "Golem Heart"),
                new Txt("石の鼓動は、決して乱れない。", "A heartbeat of stone never falters."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_golem_heart_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_golem_heart_Power1),
            new UniqueDef("unique.living_stone", "charm.stone_heart", new Txt("生きた石", "Living Stone"),
                new Txt("砕かれても、また形を取り戻す。", "Even shattered, it takes shape again."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_living_stone_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_living_stone_Power1),
            new UniqueDef("unique.oracle_lens", "charm.dream_lens", new Txt("予言者の水晶", "Oracle's Lens"),
                new Txt("次の一手が、もう見えている。", "The next move is already in sight."),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_oracle_lens_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_oracle_lens_Power1),
            new UniqueDef("unique.prism_lens", "charm.dream_lens", new Txt("虹の水晶", "Prism Lens"),
                new Txt("一つの光が、四つの夢に分かれる。", "One light splits into four dreams."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_prism_lens_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_prism_lens_Power1),
            new UniqueDef("unique.jester_mask", "charm.shadow_mask", new Txt("道化の仮面", "Jester's Mask"),
                new Txt("笑わせているうちに、背後を取る。", "While they laugh, it takes their back."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_jester_mask_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_jester_mask_Power1),
            new UniqueDef("unique.nightmare_mask", "charm.shadow_mask", new Txt("悪夢の仮面", "Nightmare Mask"),
                new Txt("仮面の下には、もっと深い闇がある。", "Beneath the mask lies a deeper dark."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_nightmare_mask_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_nightmare_mask_Power1),
            new UniqueDef("unique.sun_locket", "charm.ember_locket", new Txt("太陽のロケット", "Sun Locket"),
                new Txt("小さな太陽が、胸の中で燃えている。", "A tiny sun burns within your chest."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_sun_locket_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_sun_locket_Power1),
            new UniqueDef("unique.tidebell", "charm.moon_bell", new Txt("潮騒の鈴", "Tidebell"),
                new Txt("波の音が、疲れを洗い流す。", "The sound of waves washes fatigue away."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_tidebell_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_tidebell_Power1),
            new UniqueDef("unique.windcutter", "charm.iron_feather", new Txt("風切り羽根", "Windcutter Feather"),
                new Txt("羽ばたき一つで、戦場を駆け抜ける。", "A single flap carries you across the field."),
                Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_windcutter_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_windcutter_Power1),
            new UniqueDef("unique.hourglass", "charm.old_clock", new Txt("時の砂時計", "Hourglass of Ages"),
                new Txt("落ちる砂が、切り札を早める。", "The falling sand hastens your trump card."),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_hourglass_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_hourglass_Power1),
            new UniqueDef("unique.throbbing_core", "charm.pulsing_core", new Txt("鼓動する核", "Throbbing Core"),
                new Txt("鼓動が速まるほど、力が湧く。", "The faster it beats, the stronger you get."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_throbbing_core_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_throbbing_core_Power1),
            new UniqueDef("unique.chain_choker", "charm.chain_necklace", new Txt("鎖の首輪", "Chain Choker"),
                new Txt("縛られた者ほど、強く抗う。", "The more bound, the harder it resists."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_chain_choker_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_chain_choker_Power1),
            new UniqueDef("unique.lucky_ring", "charm.tailwind_ring", new Txt("幸運の指輪", "Lucky Ring"),
                new Txt("追い風は、勝者にだけ吹く。", "The tailwind blows only for the victor."),
                Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_lucky_ring_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_lucky_ring_Power1),
            new UniqueDef("unique.mark_of_prey", "charm.hunters_seal", new Txt("獲物の刻印", "Mark of the Prey"),
                new Txt("刻まれた獲物は、逃げられない。", "Marked prey cannot escape."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_mark_of_prey_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_mark_of_prey_Power1),
            new UniqueDef("unique.choir_amulet", "charm.resonance_amulet", new Txt("合唱の護符", "Choir Amulet"),
                new Txt("声が重なるほど、力は大きくなる。", "The more voices join, the greater the power."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_choir_amulet_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_choir_amulet_Power1),
            new UniqueDef("unique.meteor_bow", "weapon.hunting_bow", new Txt("流星の弓", "Meteor Bow"),
                new Txt("放った矢は、星となって降り注ぐ。", "Loosed arrows rain down as stars."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_meteor_bow_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_meteor_bow_Power1),
            new UniqueDef("unique.bloodaxe", "weapon.war_axe", new Txt("血斧", "Bloodaxe"),
                new Txt("血を浴びるほど、刃は鋭くなる。", "The more blood it bathes in, the sharper its edge."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_bloodaxe_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_bloodaxe_Power1),
            new UniqueDef("unique.icewall_lance", "weapon.tower_lance", new Txt("氷壁の槍", "Icewall Lance"),
                new Txt("凍った壁の向こうへ、敵は届かない。", "No foe reaches past the frozen wall."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_icewall_lance_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_icewall_lance_Power1),
            new UniqueDef("unique.slumber_staff", "weapon.calming_staff", new Txt("眠りの杖", "Slumber Staff"),
                new Txt("安らかな眠りが、仲間を守る盾になる。", "Peaceful sleep becomes a shield for allies."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_slumber_staff_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_slumber_staff_Power1),
            new UniqueDef("unique.tempest_harp", "weapon.star_harp", new Txt("嵐の竪琴", "Tempest Harp"),
                new Txt("激しい調べが、雷を呼ぶ。", "A fierce melody calls down lightning."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_tempest_harp_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_tempest_harp_Power1),
            new UniqueDef("unique.bone_mail", "armor.guardian_plate", new Txt("骨の鎧", "Bone Mail"),
                new Txt("倒した者の骨が、新しい盾になる。", "The bones of the fallen become a new shield."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_bone_mail_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_bone_mail_Power1),
            new UniqueDef("unique.robe_of_winds", "armor.resonant_robe", new Txt("風の衣", "Robe of Winds"),
                new Txt("風をまとえば、刃は届かない。", "Clad in wind, no blade can reach you."),
                Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_robe_of_winds_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_robe_of_winds_Power1),
            new UniqueDef("unique.dragonscale", "armor.bastion_shell", new Txt("竜鱗の鎧", "Dragonscale Armor"),
                new Txt("竜の鱗は、炎さえ跳ね返す。", "Dragon scales repel even flame."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_dragonscale_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_dragonscale_Power1),
            new UniqueDef("unique.shadowstitch", "armor.flowing_cloak", new Txt("影縫いの外套", "Shadowstitch Cloak"),
                new Txt("影を縫い留めれば、敵は動けない。", "Stitch down the shadow, and the foe cannot move."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_shadowstitch_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_shadowstitch_Power1),
            new UniqueDef("unique.soul_lantern", "weapon.lantern_rod", new Txt("魂の灯籠", "Soul Lantern"),
                new Txt("消えた灯が、持ち主の命になる。", "Every light that goes out becomes the bearer's life."),
                Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_soul_lantern_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_soul_lantern_Power1),
            new UniqueDef("unique.cyclone_cloak", "armor.flowing_cloak", new Txt("竜巻の外套", "Cyclone Cloak"),
                new Txt("身をかわすたび、嵐が生まれる。", "Every sidestep gives birth to a storm."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_cyclone_cloak_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_cyclone_cloak_Power1),
            new UniqueDef("unique.brawler_gauntlets", "armor.counter_gauntlets", new Txt("乱闘者の籠手", "Brawler's Gauntlets"),
                new Txt("囲まれてからが、本番だ。", "The real fight starts once you are surrounded."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_brawler_gauntlets_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_brawler_gauntlets_Power1),
            new UniqueDef("unique.first_light", "weapon.longspike_bow", new Txt("一番星の弓", "Bow of the First Star"),
                new Txt("最初の一矢が、すべてを決める。", "The first arrow decides everything."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_first_light_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_first_light_Power1),
            new UniqueDef("unique.starward_mantle", "armor.star_cloak", new Txt("星守りの外套", "Starward Mantle"),
                new Txt("切り札を切るとき、星が身を守る。", "When you play your trump card, the stars guard you."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_starward_mantle_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_starward_mantle_Power1),
            new UniqueDef("unique.hare_boots", "armor.dancer_garb", new Txt("白兎の舞衣", "White Hare Garb"),
                new Txt("跳ねるように逃げ、跳ねるように戻る。", "Bound away, bound back."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_hare_boots_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_hare_boots_Power1),
            new UniqueDef("unique.unbowed_crown", "charm.guardian_seal", new Txt("屈せぬ冠", "Unbowed Crown"),
                new Txt("傷ひとつない者は、恐れを知らない。", "One without a scratch knows no fear."),
                Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_unbowed_crown_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_unbowed_crown_Power1),
            new UniqueDef("unique.overload_core", "charm.pulsing_core", new Txt("過負荷の核", "Overload Core"),
                new Txt("力を使うほど、次の力が満ちる。", "The more power you spend, the more fills the next."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_overload_core_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_overload_core_Power1),
            new UniqueDef("unique.reaper_harvest", "weapon.dusk_scythe", new Txt("刈り入れの鎌", "Harvest Scythe"),
                new Txt("刈った命は、刃の主に還る。", "The lives it reaps return to its master."),
                Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_reaper_harvest_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_reaper_harvest_Power1),
            new UniqueDef("unique.tempest_dancer", "charm.war_drum", new Txt("嵐舞の太鼓", "Drum of the Storm Dance"),
                new Txt("跳ぶたび、太鼓が雷を呼ぶ。", "Every leap makes the drum call lightning."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_tempest_dancer_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_tempest_dancer_Power1),
            new UniqueDef("unique.berserker_axe", "weapon.war_axe", new Txt("狂戦士の斧", "Berserker's Axe"),
                new Txt("敵が多いほど、斧は軽くなる。", "The more foes, the lighter the axe."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_berserker_axe_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_berserker_axe_Power1),
            new UniqueDef("unique.dawn_herald", "weapon.hunting_bow", new Txt("暁の伝令", "Herald of Dawn"),
                new Txt("夜明けの一矢は、まだ眠る敵を貫く。", "The dawn arrow pierces foes still half asleep."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_dawn_herald_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_dawn_herald_Power1),
            new UniqueDef("unique.last_star_crown", "head.dream_circlet", new Txt("終わりの星冠", "Crown of the Last Star"),
                new Txt("最後に輝く星は、いちばん明るい。", "The last star to shine is the brightest."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_last_star_crown_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_last_star_crown_Power1),
            new UniqueDef("unique.overflowing_mind", "head.sage_hat", new Txt("溢れる思索", "Overflowing Mind"),
                new Txt("考えが止まらないなら、止めなければいい。", "If your thoughts will not stop, let them run."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_overflowing_mind_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_overflowing_mind_Power1),
            new UniqueDef("unique.unburnt_king", "head.ember_crown", new Txt("燃え尽きぬ王", "The Unburnt King"),
                new Txt("玉座は灰になった。王冠だけが燃え続けている。", "The throne is ash. Only the crown still burns."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_unburnt_king_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_unburnt_king_Power1),
            new UniqueDef("unique.moonless_hood", "head.moon_hood", new Txt("月なき夜の頭巾", "Hood of the Moonless Night"),
                new Txt("月のない夜は、狩る者の味方をする。", "A moonless night sides with the hunter."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_moonless_hood_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_moonless_hood_Power1),
            new UniqueDef("unique.wardens_oath", "head.warden_visor", new Txt("番人の誓い", "Warden's Oath"),
                new Txt("ここを通りたければ、まず私を倒せ。", "To pass, you must first get through me."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_wardens_oath_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_wardens_oath_Power1),
            new UniqueDef("unique.mist_bride", "head.mist_veil", new Txt("霧の花嫁", "Bride of the Mist"),
                new Txt("触れようとした手は、いつも霧をつかむ。", "Every hand that reaches for her grasps only mist."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_mist_bride_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_mist_bride_Power1),
            new UniqueDef("unique.raging_horns", "head.horned_helm", new Txt("怒れる双角", "Raging Horns"),
                new Txt("囲まれるほど、角は熱くなる。", "The more foes close in, the hotter the horns burn."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_raging_horns_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_raging_horns_Power1),
            new UniqueDef("unique.first_dawn", "head.healer_band", new Txt("最初の夜明け", "The First Dawn"),
                new Txt("長い夢にも、朝は来る。", "Even the longest dream has a morning."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_first_dawn_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_first_dawn_Power1),
            new UniqueDef("unique.headsman_grip", "hands.claw_gauntlets", new Txt("断頭人の握り", "Headsman's Grip"),
                new Txt("弱った獲物を、この爪は逃さない。", "These claws never let a weakened prey slip away."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_headsman_grip_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_headsman_grip_Power1),
            new UniqueDef("unique.storm_fingers", "hands.spell_gloves", new Txt("嵐を呼ぶ指", "Stormcalling Fingers"),
                new Txt("指を鳴らせば、空が応える。", "Snap your fingers, and the sky answers."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_storm_fingers_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_storm_fingers_Power1),
            new UniqueDef("unique.frostbite", "hands.frost_mitts", new Txt("凍傷", "Frostbite"),
                new Txt("凍らせて、砕く。それだけのこと。", "Freeze it, then break it. Nothing more."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_frostbite_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_frostbite_Power1),
            new UniqueDef("unique.first_arrow", "hands.archer_bracers", new Txt("一番矢", "The First Arrow"),
                new Txt("戦いは、最初の一射で決まる。", "A battle is decided by the first shot."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_first_arrow_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_first_arrow_Power1),
            new UniqueDef("unique.dawnwrap", "hands.radiant_wraps", new Txt("夜明けの手巻き", "Dawnwrap"),
                new Txt("光を巻いた拳は、四つの色を呼び寄せる。", "A fist wrapped in light calls all four colors."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_dawnwrap_Power0, Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_dawnwrap_Power1),
            new UniqueDef("unique.heartbeat_grips", "hands.vigor_grips", new Txt("鼓動の握り", "Heartbeat Grips"),
                new Txt("強く握るほど、心臓も強く打つ。", "The tighter the grip, the stronger the heartbeat."),
                Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_heartbeat_grips_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_heartbeat_grips_Power1),
            new UniqueDef("unique.thousand_cuts", "hands.leather_gloves", new Txt("千の切り傷", "A Thousand Cuts"),
                new Txt("一つひとつは浅くても、千を重ねれば深い。", "Each cut is shallow; a thousand run deep."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_thousand_cuts_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_thousand_cuts_Power1),
            new UniqueDef("unique.far_hand", "hands.reach_bracers", new Txt("遠き手", "The Far Hand"),
                new Txt("届かない場所など、ほとんどない。", "There is almost nowhere it cannot reach."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_far_hand_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_far_hand_Power1),
            new UniqueDef("unique.whirling_steps", "feet.dancer_shoes", new Txt("旋風の足取り", "Whirling Steps"),
                new Txt("舞い手が通った後には、風だけが残る。", "Where the dancer passed, only wind remains."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_whirling_steps_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_whirling_steps_Power1),
            new UniqueDef("unique.windchaser", "feet.wind_sandals", new Txt("風を追う者", "Windchaser"),
                new Txt("風より先に着けば、風は追い風になる。", "Arrive before the wind, and it becomes your tailwind."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_windchaser_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_windchaser_Power1),
            new UniqueDef("unique.rooted_oath", "feet.rooted_boots", new Txt("根付く誓い", "Rooted Oath"),
                new Txt("一歩も退かない。根は、退き方を知らない。", "Not one step back. Roots do not know how."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_rooted_oath_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_rooted_oath_Power1),
            new UniqueDef("unique.firewalker", "feet.ember_treads", new Txt("火渡り", "Firewalker"),
                new Txt("燃える道を選ぶ者にだけ、道は開ける。", "The path opens only for those who choose to walk through fire."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_firewalker_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_firewalker_Power1),
            new UniqueDef("unique.silent_step", "feet.stalker_boots", new Txt("音なき足", "Silent Step"),
                new Txt("気づいたときには、もう背後にいる。", "By the time they notice, you are already behind them."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_silent_step_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_silent_step_Power1),
            new UniqueDef("unique.pilgrims_end", "feet.pilgrim_boots", new Txt("巡礼の終わり", "Pilgrim's End"),
                new Txt("長い旅の終わりに、倒した者の数だけ祈りがある。", "At the journey's end, a prayer for every fallen foe."),
                Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_pilgrims_end_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_pilgrims_end_Power1),
            new UniqueDef("unique.mountain_stride", "feet.stone_boots", new Txt("山の歩み", "Mountain Stride"),
                new Txt("山は動かない。動くときは、すべてを押し流す。", "A mountain does not move. When it does, it moves everything."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_mountain_stride_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_mountain_stride_Power1),
            new UniqueDef("unique.star_wanderer", "feet.star_steps", new Txt("星を渡る者", "Star Wanderer"),
                new Txt("星と星のあいだにも、道はある。", "There are roads between the stars, too."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_star_wanderer_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_star_wanderer_Power1),
            new UniqueDef("unique.thorn_crown", "head.thorn_circlet", new Txt("茨の王冠", "Crown of Thorns"),
                new Txt("王の痛みは、触れた者にも分け与えられる。", "The king's pain is shared with all who touch him."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_thorn_crown_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_thorn_crown_Power1),
            new UniqueDef("unique.halo_of_mercy", "head.radiant_halo", new Txt("慈悲の光輪", "Halo of Mercy"),
                new Txt("この光の下では、誰も独りで倒れない。", "Beneath this light, no one falls alone."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_halo_of_mercy_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_halo_of_mercy_Power1),
            new UniqueDef("unique.far_sight", "head.scout_goggles", new Txt("千里眼", "Far Sight"),
                new Txt("見えているなら、もう当たっている。", "If you can see it, you have already hit it."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_far_sight_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_far_sight_Power1),
            new UniqueDef("unique.frozen_thought", "head.frost_helm", new Txt("凍てつく思考", "Frozen Thought"),
                new Txt("冷えた頭は、決して慌てない。", "A cold head never panics."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_frozen_thought_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_frozen_thought_Power1),
            new UniqueDef("unique.ash_hands", "hands.ember_gauntlets", new Txt("灰の手", "Hands of Ash"),
                new Txt("触れたものは、すべて灰になる。", "Everything it touches turns to ash."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_ash_hands_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_ash_hands_Power1),
            new UniqueDef("unique.shadow_stitch", "hands.shadow_gloves", new Txt("影縫い", "Shadowstitch"),
                new Txt("影を縫い止めれば、本体も動けない。", "Pin the shadow, and its owner cannot move."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_shadow_stitch_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_shadow_stitch_Power1),
            new UniqueDef("unique.mending_touch", "hands.healer_hands", new Txt("繕いの手", "Mending Touch"),
                new Txt("傷は、触れるそばから閉じていく。", "Wounds close as soon as it touches them."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_mending_touch_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_mending_touch_Power1),
            new UniqueDef("unique.duelists_promise", "hands.duelist_gloves", new Txt("決闘者の約束", "Duelist's Promise"),
                new Txt("一対一なら、負けたことはない。", "One on one, it has never lost."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_duelists_promise_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_duelists_promise_Power1),
            new UniqueDef("unique.icewalker", "feet.frost_boots", new Txt("氷を歩む者", "Icewalker"),
                new Txt("氷の上こそ、もっとも速く走れる。", "On ice, you run fastest of all."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_icewalker_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_icewalker_Power1),
            new UniqueDef("unique.shadow_dancer", "feet.shadow_slippers", new Txt("影踊り", "Shadow Dancer"),
                new Txt("影は、踊り手の足元から離れない。", "The shadow never leaves the dancer's feet."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_shadow_dancer_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_shadow_dancer_Power1),
            new UniqueDef("unique.spiked_charge", "feet.spiked_boots", new Txt("棘の突進", "Spiked Charge"),
                new Txt("止まれと言われて、止まったことがない。", "Told to stop, it never has."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_spiked_charge_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_spiked_charge_Power1),
            new UniqueDef("unique.quiet_study", "feet.sage_slippers", new Txt("静かな書斎", "The Quiet Study"),
                new Txt("急がない者ほど、遠くまで行ける。", "Those who do not hurry go the farthest."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_quiet_study_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_quiet_study_Power1),
            // v1.21：各枠の固有品を増やす
            new UniqueDef("unique.hungry_pack", "head.wolf_pelt", new Txt("飢えた群れ", "The Hungry Pack"),
                new Txt("一頭倒すたび、群れは速くなる。", "Every kill makes the pack run faster."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_hungry_pack_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_hungry_pack_Power1),
            new UniqueDef("unique.stargazer_diadem", "head.star_diadem", new Txt("星見の髪飾り", "Stargazer Diadem"),
                new Txt("仲間と見上げる星は、守りにもなる。", "Stars watched beside allies become a shield."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_stargazer_diadem_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_stargazer_diadem_Power1),
            new UniqueDef("unique.plague_physician", "head.plague_mask", new Txt("疫病医の面", "Plague Physician's Mask"),
                new Txt("病を診る目は、命の流れも見逃さない。", "An eye that reads sickness never misses the flow of life."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_plague_physician_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_plague_physician_Power1),
            new UniqueDef("unique.tidepool_crown", "head.coral_crown", new Txt("潮だまりの冠", "Tidepool Crown"),
                new Txt("冷たい潮が、傷口を静かに閉ざす。", "The cold tide quietly closes every wound."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_tidepool_crown_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_tidepool_crown_Power1),
            new UniqueDef("unique.oathbound_helm", "head.knight_helm", new Txt("誓いの大兜", "Oathbound Greathelm"),
                new Txt("退かぬ者の前で、刃は鈍る。", "Blades dull before one who never retreats."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_oathbound_helm_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_oathbound_helm_Power1),
            new UniqueDef("unique.ashen_hood", "head.ash_hood", new Txt("灰燼の頭巾", "Hood of Ashes"),
                new Txt("灰の中の火種が、群れごと弾け飛ぶ。", "Embers in the ash burst across the whole pack."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_ashen_hood_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_ashen_hood_Power1),
            new UniqueDef("unique.closed_eye", "head.eye_patch", new Txt("閉ざした眼", "The Closed Eye"),
                new Txt("見えぬ目が、最初の一撃を見極める。", "The unseen eye judges the very first strike."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_closed_eye_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_closed_eye_Power1),
            new UniqueDef("unique.spring_wreath", "head.leaf_wreath", new Txt("春待つ花冠", "Wreath of Waiting Spring"),
                new Txt("傷んでも、若葉はまた芽吹く。", "Even when bruised, young leaves sprout again."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_spring_wreath_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_spring_wreath_Power1),
            new UniqueDef("unique.hollow_will", "head.void_helm", new Txt("虚ろな意志", "Hollow Will"),
                new Txt("空っぽの器に、闇が魔力を満たす。", "Darkness fills the empty vessel with power."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_hollow_will_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hollow_will_Power1),
            new UniqueDef("unique.noon_mask", "head.sun_mask", new Txt("真昼の面", "Mask of High Noon"),
                new Txt("大技を放つ瞬間、日輪が輝く。", "The sun blazes at the moment you unleash your Ultimate."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_noon_mask_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_noon_mask_Power1),
            new UniqueDef("unique.old_soldier_helm", "head.iron_helm", new Txt("老兵の兜", "Veteran's Helm"),
                new Txt("数多の戦を越えた鉄は、割れない。", "Iron that has survived countless wars does not crack."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_old_soldier_helm_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_old_soldier_helm_Power1),
            new UniqueDef("unique.stalking_hood", "head.hunter_hood", new Txt("追い込みの頭巾", "Hood of the Final Chase"),
                new Txt("弱った獲物を仕留め、すぐ次へ駆ける。", "Finish the wounded prey, then run to the next."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_stalking_hood_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_stalking_hood_Power1),
            new UniqueDef("unique.carnage_mask", "head.berserker_mask", new Txt("修羅の面", "Mask of Carnage"),
                new Txt("囲まれ、斬るほどに刃が走る。", "Surrounded, every cut makes your blade run faster."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_carnage_mask_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_carnage_mask_Power1),
            new UniqueDef("unique.lucid_circlet", "head.dream_circlet", new Txt("明晰夢の額冠", "Lucid Dream Circlet"),
                new Txt("術を放ち、身をかわし、また術を放つ。", "Cast, dodge, and cast again."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_lucid_circlet_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_lucid_circlet_Power1),
            new UniqueDef("unique.cornered_beast", "head.horned_helm", new Txt("手負いの獣", "Wounded Beast"),
                new Txt("追い詰められた獣ほど、牙を剥く。", "The more cornered the beast, the fiercer its fangs."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_cornered_beast_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_cornered_beast_Power1),
            new UniqueDef("unique.stormweaver_hat", "head.sage_hat", new Txt("雷織りの帽子", "Stormweaver Hat"),
                new Txt("呪文の合間に、雷が枝分かれして走る。", "Between spells, lightning forks and runs."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_stormweaver_hat_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_stormweaver_hat_Power1),
            new UniqueDef("unique.veil_of_gales", "head.mist_veil", new Txt("風裂きの面紗", "Veil of Gales"),
                new Txt("身をかわせば霧が渦を巻き、敵を呑む。", "Dodge, and the mist spirals to swallow your foes."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_veil_of_gales_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_veil_of_gales_Power1),
            new UniqueDef("unique.stone_sentinel", "head.warden_visor", new Txt("石の番兵", "Stone Sentinel"),
                new Txt("殴られても怯まず、殴った者に返す。", "Struck, you do not flinch; you give it back."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_stone_sentinel_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_stone_sentinel_Power1),
            new UniqueDef("unique.hearth_crown", "head.ember_crown", new Txt("熾火の冠", "Crown of Embers"),
                new Txt("燃える炎が、持ち主の傷を温める。", "The burning flame warms its wearer's wounds."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_hearth_crown_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_hearth_crown_Power1),
            new UniqueDef("unique.winter_moon_hood", "head.moon_hood", new Txt("寒月の頭巾", "Hood of the Cold Moon"),
                new Txt("冷たい月の下、影も凍りつく。", "Beneath the cold moon, even shadows freeze."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_winter_moon_hood_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_winter_moon_hood_Power1),
            new UniqueDef("unique.lifeline_band", "head.healer_band", new Txt("命綱の鉢巻", "Lifeline Headband"),
                new Txt("倒れかけても、命綱が引き戻す。", "Even as you fall, the lifeline pulls you back."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_lifeline_band_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_lifeline_band_Power1),
            new UniqueDef("unique.bleeding_crown", "head.thorn_circlet", new Txt("流血の王冠", "Crown of Bleeding"),
                new Txt("血を流すほど、茨は鋭く食い込む。", "The more you bleed, the deeper the thorns bite."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_bleeding_crown_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_bleeding_crown_Power1),
            new UniqueDef("unique.glacier_mind", "head.frost_helm", new Txt("氷の叡智", "Glacial Wisdom"),
                new Txt("冷えた頭で温存し、一気に解き放つ。", "Keep a cool head, then release it all at once."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_glacier_mind_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_glacier_mind_Power1),
            new UniqueDef("unique.guardian_halo", "head.radiant_halo", new Txt("守りの後光", "Halo of Guarding"),
                new Txt("光は傷ついた者を、もう一度立たせる。", "Light lifts the wounded back to their feet."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_guardian_halo_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_guardian_halo_Power1),
            new UniqueDef("unique.lightning_scope", "head.scout_goggles", new Txt("稲光の遠眼鏡", "Lightning Spyglass"),
                new Txt("遠い獲物へ、雷が先に届く。", "Lightning reaches distant prey first."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_lightning_scope_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_lightning_scope_Power1),
            new UniqueDef("unique.prism_diadem", "head.star_diadem", new Txt("虹彩の髪飾り", "Iridescent Diadem"),
                new Txt("四つの色が重なる時、星が爆ぜる。", "When four colors overlap, a star bursts."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_prism_diadem_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_prism_diadem_Power1),
            new UniqueDef("unique.void_fragment", "head.void_helm", new Txt("虚空の砕片", "Shard of the Void"),
                new Txt("倒れた敵の闇が、周囲を砕く。", "The darkness of the fallen shatters all around."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_void_fragment_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_void_fragment_Power1),
            new UniqueDef("unique.sunfire_mask", "head.sun_mask", new Txt("灼陽の面", "Mask of the Scorching Sun"),
                new Txt("光と炎が、四拍ごとに降り注ぐ。", "Light and fire rain down every fourth beat."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_sunfire_mask_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_sunfire_mask_Power1),
            new UniqueDef("unique.briar_embrace", "hands.thorn_wraps", new Txt("茨の抱擁", "Briar Embrace"),
                new Txt("傷つくほどに、この抱擁は強く締まる。", "The more it wounds you, the tighter it clings."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_briar_embrace_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_briar_embrace_Power1),
            new UniqueDef("unique.apothecary_touch", "hands.alchemist_gloves", new Txt("調合の指先", "Apothecary's Touch"),
                new Txt("火と氷を混ぜれば、四つ目の元素が目覚める。", "Mix fire with frost, and the other elements wake."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_apothecary_touch_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_apothecary_touch_Power1),
            new UniqueDef("unique.marrow_breaker", "hands.bone_knuckles", new Txt("砕骨の拳", "Marrow Breaker"),
                new Txt("砕いた骨の数だけ、命が戻ってくる。", "For every bone you break, some life returns."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_marrow_breaker_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_marrow_breaker_Power1),
            new UniqueDef("unique.star_weaver", "hands.star_rings", new Txt("星を紡ぐ指", "Star Weaver"),
                new Txt("光を指に絡め、夜空の守りを織り上げる。", "Thread light through your fingers and weave a shield of stars."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_star_weaver_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_star_weaver_Power1),
            new UniqueDef("unique.unyielding_vow", "hands.oath_gauntlets", new Txt("不退の誓拳", "Unyielding Vow"),
                new Txt("一歩も退かぬと誓えば、痛みが力に変わる。", "Vow never to retreat, and pain becomes power."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_unyielding_vow_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_unyielding_vow_Power1),
            new UniqueDef("unique.maelstrom_grasp", "hands.tide_gloves", new Txt("渦潮の手", "Maelstrom Grasp"),
                new Txt("身を翻すたび、冷たい渦が敵を呑み込む。", "Every roll draws foes into a freezing whirlpool."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_maelstrom_grasp_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_maelstrom_grasp_Power1),
            new UniqueDef("unique.hellfire_fist", "hands.flame_grips", new Txt("業火拳", "Hellfire Fist"),
                new Txt("囲まれるほどに、拳の炎は荒れ狂う。", "The more foes surround you, the wilder the flames rage."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_hellfire_fist_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_hellfire_fist_Power1),
            new UniqueDef("unique.void_render", "hands.void_claws", new Txt("虚空を裂く爪", "Void Render"),
                new Txt("闇を染み込ませ、とどめの一裂きを狙え。", "Soak them in dark, then go for the final tear."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_void_render_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_void_render_Power1),
            new UniqueDef("unique.stonecaster", "hands.sling_bracers", new Txt("礫打ち", "Stonecaster"),
                new Txt("無傷の的へ先に投げ、風のように駆け出せ。", "Throw first at the unharmed, then run like the wind."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_stonecaster_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_stonecaster_Power1),
            new UniqueDef("unique.rosary_chant", "hands.prayer_beads", new Txt("数珠繰り", "Rosary Chant"),
                new Txt("珠を一つ繰るたび、祈りが傷を塞いでいく。", "With each bead you turn, a prayer closes your wounds."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_rosary_chant_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_rosary_chant_Power1),
            new UniqueDef("unique.last_stand_gloves", "hands.leather_gloves", new Txt("背水の手袋", "Last Stand Gloves"),
                new Txt("追い詰められた時こそ、指は最も速く走る。", "When cornered, your fingers move their fastest."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_last_stand_gloves_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_last_stand_gloves_Power1),
            new UniqueDef("unique.wallbreaker", "hands.iron_gauntlets", new Txt("城砕きの拳", "Wallbreaker"),
                new Txt("囲まれても動じず、倒した敵ごと辺りを砕く。", "Stand firm when surrounded, and shatter all around each kill."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_wallbreaker_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_wallbreaker_Power1),
            new UniqueDef("unique.backfire_gauntlets", "hands.iron_gauntlets", new Txt("返り火の籠手", "Backfire Gauntlets"),
                new Txt("殴られた痛みを、そのまま炎に変えて返す。", "It turns every blow you take into fire thrown back."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_backfire_gauntlets_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_backfire_gauntlets_Power1),
            new UniqueDef("unique.lightning_draw", "hands.archer_bracers", new Txt("雷走り", "Lightning Draw"),
                new Txt("最初の一矢が走れば、雷が群れを渡る。", "Once the first arrow flies, lightning leaps through the pack."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_lightning_draw_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_lightning_draw_Power1),
            new UniqueDef("unique.cantrip_fingers", "hands.spell_gloves", new Txt("指先の詠唱", "Cantrip Fingers"),
                new Txt("技を放つたびに、指先から火花がこぼれる。", "Sparks spill from your fingertips after every skill."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_cantrip_fingers_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_cantrip_fingers_Power1),
            new UniqueDef("unique.famished_claws", "hands.claw_gauntlets", new Txt("喰らいつく爪", "Famished Claws"),
                new Txt("弱った獲物ほど、爪は深く食い込み血を啜る。", "The weaker the prey, the deeper the claws bite and drink."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_famished_claws_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_famished_claws_Power1),
            new UniqueDef("unique.frozen_night", "hands.frost_mitts", new Txt("凍夜の指", "Frozen Night"),
                new Txt("冷気と闇を重ね、敵を静かな夜に閉じ込める。", "Layer cold upon dark and trap foes in a silent night."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_frozen_night_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_frozen_night_Power1),
            new UniqueDef("unique.light_binder", "hands.radiant_wraps", new Txt("光を束ねる手", "Light Binder"),
                new Txt("集めた光は、切り札の一撃で解き放たれる。", "The gathered light is released in a single trump strike."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_light_binder_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_light_binder_Power1),
            new UniqueDef("unique.peak_form", "hands.vigor_grips", new Txt("万全の一撃", "Peak Form"),
                new Txt("体も敵も万全のうちに、最初の一撃を叩き込め。", "Strike first while both you and your foe are unscathed."),
                Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_peak_form_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_peak_form_Power1),
            new UniqueDef("unique.flash_sleight", "hands.quick_fingers", new Txt("稲光の早業", "Flash Sleight"),
                new Txt("身をかわすたびに技が戻り、雷が跳ねる。", "Each dodge cools your skills and sends lightning leaping."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_flash_sleight_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_flash_sleight_Power1),
            new UniqueDef("unique.melee_dancer", "hands.quick_fingers", new Txt("乱戦の舞手", "Melee Dancer"),
                new Txt("敵の輪の中でこそ、手は軽やかに踊る。", "Your hands dance lightest in the middle of a ring of foes."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_melee_dancer_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_melee_dancer_Power1),
            new UniqueDef("unique.hounds_reach", "hands.reach_bracers", new Txt("追い込みの腕", "Hound's Reach"),
                new Txt("遠くから仕留め、勢いのまま次の獲物へ走る。", "Finish them from afar and chase down the next target."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hounds_reach_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_hounds_reach_Power1),
            new UniqueDef("unique.ember_soul", "hands.ember_gauntlets", new Txt("熾火の魂", "Ember Soul"),
                new Txt("火種を撒き、燃え尽きた命を己の糧とする。", "Sow the embers and feed on the lives they burn out."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_ember_soul_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_ember_soul_Power1),
            new UniqueDef("unique.shadow_sprinter", "hands.shadow_gloves", new Txt("影走り", "Shadow Sprinter"),
                new Txt("倒すたびに影が濃くなり、手の動きは増す。", "With every kill the shadow deepens and your hands quicken."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_shadow_sprinter_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_shadow_sprinter_Power1),
            new UniqueDef("unique.merciful_light", "hands.healer_hands", new Txt("慈光の手", "Merciful Light"),
                new Txt("光を宿した手が、倒れかけた命を引き戻す。", "Hands bright with light pull a failing life back."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_merciful_light_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_merciful_light_Power1),
            new UniqueDef("unique.stone_resolve", "hands.stone_fists", new Txt("不動の拳", "Stone Resolve"),
                new Txt("敵に囲まれながら殴り、傷を癒して立ち続ける。", "Punch while surrounded, heal as you hit, and stay standing."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_stone_resolve_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_stone_resolve_Power1),
            new UniqueDef("unique.thorned_hammer", "hands.stone_fists", new Txt("棘の鉄拳", "Thorned Fist"),
                new Txt("殴られた痛みを反射し、敵を粉々に砕き散らす。", "Reflect the pain of every blow and scatter foes to pieces."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_thorned_hammer_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_thorned_hammer_Power1),
            new UniqueDef("unique.riposte_creed", "hands.duelist_gloves", new Txt("返し刃の作法", "Riposte Creed"),
                new Txt("受けた痛みは、次の獲物への先手で返せ。", "Take the hit, then answer with the first strike."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_riposte_creed_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_riposte_creed_Power1),
            new UniqueDef("unique.companions_road", "feet.travel_boots", new Txt("道連れの足", "Companion's Road"),
                new Txt("誰かと歩く道は、一人の倍だけ速い。", "A road walked together is twice as swift."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_companions_road_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_companions_road_Power1),
            new UniqueDef("unique.victors_trail", "feet.travel_boots", new Txt("凱旋の道行き", "Victor's Trail"),
                new Txt("倒した数だけ、帰り道は軽くなる。", "Every foe felled lightens the road home."),
                Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_victors_trail_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_victors_trail_Power1),
            new UniqueDef("unique.iron_stance", "feet.iron_greaves", new Txt("鉄壁の立ち姿", "Iron Stance"),
                new Txt("囲まれたなら、動かずに立てばいい。", "When surrounded, simply stand your ground."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_iron_stance_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_iron_stance_Power1),
            new UniqueDef("unique.brawlers_greaves", "feet.iron_greaves", new Txt("喧嘩屋の脛", "Brawler's Shins"),
                new Txt("殴られたら、倍にして蹴り返す。", "Take a blow, kick back twice as hard."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_brawlers_greaves_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_brawlers_greaves_Power1),
            new UniqueDef("unique.unmoving_vow", "feet.guard_sabatons", new Txt("動かざる誓い", "Unmoving Vow"),
                new Txt("膝をつかぬ限り、誓いは破れない。", "The vow holds so long as you do not kneel."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_unmoving_vow_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_unmoving_vow_Power1),
            new UniqueDef("unique.barbed_march", "feet.guard_sabatons", new Txt("棘の行軍", "Barbed March"),
                new Txt("一歩ごとに、近づく者が傷を負う。", "Each step wounds those who draw near."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_barbed_march_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_barbed_march_Power1),
            new UniqueDef("unique.pack_hunter", "feet.wolf_boots", new Txt("群れ狩りの脚", "Pack Hunter"),
                new Txt("獲物に囲まれても、狼は笑って駆ける。", "Wolves run laughing through a ring of prey."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_pack_hunter_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_pack_hunter_Power1),
            new UniqueDef("unique.hungry_howl", "feet.wolf_boots", new Txt("飢えた遠吠え", "Hungry Howl"),
                new Txt("血の匂いが濃いほど、足は止まらない。", "The thicker the blood scent, the swifter the paws."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_hungry_howl_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_hungry_howl_Power1),
            new UniqueDef("unique.tidebreaker", "feet.tide_sandals", new Txt("波を砕く足", "Tidebreaker"),
                new Txt("身をかわせば、足元から渦が立つ。", "Dodge, and a whirlpool rises at your feet."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_tidebreaker_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_tidebreaker_Power1),
            new UniqueDef("unique.ebb_and_flow", "feet.tide_sandals", new Txt("満ち引きの歩", "Ebb and Flow"),
                new Txt("冷たい波は、討つたびに背を押す。", "The cold tide pushes you on with each kill."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_ebb_and_flow_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_ebb_and_flow_Power1),
            new UniqueDef("unique.citadel_stride", "feet.knight_sabatons", new Txt("城門の騎士", "Citadel Knight"),
                new Txt("敵の波を受け止め、門の前で崩さない。", "Hold the gate and let the wave break on you."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_citadel_stride_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_citadel_stride_Power1),
            new UniqueDef("unique.vengeful_knight", "feet.knight_sabatons", new Txt("復讐の騎士", "Vengeful Knight"),
                new Txt("傷を負うたび、剣は怒りで重くなる。", "Every wound makes the blade heavier with wrath."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_vengeful_knight_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_vengeful_knight_Power1),
            new UniqueDef("unique.first_light_steps", "feet.dawn_steps", new Txt("曙光を踏む", "Dawnstepper"),
                new Txt("駆け出す朝、足跡が光を残す。", "At daybreak your footprints leave light behind."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_first_light_steps_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_first_light_steps_Power1),
            new UniqueDef("unique.morning_reprieve", "feet.dawn_steps", new Txt("朝凪の舞", "Morning Calm"),
                new Txt("光を宿して舞えば、技はすぐ戻る。", "Dance in the light and your skills return swiftly."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_morning_reprieve_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_morning_reprieve_Power1),
            new UniqueDef("unique.cinder_march", "feet.ash_boots", new Txt("燃え殻の行進", "Cinder March"),
                new Txt("踏み荒らした跡に、火種が弾ける。", "Sparks burst along the path you trample."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_cinder_march_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_cinder_march_Power1),
            new UniqueDef("unique.pyre_runner", "feet.ash_boots", new Txt("火葬場の走者", "Pyre Runner"),
                new Txt("倒すほど、炎は走る足に絡みつく。", "The more you fell, the more flame clings to you."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_pyre_runner_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_pyre_runner_Power1),
            new UniqueDef("unique.green_rest", "feet.root_sandals", new Txt("緑の休息", "Green Rest"),
                new Txt("根を下ろせば、傷が芽吹き始める。", "Take root and your wounds begin to bud."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_green_rest_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_green_rest_Power1),
            new UniqueDef("unique.spring_sprout", "feet.root_sandals", new Txt("芽吹きの春", "Spring Sprout"),
                new Txt("倒れかけてなお、一歩で力が戻る。", "Even on the brink, one step restores your strength."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_spring_sprout_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_spring_sprout_Power1),
            new UniqueDef("unique.fog_runner", "feet.mist_shoes", new Txt("霧に駆ける", "Fogrunner"),
                new Txt("霧に紛れて跳べば、影も追いつけない。", "Leap through the fog and shadows cannot follow."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_fog_runner_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_fog_runner_Power1),
            new UniqueDef("unique.drifting_mind", "feet.mist_shoes", new Txt("揺蕩う思考", "Drifting Thought"),
                new Txt("避けては唱え、唱えてはまた避ける。", "Dodge, cast, then dodge again."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_drifting_mind_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_drifting_mind_Power1),
            new UniqueDef("unique.trackers_pace", "feet.hunter_boots", new Txt("追い詰める歩調", "Tracker's Pace"),
                new Txt("弱った獲物へは、迷わず駆け寄れ。", "Run without hesitation to the wounded prey."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_trackers_pace_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_trackers_pace_Power1),
            new UniqueDef("unique.ambush_leap", "feet.hunter_boots", new Txt("待ち伏せの跳躍", "Ambush Leap"),
                new Txt("一撃目を譲らぬ者が、狩りを制す。", "He who strikes first rules the hunt."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_ambush_leap_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_ambush_leap_Power1),
            new UniqueDef("unique.shackled_walk", "feet.iron_clogs", new Txt("枷つきの歩み", "Shackled Walk"),
                new Txt("重い足取りは、痛みを返す刃になる。", "A heavy gait turns pain into a blade."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_shackled_walk_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_shackled_walk_Power1),
            new UniqueDef("unique.stubborn_clogs", "feet.iron_clogs", new Txt("頑固者の木靴", "Stubborn One's Clogs"),
                new Txt("押し寄せる敵に、びくともしない。", "Unmoved by the crowd pressing in."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_stubborn_clogs_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_stubborn_clogs_Power1),
            new UniqueDef("unique.zodiac_step", "feet.star_slippers", new Txt("星宮の跳躍", "Zodiac Leap"),
                new Txt("身をかわして、星の奥義へ繋げ。", "Slip away, then chain into a stellar finisher."),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_zodiac_step_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_zodiac_step_Power1),
            new UniqueDef("unique.night_sky_walk", "feet.star_slippers", new Txt("夜空の散歩", "Night Sky Walk"),
                new Txt("星の盾を纏い、静かに術を重ねる。", "Wrapped in a star shield, stack spell upon spell."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_night_sky_walk_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_night_sky_walk_Power1),
            new UniqueDef("unique.shadowed_hunt", "feet.stalker_boots", new Txt("闇の足音", "Footfall of Dark"),
                new Txt("足音を消せば、とどめの隙が見える。", "Silence your steps and the killing opening appears."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_shadowed_hunt_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_shadowed_hunt_Power1),
            new UniqueDef("unique.gale_spinner", "feet.wind_sandals", new Txt("風車の足", "Gale Spinner"),
                new Txt("回って跳べば、旋風が道を拓く。", "Spin and leap, and a whirlwind clears the way."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_gale_spinner_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_gale_spinner_Power1),
            new UniqueDef("unique.dusk_reaper_sickle", "weapon.moon_sickle", new Txt("宵闇の刈り手", "Dusk Reaper"),
                new Txt("影を刈るほど、刃は冴え渡る。", "The more shadows it reaps, the keener it grows."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_dusk_reaper_sickle_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_dusk_reaper_sickle_Power1),
            new UniqueDef("unique.cinder_serpent", "weapon.ember_whip", new Txt("火蛇の鞭", "Cinder Serpent"),
                new Txt("先手の一打で、火蛇が牙を剥く。", "The first lash is when the serpent bites."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_cinder_serpent_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_cinder_serpent_Power1),
            new UniqueDef("unique.whiteridge_lance", "weapon.glacier_spear", new Txt("白嶺の槍", "Whiteridge Lance"),
                new Txt("凍らせた敵を、穂先で砕け。", "Freeze them, then shatter them on the tip."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_whiteridge_lance_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_whiteridge_lance_Power1),
            new UniqueDef("unique.first_daybreak_scepter", "weapon.dawn_scepter", new Txt("曙光の王笏", "Daybreak Scepter"),
                new Txt("傷のない身に、朝日は力を貸す。", "Morning light favors the unscathed."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_first_daybreak_scepter_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_first_daybreak_scepter_Power1),
            new UniqueDef("unique.stalwart_halberd", "weapon.iron_halberd", new Txt("不動の斧槍", "Stalwart Halberd"),
                new Txt("敵に囲まれても、一歩も退かない。", "Surrounded, it never yields a step."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_stalwart_halberd_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_stalwart_halberd_Power1),
            new UniqueDef("unique.nightlong_tome", "weapon.dream_tome", new Txt("終夜の綴り", "Nightlong Tome"),
                new Txt("頁をめくるたび、終曲が近づく。", "Each turned page draws the finale closer."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_nightlong_tome_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_nightlong_tome_Power1),
            new UniqueDef("unique.bonesunder", "weapon.bone_cleaver", new Txt("断骨の刃", "Bonesunder"),
                new Txt("弱った獲物に、迷わず振り下ろせ。", "Bring it down on the weakened without hesitation."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_bonesunder_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_bonesunder_Power1),
            new UniqueDef("unique.mirrored_rapier", "weapon.twin_rapier", new Txt("鏡合わせの細剣", "Mirrored Rapier"),
                new Txt("倒れ際の手応えが、次の突きを速める。", "Each kill quickens the next thrust."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_mirrored_rapier_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_mirrored_rapier_Power1),
            new UniqueDef("unique.wayfarer_bell_staff", "weapon.pilgrim_staff", new Txt("遍路の鈴杖", "Wayfarer Bell Staff"),
                new Txt("鈴が鳴る間は、まだ倒れない。", "While the bell rings, you are not yet fallen."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_wayfarer_bell_staff_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_wayfarer_bell_staff_Power1),
            new UniqueDef("unique.skydrum_hammer", "weapon.thunder_hammer", new Txt("天鳴の鎚", "Skydrum Hammer"),
                new Txt("倒した敵の上に、雷が落ちる。", "Thunder falls where your enemies do."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_skydrum_hammer_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_skydrum_hammer_Power1),
            new UniqueDef("unique.furnace_vest", "armor.ember_cuirass", new Txt("炉心の胴鎧", "Furnace Vest"),
                new Txt("囲まれるほどに、炉は熱を増す。", "The more they crowd you, the hotter the furnace."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_furnace_vest_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_furnace_vest_Power1),
            new UniqueDef("unique.rimeveil_robe", "armor.frost_robe", new Txt("霜華の法衣", "Rimeveil Robe"),
                new Txt("舞うたびに、霜の花が咲く。", "Frost blooms with every dodge."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_rimeveil_robe_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_rimeveil_robe_Power1),
            new UniqueDef("unique.drake_hide", "armor.scale_coat", new Txt("竜皮の外套", "Drake Hide"),
                new Txt("群れに囲まれたら、炎を返せ。", "When swarmed, answer with flame."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_drake_hide_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_drake_hide_Power1),
            new UniqueDef("unique.huntsmans_vest", "armor.hunter_vest", new Txt("追い込みの胴衣", "Huntsman Vest"),
                new Txt("走って追い詰め、とどめを刺す。", "Run them down, then finish the kill."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_huntsmans_vest_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_huntsmans_vest_Power1),
            new UniqueDef("unique.stardust_mantle", "armor.star_mantle", new Txt("降星の肩掛け", "Stardust Mantle"),
                new Txt("仲間と並べば、星屑が守る。", "Stand beside allies and stardust shields you."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_stardust_mantle_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_stardust_mantle_Power1),
            new UniqueDef("unique.stillfist_garb", "armor.monk_garb", new Txt("静拳の道着", "Stillfist Garb"),
                new Txt("拳を重ねるほど、心は静まる。", "The more strikes you land, the calmer you grow."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_stillfist_garb_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_stillfist_garb_Power1),
            new UniqueDef("unique.duskwoven_cloak", "armor.shadow_cloak", new Txt("宵織りの外套", "Duskwoven Cloak"),
                new Txt("打たれた痛みを、闇に染めて返せ。", "Dye the pain you suffer in shadow and return it."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_duskwoven_cloak_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_duskwoven_cloak_Power1),
            new UniqueDef("unique.solar_aegis_plate", "armor.sun_plate", new Txt("日輪の護り", "Solar Aegis"),
                new Txt("触れる者を、光の棘が拒む。", "Thorns of light turn away all who touch it."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_solar_aegis_plate_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_solar_aegis_plate_Power1),
            new UniqueDef("unique.heartwood_bark", "armor.bark_mail", new Txt("芯木の鎧", "Heartwood Mail"),
                new Txt("傷は、年輪のように癒えていく。", "Wounds heal like rings in a tree."),
                Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_heartwood_bark_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_heartwood_bark_Power1),
            new UniqueDef("unique.windfarer_coat", "armor.traveler_coat", new Txt("風渡りの旅衣", "Windfarer Coat"),
                new Txt("足を止めない者に、道は開く。", "The road opens for those who never stop."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_windfarer_coat_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_windfarer_coat_Power1),
            new UniqueDef("unique.hearth_ember_stone", "charm.hearthstone", new Txt("囲炉裏の熾", "Hearth Ember"),
                new Txt("火を絶やさぬ者は、魔術を灯し続ける。", "Keep the fire alive and your magic burns on."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_hearth_ember_stone_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hearth_ember_stone_Power1),
            new UniqueDef("unique.rimeheart_pendant", "charm.frost_pendant", new Txt("霜心の首飾り", "Rimeheart Pendant"),
                new Txt("凍てた心が、大技を星の盾に変える。", "A frozen heart turns your ultimate into a starry shield."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_rimeheart_pendant_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_rimeheart_pendant_Power1),
            new UniqueDef("unique.noon_brooch", "charm.sun_brooch", new Txt("真昼のブローチ", "Noon Brooch"),
                new Txt("陽が高いうちは、倒れはしない。", "While the sun is high, you will not fall."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_noon_brooch_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_noon_brooch_Power1),
            new UniqueDef("unique.umbral_band", "charm.shadow_ring", new Txt("影絡みの指輪", "Umbral Band"),
                new Txt("闇をまとい、回避と共に渦を巻け。", "Cloak yourself in dark and spin out of every dodge."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_umbral_band_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_umbral_band_Power1),
            new UniqueDef("unique.first_blood_fang", "charm.hunter_tooth", new Txt("初撃の牙", "First Blood Fang"),
                new Txt("最初の一撃から、獲物へ駆け出せ。", "From the first strike, give chase."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_first_blood_fang_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_first_blood_fang_Power1),
            new UniqueDef("unique.elder_oak_amulet", "charm.oak_amulet", new Txt("古樫の護符", "Elder Oak Amulet"),
                new Txt("元気なうちは強く、傷は静かに塞がる。", "Strong while hale, and wounds close quietly."),
                Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_elder_oak_amulet_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_elder_oak_amulet_Power1),
            new UniqueDef("unique.ticking_gears", "charm.clockwork_charm", new Txt("刻む歯車", "Ticking Gears"),
                new Txt("倒すたびに歯車が回り、術も速く回る。", "As the gears turn, so do your moves."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_ticking_gears_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_ticking_gears_Power1),
            new UniqueDef("unique.vow_of_iron", "charm.iron_seal", new Txt("鉄の誓印", "Vow of Iron"),
                new Txt("打たれても揺らがず、そのまま打ち返す。", "Struck, you answer under a shield of light."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_vow_of_iron_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_vow_of_iron_Power1),
            new UniqueDef("unique.skydancer_feather", "charm.feather_token", new Txt("空舞いの羽", "Skydancer Feather"),
                new Txt("避けるたび、時間が少し巻き戻る。", "Every dodge winds time back a little."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_skydancer_feather_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_skydancer_feather_Power1),
            new UniqueDef("unique.rally_horn", "charm.war_horn", new Txt("決起の角笛", "Rally Horn"),
                new Txt("群れの中で吹けば、奥義が燃え上がる。", "Blow it amid the swarm and your ultimate roars."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_rally_horn_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_rally_horn_Power1),
            // v1.23：新しい固有効果を使う固有品
            new UniqueDef("unique.finale_codex", "weapon.dream_tome", new Txt("終幕の写本", "Codex of the Curtain Call"),
                new Txt("三つの技を綴れば、終幕は早く訪れる。", "Chain three skills, and the final act arrives early."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_finale_codex_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_finale_codex_Power1),
            new UniqueDef("unique.grand_finale_diadem", "head.star_diadem", new Txt("大詰めの髪飾り", "Diadem of the Grand Finale"),
                new Txt("三つの技を重ね、切り札を引き寄せろ。", "Layer Q, W and E to pull your ultimate closer."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_grand_finale_diadem_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_grand_finale_diadem_Power1),
            new UniqueDef("unique.intermission_shoes", "feet.dancer_shoes", new Txt("幕間の舞靴", "Intermission Dancers"),
                new Txt("舞い続けて技を巡らせ、最後の幕を開けよ。", "Keep dancing through your skills to raise the last curtain."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_intermission_shoes_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_intermission_shoes_Power1),
            new UniqueDef("unique.last_movement_charm", "charm.clockwork_charm", new Txt("終楽章の歯車", "Gear of the Last Movement"),
                new Txt("歯車が噛み合えば、終曲は目前だ。", "When the gears mesh, the finale is near."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_last_movement_charm_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_last_movement_charm_Power1),
            new UniqueDef("unique.echo_longbow", "weapon.longspike_bow", new Txt("谺の長弓", "Echoing Longbow"),
                new Txt("会心の矢が鳴るたび、技はすぐに戻る。", "Each critical arrow rings out and recalls your skills."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_echo_longbow_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_echo_longbow_Power1),
            new UniqueDef("unique.afterglow_fingers", "hands.duelist_gloves", new Txt("残響の指", "Fingers of Afterglow"),
                new Txt("急所を突くたび、余韻が技を呼び戻す。", "Every vital strike leaves an echo that refreshes your skills."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_afterglow_fingers_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_afterglow_fingers_Power1),
            new UniqueDef("unique.reverberant_vest", "armor.hunter_vest", new Txt("木霊の胴衣", "Reverberant Vest"),
                new Txt("狙い澄ました一撃が、次の技を早める。", "A well-aimed blow hastens your next skill."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_reverberant_vest_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_reverberant_vest_Power1),
            new UniqueDef("unique.echoing_tread", "feet.stalker_boots", new Txt("谺を踏む足", "Tread of Echoes"),
                new Txt("忍び寄る一撃の余韻が、技を研ぎ澄ます。", "The echo of a stealthy crit sharpens your skills."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_echoing_tread_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_echoing_tread_Power1),
            new UniqueDef("unique.ice_shackle_spear", "weapon.glacier_spear", new Txt("氷枷の穂先", "Icebound Spearhead"),
                new Txt("凍らせて動きを奪い、渾身の一突きを。", "Freeze them in place, then thrust with everything."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_ice_shackle_spear_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_ice_shackle_spear_Power1),
            new UniqueDef("unique.snaring_coral", "head.coral_crown", new Txt("絡め取る珊瑚", "Snaring Coral"),
                new Txt("動けぬ敵ほど、深く刃が食い込む。", "The less they can move, the deeper the blade bites."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_snaring_coral_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_snaring_coral_Power1),
            new UniqueDef("unique.neap_tide_grip", "hands.tide_gloves", new Txt("引き潮の枷", "Shackle of the Ebb Tide"),
                new Txt("冷たい潮で足を絡め、そこを狙え。", "Tangle their legs in cold tide and strike there."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_neap_tide_grip_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_neap_tide_grip_Power1),
            new UniqueDef("unique.bound_oath", "charm.chain_necklace", new Txt("繋がれた誓い", "Chained Oath"),
                new Txt("鎖に縛られた敵へ、報復は容赦しない。", "Retribution spares no enemy caught in your chains."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_bound_oath_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_bound_oath_Power1),
            new UniqueDef("unique.crystal_chorale", "weapon.star_harp", new Txt("水晶の調べ", "Crystal Chorale"),
                new Txt("結晶を揃えるほど、魔力の音色は澄んでいく。", "The more crystals you gather, the purer the magic rings."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_crystal_chorale_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_crystal_chorale_Power1),
            new UniqueDef("unique.collectors_hat", "head.sage_hat", new Txt("蒐集家の帽子", "Collector's Hat"),
                new Txt("集めた結晶の数だけ、力は高まっていく。", "Your power grows with every crystal you collect."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_collectors_hat_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_collectors_hat_Power1),
            new UniqueDef("unique.crystal_shroud", "armor.star_mantle", new Txt("結晶纏い", "Crystal Shroud"),
                new Txt("結晶の輝きを纏い、星の盾を張れ。", "Wear the crystals' glow and raise a shield of stars."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_crystal_shroud_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_crystal_shroud_Power1),
            new UniqueDef("unique.crystal_eye", "charm.dream_lens", new Txt("結晶の瞳", "Eye of Crystal"),
                new Txt("磨いた結晶が、四つの元素を呼び合わせる。", "Polished crystals call the four elements together."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_crystal_eye_Power0, Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_crystal_eye_Power1),
            new UniqueDef("unique.huntmasters_bow", "weapon.hunting_bow", new Txt("狩猟長の弓", "Huntmaster's Bow"),
                new Txt("狩りを重ねるほど、弱った獲物は逃げられない。", "The deeper your hunt, the less your wounded prey can flee."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_huntmasters_bow_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_huntmasters_bow_Power1),
            new UniqueDef("unique.fangking_pelt", "head.wolf_pelt", new Txt("牙王の毛皮", "Pelt of the Fang King"),
                new Txt("獲物を追うほど、群れの誇りが牙を研ぐ。", "Tracking prey sharpens your fangs with the pack's pride."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_fangking_pelt_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_fangking_pelt_Power1),
            new UniqueDef("unique.praying_claws", "hands.claw_gauntlets", new Txt("祈る爪", "Praying Claws"),
                new Txt("祈りを捧げて獲物を追い、危険へ踏み込め。", "Offer prayers, track your prey, and step into danger."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_praying_claws_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_praying_claws_Power1),
            new UniqueDef("unique.proud_tracks", "feet.hunter_boots", new Txt("誇りの足跡", "Tracks of Pride"),
                new Txt("獲物を深く追うほど、足取りは軽くなる。", "The deeper the pursuit, the lighter your stride."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_proud_tracks_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_proud_tracks_Power1),
            new UniqueDef("unique.overflowing_sap", "armor.bark_mail", new Txt("滴る生命樹", "Dripping Lifetree"),
                new Txt("傷が癒えて余った命は、樹皮の盾となる。", "Surplus healing hardens into a shield of bark."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_overflowing_sap_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_overflowing_sap_Power1),
            new UniqueDef("unique.brimming_chalice_staff", "weapon.pilgrim_staff", new Txt("杯満つる錫杖", "Brimming Chalice Staff"),
                new Txt("倒して満ちた命は、溢れて身を守る。", "Life gathered from kills overflows into protection."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_brimming_chalice_staff_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_brimming_chalice_staff_Power1),
            new UniqueDef("unique.full_bloom_wreath", "head.leaf_wreath", new Txt("満開の花冠", "Wreath in Full Bloom"),
                new Txt("万全の身で咲く花は、散りぎわに盾となる。", "A flower in full health becomes a shield as it fades."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_full_bloom_wreath_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_full_bloom_wreath_Power1),
            new UniqueDef("unique.spring_water_steps", "feet.root_sandals", new Txt("湧き水の歩", "Steps of the Wellspring"),
                new Txt("癒しが溢れるほど、足元に守りが湧く。", "The more your healing overflows, the more shelter wells up."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_spring_water_steps_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_spring_water_steps_Power1),
            new UniqueDef("unique.votive_shawl", "armor.prayer_shawl", new Txt("奉納の肩掛け", "Votive Shawl"),
                new Txt("聖堂を巡るたび、肩掛けは力を宿していく。", "The shawl gathers power with every shrine you visit."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_votive_shawl_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_votive_shawl_Power1),
            new UniqueDef("unique.wish_beads", "hands.prayer_beads", new Txt("願掛けの数珠", "Wishing Beads"),
                new Txt("一つ祈るごとに、数珠は強く握られる。", "With each prayer, the beads are held tighter."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_wish_beads_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_wish_beads_Power1),
            new UniqueDef("unique.pilgrims_badge", "charm.sun_brooch", new Txt("参詣の徽章", "Pilgrim's Badge"),
                new Txt("聖堂で祈りを重ね、陽の力を高めよ。", "Stack prayers at shrines to raise the power of the sun."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_pilgrims_badge_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_pilgrims_badge_Power1),
            new UniqueDef("unique.dedicated_greathelm", "head.knight_helm", new Txt("奉じる大兜", "Dedicated Greathelm"),
                new Txt("誓いを捧げるほど、兜は守りを増す。", "Each vow offered strengthens the helm's guard."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_dedicated_greathelm_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_dedicated_greathelm_Power1),
            new UniqueDef("unique.sparking_crown", "head.ember_crown", new Txt("火の粉の冠", "Crown of Sparks"),
                new Txt("燃える敵から、火の粉が隣へ飛び移る。", "Sparks leap from burning foes to their neighbors."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_sparking_crown_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_sparking_crown_Power1),
            new UniqueDef("unique.sea_of_flame_plate", "armor.ember_plate", new Txt("火の海の鎧", "Plate of the Flame Sea"),
                new Txt("火を重ねれば、戦場ごと燃え広がる。", "Stack the flames and the whole field catches fire."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_sea_of_flame_plate_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_sea_of_flame_plate_Power1),
            new UniqueDef("unique.seed_scatterer", "feet.ash_boots", new Txt("火種蒔き", "Seed Scatterer"),
                new Txt("歩いた跡から、火種が次々と飛び移る。", "Embers hop from foe to foe in your wake."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_seed_scatterer_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_seed_scatterer_Power1),
            new UniqueDef("unique.spreading_fist", "hands.flame_grips", new Txt("燃え移る手", "Hand of Spreading Flame"),
                new Txt("一撃の炎は、隣の敵へ燃え移る。", "One strike's flame leaps on to the next enemy."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_spreading_fist_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_spreading_fist_Power1),
            // v1.25：新しい土台の固有品
            new UniqueDef("unique.thunderhead_glaive", "weapon.storm_glaive", new Txt("雷雲を裂く刃", "Thunderhead Cleaver"),
                new Txt("稲妻が走るたび、術の巡りが速まる。", "Each bolt that flashes quickens your spells."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_thunderhead_glaive_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_thunderhead_glaive_Power1),
            new UniqueDef("unique.eye_of_the_gale", "weapon.storm_glaive", new Txt("颶風の円舞", "Eye of the Gale"),
                new Txt("敵の輪の中心で、薙刀を回し続けよ。", "Spin the glaive at the heart of the ring of foes."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_eye_of_the_gale_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_eye_of_the_gale_Power1),
            new UniqueDef("unique.requiem_flute", "weapon.bone_flute", new Txt("終幕の骨笛", "Requiem Flute"),
                new Txt("術を重ねて、終曲を早く呼べ。", "Chain your spells and summon the finale sooner."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_requiem_flute_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_requiem_flute_Power1),
            new UniqueDef("unique.chorus_of_marrow", "weapon.bone_flute", new Txt("骨笛の合唱", "Chorus of Marrow"),
                new Txt("仲間と並んで吹けば、傷が癒えていく。", "Play beside allies and wounds begin to mend."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_chorus_of_marrow_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_chorus_of_marrow_Power1),
            new UniqueDef("unique.cinderfang_katar", "weapon.ember_katar", new Txt("火種蒔きの刃", "Cinderfang Katar"),
                new Txt("燃える敵から、隣の敵へ火を移せ。", "Pass the fire from burning foes to their neighbors."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_cinderfang_katar_Power0, Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_cinderfang_katar_Power1),
            new UniqueDef("unique.crimson_katar", "weapon.ember_katar", new Txt("赤熱の一突き", "Crimson Thrust"),
                new Txt("四度目の突きは、必ず熱く貫く。", "Every fourth thrust pierces white-hot."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_crimson_katar_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_crimson_katar_Power1),
            new UniqueDef("unique.drowning_trident", "weapon.tide_trident", new Txt("溺れさせる潮", "Drowning Tide"),
                new Txt("動けぬ敵へ、三叉の穂先を深く突き立てよ。", "Drive the trident deep into foes who cannot move."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_drowning_trident_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_drowning_trident_Power1),
            new UniqueDef("unique.tidecaller_trident", "weapon.tide_trident", new Txt("潮を招く者", "Tidecaller"),
                new Txt("技を放つたび、満ちる冷気が魔力を高める。", "Every skill cast lets the rising chill feed your magic."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_tidecaller_trident_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_tidecaller_trident_Power1),
            new UniqueDef("unique.sworn_hammer", "weapon.vow_mace", new Txt("祈願の聖鎚", "Hammer of Devotion"),
                new Txt("聖堂に通うほど、鎚は重く輝く。", "The more you visit shrines, the brighter it shines."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_sworn_hammer_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_sworn_hammer_Power1),
            new UniqueDef("unique.chalice_mace", "weapon.vow_mace", new Txt("溢れる誓杯", "Overflowing Vow"),
                new Txt("癒えすぎた命は、盾となって身を包む。", "Excess healing wraps you in a shield."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_chalice_mace_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_chalice_mace_Power1),
            new UniqueDef("unique.nightcrow_mask", "head.raven_mask", new Txt("夜烏の嘴", "Nightcrow Beak"),
                new Txt("闇を重ね、会心の余韻で技を回せ。", "Stack darkness and cycle skills on echoing crits."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_nightcrow_mask_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_nightcrow_mask_Power1),
            new UniqueDef("unique.carrion_pride", "head.raven_mask", new Txt("屍を啄む誇り", "Carrion Pride"),
                new Txt("追い詰めた獲物ほど、鴉は力を増す。", "The raven grows strong on prey it has cornered."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_carrion_pride_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_carrion_pride_Power1),
            new UniqueDef("unique.paper_lantern_hat", "head.lantern_hat", new Txt("灯籠流しの笠", "Floating Lantern Hat"),
                new Txt("聖堂を巡るほど、灯りは強く輝く。", "Visit shrines and the lanterns burn brighter."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_paper_lantern_hat_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_paper_lantern_hat_Power1),
            new UniqueDef("unique.lamplit_shelter", "head.lantern_hat", new Txt("宵の雨宿り", "Evening Shelter"),
                new Txt("灯の下では、溢れた命が壁になる。", "Beneath the lamps, overflowing life becomes a wall."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_lamplit_shelter_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_lamplit_shelter_Power1),
            new UniqueDef("unique.linked_coif", "head.iron_coif", new Txt("連環の守り", "Linked Ward"),
                new Txt("足を止めた敵に、鎖が食い込み続ける。", "The links bite deeper into every slowed foe."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_linked_coif_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_linked_coif_Power1),
            new UniqueDef("unique.vigil_coif", "head.iron_coif", new Txt("不寝番の頭巾", "Sleepless Watch"),
                new Txt("囲まれても、祈りが鎖を固くする。", "Even when surrounded, prayer hardens the mail."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_vigil_coif_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_vigil_coif_Power1),
            new UniqueDef("unique.starlit_veil", "head.dream_veil", new Txt("星屑の幕引き", "Starlit Curtain Call"),
                new Txt("短い間に術を重ね、星の終曲へ急げ。", "Cast in quick succession and hasten the stellar finale."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_starlit_veil_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_starlit_veil_Power1),
            new UniqueDef("unique.prism_dream_veil", "head.dream_veil", new Txt("結晶の寝覚め", "Crystal Waking"),
                new Txt("磨いた結晶が、夢の力を育てる。", "Polished crystals nurture the power of dreams."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_prism_dream_veil_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_prism_dream_veil_Power1),
            new UniqueDef("unique.stag_king_crown", "head.antler_crown", new Txt("森の王の戴冠", "Crown of the Forest King"),
                new Txt("獲物を追い詰め、万全の身で仕留めろ。", "Hunt your prey down and strike in perfect health."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_stag_king_crown_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_stag_king_crown_Power1),
            new UniqueDef("unique.rutting_antlers", "head.antler_crown", new Txt("角突き合う闘争", "Locked Antlers"),
                new Txt("囲まれて殴られるほど、角は鋭く燃える。", "The more you are struck while surrounded, the sharper the antlers."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_rutting_antlers_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_rutting_antlers_Power1),
            new UniqueDef("unique.inkflow_robe", "armor.ink_robe", new Txt("滲む墨の記録", "Bleeding Ink Ledger"),
                new Txt("結晶を磨き、術の合間も魔力を絶やすな。", "Polish crystals and keep magic flowing between spells."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_inkflow_robe_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_inkflow_robe_Power1),
            new UniqueDef("unique.last_stroke_robe", "armor.ink_robe", new Txt("筆止めの衣", "Robe of the Last Stroke"),
                new Txt("技を重ね、回避で最後の一筆へ繋げ。", "Link your skills and dodge toward the final stroke."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_last_stroke_robe_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_last_stroke_robe_Power1),
            new UniqueDef("unique.ring_hauberk", "armor.chain_hauberk", new Txt("縛り鎖の重帷子", "Binding Hauberk"),
                new Txt("囲んだ敵を鎖で縛り、まとめて削れ。", "Bind the foes around you and grind them down."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_ring_hauberk_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_ring_hauberk_Power1),
            new UniqueDef("unique.answering_mail", "armor.chain_hauberk", new Txt("応える鎖", "Answering Links"),
                new Txt("打たれた直後に、障壁と反撃で応じろ。", "Answer a blow at once with barrier and counterattack."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_answering_mail_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_answering_mail_Power1),
            new UniqueDef("unique.sparkwoven_jacket", "armor.ember_jacket", new Txt("舞い散る火の粉", "Scattering Sparks"),
                new Txt("燃える敵から燃える敵へ、火が踊り広がる。", "Fire dances from one burning foe to the next."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_sparkwoven_jacket_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_sparkwoven_jacket_Power1),
            new UniqueDef("unique.scorch_runner_jacket", "armor.ember_jacket", new Txt("焦げ跡の疾走", "Scorched Dash"),
                new Txt("回避で駆け抜け、四撃目に炎を乗せよ。", "Dodge and dash, then ignite the fourth strike."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_scorch_runner_jacket_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_scorch_runner_jacket_Power1),
            new UniqueDef("unique.moonmilk_silk", "armor.moon_silk", new Txt("月光の羽衣", "Moonmilk Raiment"),
                new Txt("闇を撒き、溢れた癒しを障壁へ変えろ。", "Spread darkness and turn overflowing healing into shields."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_moonmilk_silk_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_moonmilk_silk_Power1),
            new UniqueDef("unique.vesper_silk", "armor.moon_silk", new Txt("晩課の月衣", "Vesper Moonrobe"),
                new Txt("聖堂で祈り、切り札の後は星が身を守る。", "Pray at shrines, and stars shield you after your ultimate."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_vesper_silk_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_vesper_silk_Power1),
            new UniqueDef("unique.porcupine_plate", "armor.spiked_plate", new Txt("針鼠の鎧", "Porcupine Plate"),
                new Txt("足を鈍らせた敵ほど、棘の傷が深く刺さる。", "Thorns cut deeper into foes you have slowed."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_porcupine_plate_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_porcupine_plate_Power1),
            new UniqueDef("unique.retort_plate", "armor.spiked_plate", new Txt("棘返しの甲冑", "Barbed Retort"),
                new Txt("重い一撃を受けたら、障壁と反撃で返せ。", "Take a heavy blow, then answer with barrier and strikes."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_retort_plate_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_retort_plate_Power1),
            new UniqueDef("unique.hands_frozen_pledge", "hands.ice_bracers", new Txt("氷結の契り", "Pact of Frozen Bonds"),
                new Txt("動きを封じた敵に、ためらわず終わりを与えよ。", "Seal them in ice, then end them without hesitation."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_hands_frozen_pledge_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hands_frozen_pledge_Power1),
            new UniqueDef("unique.hands_prism_frost", "hands.ice_bracers", new Txt("結晶凍土", "Crystalfrost"),
                new Txt("装着した結晶の輝きが、冷気を研ぎ澄ます。", "Equipped crystals sharpen the cold."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_hands_prism_frost_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_hands_prism_frost_Power1),
            new UniqueDef("unique.hands_bound_fury", "hands.chain_wraps", new Txt("縛鎖の憤怒", "Fury of the Bound"),
                new Txt("囲まれるほど拳は速まり、縛った敵には重く落ちる。", "Heavier fists fall on bound foes as you wade in."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_hands_bound_fury_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_hands_bound_fury_Power1),
            new UniqueDef("unique.hands_chain_breaker", "hands.chain_wraps", new Txt("鎖砕きの拳", "Chainbreaker Fist"),
                new Txt("一人倒すたび、拳は鎖ごと周囲を砕く。", "Each kill bursts through the chains around you."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_hands_chain_breaker_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_hands_chain_breaker_Power1),
            new UniqueDef("unique.hands_sun_vow", "hands.sun_gauntlets", new Txt("陽の奉納", "Offering to the Sun"),
                new Txt("聖堂に祈るほど、壁となる光は厚くなる。", "The more you pray, the thicker the wall of light."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_hands_sun_vow_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_hands_sun_vow_Power1),
            new UniqueDef("unique.hands_gilded_rebuke", "hands.sun_gauntlets", new Txt("金色の叱責", "Gilded Rebuke"),
                new Txt("打たれたら光を宿し、すぐさま殴り返せ。", "Take the blow, then answer with radiant force."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hands_gilded_rebuke_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_hands_gilded_rebuke_Power1),
            new UniqueDef("unique.hands_light_fingers", "hands.thief_gloves", new Txt("抜き足の指", "Light Fingers"),
                new Txt("会心のたびに技が戻る。初手の一撃を逃すな。", "Crits refresh your skills. Never waste the opening hit."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_hands_light_fingers_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_hands_light_fingers_Power1),
            new UniqueDef("unique.hands_vanishing_act", "hands.thief_gloves", new Txt("消える手口", "Vanishing Act"),
                new Txt("身をかわし、弱った獲物を音もなく仕留める。", "Slip away, then finish the wounded in silence."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_hands_vanishing_act_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hands_vanishing_act_Power1),
            new UniqueDef("unique.hands_overflow_fast", "hands.monk_wraps", new Txt("断食の巻布", "Fasting Wraps"),
                new Txt("余った癒しは、そのまま身を守る障壁となる。", "Excess healing turns into a protective barrier."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_hands_overflow_fast_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_hands_overflow_fast_Power1),
            new UniqueDef("unique.hands_pained_vow", "hands.monk_wraps", new Txt("苦行の拳", "Fist of Penance"),
                new Txt("囲まれ傷つくほど拳は軽くなり、倒れても立つ。", "Pain quickens your fists, and you rise when you fall."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_hands_pained_vow_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_hands_pained_vow_Power1),
            new UniqueDef("unique.feet_dusk_wing", "feet.raven_boots", new Txt("宵の羽ばたき", "Dusk Wingbeat"),
                new Txt("追い詰めた獲物の闇が、足跡に力を与える。", "Darkness on the hunted lends strength to your tracks."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_feet_dusk_wing_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_feet_dusk_wing_Power1),
            new UniqueDef("unique.feet_caw_cyclone", "feet.raven_boots", new Txt("鴉の旋風", "Raven Cyclone"),
                new Txt("会心で技を戻し、回避の渦で敵をなぎ払え。", "Crit to refresh skills, then sweep foes with your dodge."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_feet_caw_cyclone_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_feet_caw_cyclone_Power1),
            new UniqueDef("unique.feet_noon_walk", "feet.sun_sandals", new Txt("正午の巡礼", "Noon Pilgrimage"),
                new Txt("祈りを重ねながら、光の中を駆け抜けよ。", "Stack prayers as you dash through the light."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_feet_noon_walk_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_feet_noon_walk_Power1),
            new UniqueDef("unique.feet_warm_spring", "feet.sun_sandals", new Txt("温もりの泉", "Warming Spring"),
                new Txt("溢れた癒しを障壁に変え、追い風で進め。", "Turn overflowing heals into barriers and ride the wind."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_feet_warm_spring_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_feet_warm_spring_Power1),
            new UniqueDef("unique.feet_dragged_chain", "feet.chain_greaves", new Txt("引きずる鎖", "Dragging Chains"),
                new Txt("殴られるたび鎖が鳴り、攻撃者に痛みが返る。", "Each blow rattles the chains and stings the attacker."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_feet_dragged_chain_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_feet_dragged_chain_Power1),
            new UniqueDef("unique.feet_anchor_wall", "feet.chain_greaves", new Txt("錨の重み", "Weight of the Anchor"),
                new Txt("身を守る障壁と溢れる命で、一歩も動かず耐えよ。", "Barriers and overflowing life let you hold your ground."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_feet_anchor_wall_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_feet_anchor_wall_Power1),
            new UniqueDef("unique.feet_thunder_step", "feet.storm_boots", new Txt("雷歩", "Thunderstep"),
                new Txt("倒すたびに加速し、回避の渦で雷鳴を撒く。", "Speed up with every kill and scatter thunder with dodges."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_feet_thunder_step_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_feet_thunder_step_Power1),
            new UniqueDef("unique.feet_gale_hunter", "feet.storm_boots", new Txt("嵐の狩猟長", "Stormhunt Leader"),
                new Txt("追い風に乗って獲物に迫り、誇りを高めよ。", "Ride the tailwind and grow your pride in the hunt."),
                Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_feet_gale_hunter_Power0, Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_feet_gale_hunter_Power1),
            new UniqueDef("unique.feet_spark_trail", "feet.ember_slippers", new Txt("火の粉の轍", "Trail of Sparks"),
                new Txt("走り抜けた跡に火が広がり、敵を飲み込む。", "Fire spreads along your path and swallows enemies."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_feet_spark_trail_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_feet_spark_trail_Power1),
            new UniqueDef("unique.feet_cinder_waltz", "feet.ember_slippers", new Txt("灰のワルツ", "Cinder Waltz"),
                new Txt("身をかわすたび、スキルが早く戻り火が灯る。", "Every dodge hastens your skills and kindles flame."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_feet_cinder_waltz_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_feet_cinder_waltz_Power1),
            new UniqueDef("unique.charm_dusk_quill", "charm.raven_feather", new Txt("宵闇の羽ペン", "Duskquill"),
                new Txt("会心の余韻で技を回し、弱った敵を断て。", "Cycle skills with crits and cut down the weakened."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_charm_dusk_quill_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_charm_dusk_quill_Power1),
            new UniqueDef("unique.charm_omen_wing", "charm.raven_feather", new Txt("凶兆の翼", "Wing of Omens"),
                new Txt("獲物の誇りを胸に、闇を重ねて追い込め。", "Carry the hunter's pride and layer darkness on prey."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_charm_omen_wing_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_charm_omen_wing_Power1),
            new UniqueDef("unique.charm_white_lotus", "charm.lotus_seal", new Txt("白蓮の泉", "White Lotus Spring"),
                new Txt("あふれる癒しを障壁に、倒れそうな時にも立て。", "Convert overflow into barriers and rise at the brink."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_charm_white_lotus_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_charm_white_lotus_Power1),
            new UniqueDef("unique.charm_lotus_prayer", "charm.lotus_seal", new Txt("蓮の祈り", "Lotus Prayer"),
                new Txt("聖堂で祈りを重ね、障壁で静かに身を守る。", "Stack prayers at shrines and shelter behind barriers."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_charm_lotus_prayer_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_charm_lotus_prayer_Power1),
            new UniqueDef("unique.charm_thunder_finale", "charm.storm_bell", new Txt("雷の終曲", "Thunder Finale"),
                new Txt("三つの技を重ねて奥義を呼び、雷を連ねよ。", "Chain your three skills to hasten the ultimate, trailed by lightning."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_charm_thunder_finale_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_charm_thunder_finale_Power1),
            new UniqueDef("unique.charm_ringing_storm", "charm.storm_bell", new Txt("鳴り止まぬ嵐", "Unceasing Storm"),
                new Txt("技を放つほど魔力が高まり、足も速くなる。", "Each skill raises your magic and quickens your steps."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_charm_ringing_storm_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_charm_ringing_storm_Power1),
            new UniqueDef("unique.charm_cold_bind", "charm.ice_heart", new Txt("凍える枷心", "Frostbound Heart"),
                new Txt("冷気を重ね、動けぬ敵を一方的に叩け。", "Layer cold and batter foes who cannot move."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_charm_cold_bind_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_charm_cold_bind_Power1),
            new UniqueDef("unique.charm_crystal_pulse", "charm.ice_heart", new Txt("結晶の鼓動", "Crystal Pulse"),
                new Txt("結晶が増えるほど、心臓の力が体を満たす。", "The more crystals you wear, the stronger the pulse."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_charm_crystal_pulse_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_charm_crystal_pulse_Power1),
            new UniqueDef("unique.charm_ink_prism", "charm.ink_stone", new Txt("墨の万華鏡", "Inkwell Prism"),
                new Txt("四つの色を墨に溶かし、術を重ねて放て。", "Dissolve four colors in ink and cast in layers."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_charm_ink_prism_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_charm_ink_prism_Power1),
            new UniqueDef("unique.charm_night_script", "charm.ink_stone", new Txt("夜の筆致", "Night Script"),
                new Txt("術を連ね、闇を塗り重ねて奥義を早めよ。", "String your spells, paint darkness, and hasten the finale."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_charm_night_script_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_charm_night_script_Power1),
            // v1.26：記憶・エッセンス・旅人に連携する固有品
            new UniqueDef("unique.link_butchers_feast", "weapon.bone_cleaver", new Txt("肉屋の祝宴", "Butcher's Banquet"),
                new Txt("集めた肉は傷を癒し、刃をさらに育てる。", "The meat you gather mends your wounds and feeds the blade."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link_butchers_feast_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link_butchers_feast_Power1) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_butchers_feast_Link } },
            new UniqueDef("unique.link_arrow_rain", "weapon.hunting_bow", new Txt("天降る矢雨", "Heaven's Arrow Rain"),
                new Txt("空を覆うほどの矢が、味方の背を押して降り注ぐ。", "Arrows blot out the sky and rain down, driving you onward."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_arrow_rain_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_arrow_rain_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Multishot" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_arrow_rain_Link } },
            new UniqueDef("unique.link_fireball_avatar", "weapon.blaze_greatsword", new Txt("火球の化身", "Fireball Incarnate"),
                new Txt("身を火球に変えた魔女の熱が、剣に宿っている。", "The heat of a witch who became a fireball lingers in this blade."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_link_fireball_avatar_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link_fireball_avatar_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_fireball_avatar_Link } },
            new UniqueDef("unique.link_undying_ember_curse", "weapon.ember_whip", new Txt("消えずの熾", "Unquenched Cinder"),
                new Txt("一度燃え移った呪いの炎は、いつまでも消えない。", "Once the cursed flame catches, it never goes out."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link_undying_ember_curse_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_undying_ember_curse_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_undying_ember_curse_Link } },
            new UniqueDef("unique.link_white_flash", "weapon.lantern_rod", new Txt("白一色の閃光", "Flash of Pure White"),
                new Txt("十秒ごとに満ちる純白の光が、次の一撃を研ぎ澄ます。", "Every ten seconds a pure white light gathers and sharpens the next strike."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_white_flash_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_white_flash_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_PureWhite" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_white_flash_Link } },
            new UniqueDef("unique.link_dawn_breaker", "weapon.dawn_scepter", new Txt("大地を割る曙光", "Dawn That Splits the Earth"),
                new Txt("世界を割る光は、途切れることなく敵を焼き続ける。", "The light that splits the world keeps burning foes without end."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_dawn_breaker_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_dawn_breaker_Power1) { Link = new LinkDef { Requires = new[] { "St_U_WorldCracker" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_dawn_breaker_Link } },
            new UniqueDef("unique.link_hero_homecoming", "armor.lampkeeper_mantle", new Txt("英雄の還り道", "Hero's Way Home"),
                new Txt("倒れた英雄は還り、力を永遠に刻んで立ち上がる。", "A fallen hero returns, etches their power in stone, and rises."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link_hero_homecoming_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_link_hero_homecoming_Power1) { Link = new LinkDef { Requires = new[] { "St_L_HerosReturn" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_hero_homecoming_Link } },
            new UniqueDef("unique.link_suspicion_thorns", "armor.thorn_mail", new Txt("疑心の棘衣", "Mantle of Suspicion"),
                new Txt("疑り深い者ほど、傷を負うたびに記憶が速く巡る。", "The more suspicious you are, the faster memories turn with every wound."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_link_suspicion_thorns_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_link_suspicion_thorns_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_suspicion_thorns_Link } },
            new UniqueDef("unique.link_paired_shell", "armor.bastion_shell", new Txt("対なる殻", "Twin Shell"),
                new Txt("命の灯を一つに絞り、分厚い殻で包み込む。", "You narrow your life to a single flame and wrap it in a thick shell."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link_paired_shell_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_link_paired_shell_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_paired_shell_Link } },
            new UniqueDef("unique.link_dragon_whelp_forge", "armor.ember_plate", new Txt("仔竜の炉心", "Whelp Furnace"),
                new Txt("小さな炉の中で、炎の竜が眠りながら熱を吐く。", "Within the small furnace, a dragon of flame breathes heat in its sleep."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_link_dragon_whelp_forge_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_link_dragon_whelp_forge_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SmallMoltenCore" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_dragon_whelp_forge_Link } },
            new UniqueDef("unique.link_sealed_gold", "armor.spiked_plate", new Txt("封じられし黄金", "Sealed Gold"),
                new Txt("血を捧げて封印を解けば、黄金の爆発が敵を呑む。", "Offer blood to break the seal and a golden blast engulfs your foes."),
                Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_link_sealed_gold_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link_sealed_gold_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_SuppressedArcanum" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_sealed_gold_Link } },
            new UniqueDef("unique.link_flawless_vestment", "armor.star_mantle", new Txt("欠けなき衣", "Unblemished Vestment"),
                new Txt("何一つ欠けることなく、あらゆる力が少しずつ高まる。", "Nothing is lacking, and every strength grows a little."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link_flawless_vestment_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link_flawless_vestment_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_flawless_vestment_Link } },
            new UniqueDef("unique.link_snowstorm_ward", "charm.frost_pendant", new Txt("雪嵐の護り", "Ward of the Snowstorm"),
                new Txt("吹き荒れる雪嵐が、敵を凍らせ味方を包む。", "A raging snowstorm freezes foes and wraps allies in cover."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_link_snowstorm_ward_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link_snowstorm_ward_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_snowstorm_ward_Link } },
            new UniqueDef("unique.link_gold_coin_flicker", "charm.sun_brooch", new Txt("金貨の閃き", "Flash of the Gold Coin"),
                new Txt("投げた金貨が裏か表か、光が弾けて運命が決まる。", "Heads or tails, a coin flips and light bursts to decide your fate."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link_gold_coin_flicker_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link_gold_coin_flicker_Power1) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_gold_coin_flicker_Link } },
            new UniqueDef("unique.link_hollow_her", "charm.shadow_ring", new Txt("虚空の彼女", "She of the Hollow"),
                new Txt("彼女の世界は黒く渦を巻き、すべてを呑み込んでいく。", "Her world coils in black and swallows everything."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_link_hollow_her_Power0, Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_link_hollow_her_Power1) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_hollow_her_Link } },
            new UniqueDef("unique.link_priceless_present", "charm.lotus_seal", new Txt("値のない贈り物", "Priceless Present"),
                new Txt("値札のない贈り物は、受け取るほどに持ち主を守る。", "A gift without a price tag protects its owner all the more."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link_priceless_present_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_priceless_present_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_CamillasGift" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_priceless_present_Link } },
            new UniqueDef("unique.link_golden_pulse", "charm.iron_seal", new Txt("金色の拍動", "Golden Pulse"),
                new Txt("懐の金貨を燃やすたび、心臓が力強く脈を打つ。", "Each time you burn coin from your purse, your heart beats stronger."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link_golden_pulse_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_golden_pulse_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_golden_pulse_Link } },
            new UniqueDef("unique.link_rainbow_ore", "charm.dream_lens", new Txt("虹色の鉱石", "Iridescent Ore"),
                new Txt("光を受けて不思議な色に輝く結晶が、力を育てる。", "A crystal glowing in uncanny hues under the light nurtures your power."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_link_rainbow_ore_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link_rainbow_ore_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_MetalCrystal" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_rainbow_ore_Link } },
            new UniqueDef("unique.link_bursting_halo", "head.radiant_halo", new Txt("炸裂する光輪", "Bursting Halo"),
                new Txt("光が弾けるたび、倒れた敵の跡で再び炸裂する。", "Each burst of light erupts again where a felled enemy lay."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_bursting_halo_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_link_bursting_halo_Power1) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_bursting_halo_Link } },
            new UniqueDef("unique.link_balance_pillar", "head.sun_mask", new Txt("天秤の光柱", "Pillar of the Scales"),
                new Txt("巨大な光の柱が、敵を灼き仲間を癒して釣り合う。", "A colossal pillar of light scorches foes and heals allies in balance."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_balance_pillar_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link_balance_pillar_Power1) { Link = new LinkDef { Requires = new[] { "St_U_BeamOfBalance" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_balance_pillar_Link } },
            new UniqueDef("unique.link_forgetting_cry", "head.raven_mask", new Txt("忘れ去る叫び", "Cry of Forgetting"),
                new Txt("世界の記憶を塗り潰す叫びが、闇となって轟く。", "A cry that paints over the world memory roars forth as darkness."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_link_forgetting_cry_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_link_forgetting_cry_Power1) { Link = new LinkDef { Requires = new[] { "St_U_ShoutOfOblivion" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_forgetting_cry_Link } },
            new UniqueDef("unique.link_discord_fruit", "head.dream_veil", new Txt("不和の果実", "Fruit of Discord"),
                new Txt("かじるたびに記憶は別の姿へ変わり、気まぐれに力をくれる。", "With every bite a memory changes shape and lends whimsical power."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_discord_fruit_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_link_discord_fruit_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_ChaosApple" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_discord_fruit_Link } },
            new UniqueDef("unique.link_stacked_prayers", "head.healer_band", new Txt("信仰の積み重ね", "Stacked Devotion"),
                new Txt("討つたびに信仰が積もり、祈りは確かな力となる。", "Each foe you fell adds to your faith until prayer becomes real power."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_link_stacked_prayers_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_link_stacked_prayers_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_stacked_prayers_Link } },
            new UniqueDef("unique.link_final_star_lamp", "head.star_diadem", new Txt("終星の灯", "Lamp of the Final Star"),
                new Txt("最後の星明かりが堕ちる場所に、黒い穴が口を開ける。", "Where the last starlight falls, a black hole opens its mouth."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link_final_star_lamp_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_final_star_lamp_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_LastStarlight" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_final_star_lamp_Link } },
            new UniqueDef("unique.link_creeping_dark_claws", "hands.void_claws", new Txt("蝕む闇の爪", "Claws of Creeping Dark"),
                new Txt("心を蝕む闇は、敵が息絶えるまで消えることがない。", "The darkness that corrodes the mind never fades until the enemy dies."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_link_creeping_dark_claws_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link_creeping_dark_claws_Power1) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_creeping_dark_claws_Link } },
            new UniqueDef("unique.link_mad_laughter_fists", "hands.shadow_gloves", new Txt("狂笑の乱舞", "Dance of Mad Laughter"),
                new Txt("狂ったように笑いながら、闇の拳を絶え間なく浴びせる。", "Laughing madly, you rain dark fists without pause."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_link_mad_laughter_fists_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_mad_laughter_fists_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_mad_laughter_fists_Link } },
            new UniqueDef("unique.link_rebounding_flametail", "hands.flame_grips", new Txt("跳ね返る火尾", "Rebounding Flametail"),
                new Txt("跳ね返った攻撃が、尾を引く炎となって別の敵を焼く。", "A rebounding strike trails flame and scorches another foe."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link_rebounding_flametail_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_link_rebounding_flametail_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Embertail" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_rebounding_flametail_Link } },
            new UniqueDef("unique.link_frozen_heart_mend", "hands.frost_mitts", new Txt("氷核の癒し", "Mending of the Ice Core"),
                new Txt("凍てつく核が時折脈打ち、傷をゆっくり癒していく。", "The frozen core pulses now and then, slowly mending your wounds."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_link_frozen_heart_mend_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link_frozen_heart_mend_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_frozen_heart_mend_Link } },
            new UniqueDef("unique.link_phantom_pebbles", "hands.sling_bracers", new Txt("亡霊の礫", "Phantom Pebbles"),
                new Txt("撃ち抜いた敵の魂が、夢の塵となって手元に残る。", "The souls of shot foes remain in your hand as dream dust."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_phantom_pebbles_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_link_phantom_pebbles_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SpectreBullet" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_phantom_pebbles_Link } },
            new UniqueDef("unique.link_scorching_sun_gaze", "hands.sun_gauntlets", new Txt("灼陽の瞳", "Gaze of the Scorching Sun"),
                new Txt("二十秒ごとに太陽の眼が開き、燃える敵を一斉に焼く。", "Every twenty seconds the sun eye opens and burns all burning foes at once."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_link_scorching_sun_gaze_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_scorching_sun_gaze_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_SolarEye" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_scorching_sun_gaze_Link } },
            new UniqueDef("unique.link_earthdiver_boots", "feet.rooted_boots", new Txt("土潜りの靴", "Earthdiver Boots"),
                new Txt("地の底に潜れば、どんな攻撃も届かない。", "Dive beneath the earth and no attack can reach you."),
                Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link_earthdiver_boots_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link_earthdiver_boots_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Burrow" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_earthdiver_boots_Link } },
            new UniqueDef("unique.link_fang_leap", "feet.wolf_boots", new Txt("牙の跳躍", "Fang Leap"),
                new Txt("大口を開けて飛びつき、獲物の力を丸ごと食らう。", "Leap with jaws wide and devour your prey whole."),
                Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_link_fang_leap_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_fang_leap_Power1) { Link = new LinkDef { Requires = new[] { "St_U_BigChomp" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_fang_leap_Link } },
            new UniqueDef("unique.link_soul_cage_clogs", "feet.iron_clogs", new Txt("魂を閉ざす檻", "Cage Sealing the Soul"),
                new Txt("致命の一撃を檻が受け止め、魂は束の間の無敵を得る。", "The cage takes the fatal blow and the soul gains a brief invulnerability."),
                Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link_soul_cage_clogs_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link_soul_cage_clogs_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_SoulPrison" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_soul_cage_clogs_Link } },
            new UniqueDef("unique.link_cook_stride", "feet.pilgrim_boots", new Txt("料理人の足取り", "Stride of the Cook"),
                new Txt("倒れた敵から食材を拾い集め、旅の腹を満たしていく。", "You gather ingredients from fallen foes and feed the journey."),
                Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_link_cook_stride_Power0, Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_link_cook_stride_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Culinary" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_cook_stride_Link } },
            new UniqueDef("unique.link_unbound_steps", "feet.wind_sandals", new Txt("解き放たれた足", "Unbound Steps"),
                new Txt("枷を解いた足は軽く、記憶を放つたび全身が力にあふれる。", "With shackles shed, your feet are light and each memory floods you with power."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link_unbound_steps_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_link_unbound_steps_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_unbound_steps_Link } },
            new UniqueDef("unique.link_stalled_needle_trail", "feet.dawn_steps", new Txt("止まった針の旅路", "Journey of the Stilled Needle"),
                new Txt("針の止まった羅針盤が、強い力を待ちわびている。", "A compass with a stilled needle waits for a mighty power."),
                Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_link_stalled_needle_trail_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link_stalled_needle_trail_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GuidingCompass_NotCharged" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_stalled_needle_trail_Link } },
            new UniqueDef("unique.link_vesper_holy_vow", "armor.guardian_plate", new Txt("聖盾の宣誓", "Vow of the Holy Shield"),
                new Txt("エルの名のもとに、誰も倒れさせはしない。", "In El's name, no one shall fall."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link_vesper_holy_vow_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link_vesper_holy_vow_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_vesper_holy_vow_Link } },
            new UniqueDef("unique.link_vesper_charge_rampart", "feet.guard_sabatons", new Txt("鉄壁の突進", "Rampart Rush"),
                new Txt("盾を構えたまま、運命の中へ踏み込め。", "Charge into fate with your shield held high."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link_vesper_charge_rampart_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link_vesper_charge_rampart_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_M_Charge" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_vesper_charge_rampart_Link } },
            new UniqueDef("unique.link_vesper_resolve_mercy", "weapon.shield_maul", new Txt("決意と慈悲", "Resolve and Mercy"),
                new Txt("四度目の一撃で止め、慈悲の光で立たせる。", "The fourth strike halts them; mercy lifts them up."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link_vesper_resolve_mercy_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link_vesper_resolve_mercy_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_D_Resolve", "St_D_MercyOfEl" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_vesper_resolve_mercy_Link } },
            new UniqueDef("unique.link_vesper_cruel_sun", "hands.ember_gauntlets", new Txt("裁きの烈陽", "Sun of Judgment"),
                new Txt("容赦なき陽が、膝をつく者を照らし出す。", "A merciless sun lights up those who kneel."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link_vesper_cruel_sun_Power0, Power.StillWater, EquipmentItemsBalanceValues.Unique_unique_link_vesper_cruel_sun_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_CruelSun" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_vesper_cruel_sun_Link } },
            new UniqueDef("unique.link_lacerta_gunslinger", "armor.hunter_vest", new Txt("銃士の矜持", "Gunslinger's Pride"),
                new Txt("抜く手も見せず、勝負はもう終わっている。", "The duel is over before the hand is even seen."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_gunslinger_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_gunslinger_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_lacerta_gunslinger_Link } },
            new UniqueDef("unique.link_lacerta_dodge_draw", "feet.dancer_shoes", new Txt("すれ違いの早撃ち", "Quickdraw in Passing"),
                new Txt("かわした瞬間に、もう銃口は向いている。", "The instant you dodge, the muzzle is already aimed."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_dodge_draw_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_dodge_draw_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_M_NimbleDodge" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_lacerta_dodge_draw_Link } },
            new UniqueDef("unique.link_lacerta_powder_tap", "hands.flame_grips", new Txt("双発の火薬庫", "Twin-Shot Powder Keg"),
                new Txt("二連の銃声のあとに、四度目の爆炎が咲く。", "After two shots, the fourth blooms into flame."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_powder_tap_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_powder_tap_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_D_DoubleTap", "St_D_SalamanderPowder" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_lacerta_powder_tap_Link } },
            new UniqueDef("unique.link_lacerta_precision", "weapon.longspike_bow", new Txt("必中の一射", "The Sure Shot"),
                new Txt("息を止め、狙いを定め、星を撃ち抜く。", "Hold your breath, take aim, and shoot a star."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_precision_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link_lacerta_precision_Power1) { Link = new LinkDef { Requires = new[] { "St_R_PrecisionShot" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_lacerta_precision_Link } },
            new UniqueDef("unique.link_cetus_glacier_guard", "armor.bastion_shell", new Txt("氷河の守護者", "Glacier Guardian"),
                new Txt("凍りついた背中は、仲間を守る盾になる。", "A frozen back becomes a shield for allies."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link_cetus_glacier_guard_Power0, Power.StillWater, EquipmentItemsBalanceValues.Unique_unique_link_cetus_glacier_guard_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_cetus_glacier_guard_Link } },
            new UniqueDef("unique.link_cetus_frozen_blood", "hands.frost_mitts", new Txt("凍血の誓い", "Oath of Frozen Blood"),
                new Txt("血が凍るほど、一撃は冷たく重くなる。", "The colder the blood, the heavier the blow."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_link_cetus_frozen_blood_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_cetus_frozen_blood_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_D_IcyVeins" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_cetus_frozen_blood_Link } },
            new UniqueDef("unique.link_cetus_cold_front", "weapon.glacier_spear", new Txt("寒波の砕氷", "Cold Front Breaker"),
                new Txt("巨大な氷塊が落ち、寒気がすべてを閉ざす。", "A giant chunk of ice falls, and the chill seals all."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_link_cetus_cold_front_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_cetus_cold_front_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_Q_BigBorealChunk", "St_Q_EmbracingTheChill" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_cetus_cold_front_Link } },
            new UniqueDef("unique.link_cetus_manners", "head.frost_helm", new Txt("礼節の氷拳", "Courteous Ice Fist"),
                new Txt("礼を尽くした拳は、氷よりも固く痛い。", "A courteous fist is harder and colder than ice."),
                Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link_cetus_manners_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link_cetus_manners_Power1) { Link = new LinkDef { Requires = new[] { "St_R_FrozenFists" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_cetus_manners_Link } },
            new UniqueDef("unique.link_yubar_star_traveler", "armor.star_cloak", new Txt("星間の旅人", "Interstellar Traveler"),
                new Txt("星々のあいだを、ひとすじの光が渡っていく。", "A single beam of light crosses between the stars."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link_yubar_star_traveler_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_link_yubar_star_traveler_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_yubar_star_traveler_Link } },
            new UniqueDef("unique.link_yubar_still_clock", "head.dream_circlet", new Txt("静謐の星時計", "Serene Star Clock"),
                new Txt("心を凍らせれば、時は味方についてくる。", "Still your heart, and time will take your side."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_link_yubar_still_clock_Power0, Power.LucidBoon, EquipmentItemsBalanceValues.Unique_unique_link_yubar_still_clock_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_R_Tranquility" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_yubar_still_clock_Link } },
            new UniqueDef("unique.link_yubar_nova_end", "weapon.star_harp", new Txt("終焉の新星", "Nova of Endings"),
                new Txt("星は一度だけ満ち、世界を白く塗り替える。", "The star swells only once, painting the world white."),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_link_yubar_nova_end_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_link_yubar_nova_end_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_Q_SuperNova", "St_R_Cataclysm" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_yubar_nova_end_Link } },
            new UniqueDef("unique.link_yubar_star_echo", "charm.clockwork_charm", new Txt("星々の反響", "Echo of Stars"),
                new Txt("放った光は、星々に跳ね返って戻ってくる。", "The light you cast bounces off the stars and returns."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_link_yubar_star_echo_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_link_yubar_star_echo_Power1) { Link = new LinkDef { Requires = new[] { "St_D_ConvergencePoint" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_yubar_star_echo_Link } },
            new UniqueDef("unique.link_husk_one_flash", "weapon.dusk_scythe", new Txt("一閃の旅人", "Traveler of One Flash"),
                new Txt("風より先に、刃のほうが通り過ぎていく。", "The blade passes by before the wind does."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_husk_one_flash_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_link_husk_one_flash_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_husk_one_flash_Link } },
            new UniqueDef("unique.link_husk_death_glove", "hands.shadow_gloves", new Txt("死告げの手袋", "Gloves of Foretold Death"),
                new Txt("刻まれた名は、いずれ必ず闇へ還っていく。", "A name once marked will return to the dark."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_link_husk_death_glove_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link_husk_death_glove_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_Q_DeathMark" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_husk_death_glove_Link } },
            new UniqueDef("unique.link_husk_wind_scar", "feet.stalker_boots", new Txt("風傷の殺陣", "Wind-Scar Melee"),
                new Txt("ひと走りごとに、一つの影が消えていく。", "With each dash, another shadow disappears."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link_husk_wind_scar_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_husk_wind_scar_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_D_TheKillingFlow", "St_D_ScarOfTheWind" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_husk_wind_scar_Link } },
            new UniqueDef("unique.link_husk_vanish", "armor.shadow_cloak", new Txt("霞み消える者", "One Who Fades Away"),
                new Txt("姿を消した者は、いつも背後から現れる。", "Those who vanish always reappear behind you."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link_husk_vanish_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_link_husk_vanish_Power1) { Link = new LinkDef { Requires = new[] { "St_R_Deception" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_husk_vanish_Link } },
            new UniqueDef("unique.link_mist_swordsman", "head.mist_veil", new Txt("霧の剣客", "Swordsman of Mist"),
                new Txt("霧が晴れたとき、剣先だけがそこに残る。", "When the mist clears, only the point remains."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link_mist_swordsman_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link_mist_swordsman_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_mist_swordsman_Link } },
            new UniqueDef("unique.link_mist_riposte", "armor.counter_gauntlets", new Txt("受けて返す剣", "Parry and Return"),
                new Txt("弾いた刃の勢いを、そのまま突きに乗せる。", "Ride the force of the deflected blade into a thrust."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_link_mist_riposte_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link_mist_riposte_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_R_Parry" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_mist_riposte_Link } },
            new UniqueDef("unique.link_mist_masterwork", "weapon.twin_rapier", new Txt("傑作の連剣", "Masterwork Flurry"),
                new Txt("守りと速さを重ねた一突きが、絵になる。", "A thrust of guard and speed becomes a painting."),
                Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link_mist_masterwork_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_mist_masterwork_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_D_AstridsMasterpieceEnGarde", "St_D_AstridsMasterpiecePriorite" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_mist_masterwork_Link } },
            new UniqueDef("unique.link_mist_fleche", "feet.dancer_shoes", new Txt("矢の如き踏み込み", "Arrow-Swift Lunge"),
                new Txt("間合いを一瞬で詰め、ただ一太刀で決める。", "Close the gap in an instant and end it in one cut."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_mist_fleche_Power0, Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_link_mist_fleche_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Fleche" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_mist_fleche_Link } },
            new UniqueDef("unique.link_nachia_wolf_shaman", "charm.oak_amulet", new Txt("狼の導き手", "Wolf Shaman"),
                new Txt("群れを率いる者は、牙より先に腕を広げる。", "The pack's mother opens her arms before her fangs."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_link_nachia_wolf_shaman_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link_nachia_wolf_shaman_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_nachia_wolf_shaman_Link } },
            new UniqueDef("unique.link_nachia_leaf_call", "feet.root_sandals", new Txt("葉犬の呼び声", "Call of the Leaf Hound"),
                new Txt("森が呼べば、緑の猟犬がどこからでも駆けてくる。", "When the forest calls, green hounds come running."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link_nachia_leaf_call_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_link_nachia_leaf_call_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_SylvanCall" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_nachia_leaf_call_Link } },
            new UniqueDef("unique.link_nachia_moon_circle", "weapon.bone_flute", new Txt("月光の命環", "Moonlit Circle of Life"),
                new Txt("月夜に走る人狼へ、命の恵みが巡っていく。", "Life flows around the werewolf running under the moon."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_link_nachia_moon_circle_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link_nachia_moon_circle_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_MoonlightPact", "St_D_CircleOfLife" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_nachia_moon_circle_Link } },
            new UniqueDef("unique.link_nachia_pack_heart", "head.wolf_pelt", new Txt("群れの心音", "Heartbeat of the Pack"),
                new Txt("仲間が強くなるほど、自分の鼓動も高鳴る。", "The stronger your allies, the faster your pulse."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_link_nachia_pack_heart_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_link_nachia_pack_heart_Power1) { Link = new LinkDef { Requires = new[] { "St_D_HeartOfThePack" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_nachia_pack_heart_Link } },
            new UniqueDef("unique.link_aurena_light_archer", "weapon.dawn_scepter", new Txt("光の射手", "Archer of Light"),
                new Txt("遠くから放つ光は、敵にも味方にも届く。", "Light shot from afar reaches foe and friend alike."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_aurena_light_archer_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link_aurena_light_archer_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_aurena_light_archer_Link } },
            new UniqueDef("unique.link_aurena_golden_grace", "hands.healer_hands", new Txt("黄金の恵み", "Golden Grace"),
                new Txt("光が弾けるたび、傷が静かに癒えていく。", "Each burst of light quietly heals the wounded."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link_aurena_golden_grace_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link_aurena_golden_grace_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_Q_GoldenBurst" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_aurena_golden_grace_Link } },
            new UniqueDef("unique.link_aurena_return_chain", "armor.sun_plate", new Txt("還元の連鎖光", "Chain of Returning Light"),
                new Txt("敵に注いだ力が、味方の傷口へ還っていく。", "The power poured on foes returns to mend your allies."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_link_aurena_return_chain_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link_aurena_return_chain_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_Q_Reduction", "St_R_ChainReaction" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_aurena_return_chain_Link } },
            new UniqueDef("unique.link_aurena_claw", "charm.sun_brooch", new Txt("命を啜る爪", "Life-Drinking Claw"),
                new Txt("奪った命を、そのまま自分の力に変える。", "Turn the life you take into your own strength."),
                Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_link_aurena_claw_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link_aurena_claw_Power1) { Link = new LinkDef { Requires = new[] { "St_D_DisintegratingClaw" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link_aurena_claw_Link } },
            new UniqueDef("unique.link_bismuth_facet", "charm.dream_lens", new Txt("多面の結晶体", "Faceted Crystal Body"),
                new Txt("どの面も光を返し、どの色も嘘をつかない。", "Every facet returns the light, and no color lies."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_facet_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_facet_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_bismuth_facet_Link } },
            new UniqueDef("unique.link_bismuth_prism_dash", "feet.wind_sandals", new Txt("歪光の疾走", "Warped-Light Sprint"),
                new Txt("走り抜けた軌跡が、虹色の残像を引く。", "The path you run leaves a rainbow afterimage."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_prism_dash_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_prism_dash_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_M_Sprint" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link_bismuth_prism_dash_Link } },
            new UniqueDef("unique.link_bismuth_crimson_white", "head.radiant_halo", new Txt("紅蓮と純白", "Crimson and White"),
                new Txt("燃える炎と清い光が、結晶の中で重なる。", "Blazing flame and pure light overlap inside the crystal."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_crimson_white_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_crimson_white_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_QR_InfernalTales", "St_QR_Innocence" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link_bismuth_crimson_white_Link } },
            new UniqueDef("unique.link_bismuth_prism_eye", "weapon.hunting_bow", new Txt("プリズムの眼", "Prism Eye"),
                new Txt("瞳が光を束ね、勝手に敵を射抜いていく。", "The eye gathers light and pierces foes by itself."),
                Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_prism_eye_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link_bismuth_prism_eye_Power1) { Link = new LinkDef { Requires = new[] { "St_D_PrismaticEyes" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link_bismuth_prism_eye_Link } },
            new UniqueDef("unique.link2_frostbound_slumber", "armor.frost_coat", new Txt("凍土の眠り", "Frostbound Slumber"),
                new Txt("吹雪の下で土に潜れば、春まで誰にも見つからない。", "Burrow beneath the blizzard and no one finds you until spring."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link2_frostbound_slumber_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link2_frostbound_slumber_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard", "St_U_Burrow" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_frostbound_slumber_Link } },
            new UniqueDef("unique.link2_carnival_of_cleavers", "weapon.bone_cleaver", new Txt("狂宴の肉切り", "Carnival of Cleavers"),
                new Txt("肉を断つ音が重なるほど、笑い声は大きくなる。", "The more the cleaver sings, the louder the laughter grows."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link2_carnival_of_cleavers_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link2_carnival_of_cleavers_Power1) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike", "St_U_Hysteria" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_carnival_of_cleavers_Link } },
            new UniqueDef("unique.link2_golden_flash", "head.sun_mask", new Txt("黄金の閃光", "Golden Flash"),
                new Txt("投げた金貨が光となって弾け、闇を押しのける。", "A tossed coin bursts into light and shoves the dark aside."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link2_golden_flash_Power0, Power.LucidBoon, EquipmentItemsBalanceValues.Unique_unique_link2_golden_flash_Power1) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "St_L_LightExplosion" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_golden_flash_Link } },
            new UniqueDef("unique.link2_oblivious_madness", "head.void_helm", new Txt("忘却の狂想", "Rhapsody of Oblivion"),
                new Txt("正気を蝕む闇に、忘却の叫びが最後の一押しを添える。", "A shout of oblivion pushes the mind-eating dark over the edge."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_link2_oblivious_madness_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_link2_oblivious_madness_Power1) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption", "St_U_ShoutOfOblivion" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_oblivious_madness_Link } },
            new UniqueDef("unique.link2_ghost_arrow_rain", "weapon.longspike_bow", new Txt("霊矢の雨", "Rain of Ghost Arrows"),
                new Txt("放った矢は霊となり、倒れた敵の夢まで射抜く。", "Each arrow becomes a spirit and pierces even a fallen foe's dreams."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link2_ghost_arrow_rain_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_link2_ghost_arrow_rain_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Multishot", "St_L_SpectreBullet" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_ghost_arrow_rain_Link } },
            new UniqueDef("unique.link2_dragon_hearth", "armor.ember_plate", new Txt("炎龍の炉心", "Heart of the Fire Drake"),
                new Txt("火球と溶鉱炉が重なり、小さな龍が目を覚ます。", "Fireball meets furnace, and a small drake opens its eyes."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_link2_dragon_hearth_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link2_dragon_hearth_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "St_L_SmallMoltenCore" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_dragon_hearth_Link } },
            new UniqueDef("unique.link2_returning_beam", "charm.pulsing_core", new Txt("帰還の光条", "Beam of Return"),
                new Txt("倒れた英雄を、均衡の光が静かに引き戻す。", "A balanced beam of light gently draws the fallen hero back."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link2_returning_beam_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link2_returning_beam_Power1) { Link = new LinkDef { Requires = new[] { "St_L_HerosReturn", "St_U_BeamOfBalance" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_returning_beam_Link } },
            new UniqueDef("unique.link2_void_worldbreak", "weapon.dusk_scythe", new Txt("虚空の砕世", "Void Worldbreaker"),
                new Txt("彼女の世界が開き、世界の殻が音もなく砕ける。", "Her world opens, and the shell of the world cracks without a sound."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link2_void_worldbreak_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link2_void_worldbreak_Power1) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "St_U_WorldCracker" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_void_worldbreak_Link } },
            new UniqueDef("unique.link2_feast_maw", "hands.bone_knuckles", new Txt("食卓の大顎", "Maw of the Feast"),
                new Txt("大きな一口の後には、必ず豪華な料理が並ぶ。", "After every great bite, a lavish meal is laid out."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link2_feast_maw_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link2_feast_maw_Power1) { Link = new LinkDef { Requires = new[] { "St_U_BigChomp", "Gem_L_Culinary" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_feast_maw_Link } },
            new UniqueDef("unique.link2_undying_furnace", "charm.hearthstone", new Txt("不滅の炉火", "Undying Furnace"),
                new Txt("消えぬ呪いの炎が、小さな炉をいつまでも熱くする。", "A curse of unending flame keeps the little furnace burning."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link2_undying_furnace_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link2_undying_furnace_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SmallMoltenCore", "Gem_U_EternalFlame" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_undying_furnace_Link } },
            new UniqueDef("unique.link2_glacier_waking", "armor.frost_robe", new Txt("氷河の目覚め", "Glacier's Waking"),
                new Txt("吹雪が止むころ、氷河の核が静かに脈を打つ。", "When the blizzard fades, the glacier's core begins to beat."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link2_glacier_waking_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link2_glacier_waking_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard", "Gem_U_GlacialCore" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_glacier_waking_Link } },
            new UniqueDef("unique.link2_golden_wager", "charm.clockwork_charm", new Txt("黄金の賭け", "Golden Wager"),
                new Txt("金貨を弾いて賭けに出れば、心臓が黄金色に脈打つ。", "Flip the coin and wager, and your heart beats gold."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link2_golden_wager_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_link2_golden_wager_Power1) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "Gem_L_HeartOfGold" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_golden_wager_Link } },
            new UniqueDef("unique.link2_whiteout_burst", "head.radiant_halo", new Txt("純白の爆光", "Whiteout Burst"),
                new Txt("十秒の静寂ののち、純白の光が戦場を満たす。", "After ten silent seconds, pure white light floods the field."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link2_whiteout_burst_Power0, Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link2_whiteout_burst_Power1) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion", "Gem_L_PureWhite" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_whiteout_burst_Link } },
            new UniqueDef("unique.link2_starlit_abyss", "armor.star_mantle", new Txt("星明かりの深淵", "Starlit Abyss"),
                new Txt("最後の星明かりが、彼女の世界へ吸い込まれていく。", "The last starlight is drawn into her world."),
                Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link2_starlit_abyss_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link2_starlit_abyss_Power1) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "Gem_U_LastStarlight" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_starlit_abyss_Link } },
            new UniqueDef("unique.link2_sunborn_fireball", "weapon.ember_whip", new Txt("太陽の火球", "Sunborn Fireball"),
                new Txt("太陽の眼が開くとき、火球は昼を連れてくる。", "When the sun's eye opens, the fireball carries the day with it."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link2_sunborn_fireball_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link2_sunborn_fireball_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "Gem_L_SolarEye" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_sunborn_fireball_Link } },
            new UniqueDef("unique.link2_soul_binding_shot", "feet.raven_boots", new Txt("魂縛の弾丸", "Soulbinding Bullet"),
                new Txt("霊弾は囚われの魂を撃ち、牢の扉を一度だけ開く。", "The spectral bullet strikes a captive soul and opens its cell once."),
                Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link2_soul_binding_shot_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_link2_soul_binding_shot_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SpectreBullet", "Gem_U_SoulPrison" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_soul_binding_shot_Link } },
            new UniqueDef("unique.link2_doubt_frenzy", "hands.void_claws", new Txt("疑心の乱舞", "Dance of Doubt"),
                new Txt("傷つくたび疑いは深まり、刃は速さを増していく。", "Each wound deepens the doubt, and the blade only quickens."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_link2_doubt_frenzy_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_link2_doubt_frenzy_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria", "Gem_L_Paranoia" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link2_doubt_frenzy_Link } },
            new UniqueDef("unique.link2_forbidden_gold_fire", "hands.shadow_gloves", new Txt("禁じられた金炎", "Forbidden Golden Flame"),
                new Txt("闇を蝕む呪いに、黄金の爆発が代償を求める。", "The creeping curse of darkness is met by a golden blast that demands its price."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link2_forbidden_gold_fire_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link2_forbidden_gold_fire_Power1) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption", "Gem_L_SuppressedArcanum" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link2_forbidden_gold_fire_Link } },
            new UniqueDef("unique.link2_penniless_tycoon", "charm.iron_seal", new Txt("無一文の富豪", "Penniless Tycoon"),
                new Txt("ただで手放した贈り物が、黄金の心臓を満たしていく。", "A gift given away for nothing fills the golden heart."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link2_penniless_tycoon_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_link2_penniless_tycoon_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_CamillasGift", "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_penniless_tycoon_Link } },
            new UniqueDef("unique.link2_stray_star_needle", "feet.star_slippers", new Txt("迷い星の針", "Needle of the Stray Star"),
                new Txt("気まぐれなリンゴが、壊れた羅針盤を逆向きに回す。", "A capricious apple spins the broken compass the wrong way."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link2_stray_star_needle_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link2_stray_star_needle_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_ChaosApple", "Gem_U_GuidingCompass_NotCharged" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_stray_star_needle_Link } },
            new UniqueDef("unique.link2_prayer_complete", "armor.prayer_shawl", new Txt("祈りの完成", "Prayer Made Whole"),
                new Txt("揺るがぬ信仰が、欠けのない身体へ力を注ぐ。", "Unshaken faith pours strength into a body without flaw."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link2_prayer_complete_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link2_prayer_complete_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith", "Gem_L_Perfect" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link2_prayer_complete_Link } },
            new UniqueDef("unique.link2_unending_embers", "weapon.ember_katar", new Txt("終わらぬ熾火", "Unending Embers"),
                new Txt("跳ね返る一撃が、消えない炎を連れて戻ってくる。", "The rebounding blow returns trailing a flame that will not die."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link2_unending_embers_Power0, Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_link2_unending_embers_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Embertail", "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_unending_embers_Link } },
            new UniqueDef("unique.link2_crystal_whitelight", "head.dream_circlet", new Txt("結晶の白光", "Crystal Whitelight"),
                new Txt("非現実の色に輝く結晶が、純白の光を砕いて返す。", "The surreal crystal shatters pure white light and gives it back."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_link2_crystal_whitelight_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_link2_crystal_whitelight_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_MetalCrystal", "Gem_L_PureWhite" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_crystal_whitelight_Link } },
            new UniqueDef("unique.link2_single_point_release", "armor.guardian_plate", new Txt("解放の一点", "Point of Release"),
                new Txt("体力は一つきり、それでも解き放たれた力は止まらない。", "With a single point of life, the unleashed power still will not stop."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link2_single_point_release_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_link2_single_point_release_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty", "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link2_single_point_release_Link } },
            new UniqueDef("unique.link2_cage_of_doubt", "feet.iron_clogs", new Txt("疑いの檻", "Cage of Doubt"),
                new Txt("疑念が牢を固く閉ざし、致命の一撃さえ空を切る。", "Doubt seals the cell tight, and even a fatal blow cuts only air."),
                Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link2_cage_of_doubt_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link2_cage_of_doubt_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia", "Gem_U_SoulPrison" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link2_cage_of_doubt_Link } },
            new UniqueDef("unique.link2_saint_covenant", "head.knight_helm", new Txt("聖騎士の誓約", "Saint's Covenant"),
                new Txt("信仰を重ねた者の前で、傷は静かに塞がっていく。", "Before the saint of layered faith, wounds close in quiet."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link2_saint_covenant_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link2_saint_covenant_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "Gem_L_DivineFaith" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link2_saint_covenant_Link } },
            new UniqueDef("unique.link2_ricochet_tail_fire", "hands.flame_grips", new Txt("跳弾の尾火", "Ricochet Tailfire"),
                new Txt("撃ち返された火の粉が、獲物の背に尾を引く。", "Sparks flung back trail behind the prey like a tail."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_link2_ricochet_tail_fire_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link2_ricochet_tail_fire_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "Gem_L_Embertail" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_ricochet_tail_fire_Link } },
            new UniqueDef("unique.link2_icefang_warden", "armor.bastion_shell", new Txt("氷牙の守り手", "Warden of the Ice Fang"),
                new Txt("氷河の核を抱く者は、凍える日も倒れない。", "One who holds a glacier's core does not fall even on the coldest day."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_link2_icefang_warden_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_link2_icefang_warden_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link2_icefang_warden_Link } },
            new UniqueDef("unique.link2_stardust_observer", "head.star_diadem", new Txt("星屑の観測者", "Stardust Observer"),
                new Txt("最後の星明かりを読み解けば、星が指先で道を示す。", "Reading the last starlight, he lets the stars point the way from his fingertips."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link2_stardust_observer_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_link2_stardust_observer_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "Gem_U_LastStarlight" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_stardust_observer_Link } },
            new UniqueDef("unique.link2_gatecrash_slash", "feet.hunter_boots", new Txt("獄門の一閃", "Prison Gate Slash"),
                new Txt("致命の刃を見切った瞬間、影は牢を抜けて斬り返す。", "The instant he reads a fatal blade, the shadow slips the cell and cuts back."),
                Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_link2_gatecrash_slash_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link2_gatecrash_slash_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "Gem_U_SoulPrison" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_gatecrash_slash_Link } },
            new UniqueDef("unique.link2_flawless_thrust", "weapon.twin_rapier", new Txt("完璧な一突き", "Flawless Thrust"),
                new Txt("寸分の狂いなき突きは、決闘の幕を一度で下ろす。", "A thrust without the slightest flaw drops the curtain on a duel at once."),
                Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link2_flawless_thrust_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_link2_flawless_thrust_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_flawless_thrust_Link } },
            new UniqueDef("unique.link2_pack_unleashed", "armor.root_mail", new Txt("群れの解放", "Pack Unleashed"),
                new Txt("鎖を解かれた群れは、主の声に合わせて牙を剥く。", "Freed from their chains, the pack bares fangs at their master's call."),
                Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_link2_pack_unleashed_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_link2_pack_unleashed_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "Gem_L_Liberty" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link2_pack_unleashed_Link } },
            new UniqueDef("unique.link2_benevolent_gold", "charm.lotus_seal", new Txt("慈愛の黄金律", "Golden Rule of Mercy"),
                new Txt("惜しみなく払った黄金が、味方の傷を温かく癒す。", "Gold spent without regret warmly mends the wounds of allies."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link2_benevolent_gold_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link2_benevolent_gold_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_benevolent_gold_Link } },
            new UniqueDef("unique.link2_prismatic_crystal", "hands.radiant_wraps", new Txt("虹彩の結晶体", "Iridescent Crystal Body"),
                new Txt("結晶の表面を光が巡り、四つの色が一つに重なる。", "Light circles the crystal's surface until four colors become one."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link2_prismatic_crystal_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_link2_prismatic_crystal_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "Gem_L_MetalCrystal" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link2_prismatic_crystal_Link } },
            new UniqueDef("unique.link3_supernova_starlight", "weapon.star_harp", new Txt("終星の調べ", "Swansong of Stars"),
                new Txt("最後の星明かりが、超新星の閃光を何度でも歌わせる。", "The last starlight sings the supernova's flash again and again."),
                Power.Overload, EquipmentItemsBalanceValues.Unique_unique_link3_supernova_starlight_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_link3_supernova_starlight_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_Q_SuperNova", "Gem_U_LastStarlight" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link3_supernova_starlight_Link } },
            new UniqueDef("unique.link3_crimson_cannon", "weapon.ember_katar", new Txt("紅蓮の砲声", "Crimson Report"),
                new Txt("跳ね返る炎の尾が、砲声のたびに敵陣を焼き払う。", "Rebounding tails of flame scorch the enemy line with every shot."),
                Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_link3_crimson_cannon_Power0, Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_link3_crimson_cannon_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_Q_HandCannon", "Gem_L_Embertail" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link3_crimson_cannon_Link } },
            new UniqueDef("unique.link3_absolute_sanctuary", "armor.guardian_plate", new Txt("絶対聖域", "Absolute Sanctuary"),
                new Txt("聖域に張られた盾は、持ち主の命を一つに束ねて守る。", "The shield woven into the sanctuary binds its bearer's life into one."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link3_absolute_sanctuary_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_link3_absolute_sanctuary_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_R_SanctuaryOfEl", "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link3_absolute_sanctuary_Link } },
            new UniqueDef("unique.link3_dawnless_slash", "hands.duelist_gloves", new Txt("無明の一閃", "Flash in the Dark"),
                new Txt("不安に追い立てられるほど、一歩一殺の刃は冴えていく。", "The more dread closes in, the sharper each one-step kill becomes."),
                Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_link3_dawnless_slash_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_link3_dawnless_slash_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_D_TheKillingFlow", "Gem_L_Paranoia" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link3_dawnless_slash_Link } },
            new UniqueDef("unique.link3_lifeline_circle", "head.healer_band", new Txt("命脈の環", "Ring of Lifeblood"),
                new Txt("氷河の鼓動が命の輪を巡り、仲間の傷を静かに癒やす。", "A glacier's heartbeat circles the pack and quietly closes their wounds."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_link3_lifeline_circle_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link3_lifeline_circle_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_D_CircleOfLife", "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link3_lifeline_circle_Link } },
            new UniqueDef("unique.link3_glacier_verdict", "weapon.glacier_spear", new Txt("氷嶺の審判", "Verdict of the Ice Ridge"),
                new Txt("巨大な氷塊が落ちるたび、純白の裁きが再び下される。", "Each time the great ice chunk falls, a pure white verdict is passed again."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_link3_glacier_verdict_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link3_glacier_verdict_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_Q_BigBorealChunk", "Gem_L_PureWhite" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link3_glacier_verdict_Link } },
            new UniqueDef("unique.link3_frostfire_feast", "charm.ember_locket", new Txt("氷炎の饗宴", "Frostfire Banquet"),
                new Txt("吹雪と火球が交わる夜、四つの元素が同時に牙を剥く。", "On the night blizzard and fireball meet, every element bares its fangs."),
                Power.Convergence, EquipmentItemsBalanceValues.Unique_unique_link3_frostfire_feast_Power0, Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_link3_frostfire_feast_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard", "St_L_PyranasFireball", "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link3_frostfire_feast_Link } },
            new UniqueDef("unique.link3_coin_throne", "charm.guardian_seal", new Txt("金貨の王座", "Throne of Coin"),
                new Txt("貯めた財貨が山となり、持ち主を揺るがぬ玉座に据える。", "A heap of hoarded gold becomes a throne that no blow can shake."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_link3_coin_throne_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link3_coin_throne_Power1) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "St_L_ButchersStrike", "Gem_L_HeartOfGold" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link3_coin_throne_Link } },
            new UniqueDef("unique.link3_oblivion_blackhole", "head.void_helm", new Txt("忘却の黒洞", "Void of Oblivion"),
                new Txt("彼女の世界が咆哮を呑み、命を削って闇を爆ぜさせる。", "Her world swallows the roar and burns life itself into a burst of dark."),
                Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_link3_oblivion_blackhole_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_link3_oblivion_blackhole_Power1) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "St_U_ShoutOfOblivion", "Gem_L_SuppressedArcanum" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_link3_oblivion_blackhole_Link } },
            new UniqueDef("unique.link3_heavenly_round", "hands.radiant_wraps", new Txt("天照の輪舞", "Rondo of Radiance"),
                new Txt("解き放たれた光が輪になって踊り、仲間の傷を撫でていく。", "Liberated light dances in a ring and brushes every wound away."),
                Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_link3_heavenly_round_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_link3_heavenly_round_Power1) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion", "St_U_BeamOfBalance", "Gem_L_Liberty" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link3_heavenly_round_Link } },
            new UniqueDef("unique.link3_hundred_arrows", "weapon.longspike_bow", new Txt("百矢の天蓋", "Canopy of Arrows"),
                new Txt("霊弾が矢の雨に混ざり、完璧な弧を描いて降り注ぐ。", "Spectral bullets mix with the arrow rain and fall in perfect arcs."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_link3_hundred_arrows_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_link3_hundred_arrows_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Multishot", "St_L_SpectreBullet", "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_link3_hundred_arrows_Link } },
            new UniqueDef("unique.link3_inferno_tempest", "armor.ember_cuirass", new Txt("炎獄の嵐", "Hellfire Tempest"),
                new Txt("永遠の炎が太陽の眼に見つめられ、火球が嵐となって荒れる。", "Watched by the sun's eye, the eternal flame swells into a storm of fireballs."),
                Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_link3_inferno_tempest_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_link3_inferno_tempest_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "Gem_U_EternalFlame", "Gem_L_SolarEye" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link3_inferno_tempest_Link } },
            new UniqueDef("unique.link3_invincible_cocoon", "feet.knight_sabatons", new Txt("無敵の繭", "Cocoon of Invincibility"),
                new Txt("地中に潜れば、魂の牢獄が命の殻となって傷を拒む。", "Burrowed deep, the soul prison becomes a shell that refuses every wound."),
                Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_link3_invincible_cocoon_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_link3_invincible_cocoon_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Burrow", "Gem_U_SoulPrison", "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link3_invincible_cocoon_Link } },
            new UniqueDef("unique.link3_butcher_banquet", "armor.bark_mail", new Txt("饗宴の屠り手", "Banquet Butcher"),
                new Txt("屠った肉は料理となり、氷河の力で傷を癒やす。", "Every butchered cut becomes a dish, and the glacier's chill mends his wounds."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_link3_butcher_banquet_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_link3_butcher_banquet_Power1) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike", "Gem_L_Culinary", "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_link3_butcher_banquet_Link } },
            new UniqueDef("unique.link3_frenzied_claws", "hands.void_claws", new Txt("狂乱の爪痕", "Scars of Frenzy"),
                new Txt("ヒステリーが解き放たれ、純白の爪が敵を引き裂き続ける。", "Hysteria unleashed, pure white claws keep tearing at the foe."),
                Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_link3_frenzied_claws_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_link3_frenzied_claws_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria", "Gem_L_Liberty", "Gem_L_PureWhite" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_link3_frenzied_claws_Link } },
            // v1.27.1：癒やし・シールド・召喚獣・献身を支える固有品。
            new UniqueDef("unique.support_moonlit_lullaby", "weapon.bone_flute", new Txt("月獣の子守唄", "Moonbeast Lullaby"),
                new Txt("傷ついた狼が眠るまで、笛の音は月の下を巡る。", "The flute circles beneath the moon until the wounded wolf sleeps."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_support_moonlit_lullaby_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_support_moonlit_lullaby_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_MoonlightPact" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_support_moonlit_lullaby_Link } },
            new UniqueDef("unique.support_golden_recompense", "weapon.calming_staff", new Txt("命返しの金杖", "Golden Staff of Recompense"),
                new Txt("捧げた命の行く先に、誰かの明日が芽吹く。", "Where an offered life flows, another's tomorrow takes root."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_support_golden_recompense_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_support_golden_recompense_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_GoldenBurst" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_support_golden_recompense_Link } },
            new UniqueDef("unique.support_winter_haven", "armor.frost_coat", new Txt("冬の避難所", "Winter Haven"),
                new Txt("冷たい衣の内側だけは、嵐の中でも静かだった。", "Within the cold mantle, even the storm was still."),
                Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_support_winter_haven_Power0, Power.StillWater, EquipmentItemsBalanceValues.Unique_unique_support_winter_haven_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_EmbracingTheChill" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_support_winter_haven_Link } },
            new UniqueDef("unique.support_serpent_cradle", "armor.prayer_shawl", new Txt("蛇環の揺り籠", "Serpent-Ring Cradle"),
                new Txt("古い鱗を脱ぐように、仲間の傷もほどけていく。", "As old scales fall away, the wounds of companions unravel."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_support_serpent_cradle_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_support_serpent_cradle_Power1) { Link = new LinkDef { Requires = new[] { "St_R_SerpentineBlessing" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_support_serpent_cradle_Link } },
            new UniqueDef("unique.support_mending_plume", "head.healer_band", new Txt("傷縫いの金羽根", "Golden Mending Plume"),
                new Txt("爪が奪った命を、羽根は一針ずつ縫い戻す。", "What the claw takes, the feather stitches back one thread at a time."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_support_mending_plume_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_support_mending_plume_Power1) { Link = new LinkDef { Requires = new[] { "St_D_DisintegratingClaw" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_support_mending_plume_Link } },
            new UniqueDef("unique.support_pack_vigil", "head.wolf_pelt", new Txt("群れ守りの夜番", "Packkeeper's Vigil"),
                new Txt("一匹も置いていかぬよう、夜通し耳を澄ませている。", "All night the keeper listens, so not one of the pack is left behind."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_support_pack_vigil_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_support_pack_vigil_Power1) { Link = new LinkDef { Requires = new[] { "St_D_CircleOfLife" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_support_pack_vigil_Link } },
            new UniqueDef("unique.support_measured_offering", "hands.healer_hands", new Txt("惜しみの献杯", "Measured Offering"),
                new Txt("命を注ぐ手を止めるのは、次の誰かも救うため。", "The hand pauses its offering to save the next soul as well."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_support_measured_offering_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_support_measured_offering_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Reduction" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_support_measured_offering_Link } },
            new UniqueDef("unique.support_sheltering_tide", "hands.frost_mitts", new Txt("庇い手の氷袖", "Sheltering Ice Sleeves"),
                new Txt("振り払う腕の後ろには、仲間のための凪が残る。", "Behind the sweeping arm, calm water remains for companions."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_support_sheltering_tide_Power0, Power.StillWater, EquipmentItemsBalanceValues.Unique_unique_support_sheltering_tide_Power1) { Link = new LinkDef { Requires = new[] { "St_R_BackOff" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_support_sheltering_tide_Link } },
            new UniqueDef("unique.support_waltz_shelter", "feet.root_sandals", new Txt("木漏れ日の舞靴", "Dancing Shoes of Dappled Light"),
                new Txt("踏み出すたびに木陰が揺れ、小さな背中を包み込む。", "Each step stirs the shade and shelters the little backs beneath it."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_support_waltz_shelter_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_support_waltz_shelter_Power1) { Link = new LinkDef { Requires = new[] { "St_M_DreamyWaltz" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_support_waltz_shelter_Link } },
            new UniqueDef("unique.support_brink_return", "feet.pilgrim_boots", new Txt("生還の足跡", "Footprints of Return"),
                new Txt("危うい一歩の先にも、戻る道だけは残しておく。", "Beyond the perilous step, always leave a path home."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_support_brink_return_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_support_brink_return_Power1) { Link = new LinkDef { Requires = new[] { "St_R_DangerousTheory" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_support_brink_return_Link } },
            new UniqueDef("unique.support_sylvan_kinship", "charm.oak_amulet", new Txt("若葉の呼び鈴", "Bell of Young Leaves"),
                new Txt("ひとたび鳴らせば、森の子らが家族の声を思い出す。", "One ring reminds the forest's young of the voice of home."),
                Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_support_sylvan_kinship_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_support_sylvan_kinship_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_SylvanCall" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_support_sylvan_kinship_Link } },
            new UniqueDef("unique.support_life_confluence", "charm.sun_brooch", new Txt("命の合流点", "Confluence of Life"),
                new Txt("幾筋もの光が交わり、途切れかけた鼓動をつなぐ。", "Strands of light meet and join heartbeats that were fading apart."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_support_life_confluence_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_support_life_confluence_Power1) { Link = new LinkDef { Requires = new[] { "St_R_ChainReaction" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_support_life_confluence_Link } },
            new UniqueDef("set.tide.weapon", "weapon.chain_sword", new Txt("潮鳴りの剣", "Tidecaller's Blade"), "set.tide"),
            new UniqueDef("set.tide.armor", "armor.flowing_cloak", new Txt("潮鳴りの外套", "Tidecaller's Cloak"), "set.tide"),
            new UniqueDef("set.tide.charm", "charm.tailwind_ring", new Txt("潮鳴りの指輪", "Tidecaller's Ring"), "set.tide"),
            new UniqueDef("set.tide.head", "head.tidal_circlet", new Txt("潮鳴りの額冠", "Tidecaller's Circlet"), "set.tide"),
            new UniqueDef("set.tide.hands", "hands.tide_gloves", new Txt("潮鳴りの手袋", "Tidecaller's Gloves"), "set.tide"),
            new UniqueDef("set.tide.feet", "feet.tide_sandals", new Txt("潮鳴りの渚履", "Tidecaller's Sandals"), "set.tide"),
            new UniqueDef("set.lamp.weapon", "weapon.calming_staff", new Txt("灯守の杖", "Lampkeeper's Staff"), "set.lamp"),
            new UniqueDef("set.lamp.armor", "armor.lampkeeper_mantle", new Txt("灯守の誓衣", "Lampkeeper's Vow"), "set.lamp"),
            new UniqueDef("set.lamp.charm", "charm.resonance_amulet", new Txt("灯守の護符", "Lampkeeper's Charm"), "set.lamp"),
            new UniqueDef("set.lamp.head", "head.lantern_hat", new Txt("灯守の笠", "Lampkeeper's Hat"), "set.lamp"),
            new UniqueDef("set.lamp.hands", "hands.lantern_fingerless", new Txt("灯守の指抜き", "Lampkeeper's Fingerless Gloves"), "set.lamp"),
            new UniqueDef("set.lamp.feet", "feet.dawn_steps", new Txt("灯守の長靴", "Lampkeeper's Boots"), "set.lamp"),
            new UniqueDef("set.cinder.weapon", "weapon.blaze_greatsword", new Txt("残火の大剣", "Cinderbrand"), "set.cinder"),
            new UniqueDef("set.cinder.armor", "armor.thorn_mail", new Txt("残火の鱗鎧", "Cinderscale Mail"), "set.cinder"),
            new UniqueDef("set.cinder.charm", "charm.old_clock", new Txt("残火の懐中時計", "Cinder Pocketwatch"), "set.cinder"),
            new UniqueDef("set.cinder.head", "head.ember_crown", new Txt("残火の冠", "Cinder Crown"), "set.cinder"),
            new UniqueDef("set.cinder.hands", "hands.ember_gauntlets", new Txt("残火の手甲", "Cinder Gauntlets"), "set.cinder"),
            new UniqueDef("set.cinder.feet", "feet.ember_treads", new Txt("残火の足甲", "Cinder Treads"), "set.cinder"),
            new UniqueDef("set.dusk.weapon", "weapon.twin_fang", new Txt("黄昏の双牙", "Duskfang"), "set.dusk"),
            new UniqueDef("set.dusk.armor", "armor.counter_gauntlets", new Txt("黄昏の籠手", "Dusk Gauntlets"), "set.dusk"),
            new UniqueDef("set.dusk.charm", "charm.hunters_seal", new Txt("黄昏の印章", "Dusk Seal"), "set.dusk"),
            new UniqueDef("set.dusk.head", "head.raven_mask", new Txt("黄昏の鴉面", "Dusk Raven Mask"), "set.dusk"),
            new UniqueDef("set.dusk.hands", "hands.shadow_gloves", new Txt("黄昏の影手袋", "Dusk Shadowgloves"), "set.dusk"),
            new UniqueDef("set.dusk.feet", "feet.duskstep_boots", new Txt("黄昏の足甲", "Duskstep Greaves"), "set.dusk"),
            new UniqueDef("set.winter.weapon", "weapon.frost_spear", new Txt("冬枯れの槍", "Winterbound Spear"), "set.winter"),
            new UniqueDef("set.winter.armor", "armor.frost_coat", new Txt("冬枯れの上衣", "Winterbound Coat"), "set.winter"),
            new UniqueDef("set.winter.charm", "charm.moon_bell", new Txt("冬枯れの鈴", "Winterbound Bell"), "set.winter"),
            new UniqueDef("set.winter.head", "head.icewall_helm", new Txt("冬枯れの氷壁兜", "Winterbound Icewall Helm"), "set.winter"),
            new UniqueDef("set.winter.hands", "hands.snowmelt_mitts", new Txt("冬枯れの雪解け手袋", "Winterbound Snowmelt Mitts"), "set.winter"),
            new UniqueDef("set.winter.feet", "feet.winterhide_boots", new Txt("冬枯れの毛皮靴", "Winterbound Hideboots"), "set.winter"),
            new UniqueDef("set.starsong.weapon", "weapon.lantern_rod", new Txt("星詠みの杖", "Starsinger's Rod"), "set.starsong"),
            new UniqueDef("set.starsong.armor", "armor.star_cloak", new Txt("星詠みの外套", "Starsinger's Cloak"), "set.starsong"),
            new UniqueDef("set.starsong.charm", "charm.old_clock", new Txt("星詠みの時計", "Starsinger's Clock"), "set.starsong"),
            new UniqueDef("set.starsong.head", "head.comet_diadem", new Txt("星詠みの冠", "Starsinger's Diadem"), "set.starsong"),
            new UniqueDef("set.starsong.hands", "hands.star_rings", new Txt("星詠みの指環", "Starsinger's Rings"), "set.starsong"),
            new UniqueDef("set.starsong.feet", "feet.star_slippers", new Txt("星詠みの靴", "Starsinger's Slippers"), "set.starsong"),
            new UniqueDef("set.hunt.weapon", "weapon.hunting_bow", new Txt("狩猟団の弓", "Huntmaster's Bow"), "set.hunt"),
            new UniqueDef("set.hunt.armor", "armor.hunter_leather", new Txt("狩猟団の革鎧", "Huntmaster's Leathers"), "set.hunt"),
            new UniqueDef("set.hunt.charm", "charm.fang_necklace", new Txt("狩猟団の牙", "Huntmaster's Fang"), "set.hunt"),
            new UniqueDef("set.hunt.head", "head.hunter_hood", new Txt("狩猟団の頭巾", "Huntmaster's Hood"), "set.hunt"),
            new UniqueDef("set.hunt.hands", "hands.hunting_sinew", new Txt("狩猟団の筋帯", "Huntmaster's Sinew Wraps"), "set.hunt"),
            new UniqueDef("set.hunt.feet", "feet.hunter_boots", new Txt("狩猟団の長靴", "Huntmaster's Boots"), "set.hunt"),
            new UniqueDef("set.bastion.weapon", "weapon.tower_lance", new Txt("不落城の槍", "Bastion Lance"), "set.bastion"),
            new UniqueDef("set.bastion.armor", "armor.bastion_shell", new Txt("不落城の甲羅", "Bastion Shell"), "set.bastion"),
            new UniqueDef("set.bastion.charm", "charm.guardian_seal", new Txt("不落城の封印", "Bastion Seal"), "set.bastion"),
            new UniqueDef("set.bastion.head", "head.sentry_visor", new Txt("不落城の面頬", "Bastion Visor"), "set.bastion"),
            new UniqueDef("set.bastion.hands", "hands.ironvein_gauntlets", new Txt("不落城の籠手", "Bastion Gauntlets"), "set.bastion"),
            new UniqueDef("set.bastion.feet", "feet.bastion_sabatons", new Txt("不落城の鉄鞋", "Bastion Sabatons"), "set.bastion"),
            new UniqueDef("set.wildfire.weapon", "weapon.war_axe", new Txt("燎原の斧", "Wildfire Cleaver"), "set.wildfire"),
            new UniqueDef("set.wildfire.armor", "armor.ember_plate", new Txt("燎原の鎧", "Wildfire Plate"), "set.wildfire"),
            new UniqueDef("set.wildfire.charm", "charm.war_drum", new Txt("燎原の太鼓", "Wildfire Drum"), "set.wildfire"),
            new UniqueDef("set.wildfire.head", "head.magma_band", new Txt("燎原の熔岩鉢巻", "Wildfire Magma Band"), "set.wildfire"),
            new UniqueDef("set.wildfire.hands", "hands.blazeknit_gloves", new Txt("燎原の火織り手袋", "Wildfire Blazeknit Gloves"), "set.wildfire"),
            new UniqueDef("set.wildfire.feet", "feet.emberdash_boots", new Txt("燎原の火駆け靴", "Wildfire Embertreads"), "set.wildfire"),
            new UniqueDef("set.grove.weapon", "weapon.oath_mace", new Txt("古森の戦棍", "Grove Mace"), "set.grove"),
            new UniqueDef("set.grove.armor", "armor.root_mail", new Txt("古森の帷子", "Grove Mail"), "set.grove"),
            new UniqueDef("set.grove.charm", "charm.stone_heart", new Txt("古森の心臓", "Grove Heart"), "set.grove"),
            new UniqueDef("set.grove.head", "head.moss_crown", new Txt("古森の苔冠", "Grove Moss Crown"), "set.grove"),
            new UniqueDef("set.grove.hands", "hands.bark_knuckles", new Txt("古森の樹皮手", "Grove Barkgrips"), "set.grove"),
            new UniqueDef("set.grove.feet", "feet.deeproot_boots", new Txt("古森の深根靴", "Grove Deeprootboots"), "set.grove"),
            new UniqueDef("set.reverie.weapon", "weapon.star_harp", new Txt("夢想の竪琴", "Reverie Harp"), "set.reverie"),
            new UniqueDef("set.reverie.armor", "armor.prayer_shawl", new Txt("夢想の肩掛け", "Reverie Shawl"), "set.reverie"),
            new UniqueDef("set.reverie.charm", "charm.dream_lens", new Txt("夢想の水晶", "Reverie Lens"), "set.reverie"),
            new UniqueDef("set.reverie.head", "head.moonlace_hood", new Txt("夢想の月紗頭巾", "Reverie Moonlace Hood"), "set.reverie"),
            new UniqueDef("set.reverie.hands", "hands.chime_bracers", new Txt("夢想の鈴腕輪", "Reverie Chime Bracers"), "set.reverie"),
            new UniqueDef("set.reverie.feet", "feet.whisperweave_shoes", new Txt("夢想の囁き靴", "Reverie Whisperweave Shoes"), "set.reverie"),
            new UniqueDef("set.phantom.weapon", "weapon.dream_wand", new Txt("幻影の杖", "Phantom Wand"), "set.phantom"),
            new UniqueDef("set.phantom.armor", "armor.mist_robe", new Txt("幻影の法衣", "Phantom Robe"), "set.phantom"),
            new UniqueDef("set.phantom.charm", "charm.shadow_mask", new Txt("幻影の仮面", "Phantom Mask"), "set.phantom"),
            new UniqueDef("set.phantom.head", "head.starless_veil", new Txt("幻影の黒面紗", "Phantom Blackveil"), "set.phantom"),
            new UniqueDef("set.phantom.hands", "hands.void_claws", new Txt("幻影の虚爪", "Phantom Voidclaws"), "set.phantom"),
            new UniqueDef("set.phantom.feet", "feet.nightveil_slippers", new Txt("幻影の夜履", "Phantom Nightslippers"), "set.phantom"),
            // v1.22：新しい枠を使うセットの部位
            new UniqueDef("set.permafrost.head", "head.frost_helm", new Txt("凍土の兜", "Permafrost Helm"), "set.permafrost"),
            new UniqueDef("set.permafrost.hands", "hands.frost_mitts", new Txt("凍土の手甲", "Permafrost Gauntlets"), "set.permafrost"),
            new UniqueDef("set.permafrost.feet", "feet.frost_boots", new Txt("凍土の脚絆", "Permafrost Greaves"), "set.permafrost"),
            new UniqueDef("set.permafrost.weapon", "weapon.glacier_spear", new Txt("凍土の氷槍", "Permafrost Spear"), "set.permafrost"),
            new UniqueDef("set.permafrost.armor", "armor.frost_robe", new Txt("凍土の霜衣", "Permafrost Robe"), "set.permafrost"),
            new UniqueDef("set.permafrost.charm", "charm.frost_pendant", new Txt("凍土の霜飾り", "Permafrost Frostpendant"), "set.permafrost"),
            new UniqueDef("set.asura.head", "head.berserker_mask", new Txt("修羅道の面", "Carnage Mask"), "set.asura"),
            new UniqueDef("set.asura.hands", "hands.claw_gauntlets", new Txt("修羅道の爪", "Carnage Claws"), "set.asura"),
            new UniqueDef("set.asura.feet", "feet.spiked_boots", new Txt("修羅道の脛", "Carnage Spurs"), "set.asura"),
            new UniqueDef("set.asura.weapon", "weapon.severing_axe", new Txt("修羅道の大斧", "Carnage Greataxe"), "set.asura"),
            new UniqueDef("set.asura.armor", "armor.skirmisher_coat", new Txt("修羅道の胴着", "Carnage Vest"), "set.asura"),
            new UniqueDef("set.asura.charm", "charm.hunter_tooth", new Txt("修羅道の牙飾り", "Carnage Fangcharm"), "set.asura"),
            new UniqueDef("set.gale.head", "head.mist_veil", new Txt("風舞の面紗", "Galedancer Veil"), "set.gale"),
            new UniqueDef("set.gale.hands", "hands.quick_fingers", new Txt("風舞の指貫", "Galedancer Gloves"), "set.gale"),
            new UniqueDef("set.gale.feet", "feet.dancer_shoes", new Txt("風舞の靴", "Galedancer Shoes"), "set.gale"),
            new UniqueDef("set.gale.weapon", "weapon.storm_glaive", new Txt("風舞の薙刀", "Galedancer's Glaive"), "set.gale"),
            new UniqueDef("set.gale.armor", "armor.swiftstep_coat", new Txt("風舞の疾歩衣", "Galedancer's Swiftcoat"), "set.gale"),
            new UniqueDef("set.gale.charm", "charm.zephyr_ring", new Txt("風舞の指輪", "Galedancer's Ring"), "set.gale"),
            new UniqueDef("set.firmament.head", "head.star_diadem", new Txt("天穹の冠", "Firmament Diadem"), "set.firmament"),
            new UniqueDef("set.firmament.hands", "hands.star_rings", new Txt("天穹の指環", "Firmament Rings"), "set.firmament"),
            new UniqueDef("set.firmament.feet", "feet.star_steps", new Txt("天穹の沓", "Firmament Steps"), "set.firmament"),
            new UniqueDef("set.firmament.weapon", "weapon.astral_staff", new Txt("天穹の儀杖", "Firmament Ritual Staff"), "set.firmament"),
            new UniqueDef("set.firmament.armor", "armor.star_mantle", new Txt("天穹の星衣", "Firmament Starmantle"), "set.firmament"),
            new UniqueDef("set.firmament.charm", "charm.comet_pendant", new Txt("天穹の彗星垂飾", "Firmament Comet Pendant"), "set.firmament"),
            new UniqueDef("set.ambush.head", "head.eye_patch", new Txt("闇討ちの眼帯", "Nightstrike Eyepatch"), "set.ambush"),
            new UniqueDef("set.ambush.hands", "hands.duelist_gloves", new Txt("闇討ちの手袋", "Nightstrike Gloves"), "set.ambush"),
            new UniqueDef("set.ambush.feet", "feet.stalker_boots", new Txt("闇討ちの足袋", "Nightstrike Tabi"), "set.ambush"),
            new UniqueDef("set.ambush.weapon", "weapon.gloaming_dagger", new Txt("闇討ちの宵短剣", "Nightstrike Gloamdagger"), "set.ambush"),
            new UniqueDef("set.ambush.armor", "armor.duskweave_jacket", new Txt("闇討ちの黄昏上着", "Nightstrike Duskjacket"), "set.ambush"),
            new UniqueDef("set.ambush.charm", "charm.keeneye_charm", new Txt("闇討ちの鋭眼御守", "Nightstrike Keeneye Charm"), "set.ambush"),
            new UniqueDef("set.ashrunner.head", "head.ash_hood", new Txt("灰走りの頭巾", "Ashrunner Hood"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.hands", "hands.flame_grips", new Txt("灰走りの手甲", "Ashrunner Grips"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.feet", "feet.ash_boots", new Txt("灰走りの靴", "Ashrunner Boots"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.weapon", "weapon.ember_katar", new Txt("灰走りの短刃", "Ashrunner Katar"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.armor", "armor.ember_jacket", new Txt("灰走りの外衣", "Ashrunner Jacket"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.charm", "charm.hearthstone", new Txt("灰走りの炉石", "Ashrunner Hearthstone"), "set.ashrunner"),
            new UniqueDef("set.mercy.weapon", "weapon.pilgrim_staff", new Txt("施療の錫杖", "Healer's Staff"), "set.mercy"),
            new UniqueDef("set.mercy.hands", "hands.healer_hands", new Txt("施療の手袋", "Healer's Gloves"), "set.mercy"),
            new UniqueDef("set.mercy.charm", "charm.sun_brooch", new Txt("施療の飾り", "Healer's Brooch"), "set.mercy"),
            new UniqueDef("set.mercy.head", "head.kindly_circlet", new Txt("施療の額冠", "Healer's Circlet"), "set.mercy"),
            new UniqueDef("set.mercy.armor", "armor.healing_sash", new Txt("施療の飾り帯", "Healer's Sash"), "set.mercy"),
            new UniqueDef("set.mercy.feet", "feet.mender_shoes", new Txt("施療の靴", "Healer's Shoes"), "set.mercy"),
            new UniqueDef("set.ironknight.armor", "armor.scale_coat", new Txt("鉄騎の竜鎧", "Ironknight Scale Mail"), "set.ironknight"),
            new UniqueDef("set.ironknight.head", "head.knight_helm", new Txt("鉄騎の大兜", "Ironknight Greathelm"), "set.ironknight"),
            new UniqueDef("set.ironknight.feet", "feet.knight_sabatons", new Txt("鉄騎の鉄脚", "Ironknight Sabatons"), "set.ironknight"),
            new UniqueDef("set.ironknight.weapon", "weapon.gatehouse_maul", new Txt("鉄騎の城門槌", "Ironknight Gatemaul"), "set.ironknight"),
            new UniqueDef("set.ironknight.hands", "hands.ironvein_gauntlets", new Txt("鉄騎の鉄脈籠手", "Ironknight Ironvein Gauntlets"), "set.ironknight"),
            new UniqueDef("set.ironknight.charm", "charm.iron_seal", new Txt("鉄騎の印章", "Ironknight Seal"), "set.ironknight"),
            new UniqueDef("set.eclipse.weapon", "weapon.moon_sickle", new Txt("月蝕の鎌", "Eclipse Sickle"), "set.eclipse"),
            new UniqueDef("set.eclipse.head", "head.void_helm", new Txt("月蝕の兜", "Eclipse Helm"), "set.eclipse"),
            new UniqueDef("set.eclipse.charm", "charm.shadow_ring", new Txt("月蝕の指輪", "Eclipse Ring"), "set.eclipse"),
            new UniqueDef("set.eclipse.armor", "armor.shadow_cloak", new Txt("月蝕の影外套", "Eclipse Shadowcloak"), "set.eclipse"),
            new UniqueDef("set.eclipse.hands", "hands.void_claws", new Txt("月蝕の虚爪", "Eclipse Voidclaws"), "set.eclipse"),
            new UniqueDef("set.eclipse.feet", "feet.shadow_slippers", new Txt("月蝕の影履", "Eclipse Shadowslippers"), "set.eclipse"),
            new UniqueDef("set.thunderclap.weapon", "weapon.thunder_hammer", new Txt("迅雷の戦鎚", "Thunderclap Hammer"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.armor", "armor.monk_garb", new Txt("迅雷の道着", "Thunderclap Gi"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.feet", "feet.wolf_boots", new Txt("迅雷の長靴", "Thunderclap Boots"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.head", "head.thunderveil_hood", new Txt("迅雷の頭巾", "Thunderclap Hood"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.hands", "hands.storm_knuckles", new Txt("迅雷の嵐拳", "Thunderclap Knuckles"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.charm", "charm.stormcloud_locket", new Txt("迅雷の雷雲飾り", "Thunderclap Cloudlocket"), "set.thunderclap"),
            new UniqueDef("set.myriad.armor", "armor.resonant_robe", new Txt("万象の法衣", "Myriad Robe"), "set.myriad"),
            new UniqueDef("set.myriad.hands", "hands.alchemist_gloves", new Txt("万象の手袋", "Myriad Gloves"), "set.myriad"),
            new UniqueDef("set.myriad.charm", "charm.clockwork_charm", new Txt("万象の飾り", "Myriad Trinket"), "set.myriad"),
            new UniqueDef("set.myriad.weapon", "weapon.chanting_wand", new Txt("万象の詠唱杖", "Myriad Chanting Wand"), "set.myriad"),
            new UniqueDef("set.myriad.head", "head.sage_hat", new Txt("万象の賢者帽", "Myriad Sage Hat"), "set.myriad"),
            new UniqueDef("set.myriad.feet", "feet.cometstride_shoes", new Txt("万象の彗星靴", "Myriad Cometshoes"), "set.myriad"),
            new UniqueDef("set.daybreak.weapon", "weapon.dawn_scepter", new Txt("払暁の笏", "Daybreak Scepter"), "set.daybreak"),
            new UniqueDef("set.daybreak.head", "head.radiant_halo", new Txt("払暁の光輪", "Daybreak Halo"), "set.daybreak"),
            new UniqueDef("set.daybreak.feet", "feet.dawn_steps", new Txt("払暁の長靴", "Daybreak Treads"), "set.daybreak"),
            new UniqueDef("set.daybreak.armor", "armor.sun_plate", new Txt("払暁の胸甲", "Daybreak Breastplate"), "set.daybreak"),
            new UniqueDef("set.daybreak.hands", "hands.sunfire_grips", new Txt("払暁の陽炎手", "Daybreak Sunfire Grips"), "set.daybreak"),
            new UniqueDef("set.daybreak.charm", "charm.halo_charm", new Txt("払暁の光環御守", "Daybreak Halo Charm"), "set.daybreak"),
            // v1.29：固有品の第1段52個。この後に第2段を入力済み。P37の16個だけ保留（docs/specs/v1.29-uniques-deferred.md）。
            new UniqueDef("unique.w_palmcannon", "weapon.rockbreaker", new Txt("砕岩の砲槌", "Boulderburst Hammer"),
                new Txt("撃ち抜いた後に、熱い岩だけが残る。", "Only hot stone remains where it struck."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_w_palmcannon_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_w_palmcannon_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_HandCannon" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_palmcannon_Link } },
            new UniqueDef("unique.w_gnaw_cane", "weapon.moonlit_cane", new Txt("蝕み杖", "Gnawing Cane"),
                new Txt("音もなく、じわじわと夜が欠けてゆく。", "Quietly, the night is eaten away."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_w_gnaw_cane_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_gnaw_cane_Power1) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_gnaw_cane_Link } },
            new UniqueDef("unique.w_fireball_rod", "weapon.firefly_staff", new Txt("火球の化身杖", "Fireball Avatar Rod"),
                new Txt("杖を振るたび、小さな恒星が生まれ直す。", "With each swing, a tiny star is born again."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_w_fireball_rod_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_w_fireball_rod_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_fireball_rod_Link } },
            new UniqueDef("unique.w_steam_katar", "weapon.ember_katar", new Txt("湯気の短刃", "Steam Katar"),
                new Txt("熱と冷えの境目で、世界は白く煙る。", "At the edge of heat and cold, the world turns white."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_w_steam_katar_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_w_steam_katar_Power1),
            new UniqueDef("unique.w_boiling_halberd", "weapon.iron_halberd", new Txt("沸き立つ鉄戟", "Boiling Halberd"),
                new Txt("冷えた鉄を灼けば、叫びのような蒸気が上がる。", "Heat cold iron, and steam rises like a cry."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_w_boiling_halberd_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_w_boiling_halberd_Power1),
            new UniqueDef("unique.w_waning_scythe", "weapon.dusk_scythe", new Txt("欠け月の大鎌", "Waning Greatscythe"),
                new Txt("光を刈れば、その分だけ闇が太る。", "Reap the light, and the dark grows fat."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_w_waning_scythe_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_w_waning_scythe_Power1),
            new UniqueDef("unique.w_dimmed_cudgel", "weapon.lighthouse_cudgel", new Txt("翳る灯台の棍", "Dimmed Lighthouse Cudgel"),
                new Txt("灯りが傾くとき、足元の影も牙を剥く。", "When the lamp tilts, the shadows at your feet bare their teeth."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_w_dimmed_cudgel_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_w_dimmed_cudgel_Power1),
            new UniqueDef("unique.w_embercoal_sword", "weapon.blaze_greatsword", new Txt("熾火残りの大剣", "Embercoal Greatsword"),
                new Txt("燃え尽きた後の黒い芯が、いちばん熱い。", "The black core left after the burn is the hottest part."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_w_embercoal_sword_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_embercoal_sword_Power1),
            new UniqueDef("unique.w_ashfall_blades", "weapon.lightning_pair", new Txt("灰降りの双刃", "Ashfall Twinblades"),
                new Txt("刃が通った跡に、熱い灰だけが降る。", "Only warm ash falls where the blades have passed."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_w_ashfall_blades_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_w_ashfall_blades_Power1),
            new UniqueDef("unique.w_icicrystal_pike", "weapon.glacier_spear", new Txt("氷晶の穂先", "Icicrystal Pike"),
                new Txt("光を閉じ込めた氷は、護りの形に砕ける。", "Ice that traps light shatters into a ward."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_w_icicrystal_pike_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_w_icicrystal_pike_Power1),
            new UniqueDef("unique.w_dawnfrost_scepter", "weapon.dawn_scepter", new Txt("曙霜の笏", "Dawnfrost Scepter"),
                new Txt("朝日が霜に触れる一瞬だけ、世界は宝石で飾られる。", "For the moment sun meets frost, the world is set with gems."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_w_dawnfrost_scepter_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_w_dawnfrost_scepter_Power1),
            new UniqueDef("unique.w_everwick_katar", "weapon.ember_katar", new Txt("永遠の芯の短刃", "Everwick Katar"),
                new Txt("消えない炎の呪いを、刃にそっと塗っておく。", "A curse of unending flame, brushed gently onto the blade."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_w_everwick_katar_Power0, Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_w_everwick_katar_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_EternalFlame", "St_Q_IncendiaryRounds" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_everwick_katar_Link } },
            new UniqueDef("unique.w_glassfrost_staff", "weapon.conch_scepter", new Txt("霜硝子の杖", "Frostglass Staff"),
                new Txt("冷えた光は、ガラスのように脆く鋭い。", "Chilled light is brittle and keen like glass."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_w_glassfrost_staff_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_w_glassfrost_staff_Power1),
            new UniqueDef("unique.w_umbral_ember_bow", "weapon.starsinger_bow", new Txt("熾火射ちの弓", "Embershot Bow"),
                new Txt("矢の影が焦げて、足跡に残る。", "The arrow's shadow scorches and stays on the trail."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_w_umbral_ember_bow_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_w_umbral_ember_bow_Power1),
            new UniqueDef("unique.hd_steam_hood", "head.mist_veil", new Txt("霧湯のヴェール", "Mistsimmer Veil"),
                new Txt("熱い息が冷たい布に触れて、白く散る。", "Hot breath meets cold cloth and scatters white."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_hd_steam_hood_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_hd_steam_hood_Power1),
            new UniqueDef("unique.hd_eclipse_hood", "head.eclipse_mask", new Txt("欠け日の仮面", "Chipped-Sun Mask"),
                new Txt("仮面の縁から、光が一口ずつ欠けてゆく。", "Light is bitten away, mouthful by mouthful, from the mask's rim."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_hd_eclipse_hood_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_eclipse_hood_Power1),
            new UniqueDef("unique.hd_cinder_hood", "head.ash_hood", new Txt("熾火被りの頭巾", "Embercloak Hood"),
                new Txt("被った灰が、いつまでも熱い。", "The ash worn on the head stays hot."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_hd_cinder_hood_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_hd_cinder_hood_Power1),
            new UniqueDef("unique.hd_icicrystal_crown", "head.tidal_circlet", new Txt("氷晶の潮冠", "Icicrystal Tide Crown"),
                new Txt("氷に閉じ込めた光が、潮の満ちる音を立てる。", "Light sealed in ice hums like the rising tide."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_hd_icicrystal_crown_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_hd_icicrystal_crown_Power1),
            new UniqueDef("unique.a_steam_robe", "armor.mist_robe", new Txt("湯霧の法衣", "Steammist Robe"),
                new Txt("冷たい霧と熱い息が、衣の裏で出会う。", "Cold mist and hot breath meet inside the robe."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_a_steam_robe_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_steam_robe_Power1),
            new UniqueDef("unique.a_eclipse_cloak", "armor.shadow_cloak", new Txt("蝕の影外套", "Eclipse Shadecloak"),
                new Txt("光に縫われた影は、いつか光を食む。", "A shadow stitched with light will someday devour it."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_a_eclipse_cloak_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_a_eclipse_cloak_Power1),
            new UniqueDef("unique.a_cinder_coat", "armor.ember_jacket", new Txt("燃え殻の外衣", "Cinderhide Coat"),
                new Txt("燃え尽きた衣は、熱い灰を残す。", "A burnt-out coat leaves hot ash."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_a_cinder_coat_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_a_cinder_coat_Power1),
            new UniqueDef("unique.a_icicrystal_robe", "armor.seafoam_gown", new Txt("氷晶の海泡衣", "Icicrystal Seafoam Gown"),
                new Txt("泡の中に凍った光が閉じ込められている。", "Frozen light lies trapped inside the foam."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_a_icicrystal_robe_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_icicrystal_robe_Power1),
            new UniqueDef("unique.a_icicrystal_cuirass", "armor.glacier_harness", new Txt("氷晶の胴当て", "Icicrystal Harness"),
                new Txt("凍った光が胴を巡り、護りの粒になる。", "Frozen light circles the torso and becomes grains of ward."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_a_icicrystal_cuirass_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_icicrystal_cuirass_Power1),
            new UniqueDef("unique.a_cinder_robe", "armor.nightloom_robe", new Txt("灰夜の織衣", "Ashnight Loomrobe"),
                new Txt("夜に織り込んだ火の粉が、少しずつ灰になる。", "Sparks woven into the night slowly turn to ash."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_a_cinder_robe_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_a_cinder_robe_Power1),
            new UniqueDef("unique.a_steam_harness", "armor.volcanic_coat", new Txt("蒸し焼き山外套", "Steambaked Volcanic Coat"),
                new Txt("冷えた山肌に、熱い息が白く立つ。", "Hot breath rises white from the cold mountainside."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_a_steam_harness_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_a_steam_harness_Power1),
            new UniqueDef("unique.h_baptism_sunfists", "hands.sunfire_grips", new Txt("洗礼の陽拳", "Baptism Sunfists"),
                new Txt("浴びた者は、挑まずにはいられない。", "Those bathed in it cannot help but challenge."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_h_baptism_sunfists_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_h_baptism_sunfists_Power1) { Link = new LinkDef { Requires = new[] { "St_R_BaptismOfSun" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_baptism_sunfists_Link } },
            new UniqueDef("unique.h_hellfire_grips", "hands.flame_grips", new Txt("業火語りの握り", "Hellfire Raconteur Grips"),
                new Txt("語られた炎は、聞いた者の足元で燃える。", "The flame once told burns beneath the listener."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_h_hellfire_grips_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_h_hellfire_grips_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_QR_InfernalTales" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_hellfire_grips_Link } },
            new UniqueDef("unique.h_chillward_mitts", "hands.snowmelt_mitts", new Txt("寒気守りの手袋", "Chillward Mitts"),
                new Txt("冷えた腕で抱いた者は、風邪を引かない。", "Those held by a frozen arm never catch a cold."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_h_chillward_mitts_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_h_chillward_mitts_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_EmbracingTheChill", "St_R_BackOff" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_h_chillward_mitts_Link } },
            new UniqueDef("unique.h_eclipse_tales", "hands.duskstitch_gloves", new Txt("蝕み縫いの手袋", "Eclipsestitch Gloves"),
                new Txt("無垢な光に、歪んだ影が縫い付けられている。", "A warped shadow stitched onto an innocent light."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_tales_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_tales_Power1) { Link = new LinkDef { Requires = new[] { "St_QR_Innocence", "St_QR_DistortedMind" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_eclipse_tales_Link } },
            new UniqueDef("unique.h_steam_mitts", "hands.tide_gloves", new Txt("霧湯の手袋", "Mistwater Mitts"),
                new Txt("冷たい水と熱い石が出会う場所。", "Where cold water meets a hot stone."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_h_steam_mitts_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_steam_mitts_Power1),
            new UniqueDef("unique.h_geyser_grips", "hands.flame_grips", new Txt("間欠泉の握り", "Geyser Grips"),
                new Txt("地面の下で、火と氷が順番を待っている。", "Beneath the ground, fire and ice wait their turns."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_h_geyser_grips_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_h_geyser_grips_Power1),
            new UniqueDef("unique.h_eclipse_wraps", "hands.lantern_fingerless", new Txt("日蝕の手巻き", "Solar Eclipse Wraps"),
                new Txt("灯りを覆えば、その下で影が育つ。", "Cover the lamp, and the shadow beneath it grows."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_wraps_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_wraps_Power1),
            new UniqueDef("unique.h_eclipse_fangs", "hands.viperfang_claws", new Txt("蝕み牙の爪", "Gnawfang Claws"),
                new Txt("会心は、光を一口かじる。", "A crit takes a bite out of the light."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_fangs_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_fangs_Power1),
            new UniqueDef("unique.h_cinder_veil", "hands.nightpalm_gloves", new Txt("灰かぶりの夜掌", "Ashveil Nightpalms"),
                new Txt("闇に落ちた火種が、静かに色を変える。", "A spark fallen into dark quietly changes color."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_h_cinder_veil_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_h_cinder_veil_Power1),
            new UniqueDef("unique.h_cinder_wildfire", "hands.ember_gauntlets", new Txt("燃え移りの籠手", "Spreadburn Gauntlets"),
                new Txt("影から影へ、火だけが渡ってゆく。", "From shadow to shadow, only the fire crosses."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_h_cinder_wildfire_Power0, Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_h_cinder_wildfire_Power1),
            new UniqueDef("unique.h_cinder_shatter", "hands.blazeknit_gloves", new Txt("煤けた爆ぜ手", "Soot-Burst Gloves"),
                new Txt("燻った灰ほど、派手に弾ける。", "The smokier the ash, the louder it bursts."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_h_cinder_shatter_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_h_cinder_shatter_Power1),
            new UniqueDef("unique.h_icicrystal_mitts", "hands.ice_bracers", new Txt("氷晶の腕輪", "Icicrystal Bracers"),
                new Txt("凍った光は、割れても眩しい。", "Frozen light still dazzles when broken."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_mitts_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_mitts_Power1),
            new UniqueDef("unique.h_icicrystal_light", "hands.frost_mitts", new Txt("白光の霜手", "Whitelight Frostmitts"),
                new Txt("光を冷やすと、護りの粒に変わる。", "Cool the light, and it turns to grains of ward."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_light_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_light_Power1),
            new UniqueDef("unique.h_icicrystal_star", "hands.radiant_wraps", new Txt("星守りの霜手", "Starward Frostwraps"),
                new Txt("星の加護は、凍った手の中でも消えない。", "A star's blessing does not fade even in frozen hands."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_star_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_star_Power1),
            new UniqueDef("unique.f_steam_trail_boots", "feet.froststride_shoes", new Txt("蒸気を曳く靴", "Steamtrail Shoes"),
                new Txt("熱い足跡が冷たい道を溶かし、白い尾になる。", "A hot footprint melts the cold road into a white tail."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_f_steam_trail_boots_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_f_steam_trail_boots_Power1),
            new UniqueDef("unique.f_steam_dancers", "feet.tidepool_sandals", new Txt("湯けむり草履", "Hotspring Sandals"),
                new Txt("冷えた潮溜まりに、熱い石が落ちる。", "A hot stone drops into a cold tidepool."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_f_steam_dancers_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_f_steam_dancers_Power1),
            new UniqueDef("unique.f_steam_greaves", "feet.ember_slippers", new Txt("熱冷ましの上履き", "Heat-Quencher Slippers"),
                new Txt("熱を冷ませば、湯気は敵の目をくらます。", "Quench the heat, and the steam blinds the foe."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_f_steam_greaves_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_steam_greaves_Power1),
            new UniqueDef("unique.f_eclipse_footfalls", "feet.nightveil_slippers", new Txt("蝕の足音", "Eclipse Footfalls"),
                new Txt("歩くたびに、足元の光が欠ける。", "With each step, the light at your feet is chipped away."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_f_eclipse_footfalls_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_f_eclipse_footfalls_Power1),
            new UniqueDef("unique.f_eclipse_dawnmist", "feet.dawnmist_shoes", new Txt("曙蝕の靴", "Dawneclipse Shoes"),
                new Txt("夜明けと日蝕が、同じ道を歩いている。", "Dawn and eclipse walk the same road."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_f_eclipse_dawnmist_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_f_eclipse_dawnmist_Power1),
            new UniqueDef("unique.f_cinderstep_boots", "feet.ash_boots", new Txt("燃え殻の踏み跡", "Cinderstep Boots"),
                new Txt("歩いた跡に、赤い灰が残る。", "Red ash remains on the path you walked."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_f_cinderstep_boots_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_f_cinderstep_boots_Power1),
            new UniqueDef("unique.f_ashrun_boots", "feet.emberdash_boots", new Txt("灰走りの駆け靴", "Ashrun Dashers"),
                new Txt("灰の上を走れば、足跡はすぐに燃える。", "Run over ash, and your tracks catch fire."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_f_ashrun_boots_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_f_ashrun_boots_Power1),
            new UniqueDef("unique.f_icecrystal_skates", "feet.frost_boots", new Txt("氷晶の滑り靴", "Icicrystal Skimmers"),
                new Txt("氷の上を滑るほど、光が足跡に宿る。", "The more you glide on ice, the more light your tracks hold."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_f_icecrystal_skates_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_f_icecrystal_skates_Power1),
            new UniqueDef("unique.f_crystalstep_shoes", "feet.dawnmist_shoes", new Txt("氷晶の足取り", "Icicrystal Steps"),
                new Txt("凍った光を踏むたびに、護りが砕けて広がる。", "Each step on frozen light spreads a shower of ward."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_f_crystalstep_shoes_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_f_crystalstep_shoes_Power1),
            new UniqueDef("unique.c_steam_charm", "charm.moon_bell", new Txt("湯けむりの鈴", "Steamwisp Bell"),
                new Txt("冷たい鈴の音に、熱い霧がまとわりつく。", "Hot mist clings to the sound of the cold bell."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_c_steam_charm_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_c_steam_charm_Power1),
            new UniqueDef("unique.c_eclipse_charm", "charm.eclipse_ring", new Txt("蝕の約束指輪", "Eclipse Promise Ring"),
                new Txt("光と影の約束は、欠けたところから始まる。", "The vow of light and shadow begins at the missing edge."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_c_eclipse_charm_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_c_eclipse_charm_Power1),
            new UniqueDef("unique.c_cinder_charm", "charm.cindercore_locket", new Txt("熾火と影のロケット", "Cinder-and-Shade Locket"),
                new Txt("火が影に溶ける場所に、灰が積もる。", "Ash gathers where fire melts into shade."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_c_cinder_charm_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_c_cinder_charm_Power1),
            new UniqueDef("unique.c_icicrystal_charm", "charm.snowbloom_charm", new Txt("氷晶の花守り", "Icicrystal Bloom Charm"),
                new Txt("氷に咲く花は、光を貯めている。", "Flowers that bloom in ice store light."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_c_icicrystal_charm_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_icicrystal_charm_Power1),
            // v1.29：新しいセットの部位（第1段4種・12個、第2段19種・57個。P37の1種は保留）
            new UniqueDef("set.steamweave.weapon", "weapon.tide_trident", new Txt("湯煙の三叉槍", "Steamhaze Trident"), "set.steamweave"),
            new UniqueDef("set.steamweave.armor", "armor.mist_robe", new Txt("白煙の法衣", "Whitesmoke Robe"), "set.steamweave"),
            new UniqueDef("set.steamweave.charm", "charm.moon_bell", new Txt("沸き立つ鈴", "Simmerbell"), "set.steamweave"),
            new UniqueDef("set.steamweave.head", "head.coral_crown", new Txt("湯気の珊瑚冠", "Steamweave Coral Crown"), "set.steamweave"),
            new UniqueDef("set.steamweave.hands", "hands.cinderthread_wraps", new Txt("湯糸の手巻き", "Steamthread Wraps"), "set.steamweave"),
            new UniqueDef("set.steamweave.feet", "feet.firebloom_slippers", new Txt("湯気の火華靴", "Steamweave Fireblooms"), "set.steamweave"),
            new UniqueDef("set.eclipserite.weapon", "weapon.moonlit_cane", new Txt("蝕の儀の杖", "Eclipse-Rite Cane"), "set.eclipserite"),
            new UniqueDef("set.eclipserite.head", "head.eclipse_mask", new Txt("蝕の儀の面", "Eclipse-Rite Mask"), "set.eclipserite"),
            new UniqueDef("set.eclipserite.charm", "charm.eclipse_ring", new Txt("蝕の儀の指環", "Eclipse-Rite Signet"), "set.eclipserite"),
            new UniqueDef("set.eclipserite.armor", "armor.moon_silk", new Txt("蝕の儀の月絹衣", "Eclipse-Rite Moonsilk"), "set.eclipserite"),
            new UniqueDef("set.eclipserite.hands", "hands.duskstitch_gloves", new Txt("蝕の儀の黄昏手袋", "Eclipse-Rite Gloves"), "set.eclipserite"),
            new UniqueDef("set.eclipserite.feet", "feet.dawnmist_shoes", new Txt("蝕の儀の暁霧靴", "Eclipse-Rite Mist Shoes"), "set.eclipserite"),
            new UniqueDef("set.cinderfall.weapon", "weapon.ember_whip", new Txt("灰引きの鞭", "Ashdrag Whip"), "set.cinderfall"),
            new UniqueDef("set.cinderfall.armor", "armor.ember_jacket", new Txt("降り灰の外衣", "Ashfall Mantle"), "set.cinderfall"),
            new UniqueDef("set.cinderfall.feet", "feet.ash_boots", new Txt("降り灰の行軍靴", "Cinderfall Marchers"), "set.cinderfall"),
            new UniqueDef("set.cinderfall.head", "head.ash_hood", new Txt("降り灰の頭巾", "Ashfall Hood"), "set.cinderfall"),
            new UniqueDef("set.cinderfall.hands", "hands.nightpalm_gloves", new Txt("降り灰の夜掌手袋", "Ashfall Nightpalm Gloves"), "set.cinderfall"),
            new UniqueDef("set.cinderfall.charm", "charm.cindercore_locket", new Txt("降り灰の火芯飾り", "Ashfall Cindercore Locket"), "set.cinderfall"),
            new UniqueDef("set.icicanticle.head", "head.rimebloom_hood", new Txt("霜花の聖頭巾", "Rimebloom Cowl"), "set.icicanticle"),
            new UniqueDef("set.icicanticle.hands", "hands.frost_mitts", new Txt("聖歌の白手袋", "Canticle Mittens"), "set.icicanticle"),
            new UniqueDef("set.icicanticle.charm", "charm.frost_pendant", new Txt("聖氷の首飾り", "Holy-Ice Pendant"), "set.icicanticle"),
            new UniqueDef("set.icicanticle.weapon", "weapon.conch_scepter", new Txt("氷晶の聖杖", "Icicrystal Scepter"), "set.icicanticle"),
            new UniqueDef("set.icicanticle.armor", "armor.seafoam_gown", new Txt("氷晶の聖衣", "Icicrystal Vestment"), "set.icicanticle"),
            new UniqueDef("set.icicanticle.feet", "feet.froststride_shoes", new Txt("聖氷の霜踏み靴", "Canticle Froststriders"), "set.icicanticle"),
            new UniqueDef("unique.w_goldenchime", "weapon.dawn_scepter", new Txt("黄金の鈴杖", "Goldenchime Scepter"),
                new Txt("最初の一鳴りが、いちばん遠くまで届く。", "The first chime carries the farthest."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_w_goldenchime_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_w_goldenchime_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_GoldenBurst" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_goldenchime_Link } },
            new UniqueDef("unique.w_dewreturn", "weapon.dewclear_wand", new Txt("雫返しの細杖", "Dewreturn Wand"),
                new Txt("奪った露は、誰かの朝に降り注ぐ。", "The dew it takes falls again on someone else's morning."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_w_dewreturn_Power0, Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_w_dewreturn_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_Q_Reduction" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_dewreturn_Link } },
            new UniqueDef("unique.w_chillhug", "weapon.glacier_spear", new Txt("氷抱きの槍", "Chillhug Spear"),
                new Txt("凍える腕は、抱いた者を護る盾にもなる。", "A freezing embrace can also be a shield."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_chillhug_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_w_chillhug_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_EmbracingTheChill" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_chillhug_Link } },
            new UniqueDef("unique.w_boreal_axe", "weapon.northwind_axe", new Txt("育ち氷の戦斧", "Ripening-Ice Axe"),
                new Txt("育った氷は、いつか必ず落ちる。", "Ice that keeps growing must someday fall."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_w_boreal_axe_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_w_boreal_axe_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_BigBorealChunk" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_boreal_axe_Link } },
            new UniqueDef("unique.w_twinform", "weapon.hooked_falchion", new Txt("二相の曲刀", "Twinform Falchion"),
                new Txt("一太刀目は誘い、二太刀目が本命。", "The first cut is bait; the second is the point."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_w_twinform_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_w_twinform_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Laceration" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_twinform_Link } },
            new UniqueDef("unique.w_brand_dagger", "weapon.gloaming_dagger", new Txt("刻印の短剣", "Brandmark Dagger"),
                new Txt("刻まれた印は、持ち主を覚えている。", "The mark it carves remembers who made it."),
                Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_w_brand_dagger_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_brand_dagger_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_Q_DeathMark" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_brand_dagger_Link } },
            new UniqueDef("unique.w_incendiary_knives", "weapon.starlit_knives", new Txt("焼夷の投げ刃", "Incendiary Knives"),
                new Txt("刃が落ちた先で、火は勝手に歩き出す。", "Where the blade lands, the fire learns to walk."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_w_incendiary_knives_Power0, Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_incendiary_knives_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_IncendiaryRounds" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_incendiary_knives_Link } },
            new UniqueDef("unique.w_lunge_rapiers", "weapon.twin_rapier", new Txt("踏み込みの双剣", "Lunging Rapiers"),
                new Txt("間合いは、踏み込んだ者の味方になる。", "Distance belongs to whoever steps in."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_w_lunge_rapiers_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_w_lunge_rapiers_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Lunge" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_lunge_rapiers_Link } },
            new UniqueDef("unique.w_swallowflash", "weapon.swallow_kodachi", new Txt("飛燕の一閃", "Swallow's Flash"),
                new Txt("燕は、一度の羽ばたきで影を追い越す。", "A swallow outruns its own shadow in a single stroke."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_w_swallowflash_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_w_swallowflash_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Fleche" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_swallowflash_Link } },
            new UniqueDef("unique.w_houndflute", "weapon.bone_flute", new Txt("猟犬の骨笛", "Houndcaller Bone Flute"),
                new Txt("吹かずとも、森の方から聞こえてくる。", "Even unplayed, the forest hears it."),
                Power.Spellsweep, EquipmentItemsBalanceValues.Unique_unique_w_houndflute_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_w_houndflute_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_SylvanCall" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_houndflute_Link } },
            new UniqueDef("unique.w_fangmoon", "weapon.moon_sickle", new Txt("牙月の鎌", "Fangmoon Sickle"),
                new Txt("満ちるほどに、約束は牙を研ぐ。", "As the moon swells, the pact sharpens its fangs."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_fangmoon_Power0, Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_w_fangmoon_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_MoonlightPact" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_fangmoon_Link } },
            new UniqueDef("unique.w_sunbearer", "weapon.shield_maul", new Txt("日輪の大槌", "Sunbearer Maul"),
                new Txt("担ぐ者の影に、小さな太陽が宿る。", "A small sun lives in its bearer's shadow."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_sunbearer_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_w_sunbearer_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_CruelSun" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_sunbearer_Link } },
            new UniqueDef("unique.w_discipline_lance", "weapon.tower_lance", new Txt("規律の突撃槍", "Lance of Discipline"),
                new Txt("迷いは、突撃の前に捨ててゆく。", "Hesitation is left behind before the charge."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_w_discipline_lance_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_w_discipline_lance_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Discipline" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_discipline_lance_Link } },
            new UniqueDef("unique.w_binding_lantern", "weapon.lantern_rod", new Txt("縛りの燈杖", "Binding Lantern"),
                new Txt("灯りに触れた影は、動けなくなる。", "Shadows that touch its light cannot move."),
                Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_w_binding_lantern_Power0, Power.Medley, EquipmentItemsBalanceValues.Unique_unique_w_binding_lantern_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_EtherealInfluence" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_binding_lantern_Link } },
            new UniqueDef("unique.w_supernova_staff", "weapon.astral_staff", new Txt("超新星の儀杖", "Supernova Rite Staff"),
                new Txt("溜めた一息が、星ひとつ分の光になる。", "One held breath becomes the light of a star."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_w_supernova_staff_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_w_supernova_staff_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_SuperNova" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_supernova_staff_Link } },
            new UniqueDef("unique.w_blizzard_conch", "weapon.conch_scepter", new Txt("吹雪の法螺杖", "Blizzard Conch"),
                new Txt("吹けば雪原が、答えるように白くなる。", "Blow it, and the snowfield answers in white."),
                Power.Frost, EquipmentItemsBalanceValues.Unique_unique_w_blizzard_conch_Power0, Power.BrittleIce, EquipmentItemsBalanceValues.Unique_unique_w_blizzard_conch_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_blizzard_conch_Link } },
            new UniqueDef("unique.w_arrowbaptism", "weapon.longspike_bow", new Txt("洗礼の長弓", "Baptismal Longbow"),
                new Txt("外れた矢が、次の一矢を呼び寄せる。", "Every miss calls the next arrow closer."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_w_arrowbaptism_Power0, Power.CritSplash, EquipmentItemsBalanceValues.Unique_unique_w_arrowbaptism_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Multishot" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_arrowbaptism_Link } },
            new UniqueDef("unique.w_ghostshot", "weapon.hunting_bow", new Txt("霊弾の短弓", "Ghostshot Shortbow"),
                new Txt("仕留めた獲物の影が、次の矢になる。", "The shadow of the felled becomes the next arrow."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_w_ghostshot_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_w_ghostshot_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SpectreBullet" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_ghostshot_Link } },
            new UniqueDef("unique.w_chefsblade", "weapon.bone_cleaver", new Txt("骨断ちの厨刀", "Bonecleft Cleaver"),
                new Txt("腕のいい料理人は、獲物の呼吸を数えない。", "A fine cook never counts the beast's breaths."),
                Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_w_chefsblade_Power0, Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_chefsblade_Power1) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike", "Gem_L_Culinary" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_chefsblade_Link } },
            new UniqueDef("unique.w_gilded_hatchet", "weapon.war_axe", new Txt("金貨割りの手斧", "Coinsplitter Hatchet"),
                new Txt("財布の軽さは、刃の軽さに変わる。", "A lighter purse makes a lighter blade."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_w_gilded_hatchet_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_w_gilded_hatchet_Power1) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "Gem_L_HeartOfGold" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_gilded_hatchet_Link } },
            new UniqueDef("unique.w_halo_blades", "weapon.sunlit_blade", new Txt("光輪の連刃", "Haloed Twinblade"),
                new Txt("輪を描くたび、光が一つ多く灯る。", "Each circle drawn lights one more flame."),
                Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_w_halo_blades_Power0, Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_w_halo_blades_Power1) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_halo_blades_Link } },
            new UniqueDef("unique.w_frenzy_axe", "weapon.severing_axe", new Txt("狂奔の大斧", "Frenzied Greataxe"),
                new Txt("振り回すうちに、持ち主の名を忘れる。", "Swing it long enough and it forgets its owner's name."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_w_frenzy_axe_Power0, Power.Spellsweep, EquipmentItemsBalanceValues.Unique_unique_w_frenzy_axe_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_frenzy_axe_Link } },
            new UniqueDef("unique.w_needleeye", "weapon.twin_fang", new Txt("針穴の双牙", "Needle-Eye Fangs"),
                new Txt("同じ急所を三度。針穴は、三度目で糸が通る。", "Strike the same weak point thrice; on the third, the thread passes through."),
                Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_w_needleeye_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_w_needleeye_Power1),
            new UniqueDef("unique.w_spray_blade", "weapon.maelstrom_sword", new Txt("飛沫の渦剣", "Spraywhirl Blade"),
                new Txt("会心の波は、近くの者まで濡らしてゆく。", "A critical wave soaks everyone standing near."),
                Power.CritSplash, EquipmentItemsBalanceValues.Unique_unique_w_spray_blade_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_w_spray_blade_Power1),
            new UniqueDef("unique.w_thinice_pike", "weapon.frost_spear", new Txt("薄氷の尖槍", "Thin-Ice Pike"),
                new Txt("薄い氷ほど、割れた音がよく響く。", "The thinner the ice, the louder it cracks."),
                Power.BrittleIce, EquipmentItemsBalanceValues.Unique_unique_w_thinice_pike_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_w_thinice_pike_Power1),
            new UniqueDef("unique.w_luckpile_bolts", "weapon.lightning_pair", new Txt("積み運の雷刃", "Luckpile Bolts"),
                new Txt("外した数だけ、いつか雷が味方する。", "Every miss is a debt the lightning repays."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_w_luckpile_bolts_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_w_luckpile_bolts_Power1),
            new UniqueDef("unique.w_surplus_glaive", "weapon.storm_glaive", new Txt("余り嵐の薙刀", "Surplus Storm Glaive"),
                new Txt("倒しきれなかった力は、次の誰かのもの。", "Power that overshoots finds the next foe."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_surplus_glaive_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_w_surplus_glaive_Power1),
            new UniqueDef("unique.w_focus_estoc", "weapon.whiteheat_estoc", new Txt("白熱の焦点突き", "Whiteheat Focus-Thrust"),
                new Txt("針の先ほど狭い一点に、炎のすべてを。", "All the flame, gathered into the tip of a needle."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_w_focus_estoc_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_w_focus_estoc_Power1),
            new UniqueDef("unique.w_duelwave_cutter", "weapon.tide_cutter", new Txt("一騎打ちの波切", "Duelwave Cutter"),
                new Txt("二人きりの浜では、波さえ静まる。", "On a beach for two, even the waves hold still."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_w_duelwave_cutter_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_w_duelwave_cutter_Power1),
            new UniqueDef("unique.w_migrant_bow", "weapon.starsinger_bow", new Txt("渡り鳥の星弓", "Migrant Starbow"),
                new Txt("一羽を射て、矢は群れの別の一羽へ。", "Shoot one bird, and the arrow finds another in the flock."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_w_migrant_bow_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_w_migrant_bow_Power1),
            new UniqueDef("unique.w_smolder_lash", "weapon.ember_whip", new Txt("燻りの鞭", "Smoldering Lash"),
                new Txt("打たれた痕は、いつまでも燻り続ける。", "The welt it leaves keeps smoldering."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_w_smolder_lash_Power0, Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_w_smolder_lash_Power1),
            new UniqueDef("unique.w_evilbane_pierce", "weapon.evilbreaker_lance", new Txt("破邪の貫き", "Evilbane Pierce"),
                new Txt("貫いた先に、さらに一体。", "Through one foe, and into the next."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_evilbane_pierce_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_w_evilbane_pierce_Power1),
            new UniqueDef("unique.w_returntide", "weapon.tide_trident", new Txt("戻り潮の三叉槍", "Returntide Trident"),
                new Txt("仕留めるほどに、潮は早く満ちる。", "The tide rises faster with every kill."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_w_returntide_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_w_returntide_Power1),
            new UniqueDef("unique.w_piledriver", "weapon.thunder_hammer", new Txt("畳み打ちの雷槌", "Pile-Driver Hammer"),
                new Txt("同じ場所に三度。三度目で地面が応える。", "Strike one spot thrice, and the ground answers."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_w_piledriver_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_w_piledriver_Power1),
            new UniqueDef("unique.w_medley_harp", "weapon.star_harp", new Txt("連奏の竪琴", "Medley Harp"),
                new Txt("違う音を重ねるほど、旋律は力を持つ。", "The more distinct the notes, the stronger the tune."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_w_medley_harp_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_w_medley_harp_Power1),
            new UniqueDef("unique.w_sweepcast_wand", "weapon.chanting_wand", new Txt("薙ぎ詠唱の短杖", "Sweepcast Wand"),
                new Txt("唱え終えた手で、そのまま周りを払う。", "The hand that finishes the spell sweeps the room clean."),
                Power.Spellsweep, EquipmentItemsBalanceValues.Unique_unique_w_sweepcast_wand_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_w_sweepcast_wand_Power1),
            new UniqueDef("unique.w_idlehand_bokken", "weapon.tortoise_bokken", new Txt("手慰みの木刀", "Idle-Hand Bokken"),
                new Txt("技が眠る間も、手は遊ばない。", "Even while the skills sleep, the hands stay busy."),
                Power.BareHandedPride, EquipmentItemsBalanceValues.Unique_unique_w_idlehand_bokken_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_w_idlehand_bokken_Power1),
            new UniqueDef("unique.w_kindling_gale", "weapon.windhowl_staff", new Txt("口火の風杖", "Kindling Gale Staff"),
                new Txt("最初に吹く風が、嵐の向きを決める。", "The first gust decides the storm's direction."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_w_kindling_gale_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_w_kindling_gale_Power1),
            new UniqueDef("unique.w_trumpcard_wand", "weapon.dream_wand", new Txt("切り札の夢杖", "Trump-Card Wand"),
                new Txt("最後の一枚を握っていると、手札は強気になる。", "With the last card in hand, the whole hand grows bold."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_w_trumpcard_wand_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_w_trumpcard_wand_Power1),
            new UniqueDef("unique.w_finale_codex", "weapon.dream_tome", new Txt("終曲の綴り本", "Finale Codex"),
                new Txt("すべての頁を読み終えた者に、最後の頁が開く。", "For the one who reads every page, the last page opens."),
                Power.Finale, EquipmentItemsBalanceValues.Unique_unique_w_finale_codex_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_w_finale_codex_Power1),
            new UniqueDef("unique.w_repeat_chart", "weapon.starchart_scroll", new Txt("巡り星の巻物", "Repeating Chart Scroll"),
                new Txt("同じ星を三度なぞれば、道は短くなる。", "Trace the same star three times and the road grows short."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_w_repeat_chart_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_w_repeat_chart_Power1),
            new UniqueDef("unique.w_firstmove_fan", "weapon.evening_fan", new Txt("初手の夕風扇", "First-Move Fan"),
                new Txt("扇は、閉じたまま最初の一手を打つ。", "The fan strikes first, while still folded."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_w_firstmove_fan_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_w_firstmove_fan_Power1),
            new UniqueDef("unique.w_shieldbearer_mace", "weapon.oath_mace", new Txt("盾持つ誓いの槌", "Shieldbearer's Oath Mace"),
                new Txt("護りを手放さずに、護りで殴る。", "It strikes with the guard, never dropping it."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_shieldbearer_mace_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_w_shieldbearer_mace_Power1),
            new UniqueDef("unique.w_anchor_ward", "weapon.anchor_cleaver", new Txt("錨返しの鉈", "Anchorbreaker Cleaver"),
                new Txt("重い障壁ほど、重い一撃になる。", "The heavier the ward, the heavier the blow."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_anchor_ward_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_w_anchor_ward_Power1),
            new UniqueDef("unique.w_bloodtithe_axe", "weapon.war_axe", new Txt("血の対価の戦斧", "Bloodtithe Axe"),
                new Txt("差し出した分だけ、斧は軽くなる。", "The more you pay, the lighter the axe."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_w_bloodtithe_axe_Power0, Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_w_bloodtithe_axe_Power1),
            new UniqueDef("unique.w_unthawed_hammer", "weapon.thaw_hammer", new Txt("解けぬ氷の鎚", "Unthawed Hammer"),
                new Txt("護りの氷は、割れるまで一枚岩。", "Warding ice is a single slab until it breaks."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_unthawed_hammer_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_w_unthawed_hammer_Power1),
            new UniqueDef("unique.w_calm_longblade", "weapon.stillwater_blade", new Txt("静けさの太刀", "Longblade of Calm"),
                new Txt("動かない水面が、いちばん深い。", "The stillest surface is the deepest."),
                Power.StillWater, EquipmentItemsBalanceValues.Unique_unique_w_calm_longblade_Power0, Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_w_calm_longblade_Power1),
            new UniqueDef("unique.w_suneye_thrust", "weapon.whiteheat_estoc", new Txt("太陽眼の突き", "Sun-Eye Thrust"),
                new Txt("太陽の眼に見られた敵は、いつまでも燃え続ける。", "Whoever the sun's eye sees keeps burning."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_w_suneye_thrust_Power0, Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_w_suneye_thrust_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_SolarEye", "St_Q_CruelSun" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_suneye_thrust_Link } },
            new UniqueDef("unique.w_rebound_bow", "weapon.longspike_bow", new Txt("跳ね返り矢の長弓", "Rebound Longbow"),
                new Txt("外した矢ほど、思わぬ場所で役に立つ。", "The arrows that miss prove useful in unexpected places."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_w_rebound_bow_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_w_rebound_bow_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Embertail", "St_Q_HandCannon" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_rebound_bow_Link } },
            new UniqueDef("unique.w_faith_staff", "weapon.pilgrim_staff", new Txt("数え祈りの錫杖", "Rosary Staff"),
                new Txt("数えた祈りの分だけ、杖は重くなる。", "Each counted prayer makes the staff heavier."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_w_faith_staff_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_w_faith_staff_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith", "St_R_BaptismOfSun" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_faith_staff_Link } },
            new UniqueDef("unique.w_laststar_harp", "weapon.star_harp", new Txt("終星の竪琴", "Last-Starlight Harp"),
                new Txt("最後の星が落ちる前に、いちばん高い音を。", "Play the highest note before the last star falls."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_w_laststar_harp_Power0, Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_w_laststar_harp_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_LastStarlight", "St_U_HerWorld" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_laststar_harp_Link } },
            new UniqueDef("unique.w_suspicion_dirk", "weapon.gloaming_dagger", new Txt("疑心の短剣", "Suspicion Dirk"),
                new Txt("疑えば疑うほど、刃は早くなる。", "The more it doubts, the quicker it strikes."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_w_suspicion_dirk_Power0, Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_w_suspicion_dirk_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia", "St_Q_Fleche" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_suspicion_dirk_Link } },
            new UniqueDef("unique.w_unbound_chain", "weapon.chain_sword", new Txt("解き放ちの連剣", "Unbound Chainblade"),
                new Txt("鎖は、外れる瞬間がいちばん軽い。", "A chain is lightest at the moment it comes off."),
                Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_w_unbound_chain_Power0, Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_w_unbound_chain_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_M_FlashStep", "Gem_L_Liberty" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_unbound_chain_Link } },
            new UniqueDef("unique.w_skinthin_cosh", "weapon.granite_cosh", new Txt("薄皮一枚の棍", "Skin-Thin Cosh"),
                new Txt("命は、たった一枚の障壁に預けてある。", "Life is staked on a single ward."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_skinthin_cosh_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_w_skinthin_cosh_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Supersymmetry", "St_R_SanctuaryOfEl" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_w_skinthin_cosh_Link } },
            new UniqueDef("unique.w_tithe_wand", "weapon.dewclear_wand", new Txt("供物の細杖", "Tithe Wand"),
                new Txt("差し出した命の分だけ、黄金が弾ける。", "For every sliver of life offered, gold bursts forth."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_w_tithe_wand_Power0, Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_tithe_wand_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_SuppressedArcanum", "St_R_Cataclysm" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_w_tithe_wand_Link } },
            new UniqueDef("unique.w_prism_crystal_blade", "weapon.sunlit_blade", new Txt("現ならざる結晶刃", "Unreal Crystal Blade"),
                new Txt("光の当たった面だけ、この世の色をしていない。", "Only the lit face is a color from no real world."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_w_prism_crystal_blade_Power0, Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_w_prism_crystal_blade_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_MetalCrystal", "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_w_prism_crystal_blade_Link } },
            new UniqueDef("unique.w_bigbite_axe", "weapon.war_axe", new Txt("大噛みの斧", "Bigbite Axe"),
                new Txt("噛みつく相手が、敵とは限らない。", "What it bites need not be an enemy."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_bigbite_axe_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_w_bigbite_axe_Power1) { Link = new LinkDef { Requires = new[] { "St_U_BigChomp" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_w_bigbite_axe_Link } },
            new UniqueDef("unique.w_oblivion_horn", "weapon.bone_flute", new Txt("忘却の吼え笛", "Oblivion Horn"),
                new Txt("吠えたあとの静けさが、いちばん恐ろしい。", "The silence after the roar is the most frightening part."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_oblivion_horn_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_w_oblivion_horn_Power1) { Link = new LinkDef { Requires = new[] { "St_U_ShoutOfOblivion" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_w_oblivion_horn_Link } },
            new UniqueDef("unique.w_packleader_staff", "weapon.calming_staff", new Txt("群れ導きの杖", "Packleader's Staff"),
                new Txt("先を歩く者に、群れはついてくる。", "The pack follows whoever walks ahead."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_packleader_staff_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_w_packleader_staff_Power1),
            new UniqueDef("unique.w_wellspring_staff", "weapon.pilgrim_staff", new Txt("湧き水の杖", "Wellspring Staff"),
                new Txt("溢れた水は、盾の形で戻ってくる。", "Water that overflows returns in the shape of a shield."),
                Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_w_wellspring_staff_Power0, Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_w_wellspring_staff_Power1),
            new UniqueDef("unique.w_crystal_baton", "weapon.astral_staff", new Txt("結晶の指揮棒", "Crystal Baton"),
                new Txt("音を結晶に変えると、旋律は軽くなる。", "Turn sound to crystal and the melody grows light."),
                Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_w_crystal_baton_Power0, Power.Medley, EquipmentItemsBalanceValues.Unique_unique_w_crystal_baton_Power1),
            new UniqueDef("unique.w_spendthrift_rapier", "weapon.twin_rapier", new Txt("散財の細剣", "Spendthrift Rapier"),
                new Txt("財布の軽さが、足取りの軽さになる。", "A lighter purse makes for lighter feet."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_w_spendthrift_rapier_Power0, Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_w_spendthrift_rapier_Power1),
            new UniqueDef("unique.w_pilgrim_cudgel", "weapon.lighthouse_cudgel", new Txt("祈りの棍", "Pilgrim's Cudgel"),
                new Txt("祈りを重ねるほど、棍は軽くなる。", "The more you pray, the lighter the cudgel."),
                Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_w_pilgrim_cudgel_Power0, Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_w_pilgrim_cudgel_Power1),
            new UniqueDef("unique.w_lucid_baton", "weapon.dream_wand", new Txt("明晰夢の指揮杖", "Lucid Conductor's Wand"),
                new Txt("悪い夢ほど、輪郭がはっきり見える。", "The worse the dream, the clearer its outline."),
                Power.LucidBoon, EquipmentItemsBalanceValues.Unique_unique_w_lucid_baton_Power0, Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_w_lucid_baton_Power1),
            new UniqueDef("unique.w_pridebow", "weapon.hunting_bow", new Txt("獲物誇りの短弓", "Pridebow"),
                new Txt("追われる獲物の誇りを、矢は知っている。", "The arrow knows the pride of the hunted."),
                Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_w_pridebow_Power0, Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_w_pridebow_Power1),
            new UniqueDef("unique.w_reforge_axe", "weapon.severing_axe", new Txt("研ぎ直しの大斧", "Reforging Greataxe"),
                new Txt("倒すたびに、研ぎ直す手間が省ける。", "Every kill saves the trouble of re-sharpening."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_w_reforge_axe_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_w_reforge_axe_Power1),
            new UniqueDef("unique.w_harvest_scythe", "weapon.moon_sickle", new Txt("元素刈りの鎌", "Elemental Reaper"),
                new Txt("四つの色を刈り取って、初めて収穫祭になる。", "The harvest feast begins only when all four colors are reaped."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_w_harvest_scythe_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_harvest_scythe_Power1),
            new UniqueDef("unique.w_prism_shuriken", "weapon.starlit_knives", new Txt("七彩の投げ刃", "Prism Throwing Knives"),
                new Txt("色の違う刃ほど、よく刺さる。", "The more varied the colors of the blade, the deeper it bites."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_w_prism_shuriken_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_w_prism_shuriken_Power1),
            new UniqueDef("unique.w_icecrack_axe", "weapon.northwind_axe", new Txt("氷割りの会心斧", "Icecrack Axe"),
                new Txt("凍った敵を割る音は、会心の合図。", "The sound of splitting frozen foes is the signal of a crit."),
                Power.BrittleIce, EquipmentItemsBalanceValues.Unique_unique_w_icecrack_axe_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_w_icecrack_axe_Power1),
            new UniqueDef("unique.w_charging_lance", "weapon.tower_lance", new Txt("助走付きの突撃槍", "Charging Lance"),
                new Txt("走った距離だけ、穂先は重くなる。", "The farther you run, the heavier the tip."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_w_charging_lance_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_w_charging_lance_Power1),
            new UniqueDef("unique.w_skipping_kodachi", "weapon.swallow_kodachi", new Txt("跳ね燕の小太刀", "Skipping Swallow Kodachi"),
                new Txt("地を蹴るたびに、刃が先に着いている。", "The blade arrives before the foot has landed."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_w_skipping_kodachi_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_w_skipping_kodachi_Power1),
            new UniqueDef("unique.w_duelist_sabre", "weapon.twin_rapier", new Txt("決闘者の細剣", "Duelist's Sabre"),
                new Txt("立会人がいなくても、礼は欠かさない。", "Even without a witness, the courtesy is kept."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_w_duelist_sabre_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_w_duelist_sabre_Power1),
            new UniqueDef("unique.w_pinpoint_bow", "weapon.longspike_bow", new Txt("射抜きの長弓", "Pinpoint Longbow"),
                new Txt("同じ的に五本。五本目は、的の向こうまで。", "Five arrows into one mark; the fifth goes beyond it."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_w_pinpoint_bow_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_w_pinpoint_bow_Power1),
            new UniqueDef("unique.w_wanderer_dagger", "weapon.gloaming_dagger", new Txt("渡り影の短剣", "Wandering Shadow Dagger"),
                new Txt("刺す相手を変えるたび、影も持ち主を変える。", "With every new target, the shadow changes masters."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_w_wanderer_dagger_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_w_wanderer_dagger_Power1),
            new UniqueDef("unique.w_overkill_halberd", "weapon.iron_halberd", new Txt("過ぎる刃の戟", "Overreach Halberd"),
                new Txt("必要以上に振るった分は、無駄にならない。", "Swings beyond need are never wasted."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_w_overkill_halberd_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_w_overkill_halberd_Power1),
            new UniqueDef("unique.w_barehand_katar", "weapon.ember_katar", new Txt("待ちぼうけの短刃", "Idle Katar"),
                new Txt("技が冷える間は、刃が代わりに働く。", "While the skills cool, the blade does the work."),
                Power.BareHandedPride, EquipmentItemsBalanceValues.Unique_unique_w_barehand_katar_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_w_barehand_katar_Power1),
            new UniqueDef("unique.w_ace_scythe", "weapon.dusk_scythe", new Txt("切り札の大鎌", "Trump Scythe"),
                new Txt("最後の札は、切るまで見せない。", "The last card stays hidden until it is played."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_w_ace_scythe_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_w_ace_scythe_Power1),
            new UniqueDef("unique.w_salvo_estoc", "weapon.whiteheat_estoc", new Txt("口火の突き剣", "Salvo Estoc"),
                new Txt("最初の一突きが、次の合図になる。", "The first thrust is the signal for the rest."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_w_salvo_estoc_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_w_salvo_estoc_Power1),
            new UniqueDef("unique.bond_gatemaul", "weapon.gatehouse_maul", new Txt("二人で担ぐ城門槌", "Two-Bearer Gatemaul"),
                new Txt("前に立つ者と、背を護る者。二人で一つの門になる。", "One stands before, one guards the back. Together they are a single gate."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_bond_gatemaul_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_bond_gatemaul_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "Hero_Vesper" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_gatemaul_Link } },
            new UniqueDef("unique.hd_balance_crown", "head.radiant_halo", new Txt("均衡の光冠", "Crown of Equilibrium"),
                new Txt("光の秤は、傾いた方へ重みを戻す。", "The scale of light restores weight to whichever side tilts."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_hd_balance_crown_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_balance_crown_Power1) { Link = new LinkDef { Requires = new[] { "St_U_BeamOfBalance" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_balance_crown_Link } },
            new UniqueDef("unique.hd_worldcracker_helm", "head.boulder_helm", new Txt("世界割りの兜", "Worldsplitter Helm"),
                new Txt("兜の庇を持ち上げると、地平線がひび割れる。", "Lift the visor, and the horizon cracks."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_worldcracker_helm_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_worldcracker_helm_Power1) { Link = new LinkDef { Requires = new[] { "St_U_WorldCracker" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_worldcracker_helm_Link } },
            new UniqueDef("unique.hd_aurena_circlet", "head.kindly_circlet", new Txt("癒しと脅しの冠", "Circlet of Mercy and Menace"),
                new Txt("癒す手と威す手は、同じ指から伸びている。", "The hand that heals and the hand that threatens grow from the same fingers."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_hd_aurena_circlet_Power0, Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_hd_aurena_circlet_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_D_BeautifulThreat", "St_Q_GoldenBurst" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_aurena_circlet_Link } },
            new UniqueDef("unique.hd_bismuth_veil", "head.starless_veil", new Txt("虹彩のヴェール", "Irisveil"),
                new Txt("ヴェールの内側で、色は一瞬ごとに入れ替わる。", "Behind the veil, colors change places every instant."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_hd_bismuth_veil_Power0, Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_hd_bismuth_veil_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_D_PrismaticEyes", "St_QR_Innocence" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_bismuth_veil_Link } },
            new UniqueDef("unique.hd_cetus_helm", "head.icewall_helm", new Txt("凍氷の守り兜", "Frozen-Guard Helm"),
                new Txt("動かぬ氷壁の中で、拳だけが温かい。", "Within the unmoving ice wall, only the fist stays warm."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_cetus_helm_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_hd_cetus_helm_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_D_IcyVeins", "St_R_FrozenFists" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_hd_cetus_helm_Link } },
            new UniqueDef("unique.hd_husk_mask", "head.raven_mask", new Txt("一歩一殺の鴉面", "Onestep Crow Mask"),
                new Txt("鴉は、勝負のついた場所にだけ降りる。", "The crow lands only where the matter is already settled."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_husk_mask_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_hd_husk_mask_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_D_TheKillingFlow", "St_Q_DeathMark" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_husk_mask_Link } },
            new UniqueDef("unique.hd_lacerta_patch", "head.eye_patch", new Txt("火薬と精密の眼帯", "Powder-and-Precision Eyepatch"),
                new Txt("片目で狙い、片目で次の火薬を見る。", "One eye aims, the other watches the next charge of powder."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_lacerta_patch_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_hd_lacerta_patch_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_D_SalamanderPowder", "St_R_PrecisionShot" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_lacerta_patch_Link } },
            new UniqueDef("unique.hd_mist_greathelm", "head.knight_helm", new Txt("構えと受け返しの大兜", "En-Garde Riposte Greathelm"),
                new Txt("構えを見せれば、すでに返し技は打たれている。", "Show the stance, and the riposte has already been struck."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_mist_greathelm_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_hd_mist_greathelm_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_D_AstridsMasterpieceEnGarde", "St_R_Parry" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_hd_mist_greathelm_Link } },
            new UniqueDef("unique.hd_nachia_antlers", "head.beastcaller_antlers", new Txt("群れと蛇の角冠", "Pack-and-Serpent Antlers"),
                new Txt("蛇が巻きつけば、角は祝福の枝になる。", "When the serpent coils on it, the antlers become branches of blessing."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_hd_nachia_antlers_Power0, Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_hd_nachia_antlers_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_D_HeartOfThePack", "St_R_SerpentineBlessing" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_hd_nachia_antlers_Link } },
            new UniqueDef("unique.hd_vesper_horned", "head.horned_helm", new Txt("決意と洗礼の角兜", "Resolve-and-Baptism Horned Helm"),
                new Txt("洗礼の炎の中でも、決意だけは動かない。", "Amid the baptismal fire, only resolve stays still."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_vesper_horned_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_hd_vesper_horned_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_D_Resolve", "St_R_BaptismOfSun" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_vesper_horned_Link } },
            new UniqueDef("unique.hd_yubar_diadem", "head.star_diadem", new Txt("収束と平静の星冠", "Convergence-and-Serenity Diadem"),
                new Txt("星々が一点に集うとき、時計は静かに止まる。", "When the stars gather at one point, the clock quietly stops."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_yubar_diadem_Power0, Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_hd_yubar_diadem_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_D_ConvergencePoint", "St_R_Tranquility" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_hd_yubar_diadem_Link } },
            new UniqueDef("unique.bond_ward_and_heal_crown", "head.wardband", new Txt("盾と癒しの二重冠", "Twin Crown of Ward and Mend"),
                new Txt("盾が耐え、癒しが支える。冠は二つで一つ。", "The shield endures, the mending supports. Two crowns, one whole."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_bond_ward_and_heal_crown_Power0, Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_bond_ward_and_heal_crown_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "Hero_Aurena" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_ward_and_heal_crown_Link } },
            new UniqueDef("unique.bond_gun_and_hammer_cap", "head.longshot_cap", new Txt("銃と槌の連帽", "Gun-and-Hammer Cap"),
                new Txt("音の大きな者が二人。見せ場も二つ。", "Two loud ones, two moments in the spotlight."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_bond_gun_and_hammer_cap_Power0, Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_bond_gun_and_hammer_cap_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "Hero_Vesper" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_gun_and_hammer_cap_Link } },
            new UniqueDef("unique.bond_fang_and_forest_hood", "head.moonlace_hood", new Txt("牙と森の頭巾", "Fang-and-Forest Hood"),
                new Txt("剣の前で、群れが静かに待っている。", "Before the sword, the pack waits quietly."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_bond_fang_and_forest_hood_Power0, Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_bond_fang_and_forest_hood_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "Hero_Nachia" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_fang_and_forest_hood_Link } },
            new UniqueDef("unique.bond_prism_and_star_veil", "head.whisper_veil", new Txt("虹と星の重ねヴェール", "Rainbow-and-Star Veil"),
                new Txt("星の光を、虹が受け止める。", "The rainbow catches the starlight."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_bond_prism_and_star_veil_Power0, Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_bond_prism_and_star_veil_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "Hero_Yubar" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_prism_and_star_veil_Link } },
            new UniqueDef("unique.hd_hairline_band", "head.wardband", new Txt("薄皮一枚の守り環", "Hairline Guard Band"),
                new Txt("最大HPは1、盾は無限。その危うさが取り柄。", "Max HP of one, a shield without limit; its charm is its peril."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_hd_hairline_band_Power0, Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_hairline_band_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Supersymmetry", "Hero_Cetus" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_hd_hairline_band_Link } },
            new UniqueDef("unique.hd_liberty_veil", "head.dream_veil", new Txt("解放の夢面紗", "Liberty Dreamveil"),
                new Txt("口火を切ったその瞬間、枷が一つ外れる。", "The instant you light the fuse, one shackle falls away."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_hd_liberty_veil_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_hd_liberty_veil_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty", "St_Q_Discipline" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_hd_liberty_veil_Link } },
            new UniqueDef("unique.hd_goldeneye_hood", "head.sunbeam_hood", new Txt("黄金の眼の頭巾", "Goldeneye Hood"),
                new Txt("財布と太陽が、同じ高さで輝いている。", "The purse and the sun shine at the same height."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_hd_goldeneye_hood_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_goldeneye_hood_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_HeartOfGold", "Gem_L_SolarEye" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_hd_goldeneye_hood_Link } },
            new UniqueDef("unique.hd_tithe_diadem", "head.comet_diadem", new Txt("供犠の彗星冠", "Tithe Comet Diadem"),
                new Txt("削った命の分だけ、尾は長く伸びる。", "The more life you carve away, the longer the tail grows."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_tithe_diadem_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_hd_tithe_diadem_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_SuppressedArcanum", "St_Q_SuperNova" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_tithe_diadem_Link } },
            new UniqueDef("unique.hd_laststar_veil", "head.starless_veil", new Txt("星なき夜の切り札冠", "Starless Trump Veil"),
                new Txt("最後の星が消えても、手札には最後の一枚。", "Even when the last star is gone, one card remains in hand."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_hd_laststar_veil_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_hd_laststar_veil_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_LastStarlight", "St_R_Cataclysm" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_laststar_veil_Link } },
            new UniqueDef("unique.hd_coldcore_helm", "head.frost_helm", new Txt("氷河核の凍て兜", "Coldcore Helm"),
                new Txt("兜の芯まで凍れば、迷いも凍る。", "Freeze to the helm's core, and doubt freezes too."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_coldcore_helm_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_hd_coldcore_helm_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GlacialCore", "St_L_Blizzard" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_coldcore_helm_Link } },
            new UniqueDef("unique.hd_everflame_crown", "head.ember_crown", new Txt("消えぬ火の王冠", "Everflame Crown"),
                new Txt("頭上の炎は、倒れた後も燃え続ける。", "The flame overhead keeps burning even after you fall."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_everflame_crown_Power0, Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_hd_everflame_crown_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_EternalFlame", "St_Q_CruelSun" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_everflame_crown_Link } },
            new UniqueDef("unique.hd_fireball_band", "head.magma_band", new Txt("火球化身の鉢巻", "Fireball Avatar Headband"),
                new Txt("燃える頭は、冷静な眼を隠している。", "A burning head hides a calm eye."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_fireball_band_Power0, Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_hd_fireball_band_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "Gem_U_EternalFlame" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_fireball_band_Link } },
            new UniqueDef("unique.hd_coinburst_hat", "head.lantern_hat", new Txt("金貨散らしの笠", "Coinscatter Hat"),
                new Txt("散らした金貨の分だけ、足取りは軽い。", "The more coins scattered, the lighter the step."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_hd_coinburst_hat_Power0, Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_hd_coinburst_hat_Power1) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "Gem_L_CamillasGift" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_coinburst_hat_Link } },
            new UniqueDef("unique.hd_radiant_mask", "head.sun_mask", new Txt("輝爆の日輪面", "Radiantburst Sun Mask"),
                new Txt("光が弾けるたびに、星が一つ入れ替わる。", "Each burst of light replaces a star."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_hd_radiant_mask_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_radiant_mask_Power1) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_hd_radiant_mask_Link } },
            new UniqueDef("unique.hd_gnaw_helm", "head.void_helm", new Txt("蝕む空洞兜", "Gnawing Hollow Helm"),
                new Txt("空洞の中で、光が少しずつ消えてゆく。", "Inside the hollow, the light fades little by little."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_hd_gnaw_helm_Power0, Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_hd_gnaw_helm_Power1) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption", "Gem_U_SoulPrison" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_gnaw_helm_Link } },
            new UniqueDef("unique.hd_ghostreturn_hood", "head.ash_hood", new Txt("霊弾還りの灰頭巾", "Ghostreturn Ash Hood"),
                new Txt("撃った弾が、倒れた者の影から還ってくる。", "The spent shot returns from the fallen one's shadow."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_hd_ghostreturn_hood_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_hd_ghostreturn_hood_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SpectreBullet", "St_L_HerosReturn" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_ghostreturn_hood_Link } },
            new UniqueDef("unique.hd_butcher_pelt", "head.wolf_pelt", new Txt("屠り噛みの毛皮頭巾", "Butcherbite Pelt Hood"),
                new Txt("仕留めた数だけ、牙は研ぎ上がる。", "The fangs sharpen with every kill."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_hd_butcher_pelt_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_hd_butcher_pelt_Power1) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike", "St_U_BigChomp" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_butcher_pelt_Link } },
            new UniqueDef("unique.hd_arrowstorm_band", "head.hawkeye_band", new Txt("矢嵐の鷹目帯", "Arrowstorm Hawkeye Band"),
                new Txt("外れた矢の数だけ、鷹の目は研がれる。", "The hawk's eye is honed by every arrow that missed."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_hd_arrowstorm_band_Power0, Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_arrowstorm_band_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Multishot", "St_R_QuickTrigger" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_hd_arrowstorm_band_Link } },
            new UniqueDef("unique.hd_hysteria_mask", "head.berserker_mask", new Txt("狂奔の怒り面", "Hysteria Mask"),
                new Txt("怒りの面は、持ち主の顔を忘れている。", "The mask of rage has forgotten its wearer's face."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_hysteria_mask_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_hd_hysteria_mask_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_U_Hysteria" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_hysteria_mask_Link } },
            new UniqueDef("unique.hd_burrower_visor", "head.warden_visor", new Txt("土竜の面頬", "Burrower's Visor"),
                new Txt("地に潜れば、不動は最良の戦術。", "Underground, stillness is the best tactic."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_burrower_visor_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_hd_burrower_visor_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Burrow", "Gem_L_Paranoia" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_hd_burrower_visor_Link } },
            new UniqueDef("unique.hd_oblivion_helm", "head.eclipse_mask", new Txt("忘却の吼え面", "Oblivion Roar Mask"),
                new Txt("吠えた後、一瞬だけ誰もが動きを止める。", "After the roar, for one moment everyone stops."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_oblivion_helm_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_hd_oblivion_helm_Power1) { Link = new LinkDef { Requires = new[] { "St_U_ShoutOfOblivion" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_oblivion_helm_Link } },
            new UniqueDef("unique.hd_hersworld_veil", "head.whisper_veil", new Txt("彼方の世界のヴェール", "Veil of the Other World"),
                new Txt("黒い穴の縁で、ささやきは長く続く。", "At the edge of the black hole, whispers last long."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_hersworld_veil_Power0, Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_hd_hersworld_veil_Power1) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_hersworld_veil_Link } },
            new UniqueDef("unique.hd_infernal_horned", "head.horned_helm", new Txt("業火と勇気の角兜", "Hellfire-and-Valor Helm"),
                new Txt("恐れを燃料にして、炎は燃える。", "The flame burns on fear as fuel."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_hd_infernal_horned_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_hd_infernal_horned_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_QR_InfernalTales", "St_QR_ValiantHeart" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_infernal_horned_Link } },
            new UniqueDef("unique.hd_moonlace_blessing", "head.moonlace_hood", new Txt("月光と蛇の頭巾", "Moonlight-and-Serpent Hood"),
                new Txt("月が満ちれば、蛇も祝福を吐く。", "When the moon is full, the serpent breathes blessings."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_hd_moonlace_blessing_Power0, Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_moonlace_blessing_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_MoonlightPact", "St_R_SerpentineBlessing" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_hd_moonlace_blessing_Link } },
            new UniqueDef("unique.hd_ethereal_calm_hood", "head.sunbeam_hood", new Txt("影響と平静の頭巾", "Influence-and-Calm Hood"),
                new Txt("縛った光は、静かな時間を残す。", "Bound light leaves quiet time behind."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_hd_ethereal_calm_hood_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_hd_ethereal_calm_hood_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_Q_EtherealInfluence", "St_R_Tranquility" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_hd_ethereal_calm_hood_Link } },
            new UniqueDef("unique.hd_ward_gleam_barrier", "head.sentry_visor", new Txt("障壁映しの面頬", "Wardmirror Visor"),
                new Txt("障壁が光を返すとき、拳も光る。", "When the ward returns the light, the fist shines too."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_hd_ward_gleam_barrier_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_hd_ward_gleam_barrier_Power1),
            new UniqueDef("unique.hd_ward_gleam_aegis", "head.fortress_coif", new Txt("輝く守りの頭巾", "Gleamguard Coif"),
                new Txt("守りが輝くほど、攻めも冴える。", "The brighter the guard, the sharper the offense."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_hd_ward_gleam_aegis_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_hd_ward_gleam_aegis_Power1),
            new UniqueDef("unique.hd_ward_gleam_bulwark", "head.pillar_crown", new Txt("障壁柱の冠", "Wardpillar Crown"),
                new Txt("柱のような護りが、頭上で光る。", "A pillar-like ward shines overhead."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_hd_ward_gleam_bulwark_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_hd_ward_gleam_bulwark_Power1),
            new UniqueDef("unique.hd_costar_finale", "head.oathring_circlet", new Txt("共演の終幕冠", "Co-star Finale Circlet"),
                new Txt("拍手の最後に、自分の出番がある。", "Your turn comes at the very end of the applause."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_hd_costar_finale_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_hd_costar_finale_Power1),
            new UniqueDef("unique.hd_costar_overload", "head.dream_circlet", new Txt("共演の夢冠", "Co-star Dreamcirclet"),
                new Txt("誰かの奥義が、自分の技を軽くする。", "Someone else's ultimate lightens your own skills."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_hd_costar_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_costar_overload_Power1),
            new UniqueDef("unique.hd_costar_resonance", "head.healer_band", new Txt("共に立つ癒し帯", "Standing-Together Band"),
                new Txt("並んで立つほど、声は大きくなる。", "The more people stand side by side, the louder the voice."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_hd_costar_resonance_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_hd_costar_resonance_Power1),
            new UniqueDef("unique.hd_focus_executioner", "head.scout_goggles", new Txt("焦点の処刑眼鏡", "Focus Executioner Goggles"),
                new Txt("同じ的を見続けた者に、慈悲はない。", "One who stares at the same mark shows no mercy."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_focus_executioner_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hd_focus_executioner_Power1),
            new UniqueDef("unique.hd_focus_opening", "head.hunter_hood", new Txt("初撃集中の頭巾", "Firststrike Focus Hood"),
                new Txt("最初の一撃を五回続けると、それは必殺になる。", "Five first strikes in a row become a finishing blow."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_focus_opening_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_hd_focus_opening_Power1),
            new UniqueDef("unique.hd_focus_fetters", "head.warden_visor", new Txt("縫い留めの視線", "Pinning Gaze Visor"),
                new Txt("見つめられた獲物は、足が動かなくなる。", "Prey under its gaze can no longer move its feet."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_hd_focus_fetters_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_hd_focus_fetters_Power1),
            new UniqueDef("unique.hd_duelist_vigor", "head.knight_helm", new Txt("決闘者の万全兜", "Duelist's Peak Helm"),
                new Txt("体調が万全なら、一騎打ちは楽しい。", "At full health, a duel is a pleasure."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_duelist_vigor_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_hd_duelist_vigor_Power1),
            new UniqueDef("unique.hd_duelist_crit", "head.eye_patch", new Txt("立会いの眼帯", "Witness Eyepatch"),
                new Txt("一人しか見ていない目は、よく当たる。", "An eye fixed on one person rarely misses."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_duelist_crit_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_hd_duelist_crit_Power1),
            new UniqueDef("unique.hd_duelist_thorn", "head.thorn_circlet", new Txt("一騎打ちの棘冠", "Duelist's Thorned Circlet"),
                new Txt("一騎打ちの礼として、棘をひと刺し。", "A thorn as the courtesy of a duel."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_hd_duelist_thorn_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_hd_duelist_thorn_Power1),
            new UniqueDef("unique.hd_stance_aegis", "head.iron_helm", new Txt("不動の鉄兜", "Immovable Ironhelm"),
                new Txt("止まっているときだけ、兜は盾になる。", "Only when still does the helm become a shield."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_stance_aegis_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_hd_stance_aegis_Power1),
            new UniqueDef("unique.hd_stance_bulwark", "head.sunkenbell_helm", new Txt("沈鐘の構え兜", "Sunken Bell Stance Helm"),
                new Txt("鐘が沈んでいる間は、鳴らない。", "While the bell is sunken, it does not toll."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_stance_bulwark_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_hd_stance_bulwark_Power1),
            new UniqueDef("unique.hd_stance_barrier", "head.moss_crown", new Txt("苔むす構えの冠", "Mosscovered Stance Crown"),
                new Txt("動かないものには、苔が生える。", "Moss grows on what does not move."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_hd_stance_barrier_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_hd_stance_barrier_Power1),
            new UniqueDef("unique.hd_stardust_overload", "head.meditation_band", new Txt("星屑めぐりの瞑想帯", "Stardust Meditation Band"),
                new Txt("光が五つ揃うとき、技の熱が引く。", "When five lights gather, the heat of the skills fades."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_hd_stardust_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_stardust_overload_Power1),
            new UniqueDef("unique.hd_stardust_finale", "head.noonlight_circlet", new Txt("星屑終幕の冠", "Stardust Finale Circlet"),
                new Txt("光が巡るたびに、終幕が近づく。", "With each cycle of light, the finale draws near."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_hd_stardust_finale_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_hd_stardust_finale_Power1),
            new UniqueDef("unique.hd_stardust_surge", "head.sun_mask", new Txt("星屑奥義の日輪面", "Stardust Ultimate Sun Mask"),
                new Txt("星屑の巡りが、奥義の仕度を早める。", "The cycle of stardust hastens the ultimate's preparation."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_hd_stardust_surge_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_hd_stardust_surge_Power1),
            new UniqueDef("unique.hd_prism_ember", "head.emberbloom_crown", new Txt("七彩火花の冠", "Prismspark Crown"),
                new Txt("色が移るたび、火花の殻が一枚閉じる。", "Each shift of color closes a shell of sparks."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_hd_prism_ember_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_hd_prism_ember_Power1),
            new UniqueDef("unique.hd_prism_frost", "head.rimebloom_hood", new Txt("七彩霜の頭巾", "Prismfrost Hood"),
                new Txt("色が変わるたびに、霜が盾の形に固まる。", "Every change of color hardens the frost into shield."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_hd_prism_frost_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_hd_prism_frost_Power1),
            new UniqueDef("unique.hd_prism_radiance", "head.noonlight_circlet", new Txt("七彩正午の冠", "Prismnoon Circlet"),
                new Txt("正午の光は、七つの色に分かれて降る。", "Noon light falls divided into seven colors."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_hd_prism_radiance_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_prism_radiance_Power1),
            new UniqueDef("unique.hd_prism_umbra", "head.moon_hood", new Txt("七彩月影の頭巾", "Prismmoon Hood"),
                new Txt("月影が色を変えるたび、護りが一枚増える。", "Each time the moon's shadow changes color, one more ward appears."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_hd_prism_umbra_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_hd_prism_umbra_Power1),
            new UniqueDef("unique.hd_packhead_frenzy", "head.antler_crown", new Txt("群れ追う鹿角冠", "Packchaser Antler Crown"),
                new Txt("群れが増えるほど、角も賑やかになる。", "The larger the pack, the livelier the antlers."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_hd_packhead_frenzy_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_hd_packhead_frenzy_Power1),
            new UniqueDef("unique.hd_packhead_resonance", "head.beastcaller_antlers", new Txt("群れ呼びの共鳴冠", "Packsong Resonance Antlers"),
                new Txt("声を合わせて、獲物を分け合う。", "Join voices, and share the prey."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_hd_packhead_resonance_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_hd_packhead_resonance_Power1),
            new UniqueDef("unique.hd_packhead_thorn", "head.talon_crown", new Txt("群れ守りの棘冠", "Packguard Thorn Crown"),
                new Txt("群れの背中は、棘で守られている。", "The pack's backs are guarded by thorns."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_hd_packhead_thorn_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_hd_packhead_thorn_Power1),
            new UniqueDef("unique.hd_medley_vigor", "head.sage_hat", new Txt("連奏の賢者帽", "Sage's Medley Hat"),
                new Txt("違う技を重ねられる者は、頭が冴えている。", "One who can layer different skills has a sharp mind."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_medley_vigor_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_hd_medley_vigor_Power1),
            new UniqueDef("unique.hd_medley_surge", "head.dream_circlet", new Txt("連奏と奥義の冠", "Medley-and-Finish Circlet"),
                new Txt("三つの旋律の後に、終わりの一音を。", "After three melodies, one closing note."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_medley_surge_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_hd_medley_surge_Power1),
            new UniqueDef("unique.hd_medley_overload", "head.oathring_circlet", new Txt("三重奏の誓輪", "Trio Oathring"),
                new Txt("三種の技を回すたび、輪は大きく鳴る。", "With each rotation of three skills, the ring rings louder."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_hd_medley_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_medley_overload_Power1),
            new UniqueDef("unique.hd_salvo_blaze", "head.crimsonlotus_hood", new Txt("口火の紅蓮頭巾", "Kindling Crimson Hood"),
                new Txt("最初の火を点けた者が、宴の主役になる。", "Whoever lights the first fire becomes the star of the feast."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_hd_salvo_blaze_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_hd_salvo_blaze_Power1),
            new UniqueDef("unique.hd_salvo_momentum", "head.gale_hood", new Txt("初手の疾風頭巾", "Firstgust Hood"),
                new Txt("最初の風だけは、必ず追い風になる。", "The first gust is always a tailwind."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_hd_salvo_momentum_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_hd_salvo_momentum_Power1),
            new UniqueDef("unique.hd_salvo_frenzy", "head.thunderveil_hood", new Txt("口火乱戦の雷帳", "Thunderveil of the Opening Brawl"),
                new Txt("火蓋が切られたら、あとは乱戦だ。", "Once the fuse is lit, everything turns to brawl."),
                Power.OpeningSalvo, EquipmentItemsBalanceValues.Unique_unique_hd_salvo_frenzy_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_hd_salvo_frenzy_Power1),
            new UniqueDef("unique.hd_pileon_overload", "head.vulture_hood", new Txt("畳み掛けの禿鷹頭巾", "Pile-On Vulture Hood"),
                new Txt("同じ場所を三度、啄む。", "Peck the same spot three times."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_hd_pileon_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_pileon_overload_Power1),
            new UniqueDef("unique.hd_pileon_finale", "head.sage_hat", new Txt("畳み終曲の賢者帽", "Pile-On Finale Hat"),
                new Txt("三度重ねて、四度目で幕を引く。", "Repeat thrice, and draw the curtain on the fourth."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_hd_pileon_finale_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_hd_pileon_finale_Power1),
            new UniqueDef("unique.hd_pileon_executioner", "head.horned_helm", new Txt("畳み処刑の角兜", "Pile-On Executioner's Helm"),
                new Txt("角の先で、同じ傷を何度も叩く。", "Strike the same wound again and again with the horn's tip."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_hd_pileon_executioner_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hd_pileon_executioner_Power1),
            new UniqueDef("unique.hd_trump_overload", "head.scout_goggles", new Txt("切り札の遠眼鏡", "Trump Spyglass"),
                new Txt("切り札を残すために、他の技は惜しまない。", "To keep the trump card, the other skills are spent freely."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_hd_trump_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_trump_overload_Power1),
            new UniqueDef("unique.hd_trump_finale", "head.dream_veil", new Txt("終曲を握る夢面", "Finale-in-Hand Dreamveil"),
                new Txt("最後の曲目は、まだ袖の中に。", "The last number is still up the sleeve."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_hd_trump_finale_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_hd_trump_finale_Power1),
            new UniqueDef("unique.hd_trump_vigor", "head.iron_coif", new Txt("万全の切り札頭巾", "Peak Trump Coif"),
                new Txt("体も札も万全なら、迷うことはない。", "With both body and cards at their best, there is nothing to doubt."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_hd_trump_vigor_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_hd_trump_vigor_Power1),
            new UniqueDef("unique.hd_circuit_overload", "head.moonlace_hood", new Txt("結晶回路の月頭巾", "Crystal Circuit Moonhood"),
                new Txt("エッセンスの輝きが、技の冷えを早める。", "The essence's glow hastens the cooling of skills."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_hd_circuit_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_circuit_overload_Power1),
            new UniqueDef("unique.hd_circuit_finale", "head.whisper_veil", new Txt("結晶終曲のヴェール", "Crystal Finale Veil"),
                new Txt("結晶が一つ溶けるたび、一曲が近づく。", "With each crystal that melts, a song draws nearer."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_hd_circuit_finale_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_hd_circuit_finale_Power1),
            new UniqueDef("unique.hd_lifeline_overflow", "head.healer_band", new Txt("命綱の癒し帯", "Lifeline Healing Band"),
                new Txt("溢れた命の滴が、群れの牙になる。", "Droplets of overflowing life become the pack's fangs."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_hd_lifeline_overflow_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_hd_lifeline_overflow_Power1),
            new UniqueDef("unique.hd_lifeline_barrier", "head.leaf_wreath", new Txt("命綱の葉冠", "Lifeline Leaf Crown"),
                new Txt("護りが張られるごとに、群れが息を吹き返す。", "Each raised ward revives the pack."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_hd_lifeline_barrier_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_hd_lifeline_barrier_Power1),
            new UniqueDef("unique.hd_omen_barrier", "head.dream_circlet", new Txt("夢見の前兆冠", "Dreamomen Circlet"),
                new Txt("夢が荒れる前に、頭巾が先に震える。", "The hood trembles before the dream turns rough."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_hd_omen_barrier_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_hd_omen_barrier_Power1),
            new UniqueDef("unique.hd_omen_devotion", "head.meditation_band", new Txt("予兆祈りの瞑想帯", "Omenprayer Meditation Band"),
                new Txt("予兆の前に祈ることを、習いにする。", "Make it a habit to pray before every omen."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_hd_omen_devotion_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_hd_omen_devotion_Power1),
            new UniqueDef("unique.hd_omen_lucid", "head.sage_hat", new Txt("明晰な予兆の帽子", "Lucid Omen Hat"),
                new Txt("悪夢を先に読んだ者が、悪夢に勝つ。", "Whoever reads the nightmare first beats it."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_hd_omen_lucid_Power0, Power.LucidBoon, EquipmentItemsBalanceValues.Unique_unique_hd_omen_lucid_Power1),
            new UniqueDef("unique.hd_rear_overload", "head.longshot_cap", new Txt("後衛の遠矢帽", "Rearguard Longshot Cap"),
                new Txt("いちばん遠くで、いちばん冴えている。", "Farthest away, sharpest of all."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_hd_rear_overload_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_hd_rear_overload_Power1),
            new UniqueDef("unique.hd_rear_resonance", "head.mist_veil", new Txt("後方支援の霧面紗", "Rearsupport Fogveil"),
                new Txt("霧の向こうから、仲間と呼吸を合わせる。", "Breathe in step with your friends from beyond the mist."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_hd_rear_resonance_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_hd_rear_resonance_Power1),
            new UniqueDef("unique.hd_rear_surge", "head.star_diadem", new Txt("後衛の奥義冠", "Rearguard Ultimate Diadem"),
                new Txt("最も遠い星が、最も大きな奥義を放つ。", "The farthest star loosens the greatest ultimate."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_hd_rear_surge_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_hd_rear_surge_Power1),
            new UniqueDef("unique.hd_luck_executioner", "head.hunter_hood", new Txt("積み運の処刑頭巾", "Pileluck Executioner Hood"),
                new Txt("外した数だけ、いつか処刑の刃が増える。", "Each miss adds one blade to the eventual execution."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_hd_luck_executioner_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hd_luck_executioner_Power1),
            new UniqueDef("unique.hd_luck_radiance", "head.noonlight_circlet", new Txt("運の光冠", "Luck Radiance Circlet"),
                new Txt("運を溜めた光は、必ず会心になる。", "Light that has saved up luck always crits."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_hd_luck_radiance_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_hd_luck_radiance_Power1),
            new UniqueDef("unique.hd_luck_echo", "head.hawkeye_band", new Txt("運め鷹の鉢巻", "Lucky Hawk Band"),
                new Txt("外した矢の数が、鷹の運の貯金。", "The arrows missed are the hawk's savings of luck."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_hd_luck_echo_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_hd_luck_echo_Power1),
            new UniqueDef("unique.hd_return_executioner", "head.thunderveil_hood", new Txt("戻る処刑刃の雷帳", "Returning Executioner Veil"),
                new Txt("倒した数だけ、技は早く戻る。", "The more you kill, the sooner your skills return."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_hd_return_executioner_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_hd_return_executioner_Power1),
            new UniqueDef("unique.hd_return_frost", "head.coral_crown", new Txt("戻り潮の珊瑚冠", "Returntide Coral Crown"),
                new Txt("仕留めた潮は、すぐに満ちて戻る。", "The tide that made the kill swiftly fills again."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_hd_return_frost_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_hd_return_frost_Power1),
            new UniqueDef("unique.hd_return_shatter", "head.vulture_hood", new Txt("戻り爆ぜの頭巾", "Returnburst Hood"),
                new Txt("倒して弾けて、技はもう戻っている。", "Kill, burst, and the skill is already back."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_hd_return_shatter_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_hd_return_shatter_Power1),
            new UniqueDef("unique.a_chainmend_sash", "armor.healing_sash", new Txt("連鎖を癒す帯", "Chainmend Sash"),
                new Txt("連鎖が走るたび、帯がほんのり温まる。", "With every chain that runs, the sash grows faintly warm."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_a_chainmend_sash_Power0, Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_a_chainmend_sash_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_R_ChainReaction" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_a_chainmend_sash_Link } },
            new UniqueDef("unique.a_distorted_robe", "armor.mirrorsilk_robe", new Txt("歪む鏡の法衣", "Warpedmirror Robe"),
                new Txt("歪んだ鏡の前で、不動の者は揺るがない。", "Before a warped mirror, the unmoving stay unshaken."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_distorted_robe_Power0, Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_a_distorted_robe_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_QR_DistortedMind" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_a_distorted_robe_Link } },
            new UniqueDef("unique.a_laceration_cloak", "armor.shadow_cloak", new Txt("裂傷の影外套", "Rendshade Cloak"),
                new Txt("囲まれた瞬間、影がひとつ多くなる。", "The moment you are surrounded, there is one more shadow."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_laceration_cloak_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_a_laceration_cloak_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_Q_Laceration" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_laceration_cloak_Link } },
            new UniqueDef("unique.a_handcannon_vest", "armor.hunter_vest", new Txt("砲撃手の胴衣", "Gunner's Vest"),
                new Txt("砲声のあとの静けさに、怨嗟の鐘が鳴る。", "In the quiet after the cannon, the bell of grudge tolls."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_handcannon_vest_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_a_handcannon_vest_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_Q_HandCannon" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_a_handcannon_vest_Link } },
            new UniqueDef("unique.a_unbroken_garb", "armor.dancer_garb", new Txt("折れぬ意志の舞衣", "Unbroken-Will Dancewear"),
                new Txt("待つ者の踊りは、静かで正確だ。", "The dance of one who waits is quiet and exact."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_unbroken_garb_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_a_unbroken_garb_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_R_UnbreakableDetermination" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_unbroken_garb_Link } },
            new UniqueDef("unique.a_sylvan_vest", "armor.summoners_vest", new Txt("森の呼応の胴衣", "Sylvan Call Vest"),
                new Txt("猟犬が倒れた場所に、森が小さな火を灯す。", "Where the hound falls, the forest lights a small fire."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_a_sylvan_vest_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_a_sylvan_vest_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_SylvanCall" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_sylvan_vest_Link } },
            new UniqueDef("unique.a_sanctuary_plate", "armor.guardian_plate", new Txt("聖域の前衛鎧", "Sanctuary Vanguard Plate"),
                new Txt("聖域の端に立つ者は、護りを分け与える。", "Whoever stands at the sanctuary's edge shares the ward."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_a_sanctuary_plate_Power0, Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_sanctuary_plate_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_R_SanctuaryOfEl" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_sanctuary_plate_Link } },
            new UniqueDef("unique.a_supernova_cloak", "armor.star_cloak", new Txt("超新星の後衛外套", "Supernova Rearguard Cloak"),
                new Txt("星が爆ぜるとき、いちばん遠くが最も明るい。", "When a star bursts, the farthest point is brightest."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_a_supernova_cloak_Power0, Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_a_supernova_cloak_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_Q_SuperNova" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_supernova_cloak_Link } },
            new UniqueDef("unique.a_hairline_cuirass", "armor.oathplate_cuirass", new Txt("薄皮の守り胸当て", "Hairline Cuirass"),
                new Txt("一枚の護りだけで、すべてを受け止める。", "It takes everything on a single ward."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_hairline_cuirass_Power0, Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_hairline_cuirass_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_hairline_cuirass_Link } },
            new UniqueDef("unique.a_wary_ply", "armor.twistedchain_ply", new Txt("疑い深い捻れ鎖", "Wary Twistchain"),
                new Txt("疑いが三重に巻かれた鎖は、解けにくい。", "A chain wound thrice with suspicion is hard to undo."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_wary_ply_Power0, Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_wary_ply_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia", "Gem_U_SoulPrison" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_wary_ply_Link } },
            new UniqueDef("unique.a_faith_shawl", "armor.prayer_shawl", new Txt("信仰の見守り肩掛け", "Faithwatch Shawl"),
                new Txt("祈りは、遠くの誰かを温める。", "Prayer warms someone far away."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_a_faith_shawl_Power0, Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_faith_shawl_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_faith_shawl_Link } },
            new UniqueDef("unique.a_glaciercore_coat", "armor.winterwool_coat", new Txt("氷河核の冬羊毛", "Glacier-Core Winterwool"),
                new Txt("氷河の芯に抱かれて、盾は割れても冷たい。", "Held at a glacier's core, even a broken shield stays cold."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_glaciercore_coat_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_a_glaciercore_coat_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GlacialCore", "Hero_Cetus" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_glaciercore_coat_Link } },
            new UniqueDef("unique.a_blizzard_robe", "armor.frost_robe", new Txt("吹雪の不動法衣", "Blizzard Stillrobe"),
                new Txt("吹雪の中で動かぬ者は、雪像になる。", "One who stays still in a blizzard becomes a snow figure."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_blizzard_robe_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_a_blizzard_robe_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_blizzard_robe_Link } },
            new UniqueDef("unique.a_return_shell", "armor.bastion_shell", new Txt("帰還の殻", "Shell of Return"),
                new Txt("殻の中で一度だけ、命が戻ってくる。", "Within the shell, life returns once."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_a_return_shell_Power0, Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_return_shell_Power1) { Link = new LinkDef { Requires = new[] { "St_L_HerosReturn", "Gem_L_Perfect" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_return_shell_Link } },
            new UniqueDef("unique.a_burrow_mail", "armor.bark_mail", new Txt("潜り屋の樹皮鎧", "Burrower's Barkmail"),
                new Txt("木は動かない。動かないから、強い。", "A tree does not move; that is why it is strong."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_burrow_mail_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_a_burrow_mail_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Burrow" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_burrow_mail_Link } },
            new UniqueDef("unique.a_suneye_plate", "armor.sun_plate", new Txt("太陽眼の胸当て", "Suneye Plate"),
                new Txt("燃える眼に見つめられた鐘は、一度だけ鳴る。", "A bell under a burning eye tolls only once."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_suneye_plate_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_a_suneye_plate_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_SolarEye", "St_R_BaptismOfSun" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_suneye_plate_Link } },
            new UniqueDef("unique.a_moltencore_plate", "armor.ember_plate", new Txt("炉心の竜鎧", "Furnace Drake Plate"),
                new Txt("鎧の継ぎ目から、小さな竜が散ってゆく。", "Tiny drakes scatter from the seams of the armor."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_a_moltencore_plate_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_a_moltencore_plate_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SmallMoltenCore" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_moltencore_plate_Link } },
            new UniqueDef("unique.a_cook_coat", "armor.traveler_coat", new Txt("旅の料理人の外套", "Wayfaring Cook's Coat"),
                new Txt("旅の一食が、いちばん効く薬になる。", "A single meal on the road is the best medicine."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_a_cook_coat_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_a_cook_coat_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Culinary", "Hero_Nachia" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_a_cook_coat_Link } },
            new UniqueDef("unique.a_unfettered_vest", "armor.stormfront_vest", new Txt("枷なき嵐の胴衣", "Unfettered Stormvest"),
                new Txt("枷を外した後に、囲みを抜けて走る。", "Having shed the shackles, run through the encirclement."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_unfettered_vest_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_a_unfettered_vest_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty", "Hero_Husk" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_a_unfettered_vest_Link } },
            new UniqueDef("unique.a_gift_mail", "armor.tidebound_mail", new Txt("贈り物の潮鎖", "Giftwave Mail"),
                new Txt("贈り物を受け取ったら、夢は少し先に動く。", "When a gift is received, the dream moves a little ahead."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_a_gift_mail_Power0, Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_a_gift_mail_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_CamillasGift", "Gem_L_Perfect" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_gift_mail_Link } },
            new UniqueDef("unique.a_herworld_mantle", "armor.star_mantle", new Txt("彼方の世界の肩衣", "Mantle of the Other World"),
                new Txt("穴の淵に立つ者は、最も遠くから見られる。", "One who stands at the hole's rim is watched from farthest away."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_a_herworld_mantle_Power0, Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_herworld_mantle_Power1) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "Gem_U_LastStarlight" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_a_herworld_mantle_Link } },
            new UniqueDef("unique.a_fireball_coat", "armor.volcanic_coat", new Txt("火球化身の山外套", "Fireball Avatar Coat"),
                new Txt("火球に変わる前に、護りが先に弾ける。", "The ward bursts before the transformation into a fireball."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_fireball_coat_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_a_fireball_coat_Power1) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "Hero_Lacerta" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_a_fireball_coat_Link } },
            new UniqueDef("unique.a_metalcrystal_robe", "armor.mirrorsilk_robe", new Txt("金属結晶の法衣", "Metalcrystal Robe"),
                new Txt("この世ならざる色の護りを、近くの者にも。", "Share a ward of an unearthly color with those nearby."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_metalcrystal_robe_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_a_metalcrystal_robe_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_MetalCrystal" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_metalcrystal_robe_Link } },
            new UniqueDef("unique.a_balance_corset", "armor.dawnlight_corset", new Txt("均衡の曙衣", "Equilibrium Dawnbodice"),
                new Txt("傾いた光を、近くの仲間に分け与える。", "Share the tilted light with friends nearby."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_a_balance_corset_Power0, Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_balance_corset_Power1) { Link = new LinkDef { Requires = new[] { "St_U_BeamOfBalance", "Hero_Vesper" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_balance_corset_Link } },
            new UniqueDef("unique.a_radiantburst_robe", "armor.candlelight_robe", new Txt("輝爆の灯衣", "Radiantburst Candlerobe"),
                new Txt("光が弾ける場所に、仲間を見守る目がある。", "An eye watches over friends where the light bursts."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_radiantburst_robe_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_a_radiantburst_robe_Power1) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion", "Hero_Aurena" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_radiantburst_robe_Link } },
            new UniqueDef("unique.a_glacier_harness_bd", "armor.glacier_harness", new Txt("氷河と下がれの胴当て", "Glacier-Backoff Harness"),
                new Txt("下がれと言う者は、最前列で盾を割る。", "The one who says \"back off\" breaks shields in the front row."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_glacier_harness_bd_Power0, Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_glacier_harness_bd_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_D_IcyVeins", "St_R_BackOff" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_glacier_harness_bd_Link } },
            new UniqueDef("unique.a_parry_coat", "armor.skirmisher_coat", new Txt("受け返しの遊撃衣", "Skirmisher's Riposte Coat"),
                new Txt("受けた痛みを、鐘の音に変えて返す。", "Pain taken is returned in the toll of a bell."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_parry_coat_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_a_parry_coat_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_R_Parry", "St_Q_Fleche" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_parry_coat_Link } },
            new UniqueDef("unique.a_whisper_vest", "armor.wardsigil_vest", new Txt("囁きの護符胴衣", "Whispering Sigil Vest"),
                new Txt("囁くだけで、群れは護りの形に並ぶ。", "A whisper alone arranges the pack into a ward."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_whisper_vest_Power0, Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_a_whisper_vest_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_R_NaturesWhisper", "St_Q_SylvanCall" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_a_whisper_vest_Link } },
            new UniqueDef("unique.bond_wall_and_shadow", "armor.ironbark_vest", new Txt("壁と影の胴衣", "Wall-and-Shadow Vest"),
                new Txt("壁が受け止め、影が刺す。胴衣は二人分。", "The wall absorbs, the shadow stabs. A vest for two."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_bond_wall_and_shadow_Power0, Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_bond_wall_and_shadow_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "Hero_Husk" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_wall_and_shadow_Link } },
            new UniqueDef("unique.bond_hammer_and_blade", "armor.citadel_plate", new Txt("槌と刃の胸甲", "Hammer-and-Blade Plate"),
                new Txt("槌が押し、刃が受け返す。", "The hammer pushes; the blade ripostes."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_bond_hammer_and_blade_Power0, Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_bond_hammer_and_blade_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "Hero_Mist" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_hammer_and_blade_Link } },
            new UniqueDef("unique.bond_healer_and_gun", "armor.healing_sash", new Txt("癒し手と銃の結び帯", "Healer-and-Gun Sash"),
                new Txt("撃つ手と癒す手が、互いの背を覚えている。", "The shooting hand and the healing hand remember each other's back."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_bond_healer_and_gun_Power0, Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_bond_healer_and_gun_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "Hero_Lacerta" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_healer_and_gun_Link } },
            new UniqueDef("unique.bond_pack_and_star", "armor.zephyr_robe", new Txt("群れと星の法衣", "Pack-and-Star Robe"),
                new Txt("星が遠くから、群れの背を見守っている。", "From afar, a star watches over the pack's backs."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_bond_pack_and_star_Power0, Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_bond_pack_and_star_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "Hero_Yubar" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_pack_and_star_Link } },
            new UniqueDef("unique.a_wardsplinter_mail", "armor.chain_hauberk", new Txt("割れ盾の鎖帷子", "Wardsplinter Hauberk"),
                new Txt("護りが砕けた場所に、破片の嵐が起こる。", "A storm of fragments rises where the ward broke."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_wardsplinter_mail_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_wardsplinter_mail_Power1),
            new UniqueDef("unique.a_breakshield_plate", "armor.spiked_plate", new Txt("砕盾の棘板", "Shieldbreaker Spikeplate"),
                new Txt("割れた護りの破片が、棘の仲間になる。", "Fragments of the broken ward join the thorns."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_breakshield_plate_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_a_breakshield_plate_Power1),
            new UniqueDef("unique.a_burst_coat", "armor.rampart_jacket", new Txt("護り弾けの土塁上着", "Wardburst Rampart Jacket"),
                new Txt("土塁が崩れるとき、土煙の中で反撃が始まる。", "When the rampart falls, the counterattack begins in the dust."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_burst_coat_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_burst_coat_Power1),
            new UniqueDef("unique.a_shatterstar_robe", "armor.zephyr_robe", new Txt("砕星の法衣", "Shatterstar Robe"),
                new Txt("星の護りが砕け、流れ星のように降り注ぐ。", "The star's ward breaks and falls like meteors."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_shatterstar_robe_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_a_shatterstar_robe_Power1),
            new UniqueDef("unique.a_ironbreak_vest", "armor.ironbark_vest", new Txt("鉄樹割りの胴衣", "Ironbark-Breaker Vest"),
                new Txt("固い木ほど、割れるときに派手に弾ける。", "The harder the wood, the more spectacularly it bursts."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_ironbreak_vest_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_a_ironbreak_vest_Power1),
            new UniqueDef("unique.a_sharedward_robe", "armor.prayer_shawl", new Txt("分かち合う護りの肩衣", "Sharedward Shawl"),
                new Txt("護りは、隣の肩に掛け直すとよく温まる。", "A ward warms best when draped over a neighbor's shoulders."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_robe_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_robe_Power1),
            new UniqueDef("unique.a_sharedward_star", "armor.candlelight_robe", new Txt("星を分け合う灯衣", "Starshare Candlerobe"),
                new Txt("分けた星は、二つの夜を照らす。", "A shared star lights two nights."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_star_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_star_Power1),
            new UniqueDef("unique.a_sharedward_aegis", "armor.healing_sash", new Txt("護りを頒ける帯", "Wardlending Sash"),
                new Txt("大きな一撃の護りを、半分だけ隣へ。", "Half the ward against a heavy blow goes to the neighbor."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_aegis_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_aegis_Power1),
            new UniqueDef("unique.a_sharedward_bulwark", "armor.bastion_shell", new Txt("頒け合う殻", "Shared Shell"),
                new Txt("硬い殻の下に、もう一人入れる広さがある。", "Under the hard shell there is room for one more."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_bulwark_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_a_sharedward_bulwark_Power1),
            new UniqueDef("unique.a_triumph_robe", "armor.dawnlight_corset", new Txt("凱歌の曙衣", "Triumph Dawnbodice"),
                new Txt("奥義の歌が、隣の仲間の傷を撫でる。", "The ultimate's song strokes the wound of the friend beside you."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_a_triumph_robe_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_a_triumph_robe_Power1),
            new UniqueDef("unique.a_triumph_mantle", "armor.lampkeeper_mantle", new Txt("凱旋の灯守外套", "Triumph Lampkeeper's Mantle"),
                new Txt("歌い終えた灯守は、少しだけ元気になる。", "The lampkeeper, song done, feels a little renewed."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_a_triumph_mantle_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_a_triumph_mantle_Power1),
            new UniqueDef("unique.a_triumph_plate", "armor.sun_plate", new Txt("凱歌の陽鎧", "Triumph Sunplate"),
                new Txt("太陽の歌は、近くの者ほど温める。", "The sun's song warms those nearest."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_a_triumph_plate_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_a_triumph_plate_Power1),
            new UniqueDef("unique.a_watch_cloak", "armor.morningdew_robe", new Txt("見張りの朝露衣", "Dewwatch Robe"),
                new Txt("倒れかけた仲間に、露の盾が降りる。", "A dew shield falls on a friend about to fall."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_watch_cloak_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_watch_cloak_Power1),
            new UniqueDef("unique.a_watch_hauberk", "armor.bark_mail", new Txt("見守りの樹皮鎧", "Watchwood Barkmail"),
                new Txt("樹皮の鎧は、仲間の肩にも枝を伸ばす。", "Barkmail stretches its branches to a friend's shoulder."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_watch_hauberk_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_watch_hauberk_Power1),
            new UniqueDef("unique.a_watch_mantle", "armor.lampkeeper_mantle", new Txt("灯守の見守り外套", "Lampkeeper's Watchmantle"),
                new Txt("仲間のHPが細い灯になったら、この外套が覆う。", "When a friend's health dwindles to a thin flame, this mantle covers it."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_watch_mantle_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_a_watch_mantle_Power1),
            new UniqueDef("unique.a_watch_deeproot", "armor.deeproot_vest", new Txt("見守り根張りの胴衣", "Watchroot Vest"),
                new Txt("根は静かに、仲間の足元まで届いている。", "The roots reach quietly to the friend's feet."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_a_watch_deeproot_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_a_watch_deeproot_Power1),
            new UniqueDef("unique.a_breakout_coat", "armor.stormfront_vest", new Txt("突破の嵐衣", "Breakthrough Stormcoat"),
                new Txt("囲まれたら、嵐の目になればいい。", "When surrounded, become the eye of the storm."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_breakout_coat_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_a_breakout_coat_Power1),
            new UniqueDef("unique.a_breakout_plate", "armor.citadel_plate", new Txt("突破の砦鎧", "Breakthrough Citadel Plate"),
                new Txt("砦の扉を、内側から蹴破る。", "Kick open the citadel's gate from within."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_breakout_plate_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_a_breakout_plate_Power1),
            new UniqueDef("unique.a_breakout_hide", "armor.buckler_vest", new Txt("丸盾の突破衣", "Bucklerbreak Vest"),
                new Txt("小さな盾でも、群れを割って進める。", "Even a small shield can split the crowd."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_breakout_hide_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_breakout_hide_Power1),
            new UniqueDef("unique.a_stance_plate", "armor.basalt_cuirass", new Txt("玄武の不動胸甲", "Basalt Stillplate"),
                new Txt("山は動かない。だから、山が一番強い。", "A mountain does not move; that is why it is strongest."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_stance_plate_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_a_stance_plate_Power1),
            new UniqueDef("unique.a_stance_coat", "armor.skirmisher_coat", new Txt("立ち尽くす遊撃衣", "Standstill Skirmisher Coat"),
                new Txt("立ち止まったときだけ、衣が重くなる。", "The coat grows heavy only when you stand still."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_stance_coat_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_a_stance_coat_Power1),
            new UniqueDef("unique.a_stance_robe", "armor.aurora_wrap", new Txt("凪の極光衣", "Calm Aurora Wrap"),
                new Txt("風が止んだとき、極光は最も深く揺れる。", "When the wind stops, the aurora shimmers deepest."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_stance_robe_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_stance_robe_Power1),
            new UniqueDef("unique.a_stance_monk", "armor.monk_garb", new Txt("不動修行者の道着", "Stillmonk's Garb"),
                new Txt("動かない修行は、いちばん難しい。", "Training in stillness is the hardest."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_stance_monk_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_stance_monk_Power1),
            new UniqueDef("unique.a_vanguard_plate", "armor.oathplate_cuirass", new Txt("前衛の誓胸当て", "Vanguard's Oath Cuirass"),
                new Txt("最前列に立つ約束を、鎧の裏に刻む。", "The vow to stand in the front row is carved on the armor's inside."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_plate_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_plate_Power1),
            new UniqueDef("unique.a_vanguard_mail", "armor.tidebound_mail", new Txt("矢面の潮鎖", "Brunt Tidemail"),
                new Txt("最初に水を被る者が、最初に濡れない。", "The first to take the wave is the first to stay dry."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_mail_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_mail_Power1),
            new UniqueDef("unique.a_vanguard_bulwark", "armor.rampart_jacket", new Txt("先陣の鉄輪衣", "Frontline Ironring Coat"),
                new Txt("最前線ほど、鉄の輪は固く結ばれる。", "The closer to the front, the tighter the iron ring."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_bulwark_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_bulwark_Power1),
            new UniqueDef("unique.a_vanguard_thorns", "armor.thorn_mail", new Txt("先陣の棘鎧", "Vanguard Thornmail"),
                new Txt("先頭に立つ者に、棘は最も似合う。", "Thorns suit the one who leads."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_thorns_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_a_vanguard_thorns_Power1),
            new UniqueDef("unique.a_deathbloom_vest", "armor.summoners_vest", new Txt("死に花の召喚衣", "Deathbloom Summoner's Vest"),
                new Txt("倒れた獣の跡に、火の花が咲く。", "A flower of fire blooms where the beast fell."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_a_deathbloom_vest_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_a_deathbloom_vest_Power1),
            new UniqueDef("unique.a_deathbloom_hide", "armor.bark_mail", new Txt("散り花の樹皮鎧", "Scatterbloom Barkmail"),
                new Txt("樹皮の隙間から、散り際の花弁が飛ぶ。", "Dying petals fly from the cracks in the bark."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_a_deathbloom_hide_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_deathbloom_hide_Power1),
            new UniqueDef("unique.a_deathbloom_plate", "armor.ember_cuirass", new Txt("殉火の胴鎧", "Martyrflame Cuirass"),
                new Txt("最期の一回だけ、牙は爆ぜる。", "Only at the last moment does the fang burst."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_a_deathbloom_plate_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_a_deathbloom_plate_Power1),
            new UniqueDef("unique.a_omen_robe", "armor.nightloom_robe", new Txt("夢見の前兆の夜衣", "Dreamomen Nightrobe"),
                new Txt("夜が荒れる前の静けさを、衣が知っている。", "The robe knows the stillness before the night turns rough."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_a_omen_robe_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_omen_robe_Power1),
            new UniqueDef("unique.a_omen_shawl", "armor.traveler_coat", new Txt("予兆を読む旅外套", "Omenreader's Traveler Coat"),
                new Txt("旅人は、空の色から出来事を読む。", "A traveler reads events in the color of the sky."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_a_omen_shawl_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_a_omen_shawl_Power1),
            new UniqueDef("unique.a_omen_lucid_robe", "armor.ink_robe", new Txt("明晰な墨染め衣", "Lucid Inkdyed Robe"),
                new Txt("墨に映る夢は、いつもより少し先を見せる。", "The dream reflected in ink shows a little further ahead."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_a_omen_lucid_robe_Power0, Power.LucidBoon, EquipmentItemsBalanceValues.Unique_unique_a_omen_lucid_robe_Power1),
            new UniqueDef("unique.a_rear_robe", "armor.zephyr_robe", new Txt("後衛の風法衣", "Rearguard Zephyr Robe"),
                new Txt("いちばん遠い者に、風はいつも優しい。", "The wind is always kind to the farthest one."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_a_rear_robe_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_a_rear_robe_Power1),
            new UniqueDef("unique.a_rear_cloak", "armor.star_cloak", new Txt("遠星の外套", "Farstar Cloak"),
                new Txt("最も遠い星ほど、静かに燃えている。", "The farthest star burns most quietly."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_a_rear_cloak_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_a_rear_cloak_Power1),
            new UniqueDef("unique.a_rear_silk", "armor.moon_silk", new Txt("後衛の月絹", "Rearguard Moonsilk"),
                new Txt("遠くから、月のように見守る。", "Watch over them from afar, like the moon."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_a_rear_silk_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_a_rear_silk_Power1),
            new UniqueDef("unique.a_grudge_plate", "armor.citadel_plate", new Txt("怨嗟の砦胸甲", "Grudge Citadel Plate"),
                new Txt("蓄えた恨みは、砦の壁を厚くする。", "Stored-up grudge thickens the citadel wall."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_grudge_plate_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_a_grudge_plate_Power1),
            new UniqueDef("unique.a_toll_mail", "armor.chain_hauberk", new Txt("怨嗟の鐘鎖", "Grudgebell Hauberk"),
                new Txt("傷が積もるほど、鐘は大きく響く。", "The more wounds pile up, the louder the bell rings."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_toll_mail_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_toll_mail_Power1),
            new UniqueDef("unique.a_toll_coat", "armor.winterwool_coat", new Txt("凍え鐘の冬衣", "Frostbell Coat"),
                new Txt("冷えた怨嗟は、遠くまで響く。", "A chilled grudge carries far."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_toll_coat_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_toll_coat_Power1),
            new UniqueDef("unique.a_ready_plate", "armor.oathplate_cuirass", new Txt("待ち受けの誓い胸当て", "Waiting Oath Cuirass"),
                new Txt("休んだ時間は、そのまま護りの厚みになる。", "Rested time becomes the thickness of the ward."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_ready_plate_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_ready_plate_Power1),
            new UniqueDef("unique.a_ready_mail", "armor.rampart_jacket", new Txt("構え待ちの土塁衣", "Waiting Rampart Coat"),
                new Txt("戦う前に整えた者が、最初の矢を弾く。", "The one who prepared before battle deflects the first arrow."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_ready_mail_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_ready_mail_Power1),
            new UniqueDef("unique.a_ready_robe", "armor.wardsigil_vest", new Txt("備えの護符衣", "Prepared Sigil Vest"),
                new Txt("備えがあれば、憂いは最初の一撃だけ。", "With preparation, worry lasts only until the first blow."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_ready_robe_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_a_ready_robe_Power1),
            new UniqueDef("unique.a_apothecary_vest", "armor.healing_sash", new Txt("薬師の胴帯", "Apothecary Vest Sash"),
                new Txt("薬を飲む手は、技を整える手。", "The hand that drinks the potion is the hand that readies the skills."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_a_apothecary_vest_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_a_apothecary_vest_Power1),
            new UniqueDef("unique.a_apothecary_robe", "armor.morningdew_robe", new Txt("調合の朝露衣", "Compounding Dewrobe"),
                new Txt("朝露で割った薬は、よく効く。", "Medicine diluted with morning dew works well."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_a_apothecary_robe_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_a_apothecary_robe_Power1),
            new UniqueDef("unique.a_apothecary_coat", "armor.traveler_coat", new Txt("薬箱の旅外套", "Medicine-Chest Coat"),
                new Txt("薬箱の重さが、そのまま心強さ。", "The weight of the medicine chest is a comfort in itself."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_a_apothecary_coat_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_apothecary_coat_Power1),
            new UniqueDef("unique.a_standing_toll", "armor.monk_garb", new Txt("不動と怨嗟の道着", "Stillness-and-Grudge Garb"),
                new Txt("動かぬ修行者の中に、積もる鐘がある。", "Within the unmoving ascetic, a bell is accumulating."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_a_standing_toll_Power0, Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_a_standing_toll_Power1),
            new UniqueDef("unique.h_hypothesis", "hands.manuscript_gloves", new Txt("仮説の手袋", "Hypothesis Gloves"),
                new Txt("試してみなければ、理論はただ危険なだけ。", "Untested, a theory is merely dangerous."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_h_hypothesis_Power0, Power.Spellsweep, EquipmentItemsBalanceValues.Unique_unique_h_hypothesis_Power1) { Link = new LinkDef { Requires = new[] { "St_R_DangerousTheory" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_hypothesis_Link } },
            new UniqueDef("unique.h_chainbrew", "hands.alchemist_gloves", new Txt("連鎖調合の手袋", "Chain-Brew Gloves"),
                new Txt("一つ混ぜれば、隣の瓶も弾ける。", "Mix one, and the next vial bursts too."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_h_chainbrew_Power0, Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_h_chainbrew_Power1) { Link = new LinkDef { Requires = new[] { "St_R_ChainReaction" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_chainbrew_Link } },
            new UniqueDef("unique.h_pushback", "hands.wardens_grips", new Txt("退かせの握り", "Pushback Grips"),
                new Txt("前に出る者の背で、味方は息をつく。", "Allies catch their breath behind whoever steps forward."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_pushback_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_h_pushback_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_R_BackOff" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_h_pushback_Link } },
            new UniqueDef("unique.h_frostmanners", "hands.frostbite_knuckles", new Txt("礼儀正しい氷拳", "Courteous Frostfists"),
                new Txt("礼儀は、拳で教えるものだ。", "Manners are taught by the fist."),
                Power.BrittleIce, EquipmentItemsBalanceValues.Unique_unique_h_frostmanners_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_frostmanners_Power1) { Link = new LinkDef { Requires = new[] { "St_R_FrozenFists" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_frostmanners_Link } },
            new UniqueDef("unique.h_annihilation_fists", "hands.riven_fists", new Txt("殲滅構えの拳", "Annihilation Fists"),
                new Txt("構えを取っただけで、半数は退く。", "Half of them retreat at the mere stance."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_h_annihilation_fists_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_h_annihilation_fists_Power1) { Link = new LinkDef { Requires = new[] { "St_R_AnnihilationStance" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_annihilation_fists_Link } },
            new UniqueDef("unique.h_nightpalm_deception", "hands.nightpalm_gloves", new Txt("撹乱の夜掌", "Nightpalm of Deception"),
                new Txt("見えない手が、先に肩へ触れている。", "An unseen hand has already touched the shoulder."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_h_nightpalm_deception_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_h_nightpalm_deception_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_R_Deception" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_nightpalm_deception_Link } },
            new UniqueDef("unique.h_quickdraw", "hands.precise_fingerless", new Txt("早撃ちの指なし手袋", "Quickdraw Fingerless"),
                new Txt("抜く前に、勝負はもうついている。", "The duel is settled before the draw."),
                Power.CritSplash, EquipmentItemsBalanceValues.Unique_unique_h_quickdraw_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_h_quickdraw_Power1) { Link = new LinkDef { Requires = new[] { "St_R_QuickTrigger" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_h_quickdraw_Link } },
            new UniqueDef("unique.h_chargeshot", "hands.bowmaster_bracers", new Txt("溜め撃ちの腕当て", "Chargeshot Bracers"),
                new Txt("一息の静止が、百歩先を縮める。", "One held breath shortens a hundred paces."),
                Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_h_chargeshot_Power0, Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_h_chargeshot_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_R_PrecisionShot" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_chargeshot_Link } },
            new UniqueDef("unique.h_unbowed_gauntlets", "hands.ironvein_gauntlets", new Txt("折れぬ意志の籠手", "Unbowed Gauntlets"),
                new Txt("折れそうな瞬間ほど、剣先は真っ直ぐになる。", "The closer it is to breaking, the straighter the point."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_h_unbowed_gauntlets_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_h_unbowed_gauntlets_Power1) { Link = new LinkDef { Requires = new[] { "St_R_UnbreakableDetermination" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_h_unbowed_gauntlets_Link } },
            new UniqueDef("unique.h_riposte_gloves", "hands.duelist_gloves", new Txt("受け返しの手袋", "Riposte Gloves"),
                new Txt("受けた力は、そっくり相手へ贈り返す。", "Force received is gifted right back."),
                Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_h_riposte_gloves_Power0, Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_h_riposte_gloves_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_R_Parry" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_riposte_gloves_Link } },
            new UniqueDef("unique.h_whisperbind", "hands.summoner_bands", new Txt("囁き結びの腕輪", "Whisperbind Bands"),
                new Txt("囁きは、遠吠えよりよく届く。", "A whisper carries farther than a howl."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_h_whisperbind_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_h_whisperbind_Power1) { Link = new LinkDef { Requires = new[] { "St_R_NaturesWhisper" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_h_whisperbind_Link } },
            new UniqueDef("unique.h_serpent_rosary", "hands.prayer_beads", new Txt("蛇祝の数珠", "Serpent Rosary"),
                new Txt("巻きついた祝福は、解けるまで離れない。", "A coiled blessing holds on until it is unwound."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_h_serpent_rosary_Power0, Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_h_serpent_rosary_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_R_SerpentineBlessing" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_serpent_rosary_Link } },
            new UniqueDef("unique.h_sanctuary_wardens", "hands.oath_gauntlets", new Txt("聖域の守り手", "Sanctuary Wardens"),
                new Txt("線の内側では、誰も傷つかない。", "Within the line, no one comes to harm."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_sanctuary_wardens_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_h_sanctuary_wardens_Power1) { Link = new LinkDef { Requires = new[] { "St_R_SanctuaryOfEl" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_h_sanctuary_wardens_Link } },
            new UniqueDef("unique.h_cataclysm_rings", "hands.star_rings", new Txt("厄災の星環", "Cataclysm Rings"),
                new Txt("落ちてくる星にも、順番がある。", "Even falling stars wait their turn."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_h_cataclysm_rings_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_cataclysm_rings_Power1) { Link = new LinkDef { Requires = new[] { "St_R_Cataclysm" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_cataclysm_rings_Link } },
            new UniqueDef("unique.h_serene_fingertips", "hands.quick_fingers", new Txt("平静の指先", "Serene Fingertips"),
                new Txt("静かな者ほど、時間を借りるのがうまい。", "The calm are best at borrowing time."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_h_serene_fingertips_Power0, Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_h_serene_fingertips_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_R_Tranquility" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_h_serene_fingertips_Link } },
            new UniqueDef("unique.h_innocent_wraps", "hands.radiant_wraps", new Txt("無垢の光手", "Innocent Lightwraps"),
                new Txt("触れた光は、疑いもなく足を重くする。", "Light that touches slows the feet without a doubt."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_h_innocent_wraps_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_innocent_wraps_Power1) { Link = new LinkDef { Requires = new[] { "St_QR_Innocence" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_innocent_wraps_Link } },
            new UniqueDef("unique.h_valiant_stonefists", "hands.stone_fists", new Txt("勇敢な石拳", "Valiant Stonefists"),
                new Txt("退かない心は、押し返す腕に宿る。", "A heart that will not retreat lives in the arm that pushes back."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_valiant_stonefists_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_h_valiant_stonefists_Power1) { Link = new LinkDef { Requires = new[] { "St_QR_ValiantHeart" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_h_valiant_stonefists_Link } },
            new UniqueDef("unique.h_warped_claws", "hands.void_claws", new Txt("歪む精神の爪", "Warped-Mind Claws"),
                new Txt("歪んだ鏡は、真実より鋭い。", "A warped mirror cuts sharper than truth."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_h_warped_claws_Power0, Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_h_warped_claws_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_QR_DistortedMind" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_warped_claws_Link } },
            new UniqueDef("unique.h_gunner_bracers", "hands.sling_bracers", new Txt("砲撃手の腕当て", "Gunner's Bracers"),
                new Txt("大きな一発と、静かな一発。使い分ける手が、腕当てを選ぶ。", "One loud shot, one quiet shot; the arm that tells them apart picks these bracers."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_h_gunner_bracers_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_h_gunner_bracers_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_HandCannon", "St_R_PrecisionShot" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_gunner_bracers_Link } },
            new UniqueDef("unique.h_packcommand_bands", "hands.summoner_bands", new Txt("群れ指揮の腕輪", "Packcommand Bands"),
                new Txt("呼べば現れ、命じれば動く。群れは声の主を知っている。", "Called, it comes; commanded, it moves. The pack knows its caller's voice."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_h_packcommand_bands_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_h_packcommand_bands_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_SylvanCall", "St_R_NaturesWhisper" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_h_packcommand_bands_Link } },
            new UniqueDef("unique.h_sunhammer_gauntlets", "hands.ember_gauntlets", new Txt("陽槌の籠手", "Sunhammer Gauntlets"),
                new Txt("叩き落とす太陽と、受け止める太陽。", "A sun to strike down and a sun to catch the blow."),
                Power.Ember, EquipmentItemsBalanceValues.Unique_unique_h_sunhammer_gauntlets_Power0, Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_sunhammer_gauntlets_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_CruelSun", "St_R_BaptismOfSun" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_sunhammer_gauntlets_Link } },
            new UniqueDef("unique.h_markshadow_gloves", "hands.shadow_gloves", new Txt("印影の手袋", "Markshadow Gloves"),
                new Txt("印を付けた影が、跳んだ先で待っている。", "The marked shadow waits where you will land."),
                Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_h_markshadow_gloves_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_h_markshadow_gloves_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_DeathMark", "St_R_Deception" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_markshadow_gloves_Link } },
            new UniqueDef("unique.h_lunge_riposte", "hands.duelist_bracers", new Txt("突き返しの腕当て", "Thrust-Riposte Bracers"),
                new Txt("踏み込みの一歩と、受け返しの一歩。", "One step to lunge, one step to answer."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_h_lunge_riposte_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_h_lunge_riposte_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Lunge", "St_R_Parry" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_h_lunge_riposte_Link } },
            new UniqueDef("unique.h_return_palms", "hands.healer_hands", new Txt("返し癒しの手", "Returning Palms"),
                new Txt("敵から奪い、味方に渡し、少しだけ自分も受け取る。", "Take from the foe, give to the friend, keep a little for yourself."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_h_return_palms_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_h_return_palms_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_Reduction", "St_R_DangerousTheory" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_return_palms_Link } },
            new UniqueDef("unique.h_novastar_gloves", "hands.spell_gloves", new Txt("星爆ぜの手袋", "Starburst Gloves"),
                new Txt("超新星のあとには、必ず新しい元素が生まれる。", "After every supernova, a new element is born."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_h_novastar_gloves_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_novastar_gloves_Power1) { Link = new LinkDef { Requires = new[] { "St_Q_SuperNova", "St_R_Cataclysm" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_novastar_gloves_Link } },
            new UniqueDef("unique.h_cinder_tales", "hands.blazeknit_gloves", new Txt("熾火語りの手袋", "Cindertale Gloves"),
                new Txt("燃やしたものの影は、まだ熱を持っている。", "The shadow of what was burned still holds the heat."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_h_cinder_tales_Power0, Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_h_cinder_tales_Power1) { Link = new LinkDef { Requires = new[] { "St_QR_InfernalTales", "St_QR_DistortedMind" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_cinder_tales_Link } },
            new UniqueDef("unique.h_savage_gauntlets", "hands.claw_gauntlets", new Txt("切り裂く二連の手甲", "Double-Rend Gauntlets"),
                new Txt("裂いて、構えて、仕留める。順序を違えるな。", "Tear, set, finish. Never change the order."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_h_savage_gauntlets_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_h_savage_gauntlets_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_Q_Laceration", "St_R_AnnihilationStance" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_savage_gauntlets_Link } },
            new UniqueDef("unique.h_icyveins_bracers", "hands.ice_bracers", new Txt("氷脈の腕当て", "Veinfrost Bracers"),
                new Txt("血管の中まで凍らせた者は、岩よりも動かない。", "One frozen to the veins moves less than rock."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_icyveins_bracers_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_icyveins_bracers_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_D_IcyVeins", "St_Q_BigBorealChunk" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_h_icyveins_bracers_Link } },
            new UniqueDef("unique.h_burnstep_gloves", "hands.ripgrip_gloves", new Txt("焼け足の手袋", "Burnstep Gloves"),
                new Txt("火を撒いて、跳んで逃げる。それも立派な戦法だ。", "Sow fire, then leap away; that too is a proper tactic."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_h_burnstep_gloves_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_h_burnstep_gloves_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_Q_IncendiaryRounds", "St_M_NimbleDodge" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_h_burnstep_gloves_Link } },
            new UniqueDef("unique.h_moonfang_knuckles", "hands.bark_knuckles", new Txt("月牙の拳", "Moonfang Knuckles"),
                new Txt("月が満ちたら、群れの一頭が前に出る。", "When the moon is full, one of the pack steps forward."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_h_moonfang_knuckles_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_h_moonfang_knuckles_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_MoonlightPact", "St_R_NaturesWhisper" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_h_moonfang_knuckles_Link } },
            new UniqueDef("unique.h_banquet_mitts", "hands.chain_wraps", new Txt("宴の手袋", "Banquet Mitts"),
                new Txt("食材と黄金は、同じ皿の上で輝く。", "Ingredients and gold shine on the same plate."),
                Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_h_banquet_mitts_Power0, Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_h_banquet_mitts_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Culinary", "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_h_banquet_mitts_Link } },
            new UniqueDef("unique.h_giftbearer_gloves", "hands.oathpalm_gloves", new Txt("贈り物の手袋", "Giftbearer's Gloves"),
                new Txt("贈り物は、いつも少しだけ重い。", "A gift is always a little heavy."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_h_giftbearer_gloves_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_h_giftbearer_gloves_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_CamillasGift", "Gem_L_DivineFaith" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_h_giftbearer_gloves_Link } },
            new UniqueDef("unique.h_glaciercore_mitts", "hands.frost_mitts", new Txt("氷河核の手袋", "Glacial Core Mitts"),
                new Txt("吹雪の中心で、手のひらだけが温かい。", "At the heart of the blizzard, only the palms stay warm."),
                Power.BrittleIce, EquipmentItemsBalanceValues.Unique_unique_h_glaciercore_mitts_Power0, Power.Fetters, EquipmentItemsBalanceValues.Unique_unique_h_glaciercore_mitts_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GlacialCore", "St_L_Blizzard" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_h_glaciercore_mitts_Link } },
            new UniqueDef("unique.h_prisoner_grips", "hands.heavypalm_gloves", new Txt("牢獄越えの握り", "Prison-Breaker Grips"),
                new Txt("一度だけ許された無敵を、腕力で延ばす。", "Strength stretches the single grant of invulnerability."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_prisoner_grips_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_h_prisoner_grips_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_SoulPrison", "Gem_L_Supersymmetry" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_h_prisoner_grips_Link } },
            new UniqueDef("unique.bond_healers_knot", "hands.mender_palms", new Txt("癒し手の結び", "Healers' Knot"),
                new Txt("二人で結べば、ほどけにくい。", "A knot tied by two is hard to undo."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_bond_healers_knot_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_bond_healers_knot_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "Hero_Nachia" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_healers_knot_Link } },
            new UniqueDef("unique.bond_front_rear_wraps", "hands.bulwark_wraps", new Txt("前衛と後衛の手巻き", "Front-and-Rear Wraps"),
                new Txt("一人が受け、一人が撃つ。手は互いを知っている。", "One takes the hit, one fires. The hands know each other."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_bond_front_rear_wraps_Power0, Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_bond_front_rear_wraps_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "Hero_Yubar" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_front_rear_wraps_Link } },
            new UniqueDef("unique.h_stardust_fingers", "hands.lantern_fingerless", new Txt("星屑めぐりの指", "Stardust Cycle Fingers"),
                new Txt("光を五つ集めれば、時間が少しだけ戻る。", "Gather five lights, and time rewinds a little."),
                Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_h_stardust_fingers_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_stardust_fingers_Power1),
            new UniqueDef("unique.h_shadowheir_claws", "hands.viperfang_claws", new Txt("影継ぎの爪", "Shadow-Heir Claws"),
                new Txt("倒れた影は、隣の影に宿る。", "A fallen shadow takes root in its neighbor."),
                Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_h_shadowheir_claws_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_h_shadowheir_claws_Power1),
            new UniqueDef("unique.h_fourfold_wraps", "hands.tidecaller_wraps", new Txt("四元刈りの手巻き", "Fourfold Reaper Wraps"),
                new Txt("色の多い獲物ほど、実りは大きい。", "The more colors on the prey, the richer the harvest."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_h_fourfold_wraps_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_fourfold_wraps_Power1),
            new UniqueDef("unique.h_prism_gloves_fire", "hands.cinderthread_wraps", new Txt("切り替えの七彩手袋", "Prism-Shift Gloves"),
                new Txt("色を変えるたび、光の殻が一つ閉じる。", "Each change of color closes one shell of light."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_fire_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_fire_Power1),
            new UniqueDef("unique.h_prism_gloves_frost", "hands.tide_gloves", new Txt("七彩の水掻き手袋", "Prismatic Tide Gloves"),
                new Txt("寄せる波ごとに、水の色が変わる。", "The water changes color with every wave that comes."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_frost_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_frost_Power1),
            new UniqueDef("unique.h_prism_gloves_light", "hands.chime_bracers", new Txt("変色の鈴腕輪", "Huechime Bracers"),
                new Txt("鈴の音が変わるたび、光も色を変える。", "When the chime changes pitch, the light changes hue."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_light_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_light_Power1),
            new UniqueDef("unique.h_prism_gloves_dark", "hands.thief_gloves", new Txt("化かしの手袋", "Shapeshifter's Gloves"),
                new Txt("盗むのは物ではなく、相手の目だ。", "What it steals is not goods but the opponent's eyes."),
                Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_dark_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_h_prism_gloves_dark_Power1),
            new UniqueDef("unique.h_steam_alchemist", "hands.alchemist_gloves", new Txt("蒸留者の手袋", "Distiller's Gloves"),
                new Txt("混ぜた属性は、蒸して初めて役に立つ。", "Mixed elements become useful only once steamed."),
                Power.Steam, EquipmentItemsBalanceValues.Unique_unique_h_steam_alchemist_Power0, Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_h_steam_alchemist_Power1),
            new UniqueDef("unique.h_eclipse_stardust", "hands.star_rings", new Txt("欠け星の指環", "Chipped Star Rings"),
                new Txt("星の光が、欠けるたびに強くなる。", "The star's light grows stronger with every chip."),
                Power.Eclipse, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_stardust_Power0, Power.StardustCycle, EquipmentItemsBalanceValues.Unique_unique_h_eclipse_stardust_Power1),
            new UniqueDef("unique.h_cinder_harvest", "hands.cinderthread_wraps", new Txt("灰集めの手巻き", "Ashgatherer Wraps"),
                new Txt("焼け跡には、拾う価値のある熱が残る。", "Burnt ground holds heat worth gathering."),
                Power.Cinder, EquipmentItemsBalanceValues.Unique_unique_h_cinder_harvest_Power0, Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_h_cinder_harvest_Power1),
            new UniqueDef("unique.h_icicrystal_prism", "hands.star_rings", new Txt("七彩氷の指環", "Prism-Ice Rings"),
                new Txt("氷の中の虹は、色を変えるたびに盾になる。", "The rainbow in the ice becomes a shield at every change of color."),
                Power.FrostCrystal, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_prism_Power0, Power.PrismShift, EquipmentItemsBalanceValues.Unique_unique_h_icicrystal_prism_Power1),
            new UniqueDef("unique.h_brittle_fingers", "hands.precise_fingerless", new Txt("砕け霜の指", "Frostshatter Fingers"),
                new Txt("凍った敵に会心を当てれば、割れる音が二度する。", "A crit on a frozen foe cracks twice."),
                Power.BrittleIce, EquipmentItemsBalanceValues.Unique_unique_h_brittle_fingers_Power0, Power.CriticalEcho, EquipmentItemsBalanceValues.Unique_unique_h_brittle_fingers_Power1),
            new UniqueDef("unique.h_luck_wraps", "hands.hunting_sinew", new Txt("積もり運の腱巻き", "Piled-Fortune Sinew Wraps"),
                new Txt("外れの数だけ、次の当たりが太くなる。", "Each miss fattens the next hit."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_h_luck_wraps_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_h_luck_wraps_Power1),
            new UniqueDef("unique.h_lightluck_gloves", "hands.thief_gloves", new Txt("盗み運の手袋", "Pilfered Luck Gloves"),
                new Txt("他人の運は、手に馴染むのが早い。", "Someone else's luck settles into the hand fast."),
                Power.PilingLuck, EquipmentItemsBalanceValues.Unique_unique_h_lightluck_gloves_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_h_lightluck_gloves_Power1),
            new UniqueDef("unique.h_wound_claws", "hands.void_claws", new Txt("深傷の爪", "Deepwound Claws"),
                new Txt("同じ傷を三度抉れば、闇が染み込む。", "Gouge the same wound thrice, and darkness seeps in."),
                Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_h_wound_claws_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_h_wound_claws_Power1),
            new UniqueDef("unique.h_bleedpoint_bracers", "hands.archer_bracers", new Txt("急所探りの腕当て", "Weakpoint Bracers"),
                new Txt("弱点は、同じ場所を三度試すものだ。", "A weak point is something you test three times in the same place."),
                Power.WeakPointWound, EquipmentItemsBalanceValues.Unique_unique_h_bleedpoint_bracers_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_h_bleedpoint_bracers_Power1),
            new UniqueDef("unique.h_splash_gauntlets", "hands.storm_knuckles", new Txt("雷の飛沫拳", "Stormsplash Knuckles"),
                new Txt("会心の稲妻は、隣の敵にも跳ねる。", "A critical bolt jumps to the neighboring foe."),
                Power.CritSplash, EquipmentItemsBalanceValues.Unique_unique_h_splash_gauntlets_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_h_splash_gauntlets_Power1),
            new UniqueDef("unique.h_splash_fists", "hands.bone_knuckles", new Txt("砕け骨の飛沫拳", "Bonesplinter Knuckles"),
                new Txt("砕けた破片は、近くの者を傷つける。", "The splinters wound whoever stands near."),
                Power.CritSplash, EquipmentItemsBalanceValues.Unique_unique_h_splash_fists_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_h_splash_fists_Power1),
            new UniqueDef("unique.h_soulreturn_palms", "hands.monk_wraps", new Txt("魂返しの手巻き", "Soulreturn Wraps"),
                new Txt("仲間を癒した手は、少し温まる。", "Hands that healed a friend grow a little warm."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_h_soulreturn_palms_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_h_soulreturn_palms_Power1),
            new UniqueDef("unique.h_mercy_beads", "hands.prayer_beads", new Txt("慈悲めぐりの数珠", "Mercy Rotation Beads"),
                new Txt("与えた慈悲は、巡って自分へ。", "Mercy given comes round to the giver."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_h_mercy_beads_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_h_mercy_beads_Power1),
            new UniqueDef("unique.h_relay_gloves", "hands.swiftpalm_gloves", new Txt("継ぎ走りの手袋", "Relay Gloves"),
                new Txt("倒した勢いで、次の走者の背を押す。", "The momentum of a kill pushes the next runner's back."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_h_relay_gloves_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_h_relay_gloves_Power1),
            new UniqueDef("unique.h_handoff_wraps", "hands.healer_hands", new Txt("手渡しの癒し手", "Handoff Healers' Palms"),
                new Txt("一度握り、一度渡し、二度は離さない。", "Grip once, hand off once, never let go twice."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_h_handoff_wraps_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_h_handoff_wraps_Power1),
            new UniqueDef("unique.h_apothecary_gloves", "hands.alchemist_gloves", new Txt("薬匙の手袋", "Apothecary Gloves"),
                new Txt("薬を飲む時間が、技を整える時間になる。", "The time spent drinking a potion becomes time spent readying a skill."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_h_apothecary_gloves_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_h_apothecary_gloves_Power1),
            new UniqueDef("unique.h_potion_wraps", "hands.mender_palms", new Txt("調剤師の手巻き", "Compounder's Wraps"),
                new Txt("瓶の数だけ、味方の顔がほころぶ。", "There are as many smiling faces as there are vials."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_h_potion_wraps_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_h_potion_wraps_Power1),
            new UniqueDef("unique.h_deathbloom_claws", "hands.claw_gauntlets", new Txt("散り際の爪牙", "Last-Gasp Claw Gauntlets"),
                new Txt("倒れる獣は、最後に一度だけ吠える。", "A falling beast gives one last howl."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_h_deathbloom_claws_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_h_deathbloom_claws_Power1),
            new UniqueDef("unique.h_gravebloom_wraps", "hands.rootgrip_gloves", new Txt("墓花の手袋", "Gravebloom Gloves"),
                new Txt("散った場所には、必ず花が咲く。", "Where it fell, a flower always blooms."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_h_gravebloom_wraps_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_h_gravebloom_wraps_Power1),
            new UniqueDef("unique.h_fleet_gloves", "hands.leather_gloves", new Txt("渡り手の革手袋", "Wanderer's Leather Gloves"),
                new Txt("昨日の敵に拘らず、今日の敵を撃て。", "Do not cling to yesterday's foe; strike today's."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_h_fleet_gloves_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_h_fleet_gloves_Power1),
            new UniqueDef("unique.h_focus_fists", "hands.iron_gauntlets", new Txt("集中打ちの鉄拳", "Focus-Blow Ironfists"),
                new Txt("五度叩けば、鉄でも凹む。", "Strike five times and even iron dents."),
                Power.FocusFire, EquipmentItemsBalanceValues.Unique_unique_h_focus_fists_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_h_focus_fists_Power1),
            new UniqueDef("unique.h_duel_gauntlets", "hands.oath_gauntlets", new Txt("決闘者の誓いの手甲", "Duelist's Oath Gauntlets"),
                new Txt("相手が一人なら、全力で礼を尽くす。", "When there is just one opponent, pay full respect."),
                Power.DuelistsWay, EquipmentItemsBalanceValues.Unique_unique_h_duel_gauntlets_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_h_duel_gauntlets_Power1),
            new UniqueDef("unique.h_runup_gloves", "hands.reach_bracers", new Txt("助走の腕当て", "Runup Bracers"),
                new Txt("助走の長さは、腕が覚えている。", "The arms remember the length of every run."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_h_runup_gloves_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_h_runup_gloves_Power1),
            new UniqueDef("unique.h_strafe_gloves", "hands.storm_knuckles", new Txt("歩き撃ちの拳", "Strafing Knuckles"),
                new Txt("止まらない的には、止まらない拳を。", "A moving target deserves a moving fist."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_h_strafe_gloves_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_h_strafe_gloves_Power1),
            new UniqueDef("unique.h_spellsweep_gloves", "hands.spell_gloves", new Txt("詠唱後の薙ぎ手", "Afterchant Sweepers"),
                new Txt("詠唱が終わった手は、まだ熱い。", "Hands that finish a chant are still hot."),
                Power.Spellsweep, EquipmentItemsBalanceValues.Unique_unique_h_spellsweep_gloves_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_h_spellsweep_gloves_Power1),
            new UniqueDef("unique.h_barefist_wraps", "hands.monk_wraps", new Txt("素手の誇りの手巻き", "Bare-Fist Pride Wraps"),
                new Txt("技が空いたなら、拳で埋める。", "When the skills are empty, fill the gap with fists."),
                Power.BareHandedPride, EquipmentItemsBalanceValues.Unique_unique_h_barefist_wraps_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_h_barefist_wraps_Power1),
            new UniqueDef("unique.h_pileon_gloves", "hands.quick_fingers", new Txt("畳み掛けの手袋", "Pile-On Gloves"),
                new Txt("同じ呪文は、三度目から本気になる。", "The same spell gets serious from the third cast."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_h_pileon_gloves_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_h_pileon_gloves_Power1),
            new UniqueDef("unique.h_returning_knuckles", "hands.bone_knuckles", new Txt("戻り刃の拳", "Returning Knuckles"),
                new Txt("倒した感触が、次の一撃を早める。", "The feel of a kill hastens the next strike."),
                Power.ReturningBlade, EquipmentItemsBalanceValues.Unique_unique_h_returning_knuckles_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_h_returning_knuckles_Power1),
            new UniqueDef("unique.h_spillover_fists", "hands.chain_wraps", new Txt("溢れ打ちの鎖拳", "Spillover Chain Fists"),
                new Txt("拳が余ったら、隣の誰かに。", "If the fist has strength left over, give it to the next one."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_h_spillover_fists_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_h_spillover_fists_Power1),
            new UniqueDef("unique.h_bash_gauntlets", "hands.vigor_grips", new Txt("盾拳の握り", "Shieldfist Grips"),
                new Txt("障壁を纏った拳は、盾そのものだ。", "A fist wrapped in a ward is itself a shield."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_bash_gauntlets_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_h_bash_gauntlets_Power1),
            new UniqueDef("unique.h_ward_knuckles", "hands.thorn_wraps", new Txt("護り殴りの手巻き", "Wardstrike Wraps"),
                new Txt("護りが厚いほど、打撃は重い。", "The thicker the ward, the heavier the blow."),
                Power.ShieldBash, EquipmentItemsBalanceValues.Unique_unique_h_ward_knuckles_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_h_ward_knuckles_Power1),
            new UniqueDef("unique.h_wanderer_claws", "hands.thief_gloves", new Txt("渡り盗みの手袋", "Skipping Thief Gloves"),
                new Txt("盗みは、一箇所で終わらせない。", "A theft should never end in one place."),
                Power.WanderersEdge, EquipmentItemsBalanceValues.Unique_unique_h_wanderer_claws_Power0, Power.ChainLightning, EquipmentItemsBalanceValues.Unique_unique_h_wanderer_claws_Power1),
            new UniqueDef("unique.h_runup_dodge", "hands.swiftpalm_gloves", new Txt("回り込みの手袋", "Flanking Gloves"),
                new Txt("回避の勢いを、そのまま拳に乗せる。", "Carry the momentum of the dodge into the fist."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_h_runup_dodge_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_h_runup_dodge_Power1),
            new UniqueDef("unique.h_strafe_frost", "hands.frostbite_knuckles", new Txt("凍て撃ちの歩み拳", "Chillstep Knuckles"),
                new Txt("歩きながら撃てば、敵の足元から凍る。", "Fire while walking, and the foe freezes from the feet."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_h_strafe_frost_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_h_strafe_frost_Power1),
            new UniqueDef("unique.h_sweep_rage", "hands.riven_fists", new Txt("怒り薙ぎの拳", "Wrathsweep Fists"),
                new Txt("怒りは、範囲に広げると静まる。", "Rage quiets once it is spread wide."),
                Power.Spellsweep, EquipmentItemsBalanceValues.Unique_unique_h_sweep_rage_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_h_sweep_rage_Power1),
            new UniqueDef("unique.h_pile_whirl", "hands.storm_knuckles", new Txt("重ね旋風の拳", "Stacked Whirlwind Fists"),
                new Txt("同じ技を重ねて、風の目にする。", "Stack the same skill, and it becomes the eye of the storm."),
                Power.PileOn, EquipmentItemsBalanceValues.Unique_unique_h_pile_whirl_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_h_pile_whirl_Power1),
            new UniqueDef("unique.f_featherwing", "feet.wind_sandals", new Txt("羽ばたきの履物", "Featherwing Sandals"),
                new Txt("羽根は、落ちるときにも音を立てない。", "A feather makes no sound even as it falls."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_featherwing_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_f_featherwing_Power1) { Link = new LinkDef { Requires = new[] { "St_M_FeatheryDash" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_featherwing_Link } },
            new UniqueDef("unique.f_goldenstep", "feet.dawn_steps", new Txt("光踏みの長靴", "Lightstep Treads"),
                new Txt("踏んだ跡が、ほんのり温かい。", "The prints it leaves are faintly warm."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_goldenstep_Power0, Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_goldenstep_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_M_FeatheryDash", "St_Q_GoldenBurst" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_goldenstep_Link } },
            new UniqueDef("unique.f_warped_sprint", "feet.storm_boots", new Txt("歪み疾走の靴", "Warped-Sprint Boots"),
                new Txt("曲がった道ほど、まっすぐ走れる。", "The more crooked the road, the straighter you run."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_warped_sprint_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_f_warped_sprint_Power1) { Link = new LinkDef { Requires = new[] { "St_M_Sprint" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_warped_sprint_Link } },
            new UniqueDef("unique.f_prism_stride", "feet.starlit_moccasins", new Txt("七彩の歩み靴", "Prismatic Striders"),
                new Txt("見つめる目と、走る足が同じ色になる。", "The watching eye and the running foot share one color."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_prism_stride_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_f_prism_stride_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_M_Sprint", "St_D_PrismaticEyes" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_f_prism_stride_Link } },
            new UniqueDef("unique.f_frostcharge_greaves", "feet.iron_greaves", new Txt("霜突進の鉄脛", "Frostcharge Greaves"),
                new Txt("氷を割って進む者に、道は拓ける。", "The road opens for whoever breaks through the ice."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_frostcharge_greaves_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_frostcharge_greaves_Power1) { Link = new LinkDef { Requires = new[] { "St_M_FrostyCharge" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_f_frostcharge_greaves_Link } },
            new UniqueDef("unique.f_frostbreaker_boots", "feet.ballast_boots", new Txt("氷割り重しの長靴", "Icebreaker Ballast Boots"),
                new Txt("重さこそ、いちばんの突破力。", "Weight is the best way through."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_f_frostbreaker_boots_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_f_frostbreaker_boots_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_M_FrostyCharge", "St_R_BackOff" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_f_frostbreaker_boots_Link } },
            new UniqueDef("unique.f_flashstep_sneaks", "feet.stalker_boots", new Txt("瞬歩の忍び靴", "Flashstep Sneaks"),
                new Txt("消えた場所に、足跡だけが遅れて着く。", "Footprints reach the spot long after you've vanished."),
                Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_f_flashstep_sneaks_Power0, Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_flashstep_sneaks_Power1) { Link = new LinkDef { Requires = new[] { "St_M_FlashStep" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_flashstep_sneaks_Link } },
            new UniqueDef("unique.f_windscar_boots", "feet.raven_boots", new Txt("風傷の羽靴", "Windscar Featherboots"),
                new Txt("風の傷は、走るほどに深くなる。", "A scar of wind deepens as you run."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_windscar_boots_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_f_windscar_boots_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_M_FlashStep", "St_D_ScarOfTheWind" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_f_windscar_boots_Link } },
            new UniqueDef("unique.f_quickstep_dancers", "feet.dancer_shoes", new Txt("速攻の踊り靴", "Quickstep Dancers"),
                new Txt("避けた足が、そのまま次の間合いを決める。", "The dodging foot decides the next distance."),
                Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_f_quickstep_dancers_Power0, Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_quickstep_dancers_Power1) { Link = new LinkDef { Requires = new[] { "St_M_NimbleDodge" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_quickstep_dancers_Link } },
            new UniqueDef("unique.f_doubletap_striders", "feet.hunter_striders", new Txt("二拍子の追い足", "Two-Beat Striders"),
                new Txt("一歩で避けて、二歩で撃つ。", "Dodge in one step, shoot in two."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_doubletap_striders_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_f_doubletap_striders_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_M_NimbleDodge", "St_D_DoubleTap" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_doubletap_striders_Link } },
            new UniqueDef("unique.f_quickfoot_greaves", "feet.quicksilver_greaves", new Txt("軽足の水銀脛当て", "Quickfoot Greaves"),
                new Txt("囲まれるほど、足は軽くなる。", "The more they close in, the lighter the feet."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_quickfoot_greaves_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_f_quickfoot_greaves_Power1) { Link = new LinkDef { Requires = new[] { "St_M_FastFeet" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_quickfoot_greaves_Link } },
            new UniqueDef("unique.f_counterstep_treads", "feet.blitz_treads", new Txt("返し足の電撃靴", "Counterstep Treads"),
                new Txt("先に動いた者が、先に見切られる。", "Whoever moves first is read first."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_counterstep_treads_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_f_counterstep_treads_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_M_FastFeet", "St_D_AstridsMasterpiecePriorite" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_counterstep_treads_Link } },
            new UniqueDef("unique.f_waltz_sandals", "feet.tide_sandals", new Txt("夢幻の踊り草履", "Dreamwaltz Sandals"),
                new Txt("輪になって踊れば、みんな同じ護りの中。", "Dance in a circle, and everyone shares one ward."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_waltz_sandals_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_f_waltz_sandals_Power1) { Link = new LinkDef { Requires = new[] { "St_M_DreamyWaltz" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_f_waltz_sandals_Link } },
            new UniqueDef("unique.f_packwaltz_sandals", "feet.root_sandals", new Txt("群れの輪舞草履", "Packround Sandals"),
                new Txt("輪が揃うほど、前に立つ者は軽やかになる。", "The more complete the circle, the lighter the one in front."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_packwaltz_sandals_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_f_packwaltz_sandals_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_M_DreamyWaltz", "St_D_HeartOfThePack" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_packwaltz_sandals_Link } },
            new UniqueDef("unique.f_heavycharge_sabatons", "feet.knight_sabatons", new Txt("重装突進の鉄靴", "Heavycharge Sabatons"),
                new Txt("突き進んだ道の後ろに、壁が立つ。", "A wall rises behind the road it carved."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_f_heavycharge_sabatons_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_f_heavycharge_sabatons_Power1) { Link = new LinkDef { Requires = new[] { "St_M_Charge" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_f_heavycharge_sabatons_Link } },
            new UniqueDef("unique.f_blinkstar_slippers", "feet.star_slippers", new Txt("瞬き星の上履き", "Blinkstar Slippers"),
                new Txt("瞬きの間に、足が二歩先にある。", "Within a blink, the feet are two steps ahead."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_blinkstar_slippers_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_f_blinkstar_slippers_Power1) { Link = new LinkDef { Requires = new[] { "St_M_Flicker" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_blinkstar_slippers_Link } },
            new UniqueDef("unique.f_cometskip_shoes", "feet.cometstride_shoes", new Txt("彗星跳びの靴", "Cometskip Shoes"),
                new Txt("瞬間移動の後ろに、尾が長く残る。", "A long tail trails behind the teleport."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_cometskip_shoes_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_f_cometskip_shoes_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_M_Flicker", "St_Q_SuperNova" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_cometskip_shoes_Link } },
            new UniqueDef("unique.f_burrower_boots", "feet.rooted_boots", new Txt("潜り屋の根靴", "Burrower's Rootboots"),
                new Txt("地面の下は、誰にも見えない安全地帯。", "The earth below is the one place no one can see."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_burrower_boots_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_f_burrower_boots_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Burrow" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_burrower_boots_Link } },
            new UniqueDef("unique.f_return_pilgrim_boots", "feet.pilgrim_boots", new Txt("帰還者の巡礼靴", "Returner's Pilgrim Boots"),
                new Txt("倒れても歩き出せる者が、巡礼を終える。", "Only those who can rise again finish the pilgrimage."),
                Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_f_return_pilgrim_boots_Power0, Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_return_pilgrim_boots_Power1) { Link = new LinkDef { Requires = new[] { "St_L_HerosReturn" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_f_return_pilgrim_boots_Link } },
            new UniqueDef("unique.f_whiteout_boots", "feet.frost_boots", new Txt("白一色の氷靴", "Whiteout Skimmers"),
                new Txt("吹雪の中で、動かない者は風景になる。", "Stand still in a blizzard, and you become part of the view."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_whiteout_boots_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_f_whiteout_boots_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_L_Blizzard" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_whiteout_boots_Link } },
            new UniqueDef("unique.f_arrowfall_boots", "feet.wolf_boots", new Txt("矢降る道の長靴", "Arrowfall Road Boots"),
                new Txt("矢の雨を、歩きながら撃ち返す。", "Walk through the arrow rain, returning every shaft."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_arrowfall_boots_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_f_arrowfall_boots_Power1) { Link = new LinkDef { Requires = new[] { "St_L_Multishot" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_arrowfall_boots_Link } },
            new UniqueDef("unique.f_moltencore_treads", "feet.ember_treads", new Txt("炉心の足跡", "Furnace Footprints"),
                new Txt("足跡の火が、小さな竜になって散る。", "Each burning footprint scatters as a small dragon."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_f_moltencore_treads_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_f_moltencore_treads_Power1) { Link = new LinkDef { Requires = new[] { "St_L_SmallMoltenCore" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_f_moltencore_treads_Link } },
            new UniqueDef("unique.f_unshackled_treads", "feet.gale_greaves", new Txt("枷外しの脛当て", "Unshackled Greaves"),
                new Txt("外した枷の重さが、そのまま推進力になる。", "The weight of a discarded shackle becomes thrust."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_unshackled_treads_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_f_unshackled_treads_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty", "Hero_Aurena" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_f_unshackled_treads_Link } },
            new UniqueDef("unique.f_eternalflame_dash", "feet.emberdash_boots", new Txt("消えぬ火の駆け靴", "Everflame Dashers"),
                new Txt("走った道に、永遠の炎が燃え残る。", "On the road you ran, an eternal flame remains."),
                Power.Wildfire, EquipmentItemsBalanceValues.Unique_unique_f_eternalflame_dash_Power0, Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_eternalflame_dash_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_EternalFlame", "Hero_Lacerta" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_f_eternalflame_dash_Link } },
            new UniqueDef("unique.f_purewhite_spurs", "feet.sunspur_boots", new Txt("純白の拍車", "Purewhite Spurs"),
                new Txt("十秒目の一歩は、いつもより白い。", "The tenth second's step is whiter than the rest."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_purewhite_spurs_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_f_purewhite_spurs_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_PureWhite", "St_Q_Discipline" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_purewhite_spurs_Link } },
            new UniqueDef("unique.f_perfect_slippers", "feet.sage_slippers", new Txt("完全無欠の上履き", "Flawless Slippers"),
                new Txt("欠けのない靴は、道端の欠片を見逃さない。", "A flawless shoe never overlooks a shard by the road."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_perfect_slippers_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_f_perfect_slippers_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Perfect", "Hero_Yubar" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_f_perfect_slippers_Link } },
            new UniqueDef("unique.f_golden_gift_boots", "feet.travel_boots", new Txt("黄金の贈り物靴", "Goldengift Boots"),
                new Txt("贈り物を踏んで割れば、黄金が散る。", "Stamp on a gift, and gold scatters."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_golden_gift_boots_Power0, Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_f_golden_gift_boots_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_HeartOfGold", "Gem_L_CamillasGift" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_f_golden_gift_boots_Link } },
            new UniqueDef("unique.f_compass_sandals", "feet.mistral_sandals", new Txt("羅針盤の歩き草履", "Compass-Walker Sandals"),
                new Txt("導かれた足は、迷っているときほど速い。", "Guided feet are fastest when lost."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_compass_sandals_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_f_compass_sandals_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GuidingCompass_NotCharged", "St_M_FastFeet" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_compass_sandals_Link } },
            new UniqueDef("unique.f_laststar_steps", "feet.starlit_moccasins", new Txt("終星の足音", "Last-Star Footfalls"),
                new Txt("最後の星の下を、最後まで歩く。", "Walk to the end beneath the last star."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_laststar_steps_Power0, Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_laststar_steps_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_LastStarlight", "St_M_Flicker" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_laststar_steps_Link } },
            new UniqueDef("unique.f_hysteria_spikes", "feet.spiked_boots", new Txt("狂騒の鋲靴", "Hysteria Spikes"),
                new Txt("止まった途端、足元の鋲が痛み出す。", "The moment you stop, the spikes underfoot start to ache."),
                Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_f_hysteria_spikes_Power0, Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_hysteria_spikes_Power1) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_f_hysteria_spikes_Link } },
            new UniqueDef("unique.bond_twinblade_stride", "feet.quicksilver_greaves", new Txt("双刃の足並み", "Twinblade Pace"),
                new Txt("二人の足音が、ひとつの拍になる。", "Two sets of footsteps beat as one."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_bond_twinblade_stride_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_bond_twinblade_stride_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "Hero_Mist" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_twinblade_stride_Link } },
            new UniqueDef("unique.bond_shield_and_pack", "feet.guard_sabatons", new Txt("盾と群れの歩調", "Shield-and-Pack Stride"),
                new Txt("盾が止まり、群れが進む。足並みは揃う。", "The shield halts, the pack advances; the pace stays together."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_bond_shield_and_pack_Power0, Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_bond_shield_and_pack_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "Hero_Nachia" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_shield_and_pack_Link } },
            new UniqueDef("unique.bond_powder_and_prism", "feet.froststride_shoes", new Txt("火薬と虹の足取り", "Powder-and-Prism Steps"),
                new Txt("撃つ者の足跡を、見る者の目が追う。", "The watching eye follows the shooter's footprints."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_bond_powder_and_prism_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_bond_powder_and_prism_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "Hero_Bismuth" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_powder_and_prism_Link } },
            new UniqueDef("unique.f_shardburst_greaves", "feet.iron_clogs", new Txt("割れ盾の鉄下駄", "Wardsplinter Clogs"),
                new Txt("障壁が砕ける音は、反撃の合図。", "The sound of a breaking ward is the signal to strike back."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_f_shardburst_greaves_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_f_shardburst_greaves_Power1),
            new UniqueDef("unique.f_shatterward_boots", "feet.ironwave_greaves", new Txt("砕盾の波脛当て", "Breakwave Greaves"),
                new Txt("砕かれた護りの破片が、敵を刺す。", "Splinters of a broken ward pierce the foe."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_f_shatterward_boots_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_shatterward_boots_Power1),
            new UniqueDef("unique.f_bulwark_burst_boots", "feet.bastion_sabatons", new Txt("護り砕きの鉄靴", "Bastion-Burst Sabatons"),
                new Txt("破れた砦から、爆風が吹き出す。", "A blast blows out from the breached fortress."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_f_bulwark_burst_boots_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_f_bulwark_burst_boots_Power1),
            new UniqueDef("unique.f_watchful_steps", "feet.pilgrim_boots", new Txt("見守りの巡礼靴", "Watchful Pilgrim Boots"),
                new Txt("仲間の背を見て歩くのが、巡礼者の流儀。", "A pilgrim's way is to walk watching a friend's back."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_watchful_steps_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_f_watchful_steps_Power1),
            new UniqueDef("unique.f_guardian_sandals", "feet.root_sandals", new Txt("見張り番の草履", "Sentry Sandals"),
                new Txt("倒れそうな仲間の足元に、盾を滑り込ませる。", "Slide a shield beneath the feet of a friend about to fall."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_guardian_sandals_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_f_guardian_sandals_Power1),
            new UniqueDef("unique.f_lifebloom_greaves", "feet.chain_greaves", new Txt("命流しの鎖脛", "Lifeflow Chain Greaves"),
                new Txt("余った命は、足元の友へ。", "Surplus life flows down to the friend at your feet."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_lifebloom_greaves_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_f_lifebloom_greaves_Power1),
            new UniqueDef("unique.f_breakout_boots", "feet.storm_boots", new Txt("包囲破りの長靴", "Encirclement-Breaker Boots"),
                new Txt("囲まれたら、道は外ではなく自分の足元にある。", "When surrounded, the road is not outside but under your feet."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_breakout_boots_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_f_breakout_boots_Power1),
            new UniqueDef("unique.f_breakout_whirl", "feet.gale_greaves", new Txt("囲い抜けの旋風脛", "Cordon-Slip Greaves"),
                new Txt("輪の外へ出るときに、風は輪を描く。", "When you break out of the ring, the wind draws one."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_breakout_whirl_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_f_breakout_whirl_Power1),
            new UniqueDef("unique.f_breakout_thorns", "feet.spiked_boots", new Txt("網破りの鋲靴", "Netbreaker Spikes"),
                new Txt("取り囲んだ者の方が、痛い目を見る。", "It is the ones who surround you who get hurt."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_breakout_thorns_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_f_breakout_thorns_Power1),
            new UniqueDef("unique.f_rooted_stance", "feet.rooted_boots", new Txt("根を張る構えの靴", "Rooted Stance Boots"),
                new Txt("立ち止まるほど、足は大地と契約する。", "The longer you stand, the more your feet contract with the earth."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_rooted_stance_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_f_rooted_stance_Power1),
            new UniqueDef("unique.f_stillstep_greaves", "feet.rampart_greaves", new Txt("不動の砦脛当て", "Stillrampart Greaves"),
                new Txt("動かぬ者は、動かぬ壁になる。", "One who does not move becomes a wall that does not move."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_stillstep_greaves_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_f_stillstep_greaves_Power1),
            new UniqueDef("unique.f_standfast_clogs", "feet.iron_clogs", new Txt("仁王立ちの木靴", "Standfast Clogs"),
                new Txt("立ち尽くした先に、必殺の間合いが生まれる。", "Where you stand your ground, a killing range appears."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_standfast_clogs_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_f_standfast_clogs_Power1),
            new UniqueDef("unique.f_calm_sandals", "feet.wind_sandals", new Txt("凪ぎ待ちの草履", "Calmwait Sandals"),
                new Txt("風が止んでから、いちばん鋭い一撃を。", "Wait for the wind to die, then strike hardest."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_calm_sandals_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_calm_sandals_Power1),
            new UniqueDef("unique.f_runup_boots", "feet.travel_boots", new Txt("助走の旅靴", "Runup Travel Boots"),
                new Txt("歩いた距離は、拳の重さになる。", "The distance walked becomes the weight of the fist."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_runup_boots_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_f_runup_boots_Power1),
            new UniqueDef("unique.f_run_wolfboots", "feet.wolfstride_boots", new Txt("走り狼の長靴", "Runwolf Boots"),
                new Txt("獲物を見つけたら、まず走れ。", "On sighting prey, run first."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_run_wolfboots_Power0, Power.OpeningStrike, EquipmentItemsBalanceValues.Unique_unique_f_run_wolfboots_Power1),
            new UniqueDef("unique.f_charge_leapers", "feet.deadeye_leggings", new Txt("助走の跳ね脚", "Leaping Legs"),
                new Txt("走って、跳んで、斬り下ろす。", "Run, leap, cut down."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_charge_leapers_Power0, Power.Whirlwind, EquipmentItemsBalanceValues.Unique_unique_f_charge_leapers_Power1),
            new UniqueDef("unique.f_strafing_boots", "feet.thorntread_sabatons", new Txt("歩き撃ちの棘靴", "Strafing Thorntreads"),
                new Txt("撃ちながら歩けば、敵の足は遅れる。", "Shoot while walking, and the foe's feet fall behind."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_strafing_boots_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_f_strafing_boots_Power1),
            new UniqueDef("unique.f_sidestep_shoes", "feet.gale_greaves", new Txt("横歩きの疾風靴", "Sidestep Gale Shoes"),
                new Txt("真正面から撃つのは、礼儀作法でしかない。", "Shooting head-on is mere etiquette."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_sidestep_shoes_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_f_sidestep_shoes_Power1),
            new UniqueDef("unique.f_vanguard_sabatons", "feet.bastion_sabatons", new Txt("前衛の誓いの鉄靴", "Vanguard Oath Sabatons"),
                new Txt("最前列に立つと、護りが少し分厚くなる。", "Stand in the front row, and the ward grows a little thicker."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_sabatons_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_sabatons_Power1),
            new UniqueDef("unique.f_vanguard_greaves", "feet.stoneguard_sabatons", new Txt("先陣の石脛当て", "Frontline Stone Greaves"),
                new Txt("先に立つ者は、先に守られる。", "Whoever stands first is guarded first."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_greaves_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_greaves_Power1),
            new UniqueDef("unique.f_vanguard_clogs", "feet.ballast_boots", new Txt("矢面の重し靴", "Brunt Ballast Boots"),
                new Txt("矢面に立てば、盾が育つ。", "Stand where the arrows fall, and the shield grows."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_clogs_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_clogs_Power1),
            new UniqueDef("unique.f_vanguard_thorns", "feet.ironwave_greaves", new Txt("切っ先を受ける脛当て", "Point-Taker Greaves"),
                new Txt("最初に刺された者は、最後まで刺し返す。", "The first to be pierced is the last to stop piercing back."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_thorns_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_thorns_Power1),
            new UniqueDef("unique.f_deathbloom_greaves", "feet.tortoiseshell_greaves", new Txt("散花の亀甲脛当て", "Scatterbloom Greaves"),
                new Txt("倒れた牙の跡で、火花が咲く。", "Sparks bloom where a fang has fallen."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_f_deathbloom_greaves_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_f_deathbloom_greaves_Power1),
            new UniqueDef("unique.f_gravemarch_boots", "feet.beastpaw_boots", new Txt("墓花を踏む長靴", "Gravebloom Boots"),
                new Txt("獣の亡骸の上にも、花は咲く。", "Flowers bloom even over a beast's remains."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_f_gravemarch_boots_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_f_gravemarch_boots_Power1),
            new UniqueDef("unique.f_burstfang_boots", "feet.wolf_boots", new Txt("牙裂きの毛皮靴", "Fangburst Furboots"),
                new Txt("倒れた群れの一匹が、最後にもう一度噛みつく。", "The last of a fallen pack bites once more."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_f_burstfang_boots_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_f_burstfang_boots_Power1),
            new UniqueDef("unique.f_shardfeast_boots", "feet.travel_boots", new Txt("欠片拾いの旅靴", "Shardgleaner Boots"),
                new Txt("道端の欠片は、足の疲れを癒してくれる。", "Shards on the roadside soothe weary feet."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_shardfeast_boots_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_f_shardfeast_boots_Power1),
            new UniqueDef("unique.f_shardsip_sandals", "feet.wind_sandals", new Txt("砂金すくいの草履", "Goldsift Sandals"),
                new Txt("拾った砂金が、そのまま体力になる。", "The gold dust you gather turns directly into stamina."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_shardsip_sandals_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_f_shardsip_sandals_Power1),
            new UniqueDef("unique.f_shard_hunter", "feet.hunter_boots", new Txt("欠片狩りの追跡靴", "Shard-Tracker Boots"),
                new Txt("獲物の誇りは、足跡の中に落ちている。", "The prey's pride lies within its footprints."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_shard_hunter_Power0, Power.PreyPride, EquipmentItemsBalanceValues.Unique_unique_f_shard_hunter_Power1),
            new UniqueDef("unique.f_shard_devout", "feet.pilgrim_boots", new Txt("欠片巡りの巡礼靴", "Shard-Pilgrim Boots"),
                new Txt("祈りの道には、小さな光が落ちている。", "Small lights lie along the road of prayer."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_f_shard_devout_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_f_shard_devout_Power1),
            new UniqueDef("unique.f_grudgebell_greaves", "feet.chain_greaves", new Txt("怨嗟の鐘の鎖脛", "Grudgebell Chain Greaves"),
                new Txt("傷が積もったら、鐘が鳴る。", "When the wounds pile up, the bell tolls."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_f_grudgebell_greaves_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_f_grudgebell_greaves_Power1),
            new UniqueDef("unique.f_toll_sabatons", "feet.iron_greaves", new Txt("弔鐘の鉄脛", "Tolling Greaves"),
                new Txt("受けた痛みを、鐘の音に変えて返す。", "Pain taken is returned as the ringing of a bell."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_f_toll_sabatons_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_f_toll_sabatons_Power1),
            new UniqueDef("unique.f_tollward_boots", "feet.winterhide_boots", new Txt("凍え鐘の毛皮靴", "Frostbell Hideboots"),
                new Txt("冷えた鐘ほど、遠くまで響く。", "The colder the bell, the farther it rings."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_f_tollward_boots_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_tollward_boots_Power1),
            new UniqueDef("unique.f_ready_sabatons", "feet.stoneguard_sabatons", new Txt("構え待ちの鉄靴", "Waiting-Stance Sabatons"),
                new Txt("戦いの前の静けさが、最初の盾になる。", "The calm before battle is the first shield."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_ready_sabatons_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_f_ready_sabatons_Power1),
            new UniqueDef("unique.f_ready_greaves", "feet.tortoiseshell_greaves", new Txt("備えの亀甲脛", "Tortoise-Ready Greaves"),
                new Txt("備えのある者は、最初の一撃を笑って受ける。", "One who is prepared laughs at the first blow."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_ready_greaves_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_f_ready_greaves_Power1),
            new UniqueDef("unique.f_ready_clogs", "feet.iron_clogs", new Txt("待ち伏せの鉄下駄", "Ambushwait Clogs"),
                new Txt("待った時間が、そのまま護りになる。", "Time spent waiting becomes the ward."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_ready_clogs_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_f_ready_clogs_Power1),
            new UniqueDef("unique.f_vanguard_stillboots", "feet.deeproot_boots", new Txt("不動の前衛靴", "Immovable Vanguard Boots"),
                new Txt("前に立って動かなければ、それが砦だ。", "Stand at the front without moving, and you are the fortress."),
                Power.ImmovableStance, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_stillboots_Power0, Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_vanguard_stillboots_Power1),
            new UniqueDef("unique.f_wardshatter_vanguard", "feet.wardstep_sandals", new Txt("盾砕きの前衛草履", "Wardbreaker Vanguard Sandals"),
                new Txt("盾が砕ける場所に、自分から立つ。", "Stand where the shields break, by choice."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_f_wardshatter_vanguard_Power0, Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_wardshatter_vanguard_Power1),
            new UniqueDef("unique.f_toll_ready_boots", "feet.bastion_sabatons", new Txt("鐘と備えの鉄靴", "Bell-and-Ready Sabatons"),
                new Txt("鐘が鳴る前に、護りはもう立っている。", "The ward already stands before the bell tolls."),
                Power.TollOfGrudge, EquipmentItemsBalanceValues.Unique_unique_f_toll_ready_boots_Power0, Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_f_toll_ready_boots_Power1),
            new UniqueDef("unique.f_watch_rear_boots", "feet.mender_shoes", new Txt("後ろ見守りの癒し靴", "Rearwatch Mender Shoes"),
                new Txt("背後の仲間に、先に気づく足。", "Feet that notice the friend behind first."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_f_watch_rear_boots_Power0, Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_watch_rear_boots_Power1),
            new UniqueDef("unique.f_runlight_boots", "feet.sun_sandals", new Txt("陽走りの草履", "Sunrun Sandals"),
                new Txt("光を追う足は、光に追いつかれない。", "Feet that chase the light are never caught by it."),
                Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_runlight_boots_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_f_runlight_boots_Power1),
            new UniqueDef("unique.f_strafe_light", "feet.cometstride_shoes", new Txt("彗星撃ちの靴", "Cometshot Shoes"),
                new Txt("動きながら放つ光は、尾を引く。", "A light fired on the move trails a tail."),
                Power.StrafeShot, EquipmentItemsBalanceValues.Unique_unique_f_strafe_light_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_f_strafe_light_Power1),
            new UniqueDef("unique.f_vigor_vanguard", "feet.stone_boots", new Txt("全力前衛の石靴", "Full-Vigor Stoneboots"),
                new Txt("体力が満ちている者ほど、前に立つ資格がある。", "Those full of health have the right to stand in front."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_vigor_vanguard_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_f_vigor_vanguard_Power1),
            new UniqueDef("unique.f_blaze_deathbloom", "feet.spiked_boots", new Txt("火花咲きの鋲靴", "Sparkbloom Spikes"),
                new Txt("倒れた牙の跡で、熾火がはぜる。", "Embers pop where the fang has fallen."),
                Power.DeathBloom, EquipmentItemsBalanceValues.Unique_unique_f_blaze_deathbloom_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_f_blaze_deathbloom_Power1),
            new UniqueDef("unique.c_dissolve_fang", "charm.fang_necklace", new Txt("分解の牙飾り", "Dissolving Fang Necklace"),
                new Txt("奪った命の温もりは、そのまま持ち主の首元に残る。", "The warmth of stolen life lingers at the wearer's throat."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_c_dissolve_fang_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_c_dissolve_fang_Power1) { Link = new LinkDef { Requires = new[] { "St_D_DisintegratingClaw" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_dissolve_fang_Link } },
            new UniqueDef("unique.c_menace_locket", "charm.mender_locket", new Txt("美しき脅しのロケット", "Beautiful Menace Locket"),
                new Txt("優しさを装った者ほど、背中を預けやすい。", "The ones who feign kindness are the easiest to trust at your back."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_c_menace_locket_Power0, Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_c_menace_locket_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_D_BeautifulThreat" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_menace_locket_Link } },
            new UniqueDef("unique.c_iris_lens", "charm.dream_lens", new Txt("虹彩の水晶玉", "Iris Crystal Lens"),
                new Txt("見つめる目が増えるほど、世界は軽くなる。", "The more eyes that watch, the lighter the world becomes."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_c_iris_lens_Power0, Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_c_iris_lens_Power1) { Link = new LinkDef { Requires = new[] { "St_D_PrismaticEyes" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_iris_lens_Link } },
            new UniqueDef("unique.c_veinfrost_heart", "charm.ice_heart", new Txt("氷脈の心核", "Veinfrost Heart"),
                new Txt("血が凍る前に、盾が先に砕ける。", "The shield shatters before the blood can freeze."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_c_veinfrost_heart_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_c_veinfrost_heart_Power1) { Link = new LinkDef { Requires = new[] { "St_D_IcyVeins" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_c_veinfrost_heart_Link } },
            new UniqueDef("unique.c_eelcharged_bell", "charm.storm_bell", new Txt("電気鰻の鈴", "Eelcharged Bell"),
                new Txt("身に纏った雷は、盾の形をしている。", "The lightning you wear is shaped like a shield."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_c_eelcharged_bell_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_c_eelcharged_bell_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_D_ChargedAnguillian" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_eelcharged_bell_Link } },
            new UniqueDef("unique.c_flowkill_seal", "charm.hunters_seal", new Txt("流れ殺しの刻印", "Flowkill Seal"),
                new Txt("止まらぬ者は、止まらぬまま仕留める。", "One who never stops finishes without stopping."),
                Power.BareHandedPride, EquipmentItemsBalanceValues.Unique_unique_c_flowkill_seal_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_c_flowkill_seal_Power1) { Link = new LinkDef { Requires = new[] { "St_D_TheKillingFlow" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_c_flowkill_seal_Link } },
            new UniqueDef("unique.c_windscar_quill", "charm.raven_feather", new Txt("風傷の羽根ペン", "Windscar Quill"),
                new Txt("風が走り抜けた跡に、一本だけ羽根が残る。", "One feather is left behind where the wind ran through."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_c_windscar_quill_Power0, Power.Sprint, EquipmentItemsBalanceValues.Unique_unique_c_windscar_quill_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_D_ScarOfTheWind" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_windscar_quill_Link } },
            new UniqueDef("unique.c_powderhorn", "charm.ember_locket", new Txt("蜥蜴の火薬入れ", "Salamander Powderhorn"),
                new Txt("四発目には、必ず余分な火薬を詰める。", "Always pack extra powder for the fourth shot."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_c_powderhorn_Power0, Power.Blaze, EquipmentItemsBalanceValues.Unique_unique_c_powderhorn_Power1) { Link = new LinkDef { Requires = new[] { "St_D_SalamanderPowder" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_powderhorn_Link } },
            new UniqueDef("unique.c_twobeat_sight", "charm.keeneye_charm", new Txt("二拍の照準", "Two-Beat Sight"),
                new Txt("避けて、撃つ。もう一度、撃つ。", "Dodge, shoot. Shoot once more."),
                Power.BareHandedPride, EquipmentItemsBalanceValues.Unique_unique_c_twobeat_sight_Power0, Power.EchoingDodge, EquipmentItemsBalanceValues.Unique_unique_c_twobeat_sight_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_D_DoubleTap" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_twobeat_sight_Link } },
            new UniqueDef("unique.c_engarde_token", "charm.iron_seal", new Txt("構え札", "En Garde Token"),
                new Txt("構えを見せた方が、先に譲る。", "The one who shows the stance concedes first."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_c_engarde_token_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_c_engarde_token_Power1) { Link = new LinkDef { Requires = new[] { "St_D_AstridsMasterpieceEnGarde" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_c_engarde_token_Link } },
            new UniqueDef("unique.c_rightofway_feather", "charm.feather_token", new Txt("先手の羽", "Right-of-Way Feather"),
                new Txt("道を譲らせる者が、道を決める。", "Whoever makes the other yield chooses the road."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_c_rightofway_feather_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_c_rightofway_feather_Power1) { Link = new LinkDef { Requires = new[] { "St_D_AstridsMasterpiecePriorite" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_c_rightofway_feather_Link } },
            new UniqueDef("unique.c_packheart", "charm.beasttongue_charm", new Txt("群れの鼓動札", "Packbeat Charm"),
                new Txt("群れの心臓は、一つの拍で鳴る。", "The pack's heart beats to a single rhythm."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_c_packheart_Power0, Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_c_packheart_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_D_HeartOfThePack" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_packheart_Link } },
            new UniqueDef("unique.c_lifering_lotus", "charm.lotus_seal", new Txt("命の環の蓮", "Lifering Lotus"),
                new Txt("花弁が巡るたび、命も巡る。", "As the petals turn, so does life."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_c_lifering_lotus_Power0, Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_c_lifering_lotus_Power1) { Link = new LinkDef { Requires = new[] { "St_D_CircleOfLife" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_lifering_lotus_Link } },
            new UniqueDef("unique.c_resolute_stoneheart", "charm.stone_heart", new Txt("決意の石心", "Resolute Stoneheart"),
                new Txt("最初に決めた者の石心は、割れにくい。", "The stoneheart of the one who decided first rarely cracks."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_c_resolute_stoneheart_Power0, Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_c_resolute_stoneheart_Power1) { Link = new LinkDef { Requires = new[] { "St_D_Resolve" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_c_resolute_stoneheart_Link } },
            new UniqueDef("unique.c_mercy_sunpin", "charm.sun_brooch", new Txt("慈悲の陽飾り", "Mercy Sunpin"),
                new Txt("癒した分だけ、胸の太陽は輝く。", "The sun on your chest shines as much as you heal."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_c_mercy_sunpin_Power0, Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_c_mercy_sunpin_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_D_MercyOfEl" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_mercy_sunpin_Link } },
            new UniqueDef("unique.c_exotic_inkstone", "charm.ink_stone", new Txt("異質物の墨石", "Exotic Inkstone"),
                new Txt("知らない色で書いた文字は、誰にも読めないが光る。", "Writing in an unknown color cannot be read, but it glows."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_c_exotic_inkstone_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_c_exotic_inkstone_Power1) { Link = new LinkDef { Requires = new[] { "St_D_ExoticMatter" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_exotic_inkstone_Link } },
            new UniqueDef("unique.c_convergence_comet", "charm.comet_pendant", new Txt("収束点の彗星飾り", "Convergence Comet"),
                new Txt("散らばった星が、一点に戻ってくる夜。", "The night scattered stars return to a single point."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_c_convergence_comet_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_c_convergence_comet_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_D_ConvergencePoint" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_c_convergence_comet_Link } },
            new UniqueDef("unique.c_hairline_heart", "charm.pulsing_core", new Txt("薄氷の心臓", "Hairline Heart"),
                new Txt("命は一枚の薄氷。割れる前に、盾が立つ。", "Life is one sheet of thin ice; the shield rises before it breaks."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_c_hairline_heart_Power0, Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_c_hairline_heart_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Supersymmetry", "St_D_IcyVeins" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_hairline_heart_Link } },
            new UniqueDef("unique.c_sacrifice_brooch", "charm.blaze_brooch", new Txt("供犠の輝き針", "Sacrifice Pin"),
                new Txt("差し出した命は、必ず光になって戻る。", "A life offered always returns as light."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_c_sacrifice_brooch_Power0, Power.Bloodlust, EquipmentItemsBalanceValues.Unique_unique_c_sacrifice_brooch_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "Gem_L_SuppressedArcanum" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_sacrifice_brooch_Link } },
            new UniqueDef("unique.c_wary_windchime", "charm.windchime_charm", new Txt("疑心の風鈴", "Wary Windchime"),
                new Txt("不安げに鳴る鈴ほど、先に気づく。", "The uneasier the bell, the sooner it notices."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_c_wary_windchime_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_c_wary_windchime_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia", "St_R_Tranquility" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_c_wary_windchime_Link } },
            new UniqueDef("unique.c_unbinding_horn", "charm.war_horn", new Txt("解き放ちの角笛", "Unbinding Horn"),
                new Txt("角笛が鳴れば、鎖は一斉にほどける。", "When the horn sounds, every chain comes loose at once."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_c_unbinding_horn_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_c_unbinding_horn_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty", "St_R_Cataclysm" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_unbinding_horn_Link } },
            new UniqueDef("unique.c_cooks_hearthstone", "charm.hearthstone", new Txt("料理人の竈石", "Cook's Hearthstone"),
                new Txt("永遠に燃える火は、台所でも役に立つ。", "An eternal flame is useful in the kitchen too."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_c_cooks_hearthstone_Power0, Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_c_cooks_hearthstone_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Culinary", "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_cooks_hearthstone_Link } },
            new UniqueDef("unique.c_chaos_cog", "charm.clockwork_charm", new Txt("混沌の歯車", "Chaos Cog"),
                new Txt("噛み合わない歯車ほど、予測できない。", "The worse the cogs mesh, the less predictable."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_c_chaos_cog_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_c_chaos_cog_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_ChaosApple", "St_R_DangerousTheory" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_chaos_cog_Link } },
            new UniqueDef("unique.c_perfect_stardust", "charm.stardust_pendant", new Txt("完全なる星屑", "Flawless Stardust"),
                new Txt("欠けのない星屑は、夢の予兆に最も敏感だ。", "Flawless stardust is the most sensitive to dream omens."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_c_perfect_stardust_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_c_perfect_stardust_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Perfect", "Gem_U_GlacialCore" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_perfect_stardust_Link } },
            new UniqueDef("unique.c_whitesun_pin", "charm.sunspoke_pin", new Txt("純白の太陽針", "Whitesun Pin"),
                new Txt("純白の光に、太陽が二つ重なる。", "Two suns overlap in the pure white light."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_c_whitesun_pin_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_c_whitesun_pin_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_PureWhite", "Gem_L_SolarEye" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_whitesun_pin_Link } },
            new UniqueDef("unique.c_emberbrush_locket", "charm.cindercore_locket", new Txt("尾火のロケット", "Emberbrush Locket"),
                new Txt("跳ね返った火は、必ず持ち主を探し出す。", "A rebounding flame always finds its owner."),
                Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_c_emberbrush_locket_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_c_emberbrush_locket_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Embertail", "St_D_SalamanderPowder" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_emberbrush_locket_Link } },
            new UniqueDef("unique.c_prison_feather", "charm.iron_feather", new Txt("魂牢の鉄羽", "Soulgaol Feather"),
                new Txt("牢の扉は一度だけ、内側から開く。", "The gaol door opens from the inside, once."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_c_prison_feather_Power0, Power.PerfectRead, EquipmentItemsBalanceValues.Unique_unique_c_prison_feather_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_SoulPrison", "Hero_Mist" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_c_prison_feather_Link } },
            new UniqueDef("unique.c_believer_oakguard", "charm.oak_amulet", new Txt("信徒の樫守り", "Believer's Oakguard"),
                new Txt("数えた祈りの分だけ、護りは硬くなる。", "The ward hardens by the number of prayers counted."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_c_believer_oakguard_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_c_believer_oakguard_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith", "St_R_SerpentineBlessing" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_believer_oakguard_Link } },
            new UniqueDef("unique.c_heartgold_ring", "charm.garnet_ring", new Txt("黄金心臓の指輪", "Heart-of-Gold Ring"),
                new Txt("重い財布は、軽やかな足取りの重しになる。", "A heavy purse anchors a light step."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_c_heartgold_ring_Power0, Power.SpendersWard, EquipmentItemsBalanceValues.Unique_unique_c_heartgold_ring_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_HeartOfGold", "St_L_CoinExplosion" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_heartgold_ring_Link } },
            new UniqueDef("unique.c_compass_clock", "charm.old_clock", new Txt("羅針盤の古時計", "Compass Clock"),
                new Txt("動かない針が、いちばん確かな方向を指す。", "The needle that does not move points most surely."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_c_compass_clock_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_c_compass_clock_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_GuidingCompass_NotCharged", "Hero_Yubar" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_c_compass_clock_Link } },
            new UniqueDef("unique.bond_tide_and_pack", "charm.tidewarden_brooch", new Txt("潮守りと群れの留め針", "Tide-and-Pack Pin"),
                new Txt("海の護りと森の護りが、胸元で重なる。", "The wards of sea and forest overlap at the chest."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_bond_tide_and_pack_Power0, Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_bond_tide_and_pack_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "Hero_Cetus" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_bond_tide_and_pack_Link } },
            new UniqueDef("unique.bond_sun_and_healer", "charm.halo_charm", new Txt("二つの灯の輪", "Twin-Lamp Halo"),
                new Txt("二つの灯が並ぶと、闇は居場所を失う。", "Place two lamps side by side, and the dark loses its home."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_bond_sun_and_healer_Power0, Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_bond_sun_and_healer_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "Hero_Aurena" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_sun_and_healer_Link } },
            new UniqueDef("unique.bond_veil_and_blade", "charm.eclipse_ring", new Txt("刃と帳の指輪", "Blade-and-Veil Ring"),
                new Txt("前で刃が躍るとき、後ろで帳が降りる。", "While the blade dances in front, the veil falls behind."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_bond_veil_and_blade_Power0, Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_bond_veil_and_blade_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "Hero_Yubar" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_veil_and_blade_Link } },
            new UniqueDef("unique.bond_hunting_pair", "charm.hunters_seal", new Txt("二人狩りの印", "Pair-Hunt Seal"),
                new Txt("影と銃が、同じ獲物を追う。", "The shadow and the gun chase the same quarry."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_bond_hunting_pair_Power0, Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_bond_hunting_pair_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "Hero_Lacerta" }, Kind = LinkKind.Attune, Value = EquipmentItemsBalanceValues.Unique_unique_bond_hunting_pair_Link } },
            new UniqueDef("unique.c_mercy_sanctuary_seal", "charm.guardian_seal", new Txt("慈悲と聖域の護符", "Mercy-and-Sanctuary Seal"),
                new Txt("慈悲を重ねた聖域は、広がりやすい。", "A sanctuary layered with mercy spreads easily."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_c_mercy_sanctuary_seal_Power0, Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_c_mercy_sanctuary_seal_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_D_MercyOfEl", "St_R_SanctuaryOfEl" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_mercy_sanctuary_seal_Link } },
            new UniqueDef("unique.c_lifecircle_charm", "charm.deeproot_charm", new Txt("命の環の根守り", "Lifecircle Rootcharm"),
                new Txt("根が結ばれた群れは、倒れる順番も巡る。", "A pack with joined roots even falls in turn."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_c_lifecircle_charm_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_c_lifecircle_charm_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_D_CircleOfLife", "St_Q_SylvanCall" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_c_lifecircle_charm_Link } },
            new UniqueDef("unique.c_exotic_cataclysm", "charm.stardust_pendant", new Txt("異質な厄災の星飾り", "Exotic Cataclysm Pendant"),
                new Txt("知らない物質が、星を呼ぶ。", "An unknown substance calls the stars."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_c_exotic_cataclysm_Power0, Power.Radiance, EquipmentItemsBalanceValues.Unique_unique_c_exotic_cataclysm_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_D_ExoticMatter", "St_R_Cataclysm" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_c_exotic_cataclysm_Link } },
            new UniqueDef("unique.c_chainreaction_lotus", "charm.lotus_seal", new Txt("連鎖を癒す蓮", "Chainmending Lotus"),
                new Txt("奪った分を癒しに変える、静かな連鎖。", "A quiet chain that turns what is taken into healing."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_c_chainreaction_lotus_Power0, Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_c_chainreaction_lotus_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_D_DisintegratingClaw", "St_R_ChainReaction" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_c_chainreaction_lotus_Link } },
            new UniqueDef("unique.c_wardsplinter_pin", "charm.bulwark_seal", new Txt("割れ盾の留め針", "Shardward Pin"),
                new Txt("砕けた護りの欠片が、辺りに降り注ぐ。", "Fragments of a shattered ward rain down on the surroundings."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_c_wardsplinter_pin_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_wardsplinter_pin_Power1),
            new UniqueDef("unique.c_brokenshield_ring", "charm.boulder_pendant", new Txt("砕盾の指輪", "Brokenshield Ring"),
                new Txt("割れた盾は、一度だけ最高の武器になる。", "A broken shield makes the finest weapon, once."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_c_brokenshield_ring_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_c_brokenshield_ring_Power1),
            new UniqueDef("unique.c_shared_lamp", "charm.hearthside_ring", new Txt("分け灯の指輪", "Shared-Lamp Ring"),
                new Txt("灯りは、分けても減らない。", "A lamp does not dim when shared."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_c_shared_lamp_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_shared_lamp_Power1),
            new UniqueDef("unique.c_shared_star", "charm.snowbloom_charm", new Txt("星分けの雪花守り", "Starshare Snowbloom Charm"),
                new Txt("星の加護は、隣の肩にも降る。", "A star's blessing falls on the neighbor's shoulder too."),
                Power.SharedWard, EquipmentItemsBalanceValues.Unique_unique_c_shared_star_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_c_shared_star_Power1),
            new UniqueDef("unique.c_gleaming_lamp", "charm.hearthside_ring", new Txt("輝く障壁の指輪", "Gleamward Ring"),
                new Txt("障壁が光るとき、攻撃も光る。", "When the ward gleams, the attacks gleam too."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_c_gleaming_lamp_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_gleaming_lamp_Power1),
            new UniqueDef("unique.c_gleaming_star", "charm.halo_charm", new Txt("障壁星の光輪", "Wardstar Halo"),
                new Txt("星の護りを纏えば、拳にも星が宿る。", "Wear the star's ward, and a star lodges in your fist."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_c_gleaming_star_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_c_gleaming_star_Power1),
            new UniqueDef("unique.c_gleaming_hearth", "charm.deeproot_charm", new Txt("障壁の根守り", "Wardroot Charm"),
                new Txt("根を張る者は、護りの中で枝を伸ばす。", "Those who take root stretch their branches within the ward."),
                Power.GleamingWard, EquipmentItemsBalanceValues.Unique_unique_c_gleaming_hearth_Power0, Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_c_gleaming_hearth_Power1),
            new UniqueDef("unique.c_costar_horn", "charm.war_horn", new Txt("共演の角笛", "Co-star Horn"),
                new Txt("味方の見せ場は、自分の追い風になる。", "A friend's big moment is your own tailwind."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_c_costar_horn_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_c_costar_horn_Power1),
            new UniqueDef("unique.c_costar_clock", "charm.old_clock", new Txt("共演の針時計", "Co-star Clock"),
                new Txt("誰かが奥義を使えば、時計の針が進む。", "When someone uses their ultimate, the clock's hand advances."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_c_costar_clock_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_c_costar_clock_Power1),
            new UniqueDef("unique.c_costar_amulet", "charm.resonance_amulet", new Txt("舞台袖の護符", "Wings Amulet"),
                new Txt("主役が変われば、袖で待つ者が輝く。", "When the lead changes, whoever waits in the wings shines."),
                Power.CoStar, EquipmentItemsBalanceValues.Unique_unique_c_costar_amulet_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_c_costar_amulet_Power1),
            new UniqueDef("unique.c_triumph_pin", "charm.sunspoke_pin", new Txt("凱歌の陽針", "Triumph Sunpin"),
                new Txt("必殺の歌は、近くの仲間にも届く。", "The song of the finishing blow reaches nearby friends."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_c_triumph_pin_Power0, Power.StarShield, EquipmentItemsBalanceValues.Unique_unique_c_triumph_pin_Power1),
            new UniqueDef("unique.c_triumph_lotus", "charm.lotus_seal", new Txt("凱歌の蓮守り", "Triumph Lotus"),
                new Txt("勝ちどきの後には、傷が一つ癒える。", "After the victory cry, one wound mends."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_c_triumph_lotus_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_c_triumph_lotus_Power1),
            new UniqueDef("unique.c_triumph_feather", "charm.iron_feather", new Txt("凱旋の鉄羽", "Triumph Ironquill"),
                new Txt("奥義を使うたび、鉄の羽が一枚抜け落ちる。", "Each use of the ultimate sheds one iron feather."),
                Power.TriumphSong, EquipmentItemsBalanceValues.Unique_unique_c_triumph_feather_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_c_triumph_feather_Power1),
            new UniqueDef("unique.c_watchful_amulet", "charm.oak_amulet", new Txt("見張りの樫守り", "Watchful Oakguard"),
                new Txt("倒れそうな者を、遠くから見ている。", "It watches from afar those about to fall."),
                Power.WatchfulHand, EquipmentItemsBalanceValues.Unique_unique_c_watchful_amulet_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_c_watchful_amulet_Power1),
            new UniqueDef("unique.c_kindness_pendant", "charm.mender_locket", new Txt("情けの返し首飾り", "Kindness Pendant"),
                new Txt("与えた情けは、少し形を変えて返る。", "Kindness given returns in a slightly changed form."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_c_kindness_pendant_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_c_kindness_pendant_Power1),
            new UniqueDef("unique.c_kindness_circle", "charm.zephyr_ring", new Txt("情け巡りの風輪", "Kindness Gale Ring"),
                new Txt("巡る風は、必ず誰かの肩を押す。", "The circling wind always pushes someone's shoulder."),
                Power.KindnessReturns, EquipmentItemsBalanceValues.Unique_unique_c_kindness_circle_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_c_kindness_circle_Power1),
            new UniqueDef("unique.c_relay_ring", "charm.tailwind_ring", new Txt("継ぎの追い風指輪", "Relay Tailwind Ring"),
                new Txt("倒した余勢を、仲間の足に預ける。", "Lend the momentum of a kill to a friend's feet."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_c_relay_ring_Power0, Power.Momentum, EquipmentItemsBalanceValues.Unique_unique_c_relay_ring_Power1),
            new UniqueDef("unique.c_relay_badge", "charm.pursuit_badge", new Txt("継ぎ手の徽章", "Relay Badge"),
                new Txt("追いかける者の背を、同じ手が押す。", "The same hand pushes the back of the pursuer."),
                Power.RelayHand, EquipmentItemsBalanceValues.Unique_unique_c_relay_badge_Power0, Power.Tailwind, EquipmentItemsBalanceValues.Unique_unique_c_relay_badge_Power1),
            new UniqueDef("unique.c_umbral_heir", "charm.shadow_ring", new Txt("影の継承者の指輪", "Heir of Shadow Ring"),
                new Txt("倒れた闇は、次の肩に宿る。", "A fallen dark takes up residence on the next shoulder."),
                Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_c_umbral_heir_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_c_umbral_heir_Power1),
            new UniqueDef("unique.c_dark_legacy_bead", "charm.duskbead_necklace", new Txt("闇継ぎの珠", "Darkheir Beads"),
                new Txt("珠を転がすと、影が次の持ち主を探す。", "Roll the beads, and the shadow looks for its next owner."),
                Power.UmbralHeritage, EquipmentItemsBalanceValues.Unique_unique_c_dark_legacy_bead_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_c_dark_legacy_bead_Power1),
            new UniqueDef("unique.c_harvest_locket", "charm.ember_locket", new Txt("収穫の火種飾り", "Harvest Ember Locket"),
                new Txt("火の色が混ざるほど、収穫は豊かだ。", "The more fire-colors mix, the richer the harvest."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_c_harvest_locket_Power0, Power.Ember, EquipmentItemsBalanceValues.Unique_unique_c_harvest_locket_Power1),
            new UniqueDef("unique.c_harvest_frostpendant", "charm.frost_pendant", new Txt("収穫の霜飾り", "Harvest Frost Pendant"),
                new Txt("凍った実りは、日持ちがよい。", "Frozen harvest keeps well."),
                Power.ElementalHarvest, EquipmentItemsBalanceValues.Unique_unique_c_harvest_frostpendant_Power0, Power.Frost, EquipmentItemsBalanceValues.Unique_unique_c_harvest_frostpendant_Power1),
            new UniqueDef("unique.c_packfeast_charm", "charm.beasttongue_charm", new Txt("群れの糧の護符", "Packfeast Charm"),
                new Txt("獲物を分け合う群れは、飢えない。", "A pack that shares its kill never starves."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_c_packfeast_charm_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_c_packfeast_charm_Power1),
            new UniqueDef("unique.c_packfeast_amulet", "charm.resonance_amulet", new Txt("宴の群れ守り", "Feastpack Amulet"),
                new Txt("群れの中で、誰かが仕留めれば全員が食べられる。", "When one in the pack makes a kill, everyone eats."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_c_packfeast_amulet_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_c_packfeast_amulet_Power1),
            new UniqueDef("unique.c_packfeast_thorn", "charm.chain_necklace", new Txt("棘の群れ首飾り", "Thornpack Necklace"),
                new Txt("牙を持つ者の背に、棘の護りを。", "A thorned ward on the back of the fanged."),
                Power.PackFeast, EquipmentItemsBalanceValues.Unique_unique_c_packfeast_thorn_Power0, Power.Thorns, EquipmentItemsBalanceValues.Unique_unique_c_packfeast_thorn_Power1),
            new UniqueDef("unique.c_vanguard_necklace", "charm.iron_seal", new Txt("前衛の首飾り", "Vanguard Seal"),
                new Txt("最前線に立つ者は、護りが厚い。", "The one in the front line carries the thicker ward."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_c_vanguard_necklace_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_vanguard_necklace_Power1),
            new UniqueDef("unique.c_vanguard_retaliation", "charm.ironknot_ring", new Txt("矢面の鉄結び", "Brunt Ironknot"),
                new Txt("矢面に立てば、返す力も増す。", "Stand where arrows fall, and the answering strength grows."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_c_vanguard_retaliation_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_c_vanguard_retaliation_Power1),
            new UniqueDef("unique.c_medley_ring", "charm.skirmish_ring", new Txt("連奏の指輪", "Medley Ring"),
                new Txt("三つの音が揃って、はじめて曲になる。", "Only when three notes sound together does it become a tune."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_c_medley_ring_Power0, Power.Vigor, EquipmentItemsBalanceValues.Unique_unique_c_medley_ring_Power1),
            new UniqueDef("unique.c_medley_horn", "charm.war_drum", new Txt("三連奏の戦太鼓", "Three-Beat War Drum"),
                new Txt("違う拍子を叩き分ける者が、場を支配する。", "Whoever beats different rhythms commands the field."),
                Power.Medley, EquipmentItemsBalanceValues.Unique_unique_c_medley_horn_Power0, Power.UltimateSurge, EquipmentItemsBalanceValues.Unique_unique_c_medley_horn_Power1),
            new UniqueDef("unique.c_barehand_ring", "charm.stormcloud_locket", new Txt("素手の誇りの指輪", "Bare-Hand Pride Ring"),
                new Txt("技が尽きても、拳には自信がある。", "Even with the skills spent, the fists have confidence."),
                Power.BareHandedPride, EquipmentItemsBalanceValues.Unique_unique_c_barehand_ring_Power0, Power.Frenzy, EquipmentItemsBalanceValues.Unique_unique_c_barehand_ring_Power1),
            new UniqueDef("unique.c_ace_hand_locket", "charm.garnet_ring", new Txt("切り札入れの指輪", "Ace-Holder Ring"),
                new Txt("最後の一枚は、袖の内側に。", "The last card stays up the sleeve."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_c_ace_hand_locket_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_c_ace_hand_locket_Power1),
            new UniqueDef("unique.c_ace_finale_pendant", "charm.talon_pendant", new Txt("切り札の終曲飾り", "Ace Finale Pendant"),
                new Txt("終曲の前に、手札はすべて整っている。", "Before the finale, the whole hand is in order."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_c_ace_finale_pendant_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_c_ace_finale_pendant_Power1),
            new UniqueDef("unique.c_ace_executioner", "charm.fang_necklace", new Txt("切り札の処刑牙", "Trump Fang"),
                new Txt("奥の手を残した者は、容赦がない。", "One who keeps a hidden hand shows no mercy."),
                Power.AceInHand, EquipmentItemsBalanceValues.Unique_unique_c_ace_executioner_Power0, Power.Executioner, EquipmentItemsBalanceValues.Unique_unique_c_ace_executioner_Power1),
            new UniqueDef("unique.c_crystal_circuit_ring", "charm.dream_lens", new Txt("結晶循環の水晶", "Crystal Circuit Lens"),
                new Txt("エッセンスを使うたび、時間の流れが軽くなる。", "Each use of an essence makes time flow lighter."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_c_crystal_circuit_ring_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_c_crystal_circuit_ring_Power1),
            new UniqueDef("unique.c_crystal_circuit_stone", "charm.ink_stone", new Txt("結晶巡りの墨石", "Circuitstone Inkstone"),
                new Txt("砕いた結晶は、墨の中で再び巡る。", "A shattered crystal circulates again within the ink."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_c_crystal_circuit_stone_Power0, Power.CrystalResonance, EquipmentItemsBalanceValues.Unique_unique_c_crystal_circuit_stone_Power1),
            new UniqueDef("unique.c_crystal_circuit_clock", "charm.clockwork_charm", new Txt("巡り結晶の歯車", "Circuit Cog"),
                new Txt("歯車が噛み合うたび、結晶が一つ溶ける。", "With every meshing cog, a crystal melts."),
                Power.CrystalCircuit, EquipmentItemsBalanceValues.Unique_unique_c_crystal_circuit_clock_Power0, Power.Finale, EquipmentItemsBalanceValues.Unique_unique_c_crystal_circuit_clock_Power1),
            new UniqueDef("unique.c_shard_pendant", "charm.hearthstone", new Txt("欠片拾いの竈石", "Shardgleaner Hearthstone"),
                new Txt("拾った欠片が、体の芯を温める。", "Gathered shards warm the core of the body."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_c_shard_pendant_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_c_shard_pendant_Power1),
            new UniqueDef("unique.c_shard_soulring", "charm.shadow_ring", new Txt("欠片喰いの指輪", "Shardeater Ring"),
                new Txt("砕けたものを食べて、命に変える。", "Eat what is broken and turn it into life."),
                Power.ShardBoon, EquipmentItemsBalanceValues.Unique_unique_c_shard_soulring_Power0, Power.SoulSiphon, EquipmentItemsBalanceValues.Unique_unique_c_shard_soulring_Power1),
            new UniqueDef("unique.c_lifeline_lotus", "charm.lotus_seal", new Txt("継ぎの命の蓮", "Lifeline Lotus"),
                new Txt("溢れた命は、群れの牙に宿る。", "Overflowing life lodges in the pack's fangs."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_c_lifeline_lotus_Power0, Power.OverflowingLife, EquipmentItemsBalanceValues.Unique_unique_c_lifeline_lotus_Power1),
            new UniqueDef("unique.c_lifeline_barrier", "charm.deeproot_charm", new Txt("命綱の根守り", "Lifeline Rootcharm"),
                new Txt("護りが張られるたび、群れの牙が研がれる。", "With every ward raised, the pack's fangs are sharpened."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_c_lifeline_barrier_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_lifeline_barrier_Power1),
            new UniqueDef("unique.c_lifeline_secondwind", "charm.oak_amulet", new Txt("命綱の樫札", "Lifeline Oak Token"),
                new Txt("倒れかけた命は、群れに引き継がれる。", "A failing life is handed on to the pack."),
                Power.Lifeline, EquipmentItemsBalanceValues.Unique_unique_c_lifeline_secondwind_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_c_lifeline_secondwind_Power1),
            new UniqueDef("unique.c_omen_charm", "charm.dawnsilk_band", new Txt("夢見の前兆の腕輪", "Dreamomen Band"),
                new Txt("夢が揺れる前に、腕輪が先に知らせる。", "The band warns you before the dream trembles."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_c_omen_charm_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_omen_charm_Power1),
            new UniqueDef("unique.c_omen_devout", "charm.stardust_pendant", new Txt("予兆の祈り飾り", "Omen Devotion Pendant"),
                new Txt("出来事の前触れに、祈りが自然と重なる。", "Prayers pile up naturally at the harbingers of events."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_c_omen_devout_Power0, Power.Devotion, EquipmentItemsBalanceValues.Unique_unique_c_omen_devout_Power1),
            new UniqueDef("unique.c_omen_lucid", "charm.comet_pendant", new Txt("明晰な予兆の彗星", "Lucid Omen Comet"),
                new Txt("悪い夢ほど、明るく光って教えてくれる。", "The worse the dream, the brighter it glows to warn you."),
                Power.DreamOmen, EquipmentItemsBalanceValues.Unique_unique_c_omen_lucid_Power0, Power.LucidBoon, EquipmentItemsBalanceValues.Unique_unique_c_omen_lucid_Power1),
            new UniqueDef("unique.c_rearguard_charm", "charm.longshot_charm", new Txt("後衛の遠矢札", "Rearguard Longshot Charm"),
                new Txt("遠くにいる者ほど、狙いは正確だ。", "The farther away, the more precise the aim."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_c_rearguard_charm_Power0, Power.Overload, EquipmentItemsBalanceValues.Unique_unique_c_rearguard_charm_Power1),
            new UniqueDef("unique.c_rearguard_ring", "charm.windchime_charm", new Txt("後衛の鈴", "Rearguard Chime"),
                new Txt("後ろで鳴らす鈴が、前の者を導く。", "A chime rung at the back guides those in front."),
                Power.RearguardsWay, EquipmentItemsBalanceValues.Unique_unique_c_rearguard_ring_Power0, Power.Resonance, EquipmentItemsBalanceValues.Unique_unique_c_rearguard_ring_Power1),
            new UniqueDef("unique.c_ready_charm", "charm.bulwark_seal", new Txt("備えの札", "Ready Token"),
                new Txt("備えのある者は、最初の一手を笑って受け取る。", "The prepared receive the first move with a smile."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_c_ready_charm_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_c_ready_charm_Power1),
            new UniqueDef("unique.c_ready_aegis", "charm.tidewarden_brooch", new Txt("待ち受けの守り針", "Waiting Ward Pin"),
                new Txt("休んでいた時間が、そのまま鎧になる。", "Rested time becomes armor."),
                Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_c_ready_aegis_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_c_ready_aegis_Power1),
            new UniqueDef("unique.c_apothecary_charm", "charm.mender_locket", new Txt("薬師のお守り", "Apothecary Charm"),
                new Txt("瓶を空にするたび、時は少し前に進む。", "Each emptied bottle moves time a little forward."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_c_apothecary_charm_Power0, Power.Lifesteal, EquipmentItemsBalanceValues.Unique_unique_c_apothecary_charm_Power1),
            new UniqueDef("unique.c_apothecary_wind", "charm.zephyr_ring", new Txt("薬師の風輪", "Apothecary Windring"),
                new Txt("薬を分けた手は、冷えていない。", "The hand that shared the medicine is not cold."),
                Power.Apothecary, EquipmentItemsBalanceValues.Unique_unique_c_apothecary_wind_Power0, Power.SecondWind, EquipmentItemsBalanceValues.Unique_unique_c_apothecary_wind_Power1),
            new UniqueDef("unique.c_spillover_charm", "charm.skirmish_ring", new Txt("溢れ返しの指輪", "Spillover Ring"),
                new Txt("倒しきれなかった力は、誰かの傷になる。", "Force left over from a kill becomes someone else's wound."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_c_spillover_charm_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_c_spillover_charm_Power1),
            new UniqueDef("unique.c_spillover_talon", "charm.talon_pendant", new Txt("余り爪の首飾り", "Spare-Talon Pendant"),
                new Txt("余った爪が、次の獲物を掴む。", "The spare claw grabs the next prey."),
                Power.SpilloverStrike, EquipmentItemsBalanceValues.Unique_unique_c_spillover_talon_Power0, Power.Shatter, EquipmentItemsBalanceValues.Unique_unique_c_spillover_talon_Power1),
            new UniqueDef("set.shardknight.armor", "armor.guardian_plate", new Txt("砕盾騎士の胸甲", "Shardshield Knight Plate"), "set.shardknight"),
            new UniqueDef("set.shardknight.head", "head.knight_helm", new Txt("砕盾騎士の兜", "Shardshield Greathelm"), "set.shardknight"),
            new UniqueDef("set.shardknight.feet", "feet.knight_sabatons", new Txt("砕盾騎士の鉄靴", "Shardshield Sabatons"), "set.shardknight"),
            new UniqueDef("set.shardknight.weapon", "weapon.shield_maul", new Txt("砕盾騎士の大槌", "Shardshield Maul"), "set.shardknight"),
            new UniqueDef("set.shardknight.hands", "hands.oathpalm_gloves", new Txt("砕盾騎士の籠手", "Shardshield Gauntlets"), "set.shardknight"),
            new UniqueDef("set.shardknight.charm", "charm.snowbloom_charm", new Txt("砕盾騎士の雪華御守", "Shardshield Snowbloom Charm"), "set.shardknight"),
            new UniqueDef("set.bashbound.weapon", "weapon.shield_maul", new Txt("盾誓いの大槌", "Bashbound Maul"), "set.bashbound"),
            new UniqueDef("set.bashbound.hands", "hands.oath_gauntlets", new Txt("盾誓いの籠手", "Bashbound Gauntlets"), "set.bashbound"),
            new UniqueDef("set.bashbound.charm", "charm.guardian_seal", new Txt("盾誓いの護符", "Bashbound Seal"), "set.bashbound"),
            new UniqueDef("set.bashbound.head", "head.warden_visor", new Txt("盾誓いの面頬", "Bashbound Visor"), "set.bashbound"),
            new UniqueDef("set.bashbound.armor", "armor.wardsigil_vest", new Txt("盾誓いの加護胴衣", "Bashbound Wardvest"), "set.bashbound"),
            new UniqueDef("set.bashbound.feet", "feet.wardstep_sandals", new Txt("盾誓いの草鞋", "Bashbound Sandals"), "set.bashbound"),
            new UniqueDef("set.vanguardline.armor", "armor.citadel_plate", new Txt("陣頭の砦胸甲", "Frontline Citadel Plate"), "set.vanguardline"),
            new UniqueDef("set.vanguardline.head", "head.fortress_coif", new Txt("陣頭の頭巾", "Frontline Coif"), "set.vanguardline"),
            new UniqueDef("set.vanguardline.feet", "feet.bastion_sabatons", new Txt("陣頭の堅脛", "Frontline Greaves"), "set.vanguardline"),
            new UniqueDef("set.vanguardline.weapon", "weapon.gatehouse_maul", new Txt("陣頭の城門槌", "Frontline Gatemaul"), "set.vanguardline"),
            new UniqueDef("set.vanguardline.hands", "hands.bulwark_wraps", new Txt("陣頭の壁手巻", "Frontline Wallwraps"), "set.vanguardline"),
            new UniqueDef("set.vanguardline.charm", "charm.bulwark_seal", new Txt("陣頭の壁印章", "Frontline Wallseal"), "set.vanguardline"),
            new UniqueDef("set.rearmarks.weapon", "weapon.starsinger_bow", new Txt("後陣の星弓", "Rearline Starbow"), "set.rearmarks"),
            new UniqueDef("set.rearmarks.head", "head.longshot_cap", new Txt("後陣の遠矢帽", "Rearline Cap"), "set.rearmarks"),
            new UniqueDef("set.rearmarks.armor", "armor.star_cloak", new Txt("後陣の星外套", "Rearline Starcloak"), "set.rearmarks"),
            new UniqueDef("set.rearmarks.hands", "hands.bowmaster_bracers", new Txt("後陣の弓手腕当", "Rearline Bowbracers"), "set.rearmarks"),
            new UniqueDef("set.rearmarks.feet", "feet.starlit_moccasins", new Txt("後陣の星履", "Rearline Starmoccasins"), "set.rearmarks"),
            new UniqueDef("set.rearmarks.charm", "charm.longshot_charm", new Txt("後陣の遠当て御守", "Rearline Longshot Charm"), "set.rearmarks"),
            new UniqueDef("set.watchcircle.armor", "armor.lampkeeper_mantle", new Txt("輪守りの外套", "Ringwarden Mantle"), "set.watchcircle"),
            new UniqueDef("set.watchcircle.hands", "hands.mender_palms", new Txt("輪守りの手巻き", "Ringwarden Palms"), "set.watchcircle"),
            new UniqueDef("set.watchcircle.charm", "charm.mender_locket", new Txt("輪守りの首飾り", "Ringwarden Locket"), "set.watchcircle"),
            new UniqueDef("set.watchcircle.weapon", "weapon.dewclear_wand", new Txt("輪守りの細杖", "Ringwarden Wand"), "set.watchcircle"),
            new UniqueDef("set.watchcircle.head", "head.kindly_circlet", new Txt("輪守りの額冠", "Ringwarden Circlet"), "set.watchcircle"),
            new UniqueDef("set.watchcircle.feet", "feet.mender_shoes", new Txt("輪守りの靴", "Ringwarden Shoes"), "set.watchcircle"),
            new UniqueDef("set.relaychoir.hands", "hands.swiftpalm_gloves", new Txt("継ぎ歌の手袋", "Relaysong Gloves"), "set.relaychoir"),
            new UniqueDef("set.relaychoir.head", "head.healer_band", new Txt("合唱長の鉢巻", "Choirmaster Band"), "set.relaychoir"),
            new UniqueDef("set.relaychoir.charm", "charm.war_horn", new Txt("継ぎ歌の角笛", "Relaysong Horn"), "set.relaychoir"),
            new UniqueDef("set.relaychoir.weapon", "weapon.dream_wand", new Txt("継ぎ歌の細杖", "Relaysong Wand"), "set.relaychoir"),
            new UniqueDef("set.relaychoir.armor", "armor.healing_sash", new Txt("継ぎ歌の飾り帯", "Relaysong Sash"), "set.relaychoir"),
            new UniqueDef("set.relaychoir.feet", "feet.cometstride_shoes", new Txt("継ぎ歌の靴", "Relaysong Shoes"), "set.relaychoir"),
            new UniqueDef("set.packfeast.head", "head.beastcaller_antlers", new Txt("宴の角冠", "Feastpack Antlers"), "set.packfeast"),
            new UniqueDef("set.packfeast.armor", "armor.summoners_vest", new Txt("宴の群れ胴衣", "Feastpack Vest"), "set.packfeast"),
            new UniqueDef("set.packfeast.charm", "charm.beasttongue_charm", new Txt("宴の群れ護符", "Feastpack Totem"), "set.packfeast"),
            new UniqueDef("set.packfeast.weapon", "weapon.bone_flute", new Txt("宴の骨笛", "Feastpack Flute"), "set.packfeast"),
            new UniqueDef("set.packfeast.hands", "hands.summoner_bands", new Txt("宴の群れ腕輪", "Feastpack Bands"), "set.packfeast"),
            new UniqueDef("set.packfeast.feet", "feet.beastpaw_boots", new Txt("宴の群れ獣靴", "Feastpack Pawboots"), "set.packfeast"),
            new UniqueDef("set.lastblooms.weapon", "weapon.bone_flute", new Txt("弔い花の骨笛", "Funeral-Bloom Flute"), "set.lastblooms"),
            new UniqueDef("set.lastblooms.hands", "hands.summoner_bands", new Txt("弔い花の腕輪", "Funeral-Bloom Bands"), "set.lastblooms"),
            new UniqueDef("set.lastblooms.feet", "feet.beastpaw_boots", new Txt("弔い花の獣靴", "Funeral-Bloom Pawboots"), "set.lastblooms"),
            new UniqueDef("set.lastblooms.head", "head.leaf_wreath", new Txt("弔い花の花冠", "Funeral-Bloom Wreath"), "set.lastblooms"),
            new UniqueDef("set.lastblooms.armor", "armor.summoners_vest", new Txt("弔い花の胸衣", "Funeral-Bloom Vest"), "set.lastblooms"),
            new UniqueDef("set.lastblooms.charm", "charm.beasttongue_charm", new Txt("弔い花の獣語護符", "Funeral-Bloom Beastcharm"), "set.lastblooms"),
            new UniqueDef("set.pinpoint.weapon", "weapon.longspike_bow", new Txt("一点狙いの長弓", "Single-Mark Longbow"), "set.pinpoint"),
            new UniqueDef("set.pinpoint.head", "head.eye_patch", new Txt("狙い澄ます眼帯", "Steady-Aim Eyepatch"), "set.pinpoint"),
            new UniqueDef("set.pinpoint.hands", "hands.precise_fingerless", new Txt("照準の指なし手袋", "Aimpoint Fingerless Gloves"), "set.pinpoint"),
            new UniqueDef("set.pinpoint.armor", "armor.hunter_vest", new Txt("一点狙いの胴衣", "Single-Mark Vest"), "set.pinpoint"),
            new UniqueDef("set.pinpoint.feet", "feet.deadeye_leggings", new Txt("一点狙いの脚絆", "Single-Mark Leggings"), "set.pinpoint"),
            new UniqueDef("set.pinpoint.charm", "charm.keeneye_charm", new Txt("一点狙いの鋭眼御守", "Single-Mark Keeneye Charm"), "set.pinpoint"),
            new UniqueDef("set.wanderblades.weapon", "weapon.lightning_pair", new Txt("渡り雷の双刃", "Driftbolt Twinblades"), "set.wanderblades"),
            new UniqueDef("set.wanderblades.hands", "hands.thief_gloves", new Txt("渡り鳥の細手袋", "Migrant Fine Gloves"), "set.wanderblades"),
            new UniqueDef("set.wanderblades.feet", "feet.wolfstride_boots", new Txt("渡り鳥の長靴", "Migrant Longboots"), "set.wanderblades"),
            new UniqueDef("set.wanderblades.head", "head.gale_hood", new Txt("渡り鳥の疾風頭巾", "Migrant Galehood"), "set.wanderblades"),
            new UniqueDef("set.wanderblades.armor", "armor.dancer_garb", new Txt("渡り鳥の舞衣", "Migrant Dancewear"), "set.wanderblades"),
            new UniqueDef("set.wanderblades.charm", "charm.skirmish_ring", new Txt("渡り鳥の遊撃指輪", "Migrant Skirmish Ring"), "set.wanderblades"),
            new UniqueDef("set.fortuneedge.weapon", "weapon.twin_fang", new Txt("運刃の双牙", "Fortune-Edge Fangs"), "set.fortuneedge"),
            new UniqueDef("set.fortuneedge.head", "head.hawkeye_band", new Txt("運刃の鷹目帯", "Fortune-Edge Band"), "set.fortuneedge"),
            new UniqueDef("set.fortuneedge.hands", "hands.duelist_gloves", new Txt("運刃の勝負手袋", "Fortune-Edge Gloves"), "set.fortuneedge"),
            new UniqueDef("set.fortuneedge.armor", "armor.ambush_vest", new Txt("運刃の奇襲胴衣", "Fortune-Edge Ambushvest"), "set.fortuneedge"),
            new UniqueDef("set.fortuneedge.feet", "feet.hunter_striders", new Txt("運刃の追撃脚当", "Fortune-Edge Striders"), "set.fortuneedge"),
            new UniqueDef("set.fortuneedge.charm", "charm.garnet_ring", new Txt("運刃の紅玉指輪", "Fortune-Edge Garnet Ring"), "set.fortuneedge"),
            new UniqueDef("set.runupcharge.weapon", "weapon.tower_lance", new Txt("突撃隊の長槍", "Charge-Corps Lance"), "set.runupcharge"),
            new UniqueDef("set.runupcharge.hands", "hands.reach_bracers", new Txt("突撃隊の腕当て", "Charge-Corps Bracers"), "set.runupcharge"),
            new UniqueDef("set.runupcharge.feet", "feet.storm_boots", new Txt("突撃隊の嵐靴", "Charge-Corps Stormboots"), "set.runupcharge"),
            new UniqueDef("set.runupcharge.head", "head.horned_helm", new Txt("突撃隊の角兜", "Charge-Corps Horned Helm"), "set.runupcharge"),
            new UniqueDef("set.runupcharge.armor", "armor.stormfront_vest", new Txt("突撃隊の嵐胴衣", "Charge-Corps Stormvest"), "set.runupcharge"),
            new UniqueDef("set.runupcharge.charm", "charm.skirmish_ring", new Txt("突撃隊の遊撃指輪", "Charge-Corps Skirmish Ring"), "set.runupcharge"),
            new UniqueDef("set.medleyband.weapon", "weapon.star_harp", new Txt("楽団の竪琴", "Ensemble Harp"), "set.medleyband"),
            new UniqueDef("set.medleyband.head", "head.sage_hat", new Txt("楽団長の帽子", "Conductor's Hat"), "set.medleyband"),
            new UniqueDef("set.medleyband.charm", "charm.windchime_charm", new Txt("楽団の風鈴", "Ensemble Chime"), "set.medleyband"),
            new UniqueDef("set.medleyband.armor", "armor.ink_robe", new Txt("楽団の墨染め衣", "Ensemble Inkrobe"), "set.medleyband"),
            new UniqueDef("set.medleyband.hands", "hands.manuscript_gloves", new Txt("楽団の楽譜手袋", "Ensemble Scorekeeper Gloves"), "set.medleyband"),
            new UniqueDef("set.medleyband.feet", "feet.star_slippers", new Txt("楽団の舞台靴", "Ensemble Stagesteps"), "set.medleyband"),
            new UniqueDef("set.gamblertrump.weapon", "weapon.dream_wand", new Txt("勝負師の細杖", "Gambler's Wand"), "set.gamblertrump"),
            new UniqueDef("set.gamblertrump.head", "head.dream_circlet", new Txt("勝負師の額冠", "Gambler's Circlet"), "set.gamblertrump"),
            new UniqueDef("set.gamblertrump.charm", "charm.clockwork_charm", new Txt("勝負師の懐中時計", "Gambler's Pocket Watch"), "set.gamblertrump"),
            new UniqueDef("set.gamblertrump.armor", "armor.traveler_coat", new Txt("勝負師の旅外套", "Gambler's Travelcoat"), "set.gamblertrump"),
            new UniqueDef("set.gamblertrump.hands", "hands.spell_gloves", new Txt("勝負師の呪文手袋", "Gambler's Spellgloves"), "set.gamblertrump"),
            new UniqueDef("set.gamblertrump.feet", "feet.star_slippers", new Txt("勝負師の星靴", "Gambler's Starshoes"), "set.gamblertrump"),
            new UniqueDef("set.barehand.weapon", "weapon.tortoise_bokken", new Txt("無手の誇りの木刀", "Bare-Hand Bokken"), "set.barehand"),
            new UniqueDef("set.barehand.hands", "hands.monk_wraps", new Txt("無手の誇りの巻き布", "Bare-Hand Wraps"), "set.barehand"),
            new UniqueDef("set.barehand.charm", "charm.hunters_seal", new Txt("無手の誇りの印", "Bare-Hand Seal"), "set.barehand"),
            new UniqueDef("set.barehand.head", "head.berserker_mask", new Txt("無手の誇りの面", "Bare-Hand Mask"), "set.barehand"),
            new UniqueDef("set.barehand.armor", "armor.monk_garb", new Txt("無手の誇りの道着", "Bare-Hand Gi"), "set.barehand"),
            new UniqueDef("set.barehand.feet", "feet.stalker_boots", new Txt("無手の誇りの足袋", "Bare-Hand Tabi"), "set.barehand"),
            new UniqueDef("set.crystalcircuit.head", "head.meditation_band", new Txt("回路の瞑想帯", "Circuit Meditation Band"), "set.crystalcircuit"),
            new UniqueDef("set.crystalcircuit.hands", "hands.alchemist_gloves", new Txt("回路の調合手袋", "Circuit Compounder Gloves"), "set.crystalcircuit"),
            new UniqueDef("set.crystalcircuit.charm", "charm.dream_lens", new Txt("回路の水晶玉", "Circuit Crystal Lens"), "set.crystalcircuit"),
            new UniqueDef("set.crystalcircuit.weapon", "weapon.windhowl_staff", new Txt("回路の導杖", "Circuit Conductor Staff"), "set.crystalcircuit"),
            new UniqueDef("set.crystalcircuit.armor", "armor.mirrorsilk_robe", new Txt("回路の鏡絹衣", "Circuit Mirrorsilk"), "set.crystalcircuit"),
            new UniqueDef("set.crystalcircuit.feet", "feet.whisperweave_shoes", new Txt("回路の囁き靴", "Circuit Whisperweave Shoes"), "set.crystalcircuit"),
            new UniqueDef("set.dreamvigil.head", "head.dream_veil", new Txt("寝ずの番の面紗", "Vigil Veil"), "set.dreamvigil"),
            new UniqueDef("set.dreamvigil.armor", "armor.morningdew_robe", new Txt("寝ずの番の朝露衣", "Vigil Dewrobe"), "set.dreamvigil"),
            new UniqueDef("set.dreamvigil.charm", "charm.stardust_pendant", new Txt("寝ずの番の星飾り", "Vigil Stardust"), "set.dreamvigil"),
            new UniqueDef("set.dreamvigil.weapon", "weapon.pilgrim_staff", new Txt("寝ずの番の巡礼杖", "Vigil Pilgrim Staff"), "set.dreamvigil"),
            new UniqueDef("set.dreamvigil.hands", "hands.healer_hands", new Txt("寝ずの番の手袋", "Vigil Gloves"), "set.dreamvigil"),
            new UniqueDef("set.dreamvigil.feet", "feet.pilgrim_boots", new Txt("寝ずの番の巡礼靴", "Vigil Pilgrim Boots"), "set.dreamvigil"),
            new UniqueDef("set.tithebound.weapon", "weapon.war_axe", new Txt("供犠の戦斧", "Tithe Hatchet"), "set.tithebound"),
            new UniqueDef("set.tithebound.armor", "armor.spiked_plate", new Txt("供犠の棘甲", "Tithe Spikeplate"), "set.tithebound"),
            new UniqueDef("set.tithebound.charm", "charm.blaze_brooch", new Txt("供犠の炎針", "Tithe Flamepin"), "set.tithebound"),
            new UniqueDef("set.tithebound.head", "head.thorn_circlet", new Txt("供犠の茨冠", "Tithe Thorn Crown"), "set.tithebound"),
            new UniqueDef("set.tithebound.hands", "hands.thorn_wraps", new Txt("供犠の棘手巻", "Tithe Barbwraps"), "set.tithebound"),
            new UniqueDef("set.tithebound.feet", "feet.chain_greaves", new Txt("供犠の鎖脛当", "Tithe Chaingreaves"), "set.tithebound"),
            new UniqueDef("set.prismdance.hands", "hands.star_rings", new Txt("舞い星の環", "Dancing-Star Rings"), "set.prismdance"),
            new UniqueDef("set.prismdance.head", "head.star_diadem", new Txt("舞い星の冠", "Dancing-Star Diadem"), "set.prismdance"),
            new UniqueDef("set.prismdance.feet", "feet.dancer_shoes", new Txt("舞い星の踊り靴", "Dancing-Star Shoes"), "set.prismdance"),
            new UniqueDef("set.prismdance.weapon", "weapon.evening_fan", new Txt("舞い星の夕扇", "Dancing-Star Fan"), "set.prismdance"),
            new UniqueDef("set.prismdance.armor", "armor.aurora_wrap", new Txt("舞い星の羽衣", "Dancing-Star Aurora"), "set.prismdance"),
            new UniqueDef("set.prismdance.charm", "charm.halo_charm", new Txt("舞い星の光環御守", "Dancing-Star Halo Charm"), "set.prismdance"),
            new UniqueDef("unique.a_frostembrace_coat", "armor.frost_coat", new Txt("寒気抱きの外衣", "Chillembrace Coat"),
                new Txt("氷を抱いた腕の中では、盾が先に砕ける。", "In arms that embrace ice, the shield breaks first."),
                Power.ShieldbreakBurst, EquipmentItemsBalanceValues.Unique_unique_a_frostembrace_coat_Power0, Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_frostembrace_coat_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_Q_EmbracingTheChill" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_frostembrace_coat_Link } },
            new UniqueDef("unique.a_prison_plate", "armor.citadel_plate", new Txt("牢の砦胸甲", "Gaol Citadel Plate"),
                new Txt("捕らわれても、動じなければ砦になる。", "Even when caged, the unflinching become a fortress."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_a_prison_plate_Power0, Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_prison_plate_Power1) { Link = new LinkDef { Requires = new[] { "Gem_U_SoulPrison" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_prison_plate_Link } },
            new UniqueDef("unique.a_resolve_scale", "armor.scale_coat", new Txt("決意の竜鱗", "Resolve Dragonscale"),
                new Txt("決意した者が先に立つのは、竜の掟。", "It is the dragon's law that the resolved stand first."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_a_resolve_scale_Power0, Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_resolve_scale_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_D_Resolve", "St_Q_Discipline" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_a_resolve_scale_Link } },
            new UniqueDef("unique.a_deception_jacket", "armor.duskweave_jacket", new Txt("撹乱の影上着", "Deception Duskweave Jacket"),
                new Txt("包囲を抜けた後、影が一つ多く残っている。", "After breaking out, one extra shadow remains."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_deception_jacket_Power0, Power.Umbra, EquipmentItemsBalanceValues.Unique_unique_a_deception_jacket_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_R_Deception", "St_Q_DeathMark" }, Kind = LinkKind.MemorySurge, Value = EquipmentItemsBalanceValues.Unique_unique_a_deception_jacket_Link } },
            new UniqueDef("unique.a_unbowed_cuirass", "armor.basalt_cuirass", new Txt("不撓の玄武胸甲", "Unbowed Basalt Cuirass"),
                new Txt("押されても、玄武は元の場所へ戻る。", "Pushed aside, the basalt returns to its place."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_cuirass_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_cuirass_Power1),
            new UniqueDef("unique.a_unbowed_plate", "armor.guardian_plate", new Txt("撓まぬ護り胸当て", "Springback Guardian Plate"),
                new Txt("ふっ飛ばされるほど、護りは強く立つ。", "The farther you are thrown, the stronger the ward stands."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_plate_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_plate_Power1),
            new UniqueDef("unique.a_unbowed_vest", "armor.ironbark_vest", new Txt("転ばぬ鉄樹衣", "Steadfast Ironbark Vest"),
                new Txt("転んでも、木の根は抜けない。", "Even when you stumble, the roots do not pull out."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_vest_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_vest_Power1),
            new UniqueDef("unique.a_unbowed_ready", "armor.buckler_vest", new Txt("撓まぬ備えの小盾衣", "Springback Ready Vest"),
                new Txt("倒されても、立ち上がりに間はいらない。", "Even when knocked down, there is no delay in rising."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_ready_Power0, Power.ReadyGuard, EquipmentItemsBalanceValues.Unique_unique_a_unbowed_ready_Power1),
            new UniqueDef("unique.f_resolve_greaves", "feet.guard_sabatons", new Txt("決意の前衛靴", "Resolute Vanguard Sabatons"),
                new Txt("最前列は、最初に決めた者の席。", "The front row belongs to the first to decide."),
                Power.VanguardsOath, EquipmentItemsBalanceValues.Unique_unique_f_resolve_greaves_Power0, Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_resolve_greaves_Power1) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_M_Charge", "St_D_Resolve" }, Kind = LinkKind.MemoryDamage, Value = EquipmentItemsBalanceValues.Unique_unique_f_resolve_greaves_Link } },
            new UniqueDef("unique.f_wary_shoes", "feet.mist_shoes", new Txt("疑心の忍び足", "Wary Footfalls"),
                new Txt("疑い深い足は、危険の一歩手前で止まる。", "Wary feet stop one step before danger."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_wary_shoes_Power0, Power.Retaliation, EquipmentItemsBalanceValues.Unique_unique_f_wary_shoes_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia", "St_M_FastFeet" }, Kind = LinkKind.MemoryHaste, Value = EquipmentItemsBalanceValues.Unique_unique_f_wary_shoes_Link } },
            new UniqueDef("unique.f_faith_boots", "feet.stone_boots", new Txt("信仰の石靴", "Faithstone Boots"),
                new Txt("祈りの数だけ、根を張る。", "It roots itself once for every prayer."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_faith_boots_Power0, Power.StillWater, EquipmentItemsBalanceValues.Unique_unique_f_faith_boots_Power1) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith", "Hero_Vesper" }, Kind = LinkKind.Guard, Value = EquipmentItemsBalanceValues.Unique_unique_f_faith_boots_Link } },
            new UniqueDef("unique.f_unbowed_boots", "feet.ballast_boots", new Txt("不撓の重し靴", "Unbowed Ballast Boots"),
                new Txt("突き飛ばされても、立ち上がる速さは変わらない。", "Knocked down, you rise just as fast."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_boots_Power0, Power.Barrier, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_boots_Power1),
            new UniqueDef("unique.f_unbowed_greaves", "feet.rampart_greaves", new Txt("揺るがぬ砦脛", "Steadfast Rampart Greaves"),
                new Txt("押されても、足は元の場所を覚えている。", "Pushed aside, the feet remember where they stood."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_greaves_Power0, Power.Aegis, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_greaves_Power1),
            new UniqueDef("unique.f_unbowed_stone", "feet.stone_boots", new Txt("転ばぬ石靴", "Stonestep Boots"),
                new Txt("転んだ数だけ、次の一歩は慎重になる。", "Each fall makes the next step more careful."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_stone_Power0, Power.Bulwark, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_stone_Power1),
            new UniqueDef("unique.f_sentry_boots", "feet.winterhide_boots", new Txt("歩哨の冬靴", "Sentry Winterboots"),
                new Txt("凍える夜の見張りは、最後に報われる。", "The watch on a freezing night is rewarded in the end."),
                Power.Breakout, EquipmentItemsBalanceValues.Unique_unique_f_sentry_boots_Power0, Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_sentry_boots_Power1),
            new UniqueDef("unique.f_unbowed_runup", "feet.gale_greaves", new Txt("撓まぬ助走靴", "Springback Runup Greaves"),
                new Txt("押し返された距離が、次の助走になる。", "The distance you were pushed back becomes the next run-up."),
                Power.UnbowedMind, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_runup_Power0, Power.RunUp, EquipmentItemsBalanceValues.Unique_unique_f_unbowed_runup_Power1),
            new UniqueDef("set.breakoutcorps.armor", "armor.stormfront_vest", new Txt("突破隊の嵐衣", "Breakout Stormvest"), "set.breakoutcorps"),
            new UniqueDef("set.breakoutcorps.feet", "feet.iron_greaves", new Txt("突破隊の鉄脛", "Breakout Greaves"), "set.breakoutcorps"),
            new UniqueDef("set.breakoutcorps.head", "head.iron_coif", new Txt("突破隊の鎖頭巾", "Breakout Coif"), "set.breakoutcorps"),
            new UniqueDef("set.breakoutcorps.weapon", "weapon.gatehouse_maul", new Txt("突破隊の破城槌", "Breakout Ram"), "set.breakoutcorps"),
            new UniqueDef("set.breakoutcorps.hands", "hands.ironvein_gauntlets", new Txt("突破隊の鉄手", "Breakout Ironhands"), "set.breakoutcorps"),
            new UniqueDef("set.breakoutcorps.charm", "charm.iron_feather", new Txt("突破隊の鉄羽根", "Breakout Ironfeather"), "set.breakoutcorps"),
            new UniqueDef("set.boss_demon.weapon", "weapon.shield_maul", new Txt("震根の槌", "Quakeroot Maul"), "set.boss_demon", "boss_demon.weapon"),
            new UniqueDef("set.boss_demon.armor", "armor.root_mail", new Txt("不退の樹皮", "Unyielding Bark"), "set.boss_demon", "boss_demon.armor"),
            new UniqueDef("set.boss_demon.charm", "charm.pulsing_core", new Txt("萌発の核", "Sprouting Core"), "set.boss_demon", "boss_demon.charm"),
            new UniqueDef("set.boss_demon.head", "head.moss_crown", new Txt("放射の枝冠", "Radial Branch Crown"), "set.boss_demon", "boss_demon.head"),
            new UniqueDef("set.boss_demon.hands", "hands.rootgrip_gloves", new Txt("溜め裂きの手甲", "Delayed Rending Grips"), "set.boss_demon", "boss_demon.hands"),
            new UniqueDef("set.boss_demon.feet", "feet.rooted_boots", new Txt("瞬駆の根履", "Blinkstride Treads"), "set.boss_demon", "boss_demon.feet"),
        }.Concat(BossProfiles.CreateSkollPieces()).Concat(BossProfiles.CreateInfernusPieces()).Concat(BossProfiles.CreateInkPieces())
            .Concat(BossProfiles.CreateNyxPieces()).Concat(BossProfiles.CreateErebosPieces()).Concat(BossProfiles.CreateSeekerPieces())
            .Concat(BossProfiles.CreateAzurakPieces()).Concat(BossProfiles.CreatePrimusPieces())
            .Concat(BossProfiles.CreateLightPieces()).Concat(BossProfiles.CreateMawPieces()).Concat(BossProfiles.CreateObliviaxPieces()).Concat(BossProfiles.CreatePolarisPieces()).ToArray();

        public static readonly IReadOnlyList<SetDef> Sets = SetBalanceValues.CreateSets().Concat(new[]
        {
            new SetDef
            {
                Id = "set.boss_demon", Name = new Txt("荒ぶる樹界", "Rampaging Grove"),
                BossTypeName = "Mon_Forest_BossDemon",
                TwoPiece = Array.Empty<StatLine>(),
                ThreePiece = Array.Empty<PowerLine>(),
                SixPiece = Array.Empty<PowerLine>(),
                BossStages = new[] { new BossSetStage(2, "boss_demon.stage2"), new BossSetStage(3, "boss_demon.stage3"), new BossSetStage(6, "boss_demon.stage6") },
                BossReward = BossProfiles.DemonRewardId,
                LinkStages = new[]
                {
                    new SetLinkStage(2, new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.BossReward, Value = 1 }),
                    new SetLinkStage(4, new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.BossReward, Value = 2 }),
                    new SetLinkStage(6, new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.BossReward, Value = 3 }),
                },
            },
        }).Concat(BossProfiles.CreateSkollSets()).Concat(BossProfiles.CreateInfernusSets()).Concat(BossProfiles.CreateInkSets())
            .Concat(BossProfiles.CreateNyxSets()).Concat(BossProfiles.CreateErebosSets()).Concat(BossProfiles.CreateSeekerSets())
            .Concat(BossProfiles.CreateAzurakSets()).Concat(BossProfiles.CreatePrimusSets())
            .Concat(BossProfiles.CreateLightSets()).Concat(BossProfiles.CreateMawSets()).Concat(BossProfiles.CreateObliviaxSets()).Concat(BossProfiles.CreatePolarisSets()).ToArray();

        /// <summary>
        /// 48組すべてに SixPiece を入れ終えたら true にする（v1.31）。false の間は未入力のセットを許容し、
        /// true にすると Content の検証テストが SixPiece の無いセットを失敗として報告する。
        /// </summary>
        public const bool SixPieceSetsComplete = true;

        /// <summary>セットの部位数（固有品のうち SetId が一致するもの）。</summary>
        public static int SetPieceCount(string setId)
        {
            int n = 0;
            foreach (var u in Uniques) if (u.SetId == setId) n++;
            return n;
        }

        public static SetDef GetSet(string id)
        {
            foreach (var s in Sets)
                if (s.Id == id) return s;
            return null;
        }

        private static readonly Dictionary<Slot, AffixDef[]> AffixPools = new Dictionary<Slot, AffixDef[]>
        {
            [Slot.Weapon] = new[]
            {
                new AffixDef(Stat.AttackFlat, AffixPoolsBalance.Weapon_AttackFlat_Min, AffixPoolsBalance.Weapon_AttackFlat_Max, AffixPoolsBalance.Weapon_AttackFlat_Weight),
                new AffixDef(Stat.AttackPct, AffixPoolsBalance.Weapon_AttackPct_Min, AffixPoolsBalance.Weapon_AttackPct_Max, AffixPoolsBalance.Weapon_AttackPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.PowerFlat, AffixPoolsBalance.Weapon_PowerFlat_Min, AffixPoolsBalance.Weapon_PowerFlat_Max, AffixPoolsBalance.Weapon_PowerFlat_Weight),
                new AffixDef(Stat.PowerPct, AffixPoolsBalance.Weapon_PowerPct_Min, AffixPoolsBalance.Weapon_PowerPct_Max, AffixPoolsBalance.Weapon_PowerPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.AttackSpeedPct, AffixPoolsBalance.Weapon_AttackSpeedPct_Min, AffixPoolsBalance.Weapon_AttackSpeedPct_Max, AffixPoolsBalance.Weapon_AttackSpeedPct_Weight),
                new AffixDef(Stat.CritChancePct, AffixPoolsBalance.Weapon_CritChancePct_Min, AffixPoolsBalance.Weapon_CritChancePct_Max, AffixPoolsBalance.Weapon_CritChancePct_Weight),
                new AffixDef(Stat.CritDamagePct, AffixPoolsBalance.Weapon_CritDamagePct_Min, AffixPoolsBalance.Weapon_CritDamagePct_Max, AffixPoolsBalance.Weapon_CritDamagePct_Weight),
                new AffixDef(Stat.Haste, AffixPoolsBalance.Weapon_Haste_Min, AffixPoolsBalance.Weapon_Haste_Max, AffixPoolsBalance.Weapon_Haste_Weight),
                new AffixDef(Stat.FireAmp, AffixPoolsBalance.Weapon_FireAmp_Min, AffixPoolsBalance.Weapon_FireAmp_Max, AffixPoolsBalance.Weapon_FireAmp_Weight),
                new AffixDef(Stat.LightAmp, AffixPoolsBalance.Weapon_LightAmp_Min, AffixPoolsBalance.Weapon_LightAmp_Max, AffixPoolsBalance.Weapon_LightAmp_Weight),
                new AffixDef(Stat.DarkAmp, AffixPoolsBalance.Weapon_DarkAmp_Min, AffixPoolsBalance.Weapon_DarkAmp_Max, AffixPoolsBalance.Weapon_DarkAmp_Weight),
                new AffixDef(Stat.AttackRangePct, AffixPoolsBalance.Weapon_AttackRangePct_Min, AffixPoolsBalance.Weapon_AttackRangePct_Max, AffixPoolsBalance.Weapon_AttackRangePct_Weight),
                new AffixDef(Stat.ColdAmp, AffixPoolsBalance.Weapon_ColdAmp_Min, AffixPoolsBalance.Weapon_ColdAmp_Max, AffixPoolsBalance.Weapon_ColdAmp_Weight),
                new AffixDef(Stat.MaxHealthPct, AffixPoolsBalance.Weapon_MaxHealthPct_Min, AffixPoolsBalance.Weapon_MaxHealthPct_Max, AffixPoolsBalance.Weapon_MaxHealthPct_Weight), // v1.21
                new AffixDef(Stat.Tenacity, AffixPoolsBalance.Weapon_Tenacity_Min, AffixPoolsBalance.Weapon_Tenacity_Max, AffixPoolsBalance.Weapon_Tenacity_Weight), // v1.21
            },
            [Slot.Armor] = new[]
            {
                new AffixDef(Stat.MaxHealthPct, AffixPoolsBalance.Armor_MaxHealthPct_Min, AffixPoolsBalance.Armor_MaxHealthPct_Max, AffixPoolsBalance.Armor_MaxHealthPct_Weight),
                new AffixDef(Stat.MaxHealthFlat, AffixPoolsBalance.Armor_MaxHealthFlat_Min, AffixPoolsBalance.Armor_MaxHealthFlat_Max, AffixPoolsBalance.Armor_MaxHealthFlat_Weight),
                new AffixDef(Stat.Armor, AffixPoolsBalance.Armor_Armor_Min, AffixPoolsBalance.Armor_Armor_Max, AffixPoolsBalance.Armor_Armor_Weight),
                new AffixDef(Stat.HealthRegen, AffixPoolsBalance.Armor_HealthRegen_Min, AffixPoolsBalance.Armor_HealthRegen_Max, AffixPoolsBalance.Armor_HealthRegen_Weight),
                new AffixDef(Stat.Tenacity, AffixPoolsBalance.Armor_Tenacity_Min, AffixPoolsBalance.Armor_Tenacity_Max, AffixPoolsBalance.Armor_Tenacity_Weight),
                new AffixDef(Stat.MoveSpeedPct, AffixPoolsBalance.Armor_MoveSpeedPct_Min, AffixPoolsBalance.Armor_MoveSpeedPct_Max, AffixPoolsBalance.Armor_MoveSpeedPct_Weight),
                new AffixDef(Stat.Haste, AffixPoolsBalance.Armor_Haste_Min, AffixPoolsBalance.Armor_Haste_Max, AffixPoolsBalance.Armor_Haste_Weight),
                new AffixDef(Stat.LightAmp, AffixPoolsBalance.Armor_LightAmp_Min, AffixPoolsBalance.Armor_LightAmp_Max, AffixPoolsBalance.Armor_LightAmp_Weight),
                new AffixDef(Stat.PowerFlat, AffixPoolsBalance.Armor_PowerFlat_Min, AffixPoolsBalance.Armor_PowerFlat_Max, AffixPoolsBalance.Armor_PowerFlat_Weight), // v1.21
                new AffixDef(Stat.PowerPct, AffixPoolsBalance.Armor_PowerPct_Min, AffixPoolsBalance.Armor_PowerPct_Max, AffixPoolsBalance.Armor_PowerPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.AttackFlat, AffixPoolsBalance.Armor_AttackFlat_Min, AffixPoolsBalance.Armor_AttackFlat_Max, AffixPoolsBalance.Armor_AttackFlat_Weight), // v1.21
                new AffixDef(Stat.AttackPct, AffixPoolsBalance.Armor_AttackPct_Min, AffixPoolsBalance.Armor_AttackPct_Max, AffixPoolsBalance.Armor_AttackPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.ColdAmp, AffixPoolsBalance.Armor_ColdAmp_Min, AffixPoolsBalance.Armor_ColdAmp_Max, AffixPoolsBalance.Armor_ColdAmp_Weight), // v1.21
                new AffixDef(Stat.DarkAmp, AffixPoolsBalance.Armor_DarkAmp_Min, AffixPoolsBalance.Armor_DarkAmp_Max, AffixPoolsBalance.Armor_DarkAmp_Weight), // v1.21
                new AffixDef(Stat.FireAmp, AffixPoolsBalance.Armor_FireAmp_Min, AffixPoolsBalance.Armor_FireAmp_Max, AffixPoolsBalance.Armor_FireAmp_Weight), // v1.21
                new AffixDef(Stat.HealPower, AffixPoolsBalance.Armor_HealPower_Min, AffixPoolsBalance.Armor_HealPower_Max, AffixPoolsBalance.Armor_HealPower_Weight),
                new AffixDef(Stat.ShieldPower, AffixPoolsBalance.Armor_ShieldPower_Min, AffixPoolsBalance.Armor_ShieldPower_Max, AffixPoolsBalance.Armor_ShieldPower_Weight),
            },
            [Slot.Charm] = new[]
            {
                new AffixDef(Stat.Haste, AffixPoolsBalance.Charm_Haste_Min, AffixPoolsBalance.Charm_Haste_Max, AffixPoolsBalance.Charm_Haste_Weight),
                new AffixDef(Stat.MoveSpeedPct, AffixPoolsBalance.Charm_MoveSpeedPct_Min, AffixPoolsBalance.Charm_MoveSpeedPct_Max, AffixPoolsBalance.Charm_MoveSpeedPct_Weight),
                new AffixDef(Stat.CritChancePct, AffixPoolsBalance.Charm_CritChancePct_Min, AffixPoolsBalance.Charm_CritChancePct_Max, AffixPoolsBalance.Charm_CritChancePct_Weight),
                new AffixDef(Stat.AttackFlat, AffixPoolsBalance.Charm_AttackFlat_Min, AffixPoolsBalance.Charm_AttackFlat_Max, AffixPoolsBalance.Charm_AttackFlat_Weight),
                new AffixDef(Stat.AttackPct, AffixPoolsBalance.Charm_AttackPct_Min, AffixPoolsBalance.Charm_AttackPct_Max, AffixPoolsBalance.Charm_AttackPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.PowerFlat, AffixPoolsBalance.Charm_PowerFlat_Min, AffixPoolsBalance.Charm_PowerFlat_Max, AffixPoolsBalance.Charm_PowerFlat_Weight),
                new AffixDef(Stat.PowerPct, AffixPoolsBalance.Charm_PowerPct_Min, AffixPoolsBalance.Charm_PowerPct_Max, AffixPoolsBalance.Charm_PowerPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.MaxHealthPct, AffixPoolsBalance.Charm_MaxHealthPct_Min, AffixPoolsBalance.Charm_MaxHealthPct_Max, AffixPoolsBalance.Charm_MaxHealthPct_Weight),
                new AffixDef(Stat.HealthRegen, AffixPoolsBalance.Charm_HealthRegen_Min, AffixPoolsBalance.Charm_HealthRegen_Max, AffixPoolsBalance.Charm_HealthRegen_Weight),
                new AffixDef(Stat.ColdAmp, AffixPoolsBalance.Charm_ColdAmp_Min, AffixPoolsBalance.Charm_ColdAmp_Max, AffixPoolsBalance.Charm_ColdAmp_Weight),
                new AffixDef(Stat.FireAmp, AffixPoolsBalance.Charm_FireAmp_Min, AffixPoolsBalance.Charm_FireAmp_Max, AffixPoolsBalance.Charm_FireAmp_Weight),
                new AffixDef(Stat.LightAmp, AffixPoolsBalance.Charm_LightAmp_Min, AffixPoolsBalance.Charm_LightAmp_Max, AffixPoolsBalance.Charm_LightAmp_Weight),
                new AffixDef(Stat.DarkAmp, AffixPoolsBalance.Charm_DarkAmp_Min, AffixPoolsBalance.Charm_DarkAmp_Max, AffixPoolsBalance.Charm_DarkAmp_Weight),
                new AffixDef(Stat.Tenacity, AffixPoolsBalance.Charm_Tenacity_Min, AffixPoolsBalance.Charm_Tenacity_Max, AffixPoolsBalance.Charm_Tenacity_Weight),
                new AffixDef(Stat.CritDamagePct, AffixPoolsBalance.Charm_CritDamagePct_Min, AffixPoolsBalance.Charm_CritDamagePct_Max, AffixPoolsBalance.Charm_CritDamagePct_Weight),
                new AffixDef(Stat.HealPower, AffixPoolsBalance.Charm_HealPower_Min, AffixPoolsBalance.Charm_HealPower_Max, AffixPoolsBalance.Charm_HealPower_Weight),
                new AffixDef(Stat.ShieldPower, AffixPoolsBalance.Charm_ShieldPower_Min, AffixPoolsBalance.Charm_ShieldPower_Max, AffixPoolsBalance.Charm_ShieldPower_Weight),
            },
            [Slot.Head] = new[]
            {
                new AffixDef(Stat.PowerFlat, AffixPoolsBalance.Head_PowerFlat_Min, AffixPoolsBalance.Head_PowerFlat_Max, AffixPoolsBalance.Head_PowerFlat_Weight),
                new AffixDef(Stat.PowerPct, AffixPoolsBalance.Head_PowerPct_Min, AffixPoolsBalance.Head_PowerPct_Max, AffixPoolsBalance.Head_PowerPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.Haste, AffixPoolsBalance.Head_Haste_Min, AffixPoolsBalance.Head_Haste_Max, AffixPoolsBalance.Head_Haste_Weight),
                new AffixDef(Stat.CritChancePct, AffixPoolsBalance.Head_CritChancePct_Min, AffixPoolsBalance.Head_CritChancePct_Max, AffixPoolsBalance.Head_CritChancePct_Weight),
                new AffixDef(Stat.MaxHealthPct, AffixPoolsBalance.Head_MaxHealthPct_Min, AffixPoolsBalance.Head_MaxHealthPct_Max, AffixPoolsBalance.Head_MaxHealthPct_Weight),
                new AffixDef(Stat.Armor, AffixPoolsBalance.Head_Armor_Min, AffixPoolsBalance.Head_Armor_Max, AffixPoolsBalance.Head_Armor_Weight),
                new AffixDef(Stat.Tenacity, AffixPoolsBalance.Head_Tenacity_Min, AffixPoolsBalance.Head_Tenacity_Max, AffixPoolsBalance.Head_Tenacity_Weight),
                new AffixDef(Stat.LightAmp, AffixPoolsBalance.Head_LightAmp_Min, AffixPoolsBalance.Head_LightAmp_Max, AffixPoolsBalance.Head_LightAmp_Weight),
                new AffixDef(Stat.DarkAmp, AffixPoolsBalance.Head_DarkAmp_Min, AffixPoolsBalance.Head_DarkAmp_Max, AffixPoolsBalance.Head_DarkAmp_Weight),
                new AffixDef(Stat.HealthRegen, AffixPoolsBalance.Head_HealthRegen_Min, AffixPoolsBalance.Head_HealthRegen_Max, AffixPoolsBalance.Head_HealthRegen_Weight),
                new AffixDef(Stat.AttackFlat, AffixPoolsBalance.Head_AttackFlat_Min, AffixPoolsBalance.Head_AttackFlat_Max, AffixPoolsBalance.Head_AttackFlat_Weight),
                new AffixDef(Stat.AttackPct, AffixPoolsBalance.Head_AttackPct_Min, AffixPoolsBalance.Head_AttackPct_Max, AffixPoolsBalance.Head_AttackPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.MoveSpeedPct, AffixPoolsBalance.Head_MoveSpeedPct_Min, AffixPoolsBalance.Head_MoveSpeedPct_Max, AffixPoolsBalance.Head_MoveSpeedPct_Weight), // v1.21
                new AffixDef(Stat.ColdAmp, AffixPoolsBalance.Head_ColdAmp_Min, AffixPoolsBalance.Head_ColdAmp_Max, AffixPoolsBalance.Head_ColdAmp_Weight), // v1.21
                new AffixDef(Stat.FireAmp, AffixPoolsBalance.Head_FireAmp_Min, AffixPoolsBalance.Head_FireAmp_Max, AffixPoolsBalance.Head_FireAmp_Weight), // v1.21
                new AffixDef(Stat.HealPower, AffixPoolsBalance.Head_HealPower_Min, AffixPoolsBalance.Head_HealPower_Max, AffixPoolsBalance.Head_HealPower_Weight),
                new AffixDef(Stat.ShieldPower, AffixPoolsBalance.Head_ShieldPower_Min, AffixPoolsBalance.Head_ShieldPower_Max, AffixPoolsBalance.Head_ShieldPower_Weight),
            },
            [Slot.Hands] = new[]
            {
                new AffixDef(Stat.AttackFlat, AffixPoolsBalance.Hands_AttackFlat_Min, AffixPoolsBalance.Hands_AttackFlat_Max, AffixPoolsBalance.Hands_AttackFlat_Weight),
                new AffixDef(Stat.AttackPct, AffixPoolsBalance.Hands_AttackPct_Min, AffixPoolsBalance.Hands_AttackPct_Max, AffixPoolsBalance.Hands_AttackPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.AttackSpeedPct, AffixPoolsBalance.Hands_AttackSpeedPct_Min, AffixPoolsBalance.Hands_AttackSpeedPct_Max, AffixPoolsBalance.Hands_AttackSpeedPct_Weight),
                new AffixDef(Stat.CritChancePct, AffixPoolsBalance.Hands_CritChancePct_Min, AffixPoolsBalance.Hands_CritChancePct_Max, AffixPoolsBalance.Hands_CritChancePct_Weight),
                new AffixDef(Stat.CritDamagePct, AffixPoolsBalance.Hands_CritDamagePct_Min, AffixPoolsBalance.Hands_CritDamagePct_Max, AffixPoolsBalance.Hands_CritDamagePct_Weight),
                new AffixDef(Stat.PowerFlat, AffixPoolsBalance.Hands_PowerFlat_Min, AffixPoolsBalance.Hands_PowerFlat_Max, AffixPoolsBalance.Hands_PowerFlat_Weight),
                new AffixDef(Stat.PowerPct, AffixPoolsBalance.Hands_PowerPct_Min, AffixPoolsBalance.Hands_PowerPct_Max, AffixPoolsBalance.Hands_PowerPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.FireAmp, AffixPoolsBalance.Hands_FireAmp_Min, AffixPoolsBalance.Hands_FireAmp_Max, AffixPoolsBalance.Hands_FireAmp_Weight),
                new AffixDef(Stat.ColdAmp, AffixPoolsBalance.Hands_ColdAmp_Min, AffixPoolsBalance.Hands_ColdAmp_Max, AffixPoolsBalance.Hands_ColdAmp_Weight),
                new AffixDef(Stat.AttackRangePct, AffixPoolsBalance.Hands_AttackRangePct_Min, AffixPoolsBalance.Hands_AttackRangePct_Max, AffixPoolsBalance.Hands_AttackRangePct_Weight),
                new AffixDef(Stat.Armor, AffixPoolsBalance.Hands_Armor_Min, AffixPoolsBalance.Hands_Armor_Max, AffixPoolsBalance.Hands_Armor_Weight),
                new AffixDef(Stat.MaxHealthFlat, AffixPoolsBalance.Hands_MaxHealthFlat_Min, AffixPoolsBalance.Hands_MaxHealthFlat_Max, AffixPoolsBalance.Hands_MaxHealthFlat_Weight),
                new AffixDef(Stat.LightAmp, AffixPoolsBalance.Hands_LightAmp_Min, AffixPoolsBalance.Hands_LightAmp_Max, AffixPoolsBalance.Hands_LightAmp_Weight), // v1.21
                new AffixDef(Stat.DarkAmp, AffixPoolsBalance.Hands_DarkAmp_Min, AffixPoolsBalance.Hands_DarkAmp_Max, AffixPoolsBalance.Hands_DarkAmp_Weight), // v1.21
                new AffixDef(Stat.Haste, AffixPoolsBalance.Hands_Haste_Min, AffixPoolsBalance.Hands_Haste_Max, AffixPoolsBalance.Hands_Haste_Weight), // v1.21
            },
            [Slot.Feet] = new[]
            {
                new AffixDef(Stat.MoveSpeedPct, AffixPoolsBalance.Feet_MoveSpeedPct_Min, AffixPoolsBalance.Feet_MoveSpeedPct_Max, AffixPoolsBalance.Feet_MoveSpeedPct_Weight),
                new AffixDef(Stat.Tenacity, AffixPoolsBalance.Feet_Tenacity_Min, AffixPoolsBalance.Feet_Tenacity_Max, AffixPoolsBalance.Feet_Tenacity_Weight),
                new AffixDef(Stat.Armor, AffixPoolsBalance.Feet_Armor_Min, AffixPoolsBalance.Feet_Armor_Max, AffixPoolsBalance.Feet_Armor_Weight),
                new AffixDef(Stat.MaxHealthPct, AffixPoolsBalance.Feet_MaxHealthPct_Min, AffixPoolsBalance.Feet_MaxHealthPct_Max, AffixPoolsBalance.Feet_MaxHealthPct_Weight),
                new AffixDef(Stat.MaxHealthFlat, AffixPoolsBalance.Feet_MaxHealthFlat_Min, AffixPoolsBalance.Feet_MaxHealthFlat_Max, AffixPoolsBalance.Feet_MaxHealthFlat_Weight),
                new AffixDef(Stat.HealthRegen, AffixPoolsBalance.Feet_HealthRegen_Min, AffixPoolsBalance.Feet_HealthRegen_Max, AffixPoolsBalance.Feet_HealthRegen_Weight),
                new AffixDef(Stat.AttackSpeedPct, AffixPoolsBalance.Feet_AttackSpeedPct_Min, AffixPoolsBalance.Feet_AttackSpeedPct_Max, AffixPoolsBalance.Feet_AttackSpeedPct_Weight),
                new AffixDef(Stat.Haste, AffixPoolsBalance.Feet_Haste_Min, AffixPoolsBalance.Feet_Haste_Max, AffixPoolsBalance.Feet_Haste_Weight),
                new AffixDef(Stat.ColdAmp, AffixPoolsBalance.Feet_ColdAmp_Min, AffixPoolsBalance.Feet_ColdAmp_Max, AffixPoolsBalance.Feet_ColdAmp_Weight),
                new AffixDef(Stat.FireAmp, AffixPoolsBalance.Feet_FireAmp_Min, AffixPoolsBalance.Feet_FireAmp_Max, AffixPoolsBalance.Feet_FireAmp_Weight),
                new AffixDef(Stat.CritChancePct, AffixPoolsBalance.Feet_CritChancePct_Min, AffixPoolsBalance.Feet_CritChancePct_Max, AffixPoolsBalance.Feet_CritChancePct_Weight), // v1.21
                new AffixDef(Stat.LightAmp, AffixPoolsBalance.Feet_LightAmp_Min, AffixPoolsBalance.Feet_LightAmp_Max, AffixPoolsBalance.Feet_LightAmp_Weight), // v1.21
                new AffixDef(Stat.DarkAmp, AffixPoolsBalance.Feet_DarkAmp_Min, AffixPoolsBalance.Feet_DarkAmp_Max, AffixPoolsBalance.Feet_DarkAmp_Weight), // v1.21
                new AffixDef(Stat.AttackFlat, AffixPoolsBalance.Feet_AttackFlat_Min, AffixPoolsBalance.Feet_AttackFlat_Max, AffixPoolsBalance.Feet_AttackFlat_Weight), // v1.27：足にも攻撃力・魔力
                new AffixDef(Stat.AttackPct, AffixPoolsBalance.Feet_AttackPct_Min, AffixPoolsBalance.Feet_AttackPct_Max, AffixPoolsBalance.Feet_AttackPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.PowerFlat, AffixPoolsBalance.Feet_PowerFlat_Min, AffixPoolsBalance.Feet_PowerFlat_Max, AffixPoolsBalance.Feet_PowerFlat_Weight),
                new AffixDef(Stat.PowerPct, AffixPoolsBalance.Feet_PowerPct_Min, AffixPoolsBalance.Feet_PowerPct_Max, AffixPoolsBalance.Feet_PowerPct_Weight, Rarity.Epic), // v1.28：%はエピック以上・控えめに
            },
        };

        private static readonly Dictionary<Slot, PowerRange[]> PowerPools = new Dictionary<Slot, PowerRange[]>
        {
            [Slot.Weapon] = new[]
            {
                new PowerRange(Power.ShieldBash, PowerPoolsBalance.Weapon_ShieldBash_Min, PowerPoolsBalance.Weapon_ShieldBash_Max),
                new PowerRange(Power.WanderersEdge, PowerPoolsBalance.Weapon_WanderersEdge_Min, PowerPoolsBalance.Weapon_WanderersEdge_Max),
                new PowerRange(Power.FocusFire, PowerPoolsBalance.Weapon_FocusFire_Min, PowerPoolsBalance.Weapon_FocusFire_Max),
                new PowerRange(Power.DuelistsWay, PowerPoolsBalance.Weapon_DuelistsWay_Min, PowerPoolsBalance.Weapon_DuelistsWay_Max),
                new PowerRange(Power.RunUp, PowerPoolsBalance.Weapon_RunUp_Min, PowerPoolsBalance.Weapon_RunUp_Max),
                new PowerRange(Power.BrittleIce, PowerPoolsBalance.Weapon_BrittleIce_Min, PowerPoolsBalance.Weapon_BrittleIce_Max),
                new PowerRange(Power.ElementalHarvest, PowerPoolsBalance.Weapon_ElementalHarvest_Min, PowerPoolsBalance.Weapon_ElementalHarvest_Max),
                new PowerRange(Power.Medley, PowerPoolsBalance.Weapon_Medley_Min, PowerPoolsBalance.Weapon_Medley_Max),
                new PowerRange(Power.Spellsweep, PowerPoolsBalance.Weapon_Spellsweep_Min, PowerPoolsBalance.Weapon_Spellsweep_Max),
                new PowerRange(Power.BareHandedPride, PowerPoolsBalance.Weapon_BareHandedPride_Min, PowerPoolsBalance.Weapon_BareHandedPride_Max),
                new PowerRange(Power.OpeningSalvo, PowerPoolsBalance.Weapon_OpeningSalvo_Min, PowerPoolsBalance.Weapon_OpeningSalvo_Max),
                new PowerRange(Power.PileOn, PowerPoolsBalance.Weapon_PileOn_Min, PowerPoolsBalance.Weapon_PileOn_Max),
                new PowerRange(Power.AceInHand, PowerPoolsBalance.Weapon_AceInHand_Min, PowerPoolsBalance.Weapon_AceInHand_Max),
                new PowerRange(Power.PilingLuck, PowerPoolsBalance.Weapon_PilingLuck_Min, PowerPoolsBalance.Weapon_PilingLuck_Max),
                new PowerRange(Power.WeakPointWound, PowerPoolsBalance.Weapon_WeakPointWound_Min, PowerPoolsBalance.Weapon_WeakPointWound_Max),
                new PowerRange(Power.CritSplash, PowerPoolsBalance.Weapon_CritSplash_Min, PowerPoolsBalance.Weapon_CritSplash_Max),
                new PowerRange(Power.ReturningBlade, PowerPoolsBalance.Weapon_ReturningBlade_Min, PowerPoolsBalance.Weapon_ReturningBlade_Max),
                new PowerRange(Power.SpilloverStrike, PowerPoolsBalance.Weapon_SpilloverStrike_Min, PowerPoolsBalance.Weapon_SpilloverStrike_Max),
                new PowerRange(Power.Momentum, PowerPoolsBalance.Weapon_Momentum_Min, PowerPoolsBalance.Weapon_Momentum_Max),
                new PowerRange(Power.Lifesteal, PowerPoolsBalance.Weapon_Lifesteal_Min, PowerPoolsBalance.Weapon_Lifesteal_Max),
                new PowerRange(Power.Executioner, PowerPoolsBalance.Weapon_Executioner_Min, PowerPoolsBalance.Weapon_Executioner_Max),
                new PowerRange(Power.Blaze, PowerPoolsBalance.Weapon_Blaze_Min, PowerPoolsBalance.Weapon_Blaze_Max),
                new PowerRange(Power.ChainLightning, PowerPoolsBalance.Weapon_ChainLightning_Min, PowerPoolsBalance.Weapon_ChainLightning_Max),
                new PowerRange(Power.Bloodlust, PowerPoolsBalance.Weapon_Bloodlust_Min, PowerPoolsBalance.Weapon_Bloodlust_Max),
                new PowerRange(Power.Ember, PowerPoolsBalance.Weapon_Ember_Min, PowerPoolsBalance.Weapon_Ember_Max),
                new PowerRange(Power.Radiance, PowerPoolsBalance.Weapon_Radiance_Min, PowerPoolsBalance.Weapon_Radiance_Max),
                new PowerRange(Power.UltimateSurge, PowerPoolsBalance.Weapon_UltimateSurge_Min, PowerPoolsBalance.Weapon_UltimateSurge_Max),
                new PowerRange(Power.SoulSiphon, PowerPoolsBalance.Weapon_SoulSiphon_Min, PowerPoolsBalance.Weapon_SoulSiphon_Max),
                new PowerRange(Power.Frenzy, PowerPoolsBalance.Weapon_Frenzy_Min, PowerPoolsBalance.Weapon_Frenzy_Max),
                new PowerRange(Power.OpeningStrike, PowerPoolsBalance.Weapon_OpeningStrike_Min, PowerPoolsBalance.Weapon_OpeningStrike_Max),
                new PowerRange(Power.Vigor, PowerPoolsBalance.Weapon_Vigor_Min, PowerPoolsBalance.Weapon_Vigor_Max),
                new PowerRange(Power.Overload, PowerPoolsBalance.Weapon_Overload_Min, PowerPoolsBalance.Weapon_Overload_Max),
                new PowerRange(Power.Fetters, PowerPoolsBalance.Weapon_Fetters_Min, PowerPoolsBalance.Weapon_Fetters_Max),
                new PowerRange(Power.CriticalEcho, PowerPoolsBalance.Weapon_CriticalEcho_Min, PowerPoolsBalance.Weapon_CriticalEcho_Max),
                new PowerRange(Power.Wildfire, PowerPoolsBalance.Weapon_Wildfire_Min, PowerPoolsBalance.Weapon_Wildfire_Max),
            },
            [Slot.Armor] = new[]
            {
                new PowerRange(Power.UnbowedMind, PowerPoolsBalance.Armor_UnbowedMind_Min, PowerPoolsBalance.Armor_UnbowedMind_Max),
                new PowerRange(Power.ShieldbreakBurst, PowerPoolsBalance.Armor_ShieldbreakBurst_Min, PowerPoolsBalance.Armor_ShieldbreakBurst_Max),
                new PowerRange(Power.SharedWard, PowerPoolsBalance.Armor_SharedWard_Min, PowerPoolsBalance.Armor_SharedWard_Max),
                new PowerRange(Power.TriumphSong, PowerPoolsBalance.Armor_TriumphSong_Min, PowerPoolsBalance.Armor_TriumphSong_Max),
                new PowerRange(Power.WatchfulHand, PowerPoolsBalance.Armor_WatchfulHand_Min, PowerPoolsBalance.Armor_WatchfulHand_Max),
                new PowerRange(Power.Breakout, PowerPoolsBalance.Armor_Breakout_Min, PowerPoolsBalance.Armor_Breakout_Max),
                new PowerRange(Power.ImmovableStance, PowerPoolsBalance.Armor_ImmovableStance_Min, PowerPoolsBalance.Armor_ImmovableStance_Max),
                new PowerRange(Power.VanguardsOath, PowerPoolsBalance.Armor_VanguardsOath_Min, PowerPoolsBalance.Armor_VanguardsOath_Max),
                new PowerRange(Power.DeathBloom, PowerPoolsBalance.Armor_DeathBloom_Min, PowerPoolsBalance.Armor_DeathBloom_Max),
                new PowerRange(Power.DreamOmen, PowerPoolsBalance.Armor_DreamOmen_Min, PowerPoolsBalance.Armor_DreamOmen_Max),
                new PowerRange(Power.RearguardsWay, PowerPoolsBalance.Armor_RearguardsWay_Min, PowerPoolsBalance.Armor_RearguardsWay_Max),
                new PowerRange(Power.TollOfGrudge, PowerPoolsBalance.Armor_TollOfGrudge_Min, PowerPoolsBalance.Armor_TollOfGrudge_Max),
                new PowerRange(Power.ReadyGuard, PowerPoolsBalance.Armor_ReadyGuard_Min, PowerPoolsBalance.Armor_ReadyGuard_Max),
                new PowerRange(Power.Apothecary, PowerPoolsBalance.Armor_Apothecary_Min, PowerPoolsBalance.Armor_Apothecary_Max),
                new PowerRange(Power.Retaliation, PowerPoolsBalance.Armor_Retaliation_Min, PowerPoolsBalance.Armor_Retaliation_Max),
                new PowerRange(Power.Bulwark, PowerPoolsBalance.Armor_Bulwark_Min, PowerPoolsBalance.Armor_Bulwark_Max),
                new PowerRange(Power.Thorns, PowerPoolsBalance.Armor_Thorns_Min, PowerPoolsBalance.Armor_Thorns_Max),
                new PowerRange(Power.Barrier, PowerPoolsBalance.Armor_Barrier_Min, PowerPoolsBalance.Armor_Barrier_Max),
                new PowerRange(Power.Aegis, PowerPoolsBalance.Armor_Aegis_Min, PowerPoolsBalance.Armor_Aegis_Max),
                new PowerRange(Power.EchoingDodge, PowerPoolsBalance.Armor_EchoingDodge_Min, PowerPoolsBalance.Armor_EchoingDodge_Max),
                new PowerRange(Power.Whirlwind, PowerPoolsBalance.Armor_Whirlwind_Min, PowerPoolsBalance.Armor_Whirlwind_Max),
                new PowerRange(Power.Frenzy, PowerPoolsBalance.Armor_Frenzy_Min, PowerPoolsBalance.Armor_Frenzy_Max),
                new PowerRange(Power.StarShield, PowerPoolsBalance.Armor_StarShield_Min, PowerPoolsBalance.Armor_StarShield_Max),
                new PowerRange(Power.Sprint, PowerPoolsBalance.Armor_Sprint_Min, PowerPoolsBalance.Armor_Sprint_Max),
                new PowerRange(Power.OverflowingLife, PowerPoolsBalance.Armor_OverflowingLife_Min, PowerPoolsBalance.Armor_OverflowingLife_Max),
                new PowerRange(Power.Fetters, PowerPoolsBalance.Armor_Fetters_Min, PowerPoolsBalance.Armor_Fetters_Max),
                new PowerRange(Power.StillWater, PowerPoolsBalance.Armor_StillWater_Min, PowerPoolsBalance.Armor_StillWater_Max),
            },
            [Slot.Charm] = new[]
            {
                new PowerRange(Power.ShieldbreakBurst, PowerPoolsBalance.Charm_ShieldbreakBurst_Min, PowerPoolsBalance.Charm_ShieldbreakBurst_Max),
                new PowerRange(Power.SharedWard, PowerPoolsBalance.Charm_SharedWard_Min, PowerPoolsBalance.Charm_SharedWard_Max),
                new PowerRange(Power.GleamingWard, PowerPoolsBalance.Charm_GleamingWard_Min, PowerPoolsBalance.Charm_GleamingWard_Max),
                new PowerRange(Power.CoStar, PowerPoolsBalance.Charm_CoStar_Min, PowerPoolsBalance.Charm_CoStar_Max),
                new PowerRange(Power.TriumphSong, PowerPoolsBalance.Charm_TriumphSong_Min, PowerPoolsBalance.Charm_TriumphSong_Max),
                new PowerRange(Power.WatchfulHand, PowerPoolsBalance.Charm_WatchfulHand_Min, PowerPoolsBalance.Charm_WatchfulHand_Max),
                new PowerRange(Power.KindnessReturns, PowerPoolsBalance.Charm_KindnessReturns_Min, PowerPoolsBalance.Charm_KindnessReturns_Max),
                new PowerRange(Power.RelayHand, PowerPoolsBalance.Charm_RelayHand_Min, PowerPoolsBalance.Charm_RelayHand_Max),
                new PowerRange(Power.UmbralHeritage, PowerPoolsBalance.Charm_UmbralHeritage_Min, PowerPoolsBalance.Charm_UmbralHeritage_Max),
                new PowerRange(Power.ElementalHarvest, PowerPoolsBalance.Charm_ElementalHarvest_Min, PowerPoolsBalance.Charm_ElementalHarvest_Max),
                new PowerRange(Power.PackFeast, PowerPoolsBalance.Charm_PackFeast_Min, PowerPoolsBalance.Charm_PackFeast_Max),
                new PowerRange(Power.VanguardsOath, PowerPoolsBalance.Charm_VanguardsOath_Min, PowerPoolsBalance.Charm_VanguardsOath_Max),
                new PowerRange(Power.Medley, PowerPoolsBalance.Charm_Medley_Min, PowerPoolsBalance.Charm_Medley_Max),
                new PowerRange(Power.BareHandedPride, PowerPoolsBalance.Charm_BareHandedPride_Min, PowerPoolsBalance.Charm_BareHandedPride_Max),
                new PowerRange(Power.AceInHand, PowerPoolsBalance.Charm_AceInHand_Min, PowerPoolsBalance.Charm_AceInHand_Max),
                new PowerRange(Power.CrystalCircuit, PowerPoolsBalance.Charm_CrystalCircuit_Min, PowerPoolsBalance.Charm_CrystalCircuit_Max),
                new PowerRange(Power.ShardBoon, PowerPoolsBalance.Charm_ShardBoon_Min, PowerPoolsBalance.Charm_ShardBoon_Max),
                new PowerRange(Power.Lifeline, PowerPoolsBalance.Charm_Lifeline_Min, PowerPoolsBalance.Charm_Lifeline_Max),
                new PowerRange(Power.DreamOmen, PowerPoolsBalance.Charm_DreamOmen_Min, PowerPoolsBalance.Charm_DreamOmen_Max),
                new PowerRange(Power.RearguardsWay, PowerPoolsBalance.Charm_RearguardsWay_Min, PowerPoolsBalance.Charm_RearguardsWay_Max),
                new PowerRange(Power.ReadyGuard, PowerPoolsBalance.Charm_ReadyGuard_Min, PowerPoolsBalance.Charm_ReadyGuard_Max),
                new PowerRange(Power.Apothecary, PowerPoolsBalance.Charm_Apothecary_Min, PowerPoolsBalance.Charm_Apothecary_Max),
                new PowerRange(Power.SpilloverStrike, PowerPoolsBalance.Charm_SpilloverStrike_Min, PowerPoolsBalance.Charm_SpilloverStrike_Max),
                new PowerRange(Power.Resonance, PowerPoolsBalance.Charm_Resonance_Min, PowerPoolsBalance.Charm_Resonance_Max),
                new PowerRange(Power.Tailwind, PowerPoolsBalance.Charm_Tailwind_Min, PowerPoolsBalance.Charm_Tailwind_Max),
                new PowerRange(Power.SecondWind, PowerPoolsBalance.Charm_SecondWind_Min, PowerPoolsBalance.Charm_SecondWind_Max),
                new PowerRange(Power.Shatter, PowerPoolsBalance.Charm_Shatter_Min, PowerPoolsBalance.Charm_Shatter_Max),
                new PowerRange(Power.Frost, PowerPoolsBalance.Charm_Frost_Min, PowerPoolsBalance.Charm_Frost_Max),
                new PowerRange(Power.Umbra, PowerPoolsBalance.Charm_Umbra_Min, PowerPoolsBalance.Charm_Umbra_Max),
                new PowerRange(Power.Convergence, PowerPoolsBalance.Charm_Convergence_Min, PowerPoolsBalance.Charm_Convergence_Max),
                new PowerRange(Power.Steam, PowerPoolsBalance.Charm_Steam_Min, PowerPoolsBalance.Charm_Steam_Max),
                new PowerRange(Power.Eclipse, PowerPoolsBalance.Charm_Eclipse_Min, PowerPoolsBalance.Charm_Eclipse_Max),
                new PowerRange(Power.Cinder, PowerPoolsBalance.Charm_Cinder_Min, PowerPoolsBalance.Charm_Cinder_Max),
                new PowerRange(Power.FrostCrystal, PowerPoolsBalance.Charm_FrostCrystal_Min, PowerPoolsBalance.Charm_FrostCrystal_Max),
                new PowerRange(Power.SoulSiphon, PowerPoolsBalance.Charm_SoulSiphon_Min, PowerPoolsBalance.Charm_SoulSiphon_Max),
                new PowerRange(Power.Whirlwind, PowerPoolsBalance.Charm_Whirlwind_Min, PowerPoolsBalance.Charm_Whirlwind_Max),
                new PowerRange(Power.StarShield, PowerPoolsBalance.Charm_StarShield_Min, PowerPoolsBalance.Charm_StarShield_Max),
                new PowerRange(Power.Sprint, PowerPoolsBalance.Charm_Sprint_Min, PowerPoolsBalance.Charm_Sprint_Max),
                new PowerRange(Power.Vigor, PowerPoolsBalance.Charm_Vigor_Min, PowerPoolsBalance.Charm_Vigor_Max),
                new PowerRange(Power.Overload, PowerPoolsBalance.Charm_Overload_Min, PowerPoolsBalance.Charm_Overload_Max),
                new PowerRange(Power.Devotion, PowerPoolsBalance.Charm_Devotion_Min, PowerPoolsBalance.Charm_Devotion_Max),
                new PowerRange(Power.CrystalResonance, PowerPoolsBalance.Charm_CrystalResonance_Min, PowerPoolsBalance.Charm_CrystalResonance_Max),
                new PowerRange(Power.PreyPride, PowerPoolsBalance.Charm_PreyPride_Min, PowerPoolsBalance.Charm_PreyPride_Max),
                new PowerRange(Power.SpendersWard, PowerPoolsBalance.Charm_SpendersWard_Min, PowerPoolsBalance.Charm_SpendersWard_Max),
                new PowerRange(Power.LucidBoon, PowerPoolsBalance.Charm_LucidBoon_Min, PowerPoolsBalance.Charm_LucidBoon_Max),
            },
            [Slot.Head] = new[]
            {
                new PowerRange(Power.GleamingWard, PowerPoolsBalance.Head_GleamingWard_Min, PowerPoolsBalance.Head_GleamingWard_Max),
                new PowerRange(Power.CoStar, PowerPoolsBalance.Head_CoStar_Min, PowerPoolsBalance.Head_CoStar_Max),
                new PowerRange(Power.FocusFire, PowerPoolsBalance.Head_FocusFire_Min, PowerPoolsBalance.Head_FocusFire_Max),
                new PowerRange(Power.DuelistsWay, PowerPoolsBalance.Head_DuelistsWay_Min, PowerPoolsBalance.Head_DuelistsWay_Max),
                new PowerRange(Power.ImmovableStance, PowerPoolsBalance.Head_ImmovableStance_Min, PowerPoolsBalance.Head_ImmovableStance_Max),
                new PowerRange(Power.StardustCycle, PowerPoolsBalance.Head_StardustCycle_Min, PowerPoolsBalance.Head_StardustCycle_Max),
                new PowerRange(Power.PrismShift, PowerPoolsBalance.Head_PrismShift_Min, PowerPoolsBalance.Head_PrismShift_Max),
                new PowerRange(Power.PackFeast, PowerPoolsBalance.Head_PackFeast_Min, PowerPoolsBalance.Head_PackFeast_Max),
                new PowerRange(Power.Medley, PowerPoolsBalance.Head_Medley_Min, PowerPoolsBalance.Head_Medley_Max),
                new PowerRange(Power.OpeningSalvo, PowerPoolsBalance.Head_OpeningSalvo_Min, PowerPoolsBalance.Head_OpeningSalvo_Max),
                new PowerRange(Power.PileOn, PowerPoolsBalance.Head_PileOn_Min, PowerPoolsBalance.Head_PileOn_Max),
                new PowerRange(Power.AceInHand, PowerPoolsBalance.Head_AceInHand_Min, PowerPoolsBalance.Head_AceInHand_Max),
                new PowerRange(Power.CrystalCircuit, PowerPoolsBalance.Head_CrystalCircuit_Min, PowerPoolsBalance.Head_CrystalCircuit_Max),
                new PowerRange(Power.Lifeline, PowerPoolsBalance.Head_Lifeline_Min, PowerPoolsBalance.Head_Lifeline_Max),
                new PowerRange(Power.DreamOmen, PowerPoolsBalance.Head_DreamOmen_Min, PowerPoolsBalance.Head_DreamOmen_Max),
                new PowerRange(Power.RearguardsWay, PowerPoolsBalance.Head_RearguardsWay_Min, PowerPoolsBalance.Head_RearguardsWay_Max),
                new PowerRange(Power.PilingLuck, PowerPoolsBalance.Head_PilingLuck_Min, PowerPoolsBalance.Head_PilingLuck_Max),
                new PowerRange(Power.ReturningBlade, PowerPoolsBalance.Head_ReturningBlade_Min, PowerPoolsBalance.Head_ReturningBlade_Max),
                new PowerRange(Power.Eclipse, PowerPoolsBalance.Head_Eclipse_Min, PowerPoolsBalance.Head_Eclipse_Max),
                new PowerRange(Power.FrostCrystal, PowerPoolsBalance.Head_FrostCrystal_Min, PowerPoolsBalance.Head_FrostCrystal_Max),
                new PowerRange(Power.Overload, PowerPoolsBalance.Head_Overload_Min, PowerPoolsBalance.Head_Overload_Max),
                new PowerRange(Power.UltimateSurge, PowerPoolsBalance.Head_UltimateSurge_Min, PowerPoolsBalance.Head_UltimateSurge_Max),
                new PowerRange(Power.StarShield, PowerPoolsBalance.Head_StarShield_Min, PowerPoolsBalance.Head_StarShield_Max),
                new PowerRange(Power.Resonance, PowerPoolsBalance.Head_Resonance_Min, PowerPoolsBalance.Head_Resonance_Max),
                new PowerRange(Power.SecondWind, PowerPoolsBalance.Head_SecondWind_Min, PowerPoolsBalance.Head_SecondWind_Max),
                new PowerRange(Power.Radiance, PowerPoolsBalance.Head_Radiance_Min, PowerPoolsBalance.Head_Radiance_Max),
                new PowerRange(Power.Umbra, PowerPoolsBalance.Head_Umbra_Min, PowerPoolsBalance.Head_Umbra_Max),
                new PowerRange(Power.Barrier, PowerPoolsBalance.Head_Barrier_Min, PowerPoolsBalance.Head_Barrier_Max),
                new PowerRange(Power.Vigor, PowerPoolsBalance.Head_Vigor_Min, PowerPoolsBalance.Head_Vigor_Max),
                new PowerRange(Power.Bloodlust, PowerPoolsBalance.Head_Bloodlust_Min, PowerPoolsBalance.Head_Bloodlust_Max),
                new PowerRange(Power.Finale, PowerPoolsBalance.Head_Finale_Min, PowerPoolsBalance.Head_Finale_Max),
                new PowerRange(Power.CrystalResonance, PowerPoolsBalance.Head_CrystalResonance_Min, PowerPoolsBalance.Head_CrystalResonance_Max),
                new PowerRange(Power.Devotion, PowerPoolsBalance.Head_Devotion_Min, PowerPoolsBalance.Head_Devotion_Max),
                new PowerRange(Power.LucidBoon, PowerPoolsBalance.Head_LucidBoon_Min, PowerPoolsBalance.Head_LucidBoon_Max),
            },
            [Slot.Hands] = new[]
            {
                new PowerRange(Power.ShieldBash, PowerPoolsBalance.Hands_ShieldBash_Min, PowerPoolsBalance.Hands_ShieldBash_Max),
                new PowerRange(Power.KindnessReturns, PowerPoolsBalance.Hands_KindnessReturns_Min, PowerPoolsBalance.Hands_KindnessReturns_Max),
                new PowerRange(Power.WanderersEdge, PowerPoolsBalance.Hands_WanderersEdge_Min, PowerPoolsBalance.Hands_WanderersEdge_Max),
                new PowerRange(Power.FocusFire, PowerPoolsBalance.Hands_FocusFire_Min, PowerPoolsBalance.Hands_FocusFire_Max),
                new PowerRange(Power.DuelistsWay, PowerPoolsBalance.Hands_DuelistsWay_Min, PowerPoolsBalance.Hands_DuelistsWay_Max),
                new PowerRange(Power.RunUp, PowerPoolsBalance.Hands_RunUp_Min, PowerPoolsBalance.Hands_RunUp_Max),
                new PowerRange(Power.StrafeShot, PowerPoolsBalance.Hands_StrafeShot_Min, PowerPoolsBalance.Hands_StrafeShot_Max),
                new PowerRange(Power.RelayHand, PowerPoolsBalance.Hands_RelayHand_Min, PowerPoolsBalance.Hands_RelayHand_Max),
                new PowerRange(Power.StardustCycle, PowerPoolsBalance.Hands_StardustCycle_Min, PowerPoolsBalance.Hands_StardustCycle_Max),
                new PowerRange(Power.UmbralHeritage, PowerPoolsBalance.Hands_UmbralHeritage_Min, PowerPoolsBalance.Hands_UmbralHeritage_Max),
                new PowerRange(Power.BrittleIce, PowerPoolsBalance.Hands_BrittleIce_Min, PowerPoolsBalance.Hands_BrittleIce_Max),
                new PowerRange(Power.ElementalHarvest, PowerPoolsBalance.Hands_ElementalHarvest_Min, PowerPoolsBalance.Hands_ElementalHarvest_Max),
                new PowerRange(Power.PrismShift, PowerPoolsBalance.Hands_PrismShift_Min, PowerPoolsBalance.Hands_PrismShift_Max),
                new PowerRange(Power.DeathBloom, PowerPoolsBalance.Hands_DeathBloom_Min, PowerPoolsBalance.Hands_DeathBloom_Max),
                new PowerRange(Power.Spellsweep, PowerPoolsBalance.Hands_Spellsweep_Min, PowerPoolsBalance.Hands_Spellsweep_Max),
                new PowerRange(Power.BareHandedPride, PowerPoolsBalance.Hands_BareHandedPride_Min, PowerPoolsBalance.Hands_BareHandedPride_Max),
                new PowerRange(Power.PileOn, PowerPoolsBalance.Hands_PileOn_Min, PowerPoolsBalance.Hands_PileOn_Max),
                new PowerRange(Power.PilingLuck, PowerPoolsBalance.Hands_PilingLuck_Min, PowerPoolsBalance.Hands_PilingLuck_Max),
                new PowerRange(Power.WeakPointWound, PowerPoolsBalance.Hands_WeakPointWound_Min, PowerPoolsBalance.Hands_WeakPointWound_Max),
                new PowerRange(Power.CritSplash, PowerPoolsBalance.Hands_CritSplash_Min, PowerPoolsBalance.Hands_CritSplash_Max),
                new PowerRange(Power.ReturningBlade, PowerPoolsBalance.Hands_ReturningBlade_Min, PowerPoolsBalance.Hands_ReturningBlade_Max),
                new PowerRange(Power.Apothecary, PowerPoolsBalance.Hands_Apothecary_Min, PowerPoolsBalance.Hands_Apothecary_Max),
                new PowerRange(Power.SpilloverStrike, PowerPoolsBalance.Hands_SpilloverStrike_Min, PowerPoolsBalance.Hands_SpilloverStrike_Max),
                new PowerRange(Power.Steam, PowerPoolsBalance.Hands_Steam_Min, PowerPoolsBalance.Hands_Steam_Max),
                new PowerRange(Power.Cinder, PowerPoolsBalance.Hands_Cinder_Min, PowerPoolsBalance.Hands_Cinder_Max),
                new PowerRange(Power.Executioner, PowerPoolsBalance.Hands_Executioner_Min, PowerPoolsBalance.Hands_Executioner_Max),
                new PowerRange(Power.Blaze, PowerPoolsBalance.Hands_Blaze_Min, PowerPoolsBalance.Hands_Blaze_Max),
                new PowerRange(Power.ChainLightning, PowerPoolsBalance.Hands_ChainLightning_Min, PowerPoolsBalance.Hands_ChainLightning_Max),
                new PowerRange(Power.Ember, PowerPoolsBalance.Hands_Ember_Min, PowerPoolsBalance.Hands_Ember_Max),
                new PowerRange(Power.Frost, PowerPoolsBalance.Hands_Frost_Min, PowerPoolsBalance.Hands_Frost_Max),
                new PowerRange(Power.Frenzy, PowerPoolsBalance.Hands_Frenzy_Min, PowerPoolsBalance.Hands_Frenzy_Max),
                new PowerRange(Power.Lifesteal, PowerPoolsBalance.Hands_Lifesteal_Min, PowerPoolsBalance.Hands_Lifesteal_Max),
                new PowerRange(Power.OpeningStrike, PowerPoolsBalance.Hands_OpeningStrike_Min, PowerPoolsBalance.Hands_OpeningStrike_Max),
                new PowerRange(Power.Shatter, PowerPoolsBalance.Hands_Shatter_Min, PowerPoolsBalance.Hands_Shatter_Max),
                new PowerRange(Power.Momentum, PowerPoolsBalance.Hands_Momentum_Min, PowerPoolsBalance.Hands_Momentum_Max),
                new PowerRange(Power.CriticalEcho, PowerPoolsBalance.Hands_CriticalEcho_Min, PowerPoolsBalance.Hands_CriticalEcho_Max),
                new PowerRange(Power.Fetters, PowerPoolsBalance.Hands_Fetters_Min, PowerPoolsBalance.Hands_Fetters_Max),
                new PowerRange(Power.Wildfire, PowerPoolsBalance.Hands_Wildfire_Min, PowerPoolsBalance.Hands_Wildfire_Max),
                new PowerRange(Power.StillWater, PowerPoolsBalance.Hands_StillWater_Min, PowerPoolsBalance.Hands_StillWater_Max),
                new PowerRange(Power.Overload, PowerPoolsBalance.Hands_Overload_Min, PowerPoolsBalance.Hands_Overload_Max), // v1.27：手にもスキルで発動する効果
                new PowerRange(Power.Finale, PowerPoolsBalance.Hands_Finale_Min, PowerPoolsBalance.Hands_Finale_Max),
            },
            [Slot.Feet] = new[]
            {
                new PowerRange(Power.UnbowedMind, PowerPoolsBalance.Feet_UnbowedMind_Min, PowerPoolsBalance.Feet_UnbowedMind_Max),
                new PowerRange(Power.ShieldbreakBurst, PowerPoolsBalance.Feet_ShieldbreakBurst_Min, PowerPoolsBalance.Feet_ShieldbreakBurst_Max),
                new PowerRange(Power.WatchfulHand, PowerPoolsBalance.Feet_WatchfulHand_Min, PowerPoolsBalance.Feet_WatchfulHand_Max),
                new PowerRange(Power.Breakout, PowerPoolsBalance.Feet_Breakout_Min, PowerPoolsBalance.Feet_Breakout_Max),
                new PowerRange(Power.ImmovableStance, PowerPoolsBalance.Feet_ImmovableStance_Min, PowerPoolsBalance.Feet_ImmovableStance_Max),
                new PowerRange(Power.RunUp, PowerPoolsBalance.Feet_RunUp_Min, PowerPoolsBalance.Feet_RunUp_Max),
                new PowerRange(Power.StrafeShot, PowerPoolsBalance.Feet_StrafeShot_Min, PowerPoolsBalance.Feet_StrafeShot_Max),
                new PowerRange(Power.VanguardsOath, PowerPoolsBalance.Feet_VanguardsOath_Min, PowerPoolsBalance.Feet_VanguardsOath_Max),
                new PowerRange(Power.DeathBloom, PowerPoolsBalance.Feet_DeathBloom_Min, PowerPoolsBalance.Feet_DeathBloom_Max),
                new PowerRange(Power.ShardBoon, PowerPoolsBalance.Feet_ShardBoon_Min, PowerPoolsBalance.Feet_ShardBoon_Max),
                new PowerRange(Power.TollOfGrudge, PowerPoolsBalance.Feet_TollOfGrudge_Min, PowerPoolsBalance.Feet_TollOfGrudge_Max),
                new PowerRange(Power.ReadyGuard, PowerPoolsBalance.Feet_ReadyGuard_Min, PowerPoolsBalance.Feet_ReadyGuard_Max),
                new PowerRange(Power.Sprint, PowerPoolsBalance.Feet_Sprint_Min, PowerPoolsBalance.Feet_Sprint_Max),
                new PowerRange(Power.Tailwind, PowerPoolsBalance.Feet_Tailwind_Min, PowerPoolsBalance.Feet_Tailwind_Max),
                new PowerRange(Power.Whirlwind, PowerPoolsBalance.Feet_Whirlwind_Min, PowerPoolsBalance.Feet_Whirlwind_Max),
                new PowerRange(Power.EchoingDodge, PowerPoolsBalance.Feet_EchoingDodge_Min, PowerPoolsBalance.Feet_EchoingDodge_Max),
                new PowerRange(Power.Momentum, PowerPoolsBalance.Feet_Momentum_Min, PowerPoolsBalance.Feet_Momentum_Max),
                new PowerRange(Power.Aegis, PowerPoolsBalance.Feet_Aegis_Min, PowerPoolsBalance.Feet_Aegis_Max),
                new PowerRange(Power.Bulwark, PowerPoolsBalance.Feet_Bulwark_Min, PowerPoolsBalance.Feet_Bulwark_Max),
                new PowerRange(Power.Thorns, PowerPoolsBalance.Feet_Thorns_Min, PowerPoolsBalance.Feet_Thorns_Max),
                new PowerRange(Power.SoulSiphon, PowerPoolsBalance.Feet_SoulSiphon_Min, PowerPoolsBalance.Feet_SoulSiphon_Max),
                new PowerRange(Power.Retaliation, PowerPoolsBalance.Feet_Retaliation_Min, PowerPoolsBalance.Feet_Retaliation_Max),
                new PowerRange(Power.PreyPride, PowerPoolsBalance.Feet_PreyPride_Min, PowerPoolsBalance.Feet_PreyPride_Max),
                new PowerRange(Power.OverflowingLife, PowerPoolsBalance.Feet_OverflowingLife_Min, PowerPoolsBalance.Feet_OverflowingLife_Max),
                new PowerRange(Power.PerfectRead, PowerPoolsBalance.Feet_PerfectRead_Min, PowerPoolsBalance.Feet_PerfectRead_Max),
            },
        };

        public static readonly IReadOnlyList<TalentDef> Talents = new[]
        {
            new TalentDef("t.off.edge", Line.Offense, new Txt("鋭刃", "Keen Edge"), Stat.AttackPct, MemoryDamageBalance.Effect_legacy_t_off_edge_stat_perRank, 3),
            new TalentDef("t.off.mind", Line.Offense, new Txt("魔力", "Arcane Mind"), Stat.PowerPct, MemoryDamageBalance.Effect_legacy_t_off_mind_stat_perRank, 3),
            new TalentDef("t.off.swift", Line.Offense, new Txt("迅速", "Swiftness"), Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_t_off_swift_stat_perRank, 3),
            new TalentDef("t.off.vital", Line.Offense, new Txt("急所", "Vital Points"), Stat.CritChancePct, MemoryDamageBalance.Effect_legacy_t_off_vital_stat_perRank, 3),
            new TalentDef("t.off.key", Line.Offense, new Txt("舞い続ける者", "Ceaseless Dancer"), Power.Momentum, MemoryDamageBalance.Effect_legacy_t_off_key_power_perRank,
                new Txt("敵を次々に倒す戦い方に向いています。", "Suits fights where you chain kills.")),

            new TalentDef("t.grd.hearty", Line.Guard, new Txt("頑健", "Hearty"), Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_t_grd_hearty_stat_perRank, 3),
            new TalentDef("t.grd.iron", Line.Guard, new Txt("鉄皮", "Ironhide"), Stat.Armor, MemoryDamageBalance.Effect_legacy_t_grd_iron_stat_perRank, 3),
            new TalentDef("t.grd.regen", Line.Guard, new Txt("再生", "Regrowth"), Stat.HealthRegen, MemoryDamageBalance.Effect_legacy_t_grd_regen_stat_perRank, 3),
            new TalentDef("t.grd.steady", Line.Guard, new Txt("不屈", "Unyielding"), Stat.Tenacity, MemoryDamageBalance.Effect_legacy_t_grd_steady_stat_perRank, 3),
            new TalentDef("t.grd.key", Line.Guard, new Txt("逆襲の構え", "Counterstance"), Power.Retaliation, MemoryDamageBalance.Effect_legacy_t_grd_key_power_perRank,
                new Txt("敵の攻撃を受け止めて反撃する戦い方に向いています。", "Suits a tank that strikes back.")),

            new TalentDef("t.res.focus", Line.Resonance, new Txt("集中", "Focus"), Stat.Haste, MemoryDamageBalance.Effect_legacy_t_res_focus_stat_perRank, 3),
            new TalentDef("t.res.light", Line.Resonance, new Txt("軽歩", "Lightstep"), Stat.MoveSpeedPct, MemoryDamageBalance.Effect_legacy_t_res_light_stat_perRank, 3),
            new TalentDef("t.res.tune", Line.Resonance, new Txt("共振", "Attunement"), Stat.AttackPct, MemoryDamageBalance.Effect_legacy_t_res_tune_stat_perRank, 3),
            new TalentDef("t.res.ward", Line.Resonance, new Txt("守護", "Warding"), Stat.MaxHealthFlat, MemoryDamageBalance.Effect_legacy_t_res_ward_stat_perRank, 3),
            new TalentDef("t.res.key", Line.Resonance, new Txt("共鳴の環", "Ring of Resonance"), Power.Resonance, MemoryDamageBalance.Effect_legacy_t_res_key_power_perRank,
                new Txt("味方の近くで戦うほど活きます。", "Best when fighting close to allies.")),
        };

        /// <summary>同じ固有効果を複数持つ場合の合計上限。</summary>
        private static readonly Dictionary<Power, int> PowerCaps = new Dictionary<Power, int>
        {
            [Power.Momentum] = EquipmentCapsBalance.Power_Momentum,
            [Power.Retaliation] = EquipmentCapsBalance.Power_Retaliation,
            [Power.Bulwark] = EquipmentCapsBalance.Power_Bulwark,
            [Power.Lifesteal] = EquipmentCapsBalance.Power_Lifesteal,
            [Power.Thorns] = EquipmentCapsBalance.Power_Thorns,
            [Power.Executioner] = EquipmentCapsBalance.Power_Executioner,
            [Power.Resonance] = EquipmentCapsBalance.Power_Resonance,
            [Power.Tailwind] = EquipmentCapsBalance.Power_Tailwind,
            [Power.Barrier] = EquipmentCapsBalance.Power_Barrier,
            [Power.SecondWind] = EquipmentCapsBalance.Power_SecondWind,
            [Power.Blaze] = EquipmentCapsBalance.Power_Blaze,
            [Power.ChainLightning] = EquipmentCapsBalance.Power_ChainLightning,
            [Power.Shatter] = EquipmentCapsBalance.Power_Shatter,
            [Power.Aegis] = EquipmentCapsBalance.Power_Aegis,
            [Power.Bloodlust] = EquipmentCapsBalance.Power_Bloodlust,
            [Power.Ember] = EquipmentCapsBalance.Power_Ember,
            [Power.Frost] = EquipmentCapsBalance.Power_Frost,
            [Power.Radiance] = EquipmentCapsBalance.Power_Radiance,
            [Power.Umbra] = EquipmentCapsBalance.Power_Umbra,
            [Power.Convergence] = EquipmentCapsBalance.Power_Convergence,
            [Power.EchoingDodge] = EquipmentCapsBalance.Power_EchoingDodge,
            [Power.UltimateSurge] = EquipmentCapsBalance.Power_UltimateSurge,
            [Power.SoulSiphon] = EquipmentCapsBalance.Power_SoulSiphon,
            [Power.Whirlwind] = EquipmentCapsBalance.Power_Whirlwind,
            [Power.Frenzy] = EquipmentCapsBalance.Power_Frenzy,
            [Power.OpeningStrike] = EquipmentCapsBalance.Power_OpeningStrike,
            [Power.StarShield] = EquipmentCapsBalance.Power_StarShield,
            [Power.Sprint] = EquipmentCapsBalance.Power_Sprint,
            [Power.Vigor] = EquipmentCapsBalance.Power_Vigor,
            [Power.Overload] = EquipmentCapsBalance.Power_Overload,
            [Power.Finale] = EquipmentCapsBalance.Power_Finale,
            [Power.CriticalEcho] = EquipmentCapsBalance.Power_CriticalEcho,
            [Power.Fetters] = EquipmentCapsBalance.Power_Fetters,
            [Power.CrystalResonance] = EquipmentCapsBalance.Power_CrystalResonance,
            [Power.PreyPride] = EquipmentCapsBalance.Power_PreyPride,
            [Power.OverflowingLife] = EquipmentCapsBalance.Power_OverflowingLife,
            [Power.Devotion] = EquipmentCapsBalance.Power_Devotion,
            [Power.Wildfire] = EquipmentCapsBalance.Power_Wildfire,
            [Power.StillWater] = EquipmentCapsBalance.Power_StillWater,
            [Power.SpendersWard] = EquipmentCapsBalance.Power_SpendersWard,
            [Power.PerfectRead] = EquipmentCapsBalance.Power_PerfectRead,
            [Power.LucidBoon] = EquipmentCapsBalance.Power_LucidBoon,
            [Power.ShadowStep] = EquipmentCapsBalance.Power_ShadowStep,
            [Power.Steam] = EquipmentCapsBalance.Power_Steam,
            [Power.Eclipse] = EquipmentCapsBalance.Power_Eclipse,
            [Power.Cinder] = EquipmentCapsBalance.Power_Cinder,
            [Power.FrostCrystal] = EquipmentCapsBalance.Power_FrostCrystal,
            [Power.ShieldbreakBurst] = EquipmentCapsBalance.Power_ShieldbreakBurst,
            [Power.SharedWard] = EquipmentCapsBalance.Power_SharedWard,
            [Power.ShieldBash] = EquipmentCapsBalance.Power_ShieldBash,
            [Power.GleamingWard] = EquipmentCapsBalance.Power_GleamingWard,
            [Power.CoStar] = EquipmentCapsBalance.Power_CoStar,
            [Power.TriumphSong] = EquipmentCapsBalance.Power_TriumphSong,
            [Power.WatchfulHand] = EquipmentCapsBalance.Power_WatchfulHand,
            [Power.KindnessReturns] = EquipmentCapsBalance.Power_KindnessReturns,
            [Power.WanderersEdge] = EquipmentCapsBalance.Power_WanderersEdge,
            [Power.FocusFire] = EquipmentCapsBalance.Power_FocusFire,
            [Power.Breakout] = EquipmentCapsBalance.Power_Breakout,
            [Power.DuelistsWay] = EquipmentCapsBalance.Power_DuelistsWay,
            [Power.ImmovableStance] = EquipmentCapsBalance.Power_ImmovableStance,
            [Power.RunUp] = EquipmentCapsBalance.Power_RunUp,
            [Power.StrafeShot] = EquipmentCapsBalance.Power_StrafeShot,
            [Power.RelayHand] = EquipmentCapsBalance.Power_RelayHand,
            [Power.StardustCycle] = EquipmentCapsBalance.Power_StardustCycle,
            [Power.UmbralHeritage] = EquipmentCapsBalance.Power_UmbralHeritage,
            [Power.BrittleIce] = EquipmentCapsBalance.Power_BrittleIce,
            [Power.ElementalHarvest] = EquipmentCapsBalance.Power_ElementalHarvest,
            [Power.PrismShift] = EquipmentCapsBalance.Power_PrismShift,
            [Power.PackFeast] = EquipmentCapsBalance.Power_PackFeast,
            [Power.VanguardsOath] = EquipmentCapsBalance.Power_VanguardsOath,
            [Power.DeathBloom] = EquipmentCapsBalance.Power_DeathBloom,
            [Power.Medley] = EquipmentCapsBalance.Power_Medley,
            [Power.Spellsweep] = EquipmentCapsBalance.Power_Spellsweep,
            [Power.BareHandedPride] = EquipmentCapsBalance.Power_BareHandedPride,
            [Power.OpeningSalvo] = EquipmentCapsBalance.Power_OpeningSalvo,
            [Power.PileOn] = EquipmentCapsBalance.Power_PileOn,
            [Power.AceInHand] = EquipmentCapsBalance.Power_AceInHand,
            [Power.CrystalCircuit] = EquipmentCapsBalance.Power_CrystalCircuit,
            [Power.ShardBoon] = EquipmentCapsBalance.Power_ShardBoon,
            [Power.Lifeline] = EquipmentCapsBalance.Power_Lifeline,
            [Power.DreamOmen] = EquipmentCapsBalance.Power_DreamOmen,
            [Power.RearguardsWay] = EquipmentCapsBalance.Power_RearguardsWay,
            [Power.TollOfGrudge] = EquipmentCapsBalance.Power_TollOfGrudge,
            [Power.UnbowedMind] = EquipmentCapsBalance.Power_UnbowedMind,
            [Power.PilingLuck] = EquipmentCapsBalance.Power_PilingLuck,
            [Power.WeakPointWound] = EquipmentCapsBalance.Power_WeakPointWound,
            [Power.CritSplash] = EquipmentCapsBalance.Power_CritSplash,
            [Power.ReturningBlade] = EquipmentCapsBalance.Power_ReturningBlade,
            [Power.ReadyGuard] = EquipmentCapsBalance.Power_ReadyGuard,
            [Power.Apothecary] = EquipmentCapsBalance.Power_Apothecary,
            [Power.SpilloverStrike] = EquipmentCapsBalance.Power_SpilloverStrike,
            // v1.32 A：星図だけの効果。設計表の合計上限（撃破ゴールド+12%・エリート/ボス+25%・ダスト+12%＋籠10%）。
            [Power.KillGoldPct] = EquipmentCapsBalance.Power_KillGoldPct,
            [Power.EliteKillGoldPct] = EquipmentCapsBalance.Power_EliteKillGoldPct,
            [Power.DreamDustPct] = EquipmentCapsBalance.Power_DreamDustPct,
            [Power.DreamDustDelvePct] = EquipmentCapsBalance.Power_DreamDustDelvePct,
        };

        /// <summary>MOD由来の能力値の合計上限（計画書 第7章の L2 上限 +120% を基準）。</summary>
        private static readonly Dictionary<Stat, int> StatCaps = new Dictionary<Stat, int>
        {
            [Stat.AttackPct] = EquipmentCapsBalance.Stat_AttackPct, // v1.28：120 → 100（星図の分が最大約60あるので、装備の分を残す）
            [Stat.PowerPct] = EquipmentCapsBalance.Stat_PowerPct,
            [Stat.AttackFlat] = EquipmentCapsBalance.Stat_AttackFlat,
            [Stat.PowerFlat] = EquipmentCapsBalance.Stat_PowerFlat,
            [Stat.AttackSpeedPct] = EquipmentCapsBalance.Stat_AttackSpeedPct,
            [Stat.CritChancePct] = EquipmentCapsBalance.Stat_CritChancePct,
            [Stat.CritDamagePct] = EquipmentCapsBalance.Stat_CritDamagePct,
            [Stat.MaxHealthPct] = EquipmentCapsBalance.Stat_MaxHealthPct,
            [Stat.MaxHealthFlat] = EquipmentCapsBalance.Stat_MaxHealthFlat,
            [Stat.Armor] = EquipmentCapsBalance.Stat_Armor,
            [Stat.HealthRegen] = EquipmentCapsBalance.Stat_HealthRegen,
            [Stat.Haste] = EquipmentCapsBalance.Stat_Haste,
            [Stat.MoveSpeedPct] = EquipmentCapsBalance.Stat_MoveSpeedPct,
            [Stat.Tenacity] = EquipmentCapsBalance.Stat_Tenacity,
            [Stat.FireAmp] = EquipmentCapsBalance.Stat_FireAmp,
            [Stat.ColdAmp] = EquipmentCapsBalance.Stat_ColdAmp,
            [Stat.LightAmp] = EquipmentCapsBalance.Stat_LightAmp,
            [Stat.DarkAmp] = EquipmentCapsBalance.Stat_DarkAmp,
            [Stat.AttackRangePct] = EquipmentCapsBalance.Stat_AttackRangePct,
            [Stat.FourthAttackShift] = EquipmentCapsBalance.Stat_FourthAttackShift, // v1.27：連装・四の型は1段まで
            [Stat.EssenceSlotIdentity] = EquipmentCapsBalance.Stat_EssenceSlotIdentity, // v1.27：星図でエッセンス枠+1（能力補正ではなく枠の追加）
            [Stat.EssenceSlotMovement] = EquipmentCapsBalance.Stat_EssenceSlotMovement,
            [Stat.HealPower] = EquipmentCapsBalance.Stat_HealPower,
            [Stat.ShieldPower] = EquipmentCapsBalance.Stat_ShieldPower,
            [Stat.SummonPower] = EquipmentCapsBalance.Stat_SummonPower,
            [Stat.SacrificeReduction] = EquipmentCapsBalance.Stat_SacrificeReduction,
        };

        private static readonly Dictionary<string, BaseDef> BaseById = Index(Bases, b => b.Id);
        private static readonly Dictionary<string, UniqueDef> UniqueById = Index(Uniques, u => u.Id);
        private static readonly Dictionary<AuthoredStarKey, TalentDef> TalentByKey = IndexTalents();
        private static readonly Dictionary<string, TalentDef> UniqueTalentById = IndexUniqueTalents();

        private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key)
        {
            var d = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var i in items) d.Add(key(i), i);
            return d;
        }

        private static Dictionary<AuthoredStarKey, TalentDef> IndexTalents()
        {
            var nodes = new Dictionary<AuthoredStarKey, TalentDef>();
            foreach (var talent in Talents.Concat(HeroSigils.All))
                nodes.Add(new AuthoredStarKey(talent.HeroKey, talent.Id), talent);
            return nodes;
        }

        private static Dictionary<string, TalentDef> IndexUniqueTalents()
        {
            var nodes = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
            foreach (var talent in TalentByKey.Values)
                if (nodes.ContainsKey(talent.Id)) nodes[talent.Id] = null;
                else nodes.Add(talent.Id, talent);
            return nodes;
        }

        public static bool TryGetBase(string id, out BaseDef def) => BaseById.TryGetValue(id ?? string.Empty, out def);
        public static bool TryGetUnique(string id, out UniqueDef def) => UniqueById.TryGetValue(id ?? string.Empty, out def);
        public static bool TryGetTalent(string heroKey, string localId, out TalentDef def)
        {
            if (StarClusters.TryGetRegisteredTalent(heroKey, localId, out def, out bool registered)) return true;
            if (registered) { def = null; return false; }
            return TalentByKey.TryGetValue(new AuthoredStarKey(HeroSigils.HasTree(heroKey) ? heroKey : null, localId ?? string.Empty), out def);
        }

        /// <summary>Only for globally unique legacy IDs. Shared local IDs require an explicit hero.</summary>
        public static bool TryGetTalent(string id, out TalentDef def)
        {
            if (StarClusters.TryGetRegisteredTalent(id, out def)) return true;
            if (!UniqueTalentById.TryGetValue(id ?? string.Empty, out def)) return false;
            if (def == null) throw new InvalidOperationException("A shared star ID requires its hero key: " + id);
            return true;
        }

        public static BaseDef GetBase(string id)
        {
            if (!TryGetBase(id, out var def)) throw new KeyNotFoundException("未知の装備基礎: " + id);
            return def;
        }

        public static IReadOnlyList<AffixDef> AffixPool(Slot slot) => AffixPools[slot];
        public static IReadOnlyList<PowerRange> PowerPool(Slot slot) => PowerPools[slot];

        /// <summary>Direct conditional AD/AP powers may roll only on Epic or higher gear.</summary>
        public static bool IsPowerDroppable(Power power) => Enum.IsDefined(typeof(Power), power)
            && power != Power.None && power != Power.ShadowStep && !CurrencyStars.IsPower(power);
        public static bool PowerAllowedForRarity(Power power, Rarity rarity) => IsPowerDroppable(power)
            && (rarity >= Rarity.Epic || !NewPowersV129.IsConditionalAttribute(power));

        /// <summary>エピックの銘（1つ目の固有効果から）。v1.22。</summary>
        private static readonly Dictionary<Power, Txt> Epithets = new Dictionary<Power, Txt>
        {
            [Power.Momentum] = new Txt("連撃の", "Relentless"),
            [Power.Retaliation] = new Txt("報復の", "Vengeful"),
            [Power.Bulwark] = new Txt("鉄壁の", "Steadfast"),
            [Power.Lifesteal] = new Txt("吸血の", "Vampiric"),
            [Power.Thorns] = new Txt("逆棘の", "Barbed"),
            [Power.Executioner] = new Txt("断罪の", "Executing"),
            [Power.Resonance] = new Txt("共鳴の", "Resonant"),
            [Power.Tailwind] = new Txt("追い風の", "Windborne"),
            [Power.Barrier] = new Txt("護りの", "Warding"),
            [Power.SecondWind] = new Txt("不倒の", "Undying"),
            [Power.Blaze] = new Txt("猛火の", "Blazing"),
            [Power.ChainLightning] = new Txt("雷鳴の", "Thundering"),
            [Power.Shatter] = new Txt("砕きの", "Shattering"),
            [Power.Aegis] = new Txt("守護の", "Guardian"),
            [Power.Bloodlust] = new Txt("血狂いの", "Bloodmad"),
            [Power.Ember] = new Txt("燻る", "Smoldering"),
            [Power.Frost] = new Txt("凍てつく", "Freezing"),
            [Power.Radiance] = new Txt("輝く", "Radiant"),
            [Power.Umbra] = new Txt("宵闇の", "Dusky"),
            [Power.Convergence] = new Txt("四元の", "Converging"),
            [Power.EchoingDodge] = new Txt("残像の", "Echoing"),
            [Power.UltimateSurge] = new Txt("昂りの", "Surging"),
            [Power.SoulSiphon] = new Txt("魂喰いの", "Soul-eating"),
            [Power.Whirlwind] = new Txt("旋風の", "Whirling"),
            [Power.Frenzy] = new Txt("乱戦の", "Frenzied"),
            [Power.OpeningStrike] = new Txt("先駆けの", "Vanguard"),
            [Power.StarShield] = new Txt("星護りの", "Starward"),
            [Power.Sprint] = new Txt("疾駆の", "Swift"),
            [Power.Vigor] = new Txt("万全の", "Hale"),
            [Power.Overload] = new Txt("溢れる", "Overflowing"),
            [Power.Finale] = new Txt("終曲の", "Final"),
            [Power.CriticalEcho] = new Txt("余韻の", "Lingering"),
            [Power.Fetters] = new Txt("枷の", "Fettering"),
            [Power.CrystalResonance] = new Txt("結晶の", "Crystalline"),
            [Power.PreyPride] = new Txt("誇り高き", "Proud"),
            [Power.OverflowingLife] = new Txt("満ちる", "Brimming"),
            [Power.Devotion] = new Txt("祈りの", "Devout"),
            [Power.Wildfire] = new Txt("飛び火の", "Spreading"),
            [Power.StillWater] = new Txt("止水の", "Stilled"),
            [Power.SpendersWard] = new Txt("散財の", "Lavish"),
            [Power.PerfectRead] = new Txt("見切りの", "Keen-eyed"),
            [Power.LucidBoon] = new Txt("明晰な", "Lucid"),
        };

        public static Txt Epithet(Power p) => Epithets.TryGetValue(p, out var t) ? t : NewPowersV129.IsPower(p) ? NewPowersV129.Epithet(p) : ElementReactions.Epithet(p);

        public static int PowerCap(Power p) => PowerCaps.TryGetValue(p, out int c) ? c : 0;
        public static int StatCap(Stat s) => StatCaps.TryGetValue(s, out int c) ? c : 0;

        public static IEnumerable<BaseDef> BasesFor(Slot slot)
        {
            foreach (var b in Bases)
                if (b.Slot == slot) yield return b;
        }

        public static int AffixCount(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return 1;
                case Rarity.Uncommon: return 2;
                default: return 3;
            }
        }

        /// <summary>レア度による特性値の倍率（%）。</summary>
        public static int RarityValuePct(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return 110;
                case Rarity.Epic: return 120;
                case Rarity.Legendary: return 130;
                default: return 100;
            }
        }

        /// <summary>
        /// アイテムレベルで伸びる能力値か（v1.28）。固定値（攻撃力・魔力・最大HP・防御・HP回復・記憶加速・行動妨害耐性）だけが伸び、
        /// %の能力値はレベルで伸びない（レア度と強化の倍率は掛かる）。
        /// </summary>
        public static bool ScalesWithItemLevel(Stat s)
        {
            switch (s)
            {
                case Stat.AttackFlat:
                case Stat.PowerFlat:
                case Stat.MaxHealthFlat:
                case Stat.Armor:
                case Stat.HealthRegen:
                case Stat.Haste:
                case Stat.Tenacity:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 属性を付ける量の説明（v1.28：平均のスタック数ではなく%で書く）。
        /// 100 ごとに確実に1つ、残りはその%の確率でもう1つ。例：60 →「60%の確率で1つ」、160 →「1つ、さらに60%の確率でもう1つ」。
        /// </summary>
        private static string ElementJa(int v, string element)
        {
            int sure = Math.Max(0, v) / 100, chance = Math.Max(0, v) % 100;
            if (sure == 0) return $"通常攻撃が当たると、{chance}%の確率で敵に{element}を1つ付ける";
            string head = $"通常攻撃が当たるたびに、敵に{element}を{sure}つ付ける";
            return chance > 0 ? head + $"（さらに{chance}%の確率でもう1つ）" : head;
        }

        private static string ElementEn(int v, string element)
        {
            int sure = Math.Max(0, v) / 100, chance = Math.Max(0, v) % 100;
            if (sure == 0) return $"Basic attack hits have a {chance}% chance to apply 1 {element}";
            string head = $"Basic attack hits apply {sure} {element}";
            return chance > 0 ? head + $" (plus a {chance}% chance for 1 more)" : head;
        }

        /// <summary>固定攻魔はレベルごと+5%、他の固定能力は+3%。%能力は伸びない。</summary>
        public static int LevelScalePct(Stat stat, int itemLevel)
        {
            if (!ScalesWithItemLevel(stat)) return GearBalance.BaseLevelScalePct;
            int l = Math.Max(1, Math.Min(itemLevel, ItemLevelScalingCap));
            int growth = stat == Stat.AttackFlat || stat == Stat.PowerFlat
                ? GearBalance.FlatDamageGrowthPct : GearBalance.OtherFixedGrowthPct;
            return GearBalance.BaseLevelScalePct + growth * (l - 1);
        }

        /// <summary>基礎能力・特性の強化倍率（%）。表の範囲に丸める。</summary>
        public static int EnhanceScalePct(int enhance)
            => ForgeBalance.StatPercents[Math.Max(0, Math.Min(enhance, ForgeBalance.StatPercents.Length - 1))];

        /// <summary>固有効果の強化倍率（%）。表の範囲に丸める。</summary>
        public static int EnhancePowerScalePct(int enhance)
            => ForgeBalance.PowerPercents[Math.Max(0, Math.Min(enhance, ForgeBalance.PowerPercents.Length - 1))];

        public static int EnhanceCost(int currentEnhance)
            => currentEnhance < 0 || currentEnhance >= ForgeBalance.EnhanceShardCosts.Length
                ? int.MaxValue : ForgeBalance.EnhanceShardCosts[currentEnhance];

        public static int ForgeMaterialCostMultiplier(Rarity rarity) => rarity >= Rarity.Epic ? ForgeBalance.EpicMaterialMultiplier : 1;
        public static int EnhanceCost(Relic relic) => EnhanceCost(relic.Enhance) * ForgeMaterialCostMultiplier(relic.Rarity);
        public static double EnhanceDemotionChance => ForgeBalance.DemotionChance;
        public static int EnhanceDemotionSteps => ForgeBalance.DemotionSteps;
        public static int AwakenNightmareMultiplier => ForgeBalance.AwakenNightmareMultiplier;
        public static int MaxLimitBreaks(Rarity rarity)
            => ForgeBalance.MaxBreaks[rarity >= Rarity.Legendary ? 4 : rarity >= Rarity.Epic ? 3 : rarity >= Rarity.Rare ? 2 : rarity >= Rarity.Uncommon ? 1 : 0];
        public static int MaxEnhanceFor(Relic relic) => MaxEnhanceFor(relic == null ? Rarity.Common : relic.Rarity, relic?.LimitBreaks ?? 0);
        public static int MaxEnhanceFor(Rarity rarity, int limitBreaks)
            => MaxEnhance + EnhanceStepPerBreak * Math.Max(0, Math.Min(MaxLimitBreaks(rarity), limitBreaks));
        public static int LimitBreakTuningCost(int n) => ForgeBalance.BreakTuningCosts[n <= 1 ? 0 : n == 2 ? 1 : 2];
        public static int LimitBreakShardCost(int n) => ForgeBalance.BreakShardCosts[n <= 1 ? 0 : n == 2 ? 1 : 2];
        public static int LimitBreakTuningCost(int n, Rarity rarity) => LimitBreakTuningCost(n) * ForgeMaterialCostMultiplier(rarity);
        public static int LimitBreakShardCost(int n, Rarity rarity) => LimitBreakShardCost(n) * ForgeMaterialCostMultiplier(rarity);
        public static int RetuneCost(int retunesDone) => ForgeBalance.RetuneBaseCost + ForgeBalance.RetuneCostPerLevel * retunesDone;
        public static int RetuneCost(Relic relic) => RetuneCost(relic.Retunes) * ForgeMaterialCostMultiplier(relic.Rarity);

        public static int SalvageShards(Rarity rarity)
            => ForgeBalance.SalvageShards[rarity == Rarity.Common ? 0 : rarity == Rarity.Uncommon ? 1 : rarity == Rarity.Rare ? 2 : rarity == Rarity.Epic ? 3 : 4];
        public static int SalvageTuning(Rarity rarity) => rarity >= Rarity.Epic ? ForgeBalance.SalvageEpicTuning : 0;

        public static int GuaranteedEnhanceSteps(DreamEvent dreamEvent)
            => dreamEvent == DreamEvent.Fountain ? ForgeBalance.FountainSteps
                : dreamEvent == DreamEvent.ForgeShrine ? ForgeBalance.ForgeShrineSteps
                : dreamEvent == DreamEvent.TemperingAltar ? ForgeBalance.TemperingAltarSteps : 0;
        public static int GuaranteedEnhanceCost(DreamEvent dreamEvent, Rarity rarity)
            => dreamEvent == DreamEvent.ForgeShrine ? ForgeBalance.ForgeShrineShards * ForgeMaterialCostMultiplier(rarity) : 0;
        public static bool CanGuaranteedEnhance(Relic relic, DreamEvent dreamEvent)
        {
            int steps = GuaranteedEnhanceSteps(dreamEvent);
            return relic != null && steps > 0 && relic.Enhance <= MaxEnhanceFor(relic) - steps;
        }

        /// <summary>夢のレベル n から n+1 へ必要な経験値。</summary>
        public static int XpToNext(int level) => 60 + 25 * Math.Max(1, level) + 3 * level * level;

        public static int KillXp(MonsterTier tier)
        {
            switch (tier)
            {
                case MonsterTier.Lesser: return 1;
                case MonsterTier.Normal: return 2;
                case MonsterTier.MiniBoss: return 12;
                default: return 50;
            }
        }

        /// <summary>装着中の伝説の遺物にたまる覚醒の力。悪夢化の報酬格ではなく、元の敵の格を使う。</summary>
        public static int AwakenPoints(MonsterTier tier, bool nightmare)
        {
            int points;
            switch (tier)
            {
                case MonsterTier.Boss: points = ForgeBalance.AwakenBossPoints; break;
                case MonsterTier.MiniBoss: points = ForgeBalance.AwakenMiniBossPoints; break;
                default: points = ForgeBalance.AwakenNormalPoints; break;
            }
            return nightmare ? points * ForgeBalance.AwakenNightmareMultiplier : points;
        }

        public const int SecureXp = 20;
        public const int VictoryXp = 100;

        /// <summary>同じ系統の遺物を count 個装着したときの累積ボーナス（2個・3個・4個・6個）。</summary>
        public static IEnumerable<StatLine> SetBonus(Line line, int count)
        {
            if (count >= 2)
            {
                switch (line)
                {
                    case Line.Offense:
                        yield return new StatLine(Stat.AttackPct, 5);
                        yield return new StatLine(Stat.PowerPct, 5);
                        break;
                    case Line.Guard:
                        yield return new StatLine(Stat.Armor, 8);
                        break;
                    default:
                        yield return new StatLine(Stat.Haste, 8);
                        break;
                }
            }
            if (count >= 3)
            {
                switch (line)
                {
                    case Line.Offense: yield return new StatLine(Stat.AttackSpeedPct, 6); break;
                    case Line.Guard: yield return new StatLine(Stat.MaxHealthPct, 6); break;
                    default: yield return new StatLine(Stat.MoveSpeedPct, 4); break;
                }
            }
            if (count >= 4)
            {
                switch (line)
                {
                    case Line.Offense: yield return new StatLine(Stat.CritChancePct, 4); break;
                    case Line.Guard: yield return new StatLine(Stat.Tenacity, 12); break;
                    default: yield return new StatLine(Stat.Haste, 8); break;
                }
            }
            if (count >= 6)
            {
                switch (line)
                {
                    case Line.Offense:
                        yield return new StatLine(Stat.AttackPct, 8);
                        yield return new StatLine(Stat.PowerPct, 8);
                        break;
                    case Line.Guard:
                        yield return new StatLine(Stat.Armor, 12);
                        yield return new StatLine(Stat.MaxHealthPct, 6);
                        break;
                    default:
                        yield return new StatLine(Stat.PowerPct, 8);
                        yield return new StatLine(Stat.MoveSpeedPct, 4);
                        break;
                }
            }
        }

        public static Txt SlotName(Slot s)
        {
            switch (s)
            {
                case Slot.Weapon: return new Txt("主装備", "Weapon");
                case Slot.Armor: return new Txt("防具", "Armor");
                case Slot.Head: return new Txt("頭", "Head");
                case Slot.Hands: return new Txt("手", "Hands");
                case Slot.Feet: return new Txt("足", "Feet");
                default: return new Txt("装飾品", "Charm");
            }
        }

        public static Txt RarityName(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return new Txt("コモン", "Common");
                case Rarity.Uncommon: return new Txt("アンコモン", "Uncommon");
                case Rarity.Rare: return new Txt("レア", "Rare");
                case Rarity.Epic: return new Txt("エピック", "Epic");
                default: return new Txt("固有品", "Legendary");
            }
        }

        public static Txt LineName(Line l)
        {
            switch (l)
            {
                case Line.Offense: return new Txt("破壊", "Destruction");
                case Line.Guard: return new Txt("生命", "Life");
                default: return new Txt("想像", "Imagination");
            }
        }

        public static string FormatStat(Stat s, int v)
        {
            string sign = v >= 0 ? "+" : "";
            switch (s)
            {
                case Stat.AttackPct: return Loc.T($"攻撃力 {sign}{v}%", $"Attack damage {sign}{v}%");
                case Stat.PowerPct: return Loc.T($"魔力 {sign}{v}%", $"Ability power {sign}{v}%");
                case Stat.AttackSpeedPct: return Loc.T($"攻撃速度 {sign}{v}%", $"Attack speed {sign}{v}%");
                case Stat.CritChancePct: return Loc.T($"会心率 {sign}{v}パーセントポイント", $"Critical chance {sign}{v} percentage points");
                case Stat.CritDamagePct: return Loc.T($"会心ダメージ倍率 {sign}{v}パーセントポイント", $"Critical damage multiplier {sign}{v} percentage points");
                case Stat.MaxHealthPct: return Loc.T($"最大HP {sign}{v}%", $"Maximum health {sign}{v}%");
                case Stat.MaxHealthFlat: return Loc.T($"最大HP {sign}{v}", $"Maximum health {sign}{v}");
                case Stat.AttackFlat: return Loc.T($"攻撃力 {sign}{v}", $"Attack damage {sign}{v}");
                case Stat.PowerFlat: return Loc.T($"魔力 {sign}{v}", $"Ability power {sign}{v}");
                case Stat.Armor: return Loc.T($"防御 {sign}{v}", $"Armor {sign}{v}");
                case Stat.HealthRegen: return Loc.T($"HP自動回復量 {sign}{v}/秒", $"Health regeneration {sign}{v}/s");
                case Stat.Haste: return Loc.T($"スキル加速 {sign}{v}（記憶のクールダウン回復を速める）", $"Ability haste {sign}{v} (speeds up memory cooldown recovery)");
                case Stat.MoveSpeedPct: return Loc.T($"移動速度 {sign}{v}%", $"Movement speed {sign}{v}%");
                case Stat.Tenacity: return Loc.T($"行動妨害耐性 {sign}{v}（受ける行動妨害の持続時間を短くする）", $"Tenacity {sign}{v} (shortens crowd-control effects on you)");
                case Stat.FireAmp: return Loc.T($"火属性効果 {sign}{v}%", $"Fire effect strength {sign}{v}%");
                case Stat.ColdAmp: return Loc.T($"冷気属性効果 {sign}{v}%", $"Cold effect strength {sign}{v}%");
                case Stat.LightAmp: return Loc.T($"光属性効果 {sign}{v}%", $"Light effect strength {sign}{v}%");
                case Stat.AttackRangePct: return Loc.T($"通常攻撃の射程 {sign}{v}%", $"Basic attack range {sign}{v}%");
                case Stat.FourthAttackShift: return Loc.T($"強化通常攻撃までの必要回数 -{v}発（4発目の強化攻撃を早める）", $"Attacks needed for an empowered basic attack -{v} (brings the empowered fourth attack forward)");
                case Stat.EssenceSlotIdentity: return Loc.T($"エッセンス枠 {sign}{v}（装備中のアイデンティティ記憶が対象）", $"Essence slots {sign}{v} (for your equipped Identity memory)");
                case Stat.EssenceSlotMovement: return Loc.T($"エッセンス枠 {sign}{v}（装備中の移動の記憶が対象）", $"Essence slots {sign}{v} (for your equipped Movement memory)");
                case Stat.HealPower: return Loc.T($"与えるHP回復量 {sign}{v}%（自分・味方への回復が対象。装備・星の同じ能力値は合計で最大{StatCap(s)}%）", $"Healing granted {sign}{v}% (to yourself and allies; combined equipment and star bonuses to this stat capped at {StatCap(s)}%)");
                case Stat.ShieldPower: return Loc.T($"与える障壁量 {sign}{v}%（自分・味方への障壁が対象。装備・星の同じ能力値は合計で最大{StatCap(s)}%）", $"Shield amount granted {sign}{v}% (to yourself and allies; combined equipment and star bonuses to this stat capped at {StatCap(s)}%)");
                case Stat.SummonPower: return Loc.T($"自分の召喚獣の与ダメージ {sign}{v}%（装備・星の同じ能力値は合計で最大{StatCap(s)}%）", $"Your summons' damage {sign}{v}% (combined equipment and star bonuses to this stat capped at {StatCap(s)}%)");
                case Stat.SacrificeReduction: return Loc.T($"HPを捧げる技のHP消費 {-v:+0;-0;0}%（装備・星の同じ能力値による軽減は合計で最大{StatCap(s)}%）", $"HP cost of health-sacrificing skills {-v:+0;-0;0}% (combined equipment and star reductions capped at {StatCap(s)}%)");
                default: return Loc.T($"闇属性効果 {sign}{v}%", $"Dark effect strength {sign}{v}%");
            }
        }

        public static string PowerName(Power p)
        {
            if (NewPowersV129.IsPower(p)) return NewPowersV129.Name(p);
            if (CurrencyStars.IsPower(p)) return CurrencyStars.Name(p);
            switch (p)
            {
                case Power.Momentum: return Loc.T("連撃", "Momentum");
                case Power.Retaliation: return Loc.T("逆襲", "Retaliation");
                case Power.Bulwark: return Loc.T("鉄の輪", "Bulwark");
                case Power.Lifesteal: return Loc.T("吸命", "Lifesteal");
                case Power.Thorns: return Loc.T("棘", "Thorns");
                case Power.Executioner: return Loc.T("処刑", "Executioner");
                case Power.Resonance: return Loc.T("共鳴", "Resonance");
                case Power.Tailwind: return Loc.T("追い風", "Tailwind");
                case Power.Barrier: return Loc.T("護りの灯", "Barrier");
                case Power.SecondWind: return Loc.T("灯守", "Second Wind");
                case Power.Blaze: return Loc.T("烈火", "Blaze");
                case Power.ChainLightning: return Loc.T("雷鎖", "Chain Lightning");
                case Power.Shatter: return Loc.T("爆砕", "Shatter");
                case Power.Aegis: return Loc.T("守護霊", "Aegis");
                case Power.Bloodlust: return Loc.T("血の渇き", "Bloodlust");
                case Power.Ember: return Loc.T("火種", "Ember");
                case Power.Frost: return Loc.T("霜", "Frost");
                case Power.Radiance: return Loc.T("輝き", "Radiance");
                case Power.Umbra: return Loc.T("影", "Umbra");
                case Power.Convergence: return Loc.T("四元の共鳴", "Convergence");
                case Power.EchoingDodge: return Loc.T("回避の残響", "Echoing Dodge");
                case Power.UltimateSurge: return Loc.T("終の昂り", "Ultimate Surge");
                case Power.SoulSiphon: return Loc.T("吸魂", "Soul Siphon");
                case Power.Whirlwind: return Loc.T("旋風", "Whirlwind");
                case Power.Frenzy: return Loc.T("乱戦", "Melee Frenzy");
                case Power.OpeningStrike: return Loc.T("先制", "Opening Strike");
                case Power.StarShield: return Loc.T("星の加護", "Star Shield");
                case Power.Sprint: return Loc.T("疾駆", "Sprint");
                case Power.Vigor: return Loc.T("万全", "Vigor");
                case Power.Overload: return Loc.T("過負荷", "Overload");
                case Power.Finale: return Loc.T("終曲", "Finale");
                case Power.CriticalEcho: return Loc.T("会心の余韻", "Critical Echo");
                case Power.Fetters: return Loc.T("足枷", "Fetters");
                case Power.CrystalResonance: return Loc.T("結晶共鳴", "Crystal Resonance");
                case Power.PreyPride: return Loc.T("狩人の誇り", "Hunter's Pride");
                case Power.OverflowingLife: return Loc.T("溢れる命", "Overflowing Life");
                case Power.Devotion: return Loc.T("祈願", "Devotion");
                case Power.Wildfire: return Loc.T("飛び火", "Wildfire");
                case Power.StillWater: return Loc.T("止水", "Still Water");
                case Power.SpendersWard: return Loc.T("散財の護り", "Spender's Ward");
                case Power.PerfectRead: return Loc.T("見切り", "Perfect Read");
                case Power.LucidBoon: return Loc.T("明晰", "Lucid Boon");
                case Power.ShadowStep: return Loc.T("瞬歩の刃", "Flash-Step Blade");
                case Power.Steam: return Loc.T("蒸気", "Steam");
                case Power.Eclipse: return Loc.T("蝕", "Eclipse");
                case Power.Cinder: return Loc.T("燃え殻", "Cinder");
                case Power.FrostCrystal: return Loc.T("氷晶", "Frost Crystal");
                default: return "-";
            }
        }

        /// <summary>画面に出す固有効果：1行目に効果の要点、続けて条件・間隔・上限を「・」の箇条書きにする。</summary>
        public static string FormatPowerBullets(Power p, int v, string indent = "") => EffectLayout.Bullets(FormatPower(p, v), indent);

        public static string FormatPower(Power p, int v)
        {
            if (NewPowersV129.IsPower(p)) return NewPowersV129.Describe(p, v);
            if (CurrencyStars.IsPower(p)) return CurrencyStars.Describe(p, v);
            if (ElementReactions.IsPower(p)) return FormatReaction(p, v);
            string body;
            string ElementHeading(string ja, string en) => v < 100
                ? Loc.T($"{ja}付与確率 +{v}パーセントポイント／", $"{en} application chance +{v} percentage points / ")
                : Loc.T($"{ja}付与 +{v / 100}つ" + (v % 100 > 0 ? $"（さらに{v % 100}%の確率でもう1つ）" : "") + "／",
                    $"{en} application +{v / 100} " + (v / 100 == 1 ? "stack" : "stacks") + (v % 100 > 0 ? $" (plus a {v % 100}% chance for 1 more)" : "") + " / ");
            switch (p)
            {
                case Power.Momentum: body = Loc.T($"攻撃速度 +{v}%／敵を倒すたびに{PowerRuntime.MomentumDuration}秒間。{PowerRuntime.MomentumMaxStacks}回まで重なり、撃破で全体の持続時間を延長", $"Attack speed +{v}% per kill for {PowerRuntime.MomentumDuration}s; stacks up to {PowerRuntime.MomentumMaxStacks} times, with kills refreshing the whole duration"); break;
                case Power.Retaliation: body = Loc.T($"攻撃力・魔力 +{v}%／敵からダメージを受けた後{PowerRuntime.RetaliationDuration}秒間。重ならず、再発動で時間を延長", $"Attack damage and ability power +{v}% for {PowerRuntime.RetaliationDuration}s after enemy damage; refreshes without stacking"); break;
                case Power.Bulwark: body = Loc.T($"防御 +{v}／自分の周囲{PowersBalance.BulwarkRange}m以内に敵が{PowerRuntime.BulwarkEnemies}体以上いる間", $"Armor +{v} while at least {PowerRuntime.BulwarkEnemies} enemies are within {PowersBalance.BulwarkRange}m of you"); break;
                case Power.Lifesteal: body = Loc.T($"HP回復 +最大HPの{v / 10f:0.#}%／通常攻撃が命中したとき。自分を回復（{PowerRuntime.LifestealInterval}秒に1回）", $"Health restored +{v / 10f:0.#}% of your maximum health on a basic attack hit; heals yourself (once per {PowerRuntime.LifestealInterval}s)"); break;
                case Power.Thorns: body = Loc.T($"反撃ダメージ +受けたダメージの{v}%／敵からダメージを受けたとき、その敵に返す（{PowerRuntime.ThornsInterval}秒に1回。反射ダメージからは発動しない）", $"Retaliatory damage +{v}% of damage received, dealt back to the attacking enemy (once per {PowerRuntime.ThornsInterval}s; reflected damage does not trigger it)"); break;
                case Power.Executioner: body = Loc.T($"通常攻撃の追加ダメージ +攻撃力の{v}%／HPが{PowerRuntime.ExecuteThreshold:0%}未満の敵に命中したとき", $"Basic attack bonus damage +{v}% of your attack damage when hitting an enemy below {PowerRuntime.ExecuteThreshold:0%} health"); break;
                case Power.Resonance: body = Loc.T($"自分の攻撃力・魔力 +{v}%、味方は+{v / 2}%／自分の周囲{PowerRuntime.ResonanceRange}m以内に味方がいる間。味方がいなければ自分は+{v / 2}%。{PowerRuntime.ResonanceRange}m以内の味方への付与は重ならず、最も高い値だけを適用", $"Your attack damage and ability power +{v}%, nearby allies' +{v / 2}% while an ally is within {PowerRuntime.ResonanceRange}m; without one, your bonus is +{v / 2}%. Bonuses granted to allies within {PowerRuntime.ResonanceRange}m do not stack; only the highest applies"); break;
                case Power.Tailwind: body = Loc.T($"移動速度 +{v}%／敵を倒した後{PowerRuntime.TailwindDuration}秒間。重ならず、撃破で時間を延長", $"Movement speed +{v}% for {PowerRuntime.TailwindDuration}s after a kill; kills refresh it without stacking"); break;
                case Power.Barrier: body = Loc.T($"障壁 +自分の最大HPの{v}%／自分へ{PowerRuntime.BarrierDuration}秒間付与。最初は{PowerRuntime.BarrierFirstDelay}秒後、以後{PowerRuntime.BarrierInterval}秒ごと", $"Shield +{v}% of your maximum health for {PowerRuntime.BarrierDuration}s; first granted after {PowerRuntime.BarrierFirstDelay}s, then every {PowerRuntime.BarrierInterval}s"); break;
                case Power.SecondWind: body = Loc.T($"HP回復 +自分の最大HPの{v}%／自分のHPが{PowerRuntime.SecondWindThreshold:0%}未満のとき（{PowerRuntime.SecondWindCooldown}秒に1回）", $"Health restored +{v}% of your maximum health while below {PowerRuntime.SecondWindThreshold:0%} health (once per {PowerRuntime.SecondWindCooldown}s)"); break;
                case Power.Blaze: body = Loc.T($"通常攻撃の追加魔法ダメージ +攻撃力か魔力の高い方の{v}%／4発目の強化通常攻撃が命中したとき（命中回数ではなく、強化攻撃の周期で判定）", $"Basic attack bonus magic damage +{v}% of the higher of your attack damage or ability power when your empowered fourth attack hits (judged by the empowered-attack cycle, not by counting hits)"); break;
                case Power.ChainLightning: body = Loc.T($"追加魔法ダメージ +攻撃力か魔力の高い方の{v}%／通常攻撃命中時に{PowerRuntime.ChainChance:0%}の確率。当てた敵の周囲{PowerRuntime.ChainRange}m以内の別の敵、最大{PowerRuntime.ChainTargets}体にそれぞれ与える", $"Bonus magic damage +{v}% of the higher of your attack damage or ability power per target; a basic attack hit has a {PowerRuntime.ChainChance:0%} chance to hit up to {PowerRuntime.ChainTargets} other enemies within {PowerRuntime.ChainRange}m of the struck enemy"); break;
                case Power.Shatter: body = Loc.T($"範囲ダメージ +攻撃力か魔力の高い方の{v}%／敵を倒したとき、倒した敵の周囲{PowerRuntime.ShatterRadius}m以内の敵それぞれに与える（この効果による撃破からは連鎖しない）", $"Area damage +{v}% of the higher of your attack damage or ability power per enemy within {PowerRuntime.ShatterRadius}m of an enemy you kill (kills from this effect do not chain)"); break;
                case Power.Aegis: body = Loc.T($"障壁 +自分の最大HPの{v}%／自分の最大HPの{PowerRuntime.AegisThreshold:0%}以上の一撃を受けたとき、自分へ{PowersBalance.AegisShieldDuration}秒間付与（{PowerRuntime.AegisCooldown}秒に1回）", $"Shield +{v}% of your maximum health for {PowersBalance.AegisShieldDuration}s when a single hit deals at least {PowerRuntime.AegisThreshold:0%} of your maximum health (once per {PowerRuntime.AegisCooldown}s)"); break;
                case Power.Bloodlust: body = Loc.T($"攻撃速度 +{v}%／自分のHPが{PowerRuntime.BloodlustThreshold:0%}未満の間", $"Attack speed +{v}% while your health is below {PowerRuntime.BloodlustThreshold:0%}"); break;
                case Power.Ember: body = ElementHeading("火", "Fire") + Loc.T(ElementJa(v, "火") + "（火の蓄積上限なし）", ElementEn(v, "Fire") + " (Fire has no stack limit)"); break;
                case Power.Frost: body = Loc.T($"冷気付与確率 +{v}パーセントポイント／通常攻撃が命中した敵に冷気を付ける。冷気は重ならない", $"Cold application chance +{v} percentage points on basic attack hits; applies Cold to the struck enemy, without stacking"); break;
                case Power.Radiance: body = ElementHeading("光", "Light") + Loc.T(ElementJa(v, "光") + "（光は5つまで、3つ以上で光ダメージが必ず会心）", ElementEn(v, "Light") + " (up to 5 stacks; Light damage always critically hits at 3 or more)"); break;
                case Power.Umbra: body = ElementHeading("闇", "Dark") + Loc.T(ElementJa(v, "闇") + "。通常攻撃の会心時は、この確率判定と別に必ずもう1つ付与（闇は5つまで）", ElementEn(v, "Dark") + ". A critical basic hit always applies 1 additional stack independently of this roll (up to 5 stacks)"); break;
                case Power.Convergence: body = Loc.T($"追加ダメージ +攻撃力か魔力の高い方の{v}%／自分が属性を付けた敵に火・冷気・光・闇がそろっているとき、その敵に与える（同じ敵に{PowerRuntime.ConvergenceCooldown}秒に1回）", $"Bonus damage +{v}% of the higher of your attack damage or ability power to an enemy when your elemental application leaves it with Fire, Cold, Light and Dark (once per {PowerRuntime.ConvergenceCooldown}s per enemy)"); break;
                case Power.EchoingDodge: body = Loc.T($"次の通常攻撃の追加ダメージ +攻撃力か魔力の高い方の{v}%／移動の記憶を使った後{PowerRuntime.EchoingDodgeWindow}秒以内の次の命中時。重ならず、再使用で時間を延長。次の通常攻撃への追加効果は最大の1つだけを消費し、残りは期限まで保持", $"Next basic attack bonus damage +{v}% of the higher of your attack damage or ability power on the next hit within {PowerRuntime.EchoingDodgeWindow}s of using a Movement memory; refreshes without stacking. Only the largest next-basic bonus is consumed; others remain until their expiry"); break;
                case Power.UltimateSurge: body = Loc.T($"攻撃力・魔力 +{v}%／奥義の記憶を使った後{PowerRuntime.SurgeDuration}秒間。重ならず、再使用で時間を延長", $"Attack damage and ability power +{v}% for {PowerRuntime.SurgeDuration}s after using an Ultimate memory; refreshes without stacking"); break;
                case Power.SoulSiphon: body = Loc.T($"HP回復 +自分の最大HPの{v / 10f:0.#}%／敵を倒したとき（{PowerRuntime.SoulSiphonInterval}秒に1回）", $"Health restored +{v / 10f:0.#}% of your maximum health on kill (once per {PowerRuntime.SoulSiphonInterval}s)"); break;
                case Power.Whirlwind: body = Loc.T($"範囲ダメージ +攻撃力か魔力の高い方の{v}%／移動の記憶を使ったとき、自分の周囲{PowerRuntime.WhirlwindRadius}m以内の敵それぞれに与える（{PowerRuntime.WhirlwindInterval}秒に1回）", $"Area damage +{v}% of the higher of your attack damage or ability power per enemy within {PowerRuntime.WhirlwindRadius}m of you when using a Movement memory (once per {PowerRuntime.WhirlwindInterval}s)"); break;
                case Power.Frenzy: body = Loc.T($"攻撃速度 +{v}%／自分の周囲{PowersBalance.FrenzyRange}m以内の敵1体につき。最大{PowerRuntime.FrenzyMaxEnemies}体分まで", $"Attack speed +{v}% per enemy within {PowersBalance.FrenzyRange}m of you, counting up to {PowerRuntime.FrenzyMaxEnemies} enemies"); break;
                case Power.OpeningStrike: body = Loc.T($"通常攻撃の追加ダメージ +攻撃力の{v}%／HPが{PowerRuntime.OpeningStrikeThreshold:0%}以上の敵に命中したとき", $"Basic attack bonus damage +{v}% of your attack damage when hitting an enemy at or above {PowerRuntime.OpeningStrikeThreshold:0%} health"); break;
                case Power.StarShield: body = Loc.T($"障壁 +自分の最大HPの{v}%／奥義の記憶を使ったとき、自分へ{PowerRuntime.StarShieldDuration}秒間付与", $"Shield +{v}% of your maximum health, granted to yourself for {PowerRuntime.StarShieldDuration}s when using an Ultimate memory"); break;
                case Power.Sprint: body = Loc.T($"移動速度・攻撃速度 +{v}%／移動の記憶を使った後{PowerRuntime.SprintDuration}秒間。重ならず、再使用で時間を延長", $"Movement speed and attack speed +{v}% for {PowerRuntime.SprintDuration}s after using a Movement memory; refreshes without stacking"); break;
                case Power.Vigor: body = Loc.T($"攻撃力・魔力 +{v}%／自分のHPが{PowerRuntime.VigorThreshold:0%}以上の間", $"Attack damage and ability power +{v}% while your health is at or above {PowerRuntime.VigorThreshold:0%}"); break;
                case Power.Overload: body = Loc.T($"攻撃力・魔力 +{v}%／通常記憶を使った後{PowerRuntime.OverloadDuration}秒間。移動・奥義・アイデンティティ記憶は対象外。重ならず、再使用で時間を延長", $"Attack damage and ability power +{v}% for {PowerRuntime.OverloadDuration}s after using an ordinary memory; excludes Movement, Ultimate and Identity memories; refreshes without stacking"); break;
                case Power.Finale: body = Loc.T($"奥義の残りクールダウン -{v}%／通常記憶の3枠すべてを{PowerRuntime.FinaleWindow}秒以内に使ったとき（{PowerRuntime.FinaleCooldown}秒に1回。次の発動には3枠を改めて使う）", $"Ultimate remaining cooldown -{v}% when all 3 ordinary-memory slots are used within {PowerRuntime.FinaleWindow}s (once per {PowerRuntime.FinaleCooldown}s; all 3 must be used again for the next trigger)"); break;
                case Power.CriticalEcho: body = Loc.T($"通常記憶3枠の残りクールダウン -{v / 10f:0.#}秒／通常攻撃が会心で命中したとき（{PowerRuntime.CriticalEchoCooldown}秒に1回）", $"Remaining cooldown of all 3 ordinary-memory slots -{v / 10f:0.#}s on a critical basic attack hit (once per {PowerRuntime.CriticalEchoCooldown}s)"); break;
                case Power.Fetters: body = Loc.T($"与ダメージ +{v}%／スタン・スロウ・冷気のいずれかを受けている敵が対象", $"Damage dealt +{v}% to enemies that are stunned, slowed or chilled"); break;
                case Power.CrystalResonance: body = Loc.T($"攻撃力・魔力 +{v}%／装着中の全エッセンスの品質合計100%ごとに。100%未満の端数は数えず、最大{PowerRuntime.CrystalResonanceMaxTiers * 100}%分まで", $"Attack damage and ability power +{v}% per 100% total quality across all equipped Essences; ignores incomplete hundreds, counts up to {PowerRuntime.CrystalResonanceMaxTiers * 100}%"); break;
                case Power.PreyPride: body = Loc.T($"攻撃力・魔力 +{v}%／現在のハンター追跡度1につき。最大追跡度{PowerRuntime.PreyPrideMaxLevel}まで", $"Attack damage and ability power +{v}% per current hunter tracking level, counting up to level {PowerRuntime.PreyPrideMaxLevel}"); break;
                case Power.OverflowingLife: body = Loc.T($"障壁 +超過回復量の{v}%／最大HPを超えた回復が出たとき、自分へ{PowerRuntime.OverflowingLifeDuration}秒間付与。1回の付与量は自分の最大HPの{PowerRuntime.OverflowingLifeMaxHealthRatio:0%}まで（段数でこの{PowerRuntime.OverflowingLifeMaxHealthRatio:0%}上限は増えない）", $"Shield +{v}% of overhealing, granted to yourself for {PowerRuntime.OverflowingLifeDuration}s; each grant is capped at {PowerRuntime.OverflowingLifeMaxHealthRatio:0%} of your maximum health (this {PowerRuntime.OverflowingLifeMaxHealthRatio:0%} limit does not increase with ranks)"); break;
                case Power.Devotion: body = Loc.T($"攻撃力・魔力 +{v}%／聖堂の使用が成功するたび、そのゾーンの間持続。最大{PowerRuntime.DevotionMaxStacks}回分まで", $"Attack damage and ability power +{v}% per successful shrine use, lasting for the zone; up to {PowerRuntime.DevotionMaxStacks} uses"); break;
                case Power.Wildfire: body = Loc.T($"火の拡散確率 +{v}パーセントポイント／自分の火付与で火が{PowerRuntime.WildfireMinStacks}つ以上になった敵から、周囲{PowerRuntime.WildfireRange}m以内の最も近い別の敵1体へ火を1つ付ける（同じ送り元から{PowerRuntime.WildfireCooldown}秒に1回。拡散した火からは連鎖しない）", $"Fire spread chance +{v} percentage points when your Fire application leaves an enemy with at least {PowerRuntime.WildfireMinStacks} Fire stacks; applies 1 Fire to the nearest other enemy within {PowerRuntime.WildfireRange}m (once per {PowerRuntime.WildfireCooldown}s per source enemy; spread Fire does not chain)"); break;
                case Power.StillWater: body = Loc.T($"障壁 +自分の最大HPの{v}%／自分の技で敵をスタンさせたとき、自分へ{PowerRuntime.StillWaterDuration}秒間付与（{PowerRuntime.StillWaterCooldown}秒に1回）", $"Shield +{v}% of your maximum health for {PowerRuntime.StillWaterDuration}s when your own skill stuns an enemy (once per {PowerRuntime.StillWaterCooldown}s)"); break;
                case Power.SpendersWard: body = Loc.T($"障壁 +自分の最大HPの{v}%／ゴールドを合計{PowerRuntime.SpendersWardGold}使うごとに、自分へ{PowerRuntime.SpendersWardDuration}秒間付与。最大{PowerRuntime.SpendersWardMaxStacks}回分まで重なる。各付与の期限は延長せず、満杯時の{PowerRuntime.SpendersWardGold}ゴールド分は持ち越さない", $"Shield +{v}% of your maximum health for {PowerRuntime.SpendersWardDuration}s per {PowerRuntime.SpendersWardGold} gold spent; up to {PowerRuntime.SpendersWardMaxStacks} grants coexist, with separate expiry times. Spending while full does not bank another grant"); break;
                case Power.PerfectRead: body = Loc.T($"攻撃速度 +{v}%／無敵でダメージを実際に無効化した後{PowerRuntime.PerfectReadDuration}秒間（{PowerRuntime.PerfectReadCooldown}秒に1回。重ならず、再発動で時間を延長）", $"Attack speed +{v}% for {PowerRuntime.PerfectReadDuration}s after actually negating damage with invulnerability (once per {PowerRuntime.PerfectReadCooldown}s; refreshes without stacking)"); break;
                case Power.LucidBoon: body = Loc.T($"攻撃力・魔力 +{v}%／有効な邪悪な明晰夢1つにつき。最大{PowerRuntime.LucidBoonMaxDreams}つまで。装備・星の同じ効果を合算した最終増加量は{Content.PowerCap(Power.LucidBoon)}%まで", $"Attack damage and ability power +{v}% per active Evil lucid dream, counting up to {PowerRuntime.LucidBoonMaxDreams}; the resulting bonus from this effect across equipment and stars is capped at {Content.PowerCap(Power.LucidBoon)}%"); break;
                case Power.ShadowStep: body = Loc.T($"次の通常攻撃の追加ダメージ +攻撃力か魔力の高い方の{v}%／回避・ダッシュ・瞬間移動後{PowerRuntime.ShadowStepWindow}秒以内の次の命中時。重ならず、再移動で時間を延長。次の通常攻撃への追加効果は最大の1つだけを消費し、残りは期限まで保持", $"Next basic attack bonus damage +{v}% of the higher of your attack damage or ability power on the next hit within {PowerRuntime.ShadowStepWindow}s of a dodge, dash or teleport; refreshes without stacking. Only the largest next-basic bonus is consumed; others remain until their expiry"); break;
                default: return "-";
            }
            string cap = p == Power.StillWater || p == Power.SpendersWard || p == Power.PerfectRead
                ? Loc.T($"（装備・星の同じ効果は合計で最大{PowerCap(p)}%）", $" (combined across equipment and stars, this effect is capped at {PowerCap(p)}%)")
                : "";
            if (NewPowersV129.IsConditionalAttribute(p))
                cap += Loc.T($"。条件つき攻撃力・魔力の増加は、覚醒前の数値で全効果の合計が最大{PowerRuntime.ConditionalPowerCap}%",
                    $". Conditional attack damage and ability power bonuses together are capped at +{PowerRuntime.ConditionalPowerCap}%, counted before awakening");
            return body + cap;
        }

        private static string FormatReaction(Power p, int v)
        {
            v = Math.Max(0, Math.Min(PowerCap(p), v));
            string effect;
            switch (p)
            {
                case Power.Steam:
                    effect = Loc.T($"範囲無属性ダメージ +攻撃力か魔力の高い方の{v}%、敵の移動速度 -30%／自分が属性を付けた敵に火と冷気がそろっているとき、その敵と周囲3m以内の敵それぞれが対象。スロウは2秒間（装備・星の同じ効果は合計で最大120%。道標倍率を掛ける前の1回分の割合で、全対象へのダメージ合計ではない）",
                        $"Area non-elemental damage +{v}% of the higher of your attack damage or ability power and enemy movement speed -30% for 2s when your elemental application leaves an enemy with Fire and Cold; affects it and each enemy within 3m (combined equipment and star damage percentage of this effect capped at 120% before Waypoint multipliers, not a damage total across targets)");
                    break;
                case Power.Eclipse:
                    effect = Loc.T($"敵が自分から受けるダメージ +{v}%／自分が属性を付けた敵に光と闇がそろっているとき、その敵へ4秒間適用（装備・星の同じ効果は合計で最大25%。道標倍率を掛ける前の割合。被ダメージ増加は重ならず、最も高い値だけを適用）",
                        $"Damage an enemy takes from you +{v}% for 4s when your elemental application leaves it with Light and Dark (combined equipment and star value of this effect capped at 25% before Waypoint multipliers; damage-vulnerability effects use only the highest value without stacking)");
                    break;
                case Power.Cinder:
                    effect = Loc.T($"撃破時の火付与 +1つ／自分が属性を付けた敵に火と闇がそろっているとき、その敵に印を付ける。印の敵が倒れると、倒れた敵の周囲4m以内の別の敵それぞれへ火を付与（装備・星の同じ効果は合計で最大1つ。道標倍率を掛ける前の数。印は重ならず、味方の撃破でも発動）",
                        $"Fire applied on death +1 stack / when your elemental application leaves an enemy with Fire and Dark, mark it; when the marked enemy dies, apply Fire to each other enemy within 4m of it (combined across equipment and stars, capped at 1 stack before Waypoint multipliers; marks do not stack; ally kills also trigger it)");
                    break;
                default:
                    effect = Loc.T($"障壁 +自分の最大HPの{v}%／自分が属性を付けた敵に光と冷気がそろっているとき、自分へ4秒間付与（装備・星の同じ効果は合計で最大12%。道標倍率を掛ける前の、1回の障壁量の基準となる割合）",
                        $"Shield +{v}% of your maximum health for 4s when your elemental application leaves an enemy with Light and Cold (combined equipment and star value of this effect capped at 12% before Waypoint multipliers; this is the percentage used for one shield grant)");
                    break;
            }
            return effect + Loc.T("。属性は消費しない。同じ敵への同じ反応は6秒に1回まで（継続・多段の属性付与による連発を防ぐ）。反応の追加効果は連鎖しない。道標の倍率は効果量に掛かる。",
                ". Does not consume elements. Each reaction triggers at most once per 6s per enemy, to prevent repeated triggers from persistent or multi-hit elemental effects. Reaction effects do not chain. Waypoint multipliers apply to the effect amount.");
        }
    }
}

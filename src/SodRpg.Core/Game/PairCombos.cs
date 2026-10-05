using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public enum PairComboTrigger { None = 0, OnUse = 1, OnHit = 2, OnKill = 3, OnCrit = 4, OnBasicAttack = 5 }
    public enum PairComboStep { None = 0, Mark = 1, Window = 2 }
    public enum PairComboHitKind { Any = 0, InitialExplosion = 1, TerminalExplosion = 2 }

    public sealed class PairComboDef
    {
        public string Id { get; internal set; }
        public string HeroKey { get; internal set; }
        public BridgeSuccessDefinition AuthoredDefinition { get; internal set; }
        public int BridgeIndex { get; internal set; }
        public string BridgeId { get; internal set; }
        public string RouteA { get; internal set; }
        public string RouteB { get; internal set; }
        public string StarA { get; internal set; }
        public string StarB { get; internal set; }
        public Txt Name { get; internal set; }
        public string TriggerMemory { get; internal set; }
        public PairComboTrigger Trigger { get; internal set; }
        public PairComboStep Step { get; internal set; }
        public string PayoffMemory { get; internal set; }
        public PairComboTrigger PayoffTrigger { get; internal set; }
        /// <summary>Required payoff hit phase; the host must supply this alongside activation identity.</summary>
        public PairComboHitKind PayoffHitKind { get; internal set; }
        public GimmickEffect Effect { get; internal set; }
        public int Value { get; internal set; }
        public int Arg { get; internal set; }
        public float Cooldown { get; internal set; }
        /// <summary>Explicit memory whose remaining cooldown is reduced.</summary>
        public string RechargeMemory { get; internal set; }
        /// <summary>Exact rank-one, rank-two and rank-three values; never linearly scaled.</summary>
        public IReadOnlyList<int> TableRankValues { get; internal set; }
        /// <summary>One payoff per supplied activation identity; the host must supply that identity.</summary>
        public bool OncePerActivation { get; internal set; }
        /// <summary>Only the receiving memory's kill qualifies, never an unrelated death.</summary>
        public bool KillByPayoffMemory { get; internal set; }
        public float WindowDuration { get; internal set; } = 4f;
        /// <summary>One payoff per marked victim (opening strikes) or killed victim, in addition to the activation limit.</summary>
        public bool OncePerVictim { get; internal set; }
        /// <summary>
        /// 回避そのものは起点にしない。ただしダメージを出す移動の記憶（重装タックル・フロストチャージ）の「当たったとき」だけは例外（v1.30 の決定）。
        /// </summary>
        public bool MovementOrigin => AuthoredDefinition == null && TriggerMemory != null && TriggerMemory.StartsWith("St_M_", StringComparison.Ordinal)
            && !(Trigger == PairComboTrigger.OnHit && DamagingMovement.Contains(TriggerMemory));

        private static readonly HashSet<string> DamagingMovement = new HashSet<string>(StringComparer.Ordinal) { "St_M_Charge", "St_M_FrostyCharge" };
    }

    public sealed class PairComboEntry
    {
        public PairComboDef Def { get; set; }
        public int Ranks { get; set; }
        public int Value => Def == null || Ranks <= 0 ? 0
            : Def.TableRankValues[Math.Min(Ranks, PairCombos.MaxRanks) - 1];
    }

    public static class PairCombos
    {
        public static int MaxEntries => BuildLimits.MaxPairComboEntries;
        public const int MaxRanks = 3;
        public const float Duration = 4f;
        private static readonly string[] BridgeSlugs = { "force", "insight", "vessel", "armor", "recall", "rhythm", "resolve" };
        public static readonly IReadOnlyList<PairComboDef> All = Create();
        private static readonly Dictionary<string, PairComboDef> ByBridge = Index(false);
        private static readonly Dictionary<string, PairComboDef> ById = Index(true);

        private static Dictionary<string, PairComboDef> Index(bool id)
        {
            var result = new Dictionary<string, PairComboDef>(StringComparer.Ordinal);
            foreach (var def in All) result.Add(id ? def.Id : def.BridgeId, def);
            return result;
        }
        public static IReadOnlyList<PairComboDef> RegisteredAll => StarClusters.RegisteredPairs(All);
        public static PairComboDef ForBridge(string bridgeId) => bridgeId != null && ByBridge.TryGetValue(bridgeId, out var def) ? def
            : StarClusters.RegisteredPair(bridgeId, true);
        public static PairComboDef Get(string id) => id != null && ById.TryGetValue(id, out var def) ? def
            : StarClusters.RegisteredPair(id, false);
        public static PairComboEntry Clamp(PairComboEntry entry)
        {
            var def = entry?.Def == null ? null : Get(entry.Def.Id);
            return def == null || def.AuthoredDefinition != null || entry.Ranks <= 0 ? null : new PairComboEntry { Def = def, Ranks = Math.Min(MaxRanks, entry.Ranks) };
        }
        public static PairComboEntry Activate(PairComboDef def, HeroState hero, int bridgeRanks)
        {
            if (def == null || hero == null || bridgeRanks <= 0
                || !hero.Talents.TryGetValue(def.StarA, out int a) || a <= 0
                || !hero.Talents.TryGetValue(def.StarB, out int b) || b <= 0) return null;
            return Clamp(new PairComboEntry { Def = def, Ranks = bridgeRanks });
        }
        public static bool Equipped(PairComboDef def, ICollection<string> memories) => def != null && memories != null
            && memories.Contains(def.RouteA) && memories.Contains(def.RouteB);

        public static string Describe(PairComboDef def, int ranks = 1)
        {
            if (def?.AuthoredDefinition != null)
                return AuthoredMechanisms.Describe(new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess,
                    ChannelId = def.Id, Bridge = def.AuthoredDefinition });
            var entry = Clamp(new PairComboEntry { Def = def, Ranks = ranks });
            if (entry == null) return "";
            def = entry.Def;
            bool ja = Loc.Japanese;
            string a = MemoryName(def.RouteA, ja), b = MemoryName(def.RouteB, ja);
            string origin = Action(def.Trigger, def.TriggerMemory, ja);
            if (!ja) origin = "W" + origin.Substring(1);
            string payoff = def.Step == PairComboStep.None ? "" : Action(def.PayoffTrigger, def.PayoffMemory, ja);
            string duration = def.WindowDuration.ToString("R", CultureInfo.InvariantCulture);
            string markedPayoff = def.PayoffTrigger == PairComboTrigger.OnKill
                ? (ja ? MemoryName(def.PayoffMemory, true) + "で連携印のある敵を倒すと"
                    : "When " + MemoryName(def.PayoffMemory, false) + " kills that combo-marked enemy")
                : (ja ? "連携印のある敵に" : "Against that combo-marked enemy, ") + payoff;
            string steps = def.Step == PairComboStep.None ? origin
                : def.Step == PairComboStep.Mark
                    ? origin + (ja ? "、その敵に連携の印を4秒付ける（自分から受けるダメージ +" + (entry.Ranks + 1) + "%）。" : ", mark that enemy for 4 seconds (damage taken from you +" + (entry.Ranks + 1) + "%). ") + markedPayoff
                    : origin + (ja ? "、その後" + duration + "秒以内に" : ", then within " + duration + " seconds, ") + payoff;
            string n = entry.Value.ToString(CultureInfo.InvariantCulture);
            string effect;
            switch (def.Effect)
            {
                case GimmickEffect.Recharge:
                    string target = MemoryName(def.RechargeMemory, ja);
                    effect = ja ? target + "の残りクールダウンを" + n + "%縮める" : "reduce " + target + "'s remaining cooldown by " + n + "%";
                    break;
                case GimmickEffect.RechargeOther:
                    effect = ja ? "装備中のほかの通常記憶の残りクールダウンを" + n + "%縮める（移動・奥義・アイデンティティと、発動した記憶自身は対象外）"
                        : "reduce the remaining cooldown of your other equipped normal memories by " + n + "% (excluding movement, ultimate, identity and the triggering memory itself)";
                    break;
                case GimmickEffect.Shield:
                    effect = ja ? "自分に最大HPの" + n + "%の障壁を張る（4秒）" : "gain a shield equal to " + n + "% of maximum health for 4 seconds";
                    break;
                case GimmickEffect.Heal:
                    effect = ja ? "自分" + (def.Arg == 1 ? "と10m以内の味方" : "") + "を最大HPの" + n + "%回復する"
                        : "heal yourself" + (def.Arg == 1 ? " and allies within 10m" : "") + " for " + n + "% of maximum health";
                    break;
                case GimmickEffect.Burst:
                    effect = ja ? "当てた敵の周り4mに、攻撃力か魔力の高い方の" + n + "%の追加ダメージを与える（魔力が高ければ魔法）"
                        : "deal " + n + "% of the higher of attack damage or ability power as extra damage within 4m of the hit enemy (magic damage if ability power is higher)";
                    break;
                case GimmickEffect.Element:
                    string element = ja ? (def.Arg == 0 ? "火" : def.Arg == 1 ? "冷気" : def.Arg == 2 ? "光" : "闇")
                        : (def.Arg == 0 ? "fire" : def.Arg == 1 ? "cold" : def.Arg == 2 ? "light" : "darkness");
                    int whole = entry.Value / 100, chance = entry.Value % 100;
                    string stacks = ja ? whole + "つ" + (chance == 0 ? "" : "（さらに" + chance + "%の確率でもう1つ）")
                        : whole + (whole == 1 ? " stack" : " stacks") + (chance == 0 ? "" : " (plus a " + chance + "% chance of one more)");
                    effect = ja ? "当てた敵に" + element + "を" + stacks + "付ける" : "apply " + stacks + " of " + element + " to the hit enemy";
                    break;
                default:
                    effect = ja ? "当てたダメージの" + n + "%を0.3秒後にもう一度与える" : "deal " + n + "% of the hit damage again after 0.3 seconds";
                    break;
            }
            string cd = def.Cooldown.ToString("R", CultureInfo.InvariantCulture);
            string interval = def.Cooldown == 0 ? (ja ? "間隔制限なし" : "no cooldown")
                : (ja ? "このペア全体で" + cd + "秒に1回" : "once every " + cd + (def.Cooldown == 1f ? " second" : " seconds") + " per pair, shared across enemies");
            string limit = def.OncePerActivation
                ? (ja ? "。1回の使用・通常攻撃・召喚物の攻撃につき1回まで" : "; at most once per memory use, basic attack or summon attack") : "";
            if (def.OncePerVictim) limit += ja
                ? (def.PayoffTrigger == PairComboTrigger.OnKill ? "。倒した敵1体につき1回まで" : "。連携印のある敵1体につき1回まで（最初の3回の追加ダメージで繰り返さない）")
                : (def.PayoffTrigger == PairComboTrigger.OnKill ? "; once per killed victim" : "; once per combo-marked victim, not once for each of the first three bonus hits");
            if (def.KillByPayoffMemory) limit += ja ? "。受け手の記憶で倒した敵のみ。味方・召喚獣・ほかの記憶の撃破は数えない"
                : "; only kills by the receiving memory count, not kills by allies, summons or other memories";
            string summon = def.Trigger == PairComboTrigger.OnBasicAttack || def.PayoffTrigger == PairComboTrigger.OnBasicAttack
                ? (ja ? "召喚獣がいるとき、自分が基本攻撃を撃つたびに判定する（命中は不要）。" : "Checks whenever you fire your own basic attack while summons are present; no hit is required. ") : "";
            string disabled = def.MovementOrigin ? (ja ? "移動の記憶は起点にならないため、この組は発動しない。" : "Inactive: movement memories cannot start a combo. ") : "";
            string headline = def.Effect == GimmickEffect.Recharge || def.Effect == GimmickEffect.RechargeOther
                ? Loc.T($"残りクールダウン −{n}%", $"Remaining cooldown −{n}%")
                : def.Effect == GimmickEffect.Heal ? Loc.T($"HP回復 +最大HPの{n}%", $"Healing +{n}% of maximum HP")
                : def.Effect == GimmickEffect.Shield ? Loc.T($"障壁 +最大HPの{n}%", $"Shield +{n}% of maximum HP")
                : def.Effect == GimmickEffect.Burst ? Loc.T($"範囲追加ダメージ +攻撃力・魔力の高い方の{n}%", $"Area bonus damage +{n}% of the higher of attack damage or ability power")
                : def.Effect == GimmickEffect.Echo ? Loc.T($"追撃ダメージ +与ダメージの{n}%", $"Follow-up damage +{n}% of damage dealt")
                : Loc.T($"属性付与 +{entry.Value / 100}個", $"Element application +{entry.Value / 100} stacks");
            return headline + Loc.T("：", ": ") + steps + (ja ? "、" : ", ") + effect
                + (ja ? "（" + interval + limit + "。追加ダメージは属性なし・連鎖なし。星の追加ダメージでは連携を開始・成立させられない）"
                    : " (" + interval + limit + "; extra damage is elementless and cannot chain; star-generated damage cannot start or complete the combo).")
                + "\n" + (ja ? a + "と" + b + "を両方装備し、星『" + StarName(def.BridgeId) + "』『" + StarName(def.StarA) + "』『" + StarName(def.StarB) + "』を各1段以上取得すると有効。"
                    : "Equip both " + a + " and " + b + "; acquire at least one rank in stars “" + StarName(def.BridgeId) + "”, “" + StarName(def.StarA) + "” and “" + StarName(def.StarB) + "”.")
                + "\n" + (ja ? "橋の1/2/3段での値：" : "Values at bridge ranks 1/2/3: ")
                + (def.Effect == GimmickEffect.Element
                    ? string.Join("/", def.TableRankValues.Select(v => (v / 100m).ToString(CultureInfo.InvariantCulture))) + Loc.T("個（小数部分は追加1個の確率）", " stacks (fractional part is the chance of one extra stack)")
                    : string.Join("/", def.TableRankValues) + "%")
                + disabled + summon + Restrictions(def, ja);
        }
        private static string Restrictions(PairComboDef def, bool ja)
        {
            if (def.HeroKey == "Hero_Vesper" && (def.BridgeIndex == 6 || def.BridgeIndex == 7))
                return ja ? "洗礼の最初の爆発だけが受け手。6秒間の追加火炎では発動しない。" : "Only Baptism's initial explosion completes the combo, never its six seconds of additional fire damage.";
            if (def.HeroKey == "Hero_Cetus" && def.BridgeIndex == 2)
                return ja ? "領域の持続終了時の爆発だけが受け手。継続ダメージでは発動せず、爆発しない選択では働かない。" : "Only the domain's terminal explosion completes the combo, not ongoing damage; non-exploding variants cannot trigger it.";
            if (def.HeroKey == "Hero_Husk" && def.BridgeIndex == 3)
                return ja ? "印を付けるのは風の傷の固有斬りだけ（一歩一殺では付かない）。成立は滅殺態勢の剣気の命中だけで、基本攻撃や星の追加ダメージでは働かない。"
                    : "Only the Scar of the Wind's own slash places the mark (Killing Flow places none), and only Annihilation Stance's aura hits complete it, not basic attacks or star-generated damage.";
            if (def.HeroKey == "Hero_Husk" && def.BridgeIndex == 5)
                return ja ? "死の刻印の楔が当たった敵を、死の刻印の楔で倒したときだけ。再入力の瞬間移動では印も成立も起きない。"
                    : "Only a Death Mark wedge's hit places the mark and only a wedge kill completes it; the re-cast teleport does neither.";
            if (def.HeroKey == "Hero_Mist" && def.BridgeIndex == 3)
                return ja ? "覚醒中の電撃爆発による撃破だけ。基本攻撃・ほかの記憶の撃破は数えない。" : "Only kills by the awakened lightning explosion count, not basic attacks or other memories.";
            if (def.HeroKey == "Hero_Mist" && (def.BridgeIndex == 4 || def.BridgeIndex == 5))
                return ja ? "連携印はプリオリテ本体の印とは別IDで、選択・成長・爆発を変えない。" : "The combo mark has a separate ID from Priorité's own mark and does not change its selection, growth or explosion.";
            return "";
        }
        private static string MemoryName(string memory, bool ja)
        {
            return Loc.T("記憶『", "Memory “") + (ja ? Links.Name(memory).Ja : Links.Name(memory).En) + Loc.T("』", "”");
        }
        private static string Action(PairComboTrigger trigger, string memory, bool ja)
        {
            string name = MemoryName(memory, ja);
            switch (trigger)
            {
                case PairComboTrigger.OnUse: return ja ? name + "を使うと" : "when you use " + name;
                case PairComboTrigger.OnCrit: return ja ? name + "が会心すると" : "when " + name + " critically hits";
                case PairComboTrigger.OnKill: return ja ? name + "で敵を倒すと" : "when " + name + " kills an enemy";
                case PairComboTrigger.OnBasicAttack: return ja ? name + "で自分の基本攻撃を撃つと" : "when you fire your own basic attack with " + name;
                default: return ja ? name + "が当たると" : "when " + name + " hits";
            }
        }

        private static string StarName(string id)
        {
            var def = ForBridge(id);
            if (def != null) return def.Name.ToString();
            foreach (var star in HeroSigils.All) if (star.Id == id) return star.Name.ToString();
            foreach (var hero in HeroSigils.All.Select(t => t.HeroKey).Where(k => k != null).Distinct())
                foreach (var star in HeroSigils.TreeFor(hero)) if (star.Id == id) return star.Name.ToString();
            return Loc.T("前提の星", "prerequisite star");
        }

        private static PairComboDef D(string hero, int bridge, string slugA, string memoryA, string slugB, string memoryB,
            string ja, string en, string origin, PairComboTrigger trigger, PairComboStep step, string payoff, PairComboTrigger payoffTrigger,
            GimmickEffect effect, int one, int two, int three, int arg = 0, float cooldown = 0f, string recharge = null,
            bool oncePerActivation = false, bool killByPayoffMemory = false, float windowDuration = 4f, bool oncePerVictim = false,
            PairComboHitKind hitKind = PairComboHitKind.Any)
        {
            string prefix = "h." + hero.ToLowerInvariant();
            return new PairComboDef
            {
                Id = prefix + ".pair." + bridge, HeroKey = "Hero_" + hero, BridgeIndex = bridge,
                BridgeId = prefix + ".ring." + BridgeSlugs[bridge - 1], RouteA = memoryA, RouteB = memoryB,
                StarA = prefix + ".route." + slugA + ".4", StarB = prefix + ".route." + slugB + ".4",
                Name = new Txt(ja, en), TriggerMemory = origin, Trigger = trigger, Step = step,
                PayoffMemory = payoff, PayoffTrigger = payoffTrigger, Effect = effect, Value = one,
                Arg = arg, Cooldown = cooldown, RechargeMemory = recharge,
                OncePerActivation = oncePerActivation, KillByPayoffMemory = killByPayoffMemory,
                WindowDuration = windowDuration, OncePerVictim = oncePerVictim,
                PayoffHitKind = hitKind,
                TableRankValues = Array.AsReadOnly(new[] { one, two, three })
            };
        }

        private static IReadOnlyList<PairComboDef> Create()
        {
            // Revised v1.30 table: 62 pairs, 28 activation limits, six receiving-memory-only kills.
            // Only the two continuous Nachia attacks retain time intervals. Marks last four seconds;
            // only Cataclysm and Serpentine Blessing open twelve-second windows.
            // Activation identities and explosion phases are a runtime contract, not inferred host integration.
            return Array.AsReadOnly(new PairComboDef[]
            {
                D("Vesper", 1, "resolve", "St_D_Resolve", "cruel-sun", "St_Q_CruelSun", "燃え移る会心", "Kindling Crit", "St_Q_CruelSun", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_Resolve", PairComboTrigger.OnCrit, GimmickEffect.Element, 100, 150, 200, 0, 0f, null),
                D("Vesper", 2, "cruel-sun", "St_Q_CruelSun", "sanctuary", "St_R_SanctuaryOfEl", "陽光の継ぎ足し", "Sunlight Carry-Over", "St_R_SanctuaryOfEl", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_CruelSun", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 15, 20, 0, 0f, "St_R_SanctuaryOfEl", oncePerActivation: true),
                D("Vesper", 3, "sanctuary", "St_R_SanctuaryOfEl", "charge", "St_M_Charge", "聖域への突撃", "Charge into Sanctuary", "St_R_SanctuaryOfEl", PairComboTrigger.OnHit, PairComboStep.Mark, "St_M_Charge", PairComboTrigger.OnHit, GimmickEffect.Heal, 3, 5, 7, 1, 0f, null),
                D("Vesper", 4, "charge", "St_M_Charge", "mercy", "St_D_MercyOfEl", "光明の再突進", "Rekindled Charge", "St_M_Charge", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_MercyOfEl", PairComboTrigger.OnCrit, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_Charge"),
                D("Vesper", 5, "mercy", "St_D_MercyOfEl", "discipline", "St_Q_Discipline", "慈悲の二重光", "Twofold Mercy", "St_Q_Discipline", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_MercyOfEl", PairComboTrigger.OnCrit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null),
                D("Vesper", 6, "discipline", "St_Q_Discipline", "baptism", "St_R_BaptismOfSun", "焚き付けの洗礼", "Kindled Baptism", "St_Q_Discipline", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_BaptismOfSun", PairComboTrigger.OnHit, GimmickEffect.Burst, 30, 50, 70, 0, 0f, null, oncePerActivation: true, hitKind: PairComboHitKind.InitialExplosion),
                D("Vesper", 7, "baptism", "St_R_BaptismOfSun", "resolve", "St_D_Resolve", "号令の会心", "Command Crit", "St_D_Resolve", PairComboTrigger.OnCrit, PairComboStep.Mark, "St_R_BaptismOfSun", PairComboTrigger.OnHit, GimmickEffect.RechargeOther, 10, 13, 15, 0, 0f, null, oncePerActivation: true, hitKind: PairComboHitKind.InitialExplosion),
                D("Lacerta", 1, "powder", "St_D_SalamanderPowder", "hand-cannon", "St_Q_HandCannon", "火薬の二度鳴り", "Powder Double-Report", "St_Q_HandCannon", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_SalamanderPowder", PairComboTrigger.OnCrit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null),
                D("Lacerta", 2, "hand-cannon", "St_Q_HandCannon", "quick-trigger", "St_R_QuickTrigger", "三連のあとの一発", "One After Three", "St_R_QuickTrigger", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_HandCannon", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_Q_HandCannon"),
                D("Lacerta", 3, "quick-trigger", "St_R_QuickTrigger", "nimble-dodge", "St_M_NimbleDodge", "連射の足場", "Barrage Footing", "St_R_QuickTrigger", PairComboTrigger.OnKill, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_NimbleDodge"),
                D("Lacerta", 4, "nimble-dodge", "St_M_NimbleDodge", "double-tap", "St_D_DoubleTap", "二連の足跡", "Double-Tap Trail", "St_D_DoubleTap", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_DoubleTap", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_NimbleDodge", killByPayoffMemory: true),
                D("Lacerta", 5, "double-tap", "St_D_DoubleTap", "incendiary", "St_Q_IncendiaryRounds", "火種の二射", "Twin Embers", "St_Q_IncendiaryRounds", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_DoubleTap", PairComboTrigger.OnHit, GimmickEffect.Burst, 30, 50, 70, 0, 0f, null, oncePerActivation: true),
                D("Lacerta", 6, "incendiary", "St_Q_IncendiaryRounds", "precision", "St_R_PrecisionShot", "狙撃の弾込め", "Sniper's Reload", "St_Q_IncendiaryRounds", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_PrecisionShot", PairComboTrigger.OnKill, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_Q_IncendiaryRounds"),
                D("Lacerta", 7, "precision", "St_R_PrecisionShot", "powder", "St_D_SalamanderPowder", "火薬の狙撃支援", "Powder Sniper Support", "St_D_SalamanderPowder", PairComboTrigger.OnCrit, PairComboStep.Mark, "St_R_PrecisionShot", PairComboTrigger.OnHit, GimmickEffect.RechargeOther, 10, 13, 15, 0, 0f, null, oncePerActivation: true),
                D("Cetus", 1, "icy-veins", "St_D_IcyVeins", "embrace-chill", "St_Q_EmbracingTheChill", "凍土の呼び戻し", "Frozen Ground Recall", "St_Q_EmbracingTheChill", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_IcyVeins", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_Q_EmbracingTheChill", oncePerActivation: true),
                D("Cetus", 2, "embrace-chill", "St_Q_EmbracingTheChill", "back-off", "St_R_BackOff", "押し返しの冷気", "Chill of the Push-Back", "St_R_BackOff", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_EmbracingTheChill", PairComboTrigger.OnHit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null, oncePerActivation: true, hitKind: PairComboHitKind.TerminalExplosion),
                D("Cetus", 3, "back-off", "St_R_BackOff", "frost-charge", "St_M_FrostyCharge", "氷走りの号令", "Ice-Run Command", "St_M_FrostyCharge", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_BackOff", PairComboTrigger.OnHit, GimmickEffect.RechargeOther, 10, 13, 15, 0, 0f, null, oncePerActivation: true),
                D("Cetus", 4, "frost-charge", "St_M_FrostyCharge", "charged", "St_D_ChargedAnguillian", "雷鳴の道", "Thunder Trail", "St_M_FrostyCharge", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_ChargedAnguillian", PairComboTrigger.OnHit, GimmickEffect.Burst, 40, 70, 100, 0, 0f, null, oncePerActivation: true),
                D("Cetus", 5, "charged", "St_D_ChargedAnguillian", "boreal-chunk", "St_Q_BigBorealChunk", "氷塊に呼ぶ雷", "Thunder Called to Ice", "St_Q_BigBorealChunk", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_ChargedAnguillian", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 15, 20, 0, 0f, "St_Q_BigBorealChunk", oncePerActivation: true),
                D("Cetus", 6, "boreal-chunk", "St_Q_BigBorealChunk", "frozen-fists", "St_R_FrozenFists", "氷塊と拳", "Glacier and Fists", "St_Q_BigBorealChunk", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_FrozenFists", PairComboTrigger.OnKill, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_R_FrozenFists"),
                D("Cetus", 7, "frozen-fists", "St_R_FrozenFists", "icy-veins", "St_D_IcyVeins", "氷拳の癒やし", "Icy Fist Mending", "St_D_IcyVeins", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_FrozenFists", PairComboTrigger.OnKill, GimmickEffect.Heal, 3, 5, 7, 1, 0f, null),
                D("Yubar", 1, "exotic-matter", "St_D_ExoticMatter", "ethereal", "St_Q_EtherealInfluence", "物質の呼び水", "Matter's Lure", "St_Q_EtherealInfluence", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_ExoticMatter", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_Q_EtherealInfluence", oncePerActivation: true),
                D("Yubar", 2, "ethereal", "St_Q_EtherealInfluence", "cataclysm", "St_R_Cataclysm", "天災の余韻", "Calamity Afterglow", "St_R_Cataclysm", PairComboTrigger.OnUse, PairComboStep.Window, "St_Q_EtherealInfluence", PairComboTrigger.OnHit, GimmickEffect.Burst, 40, 70, 100, 0, 0f, null, oncePerActivation: true, windowDuration: 12f),
                D("Yubar", 3, "cataclysm", "St_R_Cataclysm", "flicker", "St_M_Flicker", "隕石の瞬き", "Meteor Blink", "St_R_Cataclysm", PairComboTrigger.OnKill, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_Flicker"),
                D("Yubar", 4, "flicker", "St_M_Flicker", "converging-stars", "St_D_ConvergencePoint", "星屑の瞬き", "Stardust Blink", "St_D_ConvergencePoint", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_ConvergencePoint", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_Flicker", killByPayoffMemory: true),
                D("Yubar", 5, "converging-stars", "St_D_ConvergencePoint", "supernova", "St_Q_SuperNova", "星の目印の再装填", "Marked-Star Reload", "St_Q_SuperNova", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_ConvergencePoint", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_Q_SuperNova", oncePerActivation: true),
                D("Yubar", 6, "supernova", "St_Q_SuperNova", "tranquility", "St_R_Tranquility", "凪の光", "Calm Light", "St_R_Tranquility", PairComboTrigger.OnUse, PairComboStep.Window, "St_Q_SuperNova", PairComboTrigger.OnHit, GimmickEffect.Element, 100, 150, 200, 2, 0f, null, oncePerActivation: true),
                D("Yubar", 7, "tranquility", "St_R_Tranquility", "exotic-matter", "St_D_ExoticMatter", "静かな爆ぜ", "Quiet Blast", "St_R_Tranquility", PairComboTrigger.OnUse, PairComboStep.Window, "St_D_ExoticMatter", PairComboTrigger.OnHit, GimmickEffect.Burst, 30, 50, 70, 0, 0f, null, oncePerActivation: true),
                D("Husk", 1, "killing-flow", "St_D_TheKillingFlow", "laceration", "St_Q_Laceration", "裂傷の確定会心", "Laceration Sure Crit", "St_Q_Laceration", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_TheKillingFlow", PairComboTrigger.OnCrit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null),
                D("Husk", 2, "laceration", "St_Q_Laceration", "annihilation", "St_R_AnnihilationStance", "剣気の追い裂き", "Aura Follow-Slash", "St_R_AnnihilationStance", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_Laceration", PairComboTrigger.OnHit, GimmickEffect.Burst, 40, 70, 100, 0, 0f, null, oncePerActivation: true),
                // v2.4: the Husk ring runs L-A-W-S-M so the three trio memories sit side by side (docs/specs/v2.4-husk-trio-starmap.md).
                // Bridge i joins BranchOrder[i-1] and BranchOrder[i], so 3 is now Annihilation-Wind, 4 Wind-FlashStep, 5 FlashStep-DeathMark.
                D("Husk", 3, "annihilation", "St_R_AnnihilationStance", "wind-scar", "St_D_ScarOfTheWind", "風に導かれる剣気", "Wind-Guided Aura", "St_D_ScarOfTheWind", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_AnnihilationStance", PairComboTrigger.OnHit, GimmickEffect.Element, 100, 150, 200, 3, 0f, null, oncePerActivation: true),
                D("Husk", 4, "wind-scar", "St_D_ScarOfTheWind", "flash-step", "St_M_FlashStep", "風傷の足跡", "Windscar Footprints", "St_D_ScarOfTheWind", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_ScarOfTheWind", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_FlashStep", killByPayoffMemory: true),
                D("Husk", 5, "flash-step", "St_M_FlashStep", "death-mark", "St_Q_DeathMark", "楔の足跡", "Wedge Footprints", "St_Q_DeathMark", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_DeathMark", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_FlashStep", killByPayoffMemory: true),
                D("Husk", 6, "death-mark", "St_Q_DeathMark", "deception", "St_R_Deception", "刻印の解除", "Mark Release", "St_Q_DeathMark", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_Deception", PairComboTrigger.OnHit, GimmickEffect.Shield, 3, 5, 7, 0, 0f, null),
                D("Husk", 7, "deception", "St_R_Deception", "killing-flow", "St_D_TheKillingFlow", "暗殺の仕上げ", "Assassin's Finish", "St_D_TheKillingFlow", PairComboTrigger.OnCrit, PairComboStep.Mark, "St_R_Deception", PairComboTrigger.OnKill, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_R_Deception"),
                D("Mist", 1, "en-garde", "St_D_AstridsMasterpieceEnGarde", "lunge", "St_Q_Lunge", "初撃の見切り", "First-Strike Insight", "St_Q_Lunge", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_AstridsMasterpieceEnGarde", PairComboTrigger.OnHit, GimmickEffect.Heal, 3, 5, 7, 0, 0f, null, oncePerActivation: true, oncePerVictim: true),
                D("Mist", 2, "lunge", "St_Q_Lunge", "determination", "St_R_UnbreakableDetermination", "覚醒の光突き", "Awakened Light Thrust", "St_R_UnbreakableDetermination", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_Lunge", PairComboTrigger.OnHit, GimmickEffect.Element, 100, 150, 200, 2, 0f, null, oncePerActivation: true),
                D("Mist", 3, "determination", "St_R_UnbreakableDetermination", "fast-feet", "St_M_FastFeet", "覚醒の足取り", "Awakened Footwork", "St_R_UnbreakableDetermination", PairComboTrigger.OnKill, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_FastFeet"),
                D("Mist", 4, "fast-feet", "St_M_FastFeet", "priorite", "St_D_AstridsMasterpiecePriorite", "追い詰めの足", "Cornering Steps", "St_D_AstridsMasterpiecePriorite", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_AstridsMasterpiecePriorite", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_FastFeet", killByPayoffMemory: true),
                D("Mist", 5, "priorite", "St_D_AstridsMasterpiecePriorite", "fleche", "St_Q_Fleche", "追い立ての突き", "Driving Thrust", "St_D_AstridsMasterpiecePriorite", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_Fleche", PairComboTrigger.OnHit, GimmickEffect.Burst, 30, 50, 70, 0, 0f, null, oncePerActivation: true),
                D("Mist", 6, "fleche", "St_Q_Fleche", "parry", "St_R_Parry", "突きと返しの二連", "Thrust and Riposte", "St_Q_Fleche", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_Parry", PairComboTrigger.OnHit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null),
                D("Mist", 7, "parry", "St_R_Parry", "en-garde", "St_D_AstridsMasterpieceEnGarde", "返しの構え", "Riposte Stance", "St_D_AstridsMasterpieceEnGarde", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_Parry", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_R_Parry", oncePerActivation: true),
                D("Nachia", 1, "pack-heart", "St_D_HeartOfThePack", "sylvan-call", "St_Q_SylvanCall", "群れの呼び声", "Pack's Call", "St_Q_SylvanCall", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_HeartOfThePack", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_Q_SylvanCall", oncePerActivation: true),
                D("Nachia", 2, "sylvan-call", "St_Q_SylvanCall", "natures-whisper", "St_R_NaturesWhisper", "号令の追い咬み", "Command Bite", "St_R_NaturesWhisper", PairComboTrigger.OnUse, PairComboStep.Window, "St_Q_SylvanCall", PairComboTrigger.OnHit, GimmickEffect.Burst, 30, 50, 70, 0, 1.5f, null),
                D("Nachia", 3, "natures-whisper", "St_R_NaturesWhisper", "dreamy-waltz", "St_M_DreamyWaltz", "号令と舞い", "Command and Dance", "St_R_NaturesWhisper", PairComboTrigger.OnUse, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_DreamyWaltz"),
                D("Nachia", 4, "dreamy-waltz", "St_M_DreamyWaltz", "circle-life", "St_D_CircleOfLife", "輪舞の呼吸", "Round Dance Breath", "St_D_CircleOfLife", PairComboTrigger.OnBasicAttack, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 2, 3, 5, 0, 0f, "St_M_DreamyWaltz"),
                D("Nachia", 5, "circle-life", "St_D_CircleOfLife", "moonlight-pact", "St_Q_MoonlightPact", "狼の見守り", "Wolf's Watch", "St_Q_MoonlightPact", PairComboTrigger.OnKill, PairComboStep.Window, "St_D_CircleOfLife", PairComboTrigger.OnBasicAttack, GimmickEffect.Heal, 1, 2, 3, 1, 1f, null),
                D("Nachia", 6, "moonlight-pact", "St_Q_MoonlightPact", "serpent-blessing", "St_R_SerpentineBlessing", "祝福の爪", "Claw of Blessing", "St_R_SerpentineBlessing", PairComboTrigger.OnUse, PairComboStep.Window, "St_Q_MoonlightPact", PairComboTrigger.OnHit, GimmickEffect.Echo, 10, 15, 20, 0, 0f, null, oncePerActivation: true, windowDuration: 12f),
                D("Nachia", 7, "serpent-blessing", "St_R_SerpentineBlessing", "pack-heart", "St_D_HeartOfThePack", "蛇鱗の団結", "Serpent-Scale Unity", "St_R_SerpentineBlessing", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_HeartOfThePack", PairComboTrigger.OnHit, GimmickEffect.Shield, 3, 5, 7, 0, 0f, null),
                D("Aurena", 1, "claw", "St_D_DisintegratingClaw", "golden-burst", "St_Q_GoldenBurst", "黄金の爪痕", "Golden Claw Mark", "St_Q_GoldenBurst", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_DisintegratingClaw", PairComboTrigger.OnHit, GimmickEffect.Burst, 40, 70, 100, 0, 0f, null, oncePerActivation: true),
                D("Aurena", 2, "golden-burst", "St_Q_GoldenBurst", "dangerous-theory", "St_R_DangerousTheory", "理論の黄金光", "Golden Light of Theory", "St_R_DangerousTheory", PairComboTrigger.OnCrit, PairComboStep.Mark, "St_Q_GoldenBurst", PairComboTrigger.OnHit, GimmickEffect.Element, 100, 150, 200, 2, 0f, null, oncePerActivation: true),
                D("Aurena", 3, "dangerous-theory", "St_R_DangerousTheory", "feathery-dash", "St_M_FeatheryDash", "理論の羽ばたき", "Wings of Theory", "St_R_DangerousTheory", PairComboTrigger.OnKill, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_FeatheryDash"),
                D("Aurena", 4, "feathery-dash", "St_M_FeatheryDash", "beautiful-threat", "St_D_BeautifulThreat", "羽根の舞い戻り", "Feather Return", "St_D_BeautifulThreat", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_BeautifulThreat", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_FeatheryDash", killByPayoffMemory: true),
                D("Aurena", 5, "beautiful-threat", "St_D_BeautifulThreat", "reduction", "St_Q_Reduction", "金片の羽根", "Feather of Shards", "St_Q_Reduction", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_BeautifulThreat", PairComboTrigger.OnHit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null),
                D("Aurena", 6, "reduction", "St_Q_Reduction", "chain-reaction", "St_R_ChainReaction", "陣上の金片", "Shards on the Circle", "St_R_ChainReaction", PairComboTrigger.OnHit, PairComboStep.Mark, "St_Q_Reduction", PairComboTrigger.OnHit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null, oncePerActivation: true),
                D("Aurena", 7, "chain-reaction", "St_R_ChainReaction", "claw", "St_D_DisintegratingClaw", "陣上の収穫", "Harvest on the Circle", "St_D_DisintegratingClaw", PairComboTrigger.OnHit, PairComboStep.Mark, "St_R_ChainReaction", PairComboTrigger.OnKill, GimmickEffect.Shield, 3, 5, 7, 0, 0f, null, oncePerActivation: true, oncePerVictim: true),
                D("Bismuth", 1, "prismatic-eyes", "St_D_PrismaticEyes", "innocence", "St_QR_Innocence", "光の頁の追撃", "Light-Page Follow-Up", "St_QR_Innocence", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_PrismaticEyes", PairComboTrigger.OnHit, GimmickEffect.Echo, 15, 25, 35, 0, 0f, null),
                D("Bismuth", 2, "innocence", "St_QR_Innocence", "distorting-sprint", "St_M_Sprint", "魂の足跡", "Soul Footprints", "St_QR_Innocence", PairComboTrigger.OnKill, PairComboStep.None, null, PairComboTrigger.None, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_Sprint"),
                D("Bismuth", 3, "distorting-sprint", "St_M_Sprint", "infernal-tales", "St_QR_InfernalTales", "炎の駆け足", "Flame Sprint", "St_QR_InfernalTales", PairComboTrigger.OnHit, PairComboStep.Mark, "St_QR_InfernalTales", PairComboTrigger.OnKill, GimmickEffect.Recharge, 30, 40, 50, 0, 0f, "St_M_Sprint", killByPayoffMemory: true),
                D("Bismuth", 4, "infernal-tales", "St_QR_InfernalTales", "valiant-heart", "St_QR_ValiantHeart", "炎剣の呼び戻し", "Flame-Blade Recall", "St_QR_InfernalTales", PairComboTrigger.OnHit, PairComboStep.Mark, "St_QR_ValiantHeart", PairComboTrigger.OnHit, GimmickEffect.Recharge, 10, 20, 30, 0, 0f, "St_QR_InfernalTales", oncePerActivation: true),
                D("Bismuth", 5, "valiant-heart", "St_QR_ValiantHeart", "distorted-mind", "St_QR_DistortedMind", "矢の癒やし", "Arrow Mending", "St_QR_ValiantHeart", PairComboTrigger.OnHit, PairComboStep.Mark, "St_QR_DistortedMind", PairComboTrigger.OnHit, GimmickEffect.Heal, 2, 3, 4, 0, 0f, null, oncePerActivation: true),
                D("Bismuth", 6, "distorted-mind", "St_QR_DistortedMind", "prismatic-eyes", "St_D_PrismaticEyes", "本が仕上げる矢", "Book Finishes the Arrows", "St_QR_DistortedMind", PairComboTrigger.OnHit, PairComboStep.Mark, "St_D_PrismaticEyes", PairComboTrigger.OnKill, GimmickEffect.Shield, 3, 5, 7, 0, 0f, null),
            });
        }
    }
}

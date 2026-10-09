using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static partial class StarMapPresentation
    {
        // 画面に内部IDを出さない：星は名前、橋の合わせ技は名前で示す。
        private static string StarName(string id)
        {
            string[] parts = id?.Split('.');
            string slug = parts != null && parts.Length > 1 ? (parts[0] == "h" ? parts[1] : parts[0]) : null;
            if (!string.IsNullOrEmpty(slug))
            {
                string hero = "Hero_" + char.ToUpperInvariant(slug[0]) + slug.Substring(1);
                foreach (var talent in HeroSigils.TreeFor(hero))
                    if (talent.Id == id) return Loc.T("星『" + talent.Name.Ja + "』", "star “" + talent.Name.En + "”");
            }
            return Loc.T("（名前のない星）", "(unnamed star)");
        }

        private static string PairName(string pairId)
        {
            var pair = PairCombos.Get(pairId);
            return pair?.Name != null ? Loc.T("「" + pair.Name.Ja + "」", "\"" + pair.Name.En + "\"") : Loc.T("この橋の合わせ技", "this bridge combo");
        }

        private static string Number(decimal value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
        private static string Number(float value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
        private static string Percent(decimal units) => Number(units / 100m) + "%";

        private static string EventText(MemoryEventKind trigger)
        {
            switch (trigger)
            {
                case MemoryEventKind.ConfirmedUse: return Loc.T("を使用したとき", " is used");
                case MemoryEventKind.Hit: return Loc.T("が敵に命中したとき", " hits an enemy");
                case MemoryEventKind.CriticalHit: return Loc.T("が敵に会心で命中したとき", " critically hits an enemy");
                case MemoryEventKind.Kill: return Loc.T("で敵を倒したとき", " kills an enemy");
                case MemoryEventKind.OwnedBasicAttackFired: return Loc.T("による自分の通常攻撃を放ったとき", " fires your basic attack");
                case MemoryEventKind.OwnedBasicAttackHit: return Loc.T("による自分の通常攻撃が命中したとき", " hits with your basic attack");
                default: throw new InvalidOperationException("Unknown mechanism trigger: " + trigger);
            }
        }

        private static string BudgetText(AttributionBudget budget)
        {
            switch (budget)
            {
                case AttributionBudget.PerActivation: return Loc.T("1回の使用につき1回まで。", "At most once per use.");
                case AttributionBudget.PerActivationVictim: return Loc.T("1回の使用につき敵1体ごとに1回まで。", "At most once per enemy per use.");
                case AttributionBudget.PerKill: return Loc.T("倒した敵1体につき1回まで。", "At most once per enemy killed.");
                case AttributionBudget.PerOwnedBasicAttack: return Loc.T("自分の通常攻撃1回につき1回まで。", "At most once per basic attack you make.");
                default: throw new InvalidOperationException("Unknown mechanism budget: " + budget);
            }
        }

        private static string PoolText(ModShieldPoolKind pool)
        {
            switch (pool)
            {
                case ModShieldPoolKind.Ordinary: return Loc.T("付与者と受け手の組ごとに、通常の星の障壁と1つを共有する（重ならず、残量の大きい方を保って時間を延長）。", "Shares one shield with ordinary star shields for each caster and recipient pair (does not stack; keeps the larger remaining amount and refreshes the duration).");
                case ModShieldPoolKind.Rampart: return Loc.T("通常の星の障壁とは別に保持。", "Separate from ordinary star shields.");
                case ModShieldPoolKind.Allied: return Loc.T("同じ付与者からの障壁は重ならない。", "Shields from the same caster do not stack.");
                default: throw new InvalidOperationException("Unknown shield pool: " + pool);
            }
        }

        private static string EffectText(GimmickEffect effect)
        {
            switch (effect)
            {
                case GimmickEffect.Element: return Loc.T("火・冷気・光・闇の付与", "Fire, Cold, Light or Dark application");
                case GimmickEffect.Burst: return Loc.T("範囲追加ダメージ", "area extra damage");
                case GimmickEffect.Shield: return Loc.T("自分の障壁量", "self shield amount");
                case GimmickEffect.Heal: return Loc.T("HP回復量", "HP restored");
                case GimmickEffect.Recharge: return Loc.T("残りクールダウン短縮量", "remaining cooldown reduction");
                case GimmickEffect.Quicken: return Loc.T("攻撃速度増加量", "attack speed bonus");
                case GimmickEffect.Empower: return Loc.T("攻撃力・魔力増加量", "attack damage and ability power bonus");
                case GimmickEffect.Expose: return Loc.T("敵が自分から受けるダメージ増加量", "enemy damage taken from you");
                case GimmickEffect.Echo: return Loc.T("0.3秒後の追撃ダメージ", "follow-up damage after 0.3s");
                case GimmickEffect.Reload: return Loc.T("使用回数回復", "charge restoration");
                case GimmickEffect.RechargeOther: return Loc.T("ほかの通常記憶の残りクールダウン短縮量", "other normal memory remaining cooldown reduction");
                case GimmickEffect.Wound: return Loc.T("継続ダメージ", "damage over time");
                case GimmickEffect.Daze: return Loc.T("敵のスタン時間", "enemy stun duration");
                case GimmickEffect.Ricochet: return Loc.T("近くの別の敵への追加ダメージ", "extra damage to other nearby enemies");
                case GimmickEffect.Siphon: return Loc.T("直接ダメージによるHP回復量", "HP restored from direct damage");
                case GimmickEffect.Rampart: return Loc.T("命中した敵の数に応じた障壁量", "shield amount per enemy hit");
                case GimmickEffect.Primed: return Loc.T("次の通常攻撃の追加ダメージ", "next basic attack extra damage");
                case GimmickEffect.Crescendo: return Loc.T("同じ記憶の累積ダメージ増加量", "stacking damage bonus for the same memory");
                case GimmickEffect.ElementEdge: return Loc.T("敵の属性の種類数に応じた追加ダメージ", "extra damage per element type on the enemy");
                case GimmickEffect.PackMend: return Loc.T("自分の召喚獣のHP回復量", "HP restored to your summons");
                case GimmickEffect.Sap: return Loc.T("敵の与ダメージ減少量", "enemy damage dealt reduction");
                case GimmickEffect.Weakspot: return Loc.T("同じ敵への会心率増加量", "critical chance bonus against the same enemy");
                default: throw new InvalidOperationException("Unknown effect: " + effect);
            }
        }

        private static string DescribeMemoryTuning(MemoryTuningDefinition tuning)
        {
            string equipped = Loc.T("" + Links.ItemName(tuning.Memory) + "を装備中、", " While " + Links.ItemName(tuning.Memory) + " is equipped, ");
            switch (tuning.Kind)
            {
                case MemoryTuningKind.KillingFlowKeepSpeed:
                    return Loc.T("攻撃速度を維持（追加分の" + Percent(tuning.ValueUnits) + "）。",
                        "Retain " + Percent(tuning.ValueUnits) + " of bonus attack speed.")
                        + equipped + Loc.T("追加攻撃速度のこの割合は攻撃力へ変換せず、攻撃速度として残す（変換で得る攻撃力はその分減る）。",
                            "this share is not converted to attack damage (the attack damage gained from conversion decreases accordingly).");
                case MemoryTuningKind.KillingFlowOnHitHealScale:
                    return Loc.T("命中時のHP回復量 ×最大" + Number(tuning.ValueUnits / 10000m) + "。",
                        "On-hit HP restored × up to " + Number(tuning.ValueUnits / 10000m) + ".")
                        + equipped + Loc.T("この記憶の回復量に、変換前の攻撃速度倍率÷変換後の攻撃速度倍率を掛ける（最低1倍）。",
                            "multiply this memory's healing by attack speed multiplier before conversion ÷ multiplier after conversion (at least ×1).");
                case MemoryTuningKind.StanceSwordQiAttackBasis:
                    return Loc.T("剣気のダメージ基準：魔力 → 攻撃力。", "Sword-qi damage basis: ability power → attack damage.")
                        + equipped + Loc.T("剣気は係数を変えず攻撃力を基準にし、物理ダメージになる。態勢の攻撃力・攻撃速度強化は変わらない。",
                            "sword qi keeps its coefficient but scales with attack damage and deals physical damage. The stance's attack damage and attack speed bonuses are unchanged.");
                default: throw new InvalidOperationException("Unknown tuning: " + tuning.Kind);
            }
        }
        private static string DescribeIdentityStrike(IdentityStrikeDefinition strike)
        {
            string memory = Links.ItemName(strike.Identity);
            if (!strike.DealsDamage)
                return Loc.T("追加ダメージ量は変化なし。", "Extra damage amount unchanged.")
                    + Loc.T("" + memory + "を装備中、突進攻撃の元々の追加ダメージ部分だけを、この記憶のダメージとして扱う（通常攻撃としては扱わない）。",
                        " While " + memory + " is equipped, only the dash attack's existing bonus damage counts as this memory's damage, rather than basic attack damage.");
            string element = strike.Element == IdentityStrikeElement.None ? Loc.T("無属性", "non-elemental") : ElementName(strike.Element);
            string basis = strike.Basis == IdentityStrikeBasis.AttackDamage ? Loc.T("攻撃力", "attack damage") : Loc.T("攻撃力・魔力の高い方", "the higher of attack damage and ability power");
            string amount = Percent(strike.AdUnits)
                + (strike.BonusSpeedUnitsPerPercent > 0 ? Loc.T("+変換済みの追加攻撃速度1%につき", " + per 1% converted bonus attack speed ") + Percent(strike.BonusSpeedUnitsPerPercent) : "");
            string shape = strike.Shape == IdentityStrikeShape.ForwardLine
                ? Loc.T("前方" + Number(strike.RangeMetres) + "m・幅" + Number(strike.WidthOrArc) + "mの直線内",
                    "a forward line " + Number(strike.RangeMetres) + "m long and " + Number(strike.WidthOrArc) + "m wide")
                : Loc.T("前方" + Number(strike.RangeMetres) + "m・角度" + Number(strike.WidthOrArc) + "度の扇形内",
                    "a forward arc of " + Number(strike.RangeMetres) + "m and " + Number(strike.WidthOrArc) + "°");
            if (strike.IsCriticalMechanism)
            {
                string identity = memory;
                string timing = strike.Trigger == IdentityStrikeTrigger.AfterDisplacementCritical
                    ? Loc.T("ダッシュ・瞬間移動後" + Number(strike.WindowSeconds) + "秒以内の最初の通常攻撃が会心すると（非会心でも準備を消費）、",
                        "If the first basic attack hit within " + Number(strike.WindowSeconds) + "s after a dash or teleport is critical (a noncritical hit also consumes readiness), ")
                    : Loc.T("同じ敵に通常攻撃の会心を3回連続で命中させると（各命中間隔" + Number(strike.WindowSeconds) + "秒以内。非会心・別の敵への命中・時間切れで連続数をリセット）、",
                        "On three consecutive critical basic attack hits against the same enemy (each gap at most " + Number(strike.WindowSeconds) + "s; a noncritical hit, a different enemy or an expired window resets the sequence), ");
                string geometry = shape;
                string damage = Loc.T(identity + "の斬撃が対象と" + geometry + "の敵に" + basis + "の" + Percent(strike.AdUnits) + "に相当する闇ダメージを与える（計" + strike.MaxTargets + "体まで、発動間隔" + Number(IdentityStrikeDefinition.CriticalCooldownSeconds) + "秒）。",
                    identity + " deals dark damage equal to " + Percent(strike.AdUnits) + " of " + basis + " to the target and enemies in " + geometry + " (up to " + strike.MaxTargets + " enemies total; " + Number(IdentityStrikeDefinition.CriticalCooldownSeconds) + "s interval).");
                string partners = strike.Trigger == IdentityStrikeTrigger.AfterDisplacementCritical
                    ? Loc.T("斬撃時に装備中の回避記憶の残りクールダウンを" + Number(IdentityStrikeDefinition.CriticalMovementRefundPercent) + "%短縮する。記憶『瞬歩』のダッシュや記憶『死の刻印』『撹乱』の瞬間移動で準備でき、記憶『風の傷』の移動後の確定会心と連携する。",
                        " The strike reduces the equipped dodge memory's remaining cooldown by " + Number(IdentityStrikeDefinition.CriticalMovementRefundPercent) + "%. Dashes from Memory \"Flash Step\" and teleports from Memory \"Death Mark\" or Memory \"Deception\" prime it, pairing with Memory \"Scar of the Wind\"'s guaranteed post-movement critical hit.")
                    : Loc.T("記憶『瞬歩』のダッシュや記憶『死の刻印』の瞬間移動と、記憶『一歩一殺』の移動後の確定会心で連続会心をつなげられる。",
                        " Dashes from Memory \"Flash Step\" and teleports from Memory \"Death Mark\" pair with Memory \"One Step, One Kill\"'s guaranteed post-movement critical hit to maintain the sequence.");
                return timing + damage + partners + Loc.T(identity + "に装着したエッセンス『神聖なる信仰』のダメージ増幅と、命中後6秒以内の撃破による成長が働く（エッセンス未装着でも発動する）。",
                    " Essence \"Divine Faith\" socketed to " + identity + " amplifies this damage and gains stacks from kills within 6s of the hit (the strike also works without that Essence).");
            }
            string when = strike.Trigger == IdentityStrikeTrigger.AfterDisplacementNextBasicHit
                ? Loc.T("ダッシュ・瞬間移動後" + Number(strike.WindowSeconds) + "秒以内に次の自分の通常攻撃が命中すると（移動1回につき1回、重ならない）",
                    "when your next basic attack hits within " + Number(strike.WindowSeconds) + "s after a dash or teleport (once per displacement; does not stack)")
                : Loc.T("自分の通常攻撃が" + strike.EveryN + "回命中するごとに（同じ通常攻撃の複数命中は1回と数える）",
                    "every " + strike.EveryN + " basic attacks you make that hit (multiple hits from one attack count once)");
            return Loc.T("追加ダメージ +" + basis + "の" + amount + "。", "Extra damage +" + amount + " of " + basis + ".")
                + Loc.T("" + memory + "を装備中、" + when + "、" + shape + "の敵最大" + strike.MaxTargets + "体へ" + element + "の斬撃。この記憶のダメージとして扱う。時間による再発動制限なし。",
                    " While " + memory + " is equipped, " + when + ", strike up to " + strike.MaxTargets + " enemies in " + shape + " for " + element + " damage attributed to this memory. No time-based cooldown.");
        }
        private static string ElementName(IdentityStrikeElement element)
        {
            switch (element)
            {
                case IdentityStrikeElement.Fire: return Loc.T("火", "fire");
                case IdentityStrikeElement.Cold: return Loc.T("冷気", "cold");
                case IdentityStrikeElement.Light: return Loc.T("光", "light");
                case IdentityStrikeElement.Dark: return Loc.T("闇", "dark");
                default: throw new InvalidOperationException("Unknown element: " + element);
            }
        }

        internal static string DescribeMechanism(AuthoredMechanismSpec spec)
        {
            AuthoredMechanisms.Validate(spec);
            var lines = new List<string>();
            string source = spec.Source != null ? SelectorText(spec.Source)
                : spec.Recharge != null ? SelectorText(spec.Recharge.Source)
                : spec.Primed != null ? Links.ItemName(spec.Primed.SourceMemory)
                : spec.Relay != null ? Links.ItemName(RelayWindowDefinition.SourceMemory)
                : spec.Dividend != null ? Links.ItemName(spec.Dividend.SourceMemory)
                : Loc.T("装備中の移動以外の記憶", "equipped nonmovement memories");
            switch (spec.Kind)
            {
                case AuthoredMechanismKind.Gimmick:
                    string triggerText = Loc.T("発動条件：", "Triggered when ") + (spec.TriggerByIdentity.Count == 0
                        ? source + EventText(spec.Trigger)
                        : string.Join(Loc.T("、または", ", or "), spec.TriggerByIdentity.Select(t => Links.ItemName(t.Key) + EventText(t.Value))));
                    string description = Gimmicks.DescribeForSource(spec.Gimmick, source, triggerText: triggerText);
                    if (description.Length == 0) throw new InvalidOperationException("Unmapped effect source.");
                    lines.Add(description);
                    break;
                case AuthoredMechanismKind.DirectedRecharge:
                    var recharge = spec.Recharge;
                    lines.Add(Loc.T("残りクールダウン -", "Remaining cooldown -") + Percent(recharge.EffectiveValueUnits)
                        + Loc.T("。対象：", ". Recipient: ") + SelectorText(recharge.Recipient) + Loc.T("。", "."));
                    lines.Add(source + EventText(recharge.SourceTrigger) + Loc.T("、発動確率", "; chance ") + Percent(recharge.ProbabilityUnits)
                        + (recharge.EveryN > 1 ? Loc.T($"。条件を満たす{recharge.EveryN}回ごとに抽選。", $"; rolled every {recharge.EveryN} qualifying events.") : Loc.T("。", "."))
                        + Loc.T("残り時間を基準に短縮し、使用回数は直接回復しない。", " Reduces the remaining time, not charges directly."));
                    switch (recharge.Condition)
                    {
                        case RechargeConditionKind.Always: break;
                        case RechargeConditionKind.ChangedTarget: lines.Add(Loc.T("前回と異なる敵に命中した場合のみ（最初の命中では発動しない）。", "Requires a different target from the previous hit (the first hit does not trigger it).")); break;
                        case RechargeConditionKind.Shielded: lines.Add(Loc.T("障壁がある間のみ。", "Requires an active shield.")); break;
                        case RechargeConditionKind.ElementTypesAtLeast: lines.Add(Loc.T($"敵に{recharge.RequiredElementTypes}種類以上の属性がある場合のみ。", $"Requires at least {recharge.RequiredElementTypes} element types on the enemy.")); break;
                        default: throw new InvalidOperationException("Unknown recharge condition: " + recharge.Condition);
                    }
                    break;
                case AuthoredMechanismKind.MemoryPrimed:
                    lines.Add(Loc.T("次の通常攻撃の追加ダメージ +攻撃力・魔力の高い方の", "Next basic attack extra damage +")
                        + Percent(spec.Primed.ValueUnits) + Loc.T("。", " of the higher of attack damage and ability power."));
                    lines.Add(source + EventText(spec.Primed.Trigger) + Loc.T("、", ": ")
                        + Number(spec.Primed.DurationSeconds) + Loc.T("秒以内の次の自分の通常攻撃に上乗せ（重ならない）。次の通常攻撃への上乗せが複数あっても最大の1つだけを消費し、残りは保持。",
                            "s to use on your next basic attack (does not stack). Only the largest of all next-basic-attack bonuses is consumed; the others remain ready."));
                    break;
                case AuthoredMechanismKind.RelayWindow:
                    lines.Add(Loc.T("記憶の直接ダメージ +", "Memory direct damage +") + Percent(spec.Relay.ValueUnits)
                        + Loc.T("。対象：", ". Target: ") + Links.ItemName(spec.Relay.TargetMemory) + Loc.T("。", "."));
                    lines.Add(source + EventText(MemoryEventKind.ConfirmedUse) + Loc.T("から", ": ")
                        + Number(spec.Relay.DurationSeconds) + Loc.T("秒間、この記憶の元々のダメージを強化（星による追加ダメージは対象外）。両方の記憶を装備する必要がある。",
                            "s of increased damage from the memory itself, excluding star-generated extra damage. Both memories must be equipped."));
                    break;
                case AuthoredMechanismKind.AlliedWard:
                    lines.Add(DescribeWard(spec.Ward));
                    if (spec.TriggerByIdentity.Count == 0)
                        lines.Add(Loc.T("発動条件：", "Trigger: ") + source + EventText(spec.Trigger) + Loc.T("。", "."));
                    break;
                case AuthoredMechanismKind.PressureDividend:
                    lines.Add(Loc.T("未確保の欠片 +1個（確率", "Unsecured shards +1 (chance ") + Percent(spec.Dividend.ProbabilityUnits) + Loc.T("、上限40%）。", ", capped at 40%)."));
                    lines.Add(source + Loc.T("で、夢の圧により最大HPが25%以上増えた敵を倒したときに抽選。同じ敵からは1人につき最大1個。",
                        " must kill an enemy whose maximum HP was increased by at least 25% by dream pressure. You can receive at most 1 shard from the same enemy."));
                    if (spec.Dividend.RequiredMemories.Count > 0)
                        lines.Add(Loc.T("すべて装備が必要：", "All must be equipped: ") + string.Join(Loc.T("、", ", "), spec.Dividend.RequiredMemories.Select(Links.ItemName)));
                    break;
                case AuthoredMechanismKind.MemoryTuning: lines.Add(DescribeMemoryTuning(spec.Tuning)); break;
                case AuthoredMechanismKind.IdentityStrike: lines.Add(DescribeIdentityStrike(spec.IdentityStrike)); break;
                case AuthoredMechanismKind.SacrificeShield:
                    lines.Add(Loc.T($"障壁 +実際に支払ったHPの{Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_value)}%（{Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_duration)}秒間・1回の付与は最大HPの{Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_newAwardCap)}%まで）。",
                        $"Shield +{Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_value)}% of HP actually paid ({Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_duration)}s; each award capped at {Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_newAwardCap)}% maximum HP)."));
                    lines.Add(Loc.T(Links.ItemName("St_Q_GoldenBurst") + "または" + Links.ItemName("St_Q_Reduction") + "の元々のHP支払いが対象。通常の被ダメージは対象外。HP支払い自体は減らさない。間隔制限なし。",
                        "Applies only to the original HP payment of " + Links.ItemName("St_Q_GoldenBurst") + " or " + Links.ItemName("St_Q_Reduction") + ", not ordinary damage taken. Does not reduce the HP cost. No cooldown."));
                    lines.Add(PoolText(ModShieldPoolKind.Ordinary));
                    break;
                case AuthoredMechanismKind.StunSourceFilter:
                    lines.Add(Loc.T($"障壁 +最大HPの{Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_value)}%（{Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_duration)}秒間）。装備中のQまたはRの記憶が元々持つ効果で敵をスタンさせたとき、自分に付与。星による追加スタンは対象外。再発動まで{Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_cooldown)}秒。",
                        $"Shield +{Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_value)}% maximum HP for {Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_duration)}s. Gain it when the original effect of your equipped Q or R memory stuns an enemy, excluding star-generated stuns. Cooldown: {Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_cooldown)}s."));
                    lines.Add(PoolText(ModShieldPoolKind.Ordinary));
                    break;
                case AuthoredMechanismKind.BridgeSuccess:
                    var bridge = spec.Bridge;
                    lines.Add(DescribeBridgePayload(bridge.BasePayoff, SelectorText(bridge.PayoffSource)));
                    foreach (var payload in bridge.Extras) lines.Add(DescribeBridgePayload(payload, SelectorText(bridge.PayoffSource)));
                    foreach (var endpoint in bridge.Endpoints)
                        lines.Add(Loc.T("装備が必要：", "Required equipped: ") + Links.ItemName(endpoint.Memory)
                            + Loc.T("。必要な取得星：", ". Required star: ") + StarName(endpoint.StarId)
                            + Loc.T($"を{endpoint.MinimumRank}段以上。", $" at rank {endpoint.MinimumRank} or higher."));
                    string opening = SelectorText(bridge.OpeningSource) + EventText(bridge.OpeningTrigger);
                    string payoff = SelectorText(bridge.PayoffSource) + EventText(bridge.PayoffTrigger);
                    if (bridge.GateKind == BridgeGateKind.Mark)
                    {
                        string markedPayoff = SelectorText(bridge.PayoffSource) + EventText(bridge.PayoffTrigger)
                            .Replace(Loc.T("敵", "an enemy"), Loc.T("印のある同じ敵", "the same marked enemy"));
                        lines.Add(Loc.T("成立条件：" + opening + "、その敵に" + Number(bridge.MarkSeconds) + "秒間の印を付ける。" + markedPayoff + "、追加効果が発動（印は消費しない）。",
                            "Combo condition: when " + opening + ", mark that enemy for " + Number(bridge.MarkSeconds) + "s. When " + markedPayoff + ", apply the extra effects (does not consume the mark)."));
                    }
                    else if (bridge.GateKind == BridgeGateKind.Window)
                        lines.Add(Loc.T("成立条件：" + opening + "から、" + (bridge.UsesNativeWindowLifetime ? "元々の効果の残り持続時間の" + Number(bridge.WindowLifetimeScale) + "倍" : Number(bridge.WindowSeconds) + "秒") + "以内に" + payoff + "、追加効果が発動。",
                            "Combo condition: when " + opening + ", then " + payoff + " within " + (bridge.UsesNativeWindowLifetime ? Number(bridge.WindowLifetimeScale) + "× the original effect's remaining duration" : Number(bridge.WindowSeconds) + "s") + ", apply the extra effects."));
                    else lines.Add(Loc.T("成立条件：", "Combo condition: ") + payoff + Loc.T("、受け手の記憶へ直接適用（印・先行使用は不要）。",
                        "; apply directly to the recipient memory (no mark or opening use required)."));
                    if (bridge.SourcePhase != BridgeSourcePhase.Any)
                        lines.Add(bridge.SourcePhase == BridgeSourcePhase.InitialExplosion
                            ? Loc.T("受け手の記憶の最初の爆発が命中したときだけ発動（途中・最後の爆発は対象外）。", "Only the payoff memory's initial explosion qualifies, not intermediate or ending hits.")
                            : Loc.T("受け手の記憶の最後の爆発が命中したときだけ発動（最初・途中の爆発は対象外）。", "Only the payoff memory's ending explosion qualifies, not initial or intermediate hits."));
                    lines.Add(bridge.CooldownSeconds > 0 ? Loc.T("再発動まで", "Cooldown: ") + Number(bridge.CooldownSeconds) + Loc.T("秒。", "s.") : Loc.T("間隔制限なし。", "No cooldown."));
                    break;
                default: throw new InvalidOperationException("Unknown authored mechanism: " + spec.Kind);
            }
            bool eventDriven = spec.Kind != AuthoredMechanismKind.MemoryTuning && spec.Kind != AuthoredMechanismKind.IdentityStrike
                && spec.Kind != AuthoredMechanismKind.SacrificeShield && spec.Kind != AuthoredMechanismKind.StunSourceFilter;
            var budget = spec.Bridge?.Budget ?? spec.Recharge?.Budget ?? spec.Primed?.Budget ?? spec.Budget;
            if (eventDriven) lines.Add(BudgetText(budget));
            if (eventDriven && spec.Gimmick == null && spec.Bridge == null)
                lines.Add(Loc.T("時間による再発動制限なし。", "No time-based cooldown."));
            if (eventDriven && spec.EveryN > 1 && spec.Recharge == null)
                lines.Add(Loc.T($"条件を満たす発動{spec.EveryN}回ごとに適用。", $"Applies every {spec.EveryN} qualifying events."));
            if (spec.Once && budget != AttributionBudget.PerActivation)
                lines.Add(Loc.T("同じ使用からは1回だけ発動。", "Only once per memory use."));
            if (spec.ValuesByRank.Length > 0)
                lines.Add(Loc.T("1段目から順に：", "From rank 1 onward: ")
                    + string.Join(" / ", spec.ValuesByRank.Select(v => MechanismRankValue(spec, v))));
            foreach (var trigger in spec.TriggerByIdentity)
                lines.Add(Loc.T("発動条件：", "Trigger: ") + Links.ItemName(trigger.Key) + EventText(trigger.Value) + Loc.T("。", "."));
            string requiredNames = string.Join(Loc.T("、", ", "), spec.RequiredMemories
                .Where(id => spec.Bridge == null || !spec.Bridge.Endpoints.Any(endpoint => endpoint.Memory == id)).Select(Links.ItemName));
            if (requiredNames.Length > 0)
                lines.Add(Loc.T("すべて装備が必要：", "All must be equipped: ") + requiredNames);
            if (spec.Replaces.Length > 0) lines.Add(Loc.T("置き換える星：", "Replaces stars: ") + string.Join(Loc.T("、", ", "), spec.Replaces.Select(StarName)));
            switch (spec.Condition)
            {
                case AuthoredMechanismCondition.Always: break;
                case AuthoredMechanismCondition.BridgeSuccess: lines.Add(Loc.T("橋の成功時のみ：", "On bridge success only: ") + PairName(spec.PairId)); break;
                case AuthoredMechanismCondition.BridgeMark: lines.Add(Loc.T("橋の印がある敵にのみ：", "Only on bridge-marked enemies: ") + PairName(spec.PairId)); break;
                case AuthoredMechanismCondition.BridgeWindow: lines.Add(Loc.T("橋の受付時間内のみ：", "Only within the bridge window: ") + PairName(spec.PairId)); break;
                default: throw new InvalidOperationException("Unknown mechanism condition: " + spec.Condition);
            }
            return string.Join("\n", lines);
        }

        private static string DescribeBridgePayload(BridgePayload payload, string source)
        {
            switch (payload.Kind)
            {
                case BridgePayloadKind.Damage:
                    return Loc.T("追加ダメージ +", "Extra damage +") + Percent(payload.ValueUnits)
                        + Loc.T("（基準：", " of ") + (payload.DamageBasis == BridgeDamageBasis.NativeHit
                            ? Loc.T("発動した命中の元々のダメージ", "the payoff hit's original damage")
                            : Loc.T("攻撃力・魔力の高い方", "the higher of attack damage and ability power"))
                        + Loc.T("）。成立時に命中した敵が対象。", ". Targets the enemy hit when the combo succeeds.");
                case BridgePayloadKind.Recharge:
                    return Loc.T("残りクールダウン -", "Remaining cooldown -") + Percent(payload.ValueUnits)
                        + Loc.T("。対象：", ". Recipient: ") + SelectorText(payload.Recipient) + Loc.T("。", ".");
                case BridgePayloadKind.OrdinaryShield:
                    return Loc.T("自分の障壁 +最大HPの", "Self shield +") + Percent(payload.ValueUnits)
                        + Loc.T("（", " maximum HP (") + Number(payload.DurationSeconds) + Loc.T("秒間）。", "s). ") + PoolText(ModShieldPoolKind.Ordinary);
                case BridgePayloadKind.Gimmick:
                    return Gimmicks.DescribeForSource(payload.Gimmick, source,
                        triggerText: Loc.T("この合わせ技が成立したときに発動", "Triggered when this combo succeeds"));
                case BridgePayloadKind.AlliedWard:
                    return DescribeWard(payload.Ward);
                default: throw new InvalidOperationException("Unknown bridge payload: " + payload.Kind);
            }
        }

        private static string MechanismRankValue(AuthoredMechanismSpec spec, int units)
        {
            var gimmick = spec.Gimmick ?? spec.Bridge?.BasePayoff.Gimmick;
            if (gimmick?.Effect == GimmickEffect.Element)
            {
                decimal value = units / 100m;
                int stacks = (int)(value / 100m);
                decimal chance = value % 100m;
                return (stacks > 0 ? stacks + Loc.T("つ", " stacks") : "")
                    + (chance > 0 ? (stacks > 0 ? Loc.T("+追加1つの確率", " + chance of one more: ") : Loc.T("1つ付与の確率", "chance of one stack: ")) + Number(chance) + "%" : "");
            }
            if (gimmick?.Effect == GimmickEffect.Daze)
                return Number(units / 1000m) + Loc.T("秒のスタン", "s stun");
            return Percent(units);
        }

        private static string DescribeWard(AlliedWardDefinition ward)
        {
            string basis = ward.AmountBasis == WardAmountBasis.RecipientMaxHP
                ? Loc.T("受け手の最大HP", "the recipient's maximum HP")
                : Loc.T("付与者の攻撃力・魔力の高い方", "the caster's higher of attack damage and ability power");
            return Loc.T("障壁 +", "Shield +") + Percent(ward.ValueUnits) + Loc.T("（基準：" + basis + "）。", " of " + basis + ".")
                + Loc.T("対象：", " Targets: ") + (ward.RecipientKind == WardRecipientKind.AlliedTravelers
                    ? Loc.T("味方の旅人", "allied travelers") : Loc.T("自分の召喚獣", "your summons"))
                + (ward.IncludeOwner ? Loc.T("と自分", " and yourself") : Loc.T("（自分を除く）", " (excluding yourself)"))
                + Loc.T("。自分から", " within ") + Number(ward.RadiusMetres) + Loc.T("m以内・最大", "m of you, up to ") + ward.Targets
                + Loc.T("体へ", " targets, for ") + Number(ward.DurationSeconds) + Loc.T("秒間。", "s. ") + PoolText(ward.PoolKind);
        }
    }
}

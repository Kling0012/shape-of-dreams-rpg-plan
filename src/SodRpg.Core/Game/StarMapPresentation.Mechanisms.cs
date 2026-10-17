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
            return pair?.Name != null ? Loc.T("橋の合わせ技「" + pair.Name.Ja + "」", "\"" + pair.Name.En + "\"") : Loc.T("この橋の合わせ技", "this bridge combo");
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
                case ModShieldPoolKind.Ordinary: return Loc.T("付与者と受け手の組ごとに、通常の星の障壁と1つを共有する。重ならず、残量の大きい方を保ったまま時間が延びる。", "Shares one shield with ordinary star shields for each caster and recipient pair (does not stack; keeps the larger remaining amount and refreshes the duration).");
                case ModShieldPoolKind.Rampart: return Loc.T("通常の星の障壁とは別に保持される。", "Separate from ordinary star shields.");
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
            string memory = Links.ItemName(tuning.Memory);
            string equipped = " While " + memory + " is equipped, ";
            switch (tuning.Kind)
            {
                case MemoryTuningKind.KillingFlowKeepSpeed:
                    return Loc.T(memory + "を装備中、追加攻撃速度の" + Percent(tuning.ValueUnits) + "は攻撃力へ変換されず、攻撃速度として残る。その分、変換で得る攻撃力は減る。",
                        "Retain " + Percent(tuning.ValueUnits) + " of bonus attack speed." + equipped
                        + "this share is not converted to attack damage (the attack damage gained from conversion decreases accordingly).");
                case MemoryTuningKind.KillingFlowOnHitHealScale:
                    return Loc.T(memory + "を装備中、この記憶の命中時のHP回復量に、変換前の攻撃速度倍率÷変換後の攻撃速度倍率を掛ける。最低でも1倍で、最大" + Number(tuning.ValueUnits / 10000m) + "倍になる。",
                        "On-hit HP restored × up to " + Number(tuning.ValueUnits / 10000m) + "." + equipped
                        + "multiply this memory's healing by attack speed multiplier before conversion ÷ multiplier after conversion (at least ×1).");
                case MemoryTuningKind.StanceSwordQiAttackBasis:
                    return Loc.T(memory + "を装備中、剣気のダメージの基準が魔力から攻撃力に変わる。係数は変わらず、物理ダメージになる。態勢の攻撃力・攻撃速度の強化は変わらない。",
                        "Sword-qi damage basis: ability power → attack damage." + equipped
                        + "sword qi keeps its coefficient but scales with attack damage and deals physical damage. The stance's attack damage and attack speed bonuses are unchanged.");
                default: throw new InvalidOperationException("Unknown tuning: " + tuning.Kind);
            }
        }
        private static string DescribeIdentityStrike(IdentityStrikeDefinition strike)
        {
            string memory = Links.ItemName(strike.Identity);
            if (!strike.DealsDamage)
                return Loc.T(memory + "を装備中、突進攻撃の元々の追加ダメージ部分だけが、この記憶のダメージとして扱われ、通常攻撃のダメージとしては扱われない。追加ダメージの量は変わらない。",
                    "Extra damage amount unchanged. While " + memory + " is equipped, only the dash attack's existing bonus damage counts as this memory's damage, rather than basic attack damage.");
            string element = strike.Element == IdentityStrikeElement.None ? Loc.T("無属性", "non-elemental") : ElementName(strike.Element);
            string basis = strike.Basis == IdentityStrikeBasis.AttackDamage ? Loc.T("攻撃力", "attack damage") : Loc.T("攻撃力・魔力の高い方", "the higher of attack damage and ability power");
            string amountEn = Percent(strike.AdUnits)
                + (strike.BonusSpeedUnitsPerPercent > 0 ? " + per 1% converted bonus attack speed " + Percent(strike.BonusSpeedUnitsPerPercent) : "");
            string amountJa = Percent(strike.AdUnits)
                + (strike.BonusSpeedUnitsPerPercent > 0 ? "に、変換済みの追加攻撃速度1%につき" + Percent(strike.BonusSpeedUnitsPerPercent) + "を加えた量" : "");
            string shape = strike.Shape == IdentityStrikeShape.ForwardLine
                ? Loc.T("前方" + Number(strike.RangeMetres) + "m、幅" + Number(strike.WidthOrArc) + "mの直線内",
                    "a forward line " + Number(strike.RangeMetres) + "m long and " + Number(strike.WidthOrArc) + "m wide")
                : Loc.T("前方" + Number(strike.RangeMetres) + "m、角度" + Number(strike.WidthOrArc) + "度の扇形内",
                    "a forward arc of " + Number(strike.RangeMetres) + "m and " + Number(strike.WidthOrArc) + "°");
            if (strike.IsCriticalMechanism)
            {
                string identity = memory;
                bool afterDisplacement = strike.Trigger == IdentityStrikeTrigger.AfterDisplacementCritical;
                string cooldown = Number(IdentityStrikeDefinition.CriticalCooldownSeconds);
                string strikeJa = identity + "の斬撃が、対象と" + shape + "の敵に" + basis + "の" + Percent(strike.AdUnits) + "の闇ダメージを与える。";
                string timing = afterDisplacement
                    ? Loc.T("ダッシュや瞬間移動の後" + Number(strike.WindowSeconds) + "秒以内の最初の通常攻撃が会心したとき、" + strikeJa + "非会心でも準備は消費される。",
                        "If the first basic attack hit within " + Number(strike.WindowSeconds) + "s after a dash or teleport is critical (a noncritical hit also consumes readiness), ")
                    : Loc.T("同じ敵に通常攻撃の会心を3回連続で命中させたとき、" + strikeJa + "各命中の間隔は" + Number(strike.WindowSeconds) + "秒以内で、非会心の命中、別の敵への命中、時間切れのいずれかで連続数はリセットされる。",
                        "On three consecutive critical basic attack hits against the same enemy (each gap at most " + Number(strike.WindowSeconds) + "s; a noncritical hit, a different enemy or an expired window resets the sequence), ");
                string damage = Loc.T("斬撃は計" + strike.MaxTargets + "体までに当たり、再発動まで" + cooldown + "秒かかる。",
                    identity + " deals dark damage equal to " + Percent(strike.AdUnits) + " of " + basis + " to the target and enemies in " + shape + " (up to " + strike.MaxTargets + " enemies total; " + cooldown + "s interval).");
                string partners = afterDisplacement
                    ? Loc.T("斬撃時に装備中の回避記憶の残りクールダウンが" + Number(IdentityStrikeDefinition.CriticalMovementRefundPercent) + "%短縮される。記憶『瞬歩』のダッシュや記憶『死の刻印』『撹乱』の瞬間移動で準備でき、記憶『風の傷』の移動後の確定会心と連携する。",
                        " The strike reduces the equipped dodge memory's remaining cooldown by " + Number(IdentityStrikeDefinition.CriticalMovementRefundPercent) + "%. Dashes from Memory \"Flash Step\" and teleports from Memory \"Death Mark\" or Memory \"Deception\" prime it, pairing with Memory \"Scar of the Wind\"'s guaranteed post-movement critical hit.")
                    : Loc.T("記憶『瞬歩』のダッシュや記憶『死の刻印』の瞬間移動と、記憶『一歩一殺』の移動後の確定会心で、連続会心をつなげられる。",
                        " Dashes from Memory \"Flash Step\" and teleports from Memory \"Death Mark\" pair with Memory \"One Step, One Kill\"'s guaranteed post-movement critical hit to maintain the sequence.");
                return timing + damage + partners + Loc.T(identity + "に装着したエッセンス『神聖なる信仰』のダメージ増幅と、命中後6秒以内の撃破による成長が働く。エッセンスを装着していなくても発動する。",
                    " Essence \"Divine Faith\" socketed to " + identity + " amplifies this damage and gains stacks from kills within 6s of the hit (the strike also works without that Essence).");
            }
            bool nextBasicHit = strike.Trigger == IdentityStrikeTrigger.AfterDisplacementNextBasicHit;
            string when = nextBasicHit
                ? Loc.T("ダッシュや瞬間移動の後" + Number(strike.WindowSeconds) + "秒以内に、次の自分の通常攻撃が命中したとき",
                    "when your next basic attack hits within " + Number(strike.WindowSeconds) + "s after a dash or teleport (once per displacement; does not stack)")
                : Loc.T("自分の通常攻撃が" + strike.EveryN + "回命中するたび",
                    "every " + strike.EveryN + " basic attacks you make that hit (multiple hits from one attack count once)");
            string whenNote = nextBasicHit ? "斬撃は移動1回につき1回までで、重ならない。" : "同じ通常攻撃の複数の命中は1回と数える。";
            return Loc.T(memory + "を装備中、" + when + "、" + shape + "の敵最大" + strike.MaxTargets + "体に" + element + "の斬撃を放つ。追加ダメージの量は" + basis + "の" + amountJa
                    + "で、この記憶のダメージとして扱われる。" + whenNote + "時間による再発動制限はない。",
                "Extra damage +" + amountEn + " of " + basis + ". While " + memory + " is equipped, " + when + ", strike up to " + strike.MaxTargets + " enemies in " + shape + " for " + element + " damage attributed to this memory. No time-based cooldown.");
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

        /// <summary>日本語では文の末尾を「。」にそろえる（他の担当の文が括弧で終わっていても、段落としてつなげられるように）。</summary>
        private static string EndSentence(string text) =>
            text.Length == 0 || text.EndsWith("。", StringComparison.Ordinal) ? text : text + "。";

        /// <summary>「A、Bをすべて装備している必要がある。」</summary>
        private static string RequiredEquipText(IEnumerable<string> memories)
        {
            var names = memories.Select(Links.ItemName).ToList();
            return Loc.T(string.Join("と", names) + "を" + (names.Count > 1 ? "すべて" : "") + "装備している必要がある。",
                "All must be equipped: " + string.Join(", ", names));
        }

        internal static string DescribeMechanism(AuthoredMechanismSpec spec)
        {
            AuthoredMechanisms.Validate(spec);
            // 英語は従来どおり1項目1行。日本語は1つの段落の文章にする（空の項目は日本語側に対応する文が無いことを表す）。
            var lines = new List<string>();
            string source = spec.Source != null ? SelectorText(spec.Source)
                : spec.Recharge != null ? SelectorText(spec.Recharge.Source)
                : spec.Primed != null ? Links.ItemName(spec.Primed.SourceMemory)
                : spec.Relay != null ? Links.ItemName(RelayWindowDefinition.SourceMemory)
                : spec.Dividend != null ? Links.ItemName(spec.Dividend.SourceMemory)
                : Loc.T("装備中の移動以外の記憶", "equipped nonmovement memories");
            // 識別ごとの発動条件があるときは、それらを並べる（「または」でつなぐ）。
            string identityTriggers = spec.TriggerByIdentity.Count == 0 ? null
                : string.Join(Loc.T("、または", ", or "), spec.TriggerByIdentity.Select(t => Links.ItemName(t.Key) + EventText(t.Value)));
            string triggerClause = identityTriggers ?? source + EventText(spec.Trigger);
            switch (spec.Kind)
            {
                case AuthoredMechanismKind.Gimmick:
                    string triggerText = Loc.T("発動条件：", "Triggered when ") + (spec.TriggerByIdentity.Count == 0
                        ? source + EventText(spec.Trigger)
                        : identityTriggers);
                    string description = Gimmicks.DescribeForSource(spec.Gimmick, source, triggerText: triggerText);
                    if (description.Length == 0) throw new InvalidOperationException("Unmapped effect source.");
                    lines.Add(description);
                    break;
                case AuthoredMechanismKind.DirectedRecharge:
                    var recharge = spec.Recharge;
                    lines.Add(Loc.T(source + EventText(recharge.SourceTrigger) + "、" + (recharge.EveryN > 1 ? "その" + recharge.EveryN + "回ごとに" : "")
                            + Percent(recharge.ProbabilityUnits) + "の確率で、" + SelectorText(recharge.Recipient) + "の残りクールダウンを" + Percent(recharge.EffectiveValueUnits)
                            + "短縮する。短縮量は残り時間を基準に決まり、使用回数は直接回復しない。",
                        "Remaining cooldown -" + Percent(recharge.EffectiveValueUnits) + ". Recipient: " + SelectorText(recharge.Recipient) + "."));
                    lines.Add(Loc.T("", source + EventText(recharge.SourceTrigger) + "; chance " + Percent(recharge.ProbabilityUnits)
                        + (recharge.EveryN > 1 ? $"; rolled every {recharge.EveryN} qualifying events." : ".")
                        + " Reduces the remaining time, not charges directly."));
                    switch (recharge.Condition)
                    {
                        case RechargeConditionKind.Always: break;
                        case RechargeConditionKind.ChangedTarget: lines.Add(Loc.T("前回と異なる敵に命中したときだけ発動し、最初の命中では発動しない。", "Requires a different target from the previous hit (the first hit does not trigger it).")); break;
                        case RechargeConditionKind.Shielded: lines.Add(Loc.T("障壁がある間だけ発動する。", "Requires an active shield.")); break;
                        case RechargeConditionKind.ElementTypesAtLeast: lines.Add(Loc.T($"敵に{recharge.RequiredElementTypes}種類以上の属性があるときだけ発動する。", $"Requires at least {recharge.RequiredElementTypes} element types on the enemy.")); break;
                        default: throw new InvalidOperationException("Unknown recharge condition: " + recharge.Condition);
                    }
                    break;
                case AuthoredMechanismKind.MemoryPrimed:
                    lines.Add(Loc.T(source + EventText(spec.Primed.Trigger) + "、" + Number(spec.Primed.DurationSeconds) + "秒以内の次の自分の通常攻撃に、攻撃力・魔力の高い方の"
                            + Percent(spec.Primed.ValueUnits) + "の追加ダメージを上乗せする。重ならず、次の通常攻撃への上乗せが複数あっても、消費されるのは最大の1つだけで、残りは保持される。",
                        "Next basic attack extra damage +" + Percent(spec.Primed.ValueUnits) + " of the higher of attack damage and ability power."));
                    lines.Add(Loc.T("", source + EventText(spec.Primed.Trigger) + ": " + Number(spec.Primed.DurationSeconds)
                        + "s to use on your next basic attack (does not stack). Only the largest of all next-basic-attack bonuses is consumed; the others remain ready."));
                    break;
                case AuthoredMechanismKind.RelayWindow:
                    lines.Add(Loc.T(source + EventText(MemoryEventKind.ConfirmedUse) + "、" + Number(spec.Relay.DurationSeconds) + "秒間、" + Links.ItemName(spec.Relay.TargetMemory)
                            + "の元々のダメージが" + Percent(spec.Relay.ValueUnits) + "増える。星による追加ダメージは対象外で、両方の記憶を装備している必要がある。",
                        "Memory direct damage +" + Percent(spec.Relay.ValueUnits) + ". Target: " + Links.ItemName(spec.Relay.TargetMemory) + "."));
                    lines.Add(Loc.T("", source + EventText(MemoryEventKind.ConfirmedUse) + ": " + Number(spec.Relay.DurationSeconds)
                        + "s of increased damage from the memory itself, excluding star-generated extra damage. Both memories must be equipped."));
                    break;
                case AuthoredMechanismKind.AlliedWard:
                    lines.Add(DescribeWard(spec.Ward, triggerClause));
                    if (spec.TriggerByIdentity.Count == 0)
                        lines.Add(Loc.T("", "Trigger: " + source + EventText(spec.Trigger) + "."));
                    break;
                case AuthoredMechanismKind.PressureDividend:
                    // しきい値と上限は定数から組み立てる（PressureDividendChannel.MinimumAppliedHpMultiplier・MaximumProbabilityUnits）。
                    int dividendHpGain = (int)Math.Round((PressureDividendChannel.MinimumAppliedHpMultiplier - 1.0) * 100.0, MidpointRounding.AwayFromZero);
                    int dividendCap = PressureDividendChannel.MaximumProbabilityUnits / 100;
                    lines.Add(Loc.T(source + "で、夢の圧により最大HPが" + dividendHpGain + "%以上増えた敵を倒したとき、" + Percent(spec.Dividend.ProbabilityUnits)
                            + "の確率（上限" + dividendCap + "%）で未確保の欠片を1個獲得する。同じ敵から獲得できるのは1人につき最大1個まで。",
                        "Unsecured shards +1 (chance " + Percent(spec.Dividend.ProbabilityUnits) + ", capped at " + dividendCap + "%)."));
                    lines.Add(Loc.T("", source + " must kill an enemy whose maximum HP was increased by at least " + dividendHpGain + "% by dream pressure. You can receive at most 1 shard from the same enemy."));
                    if (spec.Dividend.RequiredMemories.Count > 0)
                        lines.Add(RequiredEquipText(spec.Dividend.RequiredMemories));
                    break;
                case AuthoredMechanismKind.MemoryTuning: lines.Add(DescribeMemoryTuning(spec.Tuning)); break;
                case AuthoredMechanismKind.IdentityStrike: lines.Add(DescribeIdentityStrike(spec.IdentityStrike)); break;
                case AuthoredMechanismKind.SacrificeShield:
                    string sacrificeValue = Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_value);
                    string sacrificeDuration = Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_duration);
                    string sacrificeCap = Number(MemoryDamageBalance.Effect_legacy_h_aurena_key2_native_newAwardCap);
                    lines.Add(Loc.T(Links.ItemName("St_Q_GoldenBurst") + "または" + Links.ItemName("St_Q_Reduction") + "が元々行うHP支払いのたびに、実際に支払ったHPの" + sacrificeValue
                            + "%の障壁を" + sacrificeDuration + "秒間得る。1回の付与は最大HPの" + sacrificeCap + "%までで、通常の被ダメージは対象外。HP支払い自体は減らない。再発動の間隔制限はない。",
                        $"Shield +{sacrificeValue}% of HP actually paid ({sacrificeDuration}s; each award capped at {sacrificeCap}% maximum HP)."));
                    lines.Add(Loc.T("", "Applies only to the original HP payment of " + Links.ItemName("St_Q_GoldenBurst") + " or " + Links.ItemName("St_Q_Reduction") + ", not ordinary damage taken. Does not reduce the HP cost. No cooldown."));
                    lines.Add(PoolText(ModShieldPoolKind.Ordinary));
                    break;
                case AuthoredMechanismKind.StunSourceFilter:
                    string stunValue = Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_value);
                    string stunDuration = Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_duration);
                    string stunCooldown = Number(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_cooldown);
                    lines.Add(Loc.T($"装備中のQまたはRの記憶が元々持つ効果で敵をスタンさせたとき、最大HPの{stunValue}%の障壁を{stunDuration}秒間得る。星による追加スタンは対象外で、再発動まで{stunCooldown}秒かかる。",
                        $"Shield +{stunValue}% maximum HP for {stunDuration}s. Gain it when the original effect of your equipped Q or R memory stuns an enemy, excluding star-generated stuns. Cooldown: {stunCooldown}s."));
                    lines.Add(PoolText(ModShieldPoolKind.Ordinary));
                    break;
                case AuthoredMechanismKind.BridgeSuccess:
                    var bridge = spec.Bridge;
                    lines.Add(DescribeBridgePayload(bridge.BasePayoff, SelectorText(bridge.PayoffSource)));
                    foreach (var payload in bridge.Extras) lines.Add(DescribeBridgePayload(payload, SelectorText(bridge.PayoffSource)));
                    foreach (var endpoint in bridge.Endpoints)
                        lines.Add(Loc.T(Links.ItemName(endpoint.Memory) + "を装備し、" + StarName(endpoint.StarId) + "を" + endpoint.MinimumRank + "段以上取得している必要がある。",
                            "Required equipped: " + Links.ItemName(endpoint.Memory) + ". Required star: " + StarName(endpoint.StarId) + $" at rank {endpoint.MinimumRank} or higher."));
                    string opening = SelectorText(bridge.OpeningSource) + EventText(bridge.OpeningTrigger);
                    string payoff = SelectorText(bridge.PayoffSource) + EventText(bridge.PayoffTrigger);
                    if (bridge.GateKind == BridgeGateKind.Mark)
                    {
                        string markedPayoff = SelectorText(bridge.PayoffSource) + EventText(bridge.PayoffTrigger)
                            .Replace(Loc.T("敵", "an enemy"), Loc.T("印のある同じ敵", "the same marked enemy"));
                        lines.Add(Loc.T(opening + "、その敵に" + Number(bridge.MarkSeconds) + "秒間の印を付ける。" + markedPayoff + "、追加効果が発動する。印は消費されない。",
                            "Combo condition: when " + opening + ", mark that enemy for " + Number(bridge.MarkSeconds) + "s. When " + markedPayoff + ", apply the extra effects (does not consume the mark)."));
                    }
                    else if (bridge.GateKind == BridgeGateKind.Window)
                        lines.Add(Loc.T(opening + "から、" + (bridge.UsesNativeWindowLifetime ? "元々の効果の残り持続時間の" + Number(bridge.WindowLifetimeScale) + "倍" : Number(bridge.WindowSeconds) + "秒") + "以内に" + payoff + "、追加効果が発動する。",
                            "Combo condition: when " + opening + ", then " + payoff + " within " + (bridge.UsesNativeWindowLifetime ? Number(bridge.WindowLifetimeScale) + "× the original effect's remaining duration" : Number(bridge.WindowSeconds) + "s") + ", apply the extra effects."));
                    else lines.Add(Loc.T(payoff + "、追加効果が受け手の記憶へ直接適用される。印や先に使う記憶は不要。",
                        "Combo condition: " + payoff + "; apply directly to the recipient memory (no mark or opening use required)."));
                    if (bridge.SourcePhase != BridgeSourcePhase.Any)
                        lines.Add(bridge.SourcePhase == BridgeSourcePhase.InitialExplosion
                            ? Loc.T("発動するのは、受け手の記憶の最初の爆発が命中したときだけで、途中や最後の爆発は対象外。", "Only the payoff memory's initial explosion qualifies, not intermediate or ending hits.")
                            : Loc.T("発動するのは、受け手の記憶の最後の爆発が命中したときだけで、最初や途中の爆発は対象外。", "Only the payoff memory's ending explosion qualifies, not initial or intermediate hits."));
                    lines.Add(bridge.CooldownSeconds > 0 ? Loc.T("再発動まで" + Number(bridge.CooldownSeconds) + "秒。", "Cooldown: " + Number(bridge.CooldownSeconds) + "s.") : Loc.T("再発動の間隔制限はない。", "No cooldown."));
                    break;
                default: throw new InvalidOperationException("Unknown authored mechanism: " + spec.Kind);
            }
            bool eventDriven = spec.Kind != AuthoredMechanismKind.MemoryTuning && spec.Kind != AuthoredMechanismKind.IdentityStrike
                && spec.Kind != AuthoredMechanismKind.SacrificeShield && spec.Kind != AuthoredMechanismKind.StunSourceFilter;
            var budget = spec.Bridge?.Budget ?? spec.Recharge?.Budget ?? spec.Primed?.Budget ?? spec.Budget;
            if (eventDriven) lines.Add(BudgetText(budget));
            if (eventDriven && spec.Gimmick == null && spec.Bridge == null)
                lines.Add(Loc.T("再発動の間隔制限はない。", "No time-based cooldown."));
            if (eventDriven && spec.EveryN > 1 && spec.Recharge == null)
                lines.Add(Loc.T($"効果は、条件を満たす{spec.EveryN}回ごとに適用される。", $"Applies every {spec.EveryN} qualifying events."));
            if (spec.Once && budget != AttributionBudget.PerActivation)
                lines.Add(Loc.T("同じ使用からは1回だけ発動する。", "Only once per memory use."));
            if (spec.ValuesByRank.Length > 0)
                lines.Add(Loc.T("値は1段目から順に、" + string.Join("、", spec.ValuesByRank.Select(v => MechanismRankValue(spec, v))) + "。",
                    "From rank 1 onward: " + string.Join(" / ", spec.ValuesByRank.Select(v => MechanismRankValue(spec, v)))));
            foreach (var trigger in spec.TriggerByIdentity)
                lines.Add(Loc.T("", "Trigger: " + Links.ItemName(trigger.Key) + EventText(trigger.Value) + "."));
            // 識別ごとの発動条件は、仕掛けと味方への障壁では上の文に含めてある。それ以外の仕組みは、ここで補う。
            if (Loc.Japanese && identityTriggers != null && spec.Kind != AuthoredMechanismKind.Gimmick && spec.Kind != AuthoredMechanismKind.AlliedWard)
                lines.Add(identityTriggers + "でも発動する。");
            var requiredMemories = spec.RequiredMemories
                .Where(id => spec.Bridge == null || !spec.Bridge.Endpoints.Any(endpoint => endpoint.Memory == id)).ToList();
            if (requiredMemories.Count > 0)
                lines.Add(RequiredEquipText(requiredMemories));
            if (spec.Replaces.Length > 0)
                lines.Add(Loc.T(string.Join("と", spec.Replaces.Select(StarName)) + "を置き換える。", "Replaces stars: " + string.Join(", ", spec.Replaces.Select(StarName))));
            switch (spec.Condition)
            {
                case AuthoredMechanismCondition.Always: break;
                case AuthoredMechanismCondition.BridgeSuccess: lines.Add(Loc.T(PairName(spec.PairId) + "が成功したときだけ有効になる。", "On bridge success only: " + PairName(spec.PairId))); break;
                case AuthoredMechanismCondition.BridgeMark: lines.Add(Loc.T(PairName(spec.PairId) + "の印がある敵にだけ有効になる。", "Only on bridge-marked enemies: " + PairName(spec.PairId))); break;
                case AuthoredMechanismCondition.BridgeWindow: lines.Add(Loc.T(PairName(spec.PairId) + "の受付時間内だけ有効になる。", "Only within the bridge window: " + PairName(spec.PairId))); break;
                default: throw new InvalidOperationException("Unknown mechanism condition: " + spec.Condition);
            }
            return Loc.Japanese ? string.Concat(lines.Where(line => line.Length > 0).Select(EndSentence)) : string.Join("\n", lines);
        }

        private static string DescribeBridgePayload(BridgePayload payload, string source)
        {
            switch (payload.Kind)
            {
                case BridgePayloadKind.Damage:
                    return Loc.T("合わせ技が成立したとき、成立時に命中した敵に、"
                            + (payload.DamageBasis == BridgeDamageBasis.NativeHit ? "発動した命中の元々のダメージ" : "攻撃力・魔力の高い方") + "の" + Percent(payload.ValueUnits) + "の追加ダメージを与える。",
                        "Extra damage +" + Percent(payload.ValueUnits) + " of "
                        + (payload.DamageBasis == BridgeDamageBasis.NativeHit ? "the payoff hit's original damage" : "the higher of attack damage and ability power")
                        + ". Targets the enemy hit when the combo succeeds.");
                case BridgePayloadKind.Recharge:
                    return Loc.T("合わせ技が成立したとき、" + SelectorText(payload.Recipient) + "の残りクールダウンを" + Percent(payload.ValueUnits) + "短縮する。",
                        "Remaining cooldown -" + Percent(payload.ValueUnits) + ". Recipient: " + SelectorText(payload.Recipient) + ".");
                case BridgePayloadKind.OrdinaryShield:
                    return Loc.T("合わせ技が成立したとき、最大HPの" + Percent(payload.ValueUnits) + "の障壁を" + Number(payload.DurationSeconds) + "秒間得る。",
                            "Self shield +" + Percent(payload.ValueUnits) + " maximum HP (" + Number(payload.DurationSeconds) + "s). ")
                        + PoolText(ModShieldPoolKind.Ordinary);
                case BridgePayloadKind.Gimmick:
                    return Gimmicks.DescribeForSource(payload.Gimmick, source,
                        triggerText: Loc.T("この合わせ技が成立したときに発動", "Triggered when this combo succeeds"));
                case BridgePayloadKind.AlliedWard:
                    return DescribeWard(payload.Ward, "合わせ技が成立したとき");
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
                return Loc.T(stacks > 0 ? stacks + "つ" + (chance > 0 ? "（さらに" + Number(chance) + "%の確率でもう1つ）" : "") : chance > 0 ? Number(chance) + "%の確率で1つ" : "",
                    (stacks > 0 ? stacks + " stacks" : "")
                    + (chance > 0 ? (stacks > 0 ? " + chance of one more: " : "chance of one stack: ") + Number(chance) + "%" : ""));
            }
            if (gimmick?.Effect == GimmickEffect.Daze)
                return Number(units / 1000m) + Loc.T("秒のスタン", "s stun");
            return Percent(units);
        }

        private static string DescribeWard(AlliedWardDefinition ward, string jaTrigger = null)
        {
            bool travelers = ward.RecipientKind == WardRecipientKind.AlliedTravelers;
            string basisEn = ward.AmountBasis == WardAmountBasis.RecipientMaxHP ? "the recipient's maximum HP" : "the caster's higher of attack damage and ability power";
            string basisJa = ward.AmountBasis == WardAmountBasis.RecipientMaxHP ? "受け手の最大HP" : "付与者の攻撃力・魔力の高い方";
            return Loc.T((jaTrigger == null ? "" : jaTrigger + "、") + "自分から" + Number(ward.RadiusMetres) + "m以内にいる" + (travelers ? "味方の旅人" : "自分の召喚獣")
                    + (ward.IncludeOwner ? "と自分" : "（自分を除く）") + "のうち最大" + ward.Targets + "体に、" + basisJa + "の" + Percent(ward.ValueUnits) + "の障壁を"
                    + Number(ward.DurationSeconds) + "秒間付与する。",
                "Shield +" + Percent(ward.ValueUnits) + " of " + basisEn + ". Targets: " + (travelers ? "allied travelers" : "your summons")
                    + (ward.IncludeOwner ? " and yourself" : " (excluding yourself)") + " within " + Number(ward.RadiusMetres) + "m of you, up to " + ward.Targets
                    + " targets, for " + Number(ward.DurationSeconds) + "s. ")
                + PoolText(ward.PoolKind);
        }
    }
}

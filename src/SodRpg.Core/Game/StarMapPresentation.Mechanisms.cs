using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static partial class StarMapPresentation
    {
        private static string Number(decimal value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
        private static string Number(float value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
        private static string Percent(decimal units) => Number(units / 100m) + "%";

        private static string EventText(MemoryEventKind trigger)
        {
            switch (trigger)
            {
                case MemoryEventKind.ConfirmedUse: return Loc.T("使用が成立したとき", "on confirmed use");
                case MemoryEventKind.Hit: return Loc.T("敵に命中したとき", "on hit");
                case MemoryEventKind.CriticalHit: return Loc.T("会心が命中したとき", "on critical hit");
                case MemoryEventKind.Kill: return Loc.T("敵を倒したとき", "on kill");
                case MemoryEventKind.OwnedBasicAttackFired: return Loc.T("自分の通常攻撃を放ったとき", "on own basic attack fired");
                case MemoryEventKind.OwnedBasicAttackHit: return Loc.T("自分の通常攻撃が命中したとき", "on own basic attack hit");
                default: throw new InvalidOperationException("Unknown mechanism trigger: " + trigger);
            }
        }

        private static string BudgetText(AttributionBudget budget)
        {
            switch (budget)
            {
                case AttributionBudget.PerActivation: return Loc.T("1回の使用につき1回まで", "at most once per activation");
                case AttributionBudget.PerActivationVictim: return Loc.T("1回の使用につき敵1体ごとに1回まで", "at most once per victim per activation");
                case AttributionBudget.PerKill: return Loc.T("倒した敵1体につき1回まで", "at most once per kill");
                case AttributionBudget.PerOwnedBasicAttack: return Loc.T("自分の通常攻撃1回につき1回まで", "at most once per own basic attack");
                default: throw new InvalidOperationException("Unknown mechanism budget: " + budget);
            }
        }

        private static string PoolText(ModShieldPoolKind pool)
        {
            switch (pool)
            {
                case ModShieldPoolKind.Ordinary: return Loc.T("通常の障壁枠", "ordinary shield pool");
                case ModShieldPoolKind.Rampart: return Loc.T("防壁の枠", "rampart pool");
                case ModShieldPoolKind.Allied: return Loc.T("味方用の障壁枠", "allied shield pool");
                default: throw new InvalidOperationException("Unknown shield pool: " + pool);
            }
        }

        private static string EffectText(GimmickEffect effect)
        {
            switch (effect)
            {
                case GimmickEffect.Element: return Loc.T("属性付与", "element stacks");
                case GimmickEffect.Burst: return Loc.T("範囲攻撃", "area burst");
                case GimmickEffect.Shield: return Loc.T("障壁", "shield");
                case GimmickEffect.Heal: return Loc.T("回復", "healing");
                case GimmickEffect.Recharge: return Loc.T("クールダウン短縮", "cooldown reduction");
                case GimmickEffect.Quicken: return Loc.T("攻撃速度強化", "attack speed enhancement");
                case GimmickEffect.Empower: return Loc.T("攻撃力・魔力強化", "offense enhancement");
                case GimmickEffect.Expose: return Loc.T("被ダメージ増加", "damage vulnerability");
                case GimmickEffect.Echo: return Loc.T("反響", "echo damage");
                case GimmickEffect.Reload: return Loc.T("使用回数回復", "charge restoration");
                case GimmickEffect.RechargeOther: return Loc.T("ほかの通常記憶のクールダウン短縮", "other normal memory cooldown reduction");
                case GimmickEffect.Wound: return Loc.T("傷", "wound damage");
                case GimmickEffect.Daze: return Loc.T("よろめき", "daze");
                case GimmickEffect.Ricochet: return Loc.T("跳弾", "ricochet");
                case GimmickEffect.Siphon: return Loc.T("生命吸収", "life siphon");
                case GimmickEffect.Rampart: return Loc.T("防壁", "rampart");
                case GimmickEffect.Primed: return Loc.T("次の通常攻撃強化", "next basic attack enhancement");
                case GimmickEffect.Crescendo: return Loc.T("段階的強化", "crescendo");
                case GimmickEffect.ElementEdge: return Loc.T("属性の刃", "element edge");
                case GimmickEffect.PackMend: return Loc.T("仲間の回復", "pack healing");
                case GimmickEffect.Sap: return Loc.T("弱体化", "sap");
                case GimmickEffect.Weakspot: return Loc.T("弱点", "weakspot");
                default: throw new InvalidOperationException("Unknown effect: " + effect);
            }
        }

        private static string DescribeMechanism(AuthoredMechanismSpec spec)
        {
            AuthoredMechanisms.Validate(spec);
            var lines = new List<string>();
            string source = spec.Source != null ? SelectorText(spec.Source)
                : spec.Recharge != null ? SelectorText(spec.Recharge.Source)
                : spec.Primed != null ? Links.Name(spec.Primed.SourceMemory).ToString()
                : spec.Relay != null ? Links.Name(RelayWindowDefinition.SourceMemory).ToString()
                : spec.Dividend != null ? Links.Name(spec.Dividend.SourceMemory).ToString()
                : Loc.T("装備中の移動以外の記憶", "equipped nonmovement memories");
            lines.Add(source + Loc.T("：", ": ") + EventText(spec.Trigger));
            switch (spec.Kind)
            {
                case AuthoredMechanismKind.Gimmick:
                    // Concrete sources use the existing complete effect formatter, never a fabricated memory.
                    if (spec.Source?.Kind == MemorySelectorKind.Memory && spec.Source.Alternatives.Count == 0)
                    {
                        string description = Gimmicks.Describe(spec.Gimmick, spec.Source.Memory);
                        if (description.Length == 0) throw new InvalidOperationException("Unmapped gimmick source: " + spec.Source.Memory);
                        lines.Add(description);
                    }
                    else lines.Add(EffectText(spec.Gimmick.Effect) + Loc.T("の基本効果量：", " base value: ")
                        + Number(spec.Gimmick.Value) + Loc.T("（効果の百分率）", "%")
                        + Loc.T("、効果の指定：", ", effect argument: ") + spec.Gimmick.Arg
                        + Loc.T("、発動間隔：", ", interval: ") + Number(spec.Gimmick.Cooldown) + Loc.T("秒", "s"));
                    break;
                case AuthoredMechanismKind.DirectedRecharge:
                    var recharge = spec.Recharge;
                    lines.Add(SelectorText(recharge.Recipient) + Loc.T("の残りクールダウンを", ": reduce remaining cooldown by ")
                        + Percent(recharge.EffectiveValueUnits) + Loc.T("短縮。発動確率", "; chance ") + Percent(recharge.ProbabilityUnits)
                        + Loc.T($"。{recharge.EveryN}回ごとに発動。", $"; every {recharge.EveryN} qualifying events."));
                    switch (recharge.Condition)
                    {
                        case RechargeConditionKind.Always: break;
                        case RechargeConditionKind.ChangedTarget: lines.Add(Loc.T("前回と異なる敵に命中した場合のみ。", "Requires a different target from the previous hit.")); break;
                        case RechargeConditionKind.Shielded: lines.Add(Loc.T("障壁がある間のみ。", "Requires an active shield.")); break;
                        case RechargeConditionKind.ElementTypesAtLeast: lines.Add(Loc.T($"敵に{recharge.RequiredElementTypes}種類以上の属性がある場合のみ。", $"Requires at least {recharge.RequiredElementTypes} element types on the enemy.")); break;
                        default: throw new InvalidOperationException("Unknown recharge condition: " + recharge.Condition);
                    }
                    break;
                case AuthoredMechanismKind.MemoryPrimed:
                    lines.Add(Loc.T("次の自分の通常攻撃を", "Enhances the next own basic attack by ") + Percent(spec.Primed.ValueUnits)
                        + Loc.T("強化。準備の持続：", "; preparation lasts ") + Number(spec.Primed.DurationSeconds) + Loc.T("秒。", "s."));
                    break;
                case AuthoredMechanismKind.RelayWindow:
                    lines.Add(Links.Name(spec.Relay.TargetMemory) + Loc.T("の固有ダメージを", " native damage +") + Percent(spec.Relay.ValueUnits)
                        + Loc.T("強化。持続：", "; duration ") + Number(spec.Relay.DurationSeconds) + Loc.T("秒。", "s."));
                    break;
                case AuthoredMechanismKind.AlliedWard:
                    var ward = spec.Ward;
                    lines.Add((ward.RecipientKind == WardRecipientKind.AlliedTravelers ? Loc.T("味方の旅人", "allied travelers") : Loc.T("自分の召喚物", "owned summons"))
                        + (ward.IncludeOwner ? Loc.T("（自分を含む）", " (including self)") : Loc.T("（自分を除く）", " (excluding self)"))
                        + Loc.T("に", ": ") + (ward.AmountBasis == WardAmountBasis.RecipientMaxHP ? Loc.T("受け手の最大HP", "recipient maximum HP") : Loc.T("付与者の攻撃力・魔力の高い方", "caster's higher offense"))
                        + Loc.T("の", " × ") + Percent(ward.ValueUnits) + Loc.T("の障壁。", " shield.")
                        + Loc.T("半径", " Radius ") + Number(ward.RadiusMetres) + "m / " + Number(ward.DurationSeconds) + Loc.T("秒 / 最大", "s / up to ")
                        + ward.Targets + Loc.T("体。", " targets. ") + PoolText(ward.PoolKind));
                    break;
                case AuthoredMechanismKind.PressureDividend: lines.Add(spec.Dividend.Describe()); break;
                case AuthoredMechanismKind.SacrificeShield:
                    lines.Add(Loc.T("指定された固有のHP支払いの50%を障壁に変換。新たに付与する量は最大HPの10%まで。", "Converts 50% of qualified native HP payment to a shield; each new award is capped at 10% maximum HP.")); break;
                case AuthoredMechanismKind.StunSourceFilter:
                    lines.Add(Loc.T("装備中のQ/Rの固有効果でスタンに成功すると最大HPの6%の障壁（3秒間・発動間隔2秒）。", "A successful native stun from equipped Q/R grants a 6% maximum HP shield for 3s, at most once every 2s.")); break;
                case AuthoredMechanismKind.BridgeSuccess:
                    var bridge = spec.Bridge;
                    foreach (var endpoint in bridge.Endpoints)
                        lines.Add(Links.Name(endpoint.Memory) + Loc.T($"：端点の星 [{endpoint.StarId}] に{endpoint.MinimumRank}段必要。", $": endpoint [{endpoint.StarId}] requires {endpoint.MinimumRank} ranks."));
                    lines.Add(SelectorText(bridge.OpeningSource) + " / " + EventText(bridge.OpeningTrigger)
                        + " → " + SelectorText(bridge.PayoffSource) + " / " + EventText(bridge.PayoffTrigger));
                    lines.Add(bridge.GateKind == BridgeGateKind.Mark ? Loc.T("同じ敵の印を起点に発動。", "Triggers from a mark on the same enemy.")
                        : bridge.GateKind == BridgeGateKind.Window ? Loc.T("橋の受付時間内に発動。", "Triggers within the bridge window.")
                        : Loc.T("指定した受け手に直接発動。", "Triggers directly for the specified recipient."));
                    lines.Add(Loc.T("受付時間：", "Window: ") + Number(bridge.WindowSeconds) + (bridge.GateKind == BridgeGateKind.Mark ? Loc.T("秒、印：", "s; mark: ") + Number(bridge.MarkSeconds) : "") + Loc.T("秒、発動間隔：", "s; interval: ") + Number(bridge.CooldownSeconds) + Loc.T("秒。", "s."));
                    lines.Add(DescribeBridgePayload(bridge.BasePayoff));
                    foreach (var payload in bridge.Extras) lines.Add(DescribeBridgePayload(payload));
                    break;
                default: throw new InvalidOperationException("Unknown authored mechanism: " + spec.Kind);
            }
            lines.Add(BudgetText(spec.Budget));
            if (spec.EveryN > 1) lines.Add(Loc.T($"発火元の通知{spec.EveryN}回ごとに発動。", $"Triggers every {spec.EveryN} source notifications."));
            if (spec.Once) lines.Add(Loc.T("同じ使用からは1回だけ発動。", "Only once from the same activation."));
            if (spec.ValuesByRank.Length > 0)
                lines.Add(Loc.T("各段の効果量：", "Values by rank: ") + string.Join(" / ", spec.ValuesByRank.Select(v => Percent(v))));
            foreach (var trigger in spec.TriggerByIdentity)
                lines.Add(Links.Name(trigger.Key) + Loc.T("の条件：", " trigger: ") + EventText(trigger.Value));
            if (spec.RequiredMemories.Length > 0)
                lines.Add(Loc.T("必要な装備記憶：", "Required equipped memories: ") + string.Join(Loc.T("、", ", "), spec.RequiredMemories.Select(m => Links.Name(m).ToString())));
            if (spec.Replaces.Length > 0) lines.Add(Loc.T("置き換える星：", "Replaces stars: ") + string.Join(", ", spec.Replaces));
            switch (spec.Condition)
            {
                case AuthoredMechanismCondition.Always: break;
                case AuthoredMechanismCondition.BridgeSuccess: lines.Add(Loc.T("橋の成功時のみ：", "On bridge success only: ") + spec.PairId); break;
                case AuthoredMechanismCondition.BridgeMark: lines.Add(Loc.T("橋の印がある敵にのみ：", "Only on bridge-marked enemies: ") + spec.PairId); break;
                case AuthoredMechanismCondition.BridgeWindow: lines.Add(Loc.T("橋の受付時間内のみ：", "Only within the bridge window: ") + spec.PairId); break;
                default: throw new InvalidOperationException("Unknown mechanism condition: " + spec.Condition);
            }
            return string.Join("\n", lines);
        }

        private static string DescribeBridgePayload(BridgePayload payload)
        {
            switch (payload.Kind)
            {
                case BridgePayloadKind.Damage:
                    return (payload.DamageBasis == BridgeDamageBasis.NativeHit ? Loc.T("命中した固有ダメージ", "native hit damage") : Loc.T("攻撃力・魔力の高い方", "higher offense"))
                        + Loc.T("の", " × ") + Percent(payload.ValueUnits) + Loc.T("の追加ダメージ。", " extra damage.");
                case BridgePayloadKind.Recharge:
                    return SelectorText(payload.Recipient) + Loc.T("の残りクールダウンを", ": reduce remaining cooldown by ") + Percent(payload.ValueUnits) + Loc.T("短縮。", ".");
                case BridgePayloadKind.OrdinaryShield:
                    return Loc.T("最大HPの", "Maximum HP × ") + Percent(payload.ValueUnits) + Loc.T("の障壁（", " shield (")
                        + Number(payload.DurationSeconds) + Loc.T("秒・通常の障壁枠）。", "s, ordinary pool).");
                case BridgePayloadKind.Gimmick:
                    return EffectText(payload.Gimmick.Effect) + Loc.T("：効果量", ": value ") + Number(payload.Gimmick.Value) + "%";
                case BridgePayloadKind.AlliedWard:
                    return Loc.T("自分と近くの味方旅人へ、攻撃力・魔力の高い方の", "Ally ward: higher offense × ") + Percent(payload.Ward.ValueUnits)
                        + Loc.T("の障壁（", " (") + Number(payload.Ward.DurationSeconds) + Loc.T("秒・最大", "s, up to ") + payload.Ward.Targets + Loc.T("体）。", " allies).");
                default: throw new InvalidOperationException("Unknown bridge payload: " + payload.Kind);
            }
        }
    }
}

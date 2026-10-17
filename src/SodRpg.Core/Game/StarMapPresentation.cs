using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>Localized star-map text, independent of the host UI.</summary>
    public static partial class StarMapPresentation
    {
        public static string ChoiceDescription(TalentDef star, int chosen, int rank, bool bulleted = false)
        {
            ValidateChoice(star, chosen);
            if (rank < 0 || rank > star.MaxRank || rank > 0 && chosen < 0)
                throw new InvalidOperationException("Invalid allocated choice state: " + star.Id);
            try { return ChoiceDescriptionCore(star, chosen, rank, bulleted); }
            catch (Exception error) { return DescriptionFallback(star, error); }
        }

        private static string ChoiceDescriptionCore(TalentDef star, int chosen, int rank, bool bulleted)
        {
            // 二つの効果は見出しと空行で段落に分ける（#46：説明が密着して、どちらの候補か分からなくなる）。
            // 見出しの「効果 A/B」は選択パネルの列の見出しと同じ言葉なので、ツールチップとパネルが対応する。
            string text = (chosen < 0 ? Loc.T("まだ選んでいない。どちらか1つを選ぶ。", "Unselected: choose one option.")
                : Loc.T("選択中の効果：", "Chosen effect:"))
                + "\n" + ChoiceOptionHeading(0) + "\n" + ChoiceOptionLabel(star, 0, chosen, bulleted)
                + "\n\n" + ChoiceOptionHeading(1) + "\n" + ChoiceOptionLabel(star, 1, chosen, bulleted);
            // 段数で強さが変わる二択（#101）：候補本文が段数を書かない型（Mechanism）のときだけ、親星の段数・費用を添える。
            return ChoiceNeedsRankNote(star) ? text + "\n\n" + RanksNote(star) : text;
        }

        private static bool ChoiceNeedsRankNote(TalentDef star) =>
            star.MaxRank > 1 && star.Choices.Any(c => c.Mechanism != null);

        /// <summary>星図の本文の末尾に付ける段数と費用の注記（通常星と二択で同じ言葉を使う）。</summary>
        internal static string RanksNote(TalentDef star) => Loc.T(
            $"（数値は1段ごと。最大{star.MaxRank}段、1段につき{star.RankCost}ポイント）",
            $" (values per rank; maximum {star.MaxRank} {(star.MaxRank == 1 ? "rank" : "ranks")}; {star.RankCost} {(star.RankCost == 1 ? "point" : "points")} per rank)");

        private static string ChoiceOptionHeading(int option)
        {
            char letter = (char)('A' + option);
            return Loc.T($"<color=#9fe0ff><b>■ 効果 {letter}</b></color>", $"<color=#9fe0ff><b>■ Effect {letter}</b></color>");
        }

        public static string ChoiceOptionLabel(TalentDef star, int option, int chosen, bool bulleted = false)
        {
            ValidateChoice(star, chosen);
            if (option < 0 || option > 1) throw new ArgumentOutOfRangeException(nameof(option));
            TalentDef selected = star.Choices[option];
            return (option == chosen ? Loc.T("［選択中］", "[Chosen] ") : Loc.T("［未選択］", "[Unselected] "))
                + selected.Name + Loc.T("：", ": ") + (bulleted ? DisplayDescription(selected) : EffectDescription(selected));
        }

        /// <summary>
        /// 画面に出す効果。1行目に効果の要点、続けて条件・間隔・上限を「・」の箇条書きにする。
        /// 刻印と二択は、すでに段落に分かれているのでそのまま（二択は各候補だけ箇条書き）。
        /// 集計・検索・テキスト書き出しは、1行の <see cref="EffectDescription"/> を使う。
        /// </summary>
        public static string DisplayDescription(TalentDef star, int rank = 1)
        {
            try
            {
                RequireStar(star);
                if (star.IsChoice) return ChoiceDescription(star, -1, 0, true);
                string text = EffectDescription(star, rank);
                if (star.KeystoneDefinition != null || star.IsKeystone) return text;
                return EffectLayout.Bullets(text);
            }
            catch (Exception error) { return DescriptionFallback(star, error); }
        }

        /// <summary>Build on content/language changes, not inside the per-frame drawing loop.</summary>
        public static string EffectDescription(TalentDef star, int rank = 1)
        {
            try
            {
                RequireStar(star);
                if (star.IsChoice) return ChoiceDescription(star, -1, 0);
                if (star.KeystoneDefinition != null || star.IsKeystone) return KeystoneDescription(star);
                if (star.Mechanism == null && star.PairCombo != null) return PairCombos.Describe(star.PairCombo, rank);
                return star.Mechanism == null ? star.Describe() : DescribeMechanism(star.Mechanism);
            }
            catch (Exception error) { return DescriptionFallback(star, error); }
        }

        private const string NL = "\n";
        public const string BenefitHeading = "<color=#9fe0b0><b>効果</b></color>";

        /// <summary>
        /// 刻印の効果。効果と必要ポイントだけを示す（刻印にデバフは付かない）。
        /// 必要ポイントは最後の1行だけで、ツリー側の説明では繰り返さない。
        /// </summary>
        public static string KeystoneDescription(TalentDef star)
        {
            try { return KeystoneDescriptionCore(star); }
            catch (Exception error) { return DescriptionFallback(star, error); }
        }

        private static string KeystoneDescriptionCore(TalentDef star)
        {
            RequireStar(star);
            if (star.KeystoneDefinition != null)
            {
                var key = star.KeystoneDefinition;
                if (key.Cost <= 0) throw new InvalidOperationException("Invalid keystone cost: " + star.Id);
                string combinedBenefit = DescribeKeySide(key);
                if (star.PerRank > 0) combinedBenefit = DescribeKeystoneStat(star) + NL + combinedBenefit;
                return KeystoneSections(combinedBenefit, star.KeystoneCost);
            }
            if (!star.IsKeystone) throw new InvalidOperationException("Not a keystone: " + star.Id);
            if (star.PerRank > 0) return KeystoneSections(DescribeKeystoneStat(star), star.KeystoneCost);
            // 旧来の刻印：効果の本文がPowerの説明。
            string benefit = Content.FormatPower(star.Power, star.PowerValue);
            if (star.Description != null && !string.IsNullOrWhiteSpace(star.Description.ToString())) benefit += NL + star.Description;
            return KeystoneSections(benefit, Content.KeystoneCost);
        }

        private static string DescribeKeystoneStat(TalentDef star)
        {
            switch (star.Stat)
            {
                case Stat.ShieldPower: return Loc.T($"自分が与える障壁の量が{star.PerRank}%増える。", $"Increases shields you grant by {star.PerRank}%.");
                case Stat.Haste: return Loc.T($"スキル加速が{star.PerRank}増え、記憶のクールダウン回復が速くなる。", $"Gain {star.PerRank} ability haste, speeding up memory cooldown recovery.");
                case Stat.Armor: return Loc.T($"自分の防御が{star.PerRank}増える。", $"Gain {star.PerRank} armor.");
                case Stat.SummonPower: return Loc.T($"自分の召喚体が与えるダメージが{star.PerRank}%増える。", $"Your summons deal {star.PerRank}% more damage.");
                default: return Content.FormatStat(star.Stat, star.PerRank);
            }
        }

        /// <summary>効果・必要ポイントを組み立てる。</summary>
        public static string KeystoneSections(string benefit, int cost)
        {
            return Loc.T(BenefitHeading, "<color=#9fe0b0><b>Effect</b></color>") + NL + benefit
                + NL + Loc.T($"必要ポイント：{cost}", $"Cost: {cost} {(cost == 1 ? "point" : "points")}");
        }

        /// <summary>次の枠が開く星のレベルの1文。開く枠がなければ空文字。</summary>
        public static string NextSlotText(int starLevel)
        {
            int next = KeystoneSlots.NextUnlockLevel(starLevel);
            return next < 0 ? "" : Loc.T($"次の枠は星のレベル{next}で開く。", $"The next slot unlocks at star level {next}.");
        }

        /// <summary>刻印の帯に出す「刻印 n/枠数」。</summary>
        public static string KeystoneSlotStatus(int selected, int slots)
            => Loc.T($"刻印 {selected}/{slots}", $"Keystones {selected}/{slots}");

        /// <summary>選択の星の1つの選択肢を、名前と効果の本文で（選択中の印は付けない。印は画面側で色と ✓ で出す）。</summary>
        public static string ChoiceOptionBody(TalentDef star, int option, bool bulleted = false)
        {
            ValidateChoice(star, -1);
            if (option < 0 || option > 1) throw new ArgumentOutOfRangeException(nameof(option));
            try { return ChoiceOptionBodyCore(star, option, bulleted); }
            catch (Exception error) { return DescriptionFallback(star, error); }
        }

        private static string ChoiceOptionBodyCore(TalentDef star, int option, bool bulleted)
        {
            TalentDef selected = star.Choices[option];
            string body = "<b>" + selected.Name + "</b>" + NL + (bulleted ? DisplayDescription(selected) : EffectDescription(selected));
            return ChoiceNeedsRankNote(star) && selected.Mechanism != null ? body + NL + RanksNote(star) : body;
        }

        public static string PresentationLabel(TalentDef star)
        {
            try { return MechanismLabel(star); }
            catch (Exception error)
            {
                DescriptionFallback(star, error);
                return Loc.T("星の効果", "Star effect");
            }
        }

        public static string MechanismLabel(TalentDef star)
        {
            RequireStar(star);
            if (star.ClusterStar != null && !Enum.IsDefined(typeof(ClusterStarKind), star.ClusterStar.Kind))
                throw new InvalidOperationException("Unknown star kind: " + star.Id);
            if (star.Mechanism != null)
            {
                AuthoredMechanisms.Validate(star.Mechanism);
                switch (star.Mechanism.Kind)
                {
                    case AuthoredMechanismKind.Gimmick: return Loc.T("記憶の追加効果", "Additional memory effect");
                    case AuthoredMechanismKind.DirectedRecharge: return Loc.T("記憶間のクールダウン短縮", "Cooldown reduction between memories");
                    case AuthoredMechanismKind.BridgeSuccess: return Loc.T("記憶を組み合わせた追加効果", "Combined memory effect");
                    case AuthoredMechanismKind.MemoryPrimed: return Loc.T("次の通常攻撃の強化", "Next basic attack enhancement");
                    case AuthoredMechanismKind.RelayWindow: return Loc.T("次に使う記憶の強化", "Enhancement for the next memory used");
                    case AuthoredMechanismKind.SacrificeShield: return Loc.T("HP支払いから障壁へ", "HP payment converted to shield");
                    case AuthoredMechanismKind.AlliedWard: return Loc.T("味方への障壁付与", "Shield for allies");
                    case AuthoredMechanismKind.PressureDividend: return Loc.T("強化された敵からの追加報酬", "Extra rewards from strengthened enemies");
                    case AuthoredMechanismKind.StunSourceFilter: return Loc.T("記憶のスタンによる障壁", "Shield when a memory stuns an enemy");
                    case AuthoredMechanismKind.MemoryTuning: return Loc.T("記憶の挙動の変更", "Memory behavior change");
                    case AuthoredMechanismKind.IdentityStrike: return Loc.T("アイデンティティ記憶の追加攻撃", "Identity memory strike");
                    default: throw new InvalidOperationException("Unknown mechanism: " + star.Id);
                }
            }
            if (star.RunGrowth != null) return Loc.T("遠征を通して成長", "Run-long growth");
            if (star.RunGrowthModifier != null) return Loc.T("遠征の鍛錬の強化", "Expedition training enhancement");
            if (star.KeystoneDefinition != null || star.IsKeystone) return Loc.T("刻印", "Keystone");
            if (star.IsChoice) { ValidateChoice(star, -1); return Loc.T("二択の効果", "Choice of two effects"); }
            if (star.NativeModifier != null || star.ScopedModifier != null)
            {
                FractionalScopedModifiers.ValidateTalent(star);
                return star.NativeModifier != null
                    ? star.NativeModifier.Kind == LinkKind.MemoryHaste ? Loc.T("記憶のクールダウン短縮", "Memory cooldown reduction")
                        : Loc.T("記憶の与ダメージ強化", "Memory damage increase")
                    : Loc.T("星の追加効果の強化", "Star effect enhancement");
            }
            if (star.LinkPerRank != null)
            {
                if (!Links.Validate(star.LinkPerRank)) throw new InvalidOperationException("Invalid link: " + star.Id);
                return star.LinkPerRank.Kind == LinkKind.MemoryHaste ? Loc.T("記憶のクールダウン短縮", "Memory cooldown reduction")
                    : star.LinkPerRank.Kind == LinkKind.MemoryDamage ? Loc.T("記憶の与ダメージ強化", "Memory damage increase")
                    : Loc.T("記憶の連携", "Memory link");
            }
            if (star.GimmickParameter.HasValue)
            {
                switch (star.GimmickParameter.Value)
                {
                    case GimmickParam.Duration: return Loc.T("追加効果の持続時間", "Additional effect duration");
                    case GimmickParam.WindowDuration: return Loc.T("連携が成立するまでの猶予", "Time allowed to complete a combo");
                    case GimmickParam.MarkDuration: return Loc.T("連携の印の持続時間", "Combo mark duration");
                    case GimmickParam.Radius: return Loc.T("追加効果の半径", "Additional effect radius");
                    case GimmickParam.ExtraTargets: return Loc.T("追加効果の対象数", "Additional effect targets");
                    case GimmickParam.Chance: return Loc.T("追加効果の発動確率", "Additional effect probability");
                    default: throw new InvalidOperationException("Unknown gimmick parameter: " + star.Id);
                }
            }
            if (star.GimmickBoost > 0) return Loc.T("追加効果の効果量強化", "Additional effect amount increase");
            if (star.Gimmick != null)
            {
                if (!Gimmicks.ValidDef(star.Gimmick)) throw new InvalidOperationException("Invalid gimmick: " + star.Id);
                return Loc.T("記憶の追加効果", "Additional memory effect");
            }
            if (star.IsPowerNode)
            {
                if (!Enum.IsDefined(typeof(Power), star.RankPower)) throw new InvalidOperationException("Unknown power: " + star.Id);
                return Content.PowerName(star.RankPower);
            }
            if (!Enum.IsDefined(typeof(Stat), star.Stat)) throw new InvalidOperationException("Unknown stat: " + star.Id);
            return Loc.T("能力値", "Stat"); // 値は説明文に出る。ここで繰り返さない。
        }

        private static void ValidateChoice(TalentDef star, int chosen)
        {
            RequireStar(star);
            if (!star.IsChoice || star.Choices == null || star.Choices.Count != 2
                || star.Choices[0]?.Name == null || star.Choices[1]?.Name == null || chosen < -1 || chosen > 1)
                throw new InvalidOperationException("Invalid two-option choice: " + star.Id);
        }

        internal static string DescribeKeySide(KeystoneDefinition key)
        {
            // 英語は従来どおり1項目1行。日本語は1つの段落の文章にする。
            var lines = new List<string>();
            bool hasScale = false;
            if (key.RetainedPower != Power.None)
                lines.Add(Content.FormatPower(key.RetainedPower, key.RetainedPowerValue));
            if (CoopKeystones.TryKind(key.KeystoneId, out _)) lines.Add(CoopKeystones.Describe(key));
            foreach (var transform in key.Upside)
            {
                if (transform.Scope.PayloadKind == KeystonePayloadKind.Coop) continue;
                string effect = KeystoneEffectText(transform);
                lines.Add(Loc.Japanese ? KeystoneChangeJa(transform, effect) : effect + ": " + KeystoneChangeText(transform));
                var scope = transform.Scope;
                var targets = new List<string>();
                targets.AddRange(scope.TargetMemorySet.Select(Links.ItemName));
                targets.AddRange(scope.SourceSelectors.Select(SelectorText));
                targets.AddRange(scope.TargetEffectIds.Select(StarName));
                lines.Add(Loc.T("対象は" + (targets.Count == 0
                    ? "自分が取得した星による該当効果のすべて。"
                    : string.Join("と", targets) + "による該当効果。")
                    + (transform.TargetLayer == KeystoneLayer.NativeDamage
                        ? "記憶が元々与えるダメージだけを変更し、星による追加ダメージは変更しない。"
                        : transform.TargetLayer == KeystoneLayer.GeneratedDamage
                            ? "星による追加ダメージだけを変更し、記憶の元々のダメージは変更しない。"
                            : ""),
                    "Applies to: " + (targets.Count == 0
                    ? "all matching effects from stars you own."
                    : string.Join(", ", targets) + ".")
                    + (transform.TargetLayer == KeystoneLayer.NativeDamage
                        ? " Changes only the memory's original damage, not star-generated extra damage."
                        : transform.TargetLayer == KeystoneLayer.GeneratedDamage
                            ? " Changes only star-generated extra damage, not the memory's original damage."
                            : "")));
                if (scope.ReceiverMemorySet.Count > 0 || scope.ReceiverSelectors.Count > 0)
                {
                    var receivers = scope.ReceiverMemorySet.Select(Links.ItemName).Concat(scope.ReceiverSelectors.Select(SelectorText)).ToList();
                    lines.Add(Loc.T("効果の受け手は" + string.Join("と", receivers) + "。",
                        "Effect recipient: " + string.Join(", ", receivers) + "."));
                }
                if (scope.Recipient != KeystoneRecipientKind.Any)
                    lines.Add(Loc.Japanese
                        ? EnumText(scope.Recipient, "", "変更されるのは自分への効果だけ。", "変更されるのは味方の旅人への効果だけで、自分への効果は変わらない。", "変更されるのは自分の召喚獣への効果だけ。")
                        : "Changes the effect on: " + EnumText(scope.Recipient, "everyone", "yourself", "allied travelers only (your own effect is unchanged)", "your summons") + ".");
                if (scope.SourceKind.HasValue)
                    lines.Add(Loc.Japanese
                        ? EnumText(scope.SourceKind.Value, "発動元は、記憶が元々持つ効果に限る。", "発動元は自分の通常攻撃に限る。", "発動元は自分の召喚獣に限る。", "発動元は星による追加効果に限る。", "発動元は自分の移動に限る。")
                        : "Source: " + EnumText(scope.SourceKind.Value, "the memory's original effect", "your basic attacks", "your summons", "star-generated extra effects", "your displacement") + ".");
                if (scope.Argument.HasValue)
                    lines.Add(scope.TargetEffectSet.Contains(GimmickEffect.Heal)
                        ? (scope.Argument.Value == 1
                            ? Loc.T("対象は自分だけでなく近くの味方旅人も回復する効果に限る。", "Applies only to effects that already heal nearby allied travelers as well as yourself.")
                            : scope.Argument.Value == 0 ? Loc.T("対象は自分だけを回復する効果に限る。", "Applies only to effects that heal yourself alone.")
                                : throw new InvalidOperationException("Unmapped healing condition."))
                        : KeystoneArgumentText(scope.TargetEffectSet, scope.Argument.Value));
                if (scope.TargetEffectSet.Contains(GimmickEffect.Wound) && transform.Field == KeystoneField.Duration)
                    lines.Add(Loc.T("毎秒のダメージは変わらず、持続が長くなる分だけ総ダメージが増える。総量の上限は変わらない。",
                        "Damage per second is unchanged; longer duration increases total damage, subject to the existing total cap."));
                if (transform.Operation == KeystoneOperation.Scale) hasScale = true;
            }
            if (hasScale)
                lines.Add(Loc.T("倍率は対象の現在の値に掛けるもので、%を足すわけではない。各効果の上限は変わらない。",
                    "Multipliers apply to the current value, not as added percentage points. Existing effect caps remain unchanged."));
            foreach (var grant in key.Grants)
                lines.Add(DescribeMechanism(grant));
            if (key.RequiredMemories.Count > 0)
                lines.Add(RequiredEquipText(key.RequiredMemories));
            if (key.Prerequisites.Count > 0)
                lines.Add(Loc.T("取得するには" + string.Join("と", key.Prerequisites.Select(StarName)) + "を" + (key.Prerequisites.Count > 1 ? "すべて" : "") + "取得している必要がある。",
                    "Acquisition prerequisites (all required): " + string.Join(", ", key.Prerequisites.Select(StarName))));
            return Loc.Japanese ? string.Concat(lines.Where(line => line.Length > 0).Select(EndSentence)) : string.Join("\n", lines);
        }

        private static string KeystoneEffectText(KeystoneTransform transform)
        {
            var scope = transform.Scope;
            if (transform.TargetLayer == KeystoneLayer.NativeDamage) return Loc.T("記憶の元々のダメージ", "Memory original damage");
            if (transform.TargetLayer == KeystoneLayer.StarMemoryDamage) return Loc.T("星による記憶のダメージ増加量", "Memory damage bonus from stars");
            if (scope.TargetEffectSet.Count > 0)
                return string.Join(Loc.T("・", " / "), scope.TargetEffectSet.Select(EffectText));
            switch (scope.PayloadKind)
            {
                case KeystonePayloadKind.DirectedRecharge: return Loc.T("受け手の残りクールダウン短縮量", "Recipient remaining cooldown reduction");
                case KeystonePayloadKind.MemoryPrimed: return Loc.T("次の通常攻撃の追加ダメージ", "Next basic attack extra damage");
                case KeystonePayloadKind.RelayWindow: return Loc.T("記憶の直接ダメージ増加量", "Memory direct damage bonus");
                case KeystonePayloadKind.SacrificeShield: return Loc.T("HP支払いによる障壁量", "Shield amount from HP payment");
                case KeystonePayloadKind.AlliedWard: return scope.Recipient == KeystoneRecipientKind.OwnedSummon
                    ? Loc.T("自分の召喚獣の障壁量", "Shield amount for your summons") : Loc.T("自分・味方の障壁量", "Shield amount for yourself and allies");
                case KeystonePayloadKind.PressureDividend: return Loc.T("未確保の欠片の獲得確率", "Chance to gain an unsecured shard");
                default: throw new InvalidOperationException("Unmapped keystone effect.");
            }
        }

        /// <summary>刻印が効果をどう変えるかの1文（日本語）。効果名を主語にして、「…が増える」「…に変更する」の形にする。</summary>
        private static string KeystoneChangeJa(KeystoneTransform transform, string effect)
        {
            if (transform.Field == KeystoneField.Argument && transform.Scope.TargetEffectSet.Contains(GimmickEffect.Heal))
                return KeystoneArgumentText(transform.Scope.TargetEffectSet, checked((int)transform.Seconds));
            string field = EnumText(transform.Field, "量", "持続時間", "半径", "追加効果が発生するまでの時間", "対象数", "発動に必要な回数", "発動確率", "対象");
            if (transform.Field == KeystoneField.Argument && transform.Scope.TargetEffectSet.Contains(GimmickEffect.Ricochet))
                field = "追加ダメージを受ける別の敵の数";
            // 効果名がすでに「量」を指すとき（値そのものの変更）は項目名を省く。
            string subject = transform.Field == KeystoneField.Value ? effect : effect + "の" + field;
            string result;
            switch (transform.Operation)
            {
                case KeystoneOperation.Scale:
                    bool raise = transform.MagnitudeUnits.Units > 0;
                    string verb = transform.Field == KeystoneField.Probability ? (raise ? "上がり" : "下がり") : (raise ? "増え" : "減り");
                    result = subject + "が" + Number(Math.Abs(transform.MagnitudeUnits.Percent)) + "%" + verb + "、元の" + Number(transform.MagnitudeUnits.Multiplier) + "倍になる。";
                    break;
                case KeystoneOperation.SetSeconds:
                    result = subject + "を" + Number(transform.Seconds) + "秒に変更する。"; break;
                case KeystoneOperation.AddTargets:
                    result = subject + "が" + Math.Abs(transform.Count) + "体" + (transform.Count > 0 ? "増える" : "減る") + "。"; break;
                case KeystoneOperation.SetEveryN:
                    result = effect + "は条件を満たす" + transform.Count + "回ごとに発動する。"; break;
                case KeystoneOperation.Set:
                    result = subject + "を" + (transform.ExpectedFrom.HasValue ? KeystoneNumericText(transform, transform.ExpectedFrom.Value) + "から" : "")
                        + KeystoneNumericText(transform, transform.Seconds) + "に変更する。"; break;
                case KeystoneOperation.Add:
                    bool grow = transform.Seconds > 0;
                    string change = transform.Field == KeystoneField.Duration || transform.Field == KeystoneField.Delay ? (grow ? "長くなる" : "短くなる")
                        : transform.Field == KeystoneField.Probability ? (grow ? "上がる" : "下がる") : (grow ? "増える" : "減る");
                    string amount = KeystoneNumericText(transform, Math.Abs(transform.Seconds));
                    result = subject + "が" + amount + (amount.StartsWith("最大HPの", StringComparison.Ordinal) ? "分" : "") + change + "。"; break;
                default: throw new InvalidOperationException("Unmapped keystone change.");
            }
            if (transform.Maximum.HasValue)
                result += "上限は" + KeystoneNumericText(transform, transform.Maximum.Value) + "まで。";
            return result;
        }

        private static string KeystoneChangeText(KeystoneTransform transform)
        {
            string field = EnumText(transform.Field, "amount", "duration", "radius", "delay before the extra effect", "target count", "qualifying events needed", "trigger chance", "targets");
            if (transform.Field == KeystoneField.Argument && transform.Scope.TargetEffectSet.Contains(GimmickEffect.Heal))
                return KeystoneArgumentText(transform.Scope.TargetEffectSet, checked((int)transform.Seconds));
            if (transform.Field == KeystoneField.Argument && transform.Scope.TargetEffectSet.Contains(GimmickEffect.Ricochet))
                field = "number of other enemies receiving extra damage";
            string result;
            switch (transform.Operation)
            {
                case KeystoneOperation.Scale:
                    result = FieldLead(transform, field) + (transform.MagnitudeUnits.Units > 0 ? "+" : "") + Number(transform.MagnitudeUnits.Percent) + "%"
                        + " (×" + Number(transform.MagnitudeUnits.Multiplier) + ").";
                    break;
                case KeystoneOperation.SetSeconds:
                    result = field + " → " + Number(transform.Seconds) + "s."; break;
                case KeystoneOperation.AddTargets:
                    result = FieldLead(transform, field) + (transform.Count > 0 ? "+" : "") + transform.Count + " targets."; break;
                case KeystoneOperation.SetEveryN:
                    result = "Every " + transform.Count + " qualifying events."; break;
                case KeystoneOperation.Set:
                    result = FieldLead(transform, field) + (transform.ExpectedFrom.HasValue ? KeystoneNumericText(transform, transform.ExpectedFrom.Value) + " → "
                            : transform.Field == KeystoneField.Value ? "Set to " : "→ ") + KeystoneNumericText(transform, transform.Seconds) + "."; break;
                case KeystoneOperation.Add:
                    result = FieldLead(transform, field) + (transform.Seconds > 0 ? "+" : "") + KeystoneNumericText(transform, transform.Seconds) + "."; break;
                default: throw new InvalidOperationException("Unmapped keystone change.");
            }
            if (transform.Maximum.HasValue)
                result += " Cap: " + KeystoneNumericText(transform, transform.Maximum.Value) + ".";
            return result;
        }

        /// <summary>変更する項目名。効果名がすでに「量」を指すとき（値そのものの変更）は項目名を省く。</summary>
        private static string FieldLead(KeystoneTransform transform, string field) =>
            transform.Field == KeystoneField.Value ? "" : field + " ";

        private static string KeystoneNumericText(KeystoneTransform transform, decimal value)
        {
            switch (transform.Field)
            {
                case KeystoneField.Duration: case KeystoneField.Delay: return Number(value) + Loc.T("秒", "s");
                case KeystoneField.Radius: return Number(value) + "m";
                case KeystoneField.TargetCount: case KeystoneField.Argument: return Number(value) + Loc.T("体", " targets");
                case KeystoneField.EveryN: return Number(value) + Loc.T("回", " events");
                case KeystoneField.Probability: return Number(value) + "%";
                case KeystoneField.Value:
                    if (transform.Scope.TargetEffectSet.Contains(GimmickEffect.Rampart) || transform.Scope.TargetEffectSet.Contains(GimmickEffect.Shield))
                        return Loc.T("最大HPの", "") + Number(value) + Loc.T("%", "% maximum HP");
                    return Number(value) + "%";
                default: throw new InvalidOperationException("Unmapped keystone number.");
            }
        }

        private static string KeystoneArgumentText(IReadOnlyList<GimmickEffect> effects, int argument)
        {
            if (effects.Contains(GimmickEffect.Heal))
                return argument == 1 ? Loc.T("回復の対象に、近くの味方旅人が加わる。", "Healing also includes nearby allied travelers.")
                    : argument == 0 ? Loc.T("回復の対象は自分だけになる。", "Healing affects only yourself.") : throw new InvalidOperationException("Unmapped healing target.");
            if (effects.Contains(GimmickEffect.Ricochet))
                return Loc.T("追加ダメージの対象は近くの別の敵最大" + argument + "体になる。", "Extra damage targets: up to " + argument + " other nearby enemies.");
            if (effects.Contains(GimmickEffect.Element))
                return Loc.Japanese
                    ? "付与する属性は" + EnumText((IdentityStrikeElement)(argument + 1), "", "火", "冷気", "光", "闇") + "になる。"
                    : "Element applied: " + EnumText((IdentityStrikeElement)(argument + 1), "", "Fire", "Cold", "Light", "Dark") + ".";
            throw new InvalidOperationException("Unmapped keystone target.");
        }

        private static string EnumText<T>(T value, bool japanese, string[] ja, string[] en) where T : struct
            => EnumText(value, japanese ? ja : en);

        private static string EnumText<T>(T value, params string[] names) where T : struct
        {
            int index = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (!Enum.IsDefined(typeof(T), value) || index < 0 || index >= names.Length)
                throw new InvalidOperationException("Unmapped presentation value: " + value);
            return names[index];
        }

        private static string SelectorText(MemorySelector selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (selector.Alternatives.Count > 0)
                return string.Join(Loc.T("または", " or "), selector.Alternatives.Select(SelectorText));
            string text;
            switch (selector.Kind)
            {
                case MemorySelectorKind.Memory: return Links.ItemName(selector.Memory);
                case MemorySelectorKind.EquippedQ: text = Loc.T("装備中のQの記憶", "equipped Q memory"); break;
                case MemorySelectorKind.EquippedR: text = Loc.T("装備中のRの記憶", "equipped R memory"); break;
                case MemorySelectorKind.EquippedIdentity: text = Loc.T("装備中の固有記憶", "equipped identity memory"); break;
                case MemorySelectorKind.EquippedQOrR: text = Loc.T("装備中のQまたはRの記憶", "equipped Q or R memory"); break;
                case MemorySelectorKind.EquippedMovement: text = Loc.T("装備中の移動記憶", "equipped movement memory"); break;
                case MemorySelectorKind.OtherNormal: text = Loc.T("ほかの装備中の通常記憶（移動・奥義・固有記憶を除く）", "other equipped normal memories (excluding Movement, Ultimate and Identity)"); break;
                default: throw new InvalidOperationException("Unknown memory selector: " + selector.Kind);
            }
            return text + (selector.AllowedMemories.Count == 0 ? "" : Loc.T("（対象は", " (limited to ")
                + string.Join(Loc.T("、", ", "), selector.AllowedMemories.Select(Links.ItemName)) + Loc.T("のみ）", ")"));
        }

        private static void RequireStar(TalentDef star)
        {
            if (star == null) throw new ArgumentNullException(nameof(star));
        }
    }
}

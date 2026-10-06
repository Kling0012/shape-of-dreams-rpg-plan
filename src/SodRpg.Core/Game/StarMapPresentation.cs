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
            string text = (chosen < 0 ? Loc.T("未選択：どちらか1つを選んでください。", "Unselected: choose one option.")
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
            $"（数値は1段あたり・最大{star.MaxRank}段・1段につき{star.RankCost}ポイント）",
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
                return KeystoneSections(DescribeKeySide(key), key.Cost);
            }
            if (!star.IsKeystone) throw new InvalidOperationException("Not a keystone: " + star.Id);
            // 旧来の刻印：効果の本文がPowerの説明。
            string benefit = Content.FormatPower(star.Power, star.PowerValue);
            if (star.Description != null && !string.IsNullOrWhiteSpace(star.Description.ToString())) benefit += NL + star.Description;
            return KeystoneSections(benefit, Content.KeystoneCost);
        }

        /// <summary>効果・必要ポイントを組み立てる。</summary>
        public static string KeystoneSections(string benefit, int cost)
        {
            return Loc.T(BenefitHeading, "<color=#9fe0b0><b>Effect</b></color>") + NL + benefit
                + NL + Loc.T($"必要ポイント：{cost}", $"Cost: {cost} {(cost == 1 ? "point" : "points")}");
        }

        /// <summary>
        /// 刻印の取得条件を、実際の数字つきの1文にする。
        /// 例：「この旅人の星を合計6段取得し、熟練度3に達する必要があります（今は星3段・熟練度10）。」
        /// <paramref name="masteryNeeded"/> が 0 なら熟練度は条件にしない（汎用ツリーの刻印）。
        /// slots/starLevel にはこの旅人の刻印の枠数と星のレベルを渡し、選べる数と次の枠の条件も添える。
        /// </summary>
        public static string KeystoneRequirement(bool heroTree, int ranksNeeded, int ranksHave, int masteryNeeded, int masteryHave, bool connected,
            int slots = 1, int starLevel = 0)
        {
            string scopeJa = heroTree ? "この旅人の星" : "同じ系統の星", scopeEn = heroTree ? "this Traveler's stars" : "stars of the same line";
            string ja = $"{scopeJa}を合計{ranksNeeded}段取得" + (masteryNeeded > 0 ? $"し、熟練度{masteryNeeded}に達する" : "する")
                + "必要があります（今は星" + ranksHave + "段" + (masteryNeeded > 0 ? "・熟練度" + masteryHave : "") + "）。";
            string en = $"Acquire {ranksNeeded} ranks of {scopeEn}" + (masteryNeeded > 0 ? $" and reach mastery {masteryNeeded}" : "")
                + " (now: " + ranksHave + " ranks" + (masteryNeeded > 0 ? ", mastery " + masteryHave : "") + ").";
            string text = Loc.T(ja, en);
            if (!connected) text += Loc.T("さらに、取得済みの星と線でつながっている必要があります。", " It must also connect to your acquired stars.");
            return Loc.T($"刻印は星のレベルに応じて最大{slots}つまで選べます。", $"Up to {slots} keystones can be selected, depending on the star level. ") + text
                + NextSlotText(starLevel);
        }

        /// <summary>次の枠が開く星のレベルの1文。開く枠がなければ空文字。</summary>
        public static string NextSlotText(int starLevel)
        {
            int next = KeystoneSlots.NextUnlockLevel(starLevel);
            return next < 0 ? "" : Loc.T($"次の枠は星のレベル{next}で開きます。", $"The next slot unlocks at star level {next}.");
        }

        /// <summary>刻印の帯に出す「刻印 n/枠数」。</summary>
        public static string KeystoneSlotStatus(int selected, int slots)
            => Loc.T($"刻印 {selected}/{slots}", $"Keystones {selected}/{slots}");

        /// <summary>
        /// 星の状態を1文で。条件の説明と「振れます」を重ねて出さないよう、状態はここで1つだけ決める。
        /// </summary>
        public static string AllocationStatus(bool keystone, bool maxed, bool unlocked, bool enoughPoints)
        {
            if (maxed) return keystone ? Loc.T("選択中です。", "Selected.") : Loc.T("最大段です。", "Maximum rank.");
            if (!unlocked)
                return keystone ? Loc.T("まだ条件を満たしていません。", "Requirements not yet met.")
                    : Loc.T("取得済みの星と線でつながると振れます。", "Requires a connection to an acquired star.");
            if (!enoughPoints) return Loc.T("ポイントが足りません。", "Not enough points.");
            return keystone ? Loc.T("選べます。", "Available to select.") : Loc.T("振れます。", "Available.");
        }

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
            var lines = new List<string>();
            bool hasScale = false;
            if (key.RetainedPower != Power.None)
                lines.Add(Content.FormatPower(key.RetainedPower, key.RetainedPowerValue));
            foreach (var transform in key.Upside)
            {
                string effect = KeystoneEffectText(transform);
                lines.Add(effect + Loc.T("：", ": ") + KeystoneChangeText(transform));
                var scope = transform.Scope;
                var targets = new List<string>();
                targets.AddRange(scope.TargetMemorySet.Select(Links.ItemName));
                targets.AddRange(scope.SourceSelectors.Select(SelectorText));
                targets.AddRange(scope.TargetEffectIds.Select(StarName));
                lines.Add(Loc.T("対象：", "Applies to: ") + (targets.Count == 0
                    ? Loc.T("自分が取得した星による該当効果すべて。", "all matching effects from stars you own.")
                    : string.Join(Loc.T("、", ", "), targets) + Loc.T("による該当効果。", "."))
                    + (transform.TargetLayer == KeystoneLayer.NativeDamage
                        ? Loc.T("記憶が元々与えるダメージだけを変更し、星による追加ダメージは変更しない。", " Changes only the memory's original damage, not star-generated extra damage.")
                        : transform.TargetLayer == KeystoneLayer.GeneratedDamage
                            ? Loc.T("星による追加ダメージだけを変更し、記憶の元々のダメージは変更しない。", " Changes only star-generated extra damage, not the memory's original damage.")
                            : ""));
                if (scope.ReceiverMemorySet.Count > 0 || scope.ReceiverSelectors.Count > 0)
                    lines.Add(Loc.T("効果の受け手：", "Effect recipient: ")
                        + string.Join(Loc.T("、", ", "), scope.ReceiverMemorySet.Select(Links.ItemName).Concat(scope.ReceiverSelectors.Select(SelectorText))) + Loc.T("。", "."));
                if (scope.Recipient != KeystoneRecipientKind.Any)
                    lines.Add(Loc.T("変更する対象：", "Changes the effect on: ") + EnumText(scope.Recipient, Loc.Japanese,
                        new[] { "すべて", "自分", "味方の旅人だけ（自分への効果は変わらない）", "自分の召喚獣" },
                        new[] { "everyone", "yourself", "allied travelers only (your own effect is unchanged)", "your summons" }) + Loc.T("。", "."));
                if (scope.SourceKind.HasValue)
                    lines.Add(Loc.T("発動元：", "Source: ") + EnumText(scope.SourceKind.Value, Loc.Japanese,
                        new[] { "記憶が元々持つ効果", "自分の通常攻撃", "自分の召喚獣", "星による追加効果", "自分の移動" },
                        new[] { "the memory's original effect", "your basic attacks", "your summons", "star-generated extra effects", "your displacement" }) + Loc.T("。", "."));
                if (scope.Argument.HasValue)
                    lines.Add(scope.TargetEffectSet.Contains(GimmickEffect.Heal)
                        ? (scope.Argument.Value == 1
                            ? Loc.T("対象は、自分だけでなく近くの味方旅人も回復する効果に限る。", "Applies only to effects that already heal nearby allied travelers as well as yourself.")
                            : scope.Argument.Value == 0 ? Loc.T("対象は自分だけを回復する効果に限る。", "Applies only to effects that heal yourself alone.")
                                : throw new InvalidOperationException("Unmapped healing condition."))
                        : KeystoneArgumentText(scope.TargetEffectSet, scope.Argument.Value));
                if (scope.TargetEffectSet.Contains(GimmickEffect.Wound) && transform.Field == KeystoneField.Duration)
                    lines.Add(Loc.T("毎秒のダメージは変わらず、持続が長くなる分だけ総ダメージが増える（総量上限は維持）。",
                        "Damage per second is unchanged; longer duration increases total damage, subject to the existing total cap."));
                if (transform.Operation == KeystoneOperation.Scale) hasScale = true;
            }
            if (hasScale)
                lines.Add(Loc.T("倍率は対象の現在の値に掛ける（パーセントポイントの加算ではない）。各効果の上限は変わらない。",
                    "Multipliers apply to the current value, not as added percentage points. Existing effect caps remain unchanged."));
            foreach (var grant in key.Grants)
                lines.Add(DescribeMechanism(grant));
            if (key.RequiredMemories.Count > 0)
                lines.Add(Loc.T("すべて装備が必要：", "All must be equipped: ") + string.Join(Loc.T("、", ", "), key.RequiredMemories.Select(Links.ItemName)));
            if (key.Prerequisites.Count > 0)
                lines.Add(Loc.T("取得の前提（すべて必要）：", "Acquisition prerequisites (all required): ") + string.Join(Loc.T("、", ", "), key.Prerequisites.Select(StarName)));
            return string.Join("\n", lines);
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

        private static string KeystoneChangeText(KeystoneTransform transform)
        {
            string field = EnumText(transform.Field, Loc.Japanese,
                new[] { "量", "持続時間", "半径", "追加効果が発生するまでの時間", "対象数", "発動に必要な回数", "発動確率", "対象" },
                new[] { "amount", "duration", "radius", "delay before the extra effect", "target count", "qualifying events needed", "trigger chance", "targets" });
            if (transform.Field == KeystoneField.Argument && transform.Scope.TargetEffectSet.Contains(GimmickEffect.Heal))
                return KeystoneArgumentText(transform.Scope.TargetEffectSet, checked((int)transform.Seconds));
            if (transform.Field == KeystoneField.Argument && transform.Scope.TargetEffectSet.Contains(GimmickEffect.Ricochet))
                field = Loc.T("追加ダメージを受ける別の敵の数", "number of other enemies receiving extra damage");
            string result;
            switch (transform.Operation)
            {
                case KeystoneOperation.Scale:
                    result = FieldLead(transform, field) + (transform.MagnitudeUnits.Units > 0 ? "+" : "") + Number(transform.MagnitudeUnits.Percent) + "%"
                        + Loc.T("（", " (×") + (Loc.Japanese ? "元の" : "") + Number(transform.MagnitudeUnits.Multiplier) + Loc.T("倍）。", ").");
                    break;
                case KeystoneOperation.SetSeconds:
                    result = field + Loc.T("を", " → ") + Number(transform.Seconds) + Loc.T("秒に変更。", "s."); break;
                case KeystoneOperation.AddTargets:
                    result = FieldLead(transform, field) + (transform.Count > 0 ? "+" : "") + transform.Count + Loc.T("体。", " targets."); break;
                case KeystoneOperation.SetEveryN:
                    result = Loc.T("条件を満たす", "Every ") + transform.Count + Loc.T("回ごとに発動。", " qualifying events."); break;
                case KeystoneOperation.Set:
                    result = FieldLead(transform, field) + (transform.ExpectedFrom.HasValue ? KeystoneNumericText(transform, transform.ExpectedFrom.Value) + " → "
                            : transform.Field == KeystoneField.Value ? Loc.T("", "Set to ") : "→ ") + KeystoneNumericText(transform, transform.Seconds) + Loc.T("に変更。", "."); break;
                case KeystoneOperation.Add:
                    result = FieldLead(transform, field) + (transform.Seconds > 0 ? "+" : "") + KeystoneNumericText(transform, transform.Seconds) + Loc.T("。", "."); break;
                default: throw new InvalidOperationException("Unmapped keystone change.");
            }
            if (transform.Maximum.HasValue)
                result += Loc.T("上限：", " Cap: ") + KeystoneNumericText(transform, transform.Maximum.Value) + Loc.T("。", ".");
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
                return argument == 1 ? Loc.T("回復対象に近くの味方旅人を含める。", "Healing also includes nearby allied travelers.")
                    : argument == 0 ? Loc.T("回復対象は自分だけ。", "Healing affects only yourself.") : throw new InvalidOperationException("Unmapped healing target.");
            if (effects.Contains(GimmickEffect.Ricochet))
                return Loc.T("追加ダメージの対象：近くの別の敵", "Extra damage targets: up to ") + argument + Loc.T("体まで。", " other nearby enemies.");
            if (effects.Contains(GimmickEffect.Element))
                return Loc.T("付与する属性：", "Element applied: ") + EnumText((IdentityStrikeElement)(argument + 1), Loc.Japanese,
                    new[] { "", "火", "冷気", "光", "闇" }, new[] { "", "Fire", "Cold", "Light", "Dark" }) + Loc.T("。", ".");
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

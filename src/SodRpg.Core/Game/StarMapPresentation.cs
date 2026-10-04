using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>Localized star-map text, independent of the host UI.</summary>
    public static partial class StarMapPresentation
    {
        public static string ChoiceDescription(TalentDef star, int chosen, int rank)
        {
            ValidateChoice(star, chosen);
            if (rank < 0 || rank > star.MaxRank || rank > 0 && chosen < 0)
                throw new InvalidOperationException("Invalid allocated choice state: " + star.Id);
            // 二つの効果は見出しと空行で段落に分ける（#46：説明が密着して、どちらの候補か分からなくなる）。
            // 見出しの「効果 A/B」は選択パネルの列の見出しと同じ言葉なので、ツールチップとパネルが対応する。
            return (chosen < 0 ? Loc.T("未選択：どちらか1つを選んでください。", "Unselected: choose one option.")
                : Loc.T("選択中の効果：", "Chosen effect:"))
                + "\n" + ChoiceOptionHeading(0) + "\n" + ChoiceOptionLabel(star, 0, chosen)
                + "\n\n" + ChoiceOptionHeading(1) + "\n" + ChoiceOptionLabel(star, 1, chosen);
        }

        private static string ChoiceOptionHeading(int option)
        {
            char letter = (char)('A' + option);
            return Loc.T($"<color=#9fe0ff><b>■ 効果 {letter}</b></color>", $"<color=#9fe0ff><b>■ Effect {letter}</b></color>");
        }

        public static string ChoiceOptionLabel(TalentDef star, int option, int chosen)
        {
            ValidateChoice(star, chosen);
            if (option < 0 || option > 1) throw new ArgumentOutOfRangeException(nameof(option));
            TalentDef selected = star.Choices[option];
            return (option == chosen ? Loc.T("［選択中］", "[Chosen] ") : Loc.T("［未選択］", "[Unselected] "))
                + selected.Name + Loc.T("：", ": ") + EffectDescription(selected);
        }

        /// <summary>Build on content/language changes, not inside the per-frame drawing loop.</summary>
        public static string EffectDescription(TalentDef star)
        {
            RequireStar(star);
            if (star.IsChoice) return ChoiceDescription(star, -1, 0);
            if (star.KeystoneDefinition != null || star.IsKeystone) return KeystoneDescription(star);
            return star.Mechanism == null ? star.Describe() : DescribeMechanism(star.Mechanism);
        }

        private const string NL = "\n";
        public const string BenefitHeading = "<color=#9fe0b0><b>効果</b></color>";

        /// <summary>
        /// 刻印の効果。効果と必要ポイントだけを示す（刻印にデバフは付かない）。
        /// 必要ポイントは最後の1行だけで、ツリー側の説明では繰り返さない。
        /// </summary>
        public static string KeystoneDescription(TalentDef star)
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
                + NL + Loc.T($"必要ポイント：{cost}", $"Cost: {cost} points");
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
        public static string ChoiceOptionBody(TalentDef star, int option)
        {
            ValidateChoice(star, -1);
            if (option < 0 || option > 1) throw new ArgumentOutOfRangeException(nameof(option));
            TalentDef selected = star.Choices[option];
            return "<b>" + selected.Name + "</b>" + NL + EffectDescription(selected);
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
                    case AuthoredMechanismKind.Gimmick: return Loc.T("記憶の仕掛け", "Memory gimmick");
                    case AuthoredMechanismKind.DirectedRecharge: return Loc.T("指定記憶のクールダウン短縮", "Directed cooldown reduction");
                    case AuthoredMechanismKind.BridgeSuccess: return Loc.T("橋の成功効果", "Bridge success payoff");
                    case AuthoredMechanismKind.MemoryPrimed: return Loc.T("次の通常攻撃の強化", "Next basic attack enhancement");
                    case AuthoredMechanismKind.RelayWindow: return Loc.T("記憶への引継ぎ", "Memory relay window");
                    case AuthoredMechanismKind.SacrificeShield: return Loc.T("HP支払いから障壁へ", "HP payment converted to shield");
                    case AuthoredMechanismKind.AlliedWard: return Loc.T("味方への障壁付与", "Allied shield ward");
                    case AuthoredMechanismKind.PressureDividend: return Loc.T("敵の強化に応じた追加報酬", "Pressure dividend");
                    case AuthoredMechanismKind.StunSourceFilter: return Loc.T("記憶のスタンによる障壁", "Native memory stun shield");
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
                    ? star.NativeModifier.Kind == LinkKind.MemoryHaste ? Loc.T("指定記憶のクールダウン短縮", "Native memory cooldown reduction")
                        : Loc.T("指定記憶のダメージ強化", "Native memory damage enhancement")
                    : Loc.T("対象限定の効果強化", "Scoped effect enhancement");
            }
            if (star.LinkPerRank != null)
            {
                if (!Links.Validate(star.LinkPerRank)) throw new InvalidOperationException("Invalid link: " + star.Id);
                return star.LinkPerRank.Kind == LinkKind.MemoryHaste ? Loc.T("指定記憶のクールダウン短縮", "Native memory cooldown reduction")
                    : star.LinkPerRank.Kind == LinkKind.MemoryDamage ? Loc.T("指定記憶のダメージ強化", "Native memory damage enhancement")
                    : Loc.T("記憶の連携", "Memory link");
            }
            if (star.GimmickParameter.HasValue)
            {
                switch (star.GimmickParameter.Value)
                {
                    case GimmickParam.Duration: return Loc.T("仕掛けの持続時間", "Gimmick duration");
                    case GimmickParam.WindowDuration: return Loc.T("橋の受付時間", "Bridge window duration");
                    case GimmickParam.MarkDuration: return Loc.T("橋の印の持続時間", "Bridge mark duration");
                    case GimmickParam.Radius: return Loc.T("仕掛けの効果半径", "Gimmick radius");
                    case GimmickParam.ExtraTargets: return Loc.T("仕掛けの追加対象", "Gimmick additional targets");
                    case GimmickParam.Chance: return Loc.T("仕掛けの属性追加確率", "Gimmick extra-element chance");
                    default: throw new InvalidOperationException("Unknown gimmick parameter: " + star.Id);
                }
            }
            if (star.GimmickBoost > 0) return Loc.T("仕掛けの効果量強化", "Gimmick effect enhancement");
            if (star.Gimmick != null)
            {
                if (!Gimmicks.ValidDef(star.Gimmick)) throw new InvalidOperationException("Invalid gimmick: " + star.Id);
                return Loc.T("記憶の仕掛け", "Memory gimmick");
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
            if (key.RetainedPower != Power.None)
                lines.Add(Loc.T("既存の刻印効果を維持：", "Keeps its existing effect: ") + Content.FormatPower(key.RetainedPower, key.RetainedPowerValue));
            foreach (var transform in key.Upside)
            {
                string layer = EnumText(transform.TargetLayer, Loc.Japanese, new[] { "記憶固有のダメージ", "星による記憶ダメージ", "追加生成ダメージ", "仕掛けの効果" }, new[] { "native memory damage", "star memory damage", "generated damage", "gimmick effect" });
                string field = EnumText(transform.Field, Loc.Japanese, new[] { "効果量", "持続時間", "効果半径", "発生までの時間", "対象数", "発火間隔", "確率", "効果の種類" }, new[] { "effect value", "duration", "radius", "delay", "target count", "interval", "chance", "effect type" });
                string operation = EnumText(transform.Operation, Loc.Japanese, new[] { "増減率", "秒数", "追加数", "発動に必要な回数", "設定値", "加算量" }, new[] { "scale", "seconds", "extra count", "required count", "set value", "additive amount" });
                var scope = transform.Scope;
                var targets = new List<string>();
                targets.AddRange(scope.TargetMemorySet.Select(m => Links.Name(m).ToString()));
                targets.AddRange(scope.TargetEffectSet.Select(EffectText));
                targets.AddRange(scope.TargetEffectIds.Select(id => Loc.T("対象の星：", "target star: ") + StarName(id)));
                targets.AddRange(scope.SourceSelectors.Select(SelectorText));
                targets.AddRange(scope.ReceiverMemorySet.Select(m => Loc.T("受け手：", "receiver: ") + Links.Name(m)));
                targets.AddRange(scope.ReceiverSelectors.Select(s => Loc.T("受け手：", "receiver: ") + SelectorText(s)));
                if (scope.PayloadKind != KeystonePayloadKind.None)
                    targets.Add(EnumText(scope.PayloadKind, Loc.Japanese, new[] { "すべての効果", "記憶の仕掛け", "対象指定のクールダウン短縮", "次の通常攻撃強化",
                        "記憶への引継ぎ", "HP支払いから障壁へ", "味方への障壁", "合わせ技の成功効果", "敵の強化に応じた追加報酬" },
                        new[] { "all effects", "memory gimmick", "directed cooldown reduction", "next basic attack enhancement", "memory relay", "HP payment to shield", "allied shield", "bridge success payoff", "pressure dividend" }));
                if (scope.Recipient != KeystoneRecipientKind.Any)
                    targets.Add(EnumText(scope.Recipient, Loc.Japanese, new[] { "すべての受け手", "自分", "味方", "自分の召喚物" }, new[] { "any recipient", "self", "allies", "own summons" }));
                if (scope.SourceKind.HasValue)
                    targets.Add(EnumText(scope.SourceKind.Value, Loc.Japanese, new[] { "記憶固有の発火", "自分の通常攻撃", "自分の召喚物", "追加生成効果", "移動による発火" }, new[] { "native memory trigger", "own basic attack", "own summon", "generated effect", "movement trigger" }));
                if (scope.Argument.HasValue) targets.Add(Loc.T("効果の種類：", "effect type: ") + scope.Argument.Value);
                string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
                string valueText;
                switch (transform.Operation)
                {
                    case KeystoneOperation.Scale: valueText = Number(transform.MagnitudeUnits.Percent) + "%"; break;
                    case KeystoneOperation.SetSeconds: valueText = Number(transform.Seconds) + Loc.T("秒", "s"); break;
                    case KeystoneOperation.Set:
                    case KeystoneOperation.Add: valueText = Number(transform.Seconds); break;
                    default: valueText = transform.Count.ToString(CultureInfo.InvariantCulture); break;
                }
                lines.Add(layer + Loc.T("（", " (") + (targets.Count == 0 ? Loc.T("すべての対象", "all targets") : string.Join(Loc.T("、", ", "), targets)) + Loc.T("）", ")") + "\n  " + field + Loc.T("：", ": ") + operation + " " + valueText
                    + (transform.ExpectedFrom.HasValue ? Loc.T("（変更前 ", " (from ") + Number(transform.ExpectedFrom.Value) + Loc.T("）", ")") : "")
                    + (transform.Maximum.HasValue ? Loc.T("（上限 ", " (max ") + Number(transform.Maximum.Value) + Loc.T("）", ")") : ""));
            }
            foreach (var grant in key.Grants)
                lines.Add(Loc.T("付与：", "Grant: ") + DescribeMechanism(grant));
            return string.Join("\n", lines);
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
                return string.Join(Loc.T(" または ", " or "), selector.Alternatives.Select(SelectorText));
            string text;
            switch (selector.Kind)
            {
                case MemorySelectorKind.Memory: return Links.Name(selector.Memory).ToString();
                case MemorySelectorKind.EquippedQ: text = Loc.T("装備中のQの記憶", "equipped Q memory"); break;
                case MemorySelectorKind.EquippedR: text = Loc.T("装備中のRの記憶", "equipped R memory"); break;
                case MemorySelectorKind.EquippedIdentity: text = Loc.T("装備中の固有記憶", "equipped identity memory"); break;
                case MemorySelectorKind.EquippedQOrR: text = Loc.T("装備中のQまたはRの記憶", "equipped Q or R memory"); break;
                case MemorySelectorKind.EquippedMovement: text = Loc.T("装備中の移動記憶", "equipped movement memory"); break;
                case MemorySelectorKind.OtherNormal: text = Loc.T("ほかの通常記憶", "other equipped normal memories"); break;
                default: throw new InvalidOperationException("Unknown memory selector: " + selector.Kind);
            }
            return text + (selector.AllowedMemories.Count == 0 ? "" : "（"
                + string.Join(Loc.T("、", ", "), selector.AllowedMemories.Select(m => Links.Name(m).ToString())) + "）");
        }

        private static void RequireStar(TalentDef star)
        {
            if (star == null) throw new ArgumentNullException(nameof(star));
        }
    }
}

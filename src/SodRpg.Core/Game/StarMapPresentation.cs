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
            return (chosen < 0 ? Loc.T("未選択：どちらか1つを選んでください。", "Unselected: choose one option.")
                : Loc.T("選択中の効果：", "Chosen effect:"))
                + "\n" + ChoiceOptionLabel(star, 0, chosen) + "\n" + ChoiceOptionLabel(star, 1, chosen);
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

        public static string KeystoneDescription(TalentDef star)
        {
            RequireStar(star);
            if (star.KeystoneDefinition != null)
            {
                var key = star.KeystoneDefinition;
                if (key.Cost <= 0) throw new InvalidOperationException("Invalid keystone cost: " + star.Id);
                return Loc.T("<color=#9fe0b0><b>恩恵</b></color>", "<color=#9fe0b0><b>Benefit</b></color>") + "\n"
                    + (star.AuthoredStar?.KeystoneUpside?.ToString() ?? DescribeKeySide(key, true))
                    + "\n\n" + Loc.T("<color=#ffb0a0><b>代償</b></color>", "<color=#ffb0a0><b>Drawback</b></color>") + "\n"
                    + (star.AuthoredStar?.KeystoneDownside?.ToString() ?? DescribeKeySide(key, false))
                    + "\n" + Loc.T($"必要ポイント：{key.Cost}", $"Cost: {key.Cost} points");
            }
            if (!star.IsKeystone) throw new InvalidOperationException("Not a keystone: " + star.Id);
            return star.Describe();
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
                    default: throw new InvalidOperationException("Unknown mechanism: " + star.Id);
                }
            }
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
            return Loc.T("能力値", "Stat") + ": " + Content.FormatStat(star.Stat, star.PerRank);
        }

        private static void ValidateChoice(TalentDef star, int chosen)
        {
            RequireStar(star);
            if (!star.IsChoice || star.Choices == null || star.Choices.Count != 2
                || star.Choices[0]?.Name == null || star.Choices[1]?.Name == null || chosen < -1 || chosen > 1)
                throw new InvalidOperationException("Invalid two-option choice: " + star.Id);
        }

        private static string DescribeKeySide(KeystoneDefinition key, bool upside)
        {
            if (!Loc.Japanese) return AuthoredMechanisms.DescribeKeystone(key, upside);
            var lines = new List<string>();
            foreach (var transform in upside ? key.Upside : key.Downside)
            {
                string layer = EnumText(transform.TargetLayer, "記憶固有のダメージ", "星による記憶ダメージ", "追加生成ダメージ", "仕掛けの効果");
                string field = EnumText(transform.Field, "効果量", "持続時間", "効果半径", "発生までの時間", "対象数", "発火間隔", "確率", "効果の種類");
                string operation = EnumText(transform.Operation, "増減率", "秒数", "追加数", "発動に必要な回数", "無効化", "傷の再配分", "設定値", "加算量");
                var scope = transform.Scope;
                var targets = new List<string>();
                targets.AddRange(scope.TargetMemorySet.Select(m => Links.Name(m).ToString()));
                targets.AddRange(scope.TargetEffectSet.Select(EffectText));
                targets.AddRange(scope.TargetEffectIds.Select(id => "対象の星：" + id));
                targets.AddRange(scope.SourceSelectors.Select(SelectorText));
                targets.AddRange(scope.ReceiverMemorySet.Select(m => "受け手：" + Links.Name(m)));
                targets.AddRange(scope.ReceiverSelectors.Select(s => "受け手：" + SelectorText(s)));
                if (scope.PayloadKind != KeystonePayloadKind.None)
                    targets.Add(EnumText(scope.PayloadKind, "すべての効果", "記憶の仕掛け", "対象指定のクールダウン短縮", "次の通常攻撃強化",
                        "記憶への引継ぎ", "HP支払いから障壁へ", "味方への障壁", "合わせ技の成功効果", "敵の強化に応じた追加報酬"));
                if (scope.Recipient != KeystoneRecipientKind.Any)
                    targets.Add(EnumText(scope.Recipient, "すべての受け手", "自分", "味方", "自分の召喚物"));
                if (scope.SourceKind.HasValue)
                    targets.Add(EnumText(scope.SourceKind.Value, "記憶固有の発火", "自分の通常攻撃", "自分の召喚物", "追加生成効果", "移動による発火"));
                if (scope.Argument.HasValue) targets.Add("効果の種類：" + scope.Argument.Value);
                string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
                string valueText;
                switch (transform.Operation)
                {
                    case KeystoneOperation.Scale: valueText = Number(transform.MagnitudeUnits.Percent) + "%"; break;
                    case KeystoneOperation.RedistributeWound: valueText = Number(transform.MagnitudeUnits.Percent) + "%、持続時間の増減率 " + Number(transform.WoundDurationUnits.Percent) + "%"; break;
                    case KeystoneOperation.Disable: valueText = ""; break;
                    case KeystoneOperation.SetSeconds: valueText = Number(transform.Seconds) + "秒"; break;
                    case KeystoneOperation.Set:
                    case KeystoneOperation.Add: valueText = Number(transform.Seconds); break;
                    default: valueText = transform.Count.ToString(CultureInfo.InvariantCulture); break;
                }
                lines.Add(layer + "（" + (targets.Count == 0 ? "すべての対象" : string.Join("、", targets)) + "）\n  " + field + "：" + operation + " " + valueText
                    + (transform.ExpectedFrom.HasValue ? "（変更前 " + Number(transform.ExpectedFrom.Value) + "）" : "")
                    + (transform.Maximum.HasValue ? "（上限 " + Number(transform.Maximum.Value) + "）" : ""));
            }
            if (upside)
                foreach (var grant in key.Grants)
                    lines.Add(Loc.T("付与：", "Grant: ") + DescribeMechanism(grant));
            return string.Join("\n", lines);
        }

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

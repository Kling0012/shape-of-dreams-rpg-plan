using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>連携の効果の種類（v1.26）。Requires をすべて満たしている間、または満たしている記憶を使ったときに効く。</summary>
    public enum LinkKind
    {
        None = 0,
        /// <summary>記憶加速：条件の記憶を使うと、そのクールダウンが Value% 早く戻る。</summary>
        MemoryHaste = 1,
        /// <summary>記憶の余韻：条件の記憶を使った後の5秒間、攻撃力・魔力が Value% 上がる。</summary>
        MemorySurge = 2,
        /// <summary>同調：条件を満たしている間、攻撃力・魔力が Value% 上がる。</summary>
        Attune = 3,
        /// <summary>守り：条件を満たしている間、最大HPが Value% 上がり、防御が Value 増える。</summary>
        Guard = 4,
        /// <summary>記憶の冴え（v1.28）：条件の記憶（アイデンティティの受け身を含む）で与えるダメージが Value% 上がる。</summary>
        MemoryDamage = 5,
        /// <summary>Boss-native reward stage, selected independently per set/profile; never an ordinary link bonus.</summary>
        BossReward = 6,
    }

    /// <summary>
    /// 固有品の連携（v1.26）。Requires の各要素は記憶（St_...）・エッセンス（Gem_...）・旅人（Hero_...）の型名で、1〜3つ。
    /// MemoryHaste と MemorySurge は Requires に記憶を1つ以上含む必要がある（効果の起点になるため）。
    /// </summary>
    public sealed class LinkDef
    {
        public string[] Requires;
        public LinkKind Kind;
        public int ValueMilli { get; set; }
        /// <summary>Host-computed star portion of ValueMilli; not transmitted or trusted from a client.</summary>
        public int StarValueMilli { get; set; }
        public float StarValuePercent => StarValueMilli / (float)BuildPrecision.Scale;
        public decimal Value { get => ValueMilli / (decimal)BuildPrecision.Scale; set => ValueMilli = BuildPrecision.FromDecimal(value); }
        public float ValuePercent => ValueMilli / (float)BuildPrecision.Scale;
    }

    /// <summary>
    /// 連携先の名前と条件の判定（v1.26）。対象は本体の RawData で確認した型名。
    /// 効果をゲームへ届けるのはホスト（SodRpg.Mod.HostAuthority）で、ここは純粋な判定だけを持つ。
    /// </summary>
    public static class Links
    {
        /// <summary>絆を満たす、生きている味方旅人までの距離（m）。</summary>
        public const double BondRange = 10;

        /// <summary>距離や生存判定はホストが渡す。無効な距離や敵の旅人は絆に数えない。</summary>
        public static bool IsBondAlly(bool living, bool allied, double distance) =>
            living && allied && distance >= 0 && distance <= BondRange;

        /// <summary>導きの羅針盤の正規形（充電前後で型が違うが、連携では同じエッセンスとして数える）。</summary>
        public const string Compass = "Gem_U_GuidingCompass_NotCharged";
        private const string CompassCharged = "Gem_U_GuidingCompass_Charged";

        private static readonly Dictionary<string, Txt> MemoryNames = new Dictionary<string, Txt>(StringComparer.Ordinal)
        {
            // Verified common memories used by the shared Outer authoring contract.
            ["St_C_GlacialStomp"] = new Txt("氷河ストンプ", "Glacial Stomp"),
            ["St_C_FlashFreeze"] = new Txt("急速凍結", "Flash Freeze"),
            ["St_C_BeamOfLight"] = new Txt("光線", "Beam of Light"),
            ["St_C_Purgatory"] = new Txt("煉獄", "Purgatory"),
            ["St_C_SparklingWaterGun"] = new Txt("ピリッとする水鉄砲", "Sparkling Water Gun"),
            ["St_C_Pew"] = new Txt("発砲", "Pew"),
            ["St_C_Starfall"] = new Txt("流星", "Starfall"),
            ["St_C_DarkBolt"] = new Txt("闇の矢", "Dark Bolt"),
            ["St_C_MassProtection"] = new Txt("範囲シールド", "Mass Protection"),
            ["St_E_MassCleanse"] = new Txt("大規模浄化", "Mass Cleanse"),
            // レジェンダリー・ユニークの記憶
            ["St_L_Blizzard"] = new Txt("吹雪", "Blizzard"),
            ["St_L_ButchersStrike"] = new Txt("屠殺者の一撃", "Butcher's Strike"),
            ["St_L_CoinExplosion"] = new Txt("コインバースト", "Coin Explosion"),
            ["St_L_LightExplosion"] = new Txt("ルミナスバースト", "Radiant Explosion"),
            ["St_L_MentalCorruption"] = new Txt("精神汚染", "Mental Corruption"),
            ["St_L_Multishot"] = new Txt("矢の洗礼", "Arrow Storm"),
            ["St_L_PyranasFireball"] = new Txt("ファイラナのファイアボール", "Pyrana's Fireball"),
            ["St_L_SpectreBullet"] = new Txt("霊弾", "Spectral Bullet"),
            ["St_L_HerosReturn"] = new Txt("英雄の帰還", "Hero's Return"),
            ["St_L_SmallMoltenCore"] = new Txt("小さな溶鉱炉", "Small Molten Core"),
            ["St_U_BeamOfBalance"] = new Txt("均衡の光線", "Beam of Balance"),
            ["St_U_Burrow"] = new Txt("穴掘り", "Burrow"),
            ["St_U_HerWorld"] = new Txt("彼女の世界", "Her World"),
            ["St_U_Hysteria"] = new Txt("ヒステリー", "Hysteria"),
            ["St_U_ShoutOfOblivion"] = new Txt("忘却の咆哮", "Shout of Oblivion"),
            ["St_U_WorldCracker"] = new Txt("世界の破壊者", "World Cracker"),
            ["St_U_BigChomp"] = new Txt("大噛みつき", "Big Chomp"),
            // 旅人記憶（Identity）
            ["St_D_AstridsMasterpieceEnGarde"] = new Txt("アストリッドの傑作 - アンガルド", "Astrid's Masterpiece - En Garde"),
            ["St_D_AstridsMasterpiecePriorite"] = new Txt("アストリッドの傑作 - プリオリテ", "Astrid's Masterpiece - Priorité"),
            ["St_D_BeautifulThreat"] = new Txt("美しい脅威", "Beautiful Threat"),
            ["St_D_CircleOfLife"] = new Txt("生命の循環", "Circle of Life"),
            ["St_D_ConvergencePoint"] = new Txt("集まる星々", "Converging Stars"),
            ["St_D_DisintegratingClaw"] = new Txt("分解の爪", "Dismantling Claw"),
            ["St_D_DoubleTap"] = new Txt("ダブルタップ", "Double Tap"),
            ["St_D_ExoticMatter"] = new Txt("エキゾチック物質", "Exotic Matter"),
            ["St_D_HeartOfThePack"] = new Txt("群れの心臓", "Heart of the Pack"),
            ["St_D_MercyOfEl"] = new Txt("エルの慈悲", "El's Mercy"),
            ["St_D_PrismaticEyes"] = new Txt("プリズムの視界", "Prismatic Vision"),
            ["St_D_Resolve"] = new Txt("決意", "Resolve"),
            ["St_D_SalamanderPowder"] = new Txt("サラマンダーパウダー", "Salamander Powder"),
            ["St_D_TheKillingFlow"] = new Txt("一歩一殺", "One Step, One Kill"),
            ["St_D_BurningFist"] = new Txt("燃え盛る拳", "Burning Fist"),
            ["St_D_ChargedAnguillian"] = new Txt("チャージされたアンギリアン", "Charged Anguillian"),
            ["St_D_ExplosionArtist"] = new Txt("華麗なる芸術家", "Explosive Artist"),
            ["St_D_IceColdPresence"] = new Txt("極寒の気迫", "Ice Cold Presence"),
            ["St_D_IcyVeins"] = new Txt("氷の血脈", "Icy Veins"),
            ["St_D_ParryMaster"] = new Txt("決闘の達人", "Master Duelist"),
            ["St_D_QuartetOfDeath"] = new Txt("死の四重奏", "Quartet of Death"),
            ["St_D_ScarOfTheWind"] = new Txt("風の傷", "Scar of the Wind"),
            ["St_D_SharedPain"] = new Txt("苦痛の共有", "Shared Pain"),
            // 旅人記憶（Movement）
            ["St_M_Charge"] = new Txt("重装タックル", "Heavy Strike"),
            ["St_M_DreamyWaltz"] = new Txt("夢幻のワルツ", "Dreamy Waltz"),
            ["St_M_FastFeet"] = new Txt("クイックフット", "Swift Steps"),
            ["St_M_FeatheryDash"] = new Txt("フェザーダッシュ", "Feathery Dash"),
            ["St_M_FlashStep"] = new Txt("瞬歩", "Flash Step"),
            ["St_M_Flicker"] = new Txt("変光星", "Flicker"),
            ["St_M_NimbleDodge"] = new Txt("速攻回避", "Nimble Dodge"),
            ["St_M_Sprint"] = new Txt("歪な疾走", "Distorting Sprint"),
            ["St_M_FrostyCharge"] = new Txt("フロストチャージ", "Frosty Charge"),
            ["St_M_IceColdPresence"] = new Txt("氷の突進", "Ice Dash"),
            ["St_M_ParryMaster"] = new Txt("受け流し", "Parry"),
            // 旅人記憶（Q・QR）
            ["St_Q_CruelSun"] = new Txt("残酷な太陽", "Cruel Sun"),
            ["St_Q_Discipline"] = new Txt("規律", "Discipline"),
            ["St_Q_EtherealInfluence"] = new Txt("エーテルの影響力", "Ether's Influence"),
            ["St_Q_Fleche"] = new Txt("フレッシュ", "Flèche"),
            ["St_Q_GoldenBurst"] = new Txt("ゴールデンバースト", "Golden Explosion"),
            ["St_Q_HandCannon"] = new Txt("ハンドキャノン", "Hand Cannon"),
            ["St_Q_IncendiaryRounds"] = new Txt("焼夷弾装填", "Incendiary Rounds"),
            ["St_Q_Laceration"] = new Txt("裂傷", "Laceration"),
            ["St_Q_Lunge"] = new Txt("ランジ", "Lunge"),
            ["St_Q_MoonlightPact"] = new Txt("月光の誓約", "Moonlight Pact"),
            ["St_Q_Reduction"] = new Txt("還元", "Reduction"),
            ["St_Q_SuperNova"] = new Txt("超新星", "Supernova"),
            ["St_Q_SylvanCall"] = new Txt("森の呼応", "Sylvan Call"),
            ["St_Q_BigBorealChunk"] = new Txt("巨大な氷河の欠片", "Big Boreal Chunk"),
            ["St_Q_DeathMark"] = new Txt("死の刻印", "Death Mark"),
            ["St_Q_EmbracingTheChill"] = new Txt("寒気を受け入れよ", "Embrace the Chill"),
            ["St_QR_DistortedMind"] = new Txt("歪な精神", "Distorted Mind"),
            ["St_QR_InfernalTales"] = new Txt("業火物語", "Tales of Hellfire"),
            ["St_QR_Innocence"] = new Txt("無垢な魂", "Pure Soul"),
            ["St_QR_ValiantHeart"] = new Txt("勇敢なる心", "Valiant Heart"),
            // 旅人記憶（R）
            ["St_R_AnnihilationStance"] = new Txt("滅殺態勢", "Annihilation Stance"),
            ["St_R_BaptismOfSun"] = new Txt("太陽の洗礼", "Baptism of the Sun"),
            ["St_R_Cataclysm"] = new Txt("カタクリスム", "Cataclysm"),
            ["St_R_ChainReaction"] = new Txt("連鎖反応", "Chain Reaction"),
            ["St_R_DangerousTheory"] = new Txt("危険な理論", "Dangerous Theory"),
            ["St_R_NaturesWhisper"] = new Txt("自然の囁き", "Nature's Whisper"),
            ["St_R_Parry"] = new Txt("パリィ", "Parry"),
            ["St_R_PrecisionShot"] = new Txt("精密射撃", "Precision Shot"),
            ["St_R_QuickTrigger"] = new Txt("クイックハンド", "Quick Draw"),
            ["St_R_SanctuaryOfEl"] = new Txt("エルの聖域", "El's Sanctuary"),
            ["St_R_SerpentineBlessing"] = new Txt("蛇の祝福", "Serpent's Blessing"),
            ["St_R_Tranquility"] = new Txt("平静", "Tranquility"),
            ["St_R_UnbreakableDetermination"] = new Txt("不屈の意志", "Unbreakable Determination"),
            ["St_R_BackOff"] = new Txt("下がれ！", "Back Off!"),
            ["St_R_Deception"] = new Txt("撹乱", "Deception"),
            ["St_R_FrozenFists"] = new Txt("礼儀注入", "Teaching Manners"),
        };

        private static readonly Dictionary<string, Txt> EssenceNames = new Dictionary<string, Txt>(StringComparer.Ordinal)
        {
            ["Gem_L_CamillasGift"] = new Txt("カミラのプレゼント", "Camilla's Gift"),
            ["Gem_L_ChaosApple"] = new Txt("混沌のリンゴ", "Apple of Discord"),
            ["Gem_L_DivineFaith"] = new Txt("神聖なる信仰", "Divine Faith"),
            ["Gem_L_Embertail"] = new Txt("エンバーテイル", "Embertail"),
            ["Gem_L_HeartOfGold"] = new Txt("黄金の心臓", "Heart of Gold"),
            ["Gem_L_MetalCrystal"] = new Txt("金属結晶", "Metallic Crystal"),
            ["Gem_L_Paranoia"] = new Txt("偏執症のエッセンス", "Essence of Paranoia"),
            ["Gem_L_Perfect"] = new Txt("完璧", "Perfection"),
            ["Gem_L_PureWhite"] = new Txt("純白", "Pure White"),
            ["Gem_L_SolarEye"] = new Txt("太陽の眼", "Eye of the Sun"),
            ["Gem_L_SuppressedArcanum"] = new Txt("抑圧されたアルカナム", "Suppressed Arcanum"),
            ["Gem_L_Culinary"] = new Txt("料理のエッセンス", "Essence of Culinary"),
            ["Gem_L_Liberty"] = new Txt("解放のエッセンス", "Essence of Liberty"),
            ["Gem_L_Supersymmetry"] = new Txt("超対称性", "Supersymmetry"),
            ["Gem_U_EternalFlame"] = new Txt("永遠の炎", "Eternal Flame"),
            ["Gem_U_GlacialCore"] = new Txt("氷河のコア", "Glacial Core"),
            ["Gem_U_LastStarlight"] = new Txt("最後の星明かり", "Last Starlight"),
            ["Gem_U_SoulPrison"] = new Txt("魂の牢獄", "Soul Prison"),
            [Compass] = new Txt("導きの羅針盤", "Guiding Compass"),
        };

        private static readonly Dictionary<string, Txt> TravelerNames = new Dictionary<string, Txt>(StringComparer.Ordinal)
        {
            ["Hero_Vesper"] = new Txt("Vesper", "Vesper"),
            ["Hero_Lacerta"] = new Txt("Lacerta", "Lacerta"),
            ["Hero_Cetus"] = new Txt("Cetus", "Cetus"),
            ["Hero_Yubar"] = new Txt("Yubar", "Yubar"),
            ["Hero_Husk"] = new Txt("Husk", "Husk"),
            ["Hero_Mist"] = new Txt("Mist", "Mist"),
            ["Hero_Nachia"] = new Txt("Nachia", "Nachia"),
            ["Hero_Aurena"] = new Txt("Aurena", "Aurena"),
            ["Hero_Bismuth"] = new Txt("Bismuth", "Bismuth"),
        };

        /// <summary>導きの羅針盤の充電前後を1つのエッセンスへ整える。それ以外はそのまま。</summary>
        public static string Canon(string target) => target == CompassCharged ? Compass : target;

        /// <summary>連携先の表示名。未知の名前には型名をそのまま返す（描画が例外にならないように）。</summary>
        public static Txt Name(string target)
        {
            target = Canon(target);
            if (MemoryNames.TryGetValue(target, out var m)) return m;
            if (EssenceNames.TryGetValue(target, out var e)) return e;
            if (TravelerNames.TryGetValue(target, out var t)) return t;
            return new Txt(target, target);
        }

        public static bool IsMemory(string target) => target != null && MemoryNames.ContainsKey(target);
        public static bool IsEssence(string target) => target != null && EssenceNames.ContainsKey(Canon(target));
        public static bool IsTraveler(string target) => target != null && TravelerNames.ContainsKey(target);
        public static bool IsKnown(string target) => IsMemory(target) || IsEssence(target) || IsTraveler(target);

        /// <summary>値の上限。単体は同調・守り25・記憶加速50・記憶の余韻40。2つで1.6倍、3つで2.2倍（四捨五入）。</summary>
        public static int Cap(LinkKind kind, int requireCount)
        {
            if (kind == LinkKind.BossReward) return requireCount == 1 ? 3 : 0;
            int single;
            switch (kind)
            {
                case LinkKind.Attune:
                case LinkKind.Guard: single = 25; break;
                case LinkKind.MemoryHaste: single = 50; break;
                case LinkKind.MemorySurge: single = 40; break;
                case LinkKind.MemoryDamage: single = 40; break;
                default: return 0;
            }
            double mult = requireCount >= 3 ? 2.2 : requireCount == 2 ? 1.6 : 1.0;
            return (int)Math.Round(single * mult, MidpointRounding.AwayFromZero);
        }

        /// <summary>記憶加速の装備合計の元値の上限。覚醒倍率はこの上限の後に掛ける。</summary>
        public const int MaxHaste = 90;

        /// <summary>
        /// 装着中の同条件の連携の元値合計に許す上限。覚醒倍率は比例配分した元値に掛け、最終値はこの2.5倍まで。
        /// 星の連携は別に加算し、ホストも Build.Compute で同じ装備計算を行う。
        /// </summary>
        public static decimal EquippedCap(LinkKind kind, int requireCount)
        {
            decimal cap = Cap(kind, requireCount) * Content.AwakenPowerPctAt(Content.MaxAwakenLevel) / 100m;
            return kind == LinkKind.MemoryHaste ? Math.Min(MaxHaste, cap) : cap;
        }

        /// <summary>データとして正しいか。対象は既知で、1〜3つ、重複なし。記憶を使う効果には記憶が要る。</summary>
        public static bool Validate(LinkDef link)
        {
            if (link == null || link.Requires == null) return false;
            int n = link.Requires.Length;
            if (n < 1 || n > 3) return false;
            if (link.Kind == LinkKind.BossReward && (n != 1 || IsTraveler(link.Requires[0])
                || link.ValueMilli < 1000 || link.ValueMilli > 3000 || link.ValueMilli % 1000 != 0)) return false;
            if (link.Kind == LinkKind.None || !Enum.IsDefined(typeof(LinkKind), link.Kind)) return false;
            bool hasMemory = false;
            for (int i = 0; i < n; i++)
            {
                string t = Canon(link.Requires[i]);
                if (!IsKnown(t)) return false;
                for (int j = 0; j < i; j++)
                    if (Canon(link.Requires[j]) == t) return false;
                if (MemoryNames.ContainsKey(t)) hasMemory = true;
            }
            if ((link.Kind == LinkKind.MemoryHaste || link.Kind == LinkKind.MemorySurge || link.Kind == LinkKind.MemoryDamage) && !hasMemory) return false;
            return true;
        }

        /// <summary>旅人は自分か10m以内の生きている味方。装着条件は自分の記憶・エッセンスだけを見る。</summary>
        public static bool RequirementSatisfied(string target, string heroKey, ICollection<string> equippedMemories, ICollection<string> equippedEssences,
            ICollection<string> nearbyAllyHeroes = null)
        {
            target = Canon(target);
            if (target == null) return false;
            if (TravelerNames.ContainsKey(target)) return heroKey == target || (nearbyAllyHeroes != null && nearbyAllyHeroes.Contains(target));
            if (MemoryNames.ContainsKey(target)) return equippedMemories != null && equippedMemories.Contains(target);
            if (EssenceNames.ContainsKey(target))
            {
                if (equippedEssences == null) return false;
                if (equippedEssences.Contains(target)) return true;
                // 羅針盤は充電前後のどちらで装着していても満たす。
                return target == Compass && equippedEssences.Contains(CompassCharged);
            }
            return false;
        }

        /// <summary>連携の条件をすべて満たしているか。ホストの定期走査と画面の印で使う。</summary>
        public static bool Satisfied(LinkDef link, string heroKey, ICollection<string> equippedMemories, ICollection<string> equippedEssences,
            ICollection<string> nearbyAllyHeroes = null)
        {
            if (link == null || link.Requires == null || link.Requires.Length == 0) return false;
            int travelers = 0;
            bool includesSelf = false;
            foreach (string t in link.Requires)
            {
                if (IsTraveler(t))
                {
                    travelers++;
                    if (t == heroKey) includesSelf = true;
                }
                if (!RequirementSatisfied(t, heroKey, equippedMemories, equippedEssences, nearbyAllyHeroes)) return false;
            }
            // 2種類以上の旅人の絆は、その組の一員としてだけ受けられる。
            return travelers < 2 || includesSelf;
        }

        public static string ItemName(string target)
        {
            var name = Name(target);
            return IsMemory(Canon(target)) ? Loc.T("記憶『" + name.Ja + "』", "Memory “" + name.En + "”")
                : IsEssence(Canon(target)) ? Loc.T("エッセンス『" + name.Ja + "』", "Essence “" + name.En + "”")
                : name.ToString();
        }
        /// <summary>Describe effects and their equipped-item or nearby-traveler requirements.</summary>
        public static string Describe(LinkDef link, Func<string, bool> isSatisfied = null)
        {
            if (link == null || link.Requires == null || link.Requires.Length == 0) return "";
            if (link.Kind == LinkKind.BossReward)
                return Loc.T($"固有報酬の段階{link.Value}が有効：本人が{ItemName(link.Requires[0])}を装着中。対応する各セットの最高段階だけ有効。",
                    $"Boss reward stage {link.Value} enabled: while you equip {ItemName(link.Requires[0])}. Highest stage per matching set only.");
            string value = link.Value.ToString(CultureInfo.InvariantCulture);
            string limitJa, limitEn;
            switch (link.Kind)
            {
                case LinkKind.MemoryHaste:
                    limitJa = "（1回の発動での短縮は合計100%まで）";
                    limitEn = " (Total reduction per cast is capped at 100%.)";
                    break;
                case LinkKind.MemorySurge:
                    limitJa = "（同時に有効なのは最も大きい1つだけ。重ならず時間を延長し、装備条件を外すと解除）";
                    limitEn = " (Only the strongest active link applies. It refreshes the duration without stacking, and ends when its equip requirements are removed.)";
                    break;
                default:
                    limitJa = "（同種の連携は加算。合計の上限なし）";
                    limitEn = " (Links of the same kind add together, with no total cap.)";
                    break;
            }
            return Loc.Japanese ? DescribeJa(link, isSatisfied, value) + limitJa : DescribeEn(link, isSatisfied, value) + limitEn;
        }

        private static string Mark(string target, Func<string, bool> isSatisfied)
        {
            if (isSatisfied == null) return "";
            return isSatisfied(target) ? "✓" : "・";
        }

        private static string ConditionJa(LinkDef link, Func<string, bool> isSatisfied, bool memoryKind)
        {
            var travelers = new List<string>();
            var items = new List<string>(link.Requires.Length);
            foreach (string t in link.Requires)
            {
                if (IsTraveler(Canon(t)))
                {
                    travelers.Add(Name(t).Ja + Mark(t, isSatisfied));
                }
                else items.Add(ItemName(t) + Mark(t, isSatisfied));
            }
            var conditions = new List<string>();
            if (travelers.Count == 1)
                conditions.Add("自分が" + travelers[0] + "であるか、10m以内に生存中の味方の" + travelers[0] + "がいるとき");
            else if (travelers.Count > 1)
                conditions.Add(string.Join("・", travelers) + "のうち1人が自分で、残りの旅人が10m以内に生存している味方であるとき");
            if (items.Count > 0)
                conditions.Add(JoinJa(items) + "を" + (items.Count > 1 ? "すべて" : "") + "装着しているとき");
            return string.Join("、かつ", conditions);
        }

        private static string JoinJa(List<string> items) =>
            items.Count == 1 ? items[0] : string.Join("と", items);

        private static string DescribeJa(LinkDef link, Func<string, bool> isSatisfied, string value)
        {
            string cond = ConditionJa(link, isSatisfied, false);
            switch (link.Kind)
            {
                case LinkKind.MemoryDamage:
                    return MemorySubjectJa(link) + "のダメージ +" + value + "%：" + cond;
                case LinkKind.Attune:
                    return "攻撃力・魔力 +" + value + "%：" + cond;
                case LinkKind.Guard:
                    return "最大HP +" + value + "%・防御 +" + value + "：" + cond;
                case LinkKind.MemoryHaste:
                    return "クールダウン短縮 +" + value + "%：" + cond + "、" + MemorySubjectJa(link) + "を使うと、その記憶の残りクールダウンを最大クールダウンの" + value + "%分短縮する";
                case LinkKind.MemorySurge:
                    return MemorySubjectJa(link) + "を使った後5秒間、攻撃力・魔力 +" + value + "%：" + cond;
                default:
                    return "連携条件：" + cond;
            }
        }

        /// <summary>記憶を使う効果の主語。条件の記憶が1つならその名前、複数ならどれか1つ。</summary>
        private static string MemorySubjectJa(LinkDef link)
        {
            string first = null;
            int count = 0;
            foreach (string t in link.Requires)
            {
                if (!IsMemory(Canon(t))) continue;
                count++;
                if (first == null) first = ItemName(t);
            }
            if (count == 1) return first;
            return "条件の記憶のどれか";
        }

        private static string ConditionEn(LinkDef link, Func<string, bool> isSatisfied, bool memoryKind)
        {
            var travelers = new List<string>();
            var items = new List<string>(link.Requires.Length);
            foreach (string t in link.Requires)
            {
                if (IsTraveler(Canon(t)))
                {
                    travelers.Add(Name(t).En + Mark(t, isSatisfied));
                }
                else items.Add(ItemName(t) + (Mark(t, isSatisfied).Length == 0 ? "" : " " + Mark(t, isSatisfied)));
            }
            var conditions = new List<string>();
            if (travelers.Count == 1)
                conditions.Add("while playing " + travelers[0] + " or with a living allied " + travelers[0] + " within 10 m");
            else if (travelers.Count > 1)
                conditions.Add("while playing one of " + string.Join(" and ", travelers) + ", with the other required travelers alive and allied within 10 m");
            if (items.Count > 0)
                conditions.Add("with " + string.Join(" and ", items) + (items.Count > 1 ? " all" : "") + " equipped");
            return string.Join(", and ", conditions);
        }

        private static string DescribeEn(LinkDef link, Func<string, bool> isSatisfied, string value)
        {
            string cond = ConditionEn(link, isSatisfied, false);
            switch (link.Kind)
            {
                case LinkKind.MemoryDamage:
                    return "Damage dealt by " + MemorySubjectEn(link) + " +" + value + "%: " + cond + ".";
                case LinkKind.Attune:
                    return "Attack damage and ability power +" + value + "%: " + cond + ".";
                case LinkKind.Guard:
                    return "Maximum health +" + value + "% and armor +" + value + ": " + cond + ".";
                case LinkKind.MemoryHaste:
                    return "Cooldown reduction +" + value + "%: " + cond + "; using " + MemorySubjectEn(link) + " removes " + value + "% of that memory's maximum cooldown from its remaining cooldown.";
                case LinkKind.MemorySurge:
                    return "After using " + MemorySubjectEn(link) + ", Attack damage and ability power +" + value + "% for 5 seconds: " + cond + ".";
                default:
                    return "Link requirements: " + cond + ".";
            }
        }

        private static string MemorySubjectEn(LinkDef link)
        {
            string first = null;
            int count = 0;
            foreach (string t in link.Requires)
            {
                if (!IsMemory(Canon(t))) continue;
                count++;
                if (first == null) first = ItemName(t);
            }
            if (count == 1) return first;
            return "any of the required memories";
        }
    }
}

using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 装備の土台の家系（v1.32）。特性と固有効果の抽選を「好み」の方向へ寄せるだけで、禁止はしない。
    /// Plain（無垢）は重みを一切変えない。
    /// </summary>
    public enum Family
    {
        /// <summary>無垢。色づけなし（従来どおりの抽選）。</summary>
        Plain = 0,
        /// <summary>氷霜。</summary>
        Frost = 1,
        /// <summary>炎。</summary>
        Flame = 2,
        /// <summary>光。</summary>
        Light = 3,
        /// <summary>闇。</summary>
        Dark = 4,
        /// <summary>守り。</summary>
        Guard = 5,
        /// <summary>疾風。</summary>
        Gale = 6,
        /// <summary>癒し。</summary>
        Mend = 7,
        /// <summary>召喚。</summary>
        Summon = 8,
        /// <summary>記憶。</summary>
        Memory = 9,
    }

    /// <summary>家系ごとの「好む能力値」「好む固有効果」の表。好むものは抽選の重みが2倍になる。</summary>
    public static class FamilyPrefs
    {
        /// <summary>好むものの重みの倍率。</summary>
        public const int PreferredWeightMultiplier = 2;

        private static readonly Stat[] None = new Stat[0];
        private static readonly Power[] NoPowers = new Power[0];

        private static readonly Dictionary<Family, Stat[]> Stats = new Dictionary<Family, Stat[]>
        {
            // 氷霜：冷気の増幅と、固さ・粘り
            [Family.Frost] = new[] { Stat.ColdAmp, Stat.Tenacity, Stat.Armor, Stat.ShieldPower, Stat.MaxHealthPct },
            // 炎：火の増幅と、攻めの数値
            [Family.Flame] = new[] { Stat.FireAmp, Stat.AttackFlat, Stat.AttackSpeedPct, Stat.CritChancePct, Stat.CritDamagePct },
            // 光：光の増幅と、回復・魔力
            [Family.Light] = new[] { Stat.LightAmp, Stat.HealPower, Stat.PowerFlat, Stat.Haste, Stat.HealthRegen },
            // 闇：闇の増幅と、会心・火力
            [Family.Dark] = new[] { Stat.DarkAmp, Stat.CritDamagePct, Stat.CritChancePct, Stat.AttackFlat, Stat.PowerFlat },
            // 守り：防御と体力
            [Family.Guard] = new[] { Stat.Armor, Stat.MaxHealthFlat, Stat.MaxHealthPct, Stat.Tenacity, Stat.HealthRegen },
            // 疾風：速さと射程
            [Family.Gale] = new[] { Stat.MoveSpeedPct, Stat.AttackSpeedPct, Stat.Haste, Stat.CritChancePct, Stat.AttackRangePct },
            // 癒し：回復とシールド
            [Family.Mend] = new[] { Stat.HealPower, Stat.HealthRegen, Stat.MaxHealthPct, Stat.MaxHealthFlat, Stat.ShieldPower },
            // 召喚：召喚獣と魔力
            [Family.Summon] = new[] { Stat.SummonPower, Stat.PowerFlat, Stat.Haste, Stat.AttackSpeedPct, Stat.MaxHealthPct },
            // 記憶：記憶技の回転（ヘイスト）と魔力、HPを捧げる技
            [Family.Memory] = new[] { Stat.Haste, Stat.PowerFlat, Stat.PowerPct, Stat.SacrificeReduction, Stat.CritChancePct },
        };

        // 条件付き攻撃力・魔力の固有効果（Retaliation 等）は、低レアでは PowerAllowedForRarity が先に除くので、
        // 好みに入れても低レアには影響しない。ここでは主に常時型・効果型を選ぶ。
        private static readonly Dictionary<Family, Power[]> Powers = new Dictionary<Family, Power[]>
        {
            // 氷霜：冷気付与と、凍った敵への追撃
            [Family.Frost] = new[] { Power.Frost, Power.FrostCrystal, Power.BrittleIce, Power.Fetters, Power.Steam },
            // 炎：火の付与と、火による追加ダメージ
            [Family.Flame] = new[] { Power.Ember, Power.Blaze, Power.Wildfire, Power.Cinder, Power.Steam },
            // 光：光の付与と、障壁・回復
            [Family.Light] = new[] { Power.Radiance, Power.Barrier, Power.StarShield, Power.Lifeline, Power.KindnessReturns },
            // 闇：闇の付与と、止めの一撃・吸収
            [Family.Dark] = new[] { Power.Umbra, Power.UmbralHeritage, Power.Executioner, Power.SoulSiphon, Power.Eclipse, Power.Cinder },
            // 守り：被弾を受け止める効果
            [Family.Guard] = new[] { Power.Bulwark, Power.Thorns, Power.Aegis, Power.ImmovableStance, Power.ReadyGuard, Power.Barrier },
            // 疾風：移動・回避・連続攻撃
            [Family.Gale] = new[] { Power.Tailwind, Power.Sprint, Power.Whirlwind, Power.Frenzy, Power.WanderersEdge, Power.RunUp },
            // 癒し：回復と生存
            [Family.Mend] = new[] { Power.Lifesteal, Power.SecondWind, Power.OverflowingLife, Power.Apothecary, Power.KindnessReturns, Power.Lifeline },
            // 召喚：群れと仲間の支援
            [Family.Summon] = new[] { Power.PackFeast, Power.CoStar, Power.SharedWard, Power.RelayHand, Power.WatchfulHand },
            // 記憶：記憶技とクールダウンの回転
            [Family.Memory] = new[] { Power.Finale, Power.CriticalEcho, Power.StardustCycle, Power.CrystalCircuit, Power.AceInHand, Power.ShardBoon },
        };

        public static IReadOnlyList<Stat> PreferredStats(Family family) => Stats.TryGetValue(family, out var v) ? v : None;

        public static IReadOnlyList<Power> PreferredPowers(Family family) => Powers.TryGetValue(family, out var v) ? v : NoPowers;

        public static bool PrefersStat(Family family, Stat stat)
        {
            if (!Stats.TryGetValue(family, out var v)) return false;
            for (int i = 0; i < v.Length; i++) if (v[i] == stat) return true;
            return false;
        }

        public static bool PrefersPower(Family family, Power power)
        {
            if (!Powers.TryGetValue(family, out var v)) return false;
            for (int i = 0; i < v.Length; i++) if (v[i] == power) return true;
            return false;
        }

        /// <summary>家系の表示名（図鑑・ツールチップ用）。無垢は null。</summary>
        public static Txt LabelName(Family family)
        {
            switch (family)
            {
                case Family.Frost: return new Txt("氷霜", "Frost");
                case Family.Flame: return new Txt("炎", "Flame");
                case Family.Light: return new Txt("光", "Light");
                case Family.Dark: return new Txt("闇", "Dark");
                case Family.Guard: return new Txt("守り", "Guard");
                case Family.Gale: return new Txt("疾風", "Gale");
                case Family.Mend: return new Txt("癒し", "Mend");
                case Family.Summon: return new Txt("召喚", "Summon");
                case Family.Memory: return new Txt("記憶", "Memory");
                default: return null;
            }
        }

        /// <summary>ツールチップ用の小さな表示（例「氷霜の家系」／「Frost lineage」）。無垢は null。</summary>
        public static string Label(Family family)
        {
            var n = LabelName(family);
            if (n == null) return null;
            return Loc.Japanese ? n.Ja + "の家系" : n.En + " lineage";
        }
    }
}

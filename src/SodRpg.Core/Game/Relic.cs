using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    public sealed class StatLine
    {
        public StatLine(Stat stat, int value)
        {
            Stat = stat;
            Value = value;
        }

        public Stat Stat { get; }
        public int Value { get; }
    }

    public sealed class PowerLine
    {
        public PowerLine(Power power, int value)
        {
            Power = power;
            Value = value;
        }

        public Power Power { get; }
        public int Value { get; }
    }

    /// <summary>
    /// 装備の個体（計画書の「遺物」）。個体ID（Uid）と基礎ID（BaseId）を分けて持つ。
    /// 基礎能力（implicit）は保存せず、基礎とアイテムレベルから毎回計算する。
    /// </summary>
    public sealed class Relic
    {
        public string Uid { get; set; }
        public string BaseId { get; set; }
        /// <summary>固有品なら固有品ID、それ以外は null。</summary>
        public string UniqueId { get; set; }
        /// <summary>銘品なら銘品ID、それ以外は null（v1.32）。</summary>
        public string NamedId { get; set; }
        public Rarity Rarity { get; set; }
        public int ItemLevel { get; set; }
        public int Enhance { get; set; }
        /// <summary>限界突破の回数（0〜3）。1回ごとに強化の上限が+5広がる（v1.27）。</summary>
        public int LimitBreaks { get; set; }
        public int Retunes { get; set; }
        /// <summary>特性の洗い直しを使った回数（v1.31）。回数の上限はない。費用はこれで増える。</summary>
        public int AffixRerolls { get; set; }
        public bool Locked { get; set; }
        /// <summary>専用の開発コマンドで付与した個体。戦闘性能や通常の抽選には影響しない。</summary>
        public bool DeveloperGranted { get; set; }
        /// <summary>New free Infinity cargo; cleared once stored/recovered as an existing asset.</summary>
        public bool InfinityFreeSupply { get; set; }
        /// <summary>受け取った強化の節目（0〜5）。強化が下がっても履歴は残る。</summary>
        public int EnhanceMilestones { get; set; }
        /// <summary>+20で1つ目の固有効果に1.2倍を適用済みか。強化の失敗でも失わない。</summary>
        public bool MilestonePowerApplied { get; set; }
        public int AwakenPoints { get; set; }
        /// <summary>覚醒の段（0〜3）。</summary>
        public int AwakenLevel { get; set; }
        /// <summary>1段以上覚醒しているか。true を入れると、まだなら旧来の覚醒と同じ段にする。</summary>
        public bool Awakened
        {
            get => AwakenLevel > 0;
            set
            {
                if (!value) AwakenLevel = 0;
                else if (AwakenLevel == 0) AwakenLevel = Content.LegacyAwakenLevel;
            }
        }
        public List<StatLine> Affixes { get; } = new List<StatLine>();
        public List<PowerLine> Powers { get; } = new List<PowerLine>();

        public BaseDef Base => Content.GetBase(BaseId);
        public Slot Slot => Base.Slot;
        /// <summary>固有品の連携（v1.26）。連携を持たない遺物・個体は null。</summary>
        public LinkDef Link => UniqueId != null && Content.TryGetUnique(UniqueId, out var u) ? u.Link : null;
        public string BossMove => UniqueId != null && Content.TryGetUnique(UniqueId, out var u) ? u.BossMove : null;
        public int AuthoredEffectCount => BossMove != null ? 1 : Powers.Count;
        public BossMoveEntry EffectiveBossMove()
        {
            if (!BossProfiles.TryGetMove(BossMove, out var profile)) return null;
            var channels = new List<BossChannelValue>(profile.Channels.Count);
            foreach (var c in profile.Channels)
            {
                bool scales = c.Kind == BossCoefficientKind.Damage || c.Kind == BossCoefficientKind.Heal || c.Kind == BossCoefficientKind.Shield;
                decimal multiplier = scales ? Content.EnhancePowerScalePct(Enhance) / 100m * Content.AwakenPowerPctAt(AwakenLevel) / 100m : 1m;
                if (scales && (MilestonePowerApplied || EnhanceMilestones >= 5)) multiplier *= Content.LimitBreakPowerPct / 100m;
                if (profile.SetId != BossProfiles.DemonSetId) multiplier = Math.Min(3m, multiplier);
                int value = scales ? checked((int)decimal.Round(c.ValueMilli * multiplier, 0, MidpointRounding.AwayFromZero)) : c.ValueMilli;
                if (profile.SetId == BossProfiles.DemonSetId) value = Math.Min(c.CapMilli, value);
                channels.Add(new BossChannelValue(c.ChannelId, value));
            }
            return new BossMoveEntry(profile.SetId, profile.Id, channels);
        }
        public string DescribeBossMove() => BossBuildCodec.Describe(EffectiveBossMove());

        /// <summary>強化値を付けない名前。</summary>
        public string PlainName
        {
            get
            {
                if (UniqueId != null && Content.TryGetUnique(UniqueId, out var u)) return u.Name.ToString();
                // 銘品は銘品の名前。エピックの銘（Epithets）は付けない（設計 4）。
                if (NamedId != null && NamedItems.TryGetNamed(NamedId, out var named)) return named.Name.ToString();
                // エピックは、1つ目の固有効果から銘が付く（例：猛火の連なりの剣）。
                if (Rarity == Rarity.Epic && Powers.Count > 0 && Content.Epithet(Powers[0].Power) is Txt ep)
                    return Loc.Japanese ? ep.Ja + " " + Base.Name.Ja : ep.En + " " + Base.Name.En; // 「乱戦の 共鳴の衣」のように区切って読みやすく
                return Base.Name.ToString();
            }
        }

        public string DisplayName => Enhance > 0 ? PlainName + " +" + Enhance.ToString(CultureInfo.InvariantCulture) : PlainName;

        /// <summary>固有効果が固定の遺物（固有品・銘品）。固有効果を交換する出来事の対象外（設計 3.4）。</summary>
        public bool HasFixedPowers => UniqueId != null || NamedId != null;

        /// <summary>図鑑に載る文字列。固有品は固有品ID、銘品は「n:」付き、それ以外は土台ID（設計 3.5・4）。</summary>
        public string CodexId => UniqueId ?? (NamedId != null ? NamedItems.CodexId(NamedId) : BaseId);

        /// <summary>基礎能力の現在値（アイテムレベルと強化を反映）。</summary>
        public StatLine Implicit
        {
            get
            {
                var b = Base;
                int level = Content.LevelScalePct(b.ImplicitStat, ItemLevel);
                return new StatLine(b.ImplicitStat, Scale(b.ImplicitValue, level * Content.EnhanceScalePct(Enhance) / 100));
            }
        }

        /// <summary>強化を反映した能力値の一覧。覚醒の倍率は特性だけに掛け、切り捨てる。</summary>
        public IEnumerable<StatLine> EffectiveStats()
        {
            yield return Implicit;
            int pct = Content.EnhanceScalePct(Enhance);
            foreach (var a in Affixes)
            {
                int value = Scale(a.Value, pct);
                if (Awakened) value = (int)((long)value * Content.AwakenAffixPctAt(AwakenLevel) / 100);
                yield return new StatLine(a.Stat, value);
            }
        }

        /// <summary>強化を反映した固有効果（+5までは+1ごとに+5%、そこから先は+1ごとに+3%）。覚醒の倍率は切り捨てる。</summary>
        public IEnumerable<PowerLine> EffectivePowers()
        {
            if (BossMove != null) yield break;
            int pct = Content.EnhancePowerScalePct(Enhance);
            foreach (var p in Powers)
            {
                int value = Scale(p.Value, pct);
                if (Awakened) value = (int)((long)value * Content.AwakenPowerPctAt(AwakenLevel) / 100);
                yield return new PowerLine(p.Power, value);
            }
        }

        /// <summary>レア度を最優先にした強さ（並べ替え・比較用）。</summary>
        public int Score
        {
            get
            {
                int s = (int)Rarity * 1000 + ItemLevel * 2 + Enhance * 10 + LimitBreaks * 50;
                return s;
            }
        }

        public Relic Clone()
        {
            var c = new Relic
            {
                Uid = Uid,
                BaseId = BaseId,
                UniqueId = UniqueId,
                NamedId = NamedId,
                Rarity = Rarity,
                ItemLevel = ItemLevel,
                Enhance = Enhance,
                LimitBreaks = LimitBreaks,
                Retunes = Retunes,
                AffixRerolls = AffixRerolls,
                Locked = Locked,
                DeveloperGranted = DeveloperGranted,
                InfinityFreeSupply = InfinityFreeSupply,
                EnhanceMilestones = EnhanceMilestones,
                MilestonePowerApplied = MilestonePowerApplied,
                AwakenPoints = AwakenPoints,
                AwakenLevel = AwakenLevel,
            };
            c.Affixes.AddRange(Affixes);
            c.Powers.AddRange(Powers);
            return c;
        }

        internal static int Scale(int value, int pct)
        {
            long v = (long)value * pct;
            // 0から遠ざかる向きに丸めず、四捨五入（負値も対称）。
            long q = v >= 0 ? (v + 50) / 100 : -((-v + 50) / 100);
            if (value != 0 && q == 0) q = value > 0 ? 1 : -1;
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, q));
        }
    }
}

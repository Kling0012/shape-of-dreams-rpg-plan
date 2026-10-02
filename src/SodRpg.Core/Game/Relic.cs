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
        public Rarity Rarity { get; set; }
        public int ItemLevel { get; set; }
        public int Enhance { get; set; }
        public int Retunes { get; set; }
        public bool Locked { get; set; }
        /// <summary>強化の節目をいくつ受け取ったか（0〜2。+3で1、+5で2）。</summary>
        public int EnhanceMilestones { get; set; }
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

        /// <summary>強化値を付けない名前。</summary>
        public string PlainName
        {
            get
            {
                if (UniqueId != null && Content.TryGetUnique(UniqueId, out var u)) return u.Name.ToString();
                // エピックは、1つ目の固有効果から銘が付く（例：猛火の連なりの剣）。
                if (Rarity == Rarity.Epic && Powers.Count > 0 && Content.Epithet(Powers[0].Power) is Txt ep)
                    return Loc.Japanese ? ep.Ja + " " + Base.Name.Ja : ep.En + " " + Base.Name.En; // 「乱戦の 共鳴の衣」のように区切って読みやすく
                return Base.Name.ToString();
            }
        }

        public string DisplayName => Enhance > 0 ? PlainName + " +" + Enhance.ToString(CultureInfo.InvariantCulture) : PlainName;

        /// <summary>基礎能力の現在値（アイテムレベルと強化を反映）。</summary>
        public StatLine Implicit
        {
            get
            {
                var b = Base;
                return new StatLine(b.ImplicitStat, Scale(b.ImplicitValue, Content.LevelScalePct(ItemLevel) * Content.EnhanceScalePct(Enhance) / 100));
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

        /// <summary>強化を反映した固有効果（+1ごとに+5%）。覚醒の倍率は切り捨てる。</summary>
        public IEnumerable<PowerLine> EffectivePowers()
        {
            int pct = 100 + 5 * Enhance;
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
                int s = (int)Rarity * 1000 + ItemLevel * 2 + Enhance * 10;
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
                Rarity = Rarity,
                ItemLevel = ItemLevel,
                Enhance = Enhance,
                Retunes = Retunes,
                Locked = Locked,
                EnhanceMilestones = EnhanceMilestones,
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

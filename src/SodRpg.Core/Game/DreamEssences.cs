using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// MOD独自のエッセンス候補12種。値・条件と既存効果の上限を評価するための定義。
    /// ゲームへの追加は native Gem / SkillTrigger の試作で行い、疑似ソケットには接続しない。
    /// 設計と候補一覧は docs/specs/v2.10-dream-essences.md。
    /// </summary>
    public sealed class DreamEssenceDef
    {
        internal DreamEssenceDef(string id, Txt name, Txt concept, GimmickTrigger trigger, GimmickEffect effect,
            float cooldown, decimal[] values, int[] args = null)
        {
            Id = id; Name = name; Concept = concept; Trigger = trigger; Effect = effect;
            Cooldown = cooldown; Values = values; Args = args ?? new[] { 0, 0, 0 };
        }

        public string Id { get; }
        public Txt Name { get; }
        /// <summary>名前の由来を短く言い表した一文。</summary>
        public Txt Concept { get; }
        public GimmickTrigger Trigger { get; }
        public GimmickEffect Effect { get; }
        /// <summary>仕掛けの内部の間隔（秒）。0 は制限なし。</summary>
        public float Cooldown { get; }
        /// <summary>品質 I・II・III の効果量（%、会心率はパーセントポイント）。</summary>
        internal decimal[] Values { get; }
        /// <summary>品質ごとの補足（跳弾の対象数など）。</summary>
        internal int[] Args { get; }

        public decimal Value(int quality) => Values[quality - 1];
        public int Arg(int quality) => Args[quality - 1];
    }


    public static class DreamEssences
    {
        public const int MinQuality = 1;
        public const int MaxQuality = 3;

        private static readonly DreamEssenceDef[] Defs =
        {
            new DreamEssenceDef("echoing_hollow", new Txt("谺", "Echoing Hollow"),
                new Txt("声が山に残る。", "A voice lingers in the hills."),
                GimmickTrigger.OnHit, GimmickEffect.Echo, 1f, new[] { 12m, 18m, 26m }),
            new DreamEssenceDef("banked_embers", new Txt("燠火", "Banked Embers"),
                new Txt("くすぶる火は、消えたふりをする。", "A smouldering fire pretends to be out."),
                GimmickTrigger.OnHit, GimmickEffect.Wound, 0f, new[] { 30m, 45m, 60m }),
            new DreamEssenceDef("kindling", new Txt("継ぎ火", "Kindling"),
                new Txt("火は、次の火へ渡される。", "A flame is passed on to the next."),
                GimmickTrigger.OnUse, GimmickEffect.RechargeOther, 4f, new[] { 3m, 5m, 7m }),
            new DreamEssenceDef("circling_star", new Txt("巡りの星", "Circling Star"),
                new Txt("倒すほどに、星は早く巡る。", "The more you fell, the faster the star turns."),
                GimmickTrigger.OnKill, GimmickEffect.Recharge, 0f, new[] { 8m, 12m, 16m }),
            new DreamEssenceDef("seed_of_shelter", new Txt("護りの種", "Seed of Shelter"),
                new Txt("踏み出すたび、足元で盾が芽吹く。", "A shield sprouts underfoot with every step."),
                GimmickTrigger.OnUse, GimmickEffect.Shield, 8f, new[] { 3m, 4.5m, 6m }),
            new DreamEssenceDef("unraveling", new Txt("綻び", "Unraveling"),
                new Txt("縫い目をほどくと、守りは緩む。", "Pull the seam and the guard comes loose."),
                GimmickTrigger.OnHit, GimmickEffect.Expose, 0f, new[] { 5m, 8m, 11m }),
            new DreamEssenceDef("stepping_stones", new Txt("飛び石", "Stepping Stones"),
                new Txt("跳ねて、隣の敵へ渡る。", "It skips across to the next enemy."),
                GimmickTrigger.OnHit, GimmickEffect.Ricochet, 1f, new[] { 15m, 22m, 30m }, new[] { 1, 1, 2 }),
            new DreamEssenceDef("rising_tide", new Txt("満ち潮", "Rising Tide"),
                new Txt("重なるほど、潮は満ちる。", "The more it gathers, the higher the tide."),
                GimmickTrigger.OnHit, GimmickEffect.Crescendo, 0f, new[] { 3m, 5m, 7m }),
            new DreamEssenceDef("draught", new Txt("吸命の杯", "Draught"),
                new Txt("一口ずつ、命が戻る。", "Life returns a sip at a time."),
                GimmickTrigger.OnHit, GimmickEffect.Siphon, 0.5f, new[] { 3m, 5m, 8m }),
            new DreamEssenceDef("weak_point_lamp", new Txt("弱点の灯", "Weak-Point Lamp"),
                new Txt("灯りが、急所を照らす。", "The lamp lights up the weak point."),
                GimmickTrigger.OnHit, GimmickEffect.Weakspot, 0f, new[] { 8m, 12m, 18m }),
            new DreamEssenceDef("dulling_mist", new Txt("鈍らせの霧", "Dulling Mist"),
                new Txt("霧が、牙をにぶらせる。", "The mist blunts the fangs."),
                GimmickTrigger.OnHit, GimmickEffect.Sap, 0f, new[] { 4m, 6m, 9m }),
            new DreamEssenceDef("harbinger", new Txt("先触れ", "Harbinger"),
                new Txt("次の一撃を、先に告げる。", "It announces the next blow ahead of time."),
                GimmickTrigger.OnUse, GimmickEffect.Primed, 6f, new[] { 40m, 70m, 100m }),
        };

        public static IReadOnlyList<DreamEssenceDef> All => Defs;

        public static DreamEssenceDef Find(string id)
        {
            foreach (var def in Defs)
                if (string.Equals(def.Id, id, StringComparison.Ordinal)) return def;
            return null;
        }


        public static bool ValidQuality(int quality) => quality >= MinQuality && quality <= MaxQuality;


        /// <summary>1段分の仕掛けの定義。品質が範囲外なら null。</summary>
        public static GimmickDef ToGimmick(DreamEssenceDef def, int quality)
        {
            if (def == null || !ValidQuality(quality)) return null;
            return new GimmickDef
            {
                Trigger = def.Trigger,
                Effect = def.Effect,
                Value = def.Value(quality),
                Arg = def.Arg(quality),
                Cooldown = def.Cooldown,
            };
        }

    }
}

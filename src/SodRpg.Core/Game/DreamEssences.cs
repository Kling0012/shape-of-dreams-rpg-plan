using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 夢のエッセンス（案）：記憶ごとの「夢のソケット」へ入れ、その記憶の性質を変える MOD 独自のエッセンス。
    /// 既存の記憶の仕掛け（<see cref="GimmickEffect"/>）だけで組める12種の定義と、ソケット1つ以上の組から仕掛けへ変える規則を持つ。
    /// ソケット・保存・通信・画面はまだ無く、ゲームには何も出ない。設計と候補一覧は docs/specs/v2.10-dream-essences.md。
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

    /// <summary>ソケットに入れた夢のエッセンス1つ分。記憶は本体の型名（St_...）。</summary>
    public struct DreamSocketing
    {
        public string Memory { get; set; }
        public string EssenceId { get; set; }
        public int Quality { get; set; }
    }

    public static class DreamEssences
    {
        public const int MinQuality = 1;
        public const int MaxQuality = 3;
        /// <summary>装備できる記憶（Q・W・E・R・アイデンティティ・移動）の数。ソケットは記憶1つにつき1つ。</summary>
        public const int MaxSockets = 6;

        private const string IdPrefix = "dream.";

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

        /// <summary>仕掛けの識別子（通信で使える文字だけ）。同じ品質の同じ品は同じ識別子になる。</summary>
        public static string StarId(DreamEssenceDef def, int quality) => IdPrefix + def.Id + ".q" + quality;

        public static bool ValidQuality(int quality) => quality >= MinQuality && quality <= MaxQuality;

        /// <summary>記憶に入れられるか。記憶の型名は本体のもの。既存の判定に従い、記憶の型名で入れられない効果（移動の記憶に入れられない効果など）を断る。</summary>
        public static bool CanSocket(DreamEssenceDef def, string memory) =>
            def != null && Links.IsMemory(memory) && Gimmicks.AllowedOnMemory(def.Effect, memory);

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

        /// <summary>既存の上限と検証に通した仕掛け。入れられない組み合わせは null。</summary>
        public static GimmickEntry ToEntry(DreamEssenceDef def, int quality, string memory)
        {
            if (!CanSocket(def, memory)) return null;
            var gimmick = ToGimmick(def, quality);
            if (gimmick == null) return null;
            return Gimmicks.Clamp(new GimmickEntry { StarId = StarId(def, quality), Memory = memory, Def = gimmick });
        }

        /// <summary>
        /// ソケットの組から、有効な仕掛けを入力の順に返す。
        /// 記憶1つにつきエッセンスは1つ（先に書かれたものを使う）、ソケットは最大 <see cref="MaxSockets"/>。
        /// 同じ系統（同じ <see cref="GimmickEffect"/>）が別の記憶に入っていれば強い方だけが有効で、同じ強さなら先のもの。
        /// 入れられない組み合わせは黙って捨てず <paramref name="rejected"/> に理由つきで返す。
        /// </summary>
        public static List<GimmickEntry> Resolve(IEnumerable<DreamSocketing> sockets, out List<string> rejected)
        {
            rejected = new List<string>();
            var candidates = new List<GimmickEntry>();
            var memories = new HashSet<string>(StringComparer.Ordinal);
            int seen = 0;
            foreach (var socket in sockets ?? new DreamSocketing[0])
            {
                if (++seen > MaxSockets) { rejected.Add("socket count over " + MaxSockets); break; }
                var def = Find(socket.EssenceId);
                if (def == null) { rejected.Add("unknown essence " + socket.EssenceId); continue; }
                if (!ValidQuality(socket.Quality)) { rejected.Add("invalid quality " + socket.Quality + " for " + def.Id); continue; }
                if (!memories.Add(socket.Memory ?? "")) { rejected.Add("memory already has a socketed essence: " + socket.Memory); continue; }
                var entry = ToEntry(def, socket.Quality, socket.Memory);
                if (entry == null) { rejected.Add(def.Id + " cannot be socketed in " + socket.Memory); continue; }
                candidates.Add(entry);
            }
            var strongest = new Dictionary<GimmickEffect, GimmickEntry>();
            foreach (var entry in candidates)
                if (!strongest.TryGetValue(entry.Def.Effect, out var best) || entry.Def.ValuePrecise > best.Def.ValuePrecise)
                    strongest[entry.Def.Effect] = entry;
            var result = new List<GimmickEntry>();
            foreach (var entry in candidates)
            {
                if (ReferenceEquals(strongest[entry.Def.Effect], entry)) result.Add(entry);
                else rejected.Add(entry.StarId + " overridden by a stronger " + entry.Def.Effect + " essence");
            }
            return result;
        }
    }
}

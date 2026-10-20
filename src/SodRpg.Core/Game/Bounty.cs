using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum BountyKind
    {
        /// <summary>雑魚・通常の敵を Target 体倒す。</summary>
        Slayer = 0,
        /// <summary>エリート（ミニボス）を Target 体倒す。</summary>
        EliteHunter = 1,
        /// <summary>ボスを Target 体倒す。</summary>
        Bossbane = 2,
        /// <summary>遺物を合計 Target 個確保する。</summary>
        Collector = 3,
        /// <summary>潜行 Target 以上で確保する。</summary>
        DeepDiver = 4,
        /// <summary>レア以上の遺物を Target 個見つける。</summary>
        Treasure = 5,
        /// <summary>戦闘部屋を Target 個突破する。</summary>
        Pathfinder = 6,
        /// <summary>悪夢化した敵を Target 体倒す（深く潜らないと出ない）。</summary>
        NightmareHunter = 7,
        /// <summary>Chaos の祭壇を Target 回使う。</summary>
        ChaosSeeker = 8,
        /// <summary>商人から Target 回買う。</summary>
        Patron = 9,
        /// <summary>Memory か Essence を Target 回強化する（祭壇・強化の井戸）。</summary>
        Refiner = 10,
        /// <summary>Essence を Target 回合成する。</summary>
        Alchemist = 11,
        /// <summary>Memory か Essence を Target 回分解する。</summary>
        Recycler = 12,
        /// <summary>ハンターに取られた場所へ Target 回踏み込む。</summary>
        HunterBait = 13,
        /// <summary>悪夢の契約を Target 回結ぶ。</summary>
        PactBearer = 14,
        /// <summary>固有品を Target 個見つける。</summary>
        LegendFinder = 15,
        /// <summary>エピック以上の遺物を Target 個見つける。</summary>
        EpicFinder = 16,
        /// <summary>夢の出来事を Target 回選ぶ。</summary>
        EventTaker = 17,
        /// <summary>確保を Target 回行う。</summary>
        Securer = 18,
        /// <summary>「深く潜る」を Target 回選ぶ。</summary>
        Delver = 19,
        /// <summary>セット品を Target 個見つける。</summary>
        SetHunter = 20,
        /// <summary>悪夢の契約を Target 個以上結んだまま確保する。</summary>
        PactKeeper = 21,
        FireHunter = 22,
        ColdHunter = 23,
        LightHunter = 24,
        DarkHunter = 25,
        ShieldGiver = 26,
        AllyHealer = 27,
        MemoryQ = 28,
        MemoryW = 29,
        MemoryE = 30,
        MemoryR = 31,
        ZoneTraveler = 32,
        RelicEnhancer = 33,
        LimitBreaker = 34,
        Awakener = 35,
        LinkWeaver = 36,
        GimmickUser = 37,
        PressureDiver = 38,
        VariantHunter = 39,
        VeilHunter = 40,
        PackHunter = 41,
        PulseHunter = 42,
        LastStandHunter = 43,
    }

    /// <summary>
    /// 依頼：1回の遠征の小目標。ランの開始時に3つ配られ、達成すると欠片・調律石（未確保として鞄へ）と経験値を得る。
    /// 計画書 第2章「5分以内に判断や小目標」を補う。
    /// </summary>
    public sealed class Bounty
    {
        public BountyKind Kind { get; set; }
        public int Target { get; set; }
        public int Progress { get; set; }
        public bool Done { get; set; }
        public int RewardShards { get; set; }
        public int RewardTuning { get; set; }
        public int RewardXp { get; set; }

        public Bounty Clone() => (Bounty)MemberwiseClone();

        public string Describe()
        {
            int t = Target;
            switch (Kind)
            {
                case BountyKind.Slayer: return Loc.T($"敵を{t}体倒す", $"Defeat {t} enemies", $"击败{t}个敌人");
                case BountyKind.EliteHunter: return Loc.T($"エリートを{t}体倒す", $"Defeat {t} elites", $"击败{t}个精英");
                case BountyKind.Bossbane: return Loc.T($"ボスを{t}体倒す", $"Defeat {t} boss(es)", $"击败{t}个Boss");
                case BountyKind.Collector: return Loc.T($"遺物を{t}個確保する", $"Secure {t} relics", $"存入仓库{t}件遗物");
                case BountyKind.DeepDiver: return Loc.T($"潜行{t}以上で確保する", $"Secure at delve level {t}+", $"在深度{t}以上存入仓库");
                case BountyKind.Treasure: return Loc.T($"レア以上の遺物を{t}個見つける", $"Find {t} Rare+ relic(s)", $"找到{t}件稀有以上遗物");
                case BountyKind.NightmareHunter: return Loc.T($"悪夢化した敵を{t}体倒す", $"Slay {t} nightmare(s)", $"击败{t}个梦魇化敌人");
                case BountyKind.ChaosSeeker: return Loc.T($"混沌を{t}回使う", $"Use Chaos {t} time(s)", $"使用{t}次混沌");
                case BountyKind.Patron: return Loc.T($"商人から{t}回買う", $"Buy from merchants {t} time(s)", $"从商人处购买{t}次");
                case BountyKind.Refiner: return Loc.T($"記憶かエッセンスを{t}回強化する", $"Upgrade Memories/Essences {t} time(s)", $"强化记忆或精华{t}次");
                case BountyKind.Alchemist: return Loc.T($"エッセンスを{t}回合成する", $"Merge Essences {t} time(s)", $"合成精华{t}次");
                case BountyKind.Recycler: return Loc.T($"記憶かエッセンスを{t}回分解する", $"Dismantle Memories/Essences {t} time(s)", $"分解记忆或精华{t}次");
                case BountyKind.HunterBait: return Loc.T($"ハンターの領域へ{t}回踏み込む", $"Enter hunter territory {t} time(s)", $"踏入猎人的领地{t}次");
                case BountyKind.PactBearer: return Loc.T($"悪夢の契約を{t}回結ぶ", $"Swear {t} nightmare pact(s)", $"结下{t}次梦魇契约");
                case BountyKind.LegendFinder: return Loc.T($"固有品を{t}個見つける", $"Find {t} legendary relic(s)", $"找到{t}件传说");
                case BountyKind.EpicFinder: return Loc.T($"エピック以上の遺物を{t}個見つける", $"Find {t} Epic+ relic(s)", $"找到{t}件史诗以上遗物");
                case BountyKind.EventTaker: return Loc.T($"夢の出来事を{t}回選ぶ", $"Take {t} dream event(s)", $"选择{t}次梦境事件");
                case BountyKind.Securer: return Loc.T($"確保を{t}回行う", $"Secure {t} time(s)", $"存入仓库{t}次");
                case BountyKind.Delver: return Loc.T($"「深く潜る」を{t}回選ぶ", $"Delve {t} time(s)", $"选择{t}次“深入下潜”");
                case BountyKind.SetHunter: return Loc.T($"セット品を{t}個見つける", $"Find {t} set piece(s)", $"找到{t}件套装部件");
                case BountyKind.PactKeeper: return Loc.T($"悪夢の契約を{t}つ結んだまま確保する", $"Secure while bound by {t}+ nightmare pact(s)", $"身负{t}个以上梦魇契约存入仓库");
                case BountyKind.FireHunter: return Loc.T($"火が付いた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Fire", $"击败{t}个附带火属性的敌人");
                case BountyKind.ColdHunter: return Loc.T($"冷気を受けた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Cold", $"击败{t}个受冰属性影响的敌人");
                case BountyKind.LightHunter: return Loc.T($"光が付いた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Light", $"击败{t}个附带光属性的敌人");
                case BountyKind.DarkHunter: return Loc.T($"闇が付いた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Dark", $"击败{t}个附带暗属性的敌人");
                case BountyKind.ShieldGiver: return Loc.T($"自分か味方に障壁を{t}回付ける", $"Grant shields to yourself or allies {t} times", $"为自己或队友提供{t}次护盾");
                case BountyKind.AllyHealer: return Loc.T($"別の旅人のHPを{t}回回復する", $"Restore another traveler's health {t} times", $"治疗其他旅人{t}次");
                case BountyKind.MemoryQ: return Loc.T($"Q枠の記憶を{t}回使う", $"Use a Memory in the Q slot {t} times", $"使用{t}次Q栏记忆");
                case BountyKind.MemoryW: return Loc.T($"W枠の記憶を{t}回使う", $"Use a Memory in the W slot {t} times", $"使用{t}次W栏记忆");
                case BountyKind.MemoryE: return Loc.T($"E枠の記憶を{t}回使う", $"Use a Memory in the E slot {t} times", $"使用{t}次E栏记忆");
                case BountyKind.MemoryR: return Loc.T($"R枠の記憶を{t}回使う", $"Use a Memory in the R slot {t} times", $"使用{t}次R栏记忆");
                case BountyKind.ZoneTraveler: return Loc.T($"次のゾーンへ{t}回進む", $"Travel to the next zone {t} times", $"前往下一区域{t}次");
                case BountyKind.RelicEnhancer: return Loc.T($"遺物の強化を{t}回行う", $"Enhance relics {t} times", $"强化遗物{t}次");
                case BountyKind.LimitBreaker: return Loc.T($"遺物を{t}回限界突破する", $"Limit break relics {t} times", $"遗物极限突破{t}次");
                case BountyKind.Awakener: return Loc.T($"装備した固有品の覚醒を{t}段まで進める", $"Reach Awakening {t} on an equipped legendary relic", $"将装备中的传说觉醒推进到{t}级");
                case BountyKind.LinkWeaver: return Loc.T($"連携を同時に{t}つ成立させる", $"Satisfy {t} links at once", $"同时达成{t}个连携");
                case BountyKind.GimmickUser: return Loc.T($"記憶の仕掛けを{t}回発動する", $"Trigger Memory gimmicks {t} times", $"触发{t}次记忆的机关");
                case BountyKind.PressureDiver: return Loc.T($"夢の圧による敵のHP倍率を{t}%以上にする", $"Reach {t}% enemy health from Dream Pressure", $"使梦境之压带来的敌人生命倍率达到{t}%以上");
                case BountyKind.VariantHunter: return Loc.T($"夢の変種を{t}体倒す", $"Defeat {t} dream variants", $"击败{t}个梦境变种");
                case BountyKind.VeilHunter: return Loc.T($"霞衣の性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Veiled trait", $"击败{t}个具有霞衣性质的敌人");
                case BountyKind.PackHunter: return Loc.T($"群れの守りの性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Packbound trait", $"击败{t}个具有群守性质的敌人");
                case BountyKind.PulseHunter: return Loc.T($"明滅の性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Pulsing trait", $"击败{t}个具有明灭性质的敌人");
                case BountyKind.LastStandHunter: return Loc.T($"最後の殻の性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Last Stand trait", $"击败{t}个具有最后之壳性质的敌人");
                default: return Loc.T($"戦闘部屋を{t}個突破する", $"Clear {t} combat rooms", $"突破{t}个战斗房间");
            }
        }

        public string RewardText(double mult = 1.0)
        {
            int shards = (int)Math.Round(RewardShards * mult);
            int tuningN = (int)Math.Round(RewardTuning * mult);
            int xp = (int)Math.Round(RewardXp * mult);
            string tuning = tuningN > 0 ? Loc.T($"、調律石{tuningN}", $", {tuningN} tuning", $"、调律石{tuningN}") : "";
            return Loc.T($"報酬：欠片{shards}{tuning}、経験値{xp}", $"Reward: {shards} shards{tuning}, {xp} xp", $"奖励：碎片{shards}{tuning}、经验值{xp}");
        }
    }

    public static class Bounties
    {
        public const int PerRun = 3;

        private sealed class Template
        {
            public BountyKind Kind;
            public int Min, Max, Shards, Tuning, Xp;
        }

        private static readonly Template[] Templates =
        {
            new Template { Kind = BountyKind.Slayer, Min = 80, Max = 160, Shards = 15, Xp = 30 },
            new Template { Kind = BountyKind.EliteHunter, Min = 2, Max = 4, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.Bossbane, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.Collector, Min = 4, Max = 8, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.DeepDiver, Min = 1, Max = 3, Shards = 25, Tuning = 1, Xp = 50 },
            new Template { Kind = BountyKind.Treasure, Min = 1, Max = 3, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.Pathfinder, Min = 4, Max = 8, Shards = 15, Xp = 30 },
            new Template { Kind = BountyKind.NightmareHunter, Min = 1, Max = 3, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.ChaosSeeker, Min = 1, Max = 2, Shards = 25, Xp = 40 },
            new Template { Kind = BountyKind.Patron, Min = 1, Max = 3, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.Refiner, Min = 2, Max = 4, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.Alchemist, Min = 1, Max = 2, Shards = 25, Tuning = 1, Xp = 50 },
            new Template { Kind = BountyKind.Recycler, Min = 1, Max = 3, Shards = 15, Xp = 30 },
            new Template { Kind = BountyKind.HunterBait, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.PactBearer, Min = 1, Max = 2, Shards = 35, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.LegendFinder, Min = 1, Max = 1, Shards = 40, Tuning = 1, Xp = 70 },
            new Template { Kind = BountyKind.EpicFinder, Min = 1, Max = 2, Shards = 25, Xp = 50 },
            new Template { Kind = BountyKind.EventTaker, Min = 1, Max = 2, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.Securer, Min = 2, Max = 3, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.Delver, Min = 1, Max = 3, Shards = 25, Tuning = 1, Xp = 50 },
            new Template { Kind = BountyKind.SetHunter, Min = 1, Max = 1, Shards = 40, Tuning = 1, Xp = 70 },
            new Template { Kind = BountyKind.PactKeeper, Min = 1, Max = 2, Shards = 35, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.FireHunter, Min = 12, Max = 24, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.ColdHunter, Min = 12, Max = 24, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.LightHunter, Min = 12, Max = 24, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.DarkHunter, Min = 12, Max = 24, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.ShieldGiver, Min = 6, Max = 12, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.AllyHealer, Min = 4, Max = 8, Shards = 25, Xp = 45 },
            new Template { Kind = BountyKind.MemoryQ, Min = 12, Max = 24, Shards = 15, Xp = 30 },
            new Template { Kind = BountyKind.MemoryW, Min = 12, Max = 24, Shards = 15, Xp = 30 },
            new Template { Kind = BountyKind.MemoryE, Min = 12, Max = 24, Shards = 15, Xp = 30 },
            new Template { Kind = BountyKind.MemoryR, Min = 3, Max = 6, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.ZoneTraveler, Min = 2, Max = 4, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.RelicEnhancer, Min = 2, Max = 4, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.LimitBreaker, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.Awakener, Min = 1, Max = 3, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.LinkWeaver, Min = 1, Max = 2, Shards = 25, Xp = 50 },
            new Template { Kind = BountyKind.GimmickUser, Min = 8, Max = 16, Shards = 20, Xp = 40 },
            new Template { Kind = BountyKind.PressureDiver, Min = 125, Max = 175, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.VariantHunter, Min = 1, Max = 3, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.VeilHunter, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.PackHunter, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.PulseHunter, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
            new Template { Kind = BountyKind.LastStandHunter, Min = 1, Max = 2, Shards = 30, Tuning = 1, Xp = 60 },
        };

        /// <summary>種類の重ならない依頼を count 個作る。目標が大きいほど報酬も増える。</summary>
        public static List<Bounty> Roll(Rng rng, int count = PerRun, Func<BountyKind, bool> allowed = null)
        {
            var pool = new List<Template>(Templates);
            if (allowed != null) pool.RemoveAll(t => !allowed(t.Kind));
            var result = new List<Bounty>();
            while (result.Count < count && pool.Count > 0)
            {
                // 固有品・セット品の依頼は、ほかの依頼の1/3の重みで選ぶ。
                int totalWeight = 0;
                foreach (var candidate in pool)
                    totalWeight += candidate.Kind == BountyKind.LegendFinder || candidate.Kind == BountyKind.SetHunter ? 1 : 3;
                int pick = rng.Range(0, totalWeight - 1);
                int index = 0;
                for (; index < pool.Count - 1; index++)
                {
                    var candidate = pool[index];
                    int selectionWeight = candidate.Kind == BountyKind.LegendFinder || candidate.Kind == BountyKind.SetHunter ? 1 : 3;
                    if (pick < selectionWeight) break;
                    pick -= selectionWeight;
                }
                var t = pool[index];
                pool.RemoveAt(index);
                int target = rng.Range(t.Min, t.Max);
                // 目標の重さ（0〜1）で報酬を最大1.5倍まで上げる。
                double weight = t.Max == t.Min ? 0 : (double)(target - t.Min) / (t.Max - t.Min);
                double mult = 1.0 + 0.5 * weight;
                result.Add(new Bounty
                {
                    Kind = t.Kind,
                    Target = target,
                    RewardShards = (int)Math.Round(t.Shards * mult),
                    RewardTuning = t.Tuning,
                    RewardXp = (int)Math.Round(t.Xp * mult),
                });
            }
            return result;
        }

        /// <summary>
        /// この遠征で進められる依頼か。ソロでは「別の旅人を回復」、インフィニティでは「次のゾーンへ進む」
        /// 「確保」（インフィニティの確保は遠征を終える1回だけ）が進まない。
        /// </summary>
        public static bool Reachable(BountyKind kind, bool solo, bool infinity)
            => !(solo && kind == BountyKind.AllyHealer)
                && !(infinity && (kind == BountyKind.ZoneTraveler || kind == BountyKind.Securer));
    }
}

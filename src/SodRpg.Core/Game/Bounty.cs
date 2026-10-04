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
                case BountyKind.Slayer: return Loc.T($"敵を{t}体倒す", $"Defeat {t} enemies");
                case BountyKind.EliteHunter: return Loc.T($"エリートを{t}体倒す", $"Defeat {t} elites");
                case BountyKind.Bossbane: return Loc.T($"ボスを{t}体倒す", $"Defeat {t} boss(es)");
                case BountyKind.Collector: return Loc.T($"遺物を{t}個確保する", $"Secure {t} relics");
                case BountyKind.DeepDiver: return Loc.T($"潜行{t}以上で確保する", $"Secure at delve level {t}+");
                case BountyKind.Treasure: return Loc.T($"レア以上の遺物を{t}個見つける", $"Find {t} Rare+ relic(s)");
                case BountyKind.NightmareHunter: return Loc.T($"悪夢化した敵を{t}体倒す", $"Slay {t} nightmare(s)");
                case BountyKind.ChaosSeeker: return Loc.T($"混沌を{t}回使う", $"Use Chaos {t} time(s)");
                case BountyKind.Patron: return Loc.T($"商人から{t}回買う", $"Buy from merchants {t} time(s)");
                case BountyKind.Refiner: return Loc.T($"記憶かエッセンスを{t}回強化する", $"Upgrade Memories/Essences {t} time(s)");
                case BountyKind.Alchemist: return Loc.T($"エッセンスを{t}回合成する", $"Merge Essences {t} time(s)");
                case BountyKind.Recycler: return Loc.T($"記憶かエッセンスを{t}回分解する", $"Dismantle Memories/Essences {t} time(s)");
                case BountyKind.HunterBait: return Loc.T($"ハンターの領域へ{t}回踏み込む", $"Enter hunter territory {t} time(s)");
                case BountyKind.PactBearer: return Loc.T($"悪夢の契約を{t}回結ぶ", $"Swear {t} nightmare pact(s)");
                case BountyKind.LegendFinder: return Loc.T($"固有品を{t}個見つける", $"Find {t} legendary relic(s)");
                case BountyKind.EpicFinder: return Loc.T($"エピック以上の遺物を{t}個見つける", $"Find {t} Epic+ relic(s)");
                case BountyKind.EventTaker: return Loc.T($"夢の出来事を{t}回選ぶ", $"Take {t} dream event(s)");
                case BountyKind.Securer: return Loc.T($"確保を{t}回行う", $"Secure {t} time(s)");
                case BountyKind.Delver: return Loc.T($"「深く潜る」を{t}回選ぶ", $"Delve {t} time(s)");
                case BountyKind.SetHunter: return Loc.T($"セット品を{t}個見つける", $"Find {t} set piece(s)");
                case BountyKind.PactKeeper: return Loc.T($"悪夢の契約を{t}つ結んだまま確保する", $"Secure while bound by {t}+ nightmare pact(s)");
                case BountyKind.FireHunter: return Loc.T($"火が付いた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Fire");
                case BountyKind.ColdHunter: return Loc.T($"冷気を受けた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Cold");
                case BountyKind.LightHunter: return Loc.T($"光が付いた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Light");
                case BountyKind.DarkHunter: return Loc.T($"闇が付いた敵を{t}体倒す", $"Defeat {t} enemies afflicted with Dark");
                case BountyKind.ShieldGiver: return Loc.T($"自分か味方に障壁を{t}回付ける", $"Grant shields to yourself or allies {t} times");
                case BountyKind.AllyHealer: return Loc.T($"別の旅人のHPを{t}回回復する", $"Restore another traveler's health {t} times");
                case BountyKind.MemoryQ: return Loc.T($"Q枠の記憶を{t}回使う", $"Use a Memory in the Q slot {t} times");
                case BountyKind.MemoryW: return Loc.T($"W枠の記憶を{t}回使う", $"Use a Memory in the W slot {t} times");
                case BountyKind.MemoryE: return Loc.T($"E枠の記憶を{t}回使う", $"Use a Memory in the E slot {t} times");
                case BountyKind.MemoryR: return Loc.T($"R枠の記憶を{t}回使う", $"Use a Memory in the R slot {t} times");
                case BountyKind.ZoneTraveler: return Loc.T($"次のゾーンへ{t}回進む", $"Travel to the next zone {t} times");
                case BountyKind.RelicEnhancer: return Loc.T($"遺物の強化を{t}回行う", $"Enhance relics {t} times");
                case BountyKind.LimitBreaker: return Loc.T($"遺物を{t}回限界突破する", $"Limit break relics {t} times");
                case BountyKind.Awakener: return Loc.T($"装備した固有品の覚醒を{t}段まで進める", $"Reach Awakening {t} on an equipped legendary relic");
                case BountyKind.LinkWeaver: return Loc.T($"連携を同時に{t}つ成立させる", $"Satisfy {t} links at once");
                case BountyKind.GimmickUser: return Loc.T($"記憶の仕掛けを{t}回発動する", $"Trigger Memory gimmicks {t} times");
                case BountyKind.PressureDiver: return Loc.T($"夢の圧による敵のHP倍率を{t}%以上にする", $"Reach {t}% enemy health from Dream Pressure");
                case BountyKind.VariantHunter: return Loc.T($"夢の変種を{t}体倒す", $"Defeat {t} dream variants");
                case BountyKind.VeilHunter: return Loc.T($"霞衣の性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Veiled trait");
                case BountyKind.PackHunter: return Loc.T($"群れの守りの性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Packbound trait");
                case BountyKind.PulseHunter: return Loc.T($"明滅の性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Pulsing trait");
                case BountyKind.LastStandHunter: return Loc.T($"最後の殻の性質を持つ敵を{t}体倒す", $"Defeat {t} enemies with the Last Stand trait");
                default: return Loc.T($"戦闘部屋を{t}個突破する", $"Clear {t} combat rooms");
            }
        }

        public string RewardText(double mult = 1.0)
        {
            int shards = (int)Math.Round(RewardShards * mult);
            int tuningN = (int)Math.Round(RewardTuning * mult);
            int xp = (int)Math.Round(RewardXp * mult);
            string tuning = tuningN > 0 ? Loc.T($"、調律石{tuningN}", $", {tuningN} tuning") : "";
            return Loc.T($"報酬：欠片{shards}{tuning}、経験値{xp}", $"Reward: {shards} shards{tuning}, {xp} xp");
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
        public static List<Bounty> Roll(Rng rng, int count = PerRun)
        {
            var pool = new List<Template>(Templates);
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
    }
}

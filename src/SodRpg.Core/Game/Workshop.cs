using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>夢の工房の強化。アカウント共通で、遊びやすさと選択肢を増やす（攻撃力などの強さは上げない）。</summary>
    public enum Upgrade
    {
        BigSatchel = 0,
        WideStash = 1,
        BountyReroll = 2,
        LostLantern = 3,
        EchoAmp = 4,
        PactEye = 5,
        /// <summary>残響の灯：全滅時の残響が増える（v1.29）。</summary>
        EchoLantern = 6,
        /// <summary>遺失物の地図：遺失物の回収が早くなる（v1.29）。</summary>
        LostMap = 7,
        /// <summary>契約の星座：契約の選択肢が増える（v1.29）。</summary>
        PactStars = 8,
    }

    public sealed class UpgradeDef
    {
        public Upgrade Id;
        public string Key;
        public Txt Name;
        public Txt Description;
        /// <summary>段階ごとの費用（欠片, 調律石）。要素数が最大段階。</summary>
        public (int Shards, int Tuning)[] Costs;

        public int MaxLevel => Costs.Length;
    }

    /// <summary>
    /// 夢の工房（計画書 第11章「研究」を簡略化）。Hades の鏡のように、余った資源で恒久的な利便性を解放する。
    /// </summary>
    public static class Workshop
    {
        /// <summary>v1.5 で廃止した強化。保存に残っていれば費用を返す。</summary>
        public static readonly IReadOnlyList<UpgradeDef> Retired = new[]
        {
            new UpgradeDef { Id = Upgrade.LostLantern, Key = "lostLantern", Name = new Txt("遺失物の灯", "Lantern of the Lost"), Description = new Txt("", ""), Costs = new[] { (250, 3) } },
            new UpgradeDef { Id = Upgrade.EchoAmp, Key = "echoAmp", Name = new Txt("残響の増幅", "Echo Amplifier"), Description = new Txt("", ""), Costs = new[] { (150, 2), (300, 4) } },
            new UpgradeDef { Id = Upgrade.PactEye, Key = "pactEye", Name = new Txt("契約の目利き", "Pact Appraiser"), Description = new Txt("", ""), Costs = new[] { (300, 5) } },
        };

        public static bool TryGetRetired(string key, out UpgradeDef def)
        {
            foreach (var d in Retired)
            {
                if (d.Key == key)
                {
                    def = d;
                    return true;
                }
            }
            def = null;
            return false;
        }

        /// <summary>廃止した強化の段階 lv までに払った費用。</summary>
        public static (int Shards, int Tuning) Refund(UpgradeDef def, int lv)
        {
            int s = 0, t = 0;
            for (int i = 0; i < Math.Min(lv, def.MaxLevel); i++)
            {
                s += def.Costs[i].Shards;
                t += def.Costs[i].Tuning;
            }
            return (s, t);
        }

        public static readonly IReadOnlyList<UpgradeDef> All = new[]
        {
            new UpgradeDef
            {
                Id = Upgrade.BigSatchel, Key = "bigSatchel",
                Name = new Txt("大きな鞄", "Bigger Satchel"),
                Description = new Txt("遠征中に持ち歩ける遺物が1段につき5個増えます（10段まで）。", "Carry 5 more unsecured relics per level on an expedition (up to 10 levels)."),
                Costs = new[] { (100, 0), (200, 2), (400, 4), (600, 6), (800, 8), (1000, 10), (1300, 12), (1600, 14), (2000, 16), (2500, 20) },
            },
            new UpgradeDef
            {
                Id = Upgrade.WideStash, Key = "wideStash",
                Name = new Txt("広い保管庫", "Wider Stash"),
                Description = new Txt("保管庫に入る遺物が増えます（1〜3段目は20個ずつ、4〜10段目は40個ずつ）。", "Your stash holds more relics (20 per level for levels 1-3, 40 per level for levels 4-10)."),
                Costs = new[] { (80, 0), (160, 1), (320, 3), (500, 5), (700, 7), (900, 9), (1200, 11), (1500, 13), (1900, 16), (2400, 20) },
            },
            new UpgradeDef
            {
                Id = Upgrade.BountyReroll, Key = "bountyReroll",
                Name = new Txt("依頼の引き直し", "Bounty Reroll"),
                Description = new Txt("依頼を引き直せる回数が、遠征ごとに1回増えます。", "One more bounty reroll per expedition."),
                Costs = new[] { (150, 2), (300, 4) },
            },
            new UpgradeDef
            {
                Id = Upgrade.EchoLantern, Key = "echoLantern",
                Name = new Txt("残響の灯", "Echo Lantern"),
                Description = new Txt("全滅したときに戻ってくる欠片が、段階ごとに5%増えます。", "Shard echoes rise by 5% per level if your party falls."),
                Costs = new[] { (200, 2), (400, 5) },
            },
            new UpgradeDef
            {
                Id = Upgrade.LostMap, Key = "lostMap",
                Name = new Txt("遺失物の地図", "Map of the Lost"),
                Description = new Txt("遺失物を取り戻すのに必要な戦闘部屋が1つ減ります。", "Clear one fewer combat room to recover a lost relic."),
                Costs = new[] { (250, 3) },
            },
            new UpgradeDef
            {
                Id = Upgrade.PactStars, Key = "pactStars",
                Name = new Txt("契約の星座", "Pact Constellation"),
                Description = new Txt("深く潜るときの契約の選択肢が1つ増えます。", "One more pact to choose from when you delve deeper."),
                Costs = new[] { (350, 6) },
            },
        };

        public static UpgradeDef Get(Upgrade u)
        {
            foreach (var d in All)
                if (d.Id == u) return d;
            foreach (var d in Retired)
                if (d.Id == u) return d;
            throw new ArgumentOutOfRangeException(nameof(u));
        }

        public static bool TryGetByKey(string key, out UpgradeDef def)
        {
            foreach (var d in All)
            {
                if (d.Key == key)
                {
                    def = d;
                    return true;
                }
            }
            def = null;
            return false;
        }

        public static int Level(Profile p, Upgrade u) => p.Upgrades.TryGetValue(u, out int lv) ? lv : 0;

        public static int SatchelCapacity(Profile p) => Content.SatchelCapacity + 5 * Level(p, Upgrade.BigSatchel);
        public static int StashCapacity(Profile p) => Content.StashCapacity + StashBonus(Level(p, Upgrade.WideStash));

        /// <summary>広い保管庫の段ごとの増分：1〜3段目は20個、4段目からは40個（v1.31）。</summary>
        public static int StashBonus(int level)
        {
            level = Math.Max(0, level);
            return 20 * Math.Min(level, 3) + 40 * Math.Max(0, level - 3);
        }
        public static int RoomsToRecover(Profile p) => Math.Max(2, Content.RoomsToRecoverLost - Level(p, Upgrade.LostMap));
        public static int EchoPercent(Profile p) => 25 + 5 * (p == null ? 0 : Level(p, Upgrade.EchoLantern));
        public static int PactsOffered(Profile p) => Pacts.Offered + (p == null ? 0 : Level(p, Upgrade.PactStars));
        public static int RerollsPerRun(Profile p) => Level(p, Upgrade.BountyReroll);

        /// <summary>全滅時に持ち帰る欠片（切り上げ、1以上ある場合は最低1）。</summary>
        public static int Echo(Profile p, int satchelShards)
        {
            if (satchelShards <= 0) return 0;
            int pct = EchoPercent(p);
            return Math.Max(1, (satchelShards * pct + 99) / 100);
        }
    }
}

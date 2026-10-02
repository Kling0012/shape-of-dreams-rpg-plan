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
                Description = new Txt("遠征中に持ち歩ける遺物が5個増えます。", "Carry 5 more unsecured relics on an expedition."),
                Costs = new[] { (100, 0), (200, 2), (400, 4) },
            },
            new UpgradeDef
            {
                Id = Upgrade.WideStash, Key = "wideStash",
                Name = new Txt("広い保管庫", "Wider Stash"),
                Description = new Txt("保管庫に入る遺物が20個増えます。", "Your stash holds 20 more relics."),
                Costs = new[] { (80, 0), (160, 1), (320, 3) },
            },
            new UpgradeDef
            {
                Id = Upgrade.BountyReroll, Key = "bountyReroll",
                Name = new Txt("依頼の引き直し", "Bounty Reroll"),
                Description = new Txt("依頼を引き直せる回数が、遠征ごとに1回増えます。", "One more bounty reroll per expedition."),
                Costs = new[] { (150, 2), (300, 4) },
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
        public static int StashCapacity(Profile p) => Content.StashCapacity + 20 * Level(p, Upgrade.WideStash);
        public static int RoomsToRecover(Profile p) => Content.RoomsToRecoverLost;
        public static int EchoPercent(Profile p) => 25;
        public static int PactsOffered(Profile p) => Pacts.Offered;
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

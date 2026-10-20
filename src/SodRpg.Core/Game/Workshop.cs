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
        /// <summary>禁忌の職人：禁忌化・宿敵・オレンジ枠の再調律の上限の引き上げを解放する（#415）。</summary>
        ForbiddenSmith = 9,
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
            new UpgradeDef { Id = Upgrade.LostLantern, Key = "lostLantern", Name = new Txt("遺失物の灯", "Lantern of the Lost", "遗失之灯"), Description = new Txt("", "", ""), Costs = new[] { (250, 3) } },
            new UpgradeDef { Id = Upgrade.EchoAmp, Key = "echoAmp", Name = new Txt("残響の増幅", "Echo Amplifier", "回响增幅器"), Description = new Txt("", "", ""), Costs = new[] { (150, 2), (300, 4) } },
            new UpgradeDef { Id = Upgrade.PactEye, Key = "pactEye", Name = new Txt("契約の目利き", "Pact Appraiser", "契约鉴定师"), Description = new Txt("", "", ""), Costs = new[] { (300, 5) } },
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
                Name = new Txt("大きな鞄", "Bigger Satchel", "大背包"),
                Description = new Txt($"遠征中に持ち歩ける遺物が1段につき{ForgeBalance.BigSatchelCapacityPerLevel}個増えます（{ForgeBalance.BigSatchelCosts.Length}段まで）。", $"Carry {ForgeBalance.BigSatchelCapacityPerLevel} more unsecured relics per level on an expedition (up to {ForgeBalance.BigSatchelCosts.Length} levels).", $"远征中每级可多携带{ForgeBalance.BigSatchelCapacityPerLevel}件尚未存入仓库的遗物（最多{ForgeBalance.BigSatchelCosts.Length}级）。"),
                Costs = ForgeBalance.BigSatchelCosts,
            },
            new UpgradeDef
            {
                Id = Upgrade.WideStash, Key = "wideStash",
                Name = new Txt("広い保管庫", "Wider Stash", "扩容仓库"),
                Description = new Txt($"保管庫に入る遺物が増えます（1〜{ForgeBalance.WideStashFirstBandLevels}段目は{ForgeBalance.WideStashFirstBandCapacity}個ずつ、{ForgeBalance.WideStashFirstBandLevels + 1}〜{ForgeBalance.WideStashCosts.Length}段目は{ForgeBalance.WideStashLaterCapacity}個ずつ）。", $"Your stash holds more relics ({ForgeBalance.WideStashFirstBandCapacity} per level for levels 1-{ForgeBalance.WideStashFirstBandLevels}, {ForgeBalance.WideStashLaterCapacity} per level for levels {ForgeBalance.WideStashFirstBandLevels + 1}-{ForgeBalance.WideStashCosts.Length}).", $"仓库每级增加{ForgeBalance.WideStashFirstBandCapacity}件遗物容量，适用于第1至{ForgeBalance.WideStashFirstBandLevels}级；之后每级增加{ForgeBalance.WideStashLaterCapacity}件，适用于第{ForgeBalance.WideStashFirstBandLevels + 1}至{ForgeBalance.WideStashCosts.Length}级。"),
                Costs = ForgeBalance.WideStashCosts,
            },
            new UpgradeDef
            {
                Id = Upgrade.BountyReroll, Key = "bountyReroll",
                Name = new Txt("依頼の引き直し", "Bounty Reroll", "重选委托"),
                Description = new Txt($"依頼を引き直せる回数が、遠征ごとに{ForgeBalance.BountyRerollRerollsPerLevel}回増えます。", $"{ForgeBalance.BountyRerollRerollsPerLevel} more bounty reroll(s) per expedition per level.", $"每级使每次远征可重选委托的次数增加{ForgeBalance.BountyRerollRerollsPerLevel}次。"),
                Costs = ForgeBalance.BountyRerollCosts,
            },
            new UpgradeDef
            {
                Id = Upgrade.EchoLantern, Key = "echoLantern",
                Name = new Txt("残響の灯", "Echo Lantern", "回响之灯"),
                Description = new Txt($"全滅したときに戻ってくる欠片が、段階ごとに{ForgeBalance.EchoLanternPercentPerLevel}%増えます。", $"Shard echoes rise by {ForgeBalance.EchoLanternPercentPerLevel}% per level if your party falls.", $"队伍全灭时，每级使返还的碎片回响增加{ForgeBalance.EchoLanternPercentPerLevel}%。"),
                Costs = ForgeBalance.EchoLanternCosts,
            },
            new UpgradeDef
            {
                Id = Upgrade.LostMap, Key = "lostMap",
                Name = new Txt("遺失物の地図", "Map of the Lost", "遗失物地图"),
                Description = new Txt($"遺失物を取り戻すのに必要な戦闘部屋が{ForgeBalance.LostMapRoomsPerLevel}つ減ります。", $"Clear {ForgeBalance.LostMapRoomsPerLevel} fewer combat room(s) to recover a lost relic.", $"找回遗失的遗物所需通关的战斗房间减少{ForgeBalance.LostMapRoomsPerLevel}间。"),
                Costs = ForgeBalance.LostMapCosts,
            },
            new UpgradeDef
            {
                Id = Upgrade.PactStars, Key = "pactStars",
                Name = new Txt("契約の星座", "Pact Constellation", "契约星座"),
                Description = new Txt($"深く潜るときの契約の選択肢が{ForgeBalance.PactStarsOffersPerLevel}つ増えます。", $"{ForgeBalance.PactStarsOffersPerLevel} more pact(s) to choose from when you delve deeper.", $"继续下潜时可选择的契约增加{ForgeBalance.PactStarsOffersPerLevel}个。"),
                Costs = ForgeBalance.PactStarsCosts,
            },
            new UpgradeDef
            {
                Id = Upgrade.ForbiddenSmith, Key = "forbiddenSmith",
                Name = new Txt("禁忌の職人", "Forbidden Smith", "禁忌铁匠"),
                Description = new Txt($"ボスとエリートがまれに宿敵「深淵の追跡者」として強くなって現れ、倒すたびに因縁が深まり、追跡者はより強く、より頻繁に現れます（協力プレイでは誰か1人が解放していれば現れます）。宿敵化したボスを倒すと禁忌の核が手に入ります。鍛冶でセットに属さない固有品（オレンジ枠）を一度だけ禁忌化でき、オレンジ枠の再調律の上限が{Forbidden.OrangeRetuneBonus}回増えます。",
                    $"Bosses and elites sometimes appear as the empowered nemesis \"Abyssal Stalker\"; each defeat deepens your grudge, making it stronger and more frequent (in co-op, anyone having this unlocked is enough). Nemesis bosses drop forbidden cores. Lets the forge make a non-set unique (orange frame) forbidden once, and raises orange-frame retunes by {Forbidden.OrangeRetuneBonus}.", $"首领和精英偶尔会作为强化的宿敌“深渊追猎者”出现；每次击败都会加深恩怨，使其更强且出现得更频繁（合作游戏中任意一人解锁即可）。击败宿敌首领可获得禁忌核心。可在锻造中将不属于套装的独特物品（橙框）禁忌化一次，并将橙框物品的再调律次数上限提高{Forbidden.OrangeRetuneBonus}次。"),
                Costs = Forbidden.SmithCosts,
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

        public static int SatchelCapacity(Profile p) => SatchelCapacityAt(Level(p, Upgrade.BigSatchel));
        /// <summary>大きな鞄が指定の段のときの鞄の上限。工房の「次の段」表示もこれを使う。</summary>
        public static int SatchelCapacityAt(int level) => Content.SatchelCapacity + ForgeBalance.BigSatchelCapacityPerLevel * Math.Max(0, level);
        public static int StashCapacity(Profile p) => Content.StashCapacity + StashBonus(Level(p, Upgrade.WideStash));

        /// <summary>表の2つの段階帯の増分を累計する。</summary>
        public static int StashBonus(int level)
        {
            level = Math.Max(0, level);
            return ForgeBalance.WideStashFirstBandCapacity * Math.Min(level, ForgeBalance.WideStashFirstBandLevels)
                + ForgeBalance.WideStashLaterCapacity * Math.Max(0, level - ForgeBalance.WideStashFirstBandLevels);
        }
        public static int RoomsToRecover(Profile p) => Math.Max(ForgeBalance.LostMapMinimumRooms, Content.RoomsToRecoverLost - ForgeBalance.LostMapRoomsPerLevel * Level(p, Upgrade.LostMap));
        public static int EchoPercent(Profile p) => ForgeBalance.EchoLanternBasePercent + ForgeBalance.EchoLanternPercentPerLevel * (p == null ? 0 : Level(p, Upgrade.EchoLantern));
        public static int PactsOffered(Profile p) => Pacts.Offered + ForgeBalance.PactStarsOffersPerLevel * (p == null ? 0 : Level(p, Upgrade.PactStars));
        public static int RerollsPerRun(Profile p) => RerollsAt(Level(p, Upgrade.BountyReroll));
        public static int RerollsAt(int level) => ForgeBalance.BountyRerollRerollsPerLevel * Math.Max(0, level);

        /// <summary>全滅時に持ち帰る欠片（切り上げ、1以上ある場合は最低1）。</summary>
        public static int Echo(Profile p, int satchelShards)
        {
            if (satchelShards <= 0) return 0;
            int pct = EchoPercent(p);
            return Math.Max(1, (satchelShards * pct + 99) / 100);
        }
    }
}

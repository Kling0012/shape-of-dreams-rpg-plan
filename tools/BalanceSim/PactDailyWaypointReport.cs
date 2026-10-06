using SodRpg.Core.Game;

namespace BalanceSim;

internal static class PactDailyWaypointReport
{
    public static IReadOnlyList<QuantityMetricValue> Measure()
    {
        var values = new List<QuantityMetricValue>();
        void Add(string id, string label, double value, string unit) => values.Add(new(id, label, value, unit));
        Add("pacts/offered", "契約の提示数", Pacts.Offered, "choices");
        Add("pacts/doubleDepthBonusMultiplier", "契約の潜行ボーナス倍率", PactBalance.DoubleDepthBonusMultiplier, "multiplier");
        foreach (var d in Pacts.All)
        {
            string id = "pacts/" + d.Id;
            string label = d.Id.ToString();
            Add(id + "/curseStrength", label + " 呪い強度", d.CurseStrength, "curse-strength");
            Add(id + "/dropBonus", label + " 遺物ドロップ増加率", d.DropBonus, "ratio");
            Add(id + "/luck", label + " 幸運", d.Luck, "luck");
            Add(id + "/luckPercent", label + " 良い遺物の出やすさ", Loot.LuckPercent(d.Luck), "percent");
            Add(id + "/shardMult", label + " 欠片倍率", d.ShardMult, "multiplier");
            Add(id + "/xpMult", label + " 経験倍率", d.XpMult, "multiplier");
            Add(id + "/tuningOnElite", label + " エリート・ボス追加調律石", d.TuningOnElite, "tuning/elite-or-boss");
            foreach (var boon in d.Boons)
            {
                string unit = boon.Stat switch
                {
                    Stat.AttackFlat => "attack", Stat.PowerFlat => "power", Stat.MaxHealthFlat => "health",
                    Stat.Armor => "armor", Stat.HealthRegen => "health/second", _ => "percent",
                };
                Add(id + "/boons/" + boon.Stat, label + " " + boon.Stat, boon.Value, unit);
            }
        }
        Add("daily-dream/powerBoostPct", "日替わり固有効果強化", DailyDream.PowerBoostPct, "percent");
        foreach (var d in DailyDream.All)
        {
            string id = "daily-dream/" + d.Id;
            Add(id + "/dropBonus", id + " 遺物ドロップ増加率", d.DropBonus, "ratio");
            Add(id + "/shardMult", id + " 欠片倍率", d.ShardMult, "multiplier");
            Add(id + "/xpMult", id + " 経験倍率", d.XpMult, "multiplier");
            Add(id + "/bountyMult", id + " 依頼報酬倍率", d.BountyMult, "multiplier");
            Add(id + "/nightmareMult", id + " 悪夢化倍率", d.NightmareMult, "multiplier");
        }
        Add("waypoints/offered", "道標の提示数", Waypoints.Offered, "choices");
        foreach (var d in Waypoints.All)
        {
            string id = "waypoints/" + d.Id;
            var t = d.Effects;
            Add(id + "/luck", id + " 幸運", t.Luck, "luck");
            Add(id + "/luckPercent", id + " 良い遺物の出やすさ", Loot.LuckPercent(t.Luck), "percent");
            Add(id + "/healingMultiplier", id + " 回復倍率", t.HealingMultiplier, "multiplier");
            Add(id + "/shieldMultiplier", id + " 障壁倍率", t.ShieldMultiplier, "multiplier");
            Add(id + "/reactionMultiplier", id + " 反応倍率", t.ReactionMultiplier, "multiplier");
            Add(id + "/nightmareChanceMultiplier", id + " 悪夢化倍率", t.NightmareChanceMultiplier, "multiplier");
            Add(id + "/nightmareRewardMultiplier", id + " 悪夢報酬倍率", t.NightmareRewardMultiplier, "multiplier");
            Add(id + "/awakeningMultiplier", id + " 覚醒倍率", t.AwakeningMultiplier, "multiplier");
            Add(id + "/memoryCooldownMultiplier", id + " 通常記憶クールダウン倍率", t.MemoryCooldownMultiplier, "multiplier");
            Add(id + "/pressureMultiplier", id + " 圧倍率", t.PressureMultiplier, "multiplier");
            Add(id + "/summonPowerMultiplier", id + " 召喚ダメージ倍率", t.SummonPowerMultiplier, "multiplier");
            Add(id + "/heroHealthMultiplier", id + " 旅人HP倍率", t.HeroHealthMultiplier, "multiplier");
            Add(id + "/shardMultiplier", id + " 欠片倍率", t.ShardMultiplier, "multiplier");
            Add(id + "/tuningMultiplier", id + " 調律石倍率", t.TuningMultiplier, "multiplier");
            Add(id + "/enhancement", id + " 初期強化", t.Enhancement, "enhancement-level");
            if (t.MaxRelicsPerRoom != int.MaxValue)
                Add(id + "/maxRelicsPerRoom", id + " 部屋遺物上限", t.MaxRelicsPerRoom, "relics/room");
            Add(id + "/relicSalvageMultiplier", id + " 遺物分解欠片倍率", t.RelicSalvageMultiplier, "multiplier");
            Add(id + "/awakeningPerRelic", id + " 遺物ごとの覚醒", t.AwakeningPerRelic, "awakening/relic");
        }
        Add("waypoints/rewards/twinRelicCopies", "双子の宝箱の遺物数", WaypointBalance.TwinRelicCopies, "copies/relic");
        Add("waypoints/rewards/bossSalvageMultiplier", "道中の収穫の分解倍率", WaypointBalance.BossSalvageMultiplier, "multiplier");
        Add("waypoints/rewards/starXpPerShard", "星への供物の変換", WaypointBalance.StarXpPerShard, "star-xp/shard");
        Add("waypoints/rewards/hoardRewardMultiplier", "封じられた宝庫の報酬倍率", WaypointBalance.HoardRewardMultiplier, "multiplier");
        Add("waypoints/rewards/tuningPerNonBossRelic", "門番への貢ぎ物の変換", WaypointBalance.TuningPerNonBossRelic, "tuning/relic");
        return values;
    }
}

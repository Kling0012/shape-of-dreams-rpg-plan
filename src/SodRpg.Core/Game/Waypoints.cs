using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>One-zone rules. Persisted identifiers are append-only.</summary>
    public enum Waypoint
    {
        None = 0, WeaponRoad = 1, ArmorRoad = 2, CharmRoad = 3, HeadRoad = 4,
        HandsRoad = 5, FeetRoad = 6, NightmareHunt = 7, GlassAegis = 8,
        ResonantRoad = 9, EndlessNight = 10, BossHoard = 11, TemperedFinds = 12,
        FleetingMemories = 13, SummonerTrail = 14, EpicMirage = 15, ShardRoad = 16,
        TwinCache = 17, StarOffering = 18, HumbleForge = 19, BossTribute = 20,
        SixfoldRoad = 21, AwakeningPilgrimage = 22, FirstClaim = 23, SupplyLine = 24,
    }

    public sealed class WaypointDef
    {
        public Waypoint Id { get; internal set; }
        public Txt Name { get; internal set; }
        public Txt Description { get; internal set; }
        public Waypoints.Totals Effects { get; internal set; }
    }

    public static class Waypoints
    {
        public const int Offered = WaypointBalance.Offered;
        public const int MaximumDeferredRelics = 256;

        /// <summary>Shared read-only values; callers must not modify a cached definition.</summary>
        public sealed class Totals
        {
            public Slot? ForcedSlot { get; internal set; }
            public double Luck { get; internal set; }
            public double HealingMultiplier { get; internal set; } = 1;
            public double ShieldMultiplier { get; internal set; } = 1;
            public double ReactionMultiplier { get; internal set; } = 1;
            public bool AllNightmares { get; internal set; }
            public double NightmareChanceMultiplier { get; internal set; } = 1;
            public double NightmareRewardMultiplier { get; internal set; } = 1;
            public double AwakeningMultiplier { get; internal set; } = 1;
            public double MemoryCooldownMultiplier { get; internal set; } = 1;
            public double PressureMultiplier { get; internal set; } = 1;
            public double SummonPowerMultiplier { get; internal set; } = 1;
            public double HeroHealthMultiplier { get; internal set; } = 1;
            public double ShardMultiplier { get; internal set; } = 1;
            public double TuningMultiplier { get; internal set; } = 1;
            public bool DelayDropsUntilBoss { get; internal set; }
            public int Enhancement { get; internal set; }
            public int MaxRelicsPerRoom { get; internal set; } = int.MaxValue;
            public Rarity MinimumRarity { get; internal set; } = Rarity.Common;
            public Rarity? ForcedRarity { get; internal set; }
            public int RelicSalvageMultiplier { get; internal set; }
            public bool TwinRelics { get; internal set; }
            public bool ShardsToStarXp { get; internal set; }
            public bool NonBossRelicsToTuning { get; internal set; }
            public bool CycleSlots { get; internal set; }
            public int AwakeningPerRelic { get; internal set; }
            public bool FirstKillRelic { get; internal set; }
            public bool BossRelicsToShards { get; internal set; }
        }

        private static WaypointDef D(Waypoint id, string ja, string en, Totals effects)
            => new WaypointDef { Id = id, Name = new Txt(ja, en), Description = Describe(id, effects), Effects = effects };

        private static string N(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        private static string Times(double value) => value == 2 ? "double" : N(value) + " times";
        private static string Fold(double value) => value == 3 ? "threefold" : N(value) + "-fold";
        private static string SalvageTimes(double value) => value == 3 ? "three times" : value == 4 ? "four times" : N(value) + " times";
        private static string PercentChange(double value, bool ja)
        {
            double percent = Math.Abs(value - 1) * 100;
            return ja
                ? N(percent) + (value < 1 ? "%減る" : "%増える")
                : (value < 1 ? "reduced" : "increased") + " by " + N(percent) + "%";
        }

        private static Txt Describe(Waypoint id, Totals t)
        {
            switch (id)
            {
                case Waypoint.WeaponRoad:
                case Waypoint.ArmorRoad:
                case Waypoint.CharmRoad:
                case Waypoint.HeadRoad:
                case Waypoint.HandsRoad:
                case Waypoint.FeetRoad:
                    var slot = t.ForcedSlot.Value switch
                    {
                        Slot.Weapon => ("武器", "weapons"), Slot.Armor => ("防具", "armor"),
                        Slot.Charm => ("装飾品", "charms"), Slot.Head => ("頭装備", "headgear"),
                        Slot.Hands => ("手装備", "hand gear"), Slot.Feet => ("足装備", "footwear"),
                        _ => throw new ArgumentOutOfRangeException(nameof(t.ForcedSlot)),
                    };
                    string luck = N(Loot.LuckPercent(t.Luck));
                    return new Txt($"次のゾーンで敵から得る遺物はすべて{slot.Item1}になり、良い遺物の出やすさが+{luck}%になる。", $"All relics from enemies in the next zone are {slot.Item2}, with +{luck}% better relics.");
                case Waypoint.NightmareHunt:
                    string chance = t.NightmareChanceMultiplier == 2 ? "twice" : N(t.NightmareChanceMultiplier) + " times";
                    return new Txt($"次のゾーンでは悪夢化する機会が{N(t.NightmareChanceMultiplier)}倍になり、悪夢と変種から得る遺物・欠片・調律石が{N(t.NightmareRewardMultiplier)}倍になる。", $"In the next zone, enemies are {chance} as likely to become nightmares. Nightmares and variants yield {Times(t.NightmareRewardMultiplier)} relics, shards and tuning stones.");
                case Waypoint.GlassAegis:
                    return new Txt($"次のゾーンでは受ける回復が{PercentChange(t.HealingMultiplier, true)}代わりに、得る障壁の量が{N(Math.Abs(t.ShieldMultiplier - 1) * 100)}%{(t.ShieldMultiplier < 1 ? "減る" : "増える")}。", $"In the next zone, healing received is {PercentChange(t.HealingMultiplier, false)}, but shield amounts {(t.ShieldMultiplier < 1 ? "decrease" : "increase")} by {N(Math.Abs(t.ShieldMultiplier - 1) * 100)}%.");
                case Waypoint.ResonantRoad:
                    string reaction = t.ReactionMultiplier == 2 ? "doubles" : "multiplies by " + N(t.ReactionMultiplier);
                    return new Txt($"次のゾーンでは装備による属性の反応の威力が{N(t.ReactionMultiplier)}倍になる。ダメージ・被ダメージ増加・付与する火・障壁の量が対象。範囲・時間・間隔・鈍足は変わらない。", $"Equipment-based elemental reactions have {Times(t.ReactionMultiplier)} strength in the next zone. This {reaction} damage, damage vulnerability, Fire applied and shield amounts. Radius, duration, interval and slow stay the same.");
                case Waypoint.EndlessNight:
                    string awakening = t.AwakeningMultiplier == 3 ? "tripled" : "multiplied by " + N(t.AwakeningMultiplier);
                    return new Txt($"次のゾーンではすべての敵が悪夢化し、撃破で得る覚醒の力が{N(t.AwakeningMultiplier)}倍になる。", $"Every enemy becomes a nightmare in the next zone, and awakening points from kills are {awakening}.");
                case Waypoint.BossHoard:
                    return new Txt($"次のゾーンで敵から得る遺物・欠片・調律石はボスを倒すまで保留され、倒すとボスの分も含め{WaypointBalance.HoardRewardMultiplier}倍受け取れる。ボス撃破後の戦利品もその場で{WaypointBalance.HoardRewardMultiplier}倍受け取れる。未開封でゾーンを離れると保留分を失う。保留・所持の上限を超えた遺物は欠片になる。経験はその場で得る。", $"Enemy relics, shards and tuning stones in the next zone are held until a boss falls, then paid out {Fold(WaypointBalance.HoardRewardMultiplier)}, including the boss's loot. Later kills also pay out {Fold(WaypointBalance.HoardRewardMultiplier)} immediately. Leaving before opening the hoard forfeits held loot. Relics beyond holding or inventory capacity become shards. Experience is immediate.");
                case Waypoint.TemperedFinds:
                    string limit = t.MaxRelicsPerRoom == 1 ? "only one relic can be found" : $"at most {t.MaxRelicsPerRoom} relics can be found";
                    return new Txt($"次のゾーンの遺物は最初から強化+{t.Enhancement}。ただし敵から得られる遺物は1部屋につき{t.MaxRelicsPerRoom}つまで。", $"Relics from enemies in the next zone start at enhancement +{t.Enhancement}, but {limit} per room.");
                case Waypoint.FleetingMemories:
                    return new Txt($"次のゾーンでは通常記憶のクールダウンが{N(Math.Abs(t.MemoryCooldownMultiplier - 1) * 100)}%{(t.MemoryCooldownMultiplier < 1 ? "短く" : "長く")}なり、夢の圧による敵のHPと攻撃の倍率が{N(Math.Abs(t.PressureMultiplier - 1) * 100)}%{(t.PressureMultiplier < 1 ? "減る" : "増える")}。回避と奥義は対象外。", $"Normal memory cooldowns are {N(Math.Abs(t.MemoryCooldownMultiplier - 1) * 100)}% {(t.MemoryCooldownMultiplier < 1 ? "shorter" : "longer")} in the next zone, while dream pressure's enemy health and damage multipliers {(t.PressureMultiplier < 1 ? "fall" : "rise")} by {N(Math.Abs(t.PressureMultiplier - 1) * 100)}%. Dodge and Ultimate are excluded.");
                case Waypoint.SummonerTrail:
                    return new Txt($"次のゾーンでは召喚獣の与えるダメージが{N(Math.Abs(t.SummonPowerMultiplier - 1) * 100)}%{(t.SummonPowerMultiplier < 1 ? "減り" : "増え")}、旅人の最大HPが{N(Math.Abs(t.HeroHealthMultiplier - 1) * 100)}%{(t.HeroHealthMultiplier < 1 ? "減る" : "増える")}。", $"In the next zone, summons deal {N(Math.Abs(t.SummonPowerMultiplier - 1) * 100)}% {(t.SummonPowerMultiplier < 1 ? "less" : "more")} damage and the Traveler has {N(Math.Abs(t.HeroHealthMultiplier - 1) * 100)}% {(t.HeroHealthMultiplier < 1 ? "less" : "more")} maximum health.");
                case Waypoint.EpicMirage:
                    return new Txt(t.ShardMultiplier == 0 ? "次のゾーンで敵から得る遺物は必ずエピック以上になるが、撃破による欠片は得られない。" : $"次のゾーンで敵から得る遺物は必ずエピック以上になり、撃破による欠片が{N(t.ShardMultiplier)}倍になる。", t.ShardMultiplier == 0 ? "Every relic from enemies in the next zone is Epic or better, but kills yield no shards." : $"Every relic from enemies in the next zone is Epic or better, and kills yield {N(t.ShardMultiplier)} times the shards.");
                case Waypoint.ShardRoad:
                    if (t.RelicSalvageMultiplier == 0)
                    return new Txt("次のゾーンで敵から得る遺物は欠片に変換せず、そのまま受け取れる。", "Relics from enemies in the next zone are kept instead of converted to shards.");
                    return new Txt($"次のゾーンで敵から得る遺物は、その場で分解価値の{t.RelicSalvageMultiplier}倍の欠片に変わる。", $"Relics from enemies in the next zone turn immediately into {SalvageTimes(t.RelicSalvageMultiplier)} their salvage value in shards.");
                case Waypoint.TwinCache:
                    string copies = WaypointBalance.TwinRelicCopies == 2 ? "an identical second copy" : $"{WaypointBalance.TwinRelicCopies - 1} identical extra copies";
                    string tuningJa = t.TuningMultiplier == 0 ? "撃破による調律石は得られない" : $"撃破による調律石が{N(t.TuningMultiplier)}倍になる";
                    string tuningEn = t.TuningMultiplier == 0 ? "kills yield no tuning stones" : $"kills yield {N(t.TuningMultiplier)} times the tuning stones";
                    return new Txt($"次のゾーンで敵から得る遺物には同じ物がもう{WaypointBalance.TwinRelicCopies - 1}つ付くが、{tuningJa}。", $"Each relic from enemies in the next zone comes with {copies}, but {tuningEn}.");
                case Waypoint.StarOffering:
                    return new Txt($"次のゾーンで撃破から得る欠片は、1個につき星の経験{WaypointBalance.StarXpPerShard}に変わる。夢の深さの経験倍率も適用される。", $"Each shard from kills in the next zone becomes {WaypointBalance.StarXpPerShard} star experience. The dream depth experience multiplier also applies.");
                case Waypoint.HumbleForge:
                    return new Txt($"次のゾーンで敵から得る遺物はすべてコモンになり、最初から強化+{t.Enhancement}になる。", $"All relics from enemies in the next zone are Common and start at enhancement +{t.Enhancement}.");
                case Waypoint.BossTribute:
                    string tuning = WaypointBalance.TuningPerNonBossRelic == 1 ? "one tuning stone" : $"{WaypointBalance.TuningPerNonBossRelic} tuning stones";
                    return new Txt($"次のゾーンではボス以外の遺物が1個につき調律石{WaypointBalance.TuningPerNonBossRelic}に変わり、ボスの遺物は必ずエピック以上になる。", $"In the next zone, each relic from a non-boss becomes {tuning}. Boss relics are always Epic or better.");
                case Waypoint.SixfoldRoad:
                    return new Txt($"次のゾーンの遺物は武器・防具・装飾品・頭・手・足の順に巡る。良い遺物の出やすさが+{N(Loot.LuckPercent(t.Luck))}%になる。", $"Relics in the next zone cycle through weapon, armor, charm, head, hands and feet, with +{N(Loot.LuckPercent(t.Luck))}% better relics.");
                case Waypoint.AwakeningPilgrimage:
                    if (t.AwakeningPerRelic == 0)
                    return new Txt("次のゾーンで敵から得る遺物は覚醒の力に変換せず、そのまま受け取れる。", "Relics from enemies in the next zone are kept instead of converted to awakening points.");
                    return new Txt($"次のゾーンで敵から得る遺物は、装着中の伝説の遺物それぞれの覚醒の力{t.AwakeningPerRelic}に変わる。装着していない場合は受け取れない。夢の深さの覚醒倍率も適用される。", $"Each relic from enemies in the next zone becomes {t.AwakeningPerRelic} awakening points for every equipped Legendary. Without an equipped Legendary, those points are lost. Dream depth's awakening multiplier applies.");
                case Waypoint.FirstClaim:
                    if (t.MaxRelicsPerRoom == 0)
                    return new Txt("次のゾーンでは敵から遺物を得られない。", "Enemies in the next zone yield no relics.");
                    return new Txt(t.MaxRelicsPerRoom == 1 ? "次のゾーンでは各部屋の最初の撃破でレア以上の遺物を1つ得る。その部屋の以後の撃破では遺物を得られない。" : $"次のゾーンでは各部屋の最初の撃破でレア以上の遺物を1つ得る。敵から得られる遺物は1部屋につき{t.MaxRelicsPerRoom}つまで。", t.MaxRelicsPerRoom == 1 ? "The first kill in each room of the next zone grants one Rare-or-better relic. Later kills in that room yield no relics." : $"The first kill in each room of the next zone grants one Rare-or-better relic. At most {t.MaxRelicsPerRoom} relics can be found per room.");
                case Waypoint.SupplyLine:
                    return WaypointBalance.BossSalvageMultiplier == 0
                        ? new Txt("次のゾーンではボスの遺物を欠片に変換せず、そのまま受け取れる。ボス以外の遺物もそのまま受け取る。", "Boss relics in the next zone are kept instead of converted to shards. Relics from other enemies are also kept.")
                        : new Txt($"次のゾーンではボスの遺物が分解価値の{WaypointBalance.BossSalvageMultiplier}倍の欠片に変わる。ボス以外の遺物はそのまま受け取れる。", $"Boss relics in the next zone become {SalvageTimes(WaypointBalance.BossSalvageMultiplier)} their salvage value in shards. Relics from other enemies are kept.");
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        public static readonly IReadOnlyList<WaypointDef> All = new[]
        {
            D(Waypoint.WeaponRoad, "刃の道", "Road of Blades", new Totals { ForcedSlot = Slot.Weapon, Luck = WaypointBalance.WeaponRoadLuck }),
            D(Waypoint.ArmorRoad, "鎧の道", "Road of Armor", new Totals { ForcedSlot = Slot.Armor, Luck = WaypointBalance.ArmorRoadLuck }),
            D(Waypoint.CharmRoad, "護符の道", "Road of Charms", new Totals { ForcedSlot = Slot.Charm, Luck = WaypointBalance.CharmRoadLuck }),
            D(Waypoint.HeadRoad, "冠の道", "Road of Crowns", new Totals { ForcedSlot = Slot.Head, Luck = WaypointBalance.HeadRoadLuck }),
            D(Waypoint.HandsRoad, "籠手の道", "Road of Gauntlets", new Totals { ForcedSlot = Slot.Hands, Luck = WaypointBalance.HandsRoadLuck }),
            D(Waypoint.FeetRoad, "足跡の道", "Road of Footsteps", new Totals { ForcedSlot = Slot.Feet, Luck = WaypointBalance.FeetRoadLuck }),
            D(Waypoint.NightmareHunt, "悪夢狩り", "Nightmare Hunt", new Totals { NightmareChanceMultiplier = WaypointBalance.NightmareHuntNightmareChanceMultiplier, NightmareRewardMultiplier = WaypointBalance.NightmareHuntNightmareRewardMultiplier }),
            D(Waypoint.GlassAegis, "硝子の障壁", "Glass Aegis", new Totals { HealingMultiplier = WaypointBalance.GlassAegisHealingMultiplier, ShieldMultiplier = WaypointBalance.GlassAegisShieldMultiplier }),
            D(Waypoint.ResonantRoad, "反応の小径", "Road of Reactions", new Totals { ReactionMultiplier = WaypointBalance.ResonantRoadReactionMultiplier }),
            D(Waypoint.EndlessNight, "明けない夜", "Endless Night", new Totals { AllNightmares = true, AwakeningMultiplier = WaypointBalance.EndlessNightAwakeningMultiplier }),
            D(Waypoint.BossHoard, "封じられた宝庫", "Sealed Hoard", new Totals { DelayDropsUntilBoss = true }),
            D(Waypoint.TemperedFinds, "焼き入れの道", "Tempered Road", new Totals { Enhancement = WaypointBalance.TemperedFindsEnhancement, MaxRelicsPerRoom = WaypointBalance.TemperedFindsMaxRelicsPerRoom }),
            D(Waypoint.FleetingMemories, "駆ける記憶", "Fleeting Memories", new Totals { MemoryCooldownMultiplier = WaypointBalance.FleetingMemoriesMemoryCooldownMultiplier, PressureMultiplier = WaypointBalance.FleetingMemoriesPressureMultiplier }),
            D(Waypoint.SummonerTrail, "群れの小径", "Trail of the Pack", new Totals { SummonPowerMultiplier = WaypointBalance.SummonerTrailSummonPowerMultiplier, HeroHealthMultiplier = WaypointBalance.SummonerTrailHeroHealthMultiplier }),
            D(Waypoint.EpicMirage, "紫の蜃気楼", "Violet Mirage", new Totals { MinimumRarity = Rarity.Epic, ShardMultiplier = WaypointBalance.EpicMirageShardMultiplier }),
            D(Waypoint.ShardRoad, "欠片の河", "River of Shards", new Totals { RelicSalvageMultiplier = WaypointBalance.ShardRoadRelicSalvageMultiplier }),
            D(Waypoint.TwinCache, "双子の宝箱", "Twin Cache", new Totals { TwinRelics = true, TuningMultiplier = WaypointBalance.TwinCacheTuningMultiplier }),
            D(Waypoint.StarOffering, "星への供物", "Offering to the Stars", new Totals { ShardsToStarXp = true }),
            D(Waypoint.HumbleForge, "素朴な鍛冶場", "Humble Forge", new Totals { ForcedRarity = Rarity.Common, Enhancement = WaypointBalance.HumbleForgeEnhancement }),
            D(Waypoint.BossTribute, "門番への貢ぎ物", "Tribute to the Gate", new Totals { NonBossRelicsToTuning = true }),
            D(Waypoint.SixfoldRoad, "六つの足取り", "Sixfold Trail", new Totals { CycleSlots = true, Luck = WaypointBalance.SixfoldRoadLuck }),
            D(Waypoint.AwakeningPilgrimage, "目覚めの巡礼", "Pilgrimage of Awakening", new Totals { AwakeningPerRelic = WaypointBalance.AwakeningPilgrimageAwakeningPerRelic }),
            D(Waypoint.FirstClaim, "一番槍の宝", "First Claim", new Totals { FirstKillRelic = true, MinimumRarity = Rarity.Rare, MaxRelicsPerRoom = WaypointBalance.FirstClaimMaxRelicsPerRoom }),
            D(Waypoint.SupplyLine, "道中の収穫", "Trail Supplies", new Totals { BossRelicsToShards = true }),
        };

        private static readonly Totals Neutral = new Totals();
        public static WaypointDef Get(Waypoint id)
        {
            int index = (int)id - 1;
            return index >= 0 && index < All.Count ? All[index] : null;
        }
        public static Totals Sum(Waypoint id) => Get(id)?.Effects ?? Neutral;

        public static List<Waypoint> Offer(Rng rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var pool = new List<Waypoint>(All.Count);
            foreach (var d in All) pool.Add(d.Id);
            var result = new List<Waypoint>(Offered);
            for (int n = 0; n < Offered; n++)
            {
                int index = rng.Range(0, pool.Count - 1);
                result.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return result;
        }

        public static void Expire(RunState run)
        {
            run.ActiveWaypoint = Waypoint.None;
            run.PendingWaypoint = Waypoint.None;
            run.WaypointChosen = false;
            run.OfferedWaypoints.Clear();
            run.WaypointRoom = -1;
            run.WaypointRelicsInRoom = 0;
            run.WaypointLootRooms.Clear();
            run.WaypointSlotCursor = 0;
            run.DeferredWaypointRelics.Clear();
            run.DeferredWaypointShards = 0;
            run.DeferredWaypointTuning = 0;
            run.WaypointHoardReleased = false;
        }

        internal static void Activate(RunState run)
        {
            if (!run.AwaitingChoice) return;
            run.ActiveWaypoint = run.WaypointChosen ? run.PendingWaypoint : Waypoint.None;
            run.PendingWaypoint = Waypoint.None;
            run.OfferedWaypoints.Clear();
        }

        /// <summary>Transform only newly rolled enemy rewards; existing inventory and event rewards are untouched.
        /// waypoint is the one active when the kill happened (#71); pass run.ActiveWaypoint for immediate kills.</summary>
        internal static void ApplyKill(Profile p, MonsterTier tier, bool nightmare, Rng rng, KillReward reward, int itemLevel, Line? focus, int roomIndex, Waypoint waypoint, out int starXp, out int awakening, bool rareAllowed = true, double rewardScale = 1)
        {
            starXp = 0;
            awakening = 0;
            var run = p.Run;
            if (waypoint == Waypoint.None)
            {
                PressureCountRewards.ScaleLoot(rng, reward, rewardScale);
                InfinityRewards.LimitReward(p, reward);
                return;
            }
            var t = Sum(waypoint);
            if (run.WaypointRoom != roomIndex)
            {
                run.WaypointRoom = roomIndex;
                run.WaypointRelicsInRoom = run.WaypointLootRooms.Contains(roomIndex) ? 1 : 0;
            }
            if (t.FirstKillRelic && run.WaypointRelicsInRoom == 0 && reward.Relics.Count == 0)
                reward.Relics.Add(Loot.RollRelic(rng, Rarity.Rare, itemLevel, null, focus, p.Stash, run.Satchel));
            if (t.MaxRelicsPerRoom != int.MaxValue)
            {
                int keep = Math.Max(0, Math.Min(reward.Relics.Count, t.MaxRelicsPerRoom - run.WaypointRelicsInRoom));
                if (keep < reward.Relics.Count) reward.Relics.RemoveRange(keep, reward.Relics.Count - keep);
            }
            run.WaypointRelicsInRoom = Add(run.WaypointRelicsInRoom, reward.Relics.Count);
            if (t.MaxRelicsPerRoom != int.MaxValue && reward.Relics.Count > 0) run.WaypointLootRooms.Add(roomIndex);
            for (int i = 0; i < reward.Relics.Count; i++)
            {
                var r = reward.Relics[i];
                Slot? slot = t.ForcedSlot;
                if (t.CycleSlots)
                {
                    slot = (Slot)(run.WaypointSlotCursor % Content.SlotCount);
                    run.WaypointSlotCursor = ((int)slot.Value + 1) % Content.SlotCount;
                }
                Rarity rarity = t.ForcedRarity ?? (Rarity)Math.Max((int)r.Rarity, (int)t.MinimumRarity);
                if (t.NonBossRelicsToTuning && tier == MonsterTier.Boss) rarity = (Rarity)Math.Max((int)rarity, (int)Rarity.Epic);
                // Decide the ceiling before any replacement roll; guarantees cannot bypass rare admission.
                if (!rareAllowed) rarity = (Rarity)Math.Min((int)rarity, (int)Rarity.Rare);
                if (rarity != r.Rarity || (slot.HasValue && r.Slot != slot.Value))
                    r = reward.Relics[i] = Loot.RollRelic(rng, rarity, r.ItemLevel, slot, focus, p.Stash, run.Satchel);
                r.Enhance = Math.Min(Content.MaxEnhanceFor(r.Rarity, r.LimitBreaks), Math.Max(r.Enhance, t.Enhancement));
                if (t.Enhancement > 0) Rules.GrantEnhanceMilestones(rng, r);
            }
            int copies = t.TwinRelics ? WaypointBalance.TwinRelicCopies : nightmare ? (int)t.NightmareRewardMultiplier : 1;
            Duplicate(reward.Relics, copies, rng);
            reward.Shards = DreamDepth.ScaleReward(reward.Shards, t.ShardMultiplier * (nightmare ? t.NightmareRewardMultiplier : 1));
            reward.Tuning = DreamDepth.ScaleReward(reward.Tuning, t.TuningMultiplier * (nightmare ? t.NightmareRewardMultiplier : 1));
            int salvage = t.BossRelicsToShards && tier == MonsterTier.Boss ? WaypointBalance.BossSalvageMultiplier : t.RelicSalvageMultiplier;
            if (salvage > 0)
            {
                foreach (var r in reward.Relics) reward.Shards = Add(reward.Shards, Content.SalvageShards(r.Rarity) * salvage);
                reward.Relics.Clear();
            }
            if (t.NonBossRelicsToTuning && tier != MonsterTier.Boss)
            {
                reward.Tuning = Add(reward.Tuning, reward.Relics.Count * WaypointBalance.TuningPerNonBossRelic);
                reward.Relics.Clear();
            }
            if (t.AwakeningPerRelic > 0)
            {
                awakening = reward.Relics.Count * t.AwakeningPerRelic;
                reward.Relics.Clear();
            }
            if (t.ShardsToStarXp)
            {
                starXp = DreamDepth.ScaleReward(reward.Shards, WaypointBalance.StarXpPerShard);
                reward.Shards = 0;
            }
            PressureCountRewards.ScaleLoot(rng, reward, rewardScale);
            InfinityRewards.LimitReward(p, reward, t.DelayDropsUntilBoss ? WaypointBalance.HoardRewardMultiplier : 1);
            if (!t.DelayDropsUntilBoss) return;
            if (!run.WaypointHoardReleased)
            {
                run.DeferredWaypointShards = Add(run.DeferredWaypointShards, reward.Shards);
                run.DeferredWaypointTuning = Add(run.DeferredWaypointTuning, reward.Tuning);
                foreach (var r in reward.Relics)
                {
                    if (run.DeferredWaypointRelics.Count < MaximumDeferredRelics) run.DeferredWaypointRelics.Add(r);
                    else run.DeferredWaypointShards = Add(run.DeferredWaypointShards, InfinityRewards.LimitHoardOverflow(p, Content.SalvageShards(r.Rarity)));
                }
                reward.Relics.Clear();
                reward.Shards = 0;
                reward.Tuning = 0;
                if (tier != MonsterTier.Boss) return;
                reward.Relics.AddRange(run.DeferredWaypointRelics);
                reward.Shards = run.DeferredWaypointShards;
                reward.Tuning = run.DeferredWaypointTuning;
                run.DeferredWaypointRelics.Clear();
                run.DeferredWaypointShards = 0;
                run.DeferredWaypointTuning = 0;
                run.WaypointHoardReleased = true;
            }
            Duplicate(reward.Relics, WaypointBalance.HoardRewardMultiplier, rng);
            reward.Shards = DreamDepth.ScaleReward(reward.Shards, WaypointBalance.HoardRewardMultiplier);
            reward.Tuning = DreamDepth.ScaleReward(reward.Tuning, WaypointBalance.HoardRewardMultiplier);
        }

        private static int Add(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);
        private static void Duplicate(List<Relic> relics, int copies, Rng rng)
        {
            int count = relics.Count;
            for (int n = 1; n < copies; n++)
                for (int i = 0; i < count; i++)
                {
                    var copy = relics[i].Clone();
                    copy.Uid = rng.NextUid();
                    relics.Add(copy);
                }
        }
    }
}

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
        public const int Offered = 3;
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

        private static WaypointDef D(Waypoint id, string ja, string en, string jaDescription, string enDescription, Totals effects)
            => new WaypointDef { Id = id, Name = new Txt(ja, en), Description = new Txt(jaDescription, enDescription), Effects = effects };

        public static readonly IReadOnlyList<WaypointDef> All = new[]
        {
            D(Waypoint.WeaponRoad, "刃の道", "Road of Blades", "次のゾーンで敵から得る遺物はすべて武器になり、良い遺物の出やすさが+60%になります。", "All relics from enemies in the next zone are weapons, with +60% better relics.", new Totals { ForcedSlot = Slot.Weapon, Luck = 1 }),
            D(Waypoint.ArmorRoad, "鎧の道", "Road of Armor", "次のゾーンで敵から得る遺物はすべて防具になり、良い遺物の出やすさが+60%になります。", "All relics from enemies in the next zone are armor, with +60% better relics.", new Totals { ForcedSlot = Slot.Armor, Luck = 1 }),
            D(Waypoint.CharmRoad, "護符の道", "Road of Charms", "次のゾーンで敵から得る遺物はすべて装飾品になり、良い遺物の出やすさが+60%になります。", "All relics from enemies in the next zone are charms, with +60% better relics.", new Totals { ForcedSlot = Slot.Charm, Luck = 1 }),
            D(Waypoint.HeadRoad, "冠の道", "Road of Crowns", "次のゾーンで敵から得る遺物はすべて頭装備になり、良い遺物の出やすさが+60%になります。", "All relics from enemies in the next zone are headgear, with +60% better relics.", new Totals { ForcedSlot = Slot.Head, Luck = 1 }),
            D(Waypoint.HandsRoad, "籠手の道", "Road of Gauntlets", "次のゾーンで敵から得る遺物はすべて手装備になり、良い遺物の出やすさが+60%になります。", "All relics from enemies in the next zone are hand gear, with +60% better relics.", new Totals { ForcedSlot = Slot.Hands, Luck = 1 }),
            D(Waypoint.FeetRoad, "足跡の道", "Road of Footsteps", "次のゾーンで敵から得る遺物はすべて足装備になり、良い遺物の出やすさが+60%になります。", "All relics from enemies in the next zone are footwear, with +60% better relics.", new Totals { ForcedSlot = Slot.Feet, Luck = 1 }),
            D(Waypoint.NightmareHunt, "悪夢狩り", "Nightmare Hunt", "次のゾーンでは悪夢化する機会が2倍になり、悪夢と変種から得る遺物・欠片・調律石が2倍になります。", "In the next zone, enemies are twice as likely to become nightmares. Nightmares and variants yield double relics, shards and tuning stones.", new Totals { NightmareChanceMultiplier = 2, NightmareRewardMultiplier = 2 }),
            D(Waypoint.GlassAegis, "硝子の障壁", "Glass Aegis", "次のゾーンでは受ける回復が50%減る代わりに、得る障壁の量が100%増えます。", "In the next zone, healing received is reduced by 50%, but shield amounts increase by 100%.", new Totals { HealingMultiplier = .5, ShieldMultiplier = 2 }),
            D(Waypoint.ResonantRoad, "反応の小径", "Road of Reactions", "次のゾーンでは装備による属性の反応の威力が2倍になります。ダメージ・被ダメージ増加・付与する火・障壁の量が対象です。範囲・時間・間隔・鈍足は変わりません。", "Equipment-based elemental reactions have double strength in the next zone. This doubles damage, damage vulnerability, Fire applied and shield amounts. Radius, duration, interval and slow stay the same.", new Totals { ReactionMultiplier = 2 }),
            D(Waypoint.EndlessNight, "明けない夜", "Endless Night", "次のゾーンではすべての敵が悪夢化し、撃破で得る覚醒の力が3倍になります。", "Every enemy becomes a nightmare in the next zone, and awakening points from kills are tripled.", new Totals { AllNightmares = true, AwakeningMultiplier = 3 }),
            D(Waypoint.BossHoard, "封じられた宝庫", "Sealed Hoard", "次のゾーンで敵から得る遺物・欠片・調律石はボスを倒すまで保留され、倒すとボスの分も含め3倍受け取れます。ボス撃破後の戦利品もその場で3倍受け取れます。未開封でゾーンを離れると保留分を失います。保留・所持の上限を超えた遺物は欠片になります。経験はその場で得ます。", "Enemy relics, shards and tuning stones in the next zone are held until a boss falls, then paid out threefold, including the boss's loot. Later kills also pay out threefold immediately. Leaving before opening the hoard forfeits held loot. Relics beyond holding or inventory capacity become shards. Experience is immediate.", new Totals { DelayDropsUntilBoss = true }),
            D(Waypoint.TemperedFinds, "焼き入れの道", "Tempered Road", "次のゾーンの遺物は最初から強化+2。ただし敵から得られる遺物は1部屋につき1つまでです。", "Relics from enemies in the next zone start at enhancement +2, but only one relic can be found per room.", new Totals { Enhancement = 2, MaxRelicsPerRoom = 1 }),
            D(Waypoint.FleetingMemories, "駆ける記憶", "Fleeting Memories", "次のゾーンでは通常記憶のクールダウンが20%短くなり、夢の圧による敵のHPと攻撃の倍率が25%増えます。回避と奥義は対象外です。", "Normal memory cooldowns are 20% shorter in the next zone, while dream pressure's enemy health and damage multipliers rise by 25%. Dodge and Ultimate are excluded.", new Totals { MemoryCooldownMultiplier = .8, PressureMultiplier = 1.25 }),
            D(Waypoint.SummonerTrail, "群れの小径", "Trail of the Pack", "次のゾーンでは召喚獣の与えるダメージが50%増え、旅人の最大HPが15%減ります。", "In the next zone, summons deal 50% more damage and the Traveler has 15% less maximum health.", new Totals { SummonPowerMultiplier = 1.5, HeroHealthMultiplier = .85 }),
            D(Waypoint.EpicMirage, "紫の蜃気楼", "Violet Mirage", "次のゾーンで敵から得る遺物は必ずエピック以上になりますが、撃破による欠片は得られません。", "Every relic from enemies in the next zone is Epic or better, but kills yield no shards.", new Totals { MinimumRarity = Rarity.Epic, ShardMultiplier = 0 }),
            D(Waypoint.ShardRoad, "欠片の河", "River of Shards", "次のゾーンで敵から得る遺物は、その場で分解価値の3倍の欠片に変わります。", "Relics from enemies in the next zone turn immediately into three times their salvage value in shards.", new Totals { RelicSalvageMultiplier = 3 }),
            D(Waypoint.TwinCache, "双子の宝箱", "Twin Cache", "次のゾーンで敵から得る遺物には同じ物がもう1つ付きますが、撃破による調律石は得られません。", "Each relic from enemies in the next zone comes with an identical second copy, but kills yield no tuning stones.", new Totals { TwinRelics = true, TuningMultiplier = 0 }),
            D(Waypoint.StarOffering, "星への供物", "Offering to the Stars", "次のゾーンで撃破から得る欠片は、1個につき星の経験4に変わります。夢の深さの経験倍率も適用されます。", "Each shard from kills in the next zone becomes 4 star experience. The dream depth experience multiplier also applies.", new Totals { ShardsToStarXp = true }),
            D(Waypoint.HumbleForge, "素朴な鍛冶場", "Humble Forge", "次のゾーンで敵から得る遺物はすべてコモンになり、最初から強化+4になります。", "All relics from enemies in the next zone are Common and start at enhancement +4.", new Totals { ForcedRarity = Rarity.Common, Enhancement = 4 }),
            D(Waypoint.BossTribute, "門番への貢ぎ物", "Tribute to the Gate", "次のゾーンではボス以外の遺物が1個につき調律石1に変わり、ボスの遺物は必ずエピック以上になります。", "In the next zone, each relic from a non-boss becomes one tuning stone. Boss relics are always Epic or better.", new Totals { NonBossRelicsToTuning = true }),
            D(Waypoint.SixfoldRoad, "六つの足取り", "Sixfold Trail", "次のゾーンの遺物は武器・防具・装飾品・頭・手・足の順に巡ります。良い遺物の出やすさが+30%になります。", "Relics in the next zone cycle through weapon, armor, charm, head, hands and feet, with +30% better relics.", new Totals { CycleSlots = true, Luck = .5 }),
            D(Waypoint.AwakeningPilgrimage, "目覚めの巡礼", "Pilgrimage of Awakening", "次のゾーンで敵から得る遺物は、装着中の伝説の遺物それぞれの覚醒の力20に変わります。装着していない場合は受け取れません。夢の深さの覚醒倍率も適用されます。", "Each relic from enemies in the next zone becomes 20 awakening points for every equipped Legendary. Without an equipped Legendary, those points are lost. Dream depth's awakening multiplier applies.", new Totals { AwakeningPerRelic = 20 }),
            D(Waypoint.FirstClaim, "一番槍の宝", "First Claim", "次のゾーンでは各部屋の最初の撃破でレア以上の遺物を1つ得ます。その部屋の以後の撃破では遺物を得られません。", "The first kill in each room of the next zone grants one Rare-or-better relic. Later kills in that room yield no relics.", new Totals { FirstKillRelic = true, MinimumRarity = Rarity.Rare, MaxRelicsPerRoom = 1 }),
            D(Waypoint.SupplyLine, "道中の収穫", "Trail Supplies", "次のゾーンではボスの遺物が分解価値の4倍の欠片に変わります。ボス以外の遺物はそのまま受け取れます。", "Boss relics in the next zone become four times their salvage value in shards. Relics from other enemies are kept.", new Totals { BossRelicsToShards = true }),
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
        internal static void ApplyKill(Profile p, MonsterTier tier, bool nightmare, Rng rng, KillReward reward, int itemLevel, Line? focus, int roomIndex, Waypoint waypoint, out int starXp, out int awakening)
        {
            starXp = 0;
            awakening = 0;
            var run = p.Run;
            if (waypoint == Waypoint.None) return;
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
            run.WaypointRelicsInRoom += reward.Relics.Count;
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
                if (rarity != r.Rarity || (slot.HasValue && r.Slot != slot.Value))
                    r = reward.Relics[i] = Loot.RollRelic(rng, rarity, r.ItemLevel, slot, focus, p.Stash, run.Satchel);
                r.Enhance = Math.Min(Content.MaxEnhanceFor(r.Rarity, r.LimitBreaks), Math.Max(r.Enhance, t.Enhancement));
                if (t.Enhancement > 0) Rules.GrantEnhanceMilestones(rng, r);
            }
            int copies = t.TwinRelics ? 2 : nightmare ? (int)t.NightmareRewardMultiplier : 1;
            Duplicate(reward.Relics, copies, rng);
            reward.Shards = DreamDepth.ScaleReward(reward.Shards, t.ShardMultiplier * (nightmare ? t.NightmareRewardMultiplier : 1));
            reward.Tuning = DreamDepth.ScaleReward(reward.Tuning, t.TuningMultiplier * (nightmare ? t.NightmareRewardMultiplier : 1));
            int salvage = t.BossRelicsToShards && tier == MonsterTier.Boss ? 4 : t.RelicSalvageMultiplier;
            if (salvage > 0)
            {
                foreach (var r in reward.Relics) reward.Shards = Add(reward.Shards, Content.SalvageShards(r.Rarity) * salvage);
                reward.Relics.Clear();
            }
            if (t.NonBossRelicsToTuning && tier != MonsterTier.Boss)
            {
                reward.Tuning = Add(reward.Tuning, reward.Relics.Count);
                reward.Relics.Clear();
            }
            if (t.AwakeningPerRelic > 0)
            {
                awakening = reward.Relics.Count * t.AwakeningPerRelic;
                reward.Relics.Clear();
            }
            if (t.ShardsToStarXp)
            {
                starXp = DreamDepth.ScaleReward(reward.Shards, 4);
                reward.Shards = 0;
            }
            if (!t.DelayDropsUntilBoss) return;
            if (!run.WaypointHoardReleased)
            {
                run.DeferredWaypointShards = Add(run.DeferredWaypointShards, reward.Shards);
                run.DeferredWaypointTuning = Add(run.DeferredWaypointTuning, reward.Tuning);
                foreach (var r in reward.Relics)
                {
                    if (run.DeferredWaypointRelics.Count < MaximumDeferredRelics) run.DeferredWaypointRelics.Add(r);
                    else run.DeferredWaypointShards = Add(run.DeferredWaypointShards, Content.SalvageShards(r.Rarity));
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
            Duplicate(reward.Relics, 3, rng);
            reward.Shards = DreamDepth.ScaleReward(reward.Shards, 3);
            reward.Tuning = DreamDepth.ScaleReward(reward.Tuning, 3);
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

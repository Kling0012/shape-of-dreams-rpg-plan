using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string InfernusSetId = "set.boss_infernus";
        public const string InfernusRewardId = "boss_infernus.eternal_flame";
        public const string InfernusEventOrder = "expire-heat;admit-native-input;heat-or-consume;legal-ordered-eruptions;last-legal-pillar;delayed-owner-radial-and-selfshield;equipped-parts;consume-counters-only-after-reservation";
        public const string InfernusNativeContract = "own-equipped-gem-and-skilltrigger;native-curse-parent-and-caster;native-timer-snapshot-plus-bounded-difference;refresh-not-additive;restore-own-difference-never-delete-curse;exact-curse-proc-applyelemental-success-and-real-stack-increase;owner-and-target-cd;finite-expired-slot-only-target-ledger;first-native-main-skill-packet-per-activation-ring64;exact-one-firestack-crit-threshold-il-site;preserve-native-high-stack-gate-modifier-and-real-stack-amplification;exclude-generated-origin-other-owner-summon-and-invalid-lifetimes;unique-native-actor-generation-for-curse-gem-owner-victim-and-target-ledger;native-curse-captures-bounded-by-runtime-and-live-native-curse;pool-and-gem-unequip-restore";

        private static BossMoveProfile InfernusProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_infernus." + suffix, InfernusSetId, new Txt(ja, en), channels, actions);
        private static BossAction InfernusEruptions(BossEvent trigger, string channel, int cooldown, int delay, int interval, int spacing, int radius)
            => new BossAction(trigger, BossMechanism.ShapeAttack, BossPayload.Damage, channel, cooldownMillis: cooldown,
                delayMillis: delay, intervalMillis: interval, count: 3, maxInstances: 1, radiusMilli: radius,
                rangeMilli: spacing * 3, widthMilli: 2000, magnitudeMilli: spacing, element: BossElement.Fire);
        private static BossAction InfernusRadial(BossEvent trigger, string channel, int cooldown, int delay, int speed, int range, int width, int lifetime, int mainHits = 0)
            => new BossAction(trigger, BossMechanism.Projectile, BossPayload.Damage, channel, BossShape.Radial,
                cooldownMillis: cooldown, delayMillis: delay, lifetimeMillis: lifetime, count: 4, maxInstances: 1,
                mainHits: mainHits, counterLifetimeMillis: 6000, rangeMilli: range, widthMilli: width,
                speedMilli: speed, angleMilli: 90000, element: BossElement.Fire, followOwner: trigger == BossEvent.Clock);

        internal static BossMoveProfile[] CreateInfernusMoves() => new[]
        {
            InfernusProfile("weapon", "通常攻撃命中時、前方1.5m／3m／4.5mの地点に半径0.8mの噴火を順次発生させ、0.30秒／0.45秒／0.60秒後にそれぞれ火属性ダメージ（攻撃力・魔力の高い方の10%）を与える（高低差2m超の段差では以降の噴火が中断）。再使用まで5秒。", "When your basic attack hits, cause eruptions 1.5m, 3m, and 4.5m ahead, each with a 0.8m radius. After 0.30, 0.45, and 0.60 seconds respectively, each deals Fire damage equal to 10% of whichever is higher: your Attack or Ability Power. A terrain step with a height difference greater than 2m stops the remaining eruptions. Cooldown: 5 seconds.",
                new[] { C("InfernusEruption",10,10) }, InfernusEruptions(BossEvent.MainHit,"InfernusEruption",5000,300,150,1500,800)),
            InfernusProfile("armor", "被ダメージ時、被弾時の自身の位置に0.35秒の予告後、半径2mに火属性ダメージ（攻撃力・魔力の高い方の24%）を与え、一般の敵を外側へ0.8m押し出す（ボスと行動妨害耐性を持つ敵は移動しない）。再使用まで7秒。", "When you take damage, show a warning at your position at the time of the hit for 0.35 seconds, then deal Fire damage in a 2m radius equal to 24% of whichever is higher: your Attack or Ability Power and push normal enemies outward by 0.8m. Bosses and enemies immune to crowd control are not moved. Cooldown: 7 seconds.",
                new[] { C("InfernusLanding",24,24) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.ShapeAttack,BossPayload.Push,"InfernusLanding",cooldownMillis:7000,delayMillis:350,maxInstances:1,radiusMilli:2000,magnitudeMilli:800,element:BossElement.Fire)),
            InfernusProfile("charm", "記憶を使ったとき、カーソル位置（8m以内）に1.5秒間持続する炎の柱を召喚する。0.50秒後に半径1.5mへ攻撃力・魔力の高い方の16%のダメージを与え、その後0.5秒間隔で3回、各6%のダメージを与える（同時に最大1本まで）。再使用まで8秒。", "When you use a memory, summon a flame pillar at the cursor within 8m, lasting 1.5 seconds. After 0.50 seconds, it deals damage in a 1.5m radius equal to 16% of whichever is higher: your Attack or Ability Power, then deals 6% damage 3 times at 0.5-second intervals. Only one pillar can exist at once. Cooldown: 8 seconds.",
                new[] { C("InfernusPillarInitial",16,16), C("InfernusPillarTick",6,6) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusPillarInitial",anchor:BossAnchor.Cursor,cooldownMillis:8000,delayMillis:500,lifetimeMillis:1500,maxInstances:1,radiusMilli:1500,rangeMilli:8000,element:BossElement.Fire),
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusPillarTick",delayMillis:1000,lifetimeMillis:1500,intervalMillis:500,count:3,maxInstances:1,radiusMilli:1500,element:BossElement.Fire)),
            InfernusProfile("head", "記憶を使ったとき、発動時の方向へ長さ5m・幅1mの炎を0秒／0.2秒／0.4秒のタイミングで3回放射し、それぞれ攻撃力・魔力の高い方の12%のダメージを与える（持続0.6秒、同一の敵への命中は各回1回まで）。再使用まで7秒。", "When you use a memory, release flames in the direction you were facing at activation along a line 5m long and 1m wide, at 0, 0.2, and 0.4 seconds. Each deals 12% of whichever is higher: your Attack or Ability Power. The effect lasts 0.6 seconds, and each enemy can be hit only once per burst. Cooldown: 7 seconds.",
                new[] { C("InfernusBreath",12,12) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusBreath",BossShape.Line,cooldownMillis:7000,lifetimeMillis:600,intervalMillis:200,count:3,maxInstances:1,rangeMilli:5000,widthMilli:1000,element:BossElement.Fire)),
            InfernusProfile("hands", "6秒以内に通常攻撃が3回命中すると、0.35秒の予告後に4方向へ咆哮弾を放つ（弾速10m/秒、射程4m、幅0.5m、持続0.4秒）。各5%、同一の敵には合計で攻撃力・魔力の高い方の20%までダメージを与える。再使用まで6秒。", "After 3 basic attack hits within 6 seconds, show a warning for 0.35 seconds, then fire roar projectiles in 4 directions. They travel at 10m/s, have a 4m range, are 0.5m wide, and last 0.4 seconds. Each deals 5% of whichever is higher: your Attack or Ability Power, up to a total of 20% per enemy. Cooldown: 6 seconds.",
                new[] { C("InfernusRoar",20,20) }, InfernusRadial(BossEvent.MainHit,"InfernusRoar",6000,350,10000,4000,500,400,3)),
            InfernusProfile("feet", "移動スキルの完了時、前方長さ3m・幅1.5mの範囲に攻撃力・魔力の高い方の18%のダメージを与え、敵を1.2m押し出す（壁で停止、ボスと行動妨害が効かない敵は移動しない）。再使用まで5秒。", "When your movement skill ends, deal damage in an area 3m long and 1.5m wide ahead of you equal to 18% of whichever is higher: your Attack or Ability Power, and push enemies 1.2m. Movement stops at walls; bosses and enemies immune to crowd control are not moved. Cooldown: 5 seconds.",
                new[] { C("InfernusDashImpact",18,18) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Push,"InfernusDashImpact",BossShape.Line,cooldownMillis:5000,maxInstances:1,rangeMilli:3000,widthMilli:1500,magnitudeMilli:1200,element:BossElement.Fire)),
            InfernusProfile("stage2", "通常攻撃命中または記憶使用時に熱の印を1つ獲得する（持続6秒、最大3個まで蓄積）。印が3個の状態で次の通常攻撃命中または記憶使用を行うと全て消費し、前方2m／4m／6mの地点に半径1mの噴火を0.35秒／0.55秒／0.75秒の予告後に発生させ、それぞれ火属性ダメージ（攻撃力・魔力の高い方の12%）を与える（高低差2m超の段差では以降の噴火が中断）。再使用まで4秒。", "When your basic attack hits or you use a memory, gain one heat mark, lasting 6 seconds and stacking up to 3 times. With 3 marks, your next basic attack hit or memory use consumes them all to cause eruptions 2m, 4m, and 6m ahead in a 1m radius, after warnings lasting 0.35, 0.55, and 0.75 seconds respectively. Each deals Fire damage equal to 12% of whichever is higher: your Attack or Ability Power. A terrain step with a height difference greater than 2m stops the remaining eruptions. Cooldown: 4 seconds.",
                new[] { C("InfernusHeatEruption",12,12) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mark,count:3,lifetimeMillis:6000,maxInstances:1,element:BossElement.Fire),
                InfernusEruptions(BossEvent.Clock,"InfernusHeatEruption",4000,350,200,2000,1000)),
            InfernusProfile("stage3", "2セット効果の噴火列の最終地点に0.8秒の予告後、2秒間持続する半径2mの炎柱を召喚する。炎柱は0.5秒間隔で4回、各攻撃力・魔力の高い方の8%のダメージを与える（同時に最大1本まで）。", "At the final location of the 2-piece eruption sequence, show a warning for 0.8 seconds, then summon a flame pillar with a 2m radius for 2 seconds. It deals damage 4 times at 0.5-second intervals, each equal to 8% of whichever is higher: your Attack or Ability Power. Only one pillar can exist at once.",
                new[] { C("InfernusHeatPillar",8,8) },
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusHeatPillar",delayMillis:800,lifetimeMillis:2000,intervalMillis:500,count:4,maxInstances:1,radiusMilli:2000,element:BossElement.Fire)),
            InfernusProfile("stage6", "2セット効果の発動から1秒後、自身の位置から4方向へ咆哮弾を放ち（弾速12m/秒、射程6m、幅0.7m、持続0.5秒）、各22.5%（同一の敵には合計で攻撃力・魔力の高い方の90%、上限100%まで）のダメージを与える。同時に2秒間、攻撃力・魔力の高い方の38%分（上限40%、重複不可）の障壁を獲得する。再使用まで10秒。", "One second after the 2-piece effect activates, fire roar projectiles from your position in 4 directions. They travel at 12m/s, have a 6m range, are 0.7m wide, and last 0.5 seconds. Each deals 22.5% of whichever is higher: your Attack or Ability Power, up to a total of 90% per enemy (cap: 100%). At the same time, gain a shield for 2 seconds equal to 38% (cap: 40%; does not stack). Cooldown: 10 seconds.",
                new[] { C("InfernusBurst",90,100), C("InfernusBurstShield",38,40,BossCoefficientKind.Shield) },
                InfernusRadial(BossEvent.Clock,"InfernusBurst",10000,1000,12000,6000,700,500),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"InfernusBurstShield",delayMillis:1000,lifetimeMillis:2000,maxInstances:1,element:BossElement.Fire,followOwner:true)),
        };

        internal static BossRewardProfile[] CreateInfernusRewards()
        {
            BossRewardAction Timer() => new BossRewardAction(BossRewardActionKind.NativeInterval,1000,1000,durationMillis:1000,order:0);
            BossRewardAction Stack() => new BossRewardAction(BossRewardActionKind.NativeState,1000,1000,cooldownMillis:1000,durationMillis:2000,count:3,order:1);
            BossRewardAction Crit() => new BossRewardAction(BossRewardActionKind.NativeDamage,3000,5000,cooldownMillis:4000,count:1,order:2);
            return new[] { new BossRewardProfile(InfernusRewardId,InfernusSetId,"Gem_U_EternalFlame",BossRewardAdapter.EternalFlame,new[]
            {
                new BossRewardStage(1,new Txt("自身が装着している「エターナルフレイム」由来の呪いの持続時間および残り時間を1秒延長する（効果更新時も同様）。装備解除時は延長分のみ元に戻る（呪い自体は解除されない）。", "Extend the duration and remaining time of curses from your equipped Eternal Flame by 1 second, including when the effect refreshes. Unequipping removes only the extension, not the curse itself."),new[] { Timer() }),
                new BossRewardStage(2,new Txt("段階1の効果に加え、自身の直接攻撃によって自身の呪い効果が発動して火傷（Fire）を蓄積させた際、追加で火傷を1スタック付与する（同一の敵への再使用まで2秒・最大3体まで個別管理、自身の再使用まで1秒。召喚や間接ダメージでは発動しない）。", "In addition to Stage 1, when your direct attack triggers your curse and adds a Burn (Fire) stack, add one extra Burn stack. Cooldown: 2 seconds per enemy, tracked separately for up to 3 enemies, and 1 second for you. Summons and indirect damage cannot trigger this effect."),new[] { Timer(),Stack() }),
                new BossRewardStage(3,new Txt("段階2の効果に加え、自身の呪いと火傷（Fire）が3〜4スタック付与されている敵1体に対し、「エターナルフレイム」を装着した記憶スキルの発動による最初の主要ダメージが会心となる必要火傷スタック条件を「5」から「3」へと緩和する。再使用まで4秒（1回の発動につき1回のみ）。", "In addition to Stage 2, against one enemy affected by your curse and 3–4 Burn (Fire) stacks, reduce the required Burn stacks from 5 to 3 for the first main damage of a memory skill equipped with Eternal Flame to critically strike. Cooldown: 4 seconds, once per activation."),new[] { Timer(),Stack(),Crit() }),
            }) };
        }

        public static UniqueDef[] CreateInfernusPieces() => new[]
        {
            new UniqueDef("unique.boss_infernus.weapon","weapon.blaze_greatsword",new Txt("噴火の大剣","Eruption Greatsword"),InfernusSetId,"boss_infernus.weapon"),
            new UniqueDef("unique.boss_infernus.armor","armor.ember_plate",new Txt("炉壁の鎧","Furnacewall Plate"),InfernusSetId,"boss_infernus.armor"),
            new UniqueDef("unique.boss_infernus.charm","charm.ember_locket",new Txt("不滅炉の火種","Undying Furnace Spark"),InfernusSetId,"boss_infernus.charm"),
            new UniqueDef("unique.boss_infernus.head","head.ember_crown",new Txt("熔冠","Molten Crown"),InfernusSetId,"boss_infernus.head"),
            new UniqueDef("unique.boss_infernus.hands","hands.ember_gauntlets",new Txt("赤熱の拳","Redheat Fists"),InfernusSetId,"boss_infernus.hands"),
            new UniqueDef("unique.boss_infernus.feet","feet.ember_treads",new Txt("火柱越えの靴","Flamepillar Treads"),InfernusSetId,"boss_infernus.feet"),
        };
        public static SetDef[] CreateInfernusSets() => new[]
        {
            new SetDef { Id=InfernusSetId, Name=new Txt("噴火炉の軍装","Eruptionforge Warplate"), BossTypeName="Mon_LavaLand_BossInfernus", BossReward=InfernusRewardId,
                TwoPiece=Array.Empty<StatLine>(), ThreePiece=Array.Empty<PowerLine>(), SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_infernus.stage2"),new BossSetStage(3,"boss_infernus.stage3"),new BossSetStage(6,"boss_infernus.stage6") },
                LinkStages=new[] { new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward, Requires=new[] { "Gem_U_EternalFlame" }, Value=1 }),new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward, Requires=new[] { "Gem_U_EternalFlame" }, Value=2 }),new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward, Requires=new[] { "Gem_U_EternalFlame" }, Value=3 }) } },
        };
    }
}

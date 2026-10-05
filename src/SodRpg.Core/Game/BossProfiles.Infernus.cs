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
            InfernusProfile("weapon", "主通常攻撃で前方1.5/3/4.5mへ半径0.8mの噴火、0.30/0.45/0.60秒後に各10%H。段差2m超で以遠中止、CD5秒。Fire・優位type、追加会心/procなし。", "Native main basic hit: eruptions at1.5/3/4.5m forward, radius0.8m,10%H each after0.30/0.45/0.60s. Stop further points at terrain steps over2m; CD5s. Fire, dominant damage type, no added crit/proc.",
                new[] { C("InfernusEruption",10,10) }, InfernusEruptions(BossEvent.MainHit,"InfernusEruption",5000,300,150,1500,800)),
            InfernusProfile("armor", "native被弾時の本人地点を0.35秒予告、半径2mに24%H着地衝撃＋通常敵を外へ0.8m押出し。CD7秒、boss/CC免疫移動除外、硬CCなし。", "Native incoming damage fixes your position: after0.35s, radius2m landing impact24%H and0.8m outward push. CD7s; bosses/CC-immune enemies do not move; no hard CC.",
                new[] { C("InfernusLanding",24,24) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.ShapeAttack,BossPayload.Push,"InfernusLanding",cooldownMillis:7000,delayMillis:350,maxInstances:1,radiusMilli:2000,magnitudeMilli:800,element:BossElement.Fire)),
            InfernusProfile("charm", "記憶使用確定でカーソル合法地点8m以内へ炎柱。0.50秒後、半径1.5mに16%H＋0.5秒間隔3tick各6%H、柱寿命1.5秒、最大1柱、CD8秒。", "Confirmed native memory use: flame pillar at a legal cursor point within8m. After0.50s, radius1.5m initial16%H plus three6%H ticks0.5s apart,1.5s pillar lifetime,max1 pillar,CD8s.",
                new[] { C("InfernusPillarInitial",16,16), C("InfernusPillarTick",6,6) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusPillarInitial",anchor:BossAnchor.Cursor,cooldownMillis:8000,delayMillis:500,lifetimeMillis:1500,maxInstances:1,radiusMilli:1500,rangeMilli:8000,element:BossElement.Fire),
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusPillarTick",delayMillis:1000,lifetimeMillis:1500,intervalMillis:500,count:3,maxInstances:1,radiusMilli:1500,element:BossElement.Fire)),
            InfernusProfile("head", "記憶使用確定時の方向へ長5m×幅1mの炎を0/0.2/0.4秒に3回、各12%H・同敵各回1命中。寿命0.6秒、最大1列、CD7秒。追跡回転/操作blockなし。", "Confirmed native memory use freezes direction:5m by1m flame line at0/0.2/0.4s,12%H each,one hit per enemy per pulse.0.6s lifetime,max1 sequence,CD7s; no steering or control block.",
                new[] { C("InfernusBreath",12,12) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusBreath",BossShape.Line,cooldownMillis:7000,lifetimeMillis:600,intervalMillis:200,count:3,maxInstances:1,rangeMilli:5000,widthMilli:1000,element:BossElement.Fire)),
            InfernusProfile("hands", "主通常攻撃3回/6秒で0.35秒予告後4方向咆哮弾、速度10m/秒・射程4m・幅0.5m・寿命0.4秒、各5%H・同敵合計20%Hまで。最大4弾、CD6秒。", "Three native main basic hits within6s: after0.35s, four radial roar shots,10m/s,range4m,width0.5m,lifetime0.4s,5%H each,total20%H per enemy. Max4 shots,CD6s.",
                new[] { C("InfernusRoar",20,20) }, InfernusRadial(BossEvent.MainHit,"InfernusRoar",6000,350,10000,4000,500,400,3)),
            InfernusProfile("feet", "本人native移動完了で前方長3m×幅1.5mに18%H＋1.2m押出し、CD5秒。壁で停止、boss/CC免疫移動なし、硬CC/壁追加damageなし。", "Your native movement completion:3m by1.5m forward impact18%H and1.2m push,CD5s. Stop at walls; bosses/CC-immune enemies do not move; no hard CC or wall bonus damage.",
                new[] { C("InfernusDashImpact",18,18) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Push,"InfernusDashImpact",BossShape.Line,cooldownMillis:5000,maxInstances:1,rangeMilli:3000,widthMilli:1500,magnitudeMilli:1200,element:BossElement.Fire)),
            InfernusProfile("stage2", "主通常攻撃/記憶使用確定で熱印1、最大3・寿命6秒。3印の次native入力で全消費、前方2/4/6mに半径1mの噴火各12%H、0.35/0.55/0.75秒予告、段差2m超で以遠中止、CD4秒。", "Native main basic hit/confirmed memory use gains1 heat mark,max3,6s lifetime. The next native input after3 marks consumes all: eruptions2/4/6m forward,radius1m,12%H each after0.35/0.55/0.75s; stop further points at steps over2m,CD4s.",
                new[] { C("InfernusHeatEruption",12,12) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mark,count:3,lifetimeMillis:6000,maxInstances:1,element:BossElement.Fire),
                InfernusEruptions(BossEvent.Clock,"InfernusHeatEruption",4000,350,200,2000,1000)),
            InfernusProfile("stage3", "2点噴火列の最後の合法地点へ0.8秒予告の半径2m炎柱。0.5秒間隔4tick各8%H、寿命2秒、最大1柱。敵数で増殖しない。", "At the last legal point of the2-piece eruption line: radius2m flame pillar after0.8s,four8%H ticks0.5s apart,2s lifetime,max1 pillar; enemy count cannot multiply pillars.",
                new[] { C("InfernusHeatPillar",8,8) },
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"InfernusHeatPillar",delayMillis:800,lifetimeMillis:2000,intervalMillis:500,count:4,maxInstances:1,radiusMilli:2000,element:BossElement.Fire)),
            InfernusProfile("stage6", "2点発動から1秒後、本人から4方向咆哮弾：速度12m/秒・射程6m・幅0.7m・寿命0.5秒、各22.5%H、同敵合計90%H（上限100%H）。同時に本人shield38%H/2秒（上限40%H、非加算）、CD10秒・最大1burst。", "One second after the2-piece proc: four radial roar shots from your position at burst time,12m/s,range6m,width0.7m,lifetime0.5s,22.5%H each,total90%H per enemy(cap100%H). Simultaneous self shield38%H for2s(cap40%H,nonadditive),CD10s,max1 burst.",
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
                new BossRewardStage(1,new Txt("本人の装着EternalFlame由来native呪いだけ持続/残時間を取得値+1秒へ。native更新も同じ上限、解除時は追加差分だけ復元。本体呪いは削除しない。", "Only your equipped EternalFlame native curse gains1s duration/remaining. Native refresh retains that bound; unequip restores only the added difference,never deletes the native curse."),new[] { Timer() }),
                new BossRewardStage(2,new Txt("2点＋本人native damageで自分の呪いの本体procが成功しFireを積んだ時だけ追加1Fire。同敵CD2秒・本人CD1秒・最大3対象台帳。失敗/生成damage/他者damageには追加なし。", "2-piece plus1 extra Fire only after your own curse successfully adds Fire through its native proc on your native damage. Target CD2s,owner CD1s,ledger max3 targets; no extra on failed procs,generated damage or another actor's damage."),new[] { Timer(),Stack() }),
                new BossRewardStage(3,new Txt("4点＋自分の呪い付き実fireStack3～4の敵1体へ、装着SkillTriggerのnative activation最初のmain damageだけ強制会心門5→3、CD4秒・1activation1回。元の5stack門と実stack増幅を保持。", "4-piece plus lower only the native crit gate5→3 for the first main damage packet of one equipped SkillTrigger activation against one enemy with your curse and real Fire stacks3–4. CD4s,once per activation; preserve original>=5 crit and real-stack amplification."),new[] { Timer(),Stack(),Crit() }),
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

using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string LightSetId = "set.boss_light_elemental";
        public const string LightRewardId = "boss_light_elemental.world_cracker";
        public const string LightEventOrder = "native-input-once;charge-three-next-input-consumes;stage2-beam350;actual-clipped-terminal-stage3-lightning650;stage6-independent-cd12-three-sweeps300-400each;parts-independent;nonentity-crystals-replaced-max2-life3s;beam-target-once;native-success-three-same-target-gap1s-ledger3-endpulse350-cd6-life2-pending1";
        public const string LightNativeContract = "Mon_Special_BossLightElemental;Ai_U_WorldCracker-owned-installed-St_U_WorldCracker-parent-caster-actor-life-room-run;loaded-positive-angleSpeed-plus30-cap1.5;loaded-positive-radius-plus.30-cap1.5;restore-exact-loaded-fields;native-channel-cost-duration-interval-raycast-maxDistance-preserved;ActiveLogicUpdate-local1-clipped-num-after-ground-raycast-distance-plus2-exact-Dispatch;positive-final-native-ticks-only-generated-excluded;stop-cancels-pending;normal-stages-no-required-memory;nonEntity-crystals-no-summon-proc-loot;bounded-owner64-native128-beams12-targets8-scan64-wall32";

        private static BossMoveProfile LightProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_light_elemental." + suffix, LightSetId, new Txt(ja, en), channels, actions);
        private static BossAction LightBeam(BossEvent trigger, string channel, int cd, int delay, int range, int width, int duration = 0, int angle = 0, int count = 1)
            => new BossAction(trigger, BossMechanism.ShapeAttack, BossPayload.Damage, channel, BossShape.Line,
                cooldownMillis: cd, delayMillis: delay, lifetimeMillis: duration, count: count, maxTargets: 8, maxInstances: 1,
                rangeMilli: range, widthMilli: width, angleMilli: angle, element: BossElement.Light);
        private static BossAction LightShield(BossEvent trigger, string channel, int cd)
            => new BossAction(trigger, BossMechanism.Defense, BossPayload.Shield, channel, cooldownMillis: cd,
                lifetimeMillis: 2000, maxTargets: 1, maxInstances: 1, followOwner: true, element: BossElement.Light);
        internal static BossMoveProfile[] CreateLightMoves() => new[]
        {
            LightProfile("weapon", "E1で敵方向を固定、0.30秒予告後に長7m×幅0.8mの瞬間Light beam0.28H、対象1hit、CD5秒。", "E1 fixes the enemy direction: after0.30s telegraph, one7m/width0.8m instantaneous Light beam0.28H,one hit per target,CD5s.",
                new[] { C("LightBeamAtk",28,28) }, LightBeam(BossEvent.MainHit,"LightBeamAtk",5000,300,7000,800)),
            LightProfile("armor", "E4被弾で本人にshield0.20H/2秒、独立container・非加算、CD7秒。永続無敵なし。", "E4 native damage taken grants you one independent,non-additive0.20H shield for2s,CD7s;no permanent invulnerability.",
                new[] { C("LightShardShield",20,20,BossCoefficientKind.Shield) }, LightShield(BossEvent.NativeDamageTaken,"LightShardShield",7000)),
            LightProfile("charm", "E2で本人2m前の合法地点に非Entity光晶1個/3秒。0.5秒後に光晶6m内の最近敵へ長6m×幅0.6m Light beam0.24Hを1回、本人所有最大1、CD8秒。召喚proc/lootなし。", "E2 places one non-Entity crystal at a legal point2m ahead for3s. After0.5s it fires one6m/width0.6m Light beam0.24H toward the nearest enemy within6m;max1 owned crystal,CD8s,no summon proc or loot.",
                new[] { C("LightCrystalBeam",24,24) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Deployable,BossPayload.Deploy,"LightCrystalBeam",cooldownMillis:8000,delayMillis:500,lifetimeMillis:3000,maxTargets:8,maxInstances:1,rangeMilli:6000,widthMilli:600,magnitudeMilli:2000,element:BossElement.Light)),
            LightProfile("head", "E2カーソル合法地点を本人8m内で固定、0.45/0.75秒予告後に半径1.2m Light落雷各0.16H、同時1列、CD7秒。", "E2 fixes a legal cursor point within8m: radius1.2m Light lightning0.16H each after0.45/0.75s telegraphs,max1 sequence,CD7s.",
                new[] { C("LightLightning",32,32) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"LightLightning",anchor:BossAnchor.Cursor,cooldownMillis:7000,delayMillis:450,intervalMillis:300,count:2,maxTargets:8,maxInstances:1,radiusMilli:1200,rangeMilli:8000,element:BossElement.Light)),
            LightProfile("hands", "6秒内のE1主撃3回で前方-25°→+25°を0.8秒掃引する長5m×幅0.6m Light beam、0.30秒予告、対象全掃引で1hit0.30H、最大1、CD6秒。", "Every3 E1 main hits within6s: after0.30s telegraph,sweep a5m/width0.6m Light beam from-25° to+25° over0.8s;one0.30H hit per target across the whole sweep,max1,CD6s.",
                new[] { C("LightBeamBarrage",30,30) },
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LightBeamBarrage",BossShape.Line,cooldownMillis:6000,delayMillis:300,lifetimeMillis:800,mainHits:3,counterLifetimeMillis:6000,maxTargets:8,maxInstances:1,rangeMilli:5000,widthMilli:600,angleMilli:50000,element:BossElement.Light)),
            LightProfile("feet", "E3 native移動完了で移動方向と±20°を固定、0.25秒予告後に長4m×幅0.5m Light beam3方向各0.08H、同対象合計cap0.24H、CD5秒。追加入力/自動移動なし。", "E3 native movement completion fixes the movement direction and±20°: after0.25s telegraph,three4m/width0.5m Light beams0.08H each,total cap0.24H per target,CD5s;no extra input or automatic movement.",
                new[] { C("LightFlashBeams",24,24) }, LightBeam(BossEvent.MovementCompleted,"LightFlashBeams",5000,250,4000,500,count:3,angle:40000)),
            LightProfile("stage2", "E1/E2で光相1、最大3・寿命6秒。3相の次E1/E2で全消費、敵/カーソル方向へ0.35秒予告後に長8m×幅1m Light beam0.35H、CD4秒。任意2部位、特定記憶/HP閾値不要。", "E1/E2 gains one light charge,max3,life6s. The next E1/E2 with3 charges consumes all: after0.35s telegraph,an8m/width1m Light beam0.35H toward enemy/cursor,CD4s. Any2 pieces,no required memory or HP threshold.",
                new[] { C("LightChargedBeam",35,35) }, LightBeam(BossEvent.MainHit,"LightChargedBeam",4000,350,8000,1000),
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mark,lifetimeMillis:6000,count:3,maxTargets:1,maxInstances:1)),
            LightProfile("stage3", "2点beamの実地形clip済み終端へ0.65秒予告の半径1.8m Light落雷0.30H、同時1予約。任意3部位、特定記憶不要。", "At the2-piece beam's actual terrain-clipped endpoint:0.65s warning then radius1.8m Light lightning0.30H,max1 reservation. Any3 pieces,no required memory.",
                new[] { C("LightTerminalLightning",30,30) },
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"LightTerminalLightning",delayMillis:650,maxTargets:8,maxInstances:1,radiusMilli:1800,element:BossElement.Light)),
            LightProfile("stage6", "2点発動後に本人起点を固定、長6m×幅0.7m Light beamを-45°→-15°/-15°→+15°/+15°→+45°へ各0.4秒掃引、予告0.3秒、各0.30H/対象1hit（A90/cap100）。旧光晶を更新し支持光晶2個/3秒、各shield0.19Hを本人へ同一container合計0.38H/2秒非加算（B38/cap40）。CD12秒・同時1phase・全光晶最大2。", "After the2-piece activation,freeze your origin:three6m/width0.7m Light beam sweeps,-45°→-15°/-15°→+15°/+15°→+45°,0.4s each after0.3s warning,0.30H per target once per sweep(A90/cap100). Replace old crystals with2 support crystals for3s;each awards0.19H to one non-additive total0.38H shield container for2s(B38/cap40). CD12s,max1 phase,max2 crystals total.",
                new[] { C("LightFractureSweeps",90,100), C("LightSupportShield",38,40,BossCoefficientKind.Shield) },
                LightBeam(BossEvent.Clock,"LightFractureSweeps",12000,300,6000,700,400,90000,3),
                LightShield(BossEvent.Clock,"LightSupportShield",0),
                new BossAction(BossEvent.Clock,BossMechanism.Deployable,BossPayload.Deploy,lifetimeMillis:3000,count:2,maxTargets:1,maxInstances:1,rangeMilli:2000,element:BossElement.Light)),
        };
        internal static BossRewardProfile[] CreateLightRewards()
        {
            var turn = new BossRewardAction(BossRewardActionKind.NativeSpeed,30000,30000,magnitudeMilli:1500);
            var width = new BossRewardAction(BossRewardActionKind.NativeState,300,300,order:1,magnitudeMilli:1500);
            var pulse = new BossRewardAction(BossRewardActionKind.NativeDamage,450,450,6000,durationMillis:350,count:2,rangeMilli:1800,order:2,magnitudeMilli:3,intervalMillis:1000,targetCapMilli:3,budgetMilli:900);
            return new[] { new BossRewardProfile(LightRewardId,LightSetId,"St_U_WorldCracker",BossRewardAdapter.WorldCracker,new[]
            {
                new BossRewardStage(1,new Txt("本人装着WorldCrackerのnative beam旋回を本体取得angleSpeed+30°/秒、最大native×1.5へ変更（native>0のみ）。本体channel/消費/持続を維持。", "Your installed WorldCracker native beam turns at loaded angleSpeed+30°/s,capped at native×1.5,only if native>0;preserve channel,cost and duration."),new[] { turn }),
                new BossRewardStage(2,new Txt("2点連携＋同native beamのSphereCast radiusを本体取得値+0.30m、最大native×1.5へ変更（native>0のみ）。raycast/maxDistance/対象別damageInterval維持。", "2-piece link plus loaded native SphereCast radius+0.30m,capped at native×1.5,only if native>0;preserve raycast,maxDistance and per-target damageInterval."),new[] { turn,width }),
                new BossRewardStage(3,new Txt("4点連携＋同敵へのnative成功tick3回（gap≤1秒、台帳最大3敵）で直前の実damage用clipped終端に半径1.8m、0.35秒予告の0.45H Light pulse。CD6秒、native lifetime最大2pulse・同時1、生成pulseはtickに数えずchannel停止で予約中止。", "4-piece link plus3 successful native ticks on the same enemy(gap≤1s,ledger max3 enemies):one radius1.8m0.45H Light pulse after0.35s warning at the actual last damage-clipped endpoint. CD6s,max2 pulses per native lifetime,one pending;generated pulses never count and channel stop cancels pending."),new[] { turn,width,pulse }),
            }) };
        }
        public static UniqueDef[] CreateLightPieces() => new[]
        {
            new UniqueDef("set.boss_light_elemental.weapon","weapon.pilgrim_staff",new Txt("光裂の杖","Radiant Fracture Staff"),LightSetId,"boss_light_elemental.weapon"),
            new UniqueDef("set.boss_light_elemental.armor","armor.prayer_shawl",new Txt("光片の法衣","Radiant Shard Robe"),LightSetId,"boss_light_elemental.armor"),
            new UniqueDef("set.boss_light_elemental.charm","charm.lotus_seal",new Txt("世界罅の結晶","Worldfissure Crystal"),LightSetId,"boss_light_elemental.charm"),
            new UniqueDef("set.boss_light_elemental.head","head.radiant_halo",new Txt("雷光の冠","Lightning Halo"),LightSetId,"boss_light_elemental.head"),
            new UniqueDef("set.boss_light_elemental.hands","hands.radiant_wraps",new Txt("光束の手巻き","Beam Wraps"),LightSetId,"boss_light_elemental.hands"),
            new UniqueDef("set.boss_light_elemental.feet","feet.dawn_steps",new Txt("閃光の履","Flash Shoes"),LightSetId,"boss_light_elemental.feet"),
        };
        public static SetDef[] CreateLightSets() => new[]
        {
            new SetDef { Id=LightSetId,Name=new Txt("光裂の法装","Radiant Fracture Raiment"),BossTypeName="Mon_Special_BossLightElemental",BossReward=LightRewardId,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_light_elemental.stage2"),new BossSetStage(3,"boss_light_elemental.stage3"),new BossSetStage(6,"boss_light_elemental.stage6") },
                LinkStages=new[] { new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward,Value=1,Requires=new[] { "St_U_WorldCracker" } }),new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward,Value=2,Requires=new[] { "St_U_WorldCracker" } }),new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward,Value=3,Requires=new[] { "St_U_WorldCracker" } }) } },
        };
    }
}

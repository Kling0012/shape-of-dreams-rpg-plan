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
            LightProfile("weapon", "第1記憶（通常攻撃）が敵に命中したとき、その敵の方向へ0.30秒の予告後に長さ7m×幅0.8mの光線を瞬時に放ち、攻撃力・魔力の高い方の28%の光属性ダメージを与える（対象ごとに1回命中、再使用まで5秒）。", "E1 fixes the enemy direction: after0.30s telegraph, one7m/width0.8m instantaneous Light beam0.28H,one hit per target,CD5s.",
                new[] { C("LightBeamAtk",28,28) }, LightBeam(BossEvent.MainHit,"LightBeamAtk",5000,300,7000,800)),
            LightProfile("armor", "第4記憶の使用中にダメージを受けたとき、自身に攻撃力・魔力の高い方の20%の障壁を2秒間付与する（重複不可、再使用まで7秒、無敵効果なし）。", "E4 native damage taken grants you one independent,non-additive0.20H shield for2s,CD7s;no permanent invulnerability.",
                new[] { C("LightShardShield",20,20,BossCoefficientKind.Shield) }, LightShield(BossEvent.NativeDamageTaken,"LightShardShield",7000)),
            LightProfile("charm", "第2記憶を使ったとき、自身の2m前方に光晶を1個設置する（持続3秒）。0.5秒後、光晶から6m以内の最も近い敵へ長さ6m×幅0.6mの光線を放ち、攻撃力・魔力の高い方の24%の光属性ダメージを1回与える（同時に存在できる光晶は最大1個、再使用まで8秒、召喚物扱いではない）。", "E2 places one non-Entity crystal at a legal point2m ahead for3s. After0.5s it fires one6m/width0.6m Light beam0.24H toward the nearest enemy within6m;max1 owned crystal,CD8s,no summon proc or loot.",
                new[] { C("LightCrystalBeam",24,24) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Deployable,BossPayload.Deploy,"LightCrystalBeam",cooldownMillis:8000,delayMillis:500,lifetimeMillis:3000,maxTargets:8,maxInstances:1,rangeMilli:6000,widthMilli:600,magnitudeMilli:2000,element:BossElement.Light)),
            LightProfile("head", "第2記憶を使ったとき、カーソル位置（自身から8m以内）に0.45秒後と0.75秒後の予告を伴う半径1.2mの落雷を落とし、それぞれ攻撃力・魔力の高い方の16%の光属性ダメージを与える（再使用まで7秒）。", "E2 fixes a legal cursor point within8m: radius1.2m Light lightning0.16H each after0.45/0.75s telegraphs,max1 sequence,CD7s.",
                new[] { C("LightLightning",32,32) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"LightLightning",anchor:BossAnchor.Cursor,cooldownMillis:7000,delayMillis:450,intervalMillis:300,count:2,maxTargets:8,maxInstances:1,radiusMilli:1200,rangeMilli:8000,element:BossElement.Light)),
            LightProfile("hands", "6秒以内に第1記憶（通常攻撃）を敵に3回命中させると、0.30秒の予告後、前方-25°から+25°へ0.8秒間なぎ払う長さ5m×幅0.6mの光線を放ち、敵1体につき1回、攻撃力・魔力の高い方の30%の光属性ダメージを与える（再使用まで6秒）。", "Every3 E1 main hits within6s: after0.30s telegraph,sweep a5m/width0.6m Light beam from-25° to+25° over0.8s;one0.30H hit per target across the whole sweep,max1,CD6s.",
                new[] { C("LightBeamBarrage",30,30) },
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LightBeamBarrage",BossShape.Line,cooldownMillis:6000,delayMillis:300,lifetimeMillis:800,mainHits:3,counterLifetimeMillis:6000,maxTargets:8,maxInstances:1,rangeMilli:5000,widthMilli:600,angleMilli:50000,element:BossElement.Light)),
            LightProfile("feet", "第3記憶（移動記憶）による移動完了時、移動方向とその左右20°の計3方向へ0.25秒の予告後に長さ4m×幅0.5mの光線を放ち、それぞれ攻撃力・魔力の高い方の8%の光属性ダメージを与える（同一対象への合計ダメージ上限は攻撃力・魔力の高い方の24%、再使用まで5秒）。", "E3 native movement completion fixes the movement direction and±20°: after0.25s telegraph,three4m/width0.5m Light beams0.08H each,total cap0.24H per target,CD5s;no extra input or automatic movement.",
                new[] { C("LightFlashBeams",24,24) }, LightBeam(BossEvent.MovementCompleted,"LightFlashBeams",5000,250,4000,500,count:3,angle:40000)),
            LightProfile("stage2", "第1記憶の命中または第2記憶の使用時に「光相」を1獲得する（最大3スタック、持続6秒）。3スタックの状態で再使用可能時に次の第1記憶を命中または第2記憶を使用すると全て消費し、敵またはカーソルの方向へ0.35秒の予告後に長さ8m×幅1mの光線を放ち、攻撃力・魔力の高い方の35%の光属性ダメージを与える（再使用まで4秒）。", "E1/E2 gains one light charge,max3,life6s. The next E1/E2 with3 charges consumes all: after0.35s telegraph,an8m/width1m Light beam0.35H toward enemy/cursor,CD4s. Any2 pieces,no required memory or HP threshold.",
                new[] { C("LightChargedBeam",35,35) }, LightBeam(BossEvent.MainHit,"LightChargedBeam",4000,350,8000,1000),
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mark,lifetimeMillis:6000,count:3,maxTargets:1,maxInstances:1)),
            LightProfile("stage3", "2セット効果の光線の着弾地点（終端）に0.65秒の予告後、半径1.8mの落雷を落とし、攻撃力・魔力の高い方の30%の光属性ダメージを与える（同時に予約できるのは1回まで）。", "At the2-piece beam's actual terrain-clipped endpoint:0.65s warning then radius1.8m Light lightning0.30H,max1 reservation. Any3 pieces,no required memory.",
                new[] { C("LightTerminalLightning",30,30) },
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"LightTerminalLightning",delayMillis:650,maxTargets:8,maxInstances:1,radiusMilli:1800,element:BossElement.Light)),
            LightProfile("stage6", "2セット効果の発動後、自身の位置から0.3秒の予告後、長さ6m×幅0.7mの光線で-45°→-15°、-15°→+15°、+15°→+45°の3区画を順に各0.4秒間なぎ払い、各なぎ払いにつき敵1体ごとに1回、攻撃力・魔力の高い方の30%の光属性ダメージを与える（合計90%、上限100%）。さらに既存の光晶を消去して支援光晶を2個生成し（持続3秒、攻撃能力なし）、自身に攻撃力・魔力の高い方の38%（上限40%）の障壁を2秒間付与する（重複不可、再使用まで12秒、光晶は全体で最大2個まで）。", "After the2-piece activation,freeze your origin:three6m/width0.7m Light beam sweeps,-45°→-15°/-15°→+15°/+15°→+45°,0.4s each after0.3s warning,0.30H per target once per sweep(A90/cap100). Replace old crystals with2 support crystals for3s;each awards0.19H to one non-additive total0.38H shield container for2s(B38/cap40). CD12s,max1 phase,max2 crystals total.",
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
                new BossRewardStage(1,new Txt("装備中の「WorldCracker」による本体光線の旋回速度を+30°/秒増加させる（元の旋回速度の最大1.5倍まで）。本体の詠唱・消費・持続時間はそのまま維持される。", "Your installed WorldCracker native beam turns at loaded angleSpeed+30°/s,capped at native×1.5,only if native>0;preserve channel,cost and duration."),new[] { turn }),
                new BossRewardStage(2,new Txt("2部位効果に加えて、同光線の判定半径を+0.30m拡大する（元の半径の最大1.5倍まで）。射程やヒット間隔はそのまま維持される。", "2-piece link plus loaded native SphereCast radius+0.30m,capped at native×1.5,only if native>0;preserve raycast,maxDistance and per-target damageInterval."),new[] { turn,width }),
                new BossRewardStage(3,new Txt("4部位効果に加えて、同じ敵に本体の攻撃が間隔1秒以内で3回命中すると（最大3体まで記録）、直前の着弾地点に0.35秒の予告後、半径1.8mの光の波動を放ち攻撃力・魔力の高い方の45%の光属性ダメージを与える（再使用まで6秒、光線1回につき最大2回まで、詠唱中断で待機中の波動はキャンセル）。", "4-piece link plus3 successful native ticks on the same enemy(gap≤1s,ledger max3 enemies):one radius1.8m0.45H Light pulse after0.35s warning at the actual last damage-clipped endpoint. CD6s,max2 pulses per native lifetime,one pending;generated pulses never count and channel stop cancels pending."),new[] { turn,width,pulse }),
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

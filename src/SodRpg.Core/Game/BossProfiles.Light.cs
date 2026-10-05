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
            LightProfile("weapon", "第1記憶（通常攻撃）が敵に命中したとき、その敵の方向へ0.30秒の予告後に長さ7m×幅0.8mの光線を瞬時に放ち、攻撃力・魔力の高い方の28%の光属性ダメージを与える（対象ごとに1回命中、再使用まで5秒）。", "When your basic attack (first memory) hits an enemy, fire an instant beam toward that enemy after a 0.30-second warning. The beam is 7m long and 0.8m wide, and deals Light damage equal to 28% of whichever is higher: your Attack or Ability Power, hitting each target once. Cooldown: 5 seconds.",
                new[] { C("LightBeamAtk",28,28) }, LightBeam(BossEvent.MainHit,"LightBeamAtk",5000,300,7000,800)),
            LightProfile("armor", "第4記憶の使用中にダメージを受けたとき、自身に攻撃力・魔力の高い方の20%の障壁を2秒間付与する（重複不可、再使用まで7秒、無敵効果なし）。", "When you take damage while using your fourth memory, gain a non-stacking shield equal to 20% of whichever is higher: your Attack or Ability Power, for 2 seconds. This does not grant invulnerability. Cooldown: 7 seconds.",
                new[] { C("LightShardShield",20,20,BossCoefficientKind.Shield) }, LightShield(BossEvent.NativeDamageTaken,"LightShardShield",7000)),
            LightProfile("charm", "第2記憶を使ったとき、自身の2m前方に光晶を1個設置する（持続3秒）。0.5秒後、光晶から6m以内の最も近い敵へ長さ6m×幅0.6mの光線を放ち、攻撃力・魔力の高い方の24%の光属性ダメージを1回与える（同時に存在できる光晶は最大1個、再使用まで8秒、召喚物扱いではない）。", "When you use your second memory, place 1 light crystal 2m in front of you for 3 seconds. After 0.5 seconds, it fires a beam 6m long and 0.6m wide toward the nearest enemy within 6m of the crystal, dealing Light damage equal to 24% of whichever is higher: your Attack or Ability Power, once. Only 1 crystal can exist at a time, and it does not count as a summon. Cooldown: 8 seconds.",
                new[] { C("LightCrystalBeam",24,24) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Deployable,BossPayload.Deploy,"LightCrystalBeam",cooldownMillis:8000,delayMillis:500,lifetimeMillis:3000,maxTargets:8,maxInstances:1,rangeMilli:6000,widthMilli:600,magnitudeMilli:2000,element:BossElement.Light)),
            LightProfile("head", "第2記憶を使ったとき、カーソル位置（自身から8m以内）に0.45秒後と0.75秒後の予告を伴う半径1.2mの落雷を落とし、それぞれ攻撃力・魔力の高い方の16%の光属性ダメージを与える（再使用まで7秒）。", "When you use your second memory, call down lightning at the cursor position within 8m of you after 0.45 and 0.75 seconds, with a warning before each strike. Each strike covers a 1.2m radius and deals Light damage equal to 16% of whichever is higher: your Attack or Ability Power. Cooldown: 7 seconds.",
                new[] { C("LightLightning",32,32) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"LightLightning",anchor:BossAnchor.Cursor,cooldownMillis:7000,delayMillis:450,intervalMillis:300,count:2,maxTargets:8,maxInstances:1,radiusMilli:1200,rangeMilli:8000,element:BossElement.Light)),
            LightProfile("hands", "6秒以内に第1記憶（通常攻撃）を敵に3回命中させると、0.30秒の予告後、前方-25°から+25°へ0.8秒間なぎ払う長さ5m×幅0.6mの光線を放ち、敵1体につき1回、攻撃力・魔力の高い方の30%の光属性ダメージを与える（再使用まで6秒）。", "After your basic attack (first memory) hits enemies 3 times within 6 seconds, fire a sweeping beam after a 0.30-second warning. The beam is 5m long and 0.6m wide, and sweeps from -25° to +25° in front of you over 0.8 seconds. It deals Light damage equal to 30% of whichever is higher: your Attack or Ability Power, once per enemy. Cooldown: 6 seconds.",
                new[] { C("LightBeamBarrage",30,30) },
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LightBeamBarrage",BossShape.Line,cooldownMillis:6000,delayMillis:300,lifetimeMillis:800,mainHits:3,counterLifetimeMillis:6000,maxTargets:8,maxInstances:1,rangeMilli:5000,widthMilli:600,angleMilli:50000,element:BossElement.Light)),
            LightProfile("feet", "第3記憶（移動記憶）による移動完了時、移動方向とその左右20°の計3方向へ0.25秒の予告後に長さ4m×幅0.5mの光線を放ち、それぞれ攻撃力・魔力の高い方の8%の光属性ダメージを与える（同一対象への合計ダメージ上限は攻撃力・魔力の高い方の24%、再使用まで5秒）。", "When movement from your third memory (movement memory) ends, fire 3 beams after a 0.25-second warning: one in your movement direction and one 20° to either side. Each beam is 4m long and 0.5m wide, and deals Light damage equal to 8% of whichever is higher: your Attack or Ability Power. Total damage to a single target is capped at 24% of whichever is higher: your Attack or Ability Power. Cooldown: 5 seconds.",
                new[] { C("LightFlashBeams",24,24) }, LightBeam(BossEvent.MovementCompleted,"LightFlashBeams",5000,250,4000,500,count:3,angle:40000)),
            LightProfile("stage2", "第1記憶の命中または第2記憶の使用時に「光相」を1獲得する（最大3スタック、持続6秒）。3スタックの状態で再使用可能時に次の第1記憶を命中または第2記憶を使用すると全て消費し、敵またはカーソルの方向へ0.35秒の予告後に長さ8m×幅1mの光線を放ち、攻撃力・魔力の高い方の35%の光属性ダメージを与える（再使用まで4秒）。", "When your first memory hits or you use your second memory, gain 1 Light Phase stack, up to 3, lasting 6 seconds. With 3 stacks and this effect off cooldown, your next first-memory hit or second-memory use consumes all stacks. After a 0.35-second warning, fire a beam 8m long and 1m wide toward the enemy or cursor, dealing Light damage equal to 35% of whichever is higher: your Attack or Ability Power. Cooldown: 4 seconds.",
                new[] { C("LightChargedBeam",35,35) }, LightBeam(BossEvent.MainHit,"LightChargedBeam",4000,350,8000,1000),
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mark,lifetimeMillis:6000,count:3,maxTargets:1,maxInstances:1)),
            LightProfile("stage3", "2セット効果の光線の着弾地点（終端）に0.65秒の予告後、半径1.8mの落雷を落とし、攻撃力・魔力の高い方の30%の光属性ダメージを与える（同時に予約できるのは1回まで）。", "At the endpoint of the 2-piece set effect's beam, call down lightning after a 0.65-second warning, dealing Light damage equal to 30% of whichever is higher: your Attack or Ability Power, in a 1.8m radius. Only 1 strike can be waiting to occur at a time.",
                new[] { C("LightTerminalLightning",30,30) },
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"LightTerminalLightning",delayMillis:650,maxTargets:8,maxInstances:1,radiusMilli:1800,element:BossElement.Light)),
            LightProfile("stage6", "2セット効果の発動後、自身の位置から0.3秒の予告後、長さ6m×幅0.7mの光線で-45°→-15°、-15°→+15°、+15°→+45°の3区画を順に各0.4秒間なぎ払い、各なぎ払いにつき敵1体ごとに1回、攻撃力・魔力の高い方の30%の光属性ダメージを与える（合計90%、上限100%）。さらに既存の光晶を消去して支援光晶を2個生成し（持続3秒、攻撃能力なし）、自身に攻撃力・魔力の高い方の38%（上限40%）の障壁を2秒間付与する（重複不可、再使用まで12秒、光晶は全体で最大2個まで）。", "After the 2-piece set effect activates, fire a sweeping beam from your position after a 0.3-second warning. The beam is 6m long and 0.7m wide, and sweeps three sections in order: -45° to -15°, -15° to +15°, and +15° to +45°, taking 0.4 seconds per section. Each sweep deals Light damage equal to 30% of whichever is higher: your Attack or Ability Power, once per enemy (90% total; cap: 100%). Also remove existing light crystals and create 2 support light crystals that last 3 seconds and cannot attack. Gain a non-stacking shield equal to 38% of whichever is higher: your Attack or Ability Power (cap: 40%), for 2 seconds. No more than 2 light crystals can exist in total. Cooldown: 12 seconds.",
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
                new BossRewardStage(1,new Txt("装備中の「WorldCracker」による本体光線の旋回速度を+30°/秒増加させる（元の旋回速度の最大1.5倍まで）。本体の詠唱・消費・持続時間はそのまま維持される。", "Increase the turning speed of your equipped WorldCracker's main beam by 30° per second, up to 1.5 times its original turning speed. Its casting, resource cost, and duration remain unchanged."),new[] { turn }),
                new BossRewardStage(2,new Txt("2部位効果に加えて、同光線の判定半径を+0.30m拡大する（元の半径の最大1.5倍まで）。射程やヒット間隔はそのまま維持される。", "In addition to the 2-piece effect, increase the same beam's hit radius by 0.30m, up to 1.5 times its original radius. Its range and time between hits remain unchanged."),new[] { turn,width }),
                new BossRewardStage(3,new Txt("4部位効果に加えて、同じ敵に本体の攻撃が間隔1秒以内で3回命中すると（最大3体まで記録）、直前の着弾地点に0.35秒の予告後、半径1.8mの光の波動を放ち攻撃力・魔力の高い方の45%の光属性ダメージを与える（再使用まで6秒、光線1回につき最大2回まで、詠唱中断で待機中の波動はキャンセル）。", "In addition to the 4-piece effect, when the main beam hits the same enemy 3 times with no more than 1 second between hits, release a pulse of light at the most recent impact point after a 0.35-second warning. The pulse deals Light damage equal to 45% of whichever is higher: your Attack or Ability Power, in a 1.8m radius. Track hits on up to 3 enemies at a time. Cooldown: 6 seconds, with at most 2 pulses per beam use. Interrupting the cast cancels any pulses waiting to occur."),new[] { turn,width,pulse }),
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

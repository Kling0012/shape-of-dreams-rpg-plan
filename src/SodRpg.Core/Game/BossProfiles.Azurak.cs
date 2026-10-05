using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string AzurakSetId = "set.boss_azurak";
        public const string AzurakRewardId = "boss_azurak.burrow";
        public const string AzurakEventOrder = "independent-stage2-native-main-hit-ledger-three-last-success-four-seconds;consume-third-even-during-cooldown;stage2-stage3-stage6-same-third-fixed-point-independent-cooldowns;parts;head-fixed-start-zone-one-inside-check-at-half-second;actual-native-emerge-only;native-packet-flat;native-stun-argument;native-owner-daze-argument";
        public const string AzurakNativeContract = "host-owner-current-skill-room-run-parent-pool-generation-exclude-generated;actual-Se_U_Burrow.Emerge-child-only;OnHit-stun-argument-native-plus-min(.15,max(0,1-native))-enemy-nonboss-nonimmune-only;OnCreate-daze-argument-max(0,native-.05)-stage6-only;instance-dealtDamageProcessor-native-OnHit-packet-flat-add-20%-total-.20H-or-replace25%-total-.45H;one-owner-eight-second-cooldown-all-targets-one-activation-budget-frozen-H;bounded-flat-compensates-native-amplification-reduction-no-extra-crit-attackeffect;original-range-damage-source-stun-attackeffect-crit-invulnerability-reuse-config-preserved;live-reconfigure-retains-native-spend-and-owner-cooldown-no-refill;terminal-detach-own-handler-no-native-status-destruction;no-summon-companion";
        private static BossChannelDef AzurakChannel(string id, int value, int cap = 0, BossCoefficientKind kind = BossCoefficientKind.Damage)
            => new BossChannelDef(id, value * 1000, (cap == 0 ? value : cap) * 1000, kind);
        private static BossMoveProfile AzurakProfile(string slot, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_azurak." + slot, AzurakSetId, new Txt(ja, en), channels, actions);
        internal static BossMoveProfile[] CreateAzurakMoves() => new[]
        {
            AzurakProfile("weapon", "通常攻撃の命中地点に、0.15秒間隔で2回の踏みつけ（半径2.5m、各攻撃力・魔力の高い方の9%）を発生させる。再使用まで4秒。", "When your basic attack hits, create two stomps at the impact point, 0.15 seconds apart. Each has a 2.5 m radius and deals 9% of your higher of Attack/Ability Power as damage. Cooldown: 4 seconds.", new[]{AzurakChannel("RoarDouble",18)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarDouble",anchor:BossAnchor.Hit,cooldownMillis:4000,intervalMillis:150,count:2,maxInstances:1,radiusMilli:2500)),
            AzurakProfile("armor", "敵の攻撃でHPに被ダメージを受けたとき、2秒間、攻撃力・魔力の高い方の10%分の障壁を獲得する（重複不可）。再使用まで6秒。", "When an enemy attack damages your HP, gain a shield equal to 10% of your higher of Attack/Ability Power for 2 seconds. This shield does not stack. Cooldown: 6 seconds.", new[]{AzurakChannel("RoarRampart",10,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Defense,BossPayload.Shield,"RoarRampart",cooldownMillis:6000,lifetimeMillis:2000,maxTargets:1,maxInstances:1)),
            AzurakProfile("charm", "通常攻撃命中時、自身の周囲半径3mに咆哮を放ち、攻撃力・魔力の高い方の14%のダメージを与えて敵を0.8m押し出す（ボスと行動妨害が効かない敵は移動せず、地形を越えて移動しない）。再使用まで6秒。", "When your basic attack hits, release a roar in a 3 m radius around you, dealing 14% of your higher of Attack/Ability Power as damage and pushing enemies outward by 0.8 m. Bosses and enemies immune to crowd control are not moved; enemies cannot be pushed through terrain. Cooldown: 6 seconds.", new[]{AzurakChannel("RoarCall",14)},
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Push,"RoarCall",cooldownMillis:6000,maxInstances:1,radiusMilli:3000,magnitudeMilli:800)),
            AzurakProfile("head", "記憶を使ったとき、発動地点に1秒間、半径2mの安全域を展開する。0.5秒後に自身が域内に留まっている場合、1.5秒間、攻撃力・魔力の高い方の8%分の障壁を1回獲得する。再使用まで6秒。", "When you use a memory, create a safety zone with a 2 m radius at your casting location for 1 second. If you are still inside after 0.5 seconds, gain a shield once, equal to 8% of your higher of Attack/Ability Power, for 1.5 seconds. Cooldown: 6 seconds.", new[]{AzurakChannel("RoarHaven",8,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Shield,"RoarHaven",cooldownMillis:6000,delayMillis:500,lifetimeMillis:1000,maxInstances:1,maxTargets:1,radiusMilli:2000),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"RoarHaven",lifetimeMillis:1500,maxTargets:1)),
            AzurakProfile("hands", "通常攻撃の命中地点（自身から最大8m）に0.6秒の予告後、半径1.5mに攻撃力・魔力の高い方の18%の爆発を1回発生させる。再使用まで5秒。", "When your basic attack hits, mark the impact point up to 8 m from you. After a 0.6-second warning, create one explosion with a 1.5 m radius, dealing 18% of your higher of Attack/Ability Power as damage. Cooldown: 5 seconds.", new[]{AzurakChannel("RoarArtillery",18)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarArtillery",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:600,maxInstances:1,radiusMilli:1500,rangeMilli:8000)),
            AzurakProfile("feet", "回避や移動スキルの完了地点に、半径2m、攻撃力・魔力の高い方の16%の着地踏みつけを発生させる。再使用まで5秒。", "When your dodge or movement skill ends, create a landing stomp at your destination with a 2 m radius, dealing 16% of your higher of Attack/Ability Power as damage. Cooldown: 5 seconds.", new[]{AzurakChannel("RoarLanding",16)},
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarLanding",anchor:BossAnchor.Destination,cooldownMillis:5000,maxInstances:1,radiusMilli:2000)),
            AzurakProfile("stage2", "通常攻撃が3回命中するごとに（カウントは最大3回まで蓄積、最後の命中から4秒で消失）、3回目の命中地点（自身から最大8m）に半径2.5m、攻撃力・魔力の高い方の16%の地鳴りを発生させる。再使用まで3秒（再使用時間中も3回目の命中カウントは消費される）。", "Every third time your basic attack hits, create a quake at that impact point up to 8 m from you, with a 2.5 m radius, dealing 16% of your higher of Attack/Ability Power as damage. The counter holds up to 3 attacks and expires 4 seconds after the last successful attack. Cooldown: 3 seconds. The third attack consumes the count even during the cooldown.", new[]{AzurakChannel("RoarQuake",16)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarQuake",anchor:BossAnchor.Hit,cooldownMillis:3000,mainHits:3,counterLifetimeMillis:4000,maxInstances:1,radiusMilli:2500,rangeMilli:8000)),
            AzurakProfile("stage3", "通常攻撃3回目の命中地点（2セット効果の地鳴りの発生有無や再使用時間とは独立）に半径2.5mの予告を表示し、0.45秒後に攻撃力・魔力の高い方の22%の追加踏みつけを与える。再使用まで5秒（再使用時間中も3回目のカウントで消費される）。", "Every third time your basic attack hits, show a warning with a 2.5 m radius at the impact point, then deliver an additional stomp after 0.45 seconds, dealing 22% of your higher of Attack/Ability Power as damage. This works independently of whether the 2-piece quake activates or is on cooldown. Cooldown: 5 seconds. The third attack consumes the count even during the cooldown.", new[]{AzurakChannel("RoarRestomp",22)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarRestomp",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:450,maxInstances:1,radiusMilli:2500,rangeMilli:8000)),
            AzurakProfile("stage6", "通常攻撃3回目の命中地点（2・3セット効果の発生有無や再使用時間とは独立）に0.35秒の予告後、0.20秒間隔で2回、攻撃力・魔力の高い方の45%（合計90%、上限100%）の打撃を与え、さらに3秒間、攻撃力・魔力の高い方の38%分（上限40%）の障壁を獲得する（障壁は重複不可）。再使用まで8秒（再使用時間中も3回目のカウントで消費される）。", "Every third time your basic attack hits, show a 0.35-second warning at the impact point, then strike twice, 0.20 seconds apart. Each strike deals 45% of your higher of Attack/Ability Power as damage (90% total, capped at 100%). Also gain a non-stacking shield equal to 38% of your higher of Attack/Ability Power (capped at 40%) for 3 seconds. This works independently of whether the 2- and 3-piece effects activate or are on cooldown. Cooldown: 8 seconds. The third attack consumes the count even during the cooldown.", new[]{AzurakChannel("RoarGrandDouble",90,100),AzurakChannel("RoarGrandShield",38,40,BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarGrandDouble",anchor:BossAnchor.Hit,cooldownMillis:8000,delayMillis:350,intervalMillis:200,count:2,maxInstances:1,radiusMilli:3000,rangeMilli:8000),
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Shield,"RoarGrandShield",lifetimeMillis:3000,maxTargets:1))
        };
        internal static BossRewardProfile[] CreateAzurakRewards()
        {
            BossRewardStage Stage(int stage)
            {
                var actions = new List<BossRewardAction>{new BossRewardAction(BossRewardActionKind.NativeState,150,150,durationMillis:1000)};
                if(stage >= 2) actions.Add(new BossRewardAction(BossRewardActionKind.NativeDamage,stage == 3 ? 250 : 200,stage == 3 ? 250 : 200,cooldownMillis:8000,budgetMilli:stage == 3 ? 450 : 200,order:1));
                if(stage == 3) actions.Add(new BossRewardAction(BossRewardActionKind.NativeState,50,50,order:2));
                return new BossRewardStage(stage,new Txt(
                    "自身の潜行（Burrow）出現時のみ：ボス以外の行動妨害が効く敵に対する元のスタン時間が1秒未満の場合、最大0.15秒（上限1秒まで）延長する。"
                    +(stage >= 2 ? $"敵への元ダメージの{(stage == 3 ? 25 : 20)}%分を加算する（全対象合計で攻撃力・魔力の高い方の{(stage == 3 ? 45 : 20)}%が上限。再使用まで8秒、1回の出現の全対象で共有）。" : "")
                    +(stage == 3 ? "自身の硬直時間を0.05秒短縮する（下限0秒）。" : "")
                    +"元の範囲、ダメージ、スタン、会心、無敵時間等は維持される。",
                    "Only when you emerge from Burrow: extend the original stun on enemies other than bosses and enemies immune to crowd control by up to 0.15 seconds if it lasts less than 1 second, without exceeding 1 second."
                    +(stage >= 2 ? $" Add {(stage == 3 ? 25 : 20)}% of the original damage dealt to each enemy, with a total limit of {(stage == 3 ? 45 : 20)}% of your higher of Attack/Ability Power across all targets. Cooldown: 8 seconds, shared by all targets of one emergence." : "")
                    +(stage == 3 ? " Shorten your recovery time by 0.05 seconds, to a minimum of 0 seconds." : "")
                    +" The original range, damage, stun, critical strikes, invulnerability duration and other effects are preserved."),actions);
            }
            return new[]{new BossRewardProfile(AzurakRewardId,AzurakSetId,"St_U_Burrow",BossRewardAdapter.Burrow,new[]{Stage(1),Stage(2),Stage(3)})};
        }
        public static UniqueDef[] CreateAzurakPieces() => new[]
        {
            new UniqueDef("unique.boss_azurak.weapon","weapon.oath_mace",new Txt("轟召の戦棍","Roarcall Mace"),AzurakSetId,"boss_azurak.weapon"),
            new UniqueDef("unique.boss_azurak.armor","armor.citadel_plate",new Txt("砲塁の鎧","Artillery Rampart"),AzurakSetId,"boss_azurak.armor"),
            new UniqueDef("unique.boss_azurak.charm","charm.war_horn",new Txt("呼び声の角笛","Calling Horn"),AzurakSetId,"boss_azurak.charm"),
            new UniqueDef("unique.boss_azurak.head","head.siege_helm",new Txt("召集の兜","Muster Helm"),AzurakSetId,"boss_azurak.head"),
            new UniqueDef("unique.boss_azurak.hands","hands.stone_fists",new Txt("地鳴りの拳","Earthrumble Fists"),AzurakSetId,"boss_azurak.hands"),
            new UniqueDef("unique.boss_azurak.feet","feet.guard_sabatons",new Txt("転輪の鉄靴","Rolling Sabatons"),AzurakSetId,"boss_azurak.feet")
        };
        public static SetDef[] CreateAzurakSets() => new[]
        {
            new SetDef{Id=AzurakSetId,Name=new Txt("轟召の重装","Roarcall Heavy Gear"),BossTypeName="Mon_Despair_BossAzurak",BossReward=AzurakRewardId,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[]{new BossSetStage(2,"boss_azurak.stage2"),new BossSetStage(3,"boss_azurak.stage3"),new BossSetStage(6,"boss_azurak.stage6")},
                LinkStages=new[]{new SetLinkStage(2,new LinkDef{Kind=LinkKind.BossReward,Value=1,Requires=new[]{"St_U_Burrow"}}),new SetLinkStage(4,new LinkDef{Kind=LinkKind.BossReward,Value=2,Requires=new[]{"St_U_Burrow"}}),new SetLinkStage(6,new LinkDef{Kind=LinkKind.BossReward,Value=3,Requires=new[]{"St_U_Burrow"}})}}
        };
    }
}

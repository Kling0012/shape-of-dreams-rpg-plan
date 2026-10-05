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
            AzurakProfile("weapon", "通常攻撃の命中地点に、0.15秒間隔で2回の踏みつけ（半径2.5m、各攻撃力・魔力の高い方の9%）を発生させる。再使用まで4秒。", "Main hit: two fixed2.5m stomps at the hit point,9%H each,0.15s apart. CD4s, one location, exactly two hits.", new[]{AzurakChannel("RoarDouble",18)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarDouble",anchor:BossAnchor.Hit,cooldownMillis:4000,intervalMillis:150,count:2,maxInstances:1,radiusMilli:2500)),
            AzurakProfile("armor", "敵の攻撃でHPに被ダメージを受けたとき、2秒間、攻撃力・魔力の高い方の10%分の障壁を獲得する（重複不可）。再使用まで6秒。", "Hostile native actual HP damage: owner shield10%H/2s. CD6s, non-stacking, no immunity block.", new[]{AzurakChannel("RoarRampart",10,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Defense,BossPayload.Shield,"RoarRampart",cooldownMillis:6000,lifetimeMillis:2000,maxTargets:1,maxInstances:1)),
            AzurakProfile("charm", "通常攻撃命中時、自身の周囲半径3mに咆哮を放ち、攻撃力・魔力の高い方の14%のダメージを与えて敵を0.8m押し出す（ボスと行動妨害が効かない敵は移動せず、地形を越えて移動しない）。再使用まで6秒。", "Main hit: owner-centered3m roar for14%H and0.8m push. CD6s; bosses/CC immunity do not move, no terrain crossing.", new[]{AzurakChannel("RoarCall",14)},
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Push,"RoarCall",cooldownMillis:6000,maxInstances:1,radiusMilli:3000,magnitudeMilli:800)),
            AzurakProfile("head", "記憶を使ったとき、発動地点に1秒間、半径2mの安全域を展開する。0.5秒後に自身が域内に留まっている場合、1.5秒間、攻撃力・魔力の高い方の8%分の障壁を1回獲得する。再使用まで6秒。", "Confirmed memory use: fixed2m safety zone at the start point for1s. At0.5s, shield the owner once for8%H/1.5s only if still inside. CD6s, one zone, no following.", new[]{AzurakChannel("RoarHaven",8,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Shield,"RoarHaven",cooldownMillis:6000,delayMillis:500,lifetimeMillis:1000,maxInstances:1,maxTargets:1,radiusMilli:2000),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"RoarHaven",lifetimeMillis:1500,maxTargets:1)),
            AzurakProfile("hands", "通常攻撃の命中地点（自身から最大8m）に0.6秒の予告後、半径1.5mに攻撃力・魔力の高い方の18%の爆発を1回発生させる。再使用まで5秒。", "Main hit: freeze the hit point within8m, warn a1.5m circle for0.6s, then one18%H explosion. CD5s, no tracking.", new[]{AzurakChannel("RoarArtillery",18)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarArtillery",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:600,maxInstances:1,radiusMilli:1500,rangeMilli:8000)),
            AzurakProfile("feet", "回避や移動スキルの完了地点に、半径2m、攻撃力・魔力の高い方の16%の着地踏みつけを発生させる。再使用まで5秒。", "Native owner dodge/movement completion: a2m landing stomp for16%H. CD5s, no extra dash, invulnerability or repeated roll.", new[]{AzurakChannel("RoarLanding",16)},
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarLanding",anchor:BossAnchor.Destination,cooldownMillis:5000,maxInstances:1,radiusMilli:2000)),
            AzurakProfile("stage2", "通常攻撃が3回命中するごとに（カウントは最大3回まで蓄積、最後の命中から4秒で消失）、3回目の命中地点（自身から最大8m）に半径2.5m、攻撃力・魔力の高い方の16%の地鳴りを発生させる。再使用まで3秒（再使用時間中も3回目の命中カウントは消費される）。", "Independently count3 native main hits(max3, expire4s after last success); on that third hit, a2.5m16%H quake at its frozen hit point within8m. CD3s. Consume each third even during CD; any two pieces suffice.", new[]{AzurakChannel("RoarQuake",16)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarQuake",anchor:BossAnchor.Hit,cooldownMillis:3000,mainHits:3,counterLifetimeMillis:4000,maxInstances:1,radiusMilli:2500,rangeMilli:8000)),
            AzurakProfile("stage3", "通常攻撃3回目の命中地点（2セット効果の地鳴りの発生有無や再使用時間とは独立）に半径2.5mの予告を表示し、0.45秒後に攻撃力・魔力の高い方の22%の追加踏みつけを与える。再使用まで5秒（再使用時間中も3回目のカウントで消費される）。", "On the same third hit as2-piece: warn the same frozen2.5m point, then22%H restomp after0.45s. CD5s, one point; any three pieces suffice, independent of2-piece CD.", new[]{AzurakChannel("RoarRestomp",22)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"RoarRestomp",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:450,maxInstances:1,radiusMilli:2500,rangeMilli:8000)),
            AzurakProfile("stage6", "通常攻撃3回目の命中地点（2・3セット効果の発生有無や再使用時間とは独立）に0.35秒の予告後、0.20秒間隔で2回、攻撃力・魔力の高い方の45%（合計90%、上限100%）の打撃を与え、さらに3秒間、攻撃力・魔力の高い方の38%分（上限40%）の障壁を獲得する（障壁は重複不可）。再使用まで8秒（再使用時間中も3回目のカウントで消費される）。", "On the same third hit: warn one fixed3m point for0.35s, then45%H twice0.20s apart(total90/cap100), plus owner shield38%H/3s(cap40). CD8s, one location, non-stacking shield. Independent of2/3-piece CDs; no summon, companion or summon death required.", new[]{AzurakChannel("RoarGrandDouble",90,100),AzurakChannel("RoarGrandShield",38,40,BossCoefficientKind.Shield)},
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
                return new BossRewardStage(stage,new Txt("自身の潜行（Burrow）出現時のみ：ボス以外の行動妨害が効く敵に対する元のスタン時間が1秒未満の場合、最大0.15秒（上限1秒まで）延長する。〔段階2以上：敵への元ダメージの20%（段階3：25%）分を加算（全対象合計で攻撃力・魔力の高い方の20%、段階3では45%が上限。再使用まで8秒、1回の出現の全対象で共有）。〕〔段階3：自身の硬直時間を0.05秒短縮（下限0秒）。〕元の範囲、ダメージ、スタン、会心、無敵時間等は維持される。", "Only your actual native Burrow emerge: add min(0.15,max(0,1-native seconds)) to native stun for hostile non-boss, non-CC-immune targets."+(stage >= 2 ? " Add "+(stage == 3 ? "25%, total cap45%H" : "20%, total cap20%H")+" per activation across all targets, owner CD8s; one shared budget for the entire emerge." : "")+(stage == 3 ? " Shorten owner native daze by0.05s, floor0; replaces4-piece addition." : "")+" Preserve native range, original damage/stun/crit/attackEffect/invulnerability duration/reuse config; exclude generated effects."),actions);
            }
            return new[]{new BossRewardProfile(AzurakRewardId,AzurakSetId,"St_U_Burrow",BossRewardAdapter.Burrow,new[]{Stage(1),Stage(2),Stage(3)})};
        }
        public static UniqueDef[] CreateAzurakPieces() => new[]
        {
            new UniqueDef("unique.boss_azurak.weapon","weapon.oath_mace",new Txt("轟召の戦棍（案: 咆号の戦棍）","Roarcall Mace"),AzurakSetId,"boss_azurak.weapon"),
            new UniqueDef("unique.boss_azurak.armor","armor.citadel_plate",new Txt("砲塁の鎧（案: 城塞の重甲）","Artillery Rampart"),AzurakSetId,"boss_azurak.armor"),
            new UniqueDef("unique.boss_azurak.charm","charm.war_horn",new Txt("呼び声の角笛（案: 喚起の角笛）","Calling Horn"),AzurakSetId,"boss_azurak.charm"),
            new UniqueDef("unique.boss_azurak.head","head.siege_helm",new Txt("召集の兜（案: 号令の兜）","Muster Helm"),AzurakSetId,"boss_azurak.head"),
            new UniqueDef("unique.boss_azurak.hands","hands.stone_fists",new Txt("地鳴りの拳（案: 鳴動の篭手）","Earthrumble Fists"),AzurakSetId,"boss_azurak.hands"),
            new UniqueDef("unique.boss_azurak.feet","feet.guard_sabatons",new Txt("転輪の鉄靴（案: 轟輪の鉄靴）","Rolling Sabatons"),AzurakSetId,"boss_azurak.feet")
        };
        public static SetDef[] CreateAzurakSets() => new[]
        {
            new SetDef{Id=AzurakSetId,Name=new Txt("轟召の重装（案: 咆号の重装）","Roarcall Heavy Gear"),BossTypeName="Mon_Despair_BossAzurak",BossReward=AzurakRewardId,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[]{new BossSetStage(2,"boss_azurak.stage2"),new BossSetStage(3,"boss_azurak.stage3"),new BossSetStage(6,"boss_azurak.stage6")},
                LinkStages=new[]{new SetLinkStage(2,new LinkDef{Kind=LinkKind.BossReward,Value=1,Requires=new[]{"St_U_Burrow"}}),new SetLinkStage(4,new LinkDef{Kind=LinkKind.BossReward,Value=2,Requires=new[]{"St_U_Burrow"}}),new SetLinkStage(6,new LinkDef{Kind=LinkKind.BossReward,Value=3,Requires=new[]{"St_U_Burrow"}})}}
        };
    }
}

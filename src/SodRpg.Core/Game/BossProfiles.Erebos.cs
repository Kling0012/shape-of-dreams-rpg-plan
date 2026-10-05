using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string ErebosSetId = "set.boss_erebos";
        public const string ErebosRewardId = "boss_erebos.last_starlight";
        public const string ErebosEventOrder = "expire-and-reconcile-total2-oldest-replaced-tokens;one-native-input;pending-E3-once-push-to-pull;consume-old-marker-before-parts;capture-consume-boundary3-before-E2-parts;E2-stage2-marker;equipped-parts;stage6-reservation-last-survives-same-input-oldest-replacement;stage6-fixed-center-orthogonal-centered-lines-telegraph-at2-hit2.5-ripple3;prepare-E3-angle-once;native-prepare-relocate-once-from-original-center";
        public const string ErebosNativeContract = "exact-OnCreateSequenced-prehook-gem-skill-owner-room-run-unique-lives;cached-gem-survives-castcomplete-parent-rebind;original-iterator-two-native-waits-adapted-to-live-deadline-predicates;independent-delay-radius-duration-own-deltas;prepare-delay-minus-min0.2-max0-native-minus0.25;attract-plus-min1-max0-12-minus-base;tick-plus-min0.5-max0-8-minus-base;duration-plus-min1-max0-6-minus-base;restore-wait-deadline-not-just-fields;prepare-once-valid-E3-original-center-to-end-max2m-Network_info-position-FX-sync;active-fixed;native-invalid-gem-skill-selfdestroy-unchanged;host-only-no-generated-origin;pool-disable-own-delta-restore";
        private static BossMoveProfile ErebosProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_erebos." + suffix, ErebosSetId, new Txt(ja, en), channels, actions);
        private static BossAction ErebosCircle(BossEvent trigger, string channel, int radius, int delay = 0, int cooldown = 0, int lifetime = 0)
            => new BossAction(trigger, BossMechanism.ShapeAttack, BossPayload.Damage, channel, cooldownMillis:cooldown, delayMillis:delay,
                lifetimeMillis:lifetime, maxInstances:2, radiusMilli:radius, element:BossElement.Light, replaceOldest:true);
        internal static BossMoveProfile[] CreateErebosMoves() => new[]
        {
            ErebosProfile("weapon", "主通常攻撃の照準方向へ固定6m/幅0.6m線。0.3秒予告後、0.3秒間隔2回各8%H Light、敵最大2命中、CD5秒。", "Native main basic hit freezes an aim line6m long,0.6m wide. After0.3s telegraph, two8%H Light hits0.3s apart; max2 hits per enemy,CD5s.",
                new[] { C("ErebosGaze",8,8) }, new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ErebosGaze",BossShape.Line,cooldownMillis:5000,delayMillis:300,intervalMillis:300,count:2,maxInstances:2,rangeMilli:6000,widthMilli:600,element:BossElement.Light,replaceOldest:true)),
            ErebosProfile("armor", "native被damage時の地点を半径2mで0.5秒予告、12%H Light着地円＋中心から0.5m以下/0.2秒押出し。boss/CC免疫移動なし、CD8秒。", "Native damage taken fixes your location: radius2m telegraph0.5s,12%H Light landing plus outward push<=0.5m over0.2s. No boss/CC-immune movement,CD8s.",
                new[] { C("ErebosLanding",12,12) }, ErebosCircle(BossEvent.NativeDamageTaken,"ErebosLanding",2000,500,8000)),
            ErebosProfile("charm", "本人native移動完了の終点で半径2.5mを0.3秒予告、10%H Light＋最大3敵を外へ0.6m以下/0.3秒移動。boss/CC免疫移動なし、CD6秒。", "Your native movement landing: radius2.5m telegraph0.3s,10%H Light plus push up to3 enemies outward<=0.6m over0.3s. No boss/CC-immune movement,CD6s.",
                new[] { C("ErebosWhitehole",10,10) }, ErebosCircle(BossEvent.MovementCompleted,"ErebosWhitehole",2500,300,6000)),
            ErebosProfile("head", "記憶使用確定で照準8m以内の半径1.8mを0.7秒予告、18%H Fire着弾。HP比例/浮遊stunなし、CD7秒。", "Confirmed native memory use: legal aim within8m,radius1.8m telegraph0.7s,18%H Fire impact. No percent-health damage or float stun,CD7s.",
                new[] { C("ErebosMeteor",18,18) }, new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"ErebosMeteor",anchor:BossAnchor.Cursor,cooldownMillis:7000,delayMillis:700,maxInstances:2,radiusMilli:1800,rangeMilli:8000,element:BossElement.Fire,replaceOldest:true)),
            ErebosProfile("hands", "主通常攻撃の敵確定位置8m以内へ2発を0.2秒差で射出。速度12m/秒・寿命1秒、到着予告円半径1mで各8%H Light、敵最大2命中、CD6秒。飛行damage/壁爆発なし。", "Native main basic hit fixes the enemy point within8m: two shots0.2s apart,12m/s,lifetime1s,telegraphed radius1m arrival explosions8%H Light each,max2 hits per enemy,CD6s. No flight damage or wall explosion.",
                new[] { C("ErebosStarRain",8,8) }, new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"ErebosStarRain",anchor:BossAnchor.Hit,cooldownMillis:6000,lifetimeMillis:1000,intervalMillis:200,count:2,maxInstances:2,radiusMilli:1000,rangeMilli:8000,widthMilli:200,speedMilli:12000,element:BossElement.Light,replaceOldest:true)),
            ErebosProfile("feet", "本人native移動終点から半径1/2/3m円を0.2/0.4/0.6秒後に起動、各4/5/6%H Light。敵最大3命中、CD6秒。", "Your native movement landing starts radius1/2/3m circles after0.2/0.4/0.6s,4/5/6%H Light respectively,max3 hits per enemy,CD6s.",
                new[] { C("ErebosRippleFirst",4,4),C("ErebosRippleSecond",5,5),C("ErebosRippleThird",6,6) }, ErebosCircle(BossEvent.MovementCompleted,"ErebosRippleFirst",1000,200,6000), ErebosCircle(BossEvent.Clock,"ErebosRippleSecond",2000,400), ErebosCircle(BossEvent.Clock,"ErebosRippleThird",3000,600)),
            ErebosProfile("stage2", "記憶使用確定で照準8m以内に中心印1個/4秒。次主通常攻撃で印消費、半径2m/10%H→0.3秒後半径3m/14%H Light波紋、CD6秒。全場本人合計2・最古置換。", "Confirmed memory use places one center marker within8m for4s. Next native main basic hit consumes it: radius2m10%H then radius3m14%H Light after0.3s,CD6s. All owned fields total2,oldest replaced.",
                new[] { C("ErebosRippleStart",10,10),C("ErebosRippleEnd",14,14) }, new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Mark,lifetimeMillis:4000,maxInstances:1,radiusMilli:3000,rangeMilli:8000,replaceOldest:true,element:BossElement.Light), ErebosCircle(BossEvent.MainHit,"ErebosRippleStart",2000,0,6000), ErebosCircle(BossEvent.Clock,"ErebosRippleEnd",3000,300)),
            ErebosProfile("stage3", "印待機中の本人native移動完了1回で排→吸に反転。波紋直前、半径3mの最大3敵を外/内へ0.8m以下/0.3秒移動。印消費で境界印+1、最大3/12秒、boss/CC免疫移動なし。", "One native movement completion while a marker waits changes push to pull. Immediately before ripple,move up to3 enemies in radius3m outward/inward<=0.8m over0.3s. Marker consumption grants1 boundary mark,max3 for12s; no boss/CC-immune movement.",
                new[] { C("ErebosPolarity",80,80,BossCoefficientKind.Distance) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mode,count:1,maxInstances:1,lifetimeMillis:4000,radiusMilli:3000,magnitudeMilli:800,element:BossElement.Light), new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mark,count:3,maxInstances:1,lifetimeMillis:12000,element:BossElement.Light)),
            ErebosProfile("stage6", "波紋3回の境界印3を次記憶使用確定で全消費、照準8m以内へ半径4m場/3秒＋本人shield38%H/3秒(cap40)。2秒時に中心直交6m線2本/幅0.6mを0.5秒予告→各22.5%H Light、3秒時に中心45%H Light円。準備中native移動完了で角度1回変更、CD10秒。A90/cap100+B38/cap40=1.85、移動不要。", "After3 ripple activations,next confirmed memory use consumes all3 boundary marks: radius4m field within8m for3s and self shield38%H for3s(cap40). At2s telegraph two centered orthogonal6m lines,width0.6m for0.5s,then22.5%H Light each; at3s radius4m center ripple45%H Light. One native movement completion during preparation changes line angle once,CD10s. A90/cap100+B38/cap40=1.85; movement optional.",
                new[] { C("ErebosFinale",90,100), C("ErebosFinaleShield",38,40,BossCoefficientKind.Shield) }, ErebosCircle(BossEvent.MemoryUse,"ErebosFinale",4000,3000,10000,3000), new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"ErebosFinale",BossShape.Line,delayMillis:2500,maxInstances:2,count:2,rangeMilli:6000,widthMilli:600,element:BossElement.Light), new BossAction(BossEvent.MemoryUse,BossMechanism.Defense,BossPayload.Shield,"ErebosFinaleShield",lifetimeMillis:3000,maxInstances:1,element:BossElement.Light)),
        };
        internal static BossRewardProfile[] CreateErebosRewards()
        {
            BossRewardAction Delay() => new BossRewardAction(BossRewardActionKind.NativeInterval,200,200,durationMillis:250,order:0);
            BossRewardAction Attract() => new BossRewardAction(BossRewardActionKind.NativeState,1000,1000,rangeMilli:12000,order:1);
            BossRewardAction Tick() => new BossRewardAction(BossRewardActionKind.NativeState,500,500,rangeMilli:8000,order:2);
            BossRewardAction Relocate() => new BossRewardAction(BossRewardActionKind.NativeTarget,2000,2000,count:1,rangeMilli:2000,order:3);
            BossRewardAction Duration() => new BossRewardAction(BossRewardActionKind.NativeInterval,1000,1000,durationMillis:6000,order:4);
            return new[] { new BossRewardProfile(ErebosRewardId,ErebosSetId,"Gem_U_LastStarlight",BossRewardAdapter.LastStarlight,new[]
            {
                new BossRewardStage(1,new Txt("本人の実LastStarlight prepare delayをd−min(0.20,max(0,d−0.25))秒へ。元の0.25秒未満は不変、解除時native待機deadlineも復元。", "Your real LastStarlight prepare delay becomes d−min(0.20,max(0,d−0.25))s. Values below0.25s unchanged; unequip restores the actual native wait deadline too."),new[] { Delay() }),
                new BossRewardStage(2,new Txt("2点＋native吸引半径r+min(1,max(0,12−r))m、tick円r+min(0.5,max(0,8−r))m。独立に拡張、元のcap超過値を下げない。", "2-piece plus native attraction radius r+min(1,max(0,12−r))m and tick radius r+min(0.5,max(0,8−r))m. Expand independently; never reduce native values above caps."),new[] { Delay(),Attract(),Tick() }),
                new BossRewardStage(3,new Txt("4点＋prepare中の本人native移動完了1回で元中心から終点方向へ最大2m合法地点に再照準、active後固定。native持続d+min(1,max(0,6−d))秒、解除時追加寿命を実待機deadlineから撤回。gem/skill失効の本体自滅を保持。", "4-piece plus one native movement completion during prepare retargets from the ORIGINAL center toward the landing by<=2m to legal ground; fixed once active. Native duration d+min(1,max(0,6−d))s; unequip retracts only extra lifetime from the actual wait deadline. Preserve native invalid-gem/skill self-destruction."),new[] { Delay(),Attract(),Tick(),Relocate(),Duration() }),
            }) };
        }
        public static UniqueDef[] CreateErebosPieces() => new[]
        {
            new UniqueDef("unique.boss_erebos.weapon","weapon.star_harp",new Txt("終星の波琴","Laststar Waveharp"),ErebosSetId,"boss_erebos.weapon"),
            new UniqueDef("unique.boss_erebos.armor","armor.star_mantle",new Txt("重力なき肩衣","Weightless Mantle"),ErebosSetId,"boss_erebos.armor"),
            new UniqueDef("unique.boss_erebos.charm","charm.dream_lens",new Txt("残照の星核","Afterglow Starcore"),ErebosSetId,"boss_erebos.charm"),
            new UniqueDef("unique.boss_erebos.head","head.ember_crown",new Txt("隕光の冠","Meteorlight Crown"),ErebosSetId,"boss_erebos.head"),
            new UniqueDef("unique.boss_erebos.hands","hands.star_rings",new Txt("星雨の指環","Starshower Fingerbands"),ErebosSetId,"boss_erebos.hands"),
            new UniqueDef("unique.boss_erebos.feet","feet.star_steps",new Txt("波紋の履","Ripple Shoes"),ErebosSetId,"boss_erebos.feet"),
        };
        public static SetDef[] CreateErebosSets() => new[]
        {
            new SetDef { Id=ErebosSetId,Name=new Txt("終星の流衣","Laststar Vesture"),BossTypeName="Mon_Special_BossErebos",BossReward=ErebosRewardId,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_erebos.stage2"),new BossSetStage(3,"boss_erebos.stage3"),new BossSetStage(6,"boss_erebos.stage6") },
                LinkStages=new[] { new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward,Requires=new[] { "Gem_U_LastStarlight" },Value=1 }),new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward,Requires=new[] { "Gem_U_LastStarlight" },Value=2 }),new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward,Requires=new[] { "Gem_U_LastStarlight" },Value=3 }) } },
        };
    }
}

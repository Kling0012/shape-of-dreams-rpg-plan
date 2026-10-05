using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string NyxSetId = "set.boss_nyx";
        public const string NyxRewardId = "boss_nyx.her_world";
        public const string NyxNativeContract = "own-equipped-HerWorld-firstTrigger-caster-victim-parent-and-pooled-ActorLife;native-tick-circle-only-additional-attract-1mps-total2m-per-target-per-status;preserve-native-curve-and-outer-attract;radius-owned-delta-r+min(1,max(0,8-r));exact-native-tick-dispatch-positive-success-first3-unique-lives;natural-StatusEffect-FrameUpdate-expiry-only-not-death-interrupt-or-forced-end;exact-end-created-child-once-token1s;recorded-actual-child-hit-only-0.45H-Light-once;stun-owned-delta-d+min(.25,max(0,3-d));restore-fields-at-end-disable-pool-and-gear-reconciliation;preserve-native-death-interrupt-speed-armor-timer-lock-cost-and-packets;exclude-generated-wrong-owner-stale-room-run-epoch";
        public const string NyxEventOrder = "expire-and-reconcile-total2-oldest-replaced-M3-tokens;E1-consume-existing-starpoint-pull-before-two-pillar-hits-add-one-mark;E3-relocate-existing-marker-once;E2-snapshot-existing-three-marks-before-new-marker;parts-after-marker;reserve-six-channel-last-for-total2-oldest-order;seed-M3-to-three-M2-single-wave-target-hit;generated-dash-actual-path-no-E3";

        private static BossChannelDef NyxChannel(string id, int value, int cap, BossCoefficientKind kind = BossCoefficientKind.Damage)
            => new BossChannelDef(id, value * 1000, cap * 1000, kind);
        private static BossMoveProfile NyxProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_nyx." + suffix, NyxSetId, new Txt(ja, en), channels, actions);
        private static BossAction NyxCircle(BossEvent kind, string channel, int radius, int delay, int cooldown = 0)
            => new BossAction(kind, BossMechanism.Field, BossPayload.Damage, channel, cooldownMillis: cooldown,
                delayMillis: delay, maxInstances: 1, radiusMilli: radius, element: BossElement.Light, replaceOldest: true);

        internal static BossMoveProfile[] CreateNyxMoves() => new[]
        {
            NyxProfile("weapon", "主撃の被害地点（本人から8m以内）へ半径1.5mを0.35秒予告、12%H Light（上限36）＋0.4秒後6%H（上限18）。CD5秒。全場本人計2個、最古置換。", "A native main hit fixes its damage point within8m: warn a1.5m circle for0.35s, then12%H Light(cap36), followed0.4s later by6%H(cap18). CD5s. All owned fields share a2-field limit, replacing the oldest.",
                new[] { NyxChannel("NyxPillar",12,36), NyxChannel("NyxPillarEcho",6,18) },
                NyxCircle(BossEvent.MainHit,"NyxPillar",1500,350,5000), NyxCircle(BossEvent.Clock,"NyxPillarEcho",1500,750)),
            NyxProfile("armor", "native被damage後、本人shield16%H（上限48）/2秒非加算＋足元半径2mを0.4秒予告して8%H Light（上限24）。CD8秒。全場計2、最古置換。", "After native damage taken gain a non-stacking16%H self shield(cap48) for2s; warn your fixed feet point for0.4s then explode for8%H Light(cap24) in2m. CD8s; all fields share limit2, oldest replaced.",
                new[] { NyxChannel("NyxMantle",16,48,BossCoefficientKind.Shield), NyxChannel("NyxMantleBurst",8,24) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Defense,BossPayload.Shield,"NyxMantle",cooldownMillis:8000,lifetimeMillis:2000,maxInstances:1,element:BossElement.Light),
                NyxCircle(BossEvent.Clock,"NyxMantleBurst",2000,400)),
            NyxProfile("charm", "通常/奥義記憶確定で照準合法地面8m以内へ非Entity種1個、0.6秒後120度間隔3弾。距離4m・速度8m/秒・半径0.25m・寿命0.5秒、同敵は3弾合計1hit10%H Light（上限30）。CD6秒、本体召喚procなし。全場計2、最古置換。", "Confirmed normal/ultimate memory use plants one non-Entity seed at a legal cursor point within8m. After0.6s emit3 shots120 degrees apart: range4m, speed8m/s, radius0.25m, lifetime0.5s; at most one10%H Light hit per enemy across all3(cap30). CD6s, no native summon proc. Fields share limit2, oldest replaced.",
                new[] { NyxChannel("NyxSeed",10,30) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Projectile,BossPayload.Damage,"NyxSeed",BossShape.Radial,BossAnchor.Cursor,cooldownMillis:6000,delayMillis:600,lifetimeMillis:500,count:3,maxInstances:1,rangeMilli:4000,widthMilli:500,speedMilli:8000,firstHitOnly:true,replaceOldest:true,element:BossElement.Light)),
            NyxProfile("head", "本人native移動完了で終点前方2mの合法地点を半径1m/0.3秒予告し14%H Light（上限42）。CD5秒。全場計2、最古置換。", "After your native movement completes, warn a legal point2m ahead of the landing for0.3s, then a1m explosion deals14%H Light(cap42). CD5s; all fields share limit2, oldest replaced.",
                new[] { NyxChannel("NyxOrbit",14,42) }, NyxCircle(BossEvent.MovementCompleted,"NyxOrbit",1000,300,5000)),
            NyxProfile("hands", "主撃対象周辺半径2mの通常敵最大3体を本人側へ最大0.6m/0.3秒吸引し、0.4秒後その固定円へ12%H Light（上限36）。boss/CC免疫は移動なし、CD6秒。全場計2、最古置換。", "A native main hit pulls at most3 ordinary enemies within2m of the target toward you by at most0.6m over0.3s; after0.4s the fixed circle deals12%H Light(cap36). Bosses/CC-immune enemies cannot move. CD6s; fields share limit2, oldest replaced.",
                new[] { NyxChannel("NyxBinding",12,36) }, NyxCircle(BossEvent.MainHit,"NyxBinding",2000,400,6000),
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Pull,lifetimeMillis:300,maxTargets:3,radiusMilli:2000,magnitudeMilli:600,element:BossElement.Light)),
            NyxProfile("feet", "通常/奥義記憶確定で照準方向へ本人最大3m/0.2秒、有効地形dash。実移動経路幅0.6mで敵各1hit10%H Light（上限30）、CD7秒。無敵なし、この自動移動はE3なし。", "Confirmed normal/ultimate memory use dashes you at most3m toward the cursor over0.2s on valid terrain. The actual swept path is0.6m wide and deals one10%H Light hit per enemy(cap30), CD7s. No invulnerability; this generated dash cannot emit E3.",
                new[] { NyxChannel("NyxCrossing",10,30) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Movement,BossPayload.Dash,"NyxCrossing",BossShape.Line,BossAnchor.Cursor,cooldownMillis:7000,lifetimeMillis:200,maxInstances:1,rangeMilli:3000,widthMilli:600,element:BossElement.Light)),
            NyxProfile("stage2", "E2照準合法地面8m以内に星点1個/6秒。次E1で消費し半径2mの柱2hit各10%H Light（0.35/0.75秒）、起動CD6秒。任意2部位で成立。星点/種/柱/吸引場は本人計2、最古置換。", "E2 places one starpoint at a legal cursor point within8m for6s. The next E1 consumes it and triggers a2m pillar:10%H Light at0.35s and0.75s, trigger CD6s. Any2 pieces suffice. Starpoints/seeds/pillars/attraction fields share an owned limit2, oldest replaced.",
                new[] { NyxChannel("NyxStarpoint",1,1,BossCoefficientKind.Stat), NyxChannel("NyxStarColumn",10,10) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Mark,"NyxStarpoint",BossShape.Circle,BossAnchor.Cursor,lifetimeMillis:6000,maxInstances:1,radiusMilli:2000,rangeMilli:8000,replaceOldest:true,element:BossElement.Light),
                NyxCircle(BossEvent.MainHit,"NyxStarColumn",2000,350,6000), NyxCircle(BossEvent.Clock,"NyxStarColumn",2000,750)),
            NyxProfile("stage3", "待機星点はE3で1回だけ終点前方2mへ再配置（元の6秒期限を保持）。柱起動に半径2mの通常敵吸引最大0.6m/0.3秒を先行追加。星点消費で星印+1、最大3・最終進行から12秒。boss/CC免疫は移動なし、E3不要で印を獲得。", "A waiting starpoint can relocate once on E3 to a legal point2m ahead of landing, retaining its original6s expiry. Pillar activation first pulls ordinary enemies within2m by at most0.6m over0.3s. Consuming a starpoint adds one owner star mark(max3,12s since last progress). Bosses/CC-immune targets cannot move; marks require no E3.",
                new[] { NyxChannel("NyxStarMark",1,1,BossCoefficientKind.Stat) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mark,"NyxStarMark",lifetimeMillis:12000,count:3,rangeMilli:2000,ledgerId:"NyxOwnerStars",element:BossElement.Light),
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Pull,lifetimeMillis:300,radiusMilli:2000,magnitudeMilli:600,element:BossElement.Light)),
            NyxProfile("stage6", "柱3回で星印3→次E2で全消費、照準合法地面8m以内に半径3mを0.5秒予告→1秒吸引（通常敵最大3体、2m/秒・敵総移動2mまで）→90%H Light爆発（上限100）＋本人shield38%H（上限40）/3秒非加算。CD10秒、全場計2・最古置換。E3不要、A90/100+B38/40=1.85は定義予算、実測なし。", "After3 pillar activations, the next E2 consumes3 star marks. Warn a legal cursor point within8m with a3m circle for0.5s, then attract at most3 ordinary enemies for1s at2m/s, each moved at most2m total; finish with90%H Light(cap100) and a non-stacking38%H self shield(cap40) for3s. CD10s; fields share limit2, oldest replaced. No E3 required. A90/100+B38/40=1.85 is an unmeasured definition budget.",
                new[] { NyxChannel("NyxStarsea",90,100), NyxChannel("NyxStarseaShield",38,40,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Pull,cooldownMillis:10000,delayMillis:500,lifetimeMillis:1000,maxTargets:3,maxInstances:1,radiusMilli:3000,rangeMilli:8000,speedMilli:2000,magnitudeMilli:2000,replaceOldest:true,requiredMarks:3,consumeMarks:true,element:BossElement.Light),
                NyxCircle(BossEvent.Clock,"NyxStarsea",3000,1500),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"NyxStarseaShield",delayMillis:1500,lifetimeMillis:3000,maxInstances:1,element:BossElement.Light)),
        };

        internal static BossRewardProfile[] CreateNyxRewards()
        {
            BossRewardAction Pull() => new BossRewardAction(BossRewardActionKind.NativeSpeed,count:64,magnitudeMilli:2000,speedMilli:1000);
            BossRewardAction Radius() => new BossRewardAction(BossRewardActionKind.NativeState,rangeMilli:8000,order:1,magnitudeMilli:1000);
            BossRewardAction End() => new BossRewardAction(BossRewardActionKind.NativeDamage,450,450,durationMillis:1000,count:3,order:2,magnitudeMilli:250,rangeMilli:3000);
            return new[] { new BossRewardProfile(NyxRewardId,NyxSetId,"St_U_HerWorld",BossRewardAdapter.HerWorld,new[]
            {
                new BossRewardStage(1,new Txt("本人装着Her Worldの実native状態だけ。tick円内の通常敵へ追加吸引1m/秒、1状態・1敵の追加移動2mまで。boss/CC免疫除外、元curve/外側吸引/全能力lock/death interrupt/期間不変。", "Only your equipped Her World's real native status adds1m/s attraction inside its tick circle, at most2m additional movement per enemy per status. Exclude bosses/CC immunity; preserve native curve, outer attraction, all ability locks, death interrupt and duration."),new[] { Pull() }),
                new BossRewardStage(2,new Txt("2部位効果＋当該native tickDamageRadiusをr+min(1,max(0,8-r))mへ直接拡張、終了/解除時に自身の差分のみ復元。本体Light DoT/damage/proc不変。", "Include2-piece effect; directly extend that native tickDamageRadius to r+min(1,max(0,8-r))m and restore only your delta at end/removal. Native Light DoT, damage and proc stay unchanged."),new[] { Pull(),Radius() }),
                new BossRewardStage(3,new Txt("4部位効果＋native tick成功敵最大3体を固有世代で記録。同状態の自然終了child実hitで記録敵へ各1回45%H Light追加、終了token1秒以内。native stunをd+min(0.25,max(0,3-d))秒、短縮なし。死亡interrupt/強制解除/epoch/部屋変更では追加なし、生成/他者origin除外。", "Include4-piece effects; record at most3 unique enemy lives successfully damaged by native ticks. Only the same status's natural-end child actual hit adds45%H Light once per recorded enemy, with an end token lasting at most1s. Native stun becomes d+min(0.25,max(0,3-d))s, never shortened. No bonus on death interrupt, forced removal, epoch/room change, generated or another owner's origin."),new[] { Pull(),Radius(),End() }),
            }) };
        }
        public static UniqueDef[] CreateNyxPieces() => new[]
        {
            new UniqueDef("unique.boss_nyx.weapon","weapon.astral_staff",new Txt("星柱の杖","Starpillar Staff"),NyxSetId,"boss_nyx.weapon"),
            new UniqueDef("unique.boss_nyx.armor","armor.star_mantle",new Txt("星海の外套","Starsea Mantle"),NyxSetId,"boss_nyx.armor"),
            new UniqueDef("unique.boss_nyx.charm","charm.pulsing_core",new Txt("夜空の種","Nightseed"),NyxSetId,"boss_nyx.charm"),
            new UniqueDef("unique.boss_nyx.head","head.star_diadem",new Txt("星軌の冠","Starorbit Crown"),NyxSetId,"boss_nyx.head"),
            new UniqueDef("unique.boss_nyx.hands","hands.star_rings",new Txt("星を結ぶ指","Starbinding Fingers"),NyxSetId,"boss_nyx.hands"),
            new UniqueDef("unique.boss_nyx.feet","feet.star_steps",new Txt("星渡りの靴","Starcrossing Shoes"),NyxSetId,"boss_nyx.feet"),
        };
        public static SetDef[] CreateNyxSets() => new[] { new SetDef
        {
            Id=NyxSetId,Name=new Txt("星海の主衣","Starsea Sovereign Raiment"),BossTypeName="Mon_Sky_BossNyx",
            TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
            BossStages=new[] { new BossSetStage(2,"boss_nyx.stage2"),new BossSetStage(3,"boss_nyx.stage3"),new BossSetStage(6,"boss_nyx.stage6") },
            BossReward=NyxRewardId,LinkStages=new[]
            {
                new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward,Value=1,Requires=new[] { "St_U_HerWorld" } }),
                new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward,Value=2,Requires=new[] { "St_U_HerWorld" } }),
                new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward,Value=3,Requires=new[] { "St_U_HerWorld" } }),
            },
        } };
    }
}

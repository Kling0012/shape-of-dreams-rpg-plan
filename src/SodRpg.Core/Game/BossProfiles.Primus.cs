using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string PrimusSetId = "set.boss_primus_aeron";
        public const string PrimusEventOrder = "native-first-main-hit-confirmed-use-completed-owned-movement;parts-independent;mode-gate500ms;departure-glyph-replace;distinct-three-bit-cycle-independent-of-mode-gate-first-input-deadline8s;duplicates-no-progress-no-refresh;complete-cycle-clear-on-success-or-cd-busy;failed-reservation-restore-two-inputs-no-queue-no-cd;ready-finisher-replaces-ordinary-even-shared-cd-busy;ordinary-shared-cd3s-no-queue;glyph-consume-only-emitted-e1-e2-or-finisher;frozen-origin-forward;source-owned-maxhp-shields-linear-decay";
        public const string PrimusNativeContract = "Mon_Primus_BossPrimusAeron;Force-GreatSword-Adapt-Spells-Rage-DoubleSword;native-main-success-attribution-confirmed-active-memory-cast-forward;host-verified-native-displacement-departure-and-landing;generated-excluded;no-native-boss-instantiation-no-hp-threshold-no-new-dash-no-summons-no-reward-no-link;adapt-light-three-distinct-targets-3m-hop150ms-life1500ms;armor8pct-maxhp-decay3s-cd12s;finisher38pct-maxhp-decay6s-cd18s;all-shapes-max8;equipment-memory-run-room-death-disconnect-teardown";

        private static BossMoveProfile PrimusProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_primus_aeron." + suffix, PrimusSetId, new Txt(ja, en), channels, actions);
        private static BossAction PrimusFan(BossEvent trigger, string channel, int cooldown, int range, int angle, BossElement element = BossElement.Neutral, int count = 1)
            => new BossAction(trigger, BossMechanism.ShapeAttack, BossPayload.Damage, channel, BossShape.Fan,
                cooldownMillis: cooldown, intervalMillis: count == 2 ? 250 : 0, count: count, maxTargets: 8, maxInstances: 1,
                rangeMilli: range, angleMilli: angle, element: element);
        private static BossAction PrimusLight(BossEvent trigger, string channel, int cooldown, int maxTargets = 8, int lifetime = 667)
            => new BossAction(trigger, BossMechanism.Projectile, BossPayload.Damage, channel,
                cooldownMillis: cooldown, lifetimeMillis: lifetime, maxTargets: maxTargets, maxInstances: 1,
                rangeMilli: 8000, widthMilli: 400, speedMilli: 12000, hitGateMillis: 150, firstHitOnly: true, element: BossElement.Light);
        private static BossAction PrimusShield(BossEvent trigger, string channel, int cooldown, int lifetime)
            => new BossAction(trigger, BossMechanism.Defense, BossPayload.Shield, channel, cooldownMillis: cooldown,
                lifetimeMillis: lifetime, maxTargets: 1, maxInstances: 1, followOwner: true);

        internal static BossMoveProfile[] CreatePrimusMoves() => new[]
        {
            PrimusProfile("weapon", "本人native主通常攻撃の初成功で前方3m・90°扇に無属性18%H、1敵1hit・最大8敵、CD4秒。", "First successful native main basic hit: forward3m/90° fan,18%H neutral,one hit per enemy,max8 enemies,CD4s.",
                new[] { C("PrimusForceStrike",18,18) }, PrimusFan(BossEvent.MainHit,"PrimusForceStrike",4000,3000,90000)),
            PrimusProfile("armor", "本人の敵対native被damage後に独立盾8%最大HP、3秒で線形減衰・1枚・CD12秒。最大HPは付与時固定、HP再設定/無敵なし。", "After hostile native damage to you: one independent8%MAXHP shield,frozen at award,linearly decays over3s,CD12s. No health reset or invulnerability.",
                new[] { C("PrimusRageShield",8,8,BossCoefficientKind.Shield) }, PrimusShield(BossEvent.NativeDamageTaken,"PrimusRageShield",12000,3000)),
            PrimusProfile("charm", "記憶使用確定の方向へ光弾1本：射程8m・速度12m/秒・幅0.4m。初着弾後3m内の未命中敵へ最大2跳、各8%H・間隔0.15秒、最大3異敵・寿命1.5秒・同時1本・CD7秒。", "Confirmed memory use fires one Light shot in the confirmed direction:range8m,speed12m/s,width0.4m. After first impact,up to2 jumps to unhit enemies within3m,8%H each,0.15s gates,max3 distinct enemies,total life1.5s,max1 shot,CD7s.",
                new[] { C("PrimusAdaptChain",8,8) }, PrimusLight(BossEvent.MemoryUse,"PrimusAdaptChain",7000,3,1500)),
            PrimusProfile("head", "本人native主通常攻撃の初成功で前方3m・120°へ0/0.25秒に闇斬撃各10%H、同敵合計20%H・各回最大8敵・CD5秒。", "First successful native main basic hit: forward3m/120° Dark cuts at0/0.25s,10%H each,total20%H per enemy,max8 enemies per cut,CD5s.",
                new[] { C("PrimusRageBlades",20,20) }, PrimusFan(BossEvent.MainHit,"PrimusRageBlades",5000,3000,120000,BossElement.Dark,2)),
            PrimusProfile("hands", "記憶使用確定の指定点を本人6m内へホスト裁定、0.5秒予告後に半径2mの火18%Hを1回。最大8敵・同時1場・CD8秒、弾幕/stunなし。", "Confirmed memory use: host clamps the specified point to a legal position within6m;0.5s telegraph then one radius2m Fire impact18%H,max8 enemies,max1 field,CD8s. No barrage or stun.",
                new[] { C("PrimusDoomImpact",18,18) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"PrimusDoomImpact",anchor:BossAnchor.Cursor,
                    cooldownMillis:8000,delayMillis:500,maxTargets:8,maxInstances:1,radiusMilli:2000,rangeMilli:6000,element:BossElement.Fire)),
            PrimusProfile("feet", "本人native移動完了の着地点から前方2.5m・120°へ0/0.25秒に冷斬撃各10%H、同敵合計20%H・各回最大8敵・CD7秒。自動dash/追跡Frost/stunなし。", "Your native movement completion: from the verified landing,forward2.5m/120° Cold cuts at0/0.25s,10%H each,total20%H per enemy,max8 enemies per cut,CD7s. No extra dash,tracking Frost or stun.",
                new[] { C("PrimusIceclawCuts",20,20) }, PrimusFan(BossEvent.MovementCompleted,"PrimusIceclawCuts",7000,2500,120000,BossElement.Cold,2)),
            PrimusProfile("stage2", "E1/E2/E3でForce/Adapt/Rageを選択、入力gate0.5秒。共有CD3秒で入力技1つ：Force=3m/90°無属性扇12%H、Adapt=射程8m/速度12/幅0.4光弾12%H、Rage=着地半径2m闇円12%H、各最大8敵。CD中はmode更新のみ、予約なし。", "E1/E2/E3 selects Force/Adapt/Rage with0.5s input gate. Shared3s CD emits one input technique:Force3m/90° neutral fan12%H;Adapt Light shot12%H,range8m,speed12,width0.4;Rage landing radius2m Dark circle12%H,max8 enemies each. Busy CD updates mode only,never queues attacks.",
                new[] { C("PrimusPhaseTechnique",12,12) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:500,count:3,maxTargets:1,maxInstances:1),
                PrimusFan(BossEvent.MainHit,"PrimusPhaseTechnique",3000,3000,90000),
                PrimusLight(BossEvent.MemoryUse,"PrimusPhaseTechnique",3000),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"PrimusPhaseTechnique",cooldownMillis:3000,maxTargets:8,maxInstances:1,radiusMilli:2000,element:BossElement.Dark)),
            PrimusProfile("stage3", "E3のホスト検証済み出発点に非Entity相紋1個・寿命4秒、新E3で上書き。次に実発動するE1/E2相技だけが本人6m内の相紋を消費し、0.35秒予告後そこから発射、本人との二重発射/増damageなし。範囲外は破棄し本人起点。", "E3 leaves one non-Entity glyph at the host-verified departure for4s;new E3 replaces it. Only the next actually emitted E1/E2 phase technique consumes a glyph within6m of you,telegraphs0.35s then fires there instead of at you,without added damage. Out-of-range glyphs are discarded and use your origin.",
                new[] { C("PrimusGlyphReach",6,6,BossCoefficientKind.Distance) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mark,delayMillis:350,lifetimeMillis:4000,maxTargets:1,maxInstances:1,rangeMilli:6000,replaceOldest:true)),
            PrimusProfile("stage6", "8秒内の異なるE1/E2/E3で三相合流、重複は進行/寿命延長なし。CD18秒、成立入力の通常相技を共有CD中でも置換。相紋が本人6m内ならそこ、なければ本人起点、起点/向き固定。0/0.3/0.6秒に無属性4m/90°扇30%H＋光8m/幅0.6線30%H＋闇半径2.5m円30%H、各最大8敵・同敵90%H（A上限100）。同時に盾38%最大HP（B上限40）1枚、6秒線形減衰。完成時3印/相紋消費、CD中完成は破棄。連携/特定記憶/召喚/HP閾値なし。", "Three distinct E1/E2/E3 within8s finish the cycle;duplicates neither progress nor extend it. CD18s,replaces the completing input's ordinary phase even during shared CD. Use a glyph within6m if valid,otherwise your origin;freeze origin/direction. At0/0.3/0.6s:neutral4m/90° fan30%H,Light8m/width0.6 line30%H,Dark radius2.5m circle30%H;max8 enemies each,total90%H per enemy(A cap100). Simultaneously one38%MAXHP shield(B cap40),linearly decaying6s. Completion consumes all3 marks/glyph;completion during finisher CD is discarded. No Link,required memory,summon or HP threshold.",
                new[] { C("PrimusConfluence",90,100), C("PrimusConfluenceShield",38,40,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mark,cooldownMillis:18000,lifetimeMillis:8000,count:3,maxTargets:1,maxInstances:1),
                PrimusFan(BossEvent.Clock,"PrimusConfluence",0,4000,90000),
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"PrimusConfluence",BossShape.Line,delayMillis:300,maxTargets:8,maxInstances:1,rangeMilli:8000,widthMilli:600,element:BossElement.Light),
                new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.Damage,"PrimusConfluence",delayMillis:600,maxTargets:8,maxInstances:1,radiusMilli:2500,element:BossElement.Dark),
                PrimusShield(BossEvent.Clock,"PrimusConfluenceShield",0,6000)),
        };

        public static UniqueDef[] CreatePrimusPieces() => new[]
        {
            new UniqueDef("unique.boss_primus_aeron.weapon","weapon.bulwark_greatsword",new Txt("三相の大剣","Threefold Greatsword"),PrimusSetId,"boss_primus_aeron.weapon"),
            new UniqueDef("unique.boss_primus_aeron.armor","armor.ember_plate",new Txt("適応の胸甲","Adaptation Cuirass"),PrimusSetId,"boss_primus_aeron.armor"),
            new UniqueDef("unique.boss_primus_aeron.charm","charm.sunrise_locket",new Txt("相転の核","Phasechange Core"),PrimusSetId,"boss_primus_aeron.charm"),
            new UniqueDef("unique.boss_primus_aeron.head","head.nightmare_visage",new Txt("激情の面","Rage Mask"),PrimusSetId,"boss_primus_aeron.head"),
            new UniqueDef("unique.boss_primus_aeron.hands","hands.volcanic_grips",new Txt("流火の双手","Flowfire Grips"),PrimusSetId,"boss_primus_aeron.hands"),
            new UniqueDef("unique.boss_primus_aeron.feet","feet.ironwave_greaves",new Txt("氷爪の脛当て","Iceclaw Greaves"),PrimusSetId,"boss_primus_aeron.feet"),
        };
        public static SetDef[] CreatePrimusSets() => new[]
        {
            new SetDef { Id=PrimusSetId, Name=new Txt("三相の武装","Threefold Armament"), BossTypeName="Mon_Primus_BossPrimusAeron",
                TwoPiece=Array.Empty<StatLine>(), ThreePiece=Array.Empty<PowerLine>(), SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_primus_aeron.stage2"),new BossSetStage(3,"boss_primus_aeron.stage3"),new BossSetStage(6,"boss_primus_aeron.stage6") },
                LinkStages=Array.Empty<SetLinkStage>() },
        };
    }
}

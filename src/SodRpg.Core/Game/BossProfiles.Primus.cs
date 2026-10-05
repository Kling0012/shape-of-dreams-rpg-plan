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
            PrimusProfile("weapon", "通常攻撃が初めて命中したとき、前方3m・角度90°の扇状範囲に攻撃力・魔力の高い方の18%の無属性ダメージを与える（敵1体につき1回命中、最大8体まで、再使用まで4秒）。", "On your basic attack's first hit, deal Neutral damage equal to 18% of whichever is higher: your Attack or Ability Power, in a forward 90° fan extending 3m. Hits each enemy once, up to 8 enemies. Cooldown: 4 seconds.",
                new[] { C("PrimusForceStrike",18,18) }, PrimusFan(BossEvent.MainHit,"PrimusForceStrike",4000,3000,90000)),
            PrimusProfile("armor", "敵の攻撃で被ダメージを受けた後、最大HPの8%分の障壁を獲得する（3秒かけて徐々に減衰、再使用まで12秒、無敵時間なし）。", "After taking damage from an enemy attack, gain a shield equal to 8% of your maximum HP that gradually decays over 3 seconds. Cooldown: 12 seconds. Does not grant invulnerability.",
                new[] { C("PrimusRageShield",8,8,BossCoefficientKind.Shield) }, PrimusShield(BossEvent.NativeDamageTaken,"PrimusRageShield",12000,3000)),
            PrimusProfile("charm", "記憶を使ったとき、その方向へ光弾を1発発射する（射程8m、弾速12m/秒、幅0.4m）。初弾着弾後、3m以内にいる未命中の敵へ0.15秒間隔で最大2回跳躍し、それぞれ攻撃力・魔力の高い方の8%の光属性ダメージを与える（最大3体の異なる敵に命中、持続1.5秒、再使用まで7秒）。", "When you use a memory, fire 1 light projectile in its direction, with an 8m range, a speed of 12m per second, and a width of 0.4m. After its first impact, it jumps up to 2 times to enemies within 3m that it has not hit, at 0.15-second intervals. Each hit deals Light damage equal to 8% of whichever is higher: your Attack or Ability Power. Hits up to 3 different enemies and lasts 1.5 seconds. Cooldown: 7 seconds.",
                new[] { C("PrimusAdaptChain",8,8) }, PrimusLight(BossEvent.MemoryUse,"PrimusAdaptChain",7000,3,1500)),
            PrimusProfile("head", "通常攻撃が初めて命中したとき、前方3m・角度120°の扇状範囲へ0秒と0.25秒に闇の斬撃を放ち、それぞれ攻撃力・魔力の高い方の10%の闇属性ダメージを与える（同じ敵に合計20%、各回最大8体まで、再使用まで5秒）。", "On your basic attack's first hit, release Dark slashes at 0 and 0.25 seconds in a forward 120° fan extending 3m. Each deals Dark damage equal to 10% of whichever is higher: your Attack or Ability Power (20% total per enemy), hitting up to 8 enemies per slash. Cooldown: 5 seconds.",
                new[] { C("PrimusRageBlades",20,20) }, PrimusFan(BossEvent.MainHit,"PrimusRageBlades",5000,3000,120000,BossElement.Dark,2)),
            PrimusProfile("hands", "記憶を使ったとき、指定位置（自身から6m以内）に0.5秒の予告後、半径2mの範囲に攻撃力・魔力の高い方の18%の火属性ダメージを1回与える（最大8体まで、再使用まで8秒、スタンなし）。", "When you use a memory, after a 0.5-second warning at the selected location within 6m of you, deal Fire damage equal to 18% of whichever is higher: your Attack or Ability Power, once within a 2m radius. Hits up to 8 enemies. Cooldown: 8 seconds. Does not stun.",
                new[] { C("PrimusDoomImpact",18,18) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"PrimusDoomImpact",anchor:BossAnchor.Cursor,
                    cooldownMillis:8000,delayMillis:500,maxTargets:8,maxInstances:1,radiusMilli:2000,rangeMilli:6000,element:BossElement.Fire)),
            PrimusProfile("feet", "移動完了時、着地点の前方2.5m・角度120°の扇状範囲へ0秒と0.25秒に冷気の斬撃を放ち、それぞれ攻撃力・魔力の高い方の10%の冷気属性ダメージを与える（同じ敵に合計20%、各回最大8体まで、再使用まで7秒）。", "When you finish moving, release Cold slashes at 0 and 0.25 seconds from your landing position in a forward 120° fan extending 2.5m. Each deals Cold damage equal to 10% of whichever is higher: your Attack or Ability Power (20% total per enemy), hitting up to 8 enemies per slash. Cooldown: 7 seconds.",
                new[] { C("PrimusIceclawCuts",20,20) }, PrimusFan(BossEvent.MovementCompleted,"PrimusIceclawCuts",7000,2500,120000,BossElement.Cold,2)),
            PrimusProfile("stage2", "通常攻撃命中・記憶使用・移動完了を行うことで、対応する相（Force / Adapt / Rage）を選択する（入力間隔0.5秒）。共通再使用時間3秒ごとに対応技を1つ放つ：通常攻撃命中（Force）＝前方3m・角度90°の扇状に攻撃力・魔力の高い方の12%の無属性ダメージ、記憶使用（Adapt）＝射程8m・弾速12m/秒・幅0.4mの光弾で攻撃力・魔力の高い方の12%の光属性ダメージ、移動完了（Rage）＝着地点の半径2mに攻撃力・魔力の高い方の12%の闇属性ダメージ（各最大8体まで）。共通再使用時間中は相の切り替えのみ行われ、技の発動や予約はされない。", "A basic attack hit, memory use, or completed movement selects Force, Adapt, or Rage respectively. Actions that select a phase must be at least 0.5 seconds apart. When the shared 3-second Cooldown is ready, release the corresponding technique: Force deals Neutral damage in a forward 90° fan extending 3m; Adapt fires a light projectile with an 8m range, a speed of 12m per second, and a width of 0.4m; Rage deals Dark damage within a 2m radius of your landing position. Each technique deals damage equal to 12% of whichever is higher: your Attack or Ability Power, to up to 8 enemies. During the shared Cooldown, actions only change your phase; they do not activate or queue a technique.",
                new[] { C("PrimusPhaseTechnique",12,12) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:500,count:3,maxTargets:1,maxInstances:1),
                PrimusFan(BossEvent.MainHit,"PrimusPhaseTechnique",3000,3000,90000),
                PrimusLight(BossEvent.MemoryUse,"PrimusPhaseTechnique",3000),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"PrimusPhaseTechnique",cooldownMillis:3000,maxTargets:8,maxInstances:1,radiusMilli:2000,element:BossElement.Dark)),
            PrimusProfile("stage3", "移動完了時、その移動の出発点に相紋を1個残す（持続4秒、新しく移動完了すると上書き）。次に発動する通常攻撃または記憶使用による相技は、自身から6m以内にある相紋を消費し、0.35秒の予告後に自身の代わりに相紋の位置から技を放つ（自身との二重発射やダメージ増加はなく、6m範囲外の相紋は破棄されて自身を起点として発射する）。", "When you finish moving, leave 1 phase glyph at your departure point for 4 seconds. Finishing another movement replaces it. The next phase technique activated by a basic attack or memory use consumes a glyph within 6m of you and, after a 0.35-second warning, fires from the glyph instead of from you. It does not fire twice or increase damage. A glyph more than 6m away is discarded, and the technique fires from you.",
                new[] { C("PrimusGlyphReach",6,6,BossCoefficientKind.Distance) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mark,delayMillis:350,lifetimeMillis:4000,maxTargets:1,maxInstances:1,rangeMilli:6000,replaceOldest:true)),
            PrimusProfile("stage6", "8秒以内に通常攻撃命中・記憶使用・移動完了の異なる3種のアクションを行うと「三相合流」が発動（再使用まで18秒、重複入力では進行せず時間も延長されない）。成立させた行動の通常相技を共通再使用時間中であっても置き換えて発動する。相紋が自身から6m以内にある場合はその位置、なければ自身を起点として（起点・向き固定）、0秒/0.3秒/0.6秒のタイミングで「前方4m・角度90°の扇状に無属性30%」「前方8m・幅0.6mの直線上に光属性30%」「半径2.5mの円形に闇属性30%」の攻撃を順次放ち、それぞれ攻撃力・魔力の高い方の30%のダメージを与える（各最大8体、同じ敵に最大計90%、上限100%）。同時に最大HPの38%分の障壁を獲得する（6秒かけて線形減衰、上限40%）。完成時に印と相紋を消費する（再使用時間中の完成は破棄、連携・特定記憶・召喚・HP条件などは不要）。", "Perform all 3 different actions within 8 seconds—a basic attack hit, memory use, and completed movement—to activate Three-Phase Confluence. Cooldown: 18 seconds. Repeating an action does not advance the sequence or extend its time limit. Confluence replaces the completing action's ordinary phase technique, even during the shared Cooldown. With its starting point and direction fixed, it fires from a phase glyph within 6m of you, or from you if there is none: at 0 seconds, a Neutral attack in a forward 90° fan extending 4m; at 0.3 seconds, a Light attack along a line 8m long and 0.6m wide; at 0.6 seconds, a Dark attack within a 2.5m radius. Each deals damage equal to 30% of whichever is higher: your Attack or Ability Power, to up to 8 enemies (up to 90% total per enemy, capped at 100%). At the same time, gain a shield equal to 38% of your maximum HP (capped at 40%) that decays at a constant rate over 6 seconds. Completion consumes the marks and phase glyph. A sequence completed during Confluence's Cooldown is discarded. No link, specific memory, summon, or HP condition is required.",
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

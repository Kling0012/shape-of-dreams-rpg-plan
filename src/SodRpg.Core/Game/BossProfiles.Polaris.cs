using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string PolarisSetId = "set.boss_polaris";
        public const string PolarisNativeContract = "Mon_Special_BossPolaris;GoldenSpear-first-hit-armor-Purgatory-CounterSpell-RainFire-JumpStomp;authored-values-not-serialized;no-guessed-Beam-CursedDive;native-main-confirmed-memory-forward-verified-displacement-departure-landing-hostile-native-damage;generated-excluded;source-owned-shields;no-boss-instance-no-reward-no-link-no-summon-no-immunity-no-skill-theft;four-fields-eight-targets;epoch-teardown";
        public const string PolarisEventOrder = "observe-own-stage-guard-end;admit-native-input;counter-reply;parts-independent;mode-gate500ms;holy-beast-seal;cycle-first-e2-fixed8s-no-duplicate-refresh;finisher-replaces-stage-guard;light45-actual-terminal-neutral45-after500ms";
        private static BossMoveProfile PolarisProfile(string id, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_polaris." + id, PolarisSetId, new Txt(ja, en), channels, actions);
        private static BossAction PolarisSpear(BossEvent e, string channel, int cd = 0, int count = 1)
            => new BossAction(e, BossMechanism.Projectile, BossPayload.Damage, channel, cooldownMillis: cd,
                lifetimeMillis: 800, count: count, maxTargets: 8, maxInstances: 4, rangeMilli: 8000,
                widthMilli: 400, speedMilli: 12000, angleMilli: count == 2 ? 12000 : 0, firstHitOnly: true, element: BossElement.Light);
        private static BossAction PolarisGuard(BossEvent e, string channel, int cd, int life)
            => new BossAction(e, BossMechanism.Defense, BossPayload.Shield, channel, cooldownMillis: cd,
                lifetimeMillis: life, maxTargets: 1, maxInstances: 1, followOwner: true);
        private static BossAction PolarisCircle(BossEvent e, string channel, int cd, int radius, int delay, BossElement element = BossElement.Neutral)
            => new BossAction(e, BossMechanism.Field, BossPayload.Damage, channel, cooldownMillis: cd,
                delayMillis: delay, maxTargets: 8, maxInstances: 4, radiusMilli: radius, rangeMilli: 6000, element: element);
        internal static BossMoveProfile[] CreatePolarisMoves() => new[]
        {
            PolarisProfile("weapon", "通常攻撃が敵に命中したとき、正面の左右6°の方向に光の槍を2本放つ（射程8m、弾速12m/秒、幅0.4m、持続0.8秒）。それぞれ攻撃力・魔力の高い方の12%の光属性ダメージを与える（同じ敵には1本のみ命中、再使用まで5秒）。", "E1 fires two Light spears at ±6°,12%H each,range8m,speed12,width0.4,life0.8s;one hit per enemy across the pair,CD5s.",
                new[] { C("PolarisGoldenSpear",12,12) }, PolarisSpear(BossEvent.MainHit,"PolarisGoldenSpear",5000,2)),
            PolarisProfile("armor", "敵の攻撃で被ダメージを受けた後、最大HPの8%分の障壁を自身に付与する（4秒かけて徐々に減衰、再使用まで12秒）。", "After hostile native E4 damage:one source-owned8%MAXHP shield,linear decay4s,CD12s.",
                new[] { C("PolarisFirstGuard",8,8,BossCoefficientKind.Shield) }, PolarisGuard(BossEvent.NativeDamageTaken,"PolarisFirstGuard",12000,4000)),
            PolarisProfile("charm", "記憶を使ったとき、指定位置（自身から6m以内）に半径2mの炎の領域を展開する。0.4秒の予告後、0秒/0.5秒/1秒のタイミングでそれぞれ攻撃力・魔力の高い方の6%の火属性ダメージを与える（同じ敵に最大計18%）。敵への初命中時、最大HPの3%分自身を回復する（1回のみ、再使用まで9秒）。", "E2 places one radius2m Fire field at a legal point within6m;0.4s warning then three6%H pulses at0/0.5/1s(total18 per enemy). First successful hit heals you3%MAXHP exactly once,CD9s.",
                new[] { C("PolarisPurgatory",18,18), C("PolarisPurgatoryReturn",3,3,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Damage,"PolarisPurgatory",anchor:BossAnchor.Cursor,cooldownMillis:9000,delayMillis:400,intervalMillis:500,count:3,maxTargets:8,maxInstances:1,radiusMilli:2000,rangeMilli:6000,element:BossElement.Fire),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Heal,"PolarisPurgatoryReturn",maxTargets:1,maxInstances:1)),
            PolarisProfile("head", "記憶を使ったとき、1秒間、最大HPの4%分の障壁を獲得し、1秒間の反撃態勢に入る。この間に敵の攻撃で被ダメージを初めて受けた際、8m以内の攻撃者へ光弾を1発放ち（射程8m、弾速12m/秒、幅0.4m、持続0.8秒）、攻撃力・魔力の高い方の14%の光属性ダメージを与える（再使用まで10秒、スタンなし）。", "E2 grants4%MAXHP shield and a1s counter window;first hostile native E4 fires one14%H Light shot toward the attacker within8m(range8,speed12,width0.4,life0.8s),CD10s. No skill theft or stun.",
                new[] { C("PolarisCounterGuard",4,4,BossCoefficientKind.Shield), C("PolarisCountershot",14,14) },
                PolarisGuard(BossEvent.MemoryUse,"PolarisCounterGuard",10000,1000), PolarisSpear(BossEvent.NativeDamageTaken,"PolarisCountershot")),
            PolarisProfile("hands", "通常攻撃が敵に命中したとき、命中地点（自身から6m以内）の周囲半径1.5m・角度0°/120°/240°の位置に半径1.2mの火炎円を3個配置する。0.4秒の予告後、0秒/0.15秒/0.3秒の時差でそれぞれ攻撃力・魔力の高い方の6%の火属性ダメージを与える（同じ敵に最大計18%）。再使用まで8秒。", "E1:around the target point clamped within6m,three radius1.2m Fire circles at radius1.5m,angles0/120/240°;0.4s warning then offsets0/0.15/0.3s,6%H each(total18 per enemy),CD8s.",
                new[] { C("PolarisRainfire",18,18) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"PolarisRainfire",anchor:BossAnchor.Hit,cooldownMillis:8000,delayMillis:400,intervalMillis:150,count:3,maxTargets:8,maxInstances:1,radiusMilli:1200,rangeMilli:6000,magnitudeMilli:1500,element:BossElement.Fire)),
            PolarisProfile("feet", "移動完了時、その着地点に0.25秒の予告後、半径2.5mに攻撃力・魔力の高い方の18%の無属性踏みつけダメージを1回与える（再使用まで7秒、追加移動やスタンなし）。", "E3 verified landing:0.25s warning then one radius2.5m neutral stomp18%H,CD7s;no extra movement or stun.",
                new[] { C("PolarisStarstomp",18,18) }, PolarisCircle(BossEvent.MovementCompleted,"PolarisStarstomp",7000,2500,250)),
            PolarisProfile("stage2", "初期状態は「神聖（Holy）」。記憶を使用すると神聖モードを選択し、3秒間、最大HPの6%分の障壁を獲得する（障壁の再使用まで8秒）。移動を完了すると「魔獣（Beast）」モードを選択し、着地点の半径2.5mに攻撃力・魔力の高い方の18%の踏みつけダメージを与える（攻撃の再使用まで4秒）。入力間隔0.5秒。それぞれの再使用時間中もモード切り替えは可能（技の予約は不可）。", "Initially Holy;E2/E3 gate0.5s. E2 selects Holy and awards stage-owned6%MAXHP guard for3s(guard CD8s);E3 selects Beast and landing radius2.5m stomp18%H(attack CD4s). Independent CDs;busy CD selects mode without queuing.",
                new[] { C("PolarisHolyGuard",6,6,BossCoefficientKind.Shield), C("PolarisBeastStomp",18,18) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:500,count:2,maxTargets:1,maxInstances:1),
                PolarisGuard(BossEvent.MemoryUse,"PolarisHolyGuard",8000,3000), PolarisCircle(BossEvent.MovementCompleted,"PolarisBeastStomp",4000,2500,250)),
            PolarisProfile("stage3", "神聖から魔獣へ移行したとき（移動完了、または神聖の障壁の全消費・効果終了時）、出発点（障壁終了時は現在地）に紋印を1個生成する（持続4秒）。次に発動する魔獣モード中の通常攻撃命中または移動完了による踏みつけは、6m以内にある印を消費し、発生地点をその印の位置へ移して0.35秒の予告で発動する（同一モード中は新たな印は生成されず、二重ヒットなし。記憶使用で神聖に戻ると古い印は破棄される）。", "Holy→Beast via E3 or exhaustion/natural expiry of the stage guard leaves one4s seal at verified departure(or current position on guard end). Next emitted E1/E3 stomp consumes a seal within6m and moves there with0.35s warning instead of duplicating. Same-mode inputs create no seal;E2 discards the old seal.",
                new[] { C("PolarisDepartureReach",6,6,BossCoefficientKind.Distance) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mark,delayMillis:350,lifetimeMillis:4000,maxTargets:1,maxInstances:1,rangeMilli:6000)),
            PolarisProfile("stage6", "最初に記憶を使ってから8秒以内に「神聖→魔獣→神聖」と移行を完了すると発動（再使用まで18秒、重複入力による進行や時間延長なし、再使用時間中の完成は保持されず破棄）。最後の記憶使用時の障壁（6%）を以下に置き換える：自身から6m以内の印（なければ自身）から、向き固定の光槍を放ち攻撃力・魔力の高い方の45%の光属性ダメージを与える（射程8m、弾速12m/秒、幅0.4m、最初の敵・壁・射程終端で停止）。その実際の終点に0.5秒の予告後、半径3mに攻撃力・魔力の高い方の45%の無属性踏みつけダメージを与え（同じ敵に最大計90%、ダメージ上限100%）、最大HPの38%分の障壁を獲得する（6秒かけて線形減衰、上限40%）。", "Within8s of first accepted E2,return Holy→Beast→Holy;duplicates never advance or extend,CD18s. Replace the completing E2's6% guard:from valid seal within6m or owner,frozen final E2 direction,Light spear45%H(range8,speed12,width0.4,first enemy/wall/range end),then at the actual endpoint0.5s warning and radius3m neutral stomp45%H(total90 per enemy,A cap100). One38%MAXHP shield,6s linear decay(B cap40). Consume cycle/seal;busy-CD completion is not retained. No reward/link.",
                new[] { C("PolarisDualSanctity",90,100), C("PolarisReturnGuard",38,40,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mark,cooldownMillis:18000,lifetimeMillis:8000,count:3,maxTargets:1,maxInstances:1),
                PolarisSpear(BossEvent.MemoryUse,"PolarisDualSanctity"), PolarisCircle(BossEvent.Clock,"PolarisDualSanctity",0,3000,500),
                PolarisGuard(BossEvent.MemoryUse,"PolarisReturnGuard",0,6000)),
        };
        public static UniqueDef[] CreatePolarisPieces() => new[]
        {
            new UniqueDef("unique.boss_polaris.weapon","weapon.bulwark_greatsword",new Txt("墜聖の金槍","Fallen Golden Spear"),PolarisSetId,"boss_polaris.weapon"),
            new UniqueDef("unique.boss_polaris.armor","armor.ember_plate",new Txt("聖獣の胸甲","Sacred Beast Cuirass"),PolarisSetId,"boss_polaris.armor"),
            new UniqueDef("unique.boss_polaris.charm","charm.sunrise_locket",new Txt("星無き聖印","Starless Sacred Seal"),PolarisSetId,"boss_polaris.charm"),
            new UniqueDef("unique.boss_polaris.head","head.nightmare_visage",new Txt("反呪の冠","Counterspell Crown"),PolarisSetId,"boss_polaris.head"),
            new UniqueDef("unique.boss_polaris.hands","hands.volcanic_grips",new Txt("降火の手甲","Rainfire Gauntlets"),PolarisSetId,"boss_polaris.hands"),
            new UniqueDef("unique.boss_polaris.feet","feet.ironwave_greaves",new Txt("踏星の鉄靴","Starstomp Sabatons"),PolarisSetId,"boss_polaris.feet"),
        };
        public static SetDef[] CreatePolarisSets() => new[]
        {
            new SetDef { Id=PolarisSetId,Name=new Txt("墜聖の双装","Fallen Sanctity Regalia"),BossTypeName="Mon_Special_BossPolaris",
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_polaris.stage2"),new BossSetStage(3,"boss_polaris.stage3"),new BossSetStage(6,"boss_polaris.stage6") },LinkStages=Array.Empty<SetLinkStage>() },
        };
    }
}

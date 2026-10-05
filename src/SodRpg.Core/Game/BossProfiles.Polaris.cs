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
            PolarisProfile("weapon", "通常攻撃が敵に命中したとき、正面の左右6°の方向に光の槍を2本放つ（射程8m、弾速12m/秒、幅0.4m、持続0.8秒）。それぞれ攻撃力・魔力の高い方の12%の光属性ダメージを与える（同じ敵には1本のみ命中、再使用まで5秒）。", "When your basic attack hits an enemy, fire 2 spears of light, angled 6° left and right of straight ahead. Each has an 8m range, a speed of 12m per second, a width of 0.4m, and a 0.8-second lifetime, and deals Light damage equal to 12% of whichever is higher: your Attack or Ability Power. Only 1 spear can hit each enemy. Cooldown: 5 seconds.",
                new[] { C("PolarisGoldenSpear",12,12) }, PolarisSpear(BossEvent.MainHit,"PolarisGoldenSpear",5000,2)),
            PolarisProfile("armor", "敵の攻撃で被ダメージを受けた後、最大HPの8%分の障壁を自身に付与する（4秒かけて徐々に減衰、再使用まで12秒）。", "After taking damage from an enemy attack, gain a shield equal to 8% of your maximum HP that gradually decays over 4 seconds. Cooldown: 12 seconds.",
                new[] { C("PolarisFirstGuard",8,8,BossCoefficientKind.Shield) }, PolarisGuard(BossEvent.NativeDamageTaken,"PolarisFirstGuard",12000,4000)),
            PolarisProfile("charm", "記憶を使ったとき、指定位置（自身から6m以内）に半径2mの炎の領域を展開する。0.4秒の予告後、0秒/0.5秒/1秒のタイミングでそれぞれ攻撃力・魔力の高い方の6%の火属性ダメージを与える（同じ敵に最大計18%）。敵への初命中時、最大HPの3%分自身を回復する（1回のみ、再使用まで9秒）。", "When you use a memory, create a fire field with a 2m radius at the selected location within 6m of you. After a 0.4-second warning, it deals Fire damage at 0, 0.5, and 1 second, each time equal to 6% of whichever is higher: your Attack or Ability Power (up to 18% total per enemy). Its first hit on an enemy heals you for 3% of your maximum HP, once only. Cooldown: 9 seconds.",
                new[] { C("PolarisPurgatory",18,18), C("PolarisPurgatoryReturn",3,3,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Damage,"PolarisPurgatory",anchor:BossAnchor.Cursor,cooldownMillis:9000,delayMillis:400,intervalMillis:500,count:3,maxTargets:8,maxInstances:1,radiusMilli:2000,rangeMilli:6000,element:BossElement.Fire),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Heal,"PolarisPurgatoryReturn",maxTargets:1,maxInstances:1)),
            PolarisProfile("head", "記憶を使ったとき、1秒間、最大HPの4%分の障壁を獲得し、1秒間の反撃態勢に入る。この間に敵の攻撃で被ダメージを初めて受けた際、8m以内の攻撃者へ光弾を1発放ち（射程8m、弾速12m/秒、幅0.4m、持続0.8秒）、攻撃力・魔力の高い方の14%の光属性ダメージを与える（再使用まで10秒、スタンなし）。", "When you use a memory, gain a shield equal to 4% of your maximum HP for 1 second and enter a counter stance for 1 second. The first time you take damage from an enemy attack during this stance, fire 1 light projectile at the attacker if they are within 8m. It has an 8m range, a speed of 12m per second, a width of 0.4m, and a 0.8-second lifetime, and deals Light damage equal to 14% of whichever is higher: your Attack or Ability Power. Cooldown: 10 seconds. Does not stun.",
                new[] { C("PolarisCounterGuard",4,4,BossCoefficientKind.Shield), C("PolarisCountershot",14,14) },
                PolarisGuard(BossEvent.MemoryUse,"PolarisCounterGuard",10000,1000), PolarisSpear(BossEvent.NativeDamageTaken,"PolarisCountershot")),
            PolarisProfile("hands", "通常攻撃が敵に命中したとき、命中地点（自身から6m以内）の周囲半径1.5m・角度0°/120°/240°の位置に半径1.2mの火炎円を3個配置する。0.4秒の予告後、0秒/0.15秒/0.3秒の時差でそれぞれ攻撃力・魔力の高い方の6%の火属性ダメージを与える（同じ敵に最大計18%）。再使用まで8秒。", "When your basic attack hits an enemy, place 3 fire circles, each with a 1.2m radius, around the hit location within 6m of you. The circles are 1.5m from that location at angles of 0°, 120°, and 240°. After a 0.4-second warning, they strike at 0, 0.15, and 0.3 seconds, each dealing Fire damage equal to 6% of whichever is higher: your Attack or Ability Power (up to 18% total per enemy). Cooldown: 8 seconds.",
                new[] { C("PolarisRainfire",18,18) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"PolarisRainfire",anchor:BossAnchor.Hit,cooldownMillis:8000,delayMillis:400,intervalMillis:150,count:3,maxTargets:8,maxInstances:1,radiusMilli:1200,rangeMilli:6000,magnitudeMilli:1500,element:BossElement.Fire)),
            PolarisProfile("feet", "移動完了時、その着地点に0.25秒の予告後、半径2.5mに攻撃力・魔力の高い方の18%の無属性踏みつけダメージを1回与える（再使用まで7秒、追加移動やスタンなし）。", "When you finish moving, after a 0.25-second warning, stomp once at your landing position within a 2.5m radius, dealing Neutral damage equal to 18% of whichever is higher: your Attack or Ability Power. Cooldown: 7 seconds. Does not cause additional movement or stun.",
                new[] { C("PolarisStarstomp",18,18) }, PolarisCircle(BossEvent.MovementCompleted,"PolarisStarstomp",7000,2500,250)),
            PolarisProfile("stage2", "初期状態は「神聖（Holy）」。記憶を使用すると神聖モードを選択し、3秒間、最大HPの6%分の障壁を獲得する（障壁の再使用まで8秒）。移動を完了すると「魔獣（Beast）」モードを選択し、着地点の半径2.5mに攻撃力・魔力の高い方の18%の踏みつけダメージを与える（攻撃の再使用まで4秒）。入力間隔0.5秒。それぞれの再使用時間中もモード切り替えは可能（技の予約は不可）。", "Start in Holy mode. Using a memory selects Holy mode and grants a shield equal to 6% of your maximum HP for 3 seconds. Shield Cooldown: 8 seconds. Finishing movement selects Beast mode and stomps within a 2.5m radius of your landing position for damage equal to 18% of the higher of your Attack and Ability Power. Attack Cooldown: 4 seconds. Actions that select a mode must be at least 0.5 seconds apart. You can still switch modes during either Cooldown, but cannot queue a technique for later.",
                new[] { C("PolarisHolyGuard",6,6,BossCoefficientKind.Shield), C("PolarisBeastStomp",18,18) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:500,count:2,maxTargets:1,maxInstances:1),
                PolarisGuard(BossEvent.MemoryUse,"PolarisHolyGuard",8000,3000), PolarisCircle(BossEvent.MovementCompleted,"PolarisBeastStomp",4000,2500,250)),
            PolarisProfile("stage3", "神聖から魔獣へ移行したとき（移動完了、または神聖の障壁の全消費・効果終了時）、出発点（障壁終了時は現在地）に紋印を1個生成する（持続4秒）。次に発動する魔獣モード中の通常攻撃命中または移動完了による踏みつけは、6m以内にある印を消費し、発生地点をその印の位置へ移して0.35秒の予告で発動する（同一モード中は新たな印は生成されず、二重ヒットなし。記憶使用で神聖に戻ると古い印は破棄される）。", "Switching from Holy to Beast mode by finishing movement, fully using up Holy's shield, or letting it expire creates 1 seal lasting 4 seconds at your departure point, or at your current position when the shield ends. Your next Beast-mode stomp triggered by a basic attack hit or completed movement consumes a seal within 6m and strikes from that seal after a 0.35-second warning instead. Staying in the same mode creates no new seals, and the stomp does not hit twice. Using a memory to return to Holy mode discards the old seal.",
                new[] { C("PolarisDepartureReach",6,6,BossCoefficientKind.Distance) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mark,delayMillis:350,lifetimeMillis:4000,maxTargets:1,maxInstances:1,rangeMilli:6000)),
            PolarisProfile("stage6", "最初に記憶を使ってから8秒以内に「神聖→魔獣→神聖」と移行を完了すると発動（再使用まで18秒、重複入力による進行や時間延長なし、再使用時間中の完成は保持されず破棄）。最後の記憶使用時の障壁（6%）を以下に置き換える：自身から6m以内の印（なければ自身）から、向き固定の光槍を放ち攻撃力・魔力の高い方の45%の光属性ダメージを与える（射程8m、弾速12m/秒、幅0.4m、最初の敵・壁・射程終端で停止）。その実際の終点に0.5秒の予告後、半径3mに攻撃力・魔力の高い方の45%の無属性踏みつけダメージを与え（同じ敵に最大計90%、ダメージ上限100%）、最大HPの38%分の障壁を獲得する（6秒かけて線形減衰、上限40%）。", "Complete Holy → Beast → Holy within 8 seconds of your first memory use to activate this effect. Cooldown: 18 seconds. Repeating the same action does not advance the sequence or extend its time limit. A sequence completed during the Cooldown is discarded, not saved. Replace the final memory use's 6% shield with the following: fire a spear of light in a fixed direction from a seal within 6m of you, or from you if there is none. It deals Light damage equal to 45% of the higher of your Attack and Ability Power, has an 8m range, a speed of 12m per second, and a width of 0.4m, and stops at the first enemy, wall, or end of its range. After a 0.5-second warning at its actual stopping point, stomp within a 3m radius for Neutral damage equal to 45% of the higher of your Attack and Ability Power (up to 90% total per enemy, with damage capped at 100%). Gain a shield equal to 38% of your maximum HP (capped at 40%) that decays at a constant rate over 6 seconds.",
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

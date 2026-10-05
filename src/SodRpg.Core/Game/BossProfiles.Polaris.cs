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
            PolarisProfile("weapon", "E1で正面±6°に光槍2本、各12%H、射程8m/速度12/幅0.4/寿命0.8秒。同敵は片槍のみ、CD5秒。", "E1 fires two Light spears at ±6°,12%H each,range8m,speed12,width0.4,life0.8s;one hit per enemy across the pair,CD5s.",
                new[] { C("PolarisGoldenSpear",12,12) }, PolarisSpear(BossEvent.MainHit,"PolarisGoldenSpear",5000,2)),
            PolarisProfile("armor", "敵対native E4被damage後に自前盾8%最大HP、4秒線形減衰、1枚、CD12秒。", "After hostile native E4 damage:one source-owned8%MAXHP shield,linear decay4s,CD12s.",
                new[] { C("PolarisFirstGuard",8,8,BossCoefficientKind.Shield) }, PolarisGuard(BossEvent.NativeDamageTaken,"PolarisFirstGuard",12000,4000)),
            PolarisProfile("charm", "E2指定点を本人6m内へ裁定、半径2m火場1個、0.4秒予告後0/0.5/1秒に各6%H（同敵18）。最初の成功hitで本人heal3%最大HPを1回のみ、CD9秒。", "E2 places one radius2m Fire field at a legal point within6m;0.4s warning then three6%H pulses at0/0.5/1s(total18 per enemy). First successful hit heals you3%MAXHP exactly once,CD9s.",
                new[] { C("PolarisPurgatory",18,18), C("PolarisPurgatoryReturn",3,3,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Damage,"PolarisPurgatory",anchor:BossAnchor.Cursor,cooldownMillis:9000,delayMillis:400,intervalMillis:500,count:3,maxTargets:8,maxInstances:1,radiusMilli:2000,rangeMilli:6000,element:BossElement.Fire),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Heal,"PolarisPurgatoryReturn",maxTargets:1,maxInstances:1)),
            PolarisProfile("head", "E2で盾4%最大HP/1秒と反撃窓1秒。最初の敵対native E4後、8m内の攻撃者へ光弾14%Hを1本（射程8/速度12/幅0.4/寿命0.8秒）、CD10秒。skill窃盗/stunなし。", "E2 grants4%MAXHP shield and a1s counter window;first hostile native E4 fires one14%H Light shot toward the attacker within8m(range8,speed12,width0.4,life0.8s),CD10s. No skill theft or stun.",
                new[] { C("PolarisCounterGuard",4,4,BossCoefficientKind.Shield), C("PolarisCountershot",14,14) },
                PolarisGuard(BossEvent.MemoryUse,"PolarisCounterGuard",10000,1000), PolarisSpear(BossEvent.NativeDamageTaken,"PolarisCountershot")),
            PolarisProfile("hands", "E1対象点を本人6m内へ裁定、半径1.5m上の0/120/240°に火円3個（各半径1.2m）、0.4秒予告後0/0.15/0.3秒ずれで各6%H（同敵18）、CD8秒。", "E1:around the target point clamped within6m,three radius1.2m Fire circles at radius1.5m,angles0/120/240°;0.4s warning then offsets0/0.15/0.3s,6%H each(total18 per enemy),CD8s.",
                new[] { C("PolarisRainfire",18,18) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"PolarisRainfire",anchor:BossAnchor.Hit,cooldownMillis:8000,delayMillis:400,intervalMillis:150,count:3,maxTargets:8,maxInstances:1,radiusMilli:1200,rangeMilli:6000,magnitudeMilli:1500,element:BossElement.Fire)),
            PolarisProfile("feet", "E3着地点で0.25秒予告後、半径2.5m無属性踏撃18%Hを1回、CD7秒。追加移動/stunなし。", "E3 verified landing:0.25s warning then one radius2.5m neutral stomp18%H,CD7s;no extra movement or stun.",
                new[] { C("PolarisStarstomp",18,18) }, PolarisCircle(BossEvent.MovementCompleted,"PolarisStarstomp",7000,2500,250)),
            PolarisProfile("stage2", "初期Holy。E2/E3入力gate0.5秒、E2=Holy＋自前段階盾6%最大HP/3秒（盾CD8秒）、E3=Beast＋着地半径2.5m踏撃18%H（攻撃CD4秒）。CD中もmode選択可、予約なし。", "Initially Holy;E2/E3 gate0.5s. E2 selects Holy and awards stage-owned6%MAXHP guard for3s(guard CD8s);E3 selects Beast and landing radius2.5m stomp18%H(attack CD4s). Independent CDs;busy CD selects mode without queuing.",
                new[] { C("PolarisHolyGuard",6,6,BossCoefficientKind.Shield), C("PolarisBeastStomp",18,18) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:500,count:2,maxTargets:1,maxInstances:1),
                PolarisGuard(BossEvent.MemoryUse,"PolarisHolyGuard",8000,3000), PolarisCircle(BossEvent.MovementCompleted,"PolarisBeastStomp",4000,2500,250)),
            PolarisProfile("stage3", "Holy→BeastはE3/段階専用盾の使切り/自然失効。出発点（盾終了は現在地）に印1個/4秒、次の実発動E1/E3踏撃を6m内の印へ0.35秒予告で移し消費。二重hitなし、同modeは新印なし、E2で旧印破棄。", "Holy→Beast via E3 or exhaustion/natural expiry of the stage guard leaves one4s seal at verified departure(or current position on guard end). Next emitted E1/E3 stomp consumes a seal within6m and moves there with0.35s warning instead of duplicating. Same-mode inputs create no seal;E2 discards the old seal.",
                new[] { C("PolarisDepartureReach",6,6,BossCoefficientKind.Distance) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mark,delayMillis:350,lifetimeMillis:4000,maxTargets:1,maxInstances:1,rangeMilli:6000)),
            PolarisProfile("stage6", "最初のE2から8秒内にHoly→Beast→Holy、重複は進行/期限延長なし、CD18秒。最後E2の盾6を置換：有効な6m内の印/本人から向き固定の光槍45%H（射程8/速度12/幅0.4、初敵/壁/射程末）→実終点で0.5秒予告後半径3m無属性踏撃45%H（同敵90、A上限100）。盾38%最大HP1枚/6秒線形減衰（B上限40）。cycle/印消費、CD中完成保持なし。連携/報酬なし。", "Within8s of first accepted E2,return Holy→Beast→Holy;duplicates never advance or extend,CD18s. Replace the completing E2's6% guard:from valid seal within6m or owner,frozen final E2 direction,Light spear45%H(range8,speed12,width0.4,first enemy/wall/range end),then at the actual endpoint0.5s warning and radius3m neutral stomp45%H(total90 per enemy,A cap100). One38%MAXHP shield,6s linear decay(B cap40). Consume cycle/seal;busy-CD completion is not retained. No reward/link.",
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

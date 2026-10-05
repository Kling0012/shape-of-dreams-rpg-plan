using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string SkollSetId = "set.boss_skoll";
        public const string SkollRewardId = "boss_skoll.glacial_core";
        public const string SkollNativeContract = "cold-heal-only;exact-coroutine-DoHeal-scope;exclude-generated-at-OnDealDamage;preserve-native-crit-chain-and-heal-processors;bonus-after-native-processors-capped-H;bonus-enters-native-bank-once;success-native-owner-heal-event-only;owner-shared-heal-and-burst-CD;consume-next-target-before-prepare-native-radius;interval-only-native-curve-result;no-bank-write;one-native-shot-per-tick;count-only-created-native-projectiles;exact-core-owner-source-parent-victim-ref-and-monotonic-native-life-not-creationTime-alone;clear-owner-room-epoch-gem-life";
        public const string SkollEventOrder = "expire-owner-marks;release-existing-three-on-next-E1-or-E2;stage2-slash;stage3-arrow;stage6-four-rain-terminal-shield;part-counters";

        private static BossChannelDef SkollChannel(string id, int value, int cap, BossCoefficientKind kind = BossCoefficientKind.Damage)
            => new BossChannelDef(id, value * 1000, cap * 1000, kind);
        private static BossMoveProfile SkollProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_skoll." + suffix, SkollSetId, new Txt(ja, en), channels, actions);
        private static BossAction SkollArrow(BossEvent kind, string channel, int delay, int radius, int cooldown = 0)
            => new BossAction(kind, BossMechanism.Field, BossPayload.Damage, channel, anchor: BossAnchor.Cursor,
                cooldownMillis: cooldown, delayMillis: delay, lifetimeMillis: 1500, maxInstances: 1, radiusMilli: radius, rangeMilli: 8000, element: BossElement.Cold);
        private static BossAction SkollArrowTicks(string channel, int delay, int radius)
            => new BossAction(BossEvent.Clock, BossMechanism.Field, BossPayload.Damage, channel, delayMillis: delay,
                lifetimeMillis: 1500, intervalMillis: 500, count: 3, maxInstances: 1, radiusMilli: radius, rangeMilli: 8000, element: BossElement.Cold);

        internal static BossMoveProfile[] CreateSkollMoves() => new[]
        {
            SkollProfile("weapon", "主撃で敵地点（本人から8m以内）を固定、0.25秒後に長4m×幅1mの氷刃28%H（上限84）、CD5秒。", "A native main hit fixes the enemy point within8m; after0.25s a4m by1m Cold blade deals28%H(cap84), CD5s.", new[] { SkollChannel("SkollBlade",28,84) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"SkollBlade",BossShape.Line,BossAnchor.Hit,cooldownMillis:5000,delayMillis:250,rangeMilli:4000,widthMilli:1000,magnitudeMilli:8000,element:BossElement.Cold)),
            SkollProfile("armor", "native被弾時の本人地点を固定、0.40秒後に半径2mの氷刃25%H（上限75）、CD7秒。", "Native damage taken fixes your current point; after0.40s a2m Cold counter deals25%H(cap75), CD7s.", new[] { SkollChannel("SkollCounter",25,75) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Field,BossPayload.Damage,"SkollCounter",cooldownMillis:7000,delayMillis:400,radiusMilli:2000,element:BossElement.Cold)),
            SkollProfile("charm", "通常/奥義記憶確定でカーソル合法地点8m以内に氷矢。0.45秒後半径1.8mへ18%H（上限54）、以後0.5秒間隔3tick各6%H（上限18）。場寿命1.5秒、最大1場、CD8秒。", "Confirmed normal/ultimate memory use fixes a legal cursor point within8m. After0.45s an icy arrow deals18%H(cap54) in1.8m, then3 ticks every0.5s for6%H each(cap18). Field lasts1.5s, max1, CD8s.", new[] { SkollChannel("SkollArrow",18,54),SkollChannel("SkollArrowTick",6,18) },
                SkollArrow(BossEvent.MemoryUse,"SkollArrow",450,1800,8000),SkollArrowTicks("SkollArrowTick",950,1800)),
            SkollProfile("head", "主撃3回（6秒期限）で敵地点中心の横列3点（間隔1.5m）を固定。0.50/0.75/1.00秒後半径0.9m各12%H（上限36）、最大1列、CD8秒。", "Every3 native main hits within6s fix3 lateral points spaced1.5m around the enemy. After0.50/0.75/1.00s each strikes0.9m for12%H(cap36), max1 row, CD8s.", new[] { SkollChannel("SkollRow",12,36) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"SkollRow",BossShape.Circle,BossAnchor.Hit,cooldownMillis:8000,delayMillis:500,intervalMillis:250,count:3,maxInstances:1,mainHits:3,counterLifetimeMillis:6000,radiusMilli:900,widthMilli:1500,element:BossElement.Cold)),
            SkollProfile("hands", "主撃4回（6秒期限）で本人前方長4m・60度の扇を0.25秒予告。30%H（上限90）＋1m押出し、boss/CC免疫は移動除外、CD6秒。", "Every4 native main hits within6s warn a4m,60-degree forward fan for0.25s, then30%H Cold(cap90) and1m push; bosses/CC-immune targets are not moved, CD6s.", new[] { SkollChannel("SkollFan",30,90) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Push,"SkollFan",BossShape.Fan,cooldownMillis:6000,delayMillis:250,mainHits:4,counterLifetimeMillis:6000,rangeMilli:4000,angleMilli:60000,magnitudeMilli:1000,element:BossElement.Cold)),
            SkollProfile("feet", "本人native移動完了で追従半径2mの氷旋風、0.25秒間隔4tick各7%H（上限21）。寿命1秒、最大1、CD5秒。自動移動/操作blockなし。", "Native movement completion creates a following2m Cold whirlwind:4 ticks every0.25s for7%H each(cap21), lasts1s, max1, CD5s. No automatic movement or control block.", new[] { SkollChannel("SkollWhirl",7,21) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Field,BossPayload.Damage,"SkollWhirl",cooldownMillis:5000,delayMillis:250,lifetimeMillis:1000,intervalMillis:250,count:4,maxInstances:1,radiusMilli:2000,followOwner:true,element:BossElement.Cold)),
            SkollProfile("stage2", "主撃/通常・奥義記憶で本人氷印1（最大3、最後の進行から6秒）。3印の次の主撃/記憶で全消費し敵/カーソル地点8m以内を固定、0.4秒後長5m×幅1mの氷裂き35%H、CD4秒。", "Native main hits/confirmed normal or ultimate memories add1 owner ice mark(max3,6s since last progress). The next activation after3 marks consumes all and fixes an enemy/cursor point within8m; after0.4s a5m by1m Cold slash deals35%H, CD4s.", new[] { SkollChannel("SkollSeal",1,1,BossCoefficientKind.Stat),SkollChannel("SkollRelease",35,35) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mark,"SkollSeal",lifetimeMillis:6000,count:3,ledgerId:"SkollOwnerMarks",element:BossElement.Cold),
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mark,"SkollSeal",lifetimeMillis:6000,count:3,ledgerId:"SkollOwnerMarks",element:BossElement.Cold),
                new BossAction(BossEvent.Clock,BossMechanism.Field,BossPayload.Damage,"SkollRelease",BossShape.Line,BossAnchor.Hit,cooldownMillis:4000,delayMillis:400,rangeMilli:5000,widthMilli:1000,magnitudeMilli:8000,element:BossElement.Cold)),
            SkollProfile("stage3", "2点の固定地点へ氷矢を追加。0.65秒後半径2mへ15%H、以後0.5秒間隔3tick各5%H、場寿命1.5秒、同時1場。", "The2-piece fixed point gains an icy arrow: after0.65s,15%H Cold in2m, then3 ticks every0.5s for5%H each; field lasts1.5s, max1.", new[] { SkollChannel("SkollSealArrow",15,15),SkollChannel("SkollSealTick",5,5) },
                SkollArrow(BossEvent.Clock,"SkollSealArrow",650,2000),SkollArrowTicks("SkollSealTick",1150,2000)),
            SkollProfile("stage6", "2点発動時、固定地点を囲む半径2mの4点へ氷雨。0.6/0.8/1.0/1.2秒後、半径1m各22.5%H（計90、上限100）。終段で本人shield38%H（上限40）/2秒非加算、CD10秒、同時1雨。", "On2-piece release,4 icy strikes encircle the fixed point at2m. After0.6/0.8/1.0/1.2s each strikes1m for22.5%H(total90,cap100); the terminal pulse gives a non-stacking38%H self shield(cap40) for2s. CD10s, max1 rain.", new[] { SkollChannel("SkollRain",90,100),SkollChannel("SkollRainShield",38,40,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.Clock,BossMechanism.Field,BossPayload.Damage,"SkollRain",cooldownMillis:10000,delayMillis:600,intervalMillis:200,count:4,maxInstances:1,radiusMilli:1000,rangeMilli:2000,element:BossElement.Cold),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"SkollRainShield",delayMillis:1200,lifetimeMillis:2000,maxInstances:1,element:BossElement.Cold)),
        };

        internal static BossRewardProfile[] CreateSkollRewards()
        {
            BossRewardAction Heal() => new BossRewardAction(BossRewardActionKind.NativeHeal,20000,20000,cooldownMillis:2000,order:0,magnitudeMilli:12000);
            BossRewardAction Target() => new BossRewardAction(BossRewardActionKind.NativeTarget,durationMillis:2000,order:1);
            return new[] { new BossRewardProfile(SkollRewardId,SkollSetId,"Gem_U_GlacialCore",BossRewardAdapter.GlacialCore,new[]
            {
                new BossRewardStage(1,new Txt("装着Coreのnative Cold回復だけ+20%、追加上限0.12H、CD2秒。元のcrit/chainを維持し回復は本体bankへ自然に入る。生成Coldは発動不可。", "Only equipped Core's native Cold heal gains20%, bonus capped at0.12H, CD2s. Preserve native crit/chain and natural heal-to-bank conversion; generated Cold cannot trigger it."),new[] { Heal() }),
                new BossRewardStage(2,new Txt("段階1＋native Cold回復の原因敵を2秒記録。次の本体弾は元shootRadius内の生存・敵対対象ならその敵を優先。bank/damage/proc/射数は不変。", "Stage1 plus remember the native Cold heal's enemy for2s; the next native projectile prefers it only while alive, hostile and inside original shootRadius. Bank/damage/proc/shot count are unchanged."),new[] { Heal(),Target() }),
                new BossRewardStage(3,new Txt("段階2＋native Cold回復後1秒、既存bankの実射のみ最大3発を最短0.10秒へ加速（元の方が速ければ維持）、CD8秒、1tick1射。bank/合法敵なしでは加速せず複製なし。", "Stage2 plus for1s after native Cold healing accelerate at most3 real bank-funded shots to0.10s minimum interval, preserving faster native cadence; CD8s, one shot per tick. No acceleration without bank/legal enemies, no duplication."),new[] { Heal(),Target(),new BossRewardAction(BossRewardActionKind.NativeInterval,cooldownMillis:8000,durationMillis:1000,count:3,order:2,intervalMillis:100) }),
            }) };
        }
        public static UniqueDef[] CreateSkollPieces() => new[]
        {
            new UniqueDef("set.boss_skoll.weapon","weapon.chain_sword",new Txt("氷雨の剣","Icerain Sword"),SkollSetId,"boss_skoll.weapon"),
            new UniqueDef("set.boss_skoll.armor","armor.chain_hauberk",new Txt("氷刃の帷子","Iceblade Mail"),SkollSetId,"boss_skoll.armor"),
            new UniqueDef("set.boss_skoll.charm","charm.pulsing_core",new Txt("雪嶺の核","Snowcrest Core"),SkollSetId,"boss_skoll.charm"),
            new UniqueDef("set.boss_skoll.head","head.frost_helm",new Txt("氷輪の冠","Icering Crown"),SkollSetId,"boss_skoll.head"),
            new UniqueDef("set.boss_skoll.hands","hands.frost_mitts",new Txt("霜裂きの手甲","Frostrend Grips"),SkollSetId,"boss_skoll.hands"),
            new UniqueDef("set.boss_skoll.feet","feet.iron_greaves",new Txt("氷渦の鉄靴","Icewhorl Sabatons"),SkollSetId,"boss_skoll.feet"),
        };
        public static SetDef[] CreateSkollSets() => new[] { new SetDef
        {
            Id=SkollSetId,Name=new Txt("氷刃の王装","Iceblade Regalia"),BossTypeName="Mon_SnowMountain_BossSkoll",
            TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
            BossStages=new[] { new BossSetStage(2,"boss_skoll.stage2"),new BossSetStage(3,"boss_skoll.stage3"),new BossSetStage(6,"boss_skoll.stage6") },
            BossReward=SkollRewardId,LinkStages=new[]
            {
                new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward,Value=1,Requires=new[] { "Gem_U_GlacialCore" } }),
                new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward,Value=2,Requires=new[] { "Gem_U_GlacialCore" } }),
                new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward,Value=3,Requires=new[] { "Gem_U_GlacialCore" } }),
            },
        } };
    }
}

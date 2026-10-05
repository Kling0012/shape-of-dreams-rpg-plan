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
            SkollProfile("weapon", "通常攻撃の命中時、自身から8m以内の敵の位置に0.25秒後、長さ4m×幅1mの氷刃を放ち、攻撃力・魔力の高い方の28%（上限84）の冷気ダメージを与える。再使用まで5秒。", "When your basic attack hits an enemy within 8m, release an ice blade at that enemy's position after 0.25 seconds. The blade is 4m long and 1m wide, and deals Cold damage equal to 28% of whichever is higher: your Attack or Ability Power (cap: 84). Cooldown: 5 seconds.", new[] { SkollChannel("SkollBlade",28,84) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"SkollBlade",BossShape.Line,BossAnchor.Hit,cooldownMillis:5000,delayMillis:250,rangeMilli:4000,widthMilli:1000,magnitudeMilli:8000,element:BossElement.Cold)),
            SkollProfile("armor", "ダメージを受けたとき、その地点に0.40秒後、半径2mの氷刃を放ち、攻撃力・魔力の高い方の25%（上限75）の冷気ダメージを与える。再使用まで7秒。", "When you take damage, release an ice blade at that position after 0.40 seconds, dealing Cold damage equal to 25% of whichever is higher: your Attack or Ability Power (cap: 75), in a 2m radius. Cooldown: 7 seconds.", new[] { SkollChannel("SkollCounter",25,75) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Field,BossPayload.Damage,"SkollCounter",cooldownMillis:7000,delayMillis:400,radiusMilli:2000,element:BossElement.Cold)),
            SkollProfile("charm", "通常記憶または奥義記憶を使ったとき、8m以内の指定地点に氷の矢を放つ。0.45秒後に半径1.8mへ攻撃力・魔力の高い方の18%（上限54）の冷気ダメージを与え、その後0.5秒ごとに3回、各6%（上限18）の冷気ダメージを与える氷の領域（持続1.5秒）を生成する。再使用まで8秒。", "When you use a normal or ultimate memory, fire an ice arrow at a selected point within 8m. After 0.45 seconds, it deals Cold damage equal to 18% of whichever is higher: your Attack or Ability Power (cap: 54), in a 1.8m radius. It then creates an icy field lasting 1.5 seconds that deals Cold damage 3 times, every 0.5 seconds, each equal to 6% of whichever is higher: your Attack or Ability Power (cap: 18). Cooldown: 8 seconds.", new[] { SkollChannel("SkollArrow",18,54),SkollChannel("SkollArrowTick",6,18) },
                SkollArrow(BossEvent.MemoryUse,"SkollArrow",450,1800,8000),SkollArrowTicks("SkollArrowTick",950,1800)),
            SkollProfile("head", "6秒以内に通常攻撃を3回命中させると、敵の位置を中心に横一列（間隔1.5m）の3箇所に、0.50秒・0.75秒・1.00秒後にそれぞれ半径0.9mへ攻撃力・魔力の高い方の12%（上限36）の冷気ダメージを与える。再使用まで8秒。", "After 3 basic attack hits within 6 seconds, strike 3 points in a horizontal row centered on the enemy's position, spaced 1.5m apart. The strikes occur after 0.50, 0.75, and 1.00 seconds, respectively, each dealing Cold damage equal to 12% of whichever is higher: your Attack or Ability Power (cap: 36), in a 0.9m radius. Cooldown: 8 seconds.", new[] { SkollChannel("SkollRow",12,36) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"SkollRow",BossShape.Circle,BossAnchor.Hit,cooldownMillis:8000,delayMillis:500,intervalMillis:250,count:3,maxInstances:1,mainHits:3,counterLifetimeMillis:6000,radiusMilli:900,widthMilli:1500,element:BossElement.Cold)),
            SkollProfile("hands", "6秒以内に通常攻撃を4回命中させると、前方4m・角度60度の扇状範囲に0.25秒の予告後、攻撃力・魔力の高い方の30%（上限90）の冷気ダメージを与えて1mノックバックさせる（ボスと行動妨害が無効な敵は移動しない）。再使用まで6秒。", "After 4 basic attack hits within 6 seconds, mark a 60-degree cone extending 4m in front of you. After a 0.25-second warning, deal Cold damage equal to 30% of whichever is higher: your Attack or Ability Power (cap: 90), and knock enemies back 1m. Bosses and enemies immune to crowd control are not moved. Cooldown: 6 seconds.", new[] { SkollChannel("SkollFan",30,90) },
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Push,"SkollFan",BossShape.Fan,cooldownMillis:6000,delayMillis:250,mainHits:4,counterLifetimeMillis:6000,rangeMilli:4000,angleMilli:60000,magnitudeMilli:1000,element:BossElement.Cold)),
            SkollProfile("feet", "移動完了時、自身に追従する半径2mの氷旋風を1秒間発生させる。0.25秒ごとに4回、それぞれ攻撃力・魔力の高い方の7%（上限21）の冷気ダメージを与える。再使用まで5秒。", "When you finish moving, create an icy whirlwind with a 2m radius that follows you for 1 second. It deals Cold damage 4 times, every 0.25 seconds, each equal to 7% of whichever is higher: your Attack or Ability Power (cap: 21). Cooldown: 5 seconds.", new[] { SkollChannel("SkollWhirl",7,21) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Field,BossPayload.Damage,"SkollWhirl",cooldownMillis:5000,delayMillis:250,lifetimeMillis:1000,intervalMillis:250,count:4,maxInstances:1,radiusMilli:2000,followOwner:true,element:BossElement.Cold)),
            SkollProfile("stage2", "通常攻撃の命中、または通常記憶・奥義記憶の使用時に自身へ氷印を1つ付与する（最大3個、最後の付与から6秒間持続）。3個溜まった状態で次の通常攻撃を命中させるか記憶を使用すると全消費し、8m以内の対象地点（敵または指定地点）に0.4秒後、長さ5m×幅1mの氷裂きを放ち、攻撃力・魔力の高い方の35%の冷気ダメージを与える。再使用まで4秒。", "When your basic attack hits or you use a normal or ultimate memory, gain 1 ice mark, up to 3. Marks last 6 seconds from the most recent gain. With 3 marks, your next basic attack hit or memory use consumes them all. After 0.4 seconds, release an icy slash 5m long and 1m wide at the target point within 8m (an enemy's position or a selected point), dealing Cold damage equal to 35% of whichever is higher: your Attack or Ability Power. Cooldown: 4 seconds.", new[] { SkollChannel("SkollSeal",1,1,BossCoefficientKind.Stat),SkollChannel("SkollRelease",35,35) },
                new BossAction(BossEvent.MainHit,BossMechanism.Ledger,BossPayload.Mark,"SkollSeal",lifetimeMillis:6000,count:3,ledgerId:"SkollOwnerMarks",element:BossElement.Cold),
                new BossAction(BossEvent.MemoryUse,BossMechanism.Ledger,BossPayload.Mark,"SkollSeal",lifetimeMillis:6000,count:3,ledgerId:"SkollOwnerMarks",element:BossElement.Cold),
                new BossAction(BossEvent.Clock,BossMechanism.Field,BossPayload.Damage,"SkollRelease",BossShape.Line,BossAnchor.Hit,cooldownMillis:4000,delayMillis:400,rangeMilli:5000,widthMilli:1000,magnitudeMilli:8000,element:BossElement.Cold)),
            SkollProfile("stage3", "2部位セット効果の攻撃地点へ氷矢を追加する。0.65秒後に半径2mへ攻撃力・魔力の高い方の15%の冷気ダメージを与え、その後0.5秒ごとに3回、各5%の冷気ダメージを与える領域（持続1.5秒、最大1つ）を生成する。", "Add an ice arrow at the 2-piece set effect's attack point. After 0.65 seconds, it deals Cold damage equal to 15% of whichever is higher: your Attack or Ability Power, in a 2m radius. It then creates a field lasting 1.5 seconds that deals Cold damage 3 times, every 0.5 seconds, each equal to 5% of whichever is higher: your Attack or Ability Power. Only 1 field can exist at a time.", new[] { SkollChannel("SkollSealArrow",15,15),SkollChannel("SkollSealTick",5,5) },
                SkollArrow(BossEvent.Clock,"SkollSealArrow",650,2000),SkollArrowTicks("SkollSealTick",1150,2000)),
            SkollProfile("stage6", "2部位セット効果の発動時、対象地点を囲む半径2mの4箇所へ氷雨を降らせる。0.6秒・0.8秒・1.0秒・1.2秒後にそれぞれ半径1mへ攻撃力・魔力の高い方の22.5%（合計90%/上限100）の冷気ダメージを与える。最終弾の発生時（1.2秒後）、敵への命中を問わず自身に2秒間、攻撃力・魔力の高い方の38%（上限40）の障壁を付与する（重複不可）。再使用まで10秒。", "When the 2-piece set effect activates, rain ice onto 4 points arranged 2m around the target point. After 0.6, 0.8, 1.0, and 1.2 seconds, respectively, each strike deals Cold damage equal to 22.5% of whichever is higher: your Attack or Ability Power (90% total; cap: 100), in a 1m radius. When the final strike occurs at 1.2 seconds, gain a non-stacking shield equal to 38% of whichever is higher: your Attack or Ability Power (cap: 40), for 2 seconds, whether or not an enemy was hit. Cooldown: 10 seconds.", new[] { SkollChannel("SkollRain",90,100),SkollChannel("SkollRainShield",38,40,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.Clock,BossMechanism.Field,BossPayload.Damage,"SkollRain",cooldownMillis:10000,delayMillis:600,intervalMillis:200,count:4,maxInstances:1,radiusMilli:1000,rangeMilli:2000,element:BossElement.Cold),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"SkollRainShield",delayMillis:1200,lifetimeMillis:2000,maxInstances:1,element:BossElement.Cold)),
        };

        internal static BossRewardProfile[] CreateSkollRewards()
        {
            BossRewardAction Heal() => new BossRewardAction(BossRewardActionKind.NativeHeal,20000,20000,cooldownMillis:2000,order:0,magnitudeMilli:12000);
            BossRewardAction Target() => new BossRewardAction(BossRewardActionKind.NativeTarget,durationMillis:2000,order:1);
            return new[] { new BossRewardProfile(SkollRewardId,SkollSetId,"Gem_U_GlacialCore",BossRewardAdapter.GlacialCore,new[]
            {
                new BossRewardStage(1,new Txt("装備している核による冷気属性の回復効果のみ+20%（増加上限：攻撃力・魔力の高い方の12%）。再使用まで2秒。", "Increase only the Cold healing provided by your equipped core by 20%. The added healing is capped at 12% of whichever is higher: your Attack or Ability Power. Cooldown: 2 seconds."),new[] { Heal() }),
                new BossRewardStage(2,new Txt("段階1の効果に加え、冷気回復のトリガーとなった敵を2秒間記録する。装備している雪嶺の核が放つ次の弾丸は、射程内にいる場合その敵を優先して狙う（プレイヤー本体の射撃ではない）。", "In addition to stage 1, remember the enemy that triggered the Cold healing for 2 seconds. The next projectile fired by your equipped Snowcrest Core prioritizes that enemy if it is within range. This affects the core's projectile, not your own shooting."),new[] { Heal(),Target() }),
                new BossRewardStage(3,new Txt("段階2の効果に加え、冷気回復後の1秒間、装備している雪嶺の核が蓄積ダメージを消費して放つ弾丸の発射間隔（最大3発）を最短0.10秒に短縮する（プレイヤー本体の攻撃速度ではなく、蓄積や射程内の敵がない場合は加速しない）。再使用まで8秒。", "In addition to stage 2, for 1 second after Cold healing, shorten the firing interval of up to 3 projectiles that your equipped Snowcrest Core fires by consuming stored damage, to as little as 0.10 seconds. This does not change your own attack speed, and does not accelerate firing without stored damage or an enemy within range. Cooldown: 8 seconds."),new[] { Heal(),Target(),new BossRewardAction(BossRewardActionKind.NativeInterval,cooldownMillis:8000,durationMillis:1000,count:3,order:2,intervalMillis:100) }),
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

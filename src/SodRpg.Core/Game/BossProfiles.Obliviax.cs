using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string ObliviaxSetId = "set.boss_obliviax";
        public const string ObliviaxRewardId = "boss_obliviax.shout_of_oblivion";
        public const string ObliviaxEventOrder = "verified-departure-token;existing-turret-separate-activation;ambush-consume-place-one-turret;independent-parts;fixed-three-artillery-and-shield;voluntary-quiet-observation";
        public const string ObliviaxNativeContract = "Mon_Special_BossObliviax;St_U_ShoutOfOblivion;own-type-parent-activation-epoch-life-room-run;native-hunt-clamp0-5-plus03-per-level;own-OnCreate-backstep4-to5-legal-duration04-terrain-false;own-OnHit-native-stun-plus04-once-target-activation-nonboss-nonimmune;native-crit-proc-locks-camera-invulnerability-unchanged;nonentity-turret1-life3-shot1;ambush-token1-life3;generated-forced-movement-excluded;no-travel-kidnap-no-native-summon-no-reward-route-change";

        private static BossMoveProfile ObliviaxProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_obliviax." + suffix, ObliviaxSetId, new Txt(ja, en), channels, actions);
        private static BossAction ObliviaxCircle(BossEvent kind, string channel, int cooldown, int radius, int delay = 0, int range = 0)
            => new BossAction(kind, BossMechanism.ShapeAttack, BossPayload.Damage, channel, cooldownMillis: cooldown,
                delayMillis: delay, maxTargets: 8, maxInstances: 1, radiusMilli: radius, rangeMilli: range, element: BossElement.Dark);
        private static BossAction ObliviaxShield(string channel, int cooldown, int lifetime)
            => new BossAction(BossEvent.MemoryUse, BossMechanism.Defense, BossPayload.Shield, channel,
                cooldownMillis: cooldown, lifetimeMillis: lifetime, maxTargets: 1, maxInstances: 1, followOwner: true);

        internal static BossMoveProfile[] CreateObliviaxMoves() => new[]
        {
            ObliviaxProfile("weapon", "通常攻撃の命中時、主対象の方向へ幅1.5m・長さ4mの直線状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える。再使用まで3秒（自身は移動しない）。", "When your basic attack hits, deal Dark damage equal to 18% of whichever is higher: your Attack or Ability Power, in a line 1.5m wide and 4m long toward the main target. This does not move you. Cooldown: 3 seconds.",
                new[] { C("ObliviaxBlades",18,18) }, new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxBlades",BossShape.Line,cooldownMillis:3000,maxTargets:8,maxInstances:1,rangeMilli:4000,widthMilli:1500,element:BossElement.Dark)),
            ObliviaxProfile("armor", "記憶を使ったとき、自身に2秒間、攻撃力・魔力の高い方の18%分の障壁を付与する。再使用まで8秒（透明化や無敵、行動制限は付与されない）。", "When you use a memory, gain a shield equal to 18% of whichever is higher: your Attack or Ability Power, for 2 seconds. This does not grant invisibility or invulnerability, or restrict your actions. Cooldown: 8 seconds.",
                new[] { C("ObliviaxMantle",18,18,BossCoefficientKind.Shield) }, ObliviaxShield("ObliviaxMantle",8000,2000)),
            ObliviaxProfile("charm", "記憶を使ったとき、自身から8m以内の最も近い敵の位置に0.6秒の予告後、半径1.5mに攻撃力・魔力の高い方の24%の闇属性ダメージを与える。ボスと行動妨害が効かない敵以外の敵に0.5秒間、20%の移動速度低下（スロウ）を付与する。再使用まで6秒。", "When you use a memory, mark the nearest enemy's position within 8m. After a 0.6-second warning, deal Dark damage equal to 24% of whichever is higher: your Attack or Ability Power, in a 1.5m radius. Slow affected enemies by 20% for 0.5 seconds, except bosses and enemies immune to crowd control. Cooldown: 6 seconds.",
                new[] { C("ObliviaxArtillery",24,24) }, ObliviaxCircle(BossEvent.MemoryUse,"ObliviaxArtillery",6000,1500,600,8000)),
            ObliviaxProfile("head", "1秒間の自発的な移動距離が1m未満だった場合（強制移動を除く）、次の通常攻撃命中時に主対象の位置へ闇の球を0.2秒間隔で2発発射する（射程8m、持続1秒、爆発半径1m）。それぞれ攻撃力・魔力の高い方の9%の闇属性ダメージを与える。再使用まで4秒（自発的に移動すると待機状態が解除される）。", "If you voluntarily move less than 1m over 1 second, your next basic attack hit fires 2 Dark orbs at the main target's position, 0.2 seconds apart. Forced movement does not count. Each orb has an 8m range, lasts 1 second, and explodes in a 1m radius for Dark damage equal to 9% of whichever is higher: your Attack or Ability Power. Voluntary movement cancels this ready effect. Cooldown: 4 seconds.",
                new[] { C("ObliviaxTurretOrbs",18,18) }, new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"ObliviaxTurretOrbs",cooldownMillis:4000,lifetimeMillis:1000,intervalMillis:200,count:2,maxTargets:8,maxInstances:1,radiusMilli:1000,rangeMilli:8000,widthMilli:400,speedMilli:12000,firstHitOnly:true,element:BossElement.Dark),
                new BossAction(BossEvent.Clock,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:1000,maxTargets:1,maxInstances:1,magnitudeMilli:1000)),
            ObliviaxProfile("hands", "通常攻撃の命中時、自身の周囲半径2mに攻撃力・魔力の高い方の18%の闇属性ダメージを与える。ボスと行動妨害が効かない敵以外の敵に0.25秒間のスタンを付与する。再使用まで6秒。", "When your basic attack hits, deal Dark damage equal to 18% of whichever is higher: your Attack or Ability Power, in a 2m radius around you. Stun affected enemies for 0.25 seconds, except bosses and enemies immune to crowd control. Cooldown: 6 seconds.",
                new[] { C("ObliviaxNeedles",18,18) }, ObliviaxCircle(BossEvent.MainHit,"ObliviaxNeedles",6000,2000)),
            ObliviaxProfile("feet", "移動完了時、着地点から3m以内の最も近い敵1体に攻撃力・魔力の高い方の12%の闇属性ダメージを与える。ボスと行動妨害が効かない敵以外の敵を、0.25秒かけて自身側へ最大1m引き寄せる。再使用まで6秒。", "When you finish moving, deal Dark damage equal to 12% of whichever is higher: your Attack or Ability Power, to the nearest enemy within 3m of your arrival point. Pull that enemy up to 1m toward you over 0.25 seconds, except bosses and enemies immune to crowd control. Cooldown: 6 seconds.",
                new[] { C("ObliviaxCapture",12,12) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.EnemyMovement,BossPayload.Pull,"ObliviaxCapture",cooldownMillis:6000,lifetimeMillis:250,maxTargets:1,maxInstances:1,rangeMilli:3000,magnitudeMilli:1000,element:BossElement.Dark)),
            ObliviaxProfile("stage2", "移動完了時、その出発点の位置を3秒間記録する（再使用まで4秒）。次の通常攻撃命中時にその記録を消費し、出発点から主対象の方向へ幅1.5m・長さ8mの直線状に攻撃力・魔力の高い方の20%の闇属性ダメージを1回与える。", "When you finish moving, remember your departure point for 3 seconds. Cooldown: 4 seconds. Your next basic attack hit uses up the remembered point to strike once from there toward the main target in a line 1.5m wide and 8m long, dealing Dark damage equal to 20% of whichever is higher: your Attack or Ability Power.",
                new[] { C("ObliviaxAmbush",20,20) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,cooldownMillis:4000,lifetimeMillis:3000,maxTargets:1,maxInstances:1,ledgerId:"ObliviaxAmbush"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxAmbush",BossShape.Line,maxTargets:8,maxInstances:1,rangeMilli:8000,widthMilli:1500,element:BossElement.Dark)),
            ObliviaxProfile("stage3", "待ち伏せ線撃の発動時、その起点に残影の砲座を1体設置する（持続3秒、装弾数1発、召喚扱いなし）。その後の別の通常攻撃命中時、砲座から8m以内の主対象の位置に0.4秒の予告後、半径1.5mに攻撃力・魔力の高い方の15%の闇属性ダメージを与えて砲座が消滅する（新たに設置すると古い砲座は消滅する）。", "When the ambush line attack activates, place an afterimage turret at its origin. It lasts 3 seconds, holds 1 shot, and does not count as a summon. When a later, separate basic attack hits a main target within 8m of the turret, mark that target's position. After a 0.4-second warning, deal Dark damage equal to 15% of whichever is higher: your Attack or Ability Power, in a 1.5m radius, then remove the turret. Placing a new turret removes the old one.",
                new[] { C("ObliviaxAfterimage",15,15) }, new BossAction(BossEvent.MainHit,BossMechanism.Deployable,BossPayload.Deploy,lifetimeMillis:3000,maxTargets:1,maxInstances:1,ledgerId:"ObliviaxAfterimage"),
                ObliviaxCircle(BossEvent.MainHit,"ObliviaxAfterimage",0,1500,400,8000)),
            ObliviaxProfile("stage6", "記憶を使ったとき、自身から8m以内の最も近い敵の位置、およびその敵の位置から左右2mの計3地点に予告を行う。0.6秒後・0.8秒後・1.0秒後に半径1.5mへそれぞれ攻撃力・魔力の高い方の30%の闇属性ダメージを与え（合計90%、上限100%）、自身に2.5秒間、攻撃力・魔力の高い方の38%分の障壁を付与する（上限40%）。再使用まで10秒。", "When you use a memory, mark 3 points: the nearest enemy's position within 8m, and points 2m to its left and right. After 0.6, 0.8, and 1.0 seconds, respectively, strike each point in a 1.5m radius for Dark damage equal to 30% of whichever is higher: your Attack or Ability Power (90% total; cap: 100%). Also gain a shield equal to 38% of whichever is higher: your Attack or Ability Power (cap: 40%), for 2.5 seconds. Cooldown: 10 seconds.",
                new[] { C("ObliviaxBarrage",90,100), C("ObliviaxBarrageShield",38,40,BossCoefficientKind.Shield) }, new BossAction(BossEvent.MemoryUse,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxBarrage",cooldownMillis:10000,delayMillis:600,intervalMillis:200,count:3,maxTargets:8,maxInstances:1,radiusMilli:1500,rangeMilli:8000,magnitudeMilli:2000,element:BossElement.Dark),
                ObliviaxShield("ObliviaxBarrageShield",0,2500)),
        };
        internal static BossRewardProfile[] CreateObliviaxRewards()
        {
            var hunt = new BossRewardAction(BossRewardActionKind.NativeDamage,30,150);
            var step = new BossRewardAction(BossRewardActionKind.NativeState,1000,1000,durationMillis:400,rangeMilli:5000,order:1);
            var stun = new BossRewardAction(BossRewardActionKind.NativeState,400,400,order:2);
            return new[] { new BossRewardProfile(ObliviaxRewardId,ObliviaxSetId,"St_U_ShoutOfOblivion",BossRewardAdapter.ShoutOfOblivion,new[]
            {
                new BossRewardStage(1,new Txt("自身のシャウトスキルの狩猟倍率に、現在の狩猟レベル（0〜5）1ごとに0.03を加算する（最大ボーナス+0.15、狩猟レベル0では加算なし。会心発生条件や他の記憶は変化しない）。", "Increase your Shout skill's hunting multiplier by 0.03 per current hunting level (0–5), up to a +0.15 bonus. Hunting level 0 grants no bonus. Critical hit conditions and other memories remain unchanged."),new[] { hunt }),
                new BossRewardStage(2,new Txt("2部位効果に加えて、シャウトスキルの後退距離を4mから5mへ延長する（追加上限1m。所要時間0.4秒、地形を貫通しない挙動は維持される）。", "In addition to the 2-piece effect, extend your Shout skill's backward movement from 4m to 5m, adding at most 1m. It still takes 0.4 seconds and cannot pass through terrain."),new[] { hunt,step }),
                new BossRewardStage(3,new Txt("4部位効果に加えて、シャウトスキル命中時のスタン時間を0.4秒延長する（ボスと行動妨害が効かない敵を除く敵に対し、1回の発動につき1体あたり1回のみ有効。無敵時間は延長されない）。", "In addition to the 4-piece effect, extend the stun inflicted by your Shout skill by 0.4 seconds. This applies once per enemy per use, except bosses and enemies immune to crowd control. Invulnerability duration is not extended."),new[] { hunt,step,stun }),
            }) };
        }
        public static UniqueDef[] CreateObliviaxPieces() => new[]
        {
            new UniqueDef("unique.boss_obliviax.weapon","weapon.twin_rapier",new Txt("忘針の連刃","Oblivion Needleblade"),ObliviaxSetId,"boss_obliviax.weapon"),
            new UniqueDef("unique.boss_obliviax.armor","armor.shadow_cloak",new Txt("巣影の外套","Nestshadow Mantle"),ObliviaxSetId,"boss_obliviax.armor"),
            new UniqueDef("unique.boss_obliviax.charm","charm.ink_stone",new Txt("忘却の喉石","Oblivion Throatstone"),ObliviaxSetId,"boss_obliviax.charm"),
            new UniqueDef("unique.boss_obliviax.head","head.void_helm",new Txt("砲座の面","Turret Mask"),ObliviaxSetId,"boss_obliviax.head"),
            new UniqueDef("unique.boss_obliviax.hands","hands.shadow_gloves",new Txt("針継ぎの手袋","Needlestitch Gloves"),ObliviaxSetId,"boss_obliviax.hands"),
            new UniqueDef("unique.boss_obliviax.feet","feet.shadow_slippers",new Txt("攫い影の靴","Abducting Shadow Boots"),ObliviaxSetId,"boss_obliviax.feet"),
        };
        public static SetDef[] CreateObliviaxSets() => new[]
        {
            new SetDef { Id=ObliviaxSetId,Name=new Txt("忘針の襲装","Oblivion Needle Gear"),BossTypeName="Mon_Special_BossObliviax",BossReward=ObliviaxRewardId,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_obliviax.stage2"),new BossSetStage(3,"boss_obliviax.stage3"),new BossSetStage(6,"boss_obliviax.stage6") },
                LinkStages=new[] { new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward,Value=1,Requires=new[] { "St_U_ShoutOfOblivion" } }),new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward,Value=2,Requires=new[] { "St_U_ShoutOfOblivion" } }),new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward,Value=3,Requires=new[] { "St_U_ShoutOfOblivion" } }) } },
        };
    }
}

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
            ObliviaxProfile("weapon", "通常攻撃の命中時、主対象の方向へ幅1.5m・長さ4mの直線状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える。再使用まで3秒（自身は移動しない）。", "Main hit: one Dark line18%H toward the main target,width1.5m/length4m,CD3s; no owner movement.",
                new[] { C("ObliviaxBlades",18,18) }, new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxBlades",BossShape.Line,cooldownMillis:3000,maxTargets:8,maxInstances:1,rangeMilli:4000,widthMilli:1500,element:BossElement.Dark)),
            ObliviaxProfile("armor", "記憶を使ったとき、自身に2秒間、攻撃力・魔力の高い方の18%分の障壁を付与する。再使用まで8秒（透明化や無敵、行動制限は付与されない）。", "Confirmed memory use: owner shield18%H/2s,CD8s; no invisibility,invulnerability or input lock.",
                new[] { C("ObliviaxMantle",18,18,BossCoefficientKind.Shield) }, ObliviaxShield("ObliviaxMantle",8000,2000)),
            ObliviaxProfile("charm", "記憶を使ったとき、自身から8m以内の最も近い敵の位置に0.6秒の予告後、半径1.5mに攻撃力・魔力の高い方の24%の闇属性ダメージを与える。ボスと行動妨害が効かない敵以外の敵に0.5秒間、20%の移動速度低下（スロウ）を付与する。再使用まで6秒。", "Confirmed memory use: freeze nearest enemy point within8m,telegraph0.6s then radius1.5m Dark24%H; slow20%/0.5s only non-boss/non-CC-immune enemies,CD6s.",
                new[] { C("ObliviaxArtillery",24,24) }, ObliviaxCircle(BossEvent.MemoryUse,"ObliviaxArtillery",6000,1500,600,8000)),
            ObliviaxProfile("head", "1秒間の自発的な移動距離が1m未満だった場合（強制移動を除く）、次の通常攻撃命中時に主対象の位置へ闇の球を0.2秒間隔で2発発射する（射程8m、持続1秒、爆発半径1m）。それぞれ攻撃力・魔力の高い方の9%の闇属性ダメージを与える。再使用まで4秒（自発的に移動すると待機状態が解除される）。", "After observing less than1m voluntary movement over1s,next main hit fires2 Dark orbs at the frozen main-target point,9%H each,0.2s apart,range8m/life1s/explosion radius1m,CD4s. Voluntary movement clears readiness; forced/generated movement is excluded.",
                new[] { C("ObliviaxTurretOrbs",18,18) }, new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"ObliviaxTurretOrbs",cooldownMillis:4000,lifetimeMillis:1000,intervalMillis:200,count:2,maxTargets:8,maxInstances:1,radiusMilli:1000,rangeMilli:8000,widthMilli:400,speedMilli:12000,firstHitOnly:true,element:BossElement.Dark),
                new BossAction(BossEvent.Clock,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:1000,maxTargets:1,maxInstances:1,magnitudeMilli:1000)),
            ObliviaxProfile("hands", "通常攻撃の命中時、自身の周囲半径2mに攻撃力・魔力の高い方の18%の闇属性ダメージを与える。ボスと行動妨害が効かない敵以外の敵に0.25秒間のスタンを付与する。再使用まで6秒。", "Main hit: owner-centered radius2m Dark18%H,stun0.25s only non-boss/non-CC-immune enemies,CD6s; no HP-percent damage or skill cooldown reset.",
                new[] { C("ObliviaxNeedles",18,18) }, ObliviaxCircle(BossEvent.MainHit,"ObliviaxNeedles",6000,2000)),
            ObliviaxProfile("feet", "移動完了時、着地点から3m以内の最も近い敵1体に攻撃力・魔力の高い方の12%の闇属性ダメージを与える。ボスと行動妨害が効かない敵以外の敵を、0.25秒かけて自身側へ最大1m引き寄せる。再使用まで6秒。", "Native movement completion: nearest enemy within3m of arrival takes Dark12%H; pull at most1m/0.25s toward owner only if non-boss/non-CC-immune,CD6s; no ally,travel or input changes.",
                new[] { C("ObliviaxCapture",12,12) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.EnemyMovement,BossPayload.Pull,"ObliviaxCapture",cooldownMillis:6000,lifetimeMillis:250,maxTargets:1,maxInstances:1,rangeMilli:3000,magnitudeMilli:1000,element:BossElement.Dark)),
            ObliviaxProfile("stage2", "移動完了時、その出発点の位置を3秒間記録する（再使用まで4秒）。次の通常攻撃命中時にその記録を消費し、出発点から主対象の方向へ幅1.5m・長さ8mの直線状に攻撃力・魔力の高い方の20%の闇属性ダメージを1回与える。", "Verified native movement departure grants one3s ambush token,CD4s. Next main hit consumes it for one Dark20%H line from departure toward main target,width1.5m/length8m; generated movement and enemy pulls grant nothing.",
                new[] { C("ObliviaxAmbush",20,20) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,cooldownMillis:4000,lifetimeMillis:3000,maxTargets:1,maxInstances:1,ledgerId:"ObliviaxAmbush"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxAmbush",BossShape.Line,maxTargets:8,maxInstances:1,rangeMilli:8000,widthMilli:1500,element:BossElement.Dark)),
            ObliviaxProfile("stage3", "待ち伏せ線撃の発動時、その起点に残影の砲座を1体設置する（持続3秒、装弾数1発、召喚扱いなし）。その後の別の通常攻撃命中時、砲座から8m以内の主対象の位置に0.4秒の予告後、半径1.5mに攻撃力・魔力の高い方の15%の闇属性ダメージを与えて砲座が消滅する（新たに設置すると古い砲座は消滅する）。", "Ambush leaves one non-Entity afterimage turret at its origin for3s,one shot. A later separate-activation main hit freezes its main-target point within8m of turret,telegraphs0.4s then radius1.5m Dark15%H,consuming turret. New placement replaces old; no summon procs or loot.",
                new[] { C("ObliviaxAfterimage",15,15) }, new BossAction(BossEvent.MainHit,BossMechanism.Deployable,BossPayload.Deploy,lifetimeMillis:3000,maxTargets:1,maxInstances:1,ledgerId:"ObliviaxAfterimage"),
                ObliviaxCircle(BossEvent.MainHit,"ObliviaxAfterimage",0,1500,400,8000)),
            ObliviaxProfile("stage6", "記憶を使ったとき、自身から8m以内の最も近い敵の位置、およびその敵の位置から左右2mの計3地点に予告を行う。0.6秒後・0.8秒後・1.0秒後に半径1.5mへそれぞれ攻撃力・魔力の高い方の30%の闇属性ダメージを与え（合計90%、上限100）、自身に2.5秒間、攻撃力・魔力の高い方の38%分の障壁を付与する（上限40）。再使用まで10秒。", "Confirmed memory use freezes nearest enemy point within8m and two points2m left/right of owner's firing angle; radius1.5m Dark30%H each at0.6/0.8/1.0s(A90/cap100),plus owner shield38%H/2.5s(B38/cap40),CD10s; no player scaling,tracking or summon requirement.",
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
                new BossRewardStage(1,new Txt("自身のシャウトスキルの狩猟倍率に、現在の狩猟レベル（0〜5）1ごとに0.03を加算する（最大ボーナス+0.15、狩猟レベル0では加算なし。会心発生条件や他の記憶は変化しない）。", "Own native Shout hunt amplification adds0.03 per native-clamped0–5 level,bonus at most0.15,zero at level0. Preserve native critical condition,all other memories and actual hunt level."),new[] { hunt }),
                new BossRewardStage(2,new Txt("2部位効果に加えて、シャウトスキルの後退距離を4mから5mへ延長する（追加上限1m。所要時間0.4秒、地形を貫通しない挙動は維持される）。", "2-piece plus only Shout's own native backstep4m→5m(at most1m added),preserving0.4s,no terrain crossing and legal sweep."),new[] { hunt,step }),
                new BossRewardStage(3,new Txt("4部位効果に加えて、シャウトスキル命中時のスタン時間を0.4秒延長する（ボスと行動妨害が効かない敵を除く敵に対し、1回の発動につき1体あたり1回のみ有効。無敵時間は延長されない）。", "4-piece plus only Shout's native hit stun gains0.4s over its real duration,once per target per activation,non-boss/non-CC-immune only. Never extend cast or post-completion invulnerability."),new[] { hunt,step,stun }),
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

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
            ObliviaxProfile("weapon", "主撃で主対象方向へ幅1.5m/長さ4mの闇線撃18%H、CD3秒。本人移動なし。", "Main hit: one Dark line18%H toward the main target,width1.5m/length4m,CD3s; no owner movement.",
                new[] { C("ObliviaxBlades",18,18) }, new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxBlades",BossShape.Line,cooldownMillis:3000,maxTargets:8,maxInstances:1,rangeMilli:4000,widthMilli:1500,element:BossElement.Dark)),
            ObliviaxProfile("armor", "記憶使用確定で本人shield18%H/2秒、CD8秒。不可視/無敵/入力拘束なし。", "Confirmed memory use: owner shield18%H/2s,CD8s; no invisibility,invulnerability or input lock.",
                new[] { C("ObliviaxMantle",18,18,BossCoefficientKind.Shield) }, ObliviaxShield("ObliviaxMantle",8000,2000)),
            ObliviaxProfile("charm", "記憶使用確定で8m内最寄り敵点を0.6秒予告、半径1.5m闇24%H。非boss/CC非免疫のみslow20%/0.5秒、CD6秒。", "Confirmed memory use: freeze nearest enemy point within8m,telegraph0.6s then radius1.5m Dark24%H; slow20%/0.5s only non-boss/non-CC-immune enemies,CD6s.",
                new[] { C("ObliviaxArtillery",24,24) }, ObliviaxCircle(BossEvent.MemoryUse,"ObliviaxArtillery",6000,1500,600,8000)),
            ObliviaxProfile("head", "本人自発移動1m未満/1秒を確認した次主撃で主対象確定点へ闇球2発、各9%H、間隔0.2秒/射程8m/寿命1秒/爆発半径1m、CD4秒。自発移動で待機解除、強制/生成移動は除外。", "After observing less than1m voluntary movement over1s,next main hit fires2 Dark orbs at the frozen main-target point,9%H each,0.2s apart,range8m/life1s/explosion radius1m,CD4s. Voluntary movement clears readiness; forced/generated movement is excluded.",
                new[] { C("ObliviaxTurretOrbs",18,18) }, new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"ObliviaxTurretOrbs",cooldownMillis:4000,lifetimeMillis:1000,intervalMillis:200,count:2,maxTargets:8,maxInstances:1,radiusMilli:1000,rangeMilli:8000,widthMilli:400,speedMilli:12000,firstHitOnly:true,element:BossElement.Dark),
                new BossAction(BossEvent.Clock,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:1000,maxTargets:1,maxInstances:1,magnitudeMilli:1000)),
            ObliviaxProfile("hands", "主撃で本人周囲半径2mに闇18%H、非boss/CC非免疫のみstun0.25秒、CD6秒。HP割合damage/技能CD resetなし。", "Main hit: owner-centered radius2m Dark18%H,stun0.25s only non-boss/non-CC-immune enemies,CD6s; no HP-percent damage or skill cooldown reset.",
                new[] { C("ObliviaxNeedles",18,18) }, ObliviaxCircle(BossEvent.MainHit,"ObliviaxNeedles",6000,2000)),
            ObliviaxProfile("feet", "本人native移動完了で到着点3m内最寄り敵1体へ闇12%H、非boss/CC非免疫のみ本人側へ最大1m/0.25秒pull、CD6秒。味方/旅先/入力は不変。", "Native movement completion: nearest enemy within3m of arrival takes Dark12%H; pull at most1m/0.25s toward owner only if non-boss/non-CC-immune,CD6s; no ally,travel or input changes.",
                new[] { C("ObliviaxCapture",12,12) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.EnemyMovement,BossPayload.Pull,"ObliviaxCapture",cooldownMillis:6000,lifetimeMillis:250,maxTargets:1,maxInstances:1,rangeMilli:3000,magnitudeMilli:1000,element:BossElement.Dark)),
            ObliviaxProfile("stage2", "本人native移動完了の検証済み出発点を3秒/1token記録、CD4秒。次主撃で起点から主対象方向へ幅1.5m/長さ8mの闇待伏せ線撃20%Hを1回、token消費。生成移動/敵pullは記録しない。", "Verified native movement departure grants one3s ambush token,CD4s. Next main hit consumes it for one Dark20%H line from departure toward main target,width1.5m/length8m; generated movement and enemy pulls grant nothing.",
                new[] { C("ObliviaxAmbush",20,20) }, new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,cooldownMillis:4000,lifetimeMillis:3000,maxTargets:1,maxInstances:1,ledgerId:"ObliviaxAmbush"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"ObliviaxAmbush",BossShape.Line,maxTargets:8,maxInstances:1,rangeMilli:8000,widthMilli:1500,element:BossElement.Dark)),
            ObliviaxProfile("stage3", "待伏せ起点に非Entity残影砲座1体/3秒・残弾1。さらに次の別activation主撃で砲座8m内の主対象確定点を0.4秒予告→半径1.5m闇15%H、砲座消失。新設置は旧砲座解除、召喚proc/lootなし。", "Ambush leaves one non-Entity afterimage turret at its origin for3s,one shot. A later separate-activation main hit freezes its main-target point within8m of turret,telegraphs0.4s then radius1.5m Dark15%H,consuming turret. New placement replaces old; no summon procs or loot.",
                new[] { C("ObliviaxAfterimage",15,15) }, new BossAction(BossEvent.MainHit,BossMechanism.Deployable,BossPayload.Deploy,lifetimeMillis:3000,maxTargets:1,maxInstances:1,ledgerId:"ObliviaxAfterimage"),
                ObliviaxCircle(BossEvent.MainHit,"ObliviaxAfterimage",0,1500,400,8000)),
            ObliviaxProfile("stage6", "記憶使用確定で8m内最寄り敵点と本人射角左右2mの計3地点を固定予告、0.6/0.8/1.0秒に半径1.5m闇各30%H（A90/cap100）＋本人shield38%H/2.5秒（B38/cap40）、CD10秒。人数増殖/追従/召喚要求なし。", "Confirmed memory use freezes nearest enemy point within8m and two points2m left/right of owner's firing angle; radius1.5m Dark30%H each at0.6/0.8/1.0s(A90/cap100),plus owner shield38%H/2.5s(B38/cap40),CD10s; no player scaling,tracking or summon requirement.",
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
                new BossRewardStage(1,new Txt("本人native Shout固有hunt倍率へlevel当たり0.03追加、native clamp0–5・bonus最大0.15、level0は0。native会心条件/全記憶倍率/実huntは不変。", "Own native Shout hunt amplification adds0.03 per native-clamped0–5 level,bonus at most0.15,zero at level0. Preserve native critical condition,all other memories and actual hunt level."),new[] { hunt }),
                new BossRewardStage(2,new Txt("2部位＋Shout自身のnative後退だけ4m→5m（追加上限1m）、0.4秒/terrain越え不可/安全掃引維持。", "2-piece plus only Shout's own native backstep4m→5m(at most1m added),preserving0.4s,no terrain crossing and legal sweep."),new[] { hunt,step }),
                new BossRewardStage(3,new Txt("4部位＋Shout自身のnative命中stunだけ実duration+0.4秒、非boss/CC非免疫・敵1体1activation1回。詠唱/complete後の無敵は延長しない。", "4-piece plus only Shout's native hit stun gains0.4s over its real duration,once per target per activation,non-boss/non-CC-immune only. Never extend cast or post-completion invulnerability."),new[] { hunt,step,stun }),
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

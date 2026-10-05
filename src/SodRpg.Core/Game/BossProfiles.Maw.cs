using System;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string MawSetId = "set.boss_maw";
        public const string MawRewardId = "boss_maw.big_chomp";
        public const string MawEventOrder = "native-main-once;freeze-origin-direction-lowhp;independent-parts;shared-three-hit-cycle-expiry4s;stage2-stage3-stage6-independent-cooldowns;frozen-two-fan-total-hp-only-absorb-budget;native-bigchomp-one-afterdelay-slot8s";
        public const string MawNativeContract = "Mon_Special_BossMaw;Ai_U_BigChomp-St_U_BigChomp-owned-native-parent-life-epoch;hostile-alive-nonimmune-onhit-bossweight-runtime-cap3;native-counts-ally-reduction-unchanged;own-original-afterdelay-heal-shield-firsttrigger-cooldown-only;loaded-scaling-duration-minimum-scale-canReceiveCooldown-preserved;no-new-native-lifesteal;Shrine_CallOfTheRavenous-reward-drop-route-untouched;no-native-ai-spawn-no-companion-no-dash-no-invulnerability;generated-excluded";

        private static BossMoveProfile MawProfile(string suffix, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile("boss_maw." + suffix, MawSetId, new Txt(ja, en), channels, actions);
        private static BossAction MawFan(string channel, int cooldown, int range, int angle, BossEvent trigger = BossEvent.MainHit)
            => new BossAction(trigger, BossMechanism.ShapeAttack, BossPayload.Damage, channel, BossShape.Fan,
                cooldownMillis: cooldown, maxTargets: 8, maxInstances: 1, rangeMilli: range, angleMilli: angle, element: BossElement.Dark);
        internal static BossMoveProfile[] CreateMawMoves() => new[]
        {
            MawProfile("weapon", "E1で命中方向の120度/3m扇に闇18%H、CD4秒。dash/無敵なし。", "E1: a120-degree/3m Dark fan toward the hit for18%H,CD4s; no dash or invulnerability.",
                new[] { C("MawBite",18,18) }, MawFan("MawBite",4000,3000,120000)),
            MawProfile("armor", "敵対native実HP被damageで本人盾min(14%H,不足HP×15%)/2秒、CD6秒、非重複・放置rechargeなし。", "Hostile native HP damage taken: owner shield min(14%H,15% missing HP) for2s,CD6s; no stacking or idle recharge.",
                new[] { C("MawConversion",14,14,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Defense,BossPayload.Shield,"MawConversion",cooldownMillis:6000,lifetimeMillis:2000,maxTargets:1,maxInstances:1,magnitudeMilli:15000)),
            MawProfile("charm", "E1の4回目（最大3保存・4秒失効）で本人heal=min(6%H,不足HP×8%)、CD5秒、多体でも1回。", "Every fourth E1 (at most3 stored,expire after4s): one owner heal min(6%H,8% missing HP),CD5s regardless of targets.",
                new[] { C("MawGreatjaw",6,6,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Heal,"MawGreatjaw",cooldownMillis:5000,mainHits:4,counterLifetimeMillis:4000,maxTargets:1,maxInstances:1,magnitudeMilli:8000)),
            MawProfile("head", "E2の確定詠唱方向へ左右2斬波、各闇8%H、幅1m/射程6m/寿命0.6秒、間隔0.15秒、CD6秒、追尾なし。", "E2: two left/right Dark waves in the confirmed cast direction,8%H each,width1m,range6m,life0.6s,0.15s apart,CD6s; no homing.",
                new[] { C("MawStance",16,16) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Projectile,BossPayload.Damage,"MawStance",cooldownMillis:6000,lifetimeMillis:600,maxTargets:8,maxInstances:2,rangeMilli:6000,widthMilli:1000,speedMilli:10000,element:BossElement.Dark),
                new BossAction(BossEvent.Clock,BossMechanism.Projectile,BossPayload.Damage,"MawStance",delayMillis:150,lifetimeMillis:600,maxTargets:8,maxInstances:2,rangeMilli:6000,widthMilli:1000,speedMilli:10000,element:BossElement.Dark)),
            MawProfile("hands", "E1の実敵HPdamage×10%を本人へ吸収、cap4%H/activation、CD4秒。無敵/味方/反射/生成damageからは回復0。", "E1: absorb10% of actual enemy HP damage,cap4%H per activation,CD4s; immune,ally,reflected or generated damage heals nothing.",
                new[] { C("MawDevour",4,4,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Heal,"MawDevour",cooldownMillis:4000,maxTargets:1,maxInstances:1,magnitudeMilli:10000)),
            MawProfile("feet", "native移動完了後2秒以内の次E1に闇18%Hの90度/3m扇、CD5秒、1印のみ。untargetable/追加dashなし。", "The next E1 within2s of native movement completion adds a90-degree/3m Dark fan for18%H,CD5s,one mark only; no untargetability or extra dash.",
                new[] { C("MawShadowbite",18,18) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000,maxTargets:1,maxInstances:1), MawFan("MawShadowbite",5000,3000,90000)),
            MawProfile("stage2", "profile自身のE1を3回数える（最大3、最終成功から4秒失効）。同じ3回目に前方120度/3m扇18%H、CD3秒。本人HP50%以下は前後90度扇へ各9%H配分、部位印不要。", "Independent profile cycle: every third E1,max3,expire4s after the last success. Forward120-degree/3m fan18%H,CD3s; at HP<=50%,replace with front/back90-degree fans9%H each; no part marks required.",
                new[] { C("MawCycleBite",18,18) }, MawFan("MawCycleBite",3000,3000,120000), MawFan("MawCycleBite",0,3000,90000,BossEvent.Clock)),
            MawProfile("stage3", "同じ3回目の命中地点を本人8m内へ固定：半径2.5m煉獄円、0.5秒予告→闇22%H爆発1回、CD5秒。HP50%以下は本人足元へ置換、damage不変。", "On the same third hit: freeze a legal hit point within8m,radius2.5m Purgatory circle;0.5s warning then one22%H Dark explosion,CD5s. At HP<=50%,replace the point with your feet without changing damage.",
                new[] { C("MawPurgatory",22,22) },
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MawPurgatory",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:500,maxTargets:8,maxInstances:1,radiusMilli:2500,rangeMilli:8000,element:BossElement.Dark)),
            MawProfile("stage6", "同じ3回目：左右±30度の120度/3.5m扇へ各闇45%H、0.20秒間隔、CD8秒。HP50%以下は前後90度扇へ置換。2扇の実敵HPdamage合計×45%を本人heal、総cap38%H（damage90/cap100、heal38/cap40）、0damageなら0、超過回復破棄。", "On the same third hit: two120-degree/3.5m Dark fans at±30 degrees,45%H each,0.20s apart,CD8s. HP<=50% replaces them with front/back90-degree fans. Heal45% of their total actual enemy HP damage with one total38%H cap(damage90/cap100,heal38/cap40); zero damage heals zero,overflow discarded.",
                new[] { C("MawRavenousHunt",90,100), C("MawHuntAbsorb",38,40,BossCoefficientKind.Heal) },
                MawFan("MawRavenousHunt",8000,3500,120000), MawFan("MawRavenousHunt",0,3500,90000,BossEvent.Clock))
        };
        internal static BossRewardProfile[] CreateMawRewards()
        {
            var heal = new BossRewardAction(BossRewardActionKind.NativeHeal,8000,8000,cooldownMillis:8000,count:3,magnitudeMilli:20000);
            var shield = new BossRewardAction(BossRewardActionKind.NativeShield,10000,10000,cooldownMillis:8000,count:3,order:1,magnitudeMilli:20000);
            var cooldown = new BossRewardAction(BossRewardActionKind.NativeState,500,500,cooldownMillis:8000,count:3,order:2,magnitudeMilli:250);
            return new[] { new BossRewardProfile(MawRewardId,MawSetId,"St_U_BigChomp",BossRewardAdapter.BigChomp,new[]
            {
                new BossRewardStage(1,new Txt("本人native Big Chompの敵対alive/非immune OnHitだけのboss重みwをcap3で記録。1回の元OnAfterDelayの本人Healへmin(8%H,元healPerHit実値×w×20%)加算、共有CD8秒。本体の味方hit/count不変。", "Own native Big Chomp: record only hostile alive/nonimmune OnHit boss-weighted w,cap3. Add min(8%H,loaded healPerHit×w×20%) to the original owner Heal in one OnAfterDelay,shared CD8s; native ally hits/counts unchanged."),new[] { heal }),
                new BossRewardStage(2,new Txt("2部位効果＋同じ元GiveShieldへmin(10%H,元shieldPerHit実値×w×20%)加算。元duration維持、同instance/共有8秒枠で1回。", "2-piece effect plus min(10%H,loaded shieldPerHit×w×20%) on the same original GiveShield; preserve duration,once per instance/shared8s slot."),new[] { heal,shield }),
                new BossRewardStage(3,new Txt("4部位効果＋同instance自身firstTriggerへの元CD短縮へmin(0.50秒,w×0.25秒)追加、元量0なら0。native minimum/scale/canReceiveCooldown維持、同8秒枠。別記憶/新lifestealなし。", "4-piece effects plus min(0.50s,w×0.25s) on the same instance's original firstTrigger cooldown reduction; zero native amount gets zero extra. Preserve native minimum,scale and canReceiveCooldown in the same8s slot; no other-memory buff or new lifesteal."),new[] { heal,shield,cooldown })
            }) };
        }
        public static UniqueDef[] CreateMawPieces() => new[]
        {
            new UniqueDef("unique.boss_maw.weapon","weapon.shadowfang_knife",new Txt("飢影の牙刃","Ravenous Fangblade"),MawSetId,"boss_maw.weapon"),
            new UniqueDef("unique.boss_maw.armor","armor.hunter_leather",new Txt("煉獄の革鎧","Purgatory Leathers"),MawSetId,"boss_maw.armor"),
            new UniqueDef("unique.boss_maw.charm","charm.hunter_tooth",new Txt("大顎の護符","Greatjaw Talisman"),MawSetId,"boss_maw.charm"),
            new UniqueDef("unique.boss_maw.head","head.nightmare_visage",new Txt("飢えの面","Hunger Mask"),MawSetId,"boss_maw.head"),
            new UniqueDef("unique.boss_maw.hands","hands.shadow_gloves",new Txt("貪りの爪","Devouring Claws"),MawSetId,"boss_maw.hands"),
            new UniqueDef("unique.boss_maw.feet","feet.shadow_slippers",new Txt("影歩きの靴","Shadowwalk Boots"),MawSetId,"boss_maw.feet")
        };
        public static SetDef[] CreateMawSets() => new[]
        {
            new SetDef { Id=MawSetId,Name=new Txt("飢影の狩装","Ravenous Shadow Gear"),BossTypeName="Mon_Special_BossMaw",BossReward=MawRewardId,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[] { new BossSetStage(2,"boss_maw.stage2"),new BossSetStage(3,"boss_maw.stage3"),new BossSetStage(6,"boss_maw.stage6") },
                LinkStages=new[] { new SetLinkStage(2,new LinkDef { Kind=LinkKind.BossReward,Value=1,Requires=new[] { "St_U_BigChomp" } }),new SetLinkStage(4,new LinkDef { Kind=LinkKind.BossReward,Value=2,Requires=new[] { "St_U_BigChomp" } }),new SetLinkStage(6,new LinkDef { Kind=LinkKind.BossReward,Value=3,Requires=new[] { "St_U_BigChomp" } }) } }
        };
    }
}

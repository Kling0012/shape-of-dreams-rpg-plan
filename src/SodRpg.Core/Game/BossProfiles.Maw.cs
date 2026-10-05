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
            MawProfile("weapon", "第1記憶が敵に命中したとき、命中方向の角度120°・範囲3mの扇状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える（再使用まで4秒、ダッシュや無敵効果なし）。", "E1: a120-degree/3m Dark fan toward the hit for18%H,CD4s; no dash or invulnerability.",
                new[] { C("MawBite",18,18) }, MawFan("MawBite",4000,3000,120000)),
            MawProfile("armor", "敵からHPダメージを受けたとき、自身に「攻撃力・魔力の高い方の14%」または「失ったHPの15%」の低い方の値の障壁を2秒間付与する（再使用まで6秒、重複不可、非戦闘時の自動蓄積なし）。", "Hostile native HP damage taken: owner shield min(14%H,15% missing HP) for2s,CD6s; no stacking or idle recharge.",
                new[] { C("MawConversion",14,14,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Defense,BossPayload.Shield,"MawConversion",cooldownMillis:6000,lifetimeMillis:2000,maxTargets:1,maxInstances:1,magnitudeMilli:15000)),
            MawProfile("charm", "第1記憶が敵に4回命中するごとに（最大3回まで蓄積、最後の命中から4秒経過でリセット）、自身を「攻撃力・魔力の高い方の6%」または「失ったHPの8%」の低い方の値だけ回復する（再使用まで5秒、1回の攻撃で複数の敵に命中してもカウント・回復は1回のみ）。", "Every fourth E1 (at most3 stored,expire after4s): one owner heal min(6%H,8% missing HP),CD5s regardless of targets.",
                new[] { C("MawGreatjaw",6,6,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Heal,"MawGreatjaw",cooldownMillis:5000,mainHits:4,counterLifetimeMillis:4000,maxTargets:1,maxInstances:1,magnitudeMilli:8000)),
            MawProfile("head", "第2記憶の発動時、発動方向へ2本の斬撃波を0.15秒間隔で放つ。それぞれ幅1m・射程6m（持続0.6秒）直進し、攻撃力・魔力の高い方の8%の闇属性ダメージを与える（再使用まで6秒、追尾効果なし）。", "E2: two left/right Dark waves in the confirmed cast direction,8%H each,width1m,range6m,life0.6s,0.15s apart,CD6s; no homing.",
                new[] { C("MawStance",16,16) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Projectile,BossPayload.Damage,"MawStance",cooldownMillis:6000,lifetimeMillis:600,maxTargets:8,maxInstances:2,rangeMilli:6000,widthMilli:1000,speedMilli:10000,element:BossElement.Dark),
                new BossAction(BossEvent.Clock,BossMechanism.Projectile,BossPayload.Damage,"MawStance",delayMillis:150,lifetimeMillis:600,maxTargets:8,maxInstances:2,rangeMilli:6000,widthMilli:1000,speedMilli:10000,element:BossElement.Dark)),
            MawProfile("hands", "第1記憶で敵のHPに与えた実ダメージの10%をHPとして吸収する（1回の攻撃判定ごとの上限は攻撃力・魔力の高い方の4%、再使用まで4秒。無敵・味方・反射・派生ダメージからは回復しない）。", "E1: absorb10% of actual enemy HP damage,cap4%H per activation,CD4s; immune,ally,reflected or generated damage heals nothing.",
                new[] { C("MawDevour",4,4,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Heal,"MawDevour",cooldownMillis:4000,maxTargets:1,maxInstances:1,magnitudeMilli:10000)),
            MawProfile("feet", "移動完了後2秒以内に行う次の第1記憶が命中したとき、角度90°・範囲3mの扇状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える（再使用まで5秒、無敵・対象指定不可や追加ダッシュなし）。", "The next E1 within2s of native movement completion adds a90-degree/3m Dark fan for18%H,CD5s,one mark only; no untargetability or extra dash.",
                new[] { C("MawShadowbite",18,18) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000,maxTargets:1,maxInstances:1), MawFan("MawShadowbite",5000,3000,90000)),
            MawProfile("stage2", "第1記憶を3回命中させるごとに（最後の命中から4秒でリセット）、前方角度120°・範囲3mの扇状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える（再使用まで3秒。自身のHPが50%以下の場合は前後に角度90°の扇状波を放ち、それぞれ攻撃力・魔力の高い方の9%ずつの闇属性ダメージを与える）。", "Independent profile cycle: every third E1,max3,expire4s after the last success. Forward120-degree/3m fan18%H,CD3s; at HP<=50%,replace with front/back90-degree fans9%H each; no part marks required.",
                new[] { C("MawCycleBite",18,18) }, MawFan("MawCycleBite",3000,3000,120000), MawFan("MawCycleBite",0,3000,90000,BossEvent.Clock)),
            MawProfile("stage3", "3回目の命中地点（自身から8m以内）に半径2.5mの煉獄領域を展開し、0.5秒の予告後に攻撃力・魔力の高い方の22%の闇属性爆発ダメージを1回与える（再使用まで5秒。自身のHPが50%以下の場合は自身の足元に発生する）。", "On the same third hit: freeze a legal hit point within8m,radius2.5m Purgatory circle;0.5s warning then one22%H Dark explosion,CD5s. At HP<=50%,replace the point with your feet without changing damage.",
                new[] { C("MawPurgatory",22,22) },
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MawPurgatory",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:500,maxTargets:8,maxInstances:1,radiusMilli:2500,rangeMilli:8000,element:BossElement.Dark)),
            MawProfile("stage6", "3回目の命中時、左右±30°の方向に角度120°・範囲3.5mの扇状波を0.20秒間隔で放ち、それぞれ攻撃力・魔力の高い方の45%（合計90%、上限100%）の闇属性ダメージを与える（再使用まで8秒。自身のHPが50%以下の場合は前後の角度90°扇状波に変化）。さらに2つの波で敵のHPに与えた合計実ダメージの45%分、自身を回復する（回復上限は攻撃力・魔力の高い方の38%・上限40%）。", "On the same third hit: two120-degree/3.5m Dark fans at±30 degrees,45%H each,0.20s apart,CD8s. HP<=50% replaces them with front/back90-degree fans. Heal45% of their total actual enemy HP damage with one total38%H cap(damage90/cap100,heal38/cap40); zero damage heals zero,overflow discarded.",
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
                new BossRewardStage(1,new Txt("自身の「Big Chomp」が生存している敵に命中した際、ボスの重み付け（通常敵1、ボスはさらに増加、最大3まで）を記録する。ディレイ後の回復量に「攻撃力・魔力の高い方の8%」または「1ヒットあたりの元回復量×重み×20%」の低い方の値を加算する（共有クールダウン8秒、味方への命中数や本体の回復処理は不変）。", "Own native Big Chomp: record only hostile alive/nonimmune OnHit boss-weighted w,cap3. Add min(8%H,loaded healPerHit×w×20%) to the original owner Heal in one OnAfterDelay,shared CD8s; native ally hits/counts unchanged."),new[] { heal }),
                new BossRewardStage(2,new Txt("2部位効果に加えて、同スキルのディレイ後の付与障壁量に「攻撃力・魔力の高い方の10%」または「1ヒットあたりの元障壁量×重み×20%」の低い方の値を加算する（持続時間は元のまま維持、共有8秒枠で1回のみ発動）。", "2-piece effect plus min(10%H,loaded shieldPerHit×w×20%) on the same original GiveShield; preserve duration,once per instance/shared8s slot."),new[] { heal,shield }),
                new BossRewardStage(3,new Txt("4部位効果に加えて、同スキルのディレイ終了時における自身のクールダウン短縮効果へ「0.50秒」または「重み×0.25秒」の低い方の値を追加する（元の短縮量が0なら加算なし、共有8秒枠。他の記憶への短縮や新たなHP吸収はなし）。", "4-piece effects plus min(0.50s,w×0.25s) on the same instance's original firstTrigger cooldown reduction; zero native amount gets zero extra. Preserve native minimum,scale and canReceiveCooldown in the same8s slot; no other-memory buff or new lifesteal."),new[] { heal,shield,cooldown })
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

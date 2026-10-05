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
            MawProfile("weapon", "第1記憶が敵に命中したとき、命中方向の角度120°・範囲3mの扇状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える（再使用まで4秒、ダッシュや無敵効果なし）。", "When your first memory hits an enemy, deal Dark damage equal to 18% of whichever is higher: your Attack or Ability Power, in a 120° fan extending 3m toward the hit. Cooldown: 4 seconds. Does not grant a dash or invulnerability.",
                new[] { C("MawBite",18,18) }, MawFan("MawBite",4000,3000,120000)),
            MawProfile("armor", "敵からHPダメージを受けたとき、自身に「攻撃力・魔力の高い方の14%」または「失ったHPの15%」の低い方の値の障壁を2秒間付与する（再使用まで6秒、重複不可、非戦闘時の自動蓄積なし）。", "When an enemy deals HP damage to you, gain a shield for 2 seconds equal to the lower of 14% of the higher of your Attack and Ability Power and 15% of your missing HP. Cooldown: 6 seconds. Does not stack or automatically build up outside combat.",
                new[] { C("MawConversion",14,14,BossCoefficientKind.Shield) },
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Defense,BossPayload.Shield,"MawConversion",cooldownMillis:6000,lifetimeMillis:2000,maxTargets:1,maxInstances:1,magnitudeMilli:15000)),
            MawProfile("charm", "第1記憶が敵に4回命中するごとに（最大3回まで蓄積、最後の命中から4秒経過でリセット）、自身を「攻撃力・魔力の高い方の6%」または「失ったHPの8%」の低い方の値だけ回復する（再使用まで5秒、1回の攻撃で複数の敵に命中してもカウント・回復は1回のみ）。", "Every fourth hit on an enemy with your first memory heals you for the lower of 6% of the higher of your Attack and Ability Power and 8% of your missing HP. Store up to 3 hits; the count resets 4 seconds after the last hit. Cooldown: 5 seconds. Hitting multiple enemies with one attack only counts once and heals once.",
                new[] { C("MawGreatjaw",6,6,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Heal,"MawGreatjaw",cooldownMillis:5000,mainHits:4,counterLifetimeMillis:4000,maxTargets:1,maxInstances:1,magnitudeMilli:8000)),
            MawProfile("head", "第2記憶の発動時、発動方向へ2本の斬撃波を0.15秒間隔で放つ。それぞれ幅1m・射程6m（持続0.6秒）直進し、攻撃力・魔力の高い方の8%の闇属性ダメージを与える（再使用まで6秒、追尾効果なし）。", "When you use your second memory, release 2 slash waves in its direction, 0.15 seconds apart. Each travels straight ahead, is 1m wide, has a 6m range, lasts 0.6 seconds, and deals Dark damage equal to 8% of whichever is higher: your Attack or Ability Power. Cooldown: 6 seconds. The waves do not home in on enemies.",
                new[] { C("MawStance",16,16) },
                new BossAction(BossEvent.MemoryUse,BossMechanism.Projectile,BossPayload.Damage,"MawStance",cooldownMillis:6000,lifetimeMillis:600,maxTargets:8,maxInstances:2,rangeMilli:6000,widthMilli:1000,speedMilli:10000,element:BossElement.Dark),
                new BossAction(BossEvent.Clock,BossMechanism.Projectile,BossPayload.Damage,"MawStance",delayMillis:150,lifetimeMillis:600,maxTargets:8,maxInstances:2,rangeMilli:6000,widthMilli:1000,speedMilli:10000,element:BossElement.Dark)),
            MawProfile("hands", "第1記憶で敵のHPに与えた実ダメージの10%をHPとして吸収する（1回の攻撃判定ごとの上限は攻撃力・魔力の高い方の4%、再使用まで4秒。無敵・味方・反射・派生ダメージからは回復しない）。", "Restore HP equal to 10% of the actual HP damage your first memory deals to enemies, up to 4% of the higher of your Attack and Ability Power per hit. Cooldown: 4 seconds. Hits against invulnerable targets or allies, reflected damage, and damage from additional triggered effects do not heal you.",
                new[] { C("MawDevour",4,4,BossCoefficientKind.Heal) },
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Heal,"MawDevour",cooldownMillis:4000,maxTargets:1,maxInstances:1,magnitudeMilli:10000)),
            MawProfile("feet", "移動完了後2秒以内に行う次の第1記憶が命中したとき、角度90°・範囲3mの扇状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える（再使用まで5秒、無敵・対象指定不可や追加ダッシュなし）。", "If you next use your first memory within 2 seconds of finishing movement, its hit deals Dark damage equal to 18% of whichever is higher: your Attack or Ability Power, in a 90° fan extending 3m. Cooldown: 5 seconds. Does not grant invulnerability, untargetability, or an additional dash.",
                new[] { C("MawShadowbite",18,18) },
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000,maxTargets:1,maxInstances:1), MawFan("MawShadowbite",5000,3000,90000)),
            MawProfile("stage2", "第1記憶を3回命中させるごとに（最後の命中から4秒でリセット）、前方角度120°・範囲3mの扇状に攻撃力・魔力の高い方の18%の闇属性ダメージを与える（再使用まで3秒。自身のHPが50%以下の場合は前後に角度90°の扇状波を放ち、それぞれ攻撃力・魔力の高い方の9%ずつの闇属性ダメージを与える）。", "Every third hit with your first memory deals Dark damage equal to 18% of whichever is higher: your Attack or Ability Power, in a forward 120° fan extending 3m. The hit count resets 4 seconds after the last hit. Cooldown: 3 seconds. At 50% HP or less, instead release 90° fan-shaped waves in front of and behind you, each dealing Dark damage equal to 9% of the higher of your Attack and Ability Power.",
                new[] { C("MawCycleBite",18,18) }, MawFan("MawCycleBite",3000,3000,120000), MawFan("MawCycleBite",0,3000,90000,BossEvent.Clock)),
            MawProfile("stage3", "3回目の命中地点（自身から8m以内）に半径2.5mの煉獄領域を展開し、0.5秒の予告後に攻撃力・魔力の高い方の22%の闇属性爆発ダメージを1回与える（再使用まで5秒。自身のHPが50%以下の場合は自身の足元に発生する）。", "On the third hit, create a Purgatory field with a 2.5m radius at the hit location within 8m of you. After a 0.5-second warning, it explodes once, dealing Dark damage equal to 22% of whichever is higher: your Attack or Ability Power. Cooldown: 5 seconds. At 50% HP or less, the field appears at your feet instead.",
                new[] { C("MawPurgatory",22,22) },
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MawPurgatory",anchor:BossAnchor.Hit,cooldownMillis:5000,delayMillis:500,maxTargets:8,maxInstances:1,radiusMilli:2500,rangeMilli:8000,element:BossElement.Dark)),
            MawProfile("stage6", "3回目の命中時、左右±30°の方向に角度120°・範囲3.5mの扇状波を0.20秒間隔で放ち、それぞれ攻撃力・魔力の高い方の45%（合計90%、上限100%）の闇属性ダメージを与える（再使用まで8秒。自身のHPが50%以下の場合は前後の角度90°扇状波に変化）。さらに2つの波で敵のHPに与えた合計実ダメージの45%分、自身を回復する（回復上限は攻撃力・魔力の高い方の38%・上限40%）。", "On the third hit, release 2 waves, 0.20 seconds apart, angled 30° left and right. Each covers a 120° fan extending 3.5m and deals Dark damage equal to 45% of whichever is higher: your Attack or Ability Power (90% total, capped at 100%). Cooldown: 8 seconds. At 50% HP or less, the waves become 90° fans in front of and behind you. Heal for 45% of the total actual HP damage both waves deal to enemies, up to 38% of the higher of your Attack and Ability Power (capped at 40%).",
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
                new BossRewardStage(1,new Txt("自身の「Big Chomp」が生存している敵に命中した際、ボスの重み付け（通常敵1、ボスはさらに増加、最大3まで）を記録する。ディレイ後の回復量に「攻撃力・魔力の高い方の8%」または「1ヒットあたりの元回復量×重み×20%」の低い方の値を加算する（共有クールダウン8秒、味方への命中数や本体の回復処理は不変）。", "When your Big Chomp hits a living enemy, count normal enemies as 1 and bosses as more, up to a total weight of 3. After its delay, increase its healing by the lower of 8% of the higher of your Attack and Ability Power and its original healing per hit × weight × 20%. Shared Cooldown: 8 seconds. Ally hit counts and the skill's original healing remain unchanged."),new[] { heal }),
                new BossRewardStage(2,new Txt("2部位効果に加えて、同スキルのディレイ後の付与障壁量に「攻撃力・魔力の高い方の10%」または「1ヒットあたりの元障壁量×重み×20%」の低い方の値を加算する（持続時間は元のまま維持、共有8秒枠で1回のみ発動）。", "In addition to the 2-piece effect, increase Big Chomp's shield after its delay by the lower of 10% of the higher of your Attack and Ability Power and its original shield amount per hit × weight × 20%. The shield's duration is unchanged. Triggers only once per shared 8-second Cooldown."),new[] { heal,shield }),
                new BossRewardStage(3,new Txt("4部位効果に加えて、同スキルのディレイ終了時における自身のクールダウン短縮効果へ「0.50秒」または「重み×0.25秒」の低い方の値を追加する（元の短縮量が0なら加算なし、共有8秒枠。他の記憶への短縮や新たなHP吸収はなし）。", "In addition to the 4-piece effects, when Big Chomp's delay ends, increase its Cooldown reduction for you by the lower of 0.50 seconds and weight × 0.25 seconds. Adds nothing if the original reduction is 0. Shares the 8-second Cooldown. Does not reduce other memories' Cooldowns or add HP absorption."),new[] { heal,shield,cooldown })
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

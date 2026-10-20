using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string DemonSetId = "set.boss_demon";
        public const string DemonRewardId = "boss_demon.hysteria";
        public const int MaxEntries = 64;
        public const int BudDelayMillis = BossDemonBalance.BudDelayMillis;
        public const int BudCollectibleDelayMillis = BossDemonBalance.BudCollectibleDelayMillis;
        public const int BudRadiusMilli = BossDemonBalance.BudRadiusMilli;
        public const int MaxBuds = BossDemonBalance.MaxBuds;
        public const int NativeHysteriaSpeedMilli = BossDemonBalance.NativeHysteriaSpeedMilli;
        public const string DemonEventOrder = "collect-existing;march;replant-start;replant-end;part;stomp;grove";
        public const string NativeContract = "native-only;admission-rings64;own-scoped-speed-cache64;one-activation;one-claw-instance;one-state-life;left-right-pair;post-success-exact-claw-packet;no-generated-proc;own-speed-minus50-to-minus25-then-restore-minus50;preserve-lock-heal-interval-dash;movement-installed-own-skilltrigger-actual-completion;exclude-hysteria-generated-enemy-movement";
        public const string CombatContract = "freeze-max-ad-ap;physical-tie;profile-element-preserved;one-defense;no-crit;no-attackeffect;no-self-generation;legal-ground-los;clear-on-epoch-death-room-memory-transition-start;finite-sequence32-one-token-owner-set4;nyx-erebos-owner-set2-oldest-replace-token-linked-shot-cancel;radial-per-shot-hit-per-target-wave-count-cap;terminal-projectile-no-flight-damage-no-wall-explosion;homing-orb-travel8-terminal-once;chain3-distinct-targets-radius3-delay150-whole-life1500;positive-final-generated-damage-only-nonboss-nonimmune-stun250;shield-linear-decay-from-actual-remaining-no-refill;confirmed-movement-departure-and-cast-forward-scopes;actor-life-not-creation-time-alone;native-parent-scoped-reservations-and-mark-contributions;live-config-preserve-unchanged-native-profile-sources-and-cooldowns;stop-generated-combat-on-owner-death";
        public const string VisualContract = "owner-run-zone-room-equipment-epoch-revision;max-live64;effect-id-update-only;snapshot1000ms-unscaled;game-time-deadlines;offset-synced-mirror-networktime-sentAt-times-timescale;remaining-time-only;invalidate-epoch;profile-element-shape-range-width-angle-count-budget-shrinking-radius;radial-kind4-arrowheads-angle180-inward-else-outward;frozen-adopted-target-persistent-net-id;shield-only-live-target-follow;host-only-native-life-scope-preserved-across-live-equipment-epoch";
        public static IReadOnlyList<BossMoveProfile> Moves { get; } = Array.AsReadOnly(CreateMoves()
            .Concat(CreateSkollMoves()).Concat(CreateInfernusMoves()).Concat(CreateInkMoves())
            .Concat(CreateNyxMoves()).Concat(CreateErebosMoves()).Concat(CreateSeekerMoves()).Concat(CreateAzurakMoves()).Concat(CreatePrimusMoves())
            .Concat(CreateLightMoves()).Concat(CreateMawMoves()).Concat(CreateObliviaxMoves()).Concat(CreatePolarisMoves()).ToArray());
        public static IReadOnlyList<BossRewardProfile> Rewards { get; } = Array.AsReadOnly(CreateRewards()
            .Concat(CreateSkollRewards()).Concat(CreateInfernusRewards()).Concat(CreateInkRewards())
            .Concat(CreateNyxRewards()).Concat(CreateErebosRewards()).Concat(CreateSeekerRewards()).Concat(CreateAzurakRewards())
            .Concat(CreateLightRewards()).Concat(CreateMawRewards()).Concat(CreateObliviaxRewards()).ToArray());
        private static readonly Dictionary<string, BossMoveProfile> moves = Moves.ToDictionary(x => x.Id, StringComparer.Ordinal);
        private static readonly Dictionary<string, BossRewardProfile> rewards = Rewards.ToDictionary(x => x.Id, StringComparer.Ordinal);
        public static bool TryGetMove(string id, out BossMoveProfile profile)
        { profile = null; return id != null && moves.TryGetValue(id, out profile); }
        public static bool TryGetReward(string id, out BossRewardProfile profile)
        { profile = null; return id != null && rewards.TryGetValue(id, out profile); }
        public static string DescribeMove(string id) => TryGetMove(id, out var p) ? p.Description.ToString() : "";
        public static string DescribeReward(string id, int stage) => TryGetReward(id, out var p) && stage >= 1 && stage <= 3 ? p.Stages[stage - 1].Description.ToString() : "";
        public static uint SetMask(string setId)
        {
            switch (setId)
            {
                case DemonSetId: return 1u;
                case SkollSetId: return 1u << 1;
                case InfernusSetId: return 1u << 2;
                case WhiteNightSetId: return 1u << 3;
                case DarkMoonSetId: return 1u << 4;
                case NyxSetId: return 1u << 5;
                case ErebosSetId: return 1u << 6;
                case SeekerSetId: return 1u << 7;
                case AzurakSetId: return 1u << 8;
                case PrimusSetId: return 1u << 9;
                case LightSetId: return 1u << 10;
                case MawSetId: return 1u << 11;
                case ObliviaxSetId: return 1u << 12;
                case PolarisSetId: return 1u << 13;
                default: return 0;
            }
        }
        private static BossChannelDef C(string id, int value, int cap, BossCoefficientKind kind = BossCoefficientKind.Damage) => new BossChannelDef(id, value * 1000, cap * 1000, kind);
        private static BossMoveProfile P(string id, string ja, string en, string zh, BossChannelDef channel, params BossAction[] actions)
            => new BossMoveProfile("boss_demon." + id, DemonSetId, new Txt(ja, en, zh), new[] { channel }, actions);
        private static BossAction Bud(BossEvent e, string channel, BossAnchor anchor, int cooldown = 0, bool collectible = true, int count = 1)
            => new BossAction(e, BossMechanism.Field, BossPayload.Bud, channel, anchor: anchor, cooldownMillis: cooldown,
                delayMillis: BudDelayMillis, lifetimeMillis: BudDelayMillis, radiusMilli: BudRadiusMilli,rangeMilli:BossDemonBalance.BudRangeMilli,
                maxInstances: MaxBuds, collectible: collectible, count: count, magnitudeMilli: BudCollectibleDelayMillis);
        private static BossMoveProfile[] CreateMoves() => new[]
        {
            P("weapon", "通常攻撃が3回命中するごとに、命中地点に踏みつけを起こし、半径3mの範囲へ攻撃力・魔力の高い方の25%（上限80%）のダメージを与える。再発動まで2秒かかる。", "Every 3 basic attack hits, cause a stomp at the hit location in a 3m radius, dealing 25% of whichever is higher: your Attack or Ability Power (cap: 80%). Cooldown: 2 seconds.", "通常攻击每命中3次，在命中地点引发半径3m的踩踏，造成相当于攻击力与法术强度中较高一方25%（上限80%）的伤害。冷却2秒。", C("DemonImpact",BossDemonBalance.DemonImpactValue,BossDemonBalance.DemonImpactCap),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonImpact",anchor:BossAnchor.Hit,cooldownMillis:BossDemonBalance.WeaponA0CooldownMillis,mainHits:BossDemonBalance.WeaponA0MainHits,radiusMilli:BossDemonBalance.WeaponA0RadiusMilli)),
            P("armor", "移動スキルが終わったとき、0.6秒間（上限1秒）、行動妨害を受けなくなる。再発動まで8秒かかり、強化しても効果時間は増えない。", "When your movement skill ends, gain immunity to crowd control for 0.6 seconds (cap: 1 second). Cooldown: 8 seconds. Upgrades do not increase the duration.", "移动技能结束时，免疫控制0.6秒（上限1秒）。冷却8秒，升级不会增加持续时间。", C("DemonStride",BossDemonBalance.DemonStrideValue,BossDemonBalance.DemonStrideCap,BossCoefficientKind.Duration),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Defense,BossPayload.Unstoppable,"DemonStride",cooldownMillis:BossDemonBalance.ArmorA0CooldownMillis,lifetimeMillis:BossDemonBalance.ArmorA0LifetimeMillis)),
            P("charm", "通常記憶または奥義記憶を使ったとき、カーソル方向の8m以内に樹芽を設置する。樹芽は1.2秒後に半径2.5mの範囲へ噴き出し、攻撃力・魔力の高い方の20%（上限60%）のダメージを与える。設置できる樹芽は最大4個まで。再発動まで4秒かかる。", "When you use a normal or ultimate memory, plant a bud within 8m toward the cursor. After 1.2 seconds, it erupts in a 2.5m radius, dealing 20% of whichever is higher: your Attack or Ability Power (cap: 60%). Up to 4 buds can exist at once. Cooldown: 4 seconds.", "使用普通记忆或奥义记忆时，向光标方向8m以内种下树芽。树芽在1.2秒后喷发，对半径2.5m范围造成相当于攻击力与法术强度中较高一方20%（上限60%）的伤害。同时最多存在4个树芽。冷却4秒。", C("DemonSeed",BossDemonBalance.DemonSeedValue,BossDemonBalance.DemonSeedCap), Bud(BossEvent.MemoryUse,"DemonSeed",BossAnchor.Cursor,BossDemonBalance.CharmA0CooldownMillis)),
            P("head", "通常攻撃が6回命中するごとに、全方位へ60度間隔で6本の直線弾を放つ。弾はそれぞれ攻撃力・魔力の高い方の12%（上限40%）のダメージを与え、弾速は12m/秒、射程は6m、幅は0.4mで、最初に当たった敵か壁で消える。同じ敵に当たるのは1波につき1回まで。再発動まで6秒かかる。", "Every 6 basic attack hits, fire 6 projectiles in all directions at 60-degree intervals. Each deals 12% of whichever is higher: your Attack or Ability Power (cap: 40%), travels at 12m/s, has a 6m range and is 0.4m wide. Projectiles disappear on the first enemy or wall they hit. Each enemy can be hit only once per wave. Cooldown: 6 seconds.", "通常攻击每命中6次，向四周以60度间隔发射6枚直线弹。每枚造成相当于攻击力与法术强度中较高一方12%（上限40%）的伤害，弹速12m/秒，射程6m，宽0.4m，在首次命中敌人或墙壁时消失。每波对同一敌人最多命中1次。冷却6秒。", C("DemonVolley",BossDemonBalance.DemonVolleyValue,BossDemonBalance.DemonVolleyCap),
                new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"DemonVolley",BossShape.Radial,cooldownMillis:BossDemonBalance.HeadA0CooldownMillis,lifetimeMillis:BossDemonBalance.HeadA0LifetimeMillis,count:BossDemonBalance.HeadA0Count,mainHits:BossDemonBalance.HeadA0MainHits,rangeMilli:BossDemonBalance.HeadA0RangeMilli,widthMilli:BossDemonBalance.HeadA0WidthMilli,speedMilli:BossDemonBalance.HeadA0SpeedMilli,angleMilli:BossDemonBalance.HeadA0AngleMilli,firstHitOnly:true)),
            P("hands", "通常攻撃が4回命中するごとに、命中地点に予告を出し、0.65秒後に半径2mの範囲へ攻撃力・魔力の高い方の25%（上限80%）のダメージを与える。再発動まで3秒かかる。", "Every 4 basic attack hits, mark the hit location with a warning. After 0.65 seconds, strike a 2m radius for 25% of whichever is higher: your Attack or Ability Power (cap: 80%). Cooldown: 3 seconds.", "通常攻击每命中4次，在命中地点显示预警，0.65秒后对半径2m范围造成相当于攻击力与法术强度中较高一方25%（上限80%）的伤害。冷却3秒。", C("DemonDelay",BossDemonBalance.DemonDelayValue,BossDemonBalance.DemonDelayCap),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"DemonDelay",anchor:BossAnchor.Hit,cooldownMillis:BossDemonBalance.HandsA0CooldownMillis,delayMillis:BossDemonBalance.HandsA0DelayMillis,lifetimeMillis:BossDemonBalance.HandsA0LifetimeMillis,mainHits:BossDemonBalance.HandsA0MainHits,radiusMilli:BossDemonBalance.HandsA0RadiusMilli)),
            P("feet", "移動スキルまたはテレポートが終わってから3秒以内に次の通常攻撃を行ったとき、半径3mの範囲に踏みつけを起こし、攻撃力・魔力の高い方の20%（上限60%）のダメージを与える。再発動まで4秒かかる。", "Your next basic attack within 3 seconds after your movement skill or teleport ends causes a stomp in a 3m radius, dealing 20% of whichever is higher: your Attack or Ability Power (cap: 60%). Cooldown: 4 seconds.", "移动技能或传送结束后3秒内的下一次通常攻击引发半径3m的踩踏，造成相当于攻击力与法术强度中较高一方20%（上限60%）的伤害。冷却4秒。", C("DemonArrival",BossDemonBalance.DemonArrivalValue,BossDemonBalance.DemonArrivalCap),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:BossDemonBalance.FeetA0LifetimeMillis,ledgerId:"DemonArrival"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonArrival",anchor:BossAnchor.Hit,cooldownMillis:BossDemonBalance.FeetA1CooldownMillis,radiusMilli:BossDemonBalance.FeetA1RadiusMilli,ledgerId:"DemonArrival")),
            P("stage2", "移動スキルまたはテレポートが終わったとき、到着地点に踏みつけを起こし、半径3mの範囲へ攻撃力・魔力の高い方の50%（上限100%）のダメージを与える。再発動まで3秒かかる。", "When your movement skill or teleport ends, cause a stomp at your destination in a 3m radius, dealing 50% of whichever is higher: your Attack or Ability Power (cap: 100%). Cooldown: 3 seconds.", "移动技能或传送结束时，在到达地点引发半径3m的踩踏，造成相当于攻击力与法术强度中较高一方50%（上限100%）的伤害。冷却3秒。", C("DemonStomp",BossDemonBalance.DemonStompValue,BossDemonBalance.DemonStompCap),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonStomp",anchor:BossAnchor.Destination,cooldownMillis:BossDemonBalance.Stage2A0CooldownMillis,radiusMilli:BossDemonBalance.Stage2A0RadiusMilli)),
            P("stage3", "自身の装備効果や2セット効果で踏みつけが起きたとき、攻撃力・魔力の高い方の45%（上限90%）の樹芽を設置する。樹芽の生成は1秒に1回まで、設置できるのは最大4個まで。通常攻撃が命中したときは命中地点から4m以内の最も近い樹芽を、着地したときは自身から8m以内でカーソルに最も近い樹芽を1つ回収する。回収は通常攻撃と着地で共通して1秒に1回までで、樹芽は生成から0.15秒後に回収できる。", "Stomps caused by your equipment or 2-piece set effect plant a bud that deals 45% of whichever is higher: your Attack or Ability Power (cap: 90%). Bud creation has a 1-second cooldown, and up to 4 buds can exist. Basic attacks collect the nearest bud within 4m of the hit location. On landing, collect the bud nearest the cursor within 8m of you. Both collection methods share a 1-second cooldown. Buds can be collected 0.15 seconds after creation.", "自身装备或2件套效果引发踩踏时，种下造成相当于攻击力与法术强度中较高一方45%（上限90%）伤害的树芽。树芽每1秒最多生成1个，同时最多存在4个。通常攻击命中时，回收命中地点4m以内最近的1个树芽；落地时，回收自身8m以内最靠近光标的1个树芽。两种回收方式共享每1秒最多回收1次的限制，树芽在生成0.15秒后才可回收。", C("DemonGrove",BossDemonBalance.DemonGroveValue,BossDemonBalance.DemonGroveCap),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Hit,cooldownMillis:BossDemonBalance.Stage3A0CooldownMillis,rangeMilli:BossDemonBalance.Stage3A0RangeMilli),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Cursor,cooldownMillis:BossDemonBalance.Stage3A1CooldownMillis,rangeMilli:BossDemonBalance.Stage3A1RangeMilli),
                Bud(BossEvent.MainHit,"DemonGrove",BossAnchor.Hit,BossDemonBalance.Stage3A2CooldownMillis)),
            new BossMoveProfile("boss_demon.stage6",DemonSetId,
                new Txt("樹芽を回収したとき、自身から樹芽へ向けて最大8mの直線を作る。直線の1/3地点、2/3地点、終点では、それぞれ0秒後、0.25秒後、0.5秒後に半径2.5mの衝撃波が起き、攻撃力・魔力の高い方の30%（合計90%、上限100%）のダメージを与える。さらに始点と終点に、回収できない樹芽（ダメージは各38%、上限40%）を1つずつ再設置する。再設置する樹芽は最大4個の制限を共有し、始点と終点が同じ地点のときはカーソル方向6mに設置する。この効果から樹芽が再設置されたり回収されたりすることはない。再発動まで6秒かかる。", "When you collect a bud, form a line up to 8m long from you toward it. Shockwaves erupt at one-third of the line, two-thirds of the line, and its end after 0, 0.25, and 0.5 seconds respectively. Each has a 2.5m radius and deals 30% of whichever is higher: your Attack or Ability Power (90% total, cap: 100%). Also plant one uncollectible bud at each end, each dealing 38% (cap: 40%) and sharing the 4-bud limit. If both ends coincide, plant a bud 6m toward the cursor. These follow-up effects cannot plant or collect more buds. Cooldown: 6 seconds.", "回收树芽时，从自身向树芽形成最长8m的直线。在直线的1/3处、2/3处和终点，分别于0秒后、0.25秒后、0.5秒后引发半径2.5m的冲击波，各造成相当于攻击力与法术强度中较高一方30%的伤害（合计90%，上限100%）。此外在起点和终点各重新种下1个无法回收的树芽，各造成38%（上限40%）的伤害。重新种下的树芽共享最多4个的限制；起点与终点重合时，在光标方向6m处种下树芽。这些后续效果不会再次种下或回收树芽。冷却6秒。"),
                new[] { C("DemonMarch",BossDemonBalance.DemonMarchValue,BossDemonBalance.DemonMarchCap),C("DemonReplant",BossDemonBalance.DemonReplantValue,BossDemonBalance.DemonReplantCap) },new[]
                {
                    new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.March,"DemonMarch",BossShape.Column,BossAnchor.Collected,cooldownMillis:BossDemonBalance.Stage6A0CooldownMillis,intervalMillis:BossDemonBalance.Stage6A0IntervalMillis,count:BossDemonBalance.Stage6A0Count,radiusMilli:BossDemonBalance.Stage6A0RadiusMilli,rangeMilli:BossDemonBalance.Stage6A0RangeMilli,maxInstances:BossDemonBalance.Stage6A0MaxInstances,magnitudeMilli:BossDemonBalance.Stage6A0MagnitudeMilli),
                    Bud(BossEvent.Clock,"DemonReplant",BossAnchor.Collected,collectible:false,count:BossDemonBalance.Stage6A1Count),
                }),
        };
        private static BossRewardProfile[] CreateRewards()
        {
            BossRewardAction Pair() => new BossRewardAction(BossRewardActionKind.HysteriaPair,cooldownMillis:BossDemonBalance.RewardPairCooldownMillis,count:BossDemonBalance.RewardPairCount,order:0);
            BossRewardAction Plant() => new BossRewardAction(BossRewardActionKind.HysteriaPlant,BossDemonBalance.RewardPlantValueMilli,BossDemonBalance.RewardPlantCapMilli,BossDemonBalance.RewardPlantCooldownMillis,count:BossDemonBalance.RewardPlantCount,rangeMilli:BossDemonBalance.RewardPlantRangeMilli,order:1);
            BossRewardAction Collect() => new BossRewardAction(BossRewardActionKind.HysteriaCollect,cooldownMillis:BossDemonBalance.RewardCollectCooldownMillis,rangeMilli:BossDemonBalance.RewardCollectRangeMilli,order:2);
            return new[] { new BossRewardProfile(DemonRewardId,DemonSetId,"St_U_Hysteria",BossRewardAdapter.Hysteria,new[]
            {
                new BossRewardStage(1,new Txt("左右の爪撃を1組として扱い、片方でも命中したとき、樹界装備の部位専用カウンタだけを進める主撃を1回起こし、自身を中心に2セット効果の踏みつけを行う。通常攻撃時に発動する他の効果は誘発しない。この踏みつけは移動スキルによる踏みつけと再発動までの3秒を共有する。", "Treat the left and right claw strikes as one pair. If either hits, count one hit toward only the Forest Realm equipment counters, without triggering other basic-attack effects, and activate the 2-piece stomp centered on you. This shares its 3-second cooldown with the movement-skill stomp.", "将左右爪击视为一组，只要其中一次命中，就触发1次主击，仅推进树界装备各部位的专用计数，并以自身为中心发动2件套踩踏。不会触发其他通常攻击效果。此踩踏与移动技能引发的踩踏共享3秒冷却。"),new[] { Pair() }),
                new BossRewardStage(2,new Txt("固有報酬の段階1の効果に加え、左爪が最初に命中した地点に、攻撃力・魔力の高い方の45%（上限90%）の樹芽を設置し、踏みつけによる生成と1秒に1回までの制限を共有する。続いて右爪が最初に命中した地点から4m以内の最も近い樹芽を回収し、通常の回収と1秒に1回までの制限を共有する。", "In addition to boss reward stage 1, plant a bud at the first location hit by the left claw, dealing 45% of whichever is higher: your Attack or Ability Power (cap: 90%) and sharing the 1-second bud-creation cooldown with stomps. The following right claw collects the nearest bud within 4m of its first hit location, sharing the normal 1-second collection cooldown.", "除首领专属奖励阶段1的效果外，在左爪首次命中的地点种下造成相当于攻击力与法术强度中较高一方45%（上限90%）伤害的树芽，与踩踏生成树芽共享每1秒最多生成1次的限制。随后右爪会回收其首次命中地点4m以内最近的树芽，与普通回收共享每1秒最多回收1次的限制。"),new[] { Pair(),Plant(),Collect() }),
                new BossRewardStage(3,new Txt("固有報酬の段階2の効果に加え、右爪で樹芽を回収したときにも、6セット効果の衝撃波列と再設置が発動する。再発動までの6秒は共有する。また、「ヒステリー」による自身の移動速度が50%下がるペナルティが、25%下がるだけに緩和される。それ以外のダッシュ、間隔、回復、持続時間は変わらない。", "In addition to boss reward stage 2, collecting a bud with the right claw also triggers the 6-piece shockwave sequence and bud replanting, sharing their 6-second cooldown. Hysteria's movement speed penalty is reduced from -50% to -25%. Its other dash, interval, healing, and duration effects remain unchanged.", "除首领专属奖励阶段2的效果外，右爪回收树芽时，也会触发6件套的冲击波序列和重新种植，共享6秒冷却。此外，“歇斯底里”对自身移动速度的惩罚从降低50%减轻为降低25%。其余冲刺、间隔、治疗和持续时间保持不变。"),new[] { Pair(),Plant(),Collect(),
                    new BossRewardAction(BossRewardActionKind.HysteriaMarch,cooldownMillis:BossDemonBalance.RewardMarchCooldownMillis,rangeMilli:BossDemonBalance.RewardMarchRangeMilli,order:3),
                    new BossRewardAction(BossRewardActionKind.NativeSpeed,BossDemonBalance.RewardNativeSpeedValueMilli,BossDemonBalance.RewardNativeSpeedCapMilli,order:4) }),
            }) };
        }
        internal static IEnumerable<string> FingerprintRecords()
        {
            yield return "boss-schema:v2:" + MaxEntries + ":" + BudDelayMillis + ":" + BudCollectibleDelayMillis + ":" + BudRadiusMilli + ":" + MaxBuds + ":" + NativeHysteriaSpeedMilli.ToString(CultureInfo.InvariantCulture);
            yield return "boss-order:" + DemonEventOrder;
            yield return "boss-native:" + NativeContract;
            yield return "boss-order:skoll:" + SkollEventOrder;
            yield return "boss-native:skoll:" + SkollNativeContract;
            yield return "boss-order:infernus:" + InfernusEventOrder;
            yield return "boss-native:infernus:" + InfernusNativeContract;
            yield return "boss-order:ink:" + InkEventOrder;
            yield return "boss-native:ink:" + InkNativeContract;
            yield return "boss-order:nyx:" + NyxEventOrder;
            yield return "boss-native:nyx:" + NyxNativeContract;
            yield return "boss-order:erebos:" + ErebosEventOrder;
            yield return "boss-native:erebos:" + ErebosNativeContract;
            yield return "boss-order:seeker:" + SeekerEventOrder;
            yield return "boss-native:seeker:" + SeekerNativeContract;
            yield return "boss-order:azurak:" + AzurakEventOrder;
            yield return "boss-native:azurak:" + AzurakNativeContract;
            yield return "boss-order:primus:" + PrimusEventOrder;
            yield return "boss-native:primus:" + PrimusNativeContract;
            yield return "boss-order:light:" + LightEventOrder;
            yield return "boss-native:light:" + LightNativeContract;
            yield return "boss-order:maw:" + MawEventOrder;
            yield return "boss-native:maw:" + MawNativeContract;
            yield return "boss-order:obliviax:" + ObliviaxEventOrder;
            yield return "boss-native:obliviax:" + ObliviaxNativeContract;
            yield return "boss-order:polaris:" + PolarisEventOrder;
            yield return "boss-native:polaris:" + PolarisNativeContract;
            yield return "boss-combat:" + CombatContract;
            yield return "boss-visual:" + VisualContract;
            yield return "boss-profile-order:" + string.Join(",", Moves.Select(p => p.Id));
            yield return "boss-vocabulary:" + string.Join(",", Enum.GetNames(typeof(BossEvent))) + ":" + string.Join(",", Enum.GetNames(typeof(BossMechanism)))
                + ":" + string.Join(",", Enum.GetNames(typeof(BossPayload))) + ":" + string.Join(",", Enum.GetNames(typeof(BossShape)))
                + ":" + string.Join(",", Enum.GetNames(typeof(BossAnchor))) + ":" + string.Join(",", Enum.GetNames(typeof(BossCoefficientKind)))
                + ":" + string.Join(",", Enum.GetNames(typeof(BossRewardAdapter))) + ":" + string.Join(",", Enum.GetNames(typeof(BossRewardActionKind)))
                + ":" + string.Join(",", Enum.GetNames(typeof(BossElement)));
            foreach (var p in Moves)
            {
                yield return "boss-move:" + p.SetId + ":" + p.Id;
                for (int i = 0; i < p.Channels.Count; i++)
                { var c = p.Channels[i]; yield return "boss-channel:" + p.Id + ":" + i + ":" + c.ChannelId + ":" + c.ValueMilli + ":" + c.CapMilli + ":" + (int)c.Kind; }
                for (int i = 0; i < p.Actions.Count; i++) yield return "boss-action:" + p.Id + ":" + i + ":" + p.Actions[i].Fingerprint;
            }
            foreach (var p in Rewards)
            {
                yield return "boss-reward:" + p.Id + ":" + p.SetId + ":" + p.Requires + ":" + (int)p.Adapter;
                foreach (var stage in p.Stages)
                    for (int i = 0; i < stage.Actions.Count; i++) yield return "boss-reward-action:" + p.Id + ":" + stage.Stage + ":" + i + ":" + stage.Actions[i].Fingerprint;
            }
        }
    }
}

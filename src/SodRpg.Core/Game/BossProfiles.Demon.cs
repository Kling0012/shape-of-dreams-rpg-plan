using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string DemonSetId = "set.boss_demon";
        public const string DemonRewardId = "boss_demon.hysteria";
        public const int MaxEntries = 64;
        public const int BudDelayMillis = 1200;
        public const int BudCollectibleDelayMillis = 150;
        public const int BudRadiusMilli = 2500;
        public const int MaxBuds = 4;
        public const int NativeHysteriaSpeedMilli = -50000;
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
        private static BossMoveProfile P(string id, string ja, string en, BossChannelDef channel, params BossAction[] actions)
            => new BossMoveProfile("boss_demon." + id, DemonSetId, new Txt(ja, en), new[] { channel }, actions);
        private static BossAction Bud(BossEvent e, string channel, BossAnchor anchor, int cooldown = 0, bool collectible = true, int count = 1)
            => new BossAction(e, BossMechanism.Field, BossPayload.Bud, channel, anchor: anchor, cooldownMillis: cooldown,
                delayMillis: BudDelayMillis, lifetimeMillis: BudDelayMillis, radiusMilli: BudRadiusMilli, rangeMilli: 8000,
                maxInstances: MaxBuds, collectible: collectible, count: count, magnitudeMilli: BudCollectibleDelayMillis);
        private static BossMoveProfile[] CreateMoves() => new[]
        {
            P("weapon", "通常攻撃が3回命中するごとに、命中地点に半径3m、攻撃力・魔力の高い方の25%（上限80%）の踏みつけを発生させる。再使用まで2秒。", "Every 3 basic attack hits, cause a stomp at the hit location in a 3m radius, dealing 25% of whichever is higher: your Attack or Ability Power (cap: 80%). Cooldown: 2 seconds.", C("DemonImpact",25,80),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonImpact",anchor:BossAnchor.Hit,cooldownMillis:2000,mainHits:3,radiusMilli:3000)),
            P("armor", "自身の移動スキル完了時、0.6秒間行動妨害無効（上限1秒）を獲得する。再使用まで8秒（効果時間は強化で増加しない）。", "When your movement skill ends, gain immunity to crowd control for 0.6 seconds (cap: 1 second). Cooldown: 8 seconds. Upgrades do not increase the duration.", C("DemonStride",6,10,BossCoefficientKind.Duration),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Defense,BossPayload.Unstoppable,"DemonStride",cooldownMillis:8000,lifetimeMillis:600)),
            P("charm", "通常記憶または奥義記憶を使ったとき、カーソル方向8m以内に攻撃力・魔力の高い方の20%（上限60%）の樹芽を設置する。樹芽は1.2秒後に半径2.5mへ噴出する（最大4個まで設置可能）。再使用まで4秒。", "When you use a normal or ultimate memory, plant a bud within 8m toward the cursor. After 1.2 seconds, it erupts in a 2.5m radius, dealing 20% of whichever is higher: your Attack or Ability Power (cap: 60%). Up to 4 buds can exist at once. Cooldown: 4 seconds.", C("DemonSeed",20,60), Bud(BossEvent.MemoryUse,"DemonSeed",BossAnchor.Cursor,4000)),
            P("head", "通常攻撃が6回命中するごとに、全方位（60度間隔）に6本の直線弾を放つ。弾は各攻撃力・魔力の高い方の12%（上限40%）、弾速12m/秒、射程6m、幅0.4mで、最初の敵または壁で消滅する（同一の敵への命中は1波につき1回まで）。再使用まで6秒。", "Every 6 basic attack hits, fire 6 projectiles in all directions at 60-degree intervals. Each deals 12% of whichever is higher: your Attack or Ability Power (cap: 40%), travels at 12m/s, has a 6m range and is 0.4m wide. Projectiles disappear on the first enemy or wall they hit. Each enemy can be hit only once per wave. Cooldown: 6 seconds.", C("DemonVolley",12,40),
                new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"DemonVolley",BossShape.Radial,cooldownMillis:6000,lifetimeMillis:500,count:6,mainHits:6,rangeMilli:6000,widthMilli:400,speedMilli:12000,angleMilli:60000,firstHitOnly:true)),
            P("hands", "通常攻撃が4回命中するごとに命中地点に予告を表示し、0.65秒後に半径2mに攻撃力・魔力の高い方の25%（上限80%）の打撃を与える。再使用まで3秒。", "Every 4 basic attack hits, mark the hit location with a warning. After 0.65 seconds, strike a 2m radius for 25% of whichever is higher: your Attack or Ability Power (cap: 80%). Cooldown: 3 seconds.", C("DemonDelay",25,80),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"DemonDelay",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:650,lifetimeMillis:650,mainHits:4,radiusMilli:2000)),
            P("feet", "自身の移動スキルまたはテレポート完了後、3秒以内に行う次の通常攻撃時に、半径3m、攻撃力・魔力の高い方の20%（上限60%）の踏みつけを発生させる。再使用まで4秒。", "Your next basic attack within 3 seconds after your movement skill or teleport ends causes a stomp in a 3m radius, dealing 20% of whichever is higher: your Attack or Ability Power (cap: 60%). Cooldown: 4 seconds.", C("DemonArrival",20,60),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:3000,ledgerId:"DemonArrival"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonArrival",anchor:BossAnchor.Hit,cooldownMillis:4000,radiusMilli:3000,ledgerId:"DemonArrival")),
            P("stage2", "移動スキルまたはテレポートの到着地点に、半径3m、攻撃力・魔力の高い方の50%（上限100%）の踏みつけを発生させる。再使用まで3秒。", "When your movement skill or teleport ends, cause a stomp at your destination in a 3m radius, dealing 50% of whichever is higher: your Attack or Ability Power (cap: 100%). Cooldown: 3 seconds.", C("DemonStomp",50,100),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonStomp",anchor:BossAnchor.Destination,cooldownMillis:3000,radiusMilli:3000)),
            P("stage3", "自身の装備効果や2セット効果による踏みつけ時、攻撃力・魔力の高い方の45%（上限90%）の樹芽を設置する（生成クールダウン1秒、最大4個まで）。通常攻撃は命中地点から4m以内の最も近い樹芽を、着地時は自身から8m以内でカーソルに最も近い樹芽を1つ回収する（回収クールダウン共通1秒、生成から0.15秒後より回収可能）。", "Stomps caused by your equipment or 2-piece set effect plant a bud that deals 45% of whichever is higher: your Attack or Ability Power (cap: 90%). Bud creation has a 1-second cooldown, and up to 4 buds can exist. Basic attacks collect the nearest bud within 4m of the hit location. On landing, collect the bud nearest the cursor within 8m of you. Both collection methods share a 1-second cooldown. Buds can be collected 0.15 seconds after creation.", C("DemonGrove",45,90),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Hit,cooldownMillis:1000,rangeMilli:4000),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Cursor,cooldownMillis:1000,rangeMilli:8000),
                Bud(BossEvent.MainHit,"DemonGrove",BossAnchor.Hit,1000)),
            new BossMoveProfile("boss_demon.stage6",DemonSetId,
                new Txt("樹芽の回収時、自身から樹芽へ向けて最大8mの直線を形成し、1/3地点・2/3地点・終点にそれぞれ0秒／0.25秒／0.5秒後に半径2.5m、各攻撃力・魔力の高い方の30%（合計90%、上限100%）の衝撃波を発生させる。さらに始点と終点に回収不可の樹芽（各38%、上限40%）を1つずつ再設置する（最大4個の制限を共有、同一地点の場合はカーソル方向6mに設置。派生効果からは再設置・回収は発生しない）。再使用まで6秒。", "When you collect a bud, form a line up to 8m long from you toward it. Shockwaves erupt at one-third of the line, two-thirds of the line, and its end after 0, 0.25, and 0.5 seconds respectively. Each has a 2.5m radius and deals 30% of whichever is higher: your Attack or Ability Power (90% total, cap: 100%). Also plant one uncollectible bud at each end, each dealing 38% (cap: 40%) and sharing the 4-bud limit. If both ends coincide, plant a bud 6m toward the cursor. These follow-up effects cannot plant or collect more buds. Cooldown: 6 seconds."),
                new[] { C("DemonMarch",90,100),C("DemonReplant",38,40) },new[]
                {
                    new BossAction(BossEvent.Clock,BossMechanism.ShapeAttack,BossPayload.March,"DemonMarch",BossShape.Column,BossAnchor.Collected,cooldownMillis:6000,intervalMillis:250,count:3,radiusMilli:2500,rangeMilli:8000,maxInstances:4,magnitudeMilli:6000),
                    Bud(BossEvent.Clock,"DemonReplant",BossAnchor.Collected,collectible:false,count:2),
                }),
        };
        private static BossRewardProfile[] CreateRewards()
        {
            BossRewardAction Pair() => new BossRewardAction(BossRewardActionKind.HysteriaPair,cooldownMillis:3000,count:2,order:0);
            BossRewardAction Plant() => new BossRewardAction(BossRewardActionKind.HysteriaPlant,45000,90000,1000,count:1,rangeMilli:8000,order:1);
            BossRewardAction Collect() => new BossRewardAction(BossRewardActionKind.HysteriaCollect,cooldownMillis:1000,rangeMilli:4000,order:2);
            return new[] { new BossRewardProfile(DemonRewardId,DemonSetId,"St_U_Hysteria",BossRewardAdapter.Hysteria,new[]
            {
                new BossRewardStage(1,new Txt("左右の爪撃を1組とし、片方でも命中すると、樹界装備の部位専用カウンタのみを進める主撃を1回発生させ（通常攻撃時発動の他効果は誘発しない）、自身を中心に2セット効果の踏みつけを行う（移動スキルによる踏みつけと再使用時間3秒を共有）。", "Treat the left and right claw strikes as one pair. If either hits, count one hit toward only the Forest Realm equipment counters, without triggering other basic-attack effects, and activate the 2-piece stomp centered on you. This shares its 3-second cooldown with the movement-skill stomp."),new[] { Pair() }),
                new BossRewardStage(2,new Txt("段階1の効果に加え、左爪が最初に命中した地点に攻撃力・魔力の高い方の45%（上限90%）の樹芽を設置し（踏みつけによる生成とクールダウン1秒を共有）、続く右爪が最初に命中した地点から4m以内の最も近い樹芽を回収する（通常の回収とクールダウン1秒を共有）。", "In addition to Stage 1, plant a bud at the first location hit by the left claw, dealing 45% of whichever is higher: your Attack or Ability Power (cap: 90%) and sharing the 1-second bud-creation cooldown with stomps. The following right claw collects the nearest bud within 4m of its first hit location, sharing the normal 1-second collection cooldown."),new[] { Pair(),Plant(),Collect() }),
                new BossRewardStage(3,new Txt("段階2の効果に加え、右爪での回収時にも6セット効果の衝撃波列と再設置が発動する（共有クールダウン6秒）。また、「ヒステリー」による自身の移動速度低下ペナルティが-50%から-25%へと緩和される。その他のダッシュ、間隔、回復、持続時間は変更されない。", "In addition to Stage 2, collecting a bud with the right claw also triggers the 6-piece shockwave sequence and bud replanting, sharing their 6-second cooldown. Hysteria's movement speed penalty is reduced from -50% to -25%. Its other dash, interval, healing, and duration effects remain unchanged."),new[] { Pair(),Plant(),Collect(),
                    new BossRewardAction(BossRewardActionKind.HysteriaMarch,cooldownMillis:6000,rangeMilli:8000,order:3),
                    new BossRewardAction(BossRewardActionKind.NativeSpeed,-25000,50000,order:4) }),
            }) };
        }
        internal static IEnumerable<string> FingerprintRecords()
        {
            yield return "boss-schema:v2:" + MaxEntries + ":" + BudDelayMillis + ":" + BudCollectibleDelayMillis + ":" + BudRadiusMilli + ":" + MaxBuds + ":" + NativeHysteriaSpeedMilli;
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

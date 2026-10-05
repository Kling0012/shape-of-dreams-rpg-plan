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
            P("weapon", "通常攻撃が3回命中するごとに、命中地点に半径3m、攻撃力・魔力の高い方の25%（上限80%）の踏みつけを発生させる。再使用まで2秒。", "Every 3 main hits: a 3m stomp at the hit point for 25%H (cap80), CD2s.", C("DemonImpact",25,80),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonImpact",anchor:BossAnchor.Hit,cooldownMillis:2000,mainHits:3,radiusMilli:3000)),
            P("armor", "自身の移動スキル完了時、0.6秒間行動妨害無効（上限1秒）を獲得する。再使用まで8秒（効果時間は強化で増加しない）。", "After your native movement skill completes: Unstoppable for 0.6s (cap1s), CD8s. Duration never scales.", C("DemonStride",6,10,BossCoefficientKind.Duration),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Defense,BossPayload.Unstoppable,"DemonStride",cooldownMillis:8000,lifetimeMillis:600)),
            P("charm", "通常記憶または奥義記憶を使ったとき、カーソル方向8m以内に攻撃力・魔力の高い方の20%（上限60%）の樹芽を設置する。樹芽は1.2秒後に半径2.5mへ噴出する（最大4個まで設置可能）。再使用まで4秒。", "Confirmed normal/ultimate memory use plants a 20%H bud (cap60) within 8m toward cursor, CD4s. Erupts after1.2s in2.5m, max4 buds.", C("DemonSeed",20,60), Bud(BossEvent.MemoryUse,"DemonSeed",BossAnchor.Cursor,4000)),
            P("head", "通常攻撃が6回命中するごとに、全方位（60度間隔）に6本の直線弾を放つ。弾は各攻撃力・魔力の高い方の12%（上限40%）、弾速12m/秒、射程6m、幅0.4mで、最初の敵または壁で消滅する（同一の敵への命中は1波につき1回まで）。再使用まで6秒。", "Every6 main hits: 6 radial missiles at60 degrees,12%H each(cap40), speed12m/s,range6m,width0.4m; stop on first enemy/wall, one hit per enemy per wave, CD6s.", C("DemonVolley",12,40),
                new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"DemonVolley",BossShape.Radial,cooldownMillis:6000,lifetimeMillis:500,count:6,mainHits:6,rangeMilli:6000,widthMilli:400,speedMilli:12000,angleMilli:60000,firstHitOnly:true)),
            P("hands", "通常攻撃が4回命中するごとに命中地点に予告を表示し、0.65秒後に半径2mに攻撃力・魔力の高い方の25%（上限80%）の打撃を与える。再使用まで3秒。", "Every4 main hits: fixed hit-point warning, after0.65s a2m strike for25%H(cap80), CD3s; no tracking.", C("DemonDelay",25,80),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"DemonDelay",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:650,lifetimeMillis:650,mainHits:4,radiusMilli:2000)),
            P("feet", "自身の移動スキルまたはテレポート完了後、3秒以内に行う次の通常攻撃時に、半径3m、攻撃力・魔力の高い方の20%（上限60%）の踏みつけを発生させる。再使用まで4秒。", "The next main hit within3s of your native movement/confirmed teleport causes a3m stomp for20%H(cap60), CD4s.", C("DemonArrival",20,60),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:3000,ledgerId:"DemonArrival"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonArrival",anchor:BossAnchor.Hit,cooldownMillis:4000,radiusMilli:3000,ledgerId:"DemonArrival")),
            P("stage2", "移動スキルまたはテレポートの到着地点に、半径3m、攻撃力・魔力の高い方の50%（上限100%）の踏みつけを発生させる。再使用まで3秒。", "Native movement completion/confirmed teleport:3m stomp at destination for50%H(cap100), CD3s.", C("DemonStomp",50,100),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonStomp",anchor:BossAnchor.Destination,cooldownMillis:3000,radiusMilli:3000)),
            P("stage3", "自身の装備効果や2セット効果による踏みつけ時、攻撃力・魔力の高い方の45%（上限90%）の樹芽を設置する（生成クールダウン1秒、最大4個まで）。通常攻撃は命中地点から4m以内の最も近い樹芽を、着地時は自身から8m以内でカーソルに最も近い樹芽を1つ回収する（回収クールダウン共通1秒、生成から0.15秒後より回収可能）。", "Your part/2-piece stomps plant45%H buds(cap90,plant CD1s). Main hit collects nearest mature bud within4m of hit; landing collects cursor-nearest bud within8m of owner(shared CD1s,mature0.15s). Max4 buds.", C("DemonGrove",45,90),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Hit,cooldownMillis:1000,rangeMilli:4000),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Cursor,cooldownMillis:1000,rangeMilli:8000),
                Bud(BossEvent.MainHit,"DemonGrove",BossAnchor.Hit,1000)),
            new BossMoveProfile("boss_demon.stage6",DemonSetId,
                new Txt("樹芽の回収時、自身から樹芽へ向けて最大8mの直線を形成し、1/3地点・2/3地点・終点にそれぞれ0秒／0.25秒／0.5秒後に半径2.5m、各攻撃力・魔力の高い方の30%（合計90%、上限100%）の衝撃波を発生させる。さらに始点と終点に回収不可の樹芽（各38%、上限40%）を1つずつ再設置する（最大4個の制限を共有、同一地点の場合はカーソル方向6mに設置。派生効果からは再設置・回収は発生しない）。再使用まで6秒。", "On collection: a line up to8m from owner to bud; strikes at1/3,2/3,end after0/0.25/0.5s,2.5m each30%H(total90,cap100,CD6s). Replant start then end with uncollectible38%H buds(cap40,shared4-bud limit). Coincident points use6m cursor direction. Generated strikes cannot restart the loop."),
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
                new BossRewardStage(1,new Txt("左右の爪撃を1組とし、片方でも命中すると、樹界装備の部位専用カウンタのみを進める主撃を1回発生させ（通常攻撃時発動の他効果は誘発しない）、自身を中心に2セット効果の踏みつけを行う（移動スキルによる踏みつけと再使用時間3秒を共有）。", "One native left/right claw pair with at least one successful hit issues one forest main hit and tries the2-piece stomp(shared movement CD3s); advances only equipped part counters."),new[] { Pair() }),
                new BossRewardStage(2,new Txt("段階1の効果に加え、左爪が最初に命中した地点に攻撃力・魔力の高い方の45%（上限90%）の樹芽を設置し（踏みつけによる生成とクールダウン1秒を共有）、続く右爪が最初に命中した地点から4m以内の最も近い樹芽を回収する（通常の回収とクールダウン1秒を共有）。", "Stage1 plus first successful left claw plants45%H bud(CD1s); first successful next right claw collects nearest mature bud within4m(shared collection CD1s)."),new[] { Pair(),Plant(),Collect() }),
                new BossRewardStage(3,new Txt("段階2の効果に加え、右爪での回収時にも6セット効果の衝撃波列と再設置が発動する（共有クールダウン6秒）。また、「ヒステリー」による自身の移動速度低下ペナルティが-50%から-25%へと緩和される。その他のダッシュ、間隔、回復、持続時間は変更されない。", "Stage2 plus right-claw collection enables6-piece march/replant(shared CD6s). Replace only native Hysteria speed modifier -50 with -25; preserve native dash/interval/locks/heal/duration."),new[] { Pair(),Plant(),Collect(),
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

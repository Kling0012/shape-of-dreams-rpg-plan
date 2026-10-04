using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static class BossProfiles
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
        public const string CombatContract = "freeze-max-ad-ap;physical-tie;neutral;one-defense;no-crit;no-attackeffect;no-self-generation;legal-ground-los;clear-on-epoch-death-room-memory";
        public const string VisualContract = "owner-run-zone-room-equipment-epoch-revision;max-live64;effect-id-update-only;snapshot1000ms-unscaled;game-time-deadlines;offset-synced-mirror-networktime-sentAt-times-timescale;remaining-time-only;invalidate-epoch";
        public static IReadOnlyList<BossMoveProfile> Moves { get; } = Array.AsReadOnly(CreateMoves());
        public static IReadOnlyList<BossRewardProfile> Rewards { get; } = Array.AsReadOnly(CreateRewards());
        private static readonly Dictionary<string, BossMoveProfile> moves = Moves.ToDictionary(x => x.Id, StringComparer.Ordinal);
        private static readonly Dictionary<string, BossRewardProfile> rewards = Rewards.ToDictionary(x => x.Id, StringComparer.Ordinal);
        public static bool TryGetMove(string id, out BossMoveProfile profile)
        { profile = null; return id != null && moves.TryGetValue(id, out profile); }
        public static bool TryGetReward(string id, out BossRewardProfile profile)
        { profile = null; return id != null && rewards.TryGetValue(id, out profile); }
        public static string DescribeMove(string id) => TryGetMove(id, out var p) ? p.Description.ToString() : "";
        public static string DescribeReward(string id, int stage) => TryGetReward(id, out var p) && stage >= 1 && stage <= 3 ? p.Stages[stage - 1].Description.ToString() : "";
        private static BossChannelDef C(string id, int value, int cap, BossCoefficientKind kind = BossCoefficientKind.Damage) => new BossChannelDef(id, value * 1000, cap * 1000, kind);
        private static BossMoveProfile P(string id, string ja, string en, BossChannelDef channel, params BossAction[] actions)
            => new BossMoveProfile("boss_demon." + id, DemonSetId, new Txt(ja, en), new[] { channel }, actions);
        private static BossAction Bud(BossEvent e, string channel, BossAnchor anchor, int cooldown = 0, bool collectible = true, int count = 1)
            => new BossAction(e, BossMechanism.Field, BossPayload.Bud, channel, anchor: anchor, cooldownMillis: cooldown,
                delayMillis: BudDelayMillis, lifetimeMillis: BudDelayMillis, radiusMilli: BudRadiusMilli, rangeMilli: 8000,
                maxInstances: MaxBuds, collectible: collectible, count: count, magnitudeMilli: BudCollectibleDelayMillis);
        private static BossMoveProfile[] CreateMoves() => new[]
        {
            P("weapon", "主撃3回で命中地点に半径3mの踏みつけ25%H（上限80）、CD2秒。", "Every 3 main hits: a 3m stomp at the hit point for 25%H (cap80), CD2s.", C("DemonImpact",25,80),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonImpact",anchor:BossAnchor.Hit,cooldownMillis:2000,mainHits:3,radiusMilli:3000)),
            P("armor", "本人移動スキル完了で0.6秒Unstoppable（上限1秒）、CD8秒。強化で時間は増えない。", "After your native movement skill completes: Unstoppable for 0.6s (cap1s), CD8s. Duration never scales.", C("DemonStride",6,10,BossCoefficientKind.Duration),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Defense,BossPayload.Unstoppable,"DemonStride",cooldownMillis:8000,lifetimeMillis:600)),
            P("charm", "通常・奥義記憶使用確定でカーソル方向8m以内に20%Hの樹芽（上限60）、CD4秒。1.2秒後半径2.5mに噴出、最大4芽。", "Confirmed normal/ultimate memory use plants a 20%H bud (cap60) within 8m toward cursor, CD4s. Erupts after1.2s in2.5m, max4 buds.", C("DemonSeed",20,60), Bud(BossEvent.MemoryUse,"DemonSeed",BossAnchor.Cursor,4000)),
            P("head", "主撃6回で60度間隔の直線弾6本、各12%H（上限40）。速度12m/秒、射程6m、幅0.4m、最初の敵/壁で消滅、同敵は波内1命中、CD6秒。", "Every6 main hits: 6 radial missiles at60 degrees,12%H each(cap40), speed12m/s,range6m,width0.4m; stop on first enemy/wall, one hit per enemy per wave, CD6s.", C("DemonVolley",12,40),
                new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"DemonVolley",BossShape.Radial,cooldownMillis:6000,lifetimeMillis:500,count:6,mainHits:6,rangeMilli:6000,widthMilli:400,speedMilli:12000,angleMilli:60000,firstHitOnly:true)),
            P("hands", "主撃4回で命中地点を予告、0.65秒後半径2mに25%H（上限80）、CD3秒。追尾しない。", "Every4 main hits: fixed hit-point warning, after0.65s a2m strike for25%H(cap80), CD3s; no tracking.", C("DemonDelay",25,80),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.Damage,"DemonDelay",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:650,lifetimeMillis:650,mainHits:4,radiusMilli:2000)),
            P("feet", "本人移動スキル完了/確認済みTeleport後3秒以内の次の主撃に半径3mの踏みつけ20%H（上限60）、CD4秒。", "The next main hit within3s of your native movement/confirmed teleport causes a3m stomp for20%H(cap60), CD4s.", C("DemonArrival",20,60),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:3000,ledgerId:"DemonArrival"),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonArrival",anchor:BossAnchor.Hit,cooldownMillis:4000,radiusMilli:3000,ledgerId:"DemonArrival")),
            P("stage2", "移動スキル完了/確認済みTeleport地点に半径3mの踏みつけ50%H（上限100）、CD3秒。", "Native movement completion/confirmed teleport:3m stomp at destination for50%H(cap100), CD3s.", C("DemonStomp",50,100),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.ShapeAttack,BossPayload.Damage,"DemonStomp",anchor:BossAnchor.Destination,cooldownMillis:3000,radiusMilli:3000)),
            P("stage3", "本人の部位/2点踏みつけに45%Hの樹芽（上限90、生成CD1秒）。主撃は命中地点4m内の最近芽、着地は本人8m内でカーソルに最も近い芽を1つ回収（共通CD1秒、生成0.15秒後から）。最大4芽。", "Your part/2-piece stomps plant45%H buds(cap90,plant CD1s). Main hit collects nearest mature bud within4m of hit; landing collects cursor-nearest bud within8m of owner(shared CD1s,mature0.15s). Max4 buds.", C("DemonGrove",45,90),
                new BossAction(BossEvent.MainHit,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Hit,cooldownMillis:1000,rangeMilli:4000),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Field,BossPayload.CollectBud,anchor:BossAnchor.Cursor,cooldownMillis:1000,rangeMilli:8000),
                Bud(BossEvent.MainHit,"DemonGrove",BossAnchor.Hit,1000)),
            new BossMoveProfile("boss_demon.stage6",DemonSetId,
                new Txt("回収時に本人から芽へ最大8mの列、1/3・2/3・終点へ0/0.25/0.5秒、半径2.5m各30%H（合計90、上限100、CD6秒）。開始→終点に38%Hの回収不可芽を各1つ（上限40、共通4芽）。同地点はカーソル方向6m。派生は再植樹/回収を再発動しない。", "On collection: a line up to8m from owner to bud; strikes at1/3,2/3,end after0/0.25/0.5s,2.5m each30%H(total90,cap100,CD6s). Replant start then end with uncollectible38%H buds(cap40,shared4-bud limit). Coincident points use6m cursor direction. Generated strikes cannot restart the loop."),
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
                new BossRewardStage(1,new Txt("左右2爪で1組。片方以上の成功で森の主撃1回と2点踏みつけ（移動とCD3秒共有）。部位専用カウンタだけ進む。", "One native left/right claw pair with at least one successful hit issues one forest main hit and tries the2-piece stomp(shared movement CD3s); advances only equipped part counters."),new[] { Pair() }),
                new BossRewardStage(2,new Txt("段階1＋左爪の最初の成功地点に45%Hの樹芽（CD1秒）、次の右爪の最初の成功地点4m内で最近芽を回収（通常とCD1秒共有）。", "Stage1 plus first successful left claw plants45%H bud(CD1s); first successful next right claw collects nearest mature bud within4m(shared collection CD1s)."),new[] { Pair(),Plant(),Collect() }),
                new BossRewardStage(3,new Txt("段階2＋右爪回収でも6点の列と再植樹（共有CD6秒）。本体ヒステリーの移動速度補正だけ-50→-25、その他のdash/間隔/ロック/回復/持続は変更なし。", "Stage2 plus right-claw collection enables6-piece march/replant(shared CD6s). Replace only native Hysteria speed modifier -50 with -25; preserve native dash/interval/locks/heal/duration."),new[] { Pair(),Plant(),Collect(),
                    new BossRewardAction(BossRewardActionKind.HysteriaMarch,cooldownMillis:6000,rangeMilli:8000,order:3),
                    new BossRewardAction(BossRewardActionKind.NativeSpeed,-25000,50000,order:4) }),
            }) };
        }
        internal static IEnumerable<string> FingerprintRecords()
        {
            yield return "boss-schema:v1:" + MaxEntries + ":" + BudDelayMillis + ":" + BudCollectibleDelayMillis + ":" + BudRadiusMilli + ":" + MaxBuds + ":" + NativeHysteriaSpeedMilli;
            yield return "boss-order:" + DemonEventOrder;
            yield return "boss-native:" + NativeContract;
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

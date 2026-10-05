using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public static partial class BossProfiles
    {
        public const string WhiteNightSetId = "set.boss_white_night";
        public const string WhiteNightRewardId = "boss_white_night.beam";
        public const string DarkMoonSetId = "set.boss_dark_moon";
        public const string DarkMoonRewardId = "boss_dark_moon.beam";
        public const string InkEventOrder = "main-start-existing-executed-palm-domain;consume-domain;white-six;white-wave;dark-form;single-target-mark;dark-chain-finish;pieces;new-white-domain;native-branch;past-success-eligible-flat;native-final-success;white-overheal-shield;independent-dark-spear;independent-illumination";
        public const string InkNativeContract = "one-parent-se-one-pooled-child-life-one-adapter;exact-native-hit-branch-checked-il-observed-once;child-only-processor-and-success-events;host-owner-parent-skill-room-run-generation;exclude-generated-reflection-summon;native-original-damage-heal-source-element-proc-steering-range-interval-endtime-timer-lock-invulnerability-unchanged;flat-add-spend-on-packet-no-refund;past-success-dwell-only-expired-slot-replacement-oldest-last-success-netid;independent-owner-global-add-spear-illumination-gates-parent-scoped-counts-budgets;freeze-spawn-H-and-magic-including-illumination-forms-marks-finishes;first-native-healed-heroes-fixed;actual-discarded-heal-only;shield-updates-spend-parent-budget;own-shield-highest-nonadditive-cap-native-initial-actual-linked-normal-reward-consumption-no-resurrection;no-native-heal-subtraction;profile-signature-independent-invalidation;unchanged-profile-ledger-container-survives-other-profile-unequip;live-current-stage-reapplication-retains-parent-tombstoned-spent-counts-owner-cooldowns-frozen-H-and-first-hero-slots-no-quota-refill;changed-profile-clears-own-dwell-marks-reservations-shields-only;terminal-removes-only-own-subscriptions-processors-containers-native-reservations-native-mark-contributions-last-source-form-chain";
        private static BossChannelDef InkChannel(string id, int value, int cap = 0, BossCoefficientKind kind = BossCoefficientKind.Damage)
            => new BossChannelDef(id, value * 1000, (cap == 0 ? value : cap) * 1000, kind);
        private static BossMoveProfile InkProfile(bool white, string id, string ja, string en, BossChannelDef[] channels, params BossAction[] actions)
            => new BossMoveProfile((white ? "boss_white_night." : "boss_dark_moon.") + id, white ? WhiteNightSetId : DarkMoonSetId, new Txt(ja, en), channels, actions);
        internal static BossMoveProfile[] CreateInkMoves() => new[]
        {
            InkProfile(true,"weapon","通常攻撃が3回命中するごとに、命中地点に0.65秒の予告後、半径2.5mに攻撃力・魔力の高い方の25%の掌撃を放つ。再使用まで3秒。", "Every3 main hits: warn at the frozen hit point for0.65s, then a2.5m palm for25%H. CD3s, one reservation.",new[]{InkChannel("LotusPalm",25)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusPalm",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:650,mainHits:3,maxInstances:1,radiusMilli:2500,element:BossElement.Light)),
            InkProfile(true,"armor","被ダメージ後、自身の位置に1.5秒間、半径2mの領域を展開する。領域内にいる自身に対し、1.5秒間、攻撃力・魔力の高い方の15%分の障壁を1回付与する。再使用まで8秒。", "After native damage taken: a fixed2m haven for1.5s; shield the owner inside once for15%H/1.5s. CD8s, one domain.",new[]{InkChannel("LotusHaven",15,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Field,BossPayload.Shield,"LotusHaven",cooldownMillis:8000,lifetimeMillis:1500,maxInstances:1,maxTargets:1,radiusMilli:2000,magnitudeMilli:380)),
            InkProfile(true,"charm","記憶を使ったとき、自身の位置に半径が3mから2mへと1.2秒かけて縮小する領域を展開する。領域の終了時、範囲内にいる自身と最も近い味方ヒーロー1人に対し、それぞれ2秒間、攻撃力・魔力の高い方の12%分の障壁を付与する。再使用まで6秒。", "Confirmed memory use: a domain shrinks3→2m over1.2s at the owner. On expiry shield owner and nearest allied hero inside for12%H/2s each. CD6s, one domain; distance ties use netId.",new[]{InkChannel("LotusSafe",12,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Shield,"LotusSafe",cooldownMillis:6000,delayMillis:1200,lifetimeMillis:2000,maxInstances:1,maxTargets:2,radiusMilli:3000,widthMilli:2000,magnitudeMilli:380)),
            InkProfile(true,"head","通常攻撃が5回命中するごとに、-30°/0°/+30°の3方向へ0秒/0.2秒/0.4秒の間隔で範囲5m・角度60°の光の扇状波を放ち、それぞれ攻撃力・魔力の高い方の10%のダメージを与える（再使用まで5秒、1連撃のみ）。", "Every5 main hits: three5m/60° Light fans at−30/0/+30°, after0/0.2/0.4s,10%H each. CD5s, one sequence.",new[]{InkChannel("LotusWave",30)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusWave",BossShape.Fan,cooldownMillis:5000,intervalMillis:200,count:3,mainHits:5,maxInstances:1,rangeMilli:5000,angleMilli:60000,magnitudeMilli:30000,element:BossElement.Light)),
            InkProfile(true,"hands","通常攻撃が4回命中するごとに、命中地点から2m以内にいるボス以外の行動妨害が無効でない敵を最大1m引き寄せ、0.35秒後に攻撃力・魔力の高い方の20%の掌打ダメージを与える（再使用まで4秒、地形を通過せず自身も移動しない）。", "Every4 main hits: pull non-boss, non-CC-immune enemies within2m up to1m, then a20%H palm after0.35s. CD4s, one reservation; no terrain crossing or owner movement.",new[]{InkChannel("LotusInch",20)},
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Pull,anchor:BossAnchor.Hit,radiusMilli:2000,magnitudeMilli:1000,element:BossElement.Light),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusInch",anchor:BossAnchor.Hit,cooldownMillis:4000,delayMillis:350,mainHits:4,maxInstances:1,radiusMilli:2000,element:BossElement.Light)),
            InkProfile(true,"feet","移動完了後2秒以内に行う次の通常攻撃が命中したとき、到達地点から命中方向へ長さ6m・幅1mの直線状に攻撃力・魔力の高い方の20%のダメージを与える（再使用まで4秒）。", "The next main hit within2s of native movement completion sends a6m/1m-wide20%H line from the frozen arrival point toward the hit. CD4s.",new[]{InkChannel("LotusArrival",20)},
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusArrival",BossShape.Line,cooldownMillis:4000,rangeMilli:6000,widthMilli:1000,element:BossElement.Light)),
            InkProfile(true,"stage2","通常攻撃が3回命中するごとに自身の位置に半径3mの白蓮領域を展開し、0.75秒後に攻撃力・魔力の高い方の40%の掌打ダメージを与える。掌打後、領域は2秒間持続し、領域内の自身と最も近い味方ヒーロー1人に攻撃力・魔力の高い方の15%の障壁を2秒間付与する（各1回、再使用まで4秒、同時に存在できる領域は1つ）。", "Every3 main hits: a3m lotus domain;40%H palm after0.75s, then persist2s. Shield owner and nearest allied hero inside once each for15%H/2s. CD4s, one domain.",new[]{InkChannel("LotusDomainPalm",40),InkChannel("LotusDomainShield",15,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusDomainPalm",cooldownMillis:4000,delayMillis:750,lifetimeMillis:2000,mainHits:3,maxInstances:1,radiusMilli:3000,element:BossElement.Light),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"LotusDomainShield",lifetimeMillis:2000,maxTargets:2,radiusMilli:3000,magnitudeMilli:380)),
            InkProfile(true,"stage3","掌打が発動済みの自身の領域内にいる状態で通常攻撃が命中したとき、命中方向へ範囲6m・角度90°の波を放ち、攻撃力・魔力の高い方の30%のダメージを与える（再使用まで2秒。予告中やこの攻撃で新しく生成された領域は対象外）。", "A main hit starting inside your already-mature domain sends a6m/90°30%H wave toward the hit. CD2s; excludes warnings and domains created by this event.",new[]{InkChannel("LotusDomainWave",30)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusDomainWave",BossShape.Fan,cooldownMillis:2000,rangeMilli:6000,angleMilli:90000,element:BossElement.Light)),
            InkProfile(true,"stage6","同じ掌打発動済みの領域内にいる状態で通常攻撃が3回命中すると（波の再使用待機中もカウント）、領域を消費して半径5mに境界波を放ち攻撃力・魔力の高い方の90%のダメージを与える（上限は100%）。さらに元の領域内にいたヒーロー最大2人に、攻撃力・魔力の高い方の38%の障壁を2秒間付与する（上限は40%、再使用まで6秒。白夜セットの障壁は最大値で上書きされ、上限は攻撃力・魔力の高い方の38%）。", "Three main hits in the same mature domain, including wave CD: consume domain, then5m90%H boundary wave(cap100) and38%H shield/2s(cap40) for up to2 heroes inside the original domain. CD6s. Parts/new domains follow; all white shields use highest replacement, cap38%H.",new[]{InkChannel("LotusBoundary",90,100),InkChannel("LotusBoundaryShield",38,40,BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusBoundary",cooldownMillis:6000,mainHits:3,radiusMilli:5000,element:BossElement.Light),
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Shield,"LotusBoundaryShield",lifetimeMillis:2000,maxTargets:2,radiusMilli:3000,magnitudeMilli:380)),
            InkProfile(false,"weapon","通常攻撃が3回命中するごとに、前方範囲4m・角度90°に刃を放ち、攻撃力・魔力の高い方の25%のダメージを与える（再使用まで3秒）。", "Every3 main hits: a forward4m/90°25%H blade. CD3s.",new[]{InkChannel("MoonBlade",25)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonBlade",BossShape.Fan,cooldownMillis:3000,mainHits:3,rangeMilli:4000,angleMilli:90000,element:BossElement.Dark)),
            InkProfile(false,"armor","ダメージを受けた後、攻撃者の方向8m以内の地点に0.8秒の予告後、範囲2mに槌を叩きつけて攻撃力・魔力の高い方の20%のダメージを与える（再使用まで8秒、無敵効果なし）。", "After native damage taken: warn at the attacker point within8m for0.8s, then a2m20%H hammer. CD8s, one reservation, no invulnerability.",new[]{InkChannel("MoonWrath",20)},
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonWrath",anchor:BossAnchor.Hit,cooldownMillis:8000,delayMillis:800,maxInstances:1,radiusMilli:2000,rangeMilli:8000,element:BossElement.Dark)),
            InkProfile(false,"charm","記憶を使ったとき、照準方向へ弾速12m/秒で投槍を1本放ち、長さ8m・幅0.5mの軌道上の最初の敵か壁に当たるまで進んで攻撃力・魔力の高い方の20%のダメージを与える（再使用まで5秒）。", "Confirmed memory use: one20%H spear toward the cursor,12m/s,8m/0.5m wide; stops on first enemy/wall. CD5s, one shot.",new[]{InkChannel("MoonThrow",20)},
                new BossAction(BossEvent.MemoryUse,BossMechanism.Projectile,BossPayload.Damage,"MoonThrow",cooldownMillis:5000,maxInstances:1,rangeMilli:8000,widthMilli:500,speedMilli:12000,firstHitOnly:true,element:BossElement.Dark)),
            InkProfile(false,"head","通常攻撃が5回命中するごとに、命中地点（最大8m）へ弾速12m/秒の剣弾を放つ。終点の半径2mにのみ攻撃力・魔力の高い方の25%のダメージを与える（壁で消滅、再使用まで5秒、召喚物ではない）。", "Every5 main hits: a12m/s sword projectile to the frozen hit point(up to8m); only its2m endpoint deals25%H. Walls cancel it. CD5s, not a Summon.",new[]{InkChannel("MoonEgo",25)},
                new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"MoonEgo",anchor:BossAnchor.Hit,cooldownMillis:5000,mainHits:5,maxInstances:1,radiusMilli:2000,rangeMilli:8000,widthMilli:500,speedMilli:12000,element:BossElement.Dark)),
            InkProfile(false,"hands","通常攻撃が4回命中するごとに、前方長さ5m・幅0.8mの直線状に攻撃力・魔力の高い方の20%のダメージを与え、ボス以外の行動妨害が無効でない敵のみを前方へ最大1m押し出す（再使用まで4秒、地形は通過しない）。", "Every4 main hits: a5m/0.8m-wide20%H line; push only non-boss/non-CC-immune enemies forward up to1m. CD4s, no terrain crossing.",new[]{InkChannel("MoonCarry",20)},
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Push,"MoonCarry",BossShape.Line,cooldownMillis:4000,mainHits:4,rangeMilli:5000,widthMilli:800,magnitudeMilli:1000,element:BossElement.Dark)),
            InkProfile(false,"feet","移動完了後2秒以内に行う次の通常攻撃が命中したとき、到達地点の幻影が0.4秒間予告した後、命中方向へ長さ5m・幅1mの直線状に攻撃力・魔力の高い方の25%のダメージを与える（再使用まで4秒、実体は伴わない）。", "Next main hit within2s of native movement: a fixed phantom at arrival warns for0.4s, then a5m/1m-wide25%H line toward the hit. CD4s, no Entity.",new[]{InkChannel("MoonArrival",25)},
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonArrival",BossShape.Line,cooldownMillis:4000,delayMillis:400,maxInstances:1,rangeMilli:5000,widthMilli:1000,element:BossElement.Dark)),
            InkProfile(false,"stage2","通常攻撃が命中するごとに「刃（前方4m・角度90°に攻撃力・魔力の高い方の25%ダメージ）」→「槍（長さ7m・幅0.8mに攻撃力・魔力の高い方の25%ダメージ）」→「槌（0.6秒予告後、半径2.5mに攻撃力・魔力の高い方の40%ダメージ）」へと順に派生する（再使用時間1秒、待機中は進行・蓄積不可。刃から開始し、6秒間派生しないとリセット）。", "Main hits cycle blade4m/90°25%H → spear7m/0.8m25%H → hammer0.6s warning/2.5m40%H. CD1s: neither advance nor bank during CD. Start blade; reset after6s inactivity.",new[]{InkChannel("MoonForm",25),InkChannel("MoonFormHammer",40)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonForm",BossShape.Fan,cooldownMillis:1000,counterLifetimeMillis:6000,count:3,rangeMilli:4000,angleMilli:90000,element:BossElement.Dark),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonForm",BossShape.Line,rangeMilli:7000,widthMilli:800,element:BossElement.Dark),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonFormHammer",anchor:BossAnchor.Hit,delayMillis:600,maxInstances:1,radiusMilli:2500,element:BossElement.Dark)),
            InkProfile(false,"stage3","2セット効果が命中した敵1体に「月印」を付与する（最大3蓄積、持続6秒）。3蓄積時に印を消費し、0.3秒後に攻撃力・魔力の高い方の30%の斬撃ダメージを与える（再使用まで3秒。別の敵に命中させると古い印は消滅）。また、移動完了時に派生を1段階進める（再使用まで2秒、コンボはリセットされ、移動自体ではダメージは発生しない）。", "A reserved2-piece form marks one native target, max3 stacks/6s; at3 stacks consume for a30%H slash after0.3s, CD3s. Changing targets clears old marks. Native movement advances one form, CD2s, resets chain, no empty-movement damage.",new[]{InkChannel("MoonMark",30)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonMark",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:300,lifetimeMillis:6000,mainHits:3,maxTargets:1,maxInstances:1,element:BossElement.Dark),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:2000,count:3)),
            InkProfile(false,"stage6","6秒以内に「刃→槍→槌」の連携を完了させると、槌地点の左右2mに現れる幻影から0.35秒後に長さ6m・幅0.8mの斬撃線が放たれ、それぞれ攻撃力・魔力の高い方の45%のダメージを与える（合計90%、上限は100%）。さらに0.8秒後、半径3mに攻撃力・魔力の高い方の38%の槌ダメージを与える（上限は40%、再使用まで6秒。移動で順序を飛ばすと連携はリセットされる。召喚物や実体は伴わない）。", "Finish blade→spear→hammer within6s: fixed phantoms2m left/right of hammer point send6m/0.8m lines after0.35s,45%H each(total90/cap100), then a3m38%H hammer after0.8s(cap40). CD6s, one sequence. Movement skips reset chain; no Entity/Summon.",new[]{InkChannel("MoonPhantom",90,100),InkChannel("MoonFinish",38,40)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonPhantom",BossShape.Line,cooldownMillis:6000,delayMillis:350,counterLifetimeMillis:6000,count:2,maxInstances:1,rangeMilli:6000,widthMilli:800,magnitudeMilli:2000,element:BossElement.Dark),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonFinish",delayMillis:800,maxInstances:1,radiusMilli:3000,element:BossElement.Dark)),
        };
        internal static BossRewardProfile[] CreateInkRewards()
        {
            BossRewardStage White(int stage,int conversion,int slots,int target,int budget) => new BossRewardStage(stage,
                new Txt($"本体の光線による実際の超過回復量の{conversion/10}%を、2秒間の障壁に変換する。先着のヒーロー（自身を含む）{slots}人が対象。対象ごとの上限は攻撃力・魔力の高い方の{target/10}%、光線の詠唱1回ごとの合計上限は攻撃力・魔力の高い方の{budget/10}%（障壁の更新時も上限枠を消費）。本体の回復量は減少しない。すべての白夜セットの障壁は最大値で上書きされ、上限は攻撃力・魔力の高い方の38%。",$"Native Beam actual overheal → {conversion/10}% shield/2s. First{slots} heroes only; target cap{target/10}%H, parent budget{budget/10}%H, updates spend budget. H frozen at spawn, native heal unchanged; all white shields highest-replacement/cap38%H."),
                new[]{new BossRewardAction(BossRewardActionKind.NativeShield,conversion,conversion,durationMillis:2000,count:slots,targetCapMilli:target,budgetMilli:budget)});
            BossRewardStage Dark(int stage,int dwell,int flat,int budget)
            {
                var actions = new List<BossRewardAction>{new BossRewardAction(BossRewardActionKind.NativeDamage,flat,flat,1000,count:3,dwellMillis:dwell,gapToleranceMillis:100,budgetMilli:budget,magnitudeMilli:3)};
                if(stage>=2) actions.Add(new BossRewardAction(BossRewardActionKind.NativeTarget,250,250,1000,count:3,rangeMilli:8000,order:1,speedMilli:12000,widthMilli:500));
                if(stage>=3) actions.Add(new BossRewardAction(BossRewardActionKind.NativeMode,cooldownMillis:1000,count:6,order:2));
                return new BossRewardStage(stage,new Txt($"同じ敵に本体の光線が連続で{dwell/1000.0:0.##}秒間命中した後、その敵への次の光線ダメージに攻撃力・魔力の高い方の{flat/10}%の追加ダメージを与える（再使用まで1秒、光線1回につき最大3回まで・合計上限{budget/10}%、最大3体の敵まで）。"+(stage>=2?"また、光線命中時に継続時間とは独立して投槍（弾速12m/秒、長さ8m・幅0.5m、攻撃力・魔力の高い方の25%ダメージ）を放つ（再使用まで1秒、光線1回につき最大3本）。":"")+(stage>=3?"さらに光線命中時に独立して通常攻撃扱いの2セット派生を放つ（再使用まで1秒、光線1回につき最大6回。2セット効果と再使用時間を共有し、派生・月印・コンボのみを進行させ、部位装備の攻撃回数はカウントされない）。":""),$"After{dwell/1000.0:0.##}s of native successes on one enemy, add fixed{flat/10}%H to next native packet. CD1s, parent max3/total{budget/10}%H,3 enemies; gaps≤native hit+check+0.1s. "+(stage>=2?"Independent25%H spears:12m/s,8m/0.5m,CD1s,parent max3. ":"")+(stage>=3?"Independent illumination main hits:CD1s,parent max6, shared2-piece CD; advance forms/marks/chain only, never part counters.":"")),actions);
            }
            return new[]{new BossRewardProfile(WhiteNightRewardId,WhiteNightSetId,"St_U_BeamOfBalance",BossRewardAdapter.BeamOfBalance,new[]{White(1,250,1,120,240),White(2,250,2,200,400),White(3,500,2,300,600)}),new BossRewardProfile(DarkMoonRewardId,DarkMoonSetId,"St_U_BeamOfBalance",BossRewardAdapter.BeamOfBalance,new[]{Dark(1,1000,100,300),Dark(2,750,150,450),Dark(3,500,200,600)})};
        }
        public static UniqueDef[] CreateInkPieces()
        {
            var result = new List<UniqueDef>();
            void Add(string set,string slot,string baseId,string ja,string en) => result.Add(new UniqueDef(set+"."+slot,baseId,new Txt(ja,en),set,set.Substring(4)+"."+slot));
            Add(WhiteNightSetId,"weapon","weapon.pilgrim_staff","白蓮の錫杖","White Lotus Staff");
            Add(WhiteNightSetId,"armor","armor.prayer_shawl","安息域の法衣（案：安息の法衣）","Haven Robe");
            Add(WhiteNightSetId,"charm","charm.lotus_seal","光秤の印","Lightscale Seal");
            Add(WhiteNightSetId,"head","head.radiant_halo","白暁の冠","White Dawn Crown");
            Add(WhiteNightSetId,"hands","hands.radiant_wraps","蓮掌の手巻き（案：蓮掌の手甲 / 蓮掌の包帯）","Lotus Palm Wraps");
            Add(WhiteNightSetId,"feet","feet.dawn_steps","光界の歩み（案：光界の具足 / 光界の靴）","Lightward Steps");
            Add(DarkMoonSetId,"weapon","weapon.tide_trident","黒月の三叉刃","Black Moon Trident");
            Add(DarkMoonSetId,"armor","armor.shadow_cloak","怒影の鎧（案：憤影の鎧）","Wrathshadow Mail");
            Add(DarkMoonSetId,"charm","charm.shadow_ring","月欠けの環（案：虧月の環）","Waning Moon Ring");
            Add(DarkMoonSetId,"head","head.raven_mask","自我の面","Mask of the Self");
            Add(DarkMoonSetId,"hands","hands.shadow_gloves","切替の握り（案：変転の籠手 / 幻影の握り）","Shifting Grips");
            Add(DarkMoonSetId,"feet","feet.shadow_slippers","跳影の靴（案：跳影の具足）","Shadowleap Boots");
            return result.ToArray();
        }
        public static SetDef[] CreateInkSets()
        {
            SetDef Set(string id,string ja,string en,string boss,string reward) => new SetDef
            {
                Id=id,Name=new Txt(ja,en),BossTypeName=boss,BossReward=reward,
                TwoPiece=Array.Empty<StatLine>(),ThreePiece=Array.Empty<PowerLine>(),SixPiece=Array.Empty<PowerLine>(),
                BossStages=new[]{new BossSetStage(2,id.Substring(4)+".stage2"),new BossSetStage(3,id.Substring(4)+".stage3"),new BossSetStage(6,id.Substring(4)+".stage6")},
                LinkStages=new[]
                {
                    new SetLinkStage(2,new LinkDef{Kind=LinkKind.BossReward,Value=1,Requires=new[]{"St_U_BeamOfBalance"}}),
                    new SetLinkStage(4,new LinkDef{Kind=LinkKind.BossReward,Value=2,Requires=new[]{"St_U_BeamOfBalance"}}),
                    new SetLinkStage(6,new LinkDef{Kind=LinkKind.BossReward,Value=3,Requires=new[]{"St_U_BeamOfBalance"}}),
                },
            };
            return new[]{Set(WhiteNightSetId,"白蓮の守装","White Lotus Vestments","Mon_Ink_BossWhiteNight",WhiteNightRewardId),Set(DarkMoonSetId,"黒月の刃装","Black Moon Armament","Mon_Ink_BossDarkMoon",DarkMoonRewardId)};
        }
    }
}

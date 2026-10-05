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
            InkProfile(true,"weapon","主撃3回：命中点を0.65秒予告、半径2.5mへ25%Hの掌。CD3秒、予約1個。", "Every3 main hits: warn at the frozen hit point for0.65s, then a2.5m palm for25%H. CD3s, one reservation.",new[]{InkChannel("LotusPalm",25)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusPalm",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:650,mainHits:3,maxInstances:1,radiusMilli:2500,element:BossElement.Light)),
            InkProfile(true,"armor","native被弾後に本人位置へ半径2m/1.5秒の域。域内本人へ15%H shieldを1回/1.5秒。CD8秒、1域。", "After native damage taken: a fixed2m haven for1.5s; shield the owner inside once for15%H/1.5s. CD8s, one domain.",new[]{InkChannel("LotusHaven",15,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.Field,BossPayload.Shield,"LotusHaven",cooldownMillis:8000,lifetimeMillis:1500,maxInstances:1,maxTargets:1,radiusMilli:2000,magnitudeMilli:380)),
            InkProfile(true,"charm","記憶使用確定：本人位置に3→2mへ1.2秒で縮む域。満了時、圏内本人＋最寄り味方hero1人へ各12%H shield/2秒。CD6秒、1域。", "Confirmed memory use: a domain shrinks3→2m over1.2s at the owner. On expiry shield owner and nearest allied hero inside for12%H/2s each. CD6s, one domain; distance ties use netId.",new[]{InkChannel("LotusSafe",12,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MemoryUse,BossMechanism.Field,BossPayload.Shield,"LotusSafe",cooldownMillis:6000,delayMillis:1200,lifetimeMillis:2000,maxInstances:1,maxTargets:2,radiusMilli:3000,widthMilli:2000,magnitudeMilli:380)),
            InkProfile(true,"head","主撃5回：5m/60°のLight扇を-30/0/+30°へ0/0.2/0.4秒、各10%H。CD5秒、列1組。", "Every5 main hits: three5m/60° Light fans at−30/0/+30°, after0/0.2/0.4s,10%H each. CD5s, one sequence.",new[]{InkChannel("LotusWave",30)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusWave",BossShape.Fan,cooldownMillis:5000,intervalMillis:200,count:3,mainHits:5,maxInstances:1,rangeMilli:5000,angleMilli:60000,magnitudeMilli:30000,element:BossElement.Light)),
            InkProfile(true,"hands","主撃4回：命中点2m内の非boss/CC非免疫敵を最大1m吸引、0.35秒後20%H掌。CD4秒、予約1個。地形越え/本人移動なし。", "Every4 main hits: pull non-boss, non-CC-immune enemies within2m up to1m, then a20%H palm after0.35s. CD4s, one reservation; no terrain crossing or owner movement.",new[]{InkChannel("LotusInch",20)},
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Pull,anchor:BossAnchor.Hit,radiusMilli:2000,magnitudeMilli:1000,element:BossElement.Light),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusInch",anchor:BossAnchor.Hit,cooldownMillis:4000,delayMillis:350,mainHits:4,maxInstances:1,radiusMilli:2000,element:BossElement.Light)),
            InkProfile(true,"feet","本人移動完了後2秒内の次主撃：到達点から命中方向へ6m/幅1m/20%H線。CD4秒。", "The next main hit within2s of native movement completion sends a6m/1m-wide20%H line from the frozen arrival point toward the hit. CD4s.",new[]{InkChannel("LotusArrival",20)},
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusArrival",BossShape.Line,cooldownMillis:4000,rangeMilli:6000,widthMilli:1000,element:BossElement.Light)),
            InkProfile(true,"stage2","主撃3回：3m白蓮域、0.75秒後40%H掌→域2秒。域内本人＋最寄り味方hero1人へ15%H shield各1回/2秒。CD4秒、1域。", "Every3 main hits: a3m lotus domain;40%H palm after0.75s, then persist2s. Shield owner and nearest allied hero inside once each for15%H/2s. CD4s, one domain.",new[]{InkChannel("LotusDomainPalm",40),InkChannel("LotusDomainShield",15,kind:BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusDomainPalm",cooldownMillis:4000,delayMillis:750,lifetimeMillis:2000,mainHits:3,maxInstances:1,radiusMilli:3000,element:BossElement.Light),
                new BossAction(BossEvent.Clock,BossMechanism.Defense,BossPayload.Shield,"LotusDomainShield",lifetimeMillis:2000,maxTargets:2,radiusMilli:3000,magnitudeMilli:380)),
            InkProfile(true,"stage3","開始時に掌済みの本人域内にいる主撃：命中方向へ6m/90°/30%H波。CD2秒。予告中/同event新域は対象外。", "A main hit starting inside your already-mature domain sends a6m/90°30%H wave toward the hit. CD2s; excludes warnings and domains created by this event.",new[]{InkChannel("LotusDomainWave",30)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusDomainWave",BossShape.Fan,cooldownMillis:2000,rangeMilli:6000,angleMilli:90000,element:BossElement.Light)),
            InkProfile(true,"stage6","同じ掌済み域内の主撃3回（波CD中も数える）：域消費→5m境界波90%H(cap100)＋元域内2heroまで各38%H shield/2秒(cap40)。CD6秒。部位/新域は後段、全白夜shieldは最高量置換/cap38%H。", "Three main hits in the same mature domain, including wave CD: consume domain, then5m90%H boundary wave(cap100) and38%H shield/2s(cap40) for up to2 heroes inside the original domain. CD6s. Parts/new domains follow; all white shields use highest replacement, cap38%H.",new[]{InkChannel("LotusBoundary",90,100),InkChannel("LotusBoundaryShield",38,40,BossCoefficientKind.Shield)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"LotusBoundary",cooldownMillis:6000,mainHits:3,radiusMilli:5000,element:BossElement.Light),
                new BossAction(BossEvent.MainHit,BossMechanism.Defense,BossPayload.Shield,"LotusBoundaryShield",lifetimeMillis:2000,maxTargets:2,radiusMilli:3000,magnitudeMilli:380)),
            InkProfile(false,"weapon","主撃3回：前方4m/90°/25%H刃。CD3秒。", "Every3 main hits: a forward4m/90°25%H blade. CD3s.",new[]{InkChannel("MoonBlade",25)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonBlade",BossShape.Fan,cooldownMillis:3000,mainHits:3,rangeMilli:4000,angleMilli:90000,element:BossElement.Dark)),
            InkProfile(false,"armor","native被弾後：攻撃者方向8m以内を0.8秒予告、2m/20%H槌。CD8秒、予約1個。無敵なし。", "After native damage taken: warn at the attacker point within8m for0.8s, then a2m20%H hammer. CD8s, one reservation, no invulnerability.",new[]{InkChannel("MoonWrath",20)},
                new BossAction(BossEvent.NativeDamageTaken,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonWrath",anchor:BossAnchor.Hit,cooldownMillis:8000,delayMillis:800,maxInstances:1,radiusMilli:2000,rangeMilli:8000,element:BossElement.Dark)),
            InkProfile(false,"charm","記憶使用確定：照準へ20%H投槍1本。速度12m/秒、8m/幅0.5m、最初の敵/壁で消滅。CD5秒、1弾。", "Confirmed memory use: one20%H spear toward the cursor,12m/s,8m/0.5m wide; stops on first enemy/wall. CD5s, one shot.",new[]{InkChannel("MoonThrow",20)},
                new BossAction(BossEvent.MemoryUse,BossMechanism.Projectile,BossPayload.Damage,"MoonThrow",cooldownMillis:5000,maxInstances:1,rangeMilli:8000,widthMilli:500,speedMilli:12000,firstHitOnly:true,element:BossElement.Dark)),
            InkProfile(false,"head","主撃5回：命中点（最大8m）へ12m/秒剣弾。終点2m/25%Hのみ、壁で消滅。CD5秒。非Summon。", "Every5 main hits: a12m/s sword projectile to the frozen hit point(up to8m); only its2m endpoint deals25%H. Walls cancel it. CD5s, not a Summon.",new[]{InkChannel("MoonEgo",25)},
                new BossAction(BossEvent.MainHit,BossMechanism.Projectile,BossPayload.Damage,"MoonEgo",anchor:BossAnchor.Hit,cooldownMillis:5000,mainHits:5,maxInstances:1,radiusMilli:2000,rangeMilli:8000,widthMilli:500,speedMilli:12000,element:BossElement.Dark)),
            InkProfile(false,"hands","主撃4回：5m/幅0.8m/20%H線、非boss/CC非免疫敵だけ前方最大1m押出し。CD4秒、地形越えなし。", "Every4 main hits: a5m/0.8m-wide20%H line; push only non-boss/non-CC-immune enemies forward up to1m. CD4s, no terrain crossing.",new[]{InkChannel("MoonCarry",20)},
                new BossAction(BossEvent.MainHit,BossMechanism.EnemyMovement,BossPayload.Push,"MoonCarry",BossShape.Line,cooldownMillis:4000,mainHits:4,rangeMilli:5000,widthMilli:800,magnitudeMilli:1000,element:BossElement.Dark)),
            InkProfile(false,"feet","本人移動完了後2秒内の次主撃：到達点の固定幻影予告、0.4秒後命中方向へ5m/幅1m/25%H線。CD4秒、非Entity。", "Next main hit within2s of native movement: a fixed phantom at arrival warns for0.4s, then a5m/1m-wide25%H line toward the hit. CD4s, no Entity.",new[]{InkChannel("MoonArrival",25)},
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.ArrivalWindow,lifetimeMillis:2000),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonArrival",BossShape.Line,cooldownMillis:4000,delayMillis:400,maxInstances:1,rangeMilli:5000,widthMilli:1000,element:BossElement.Dark)),
            InkProfile(false,"stage2","主撃ごと刃4m/90°25%H→槍7m/幅0.8m25%H→槌0.6秒予告/2.5m40%H。CD1秒中は進めず蓄積なし。初期刃、無操作6秒reset。", "Main hits cycle blade4m/90°25%H → spear7m/0.8m25%H → hammer0.6s warning/2.5m40%H. CD1s: neither advance nor bank during CD. Start blade; reset after6s inactivity.",new[]{InkChannel("MoonForm",25),InkChannel("MoonFormHammer",40)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonForm",BossShape.Fan,cooldownMillis:1000,counterLifetimeMillis:6000,count:3,rangeMilli:4000,angleMilli:90000,element:BossElement.Dark),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonForm",BossShape.Line,rangeMilli:7000,widthMilli:800,element:BossElement.Dark),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonFormHammer",anchor:BossAnchor.Hit,delayMillis:600,maxInstances:1,radiusMilli:2500,element:BossElement.Dark)),
            InkProfile(false,"stage3","予約した2点形のnative対象1体へ月印、最大3stack/6秒。3stackで0.3秒後30%H斬/CD3秒、印消費。対象変更で旧印消去。本人移動完了で次形へ1段/CD2秒、chain reset、空移動はdamageなし。", "A reserved2-piece form marks one native target, max3 stacks/6s; at3 stacks consume for a30%H slash after0.3s, CD3s. Changing targets clears old marks. Native movement advances one form, CD2s, resets chain, no empty-movement damage.",new[]{InkChannel("MoonMark",30)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonMark",anchor:BossAnchor.Hit,cooldownMillis:3000,delayMillis:300,lifetimeMillis:6000,mainHits:3,maxTargets:1,maxInstances:1,element:BossElement.Dark),
                new BossAction(BossEvent.MovementCompleted,BossMechanism.Ledger,BossPayload.Mode,cooldownMillis:2000,count:3)),
            InkProfile(false,"stage6","6秒内の刃→槍→槌完成：槌地点左右2mの固定幻影から0.35秒後6m/幅0.8m各45%H斬線（合計90/cap100）、0.8秒後3m38%H槌(cap40)。CD6秒、予約1組。移動で順を飛ばすとchain reset。Entity/Summonなし。", "Finish blade→spear→hammer within6s: fixed phantoms2m left/right of hammer point send6m/0.8m lines after0.35s,45%H each(total90/cap100), then a3m38%H hammer after0.8s(cap40). CD6s, one sequence. Movement skips reset chain; no Entity/Summon.",new[]{InkChannel("MoonPhantom",90,100),InkChannel("MoonFinish",38,40)},
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonPhantom",BossShape.Line,cooldownMillis:6000,delayMillis:350,counterLifetimeMillis:6000,count:2,maxInstances:1,rangeMilli:6000,widthMilli:800,magnitudeMilli:2000,element:BossElement.Dark),
                new BossAction(BossEvent.MainHit,BossMechanism.ShapeAttack,BossPayload.Damage,"MoonFinish",delayMillis:800,maxInstances:1,radiusMilli:3000,element:BossElement.Dark)),
        };
        internal static BossRewardProfile[] CreateInkRewards()
        {
            BossRewardStage White(int stage,int conversion,int slots,int target,int budget) => new BossRewardStage(stage,
                new Txt($"native Beam実overhealの{conversion/10}%をshield/2秒へ。先着hero{slots}人、target cap{target/10}%H、親Se予算{budget/10}%H、更新も消費。Hは生成時固定、native heal不減、全白夜shield最高置換/cap38%H。",$"Native Beam actual overheal → {conversion/10}% shield/2s. First{slots} heroes only; target cap{target/10}%H, parent budget{budget/10}%H, updates spend budget. H frozen at spawn, native heal unchanged; all white shields highest-replacement/cap38%H."),
                new[]{new BossRewardAction(BossRewardActionKind.NativeShield,conversion,conversion,durationMillis:2000,count:slots,targetCapMilli:target,budgetMilli:budget)});
            BossRewardStage Dark(int stage,int dwell,int flat,int budget)
            {
                var actions = new List<BossRewardAction>{new BossRewardAction(BossRewardActionKind.NativeDamage,flat,flat,1000,count:3,dwellMillis:dwell,gapToleranceMillis:100,budgetMilli:budget,magnitudeMilli:3)};
                if(stage>=2) actions.Add(new BossRewardAction(BossRewardActionKind.NativeTarget,250,250,1000,count:3,rangeMilli:8000,order:1,speedMilli:12000,widthMilli:500));
                if(stage>=3) actions.Add(new BossRewardAction(BossRewardActionKind.NativeMode,cooldownMillis:1000,count:6,order:2));
                return new BossRewardStage(stage,new Txt($"同敵native成功列{dwell/1000.0:0.##}秒後、次敵packetへ固定{flat/10}%H。CD1秒、親最大3回/計{budget/10}%H、敵3体、tick間隔≤本体hit+check+0.1秒。"+(stage>=2?"独立して25%H投槍/12m毎秒/8m/幅0.5m、CD1秒、親最大3本。":"")+(stage>=3?"独立照射主撃CD1秒/親6回、2点CD共有、形/印/chainのみ、部位counter除外。":""),$"After{dwell/1000.0:0.##}s of native successes on one enemy, add fixed{flat/10}%H to next native packet. CD1s, parent max3/total{budget/10}%H,3 enemies; gaps≤native hit+check+0.1s. "+(stage>=2?"Independent25%H spears:12m/s,8m/0.5m,CD1s,parent max3. ":"")+(stage>=3?"Independent illumination main hits:CD1s,parent max6, shared2-piece CD; advance forms/marks/chain only, never part counters.":"")),actions);
            }
            return new[]{new BossRewardProfile(WhiteNightRewardId,WhiteNightSetId,"St_U_BeamOfBalance",BossRewardAdapter.BeamOfBalance,new[]{White(1,250,1,120,240),White(2,250,2,200,400),White(3,500,2,300,600)}),new BossRewardProfile(DarkMoonRewardId,DarkMoonSetId,"St_U_BeamOfBalance",BossRewardAdapter.BeamOfBalance,new[]{Dark(1,1000,100,300),Dark(2,750,150,450),Dark(3,500,200,600)})};
        }
        public static UniqueDef[] CreateInkPieces()
        {
            var result = new List<UniqueDef>();
            void Add(string set,string slot,string baseId,string ja,string en) => result.Add(new UniqueDef(set+"."+slot,baseId,new Txt(ja,en),set,set.Substring(4)+"."+slot));
            Add(WhiteNightSetId,"weapon","weapon.pilgrim_staff","白蓮の錫杖","White Lotus Staff");
            Add(WhiteNightSetId,"armor","armor.prayer_shawl","安息域の法衣","Haven Robe");
            Add(WhiteNightSetId,"charm","charm.lotus_seal","光秤の印","Lightscale Seal");
            Add(WhiteNightSetId,"head","head.radiant_halo","白暁の冠","White Dawn Crown");
            Add(WhiteNightSetId,"hands","hands.radiant_wraps","蓮掌の手巻き","Lotus Palm Wraps");
            Add(WhiteNightSetId,"feet","feet.dawn_steps","光界の歩み","Lightward Steps");
            Add(DarkMoonSetId,"weapon","weapon.tide_trident","黒月の三叉刃","Black Moon Trident");
            Add(DarkMoonSetId,"armor","armor.shadow_cloak","怒影の鎧","Wrathshadow Mail");
            Add(DarkMoonSetId,"charm","charm.shadow_ring","月欠けの環","Waning Moon Ring");
            Add(DarkMoonSetId,"head","head.raven_mask","自我の面","Mask of the Self");
            Add(DarkMoonSetId,"hands","hands.shadow_gloves","切替の握り","Shifting Grips");
            Add(DarkMoonSetId,"feet","feet.shadow_slippers","跳影の靴","Shadowleap Boots");
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

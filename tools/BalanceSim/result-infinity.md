# Infinity stage 2: actual Core economics / 実コード経済計測

## Reproduction / 再現
```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project tools/BalanceSim -c Release -- --mode infinity --players 2000 --seed 95 --item-level 1 --out tools/BalanceSim/result-infinity.md
```
Independent profiles per row: **2000**; deterministic Core SplitMix64 seed: **95**; item level: **1**. Runtime: 1078.8 s. Each row uses a fresh zero-credit profile; the same player seed stream is reused across scenarios. Generated star content is registered with `StarClusters.RegisterAllGenerated()`. Native difficulty fixture: **diffNormal**, boss nightmare flag **false**. Boss source: `Mon_Forest_BossDemon` (the registered native Forest boss-set source). Ordinary uses one eligible source plus three unregistered bosses per expedition; Infinity fixes that source for every cycle.
Observed .NET runtime: **10.0.11**. Major roll-forward lets the net8.0 simulator run on the installed newer runtime without changing its target framework.

The simulator executes `Rules.BeginRun`, `Rules.OnKill` → real `Loot`/`BossSets`/`Waypoints`, `Rules.OnRoomsCleared`, `Rules.ReachInfinityChoice`, `Rules.Delve` and actual Infinity graph/phase transitions. It calls **the same `InfinityRewards.AdvanceCombat` and `EnterRoom` APIs as the runtime**. There is no second limiter, rarity reroll, or rewritten loot distribution here. The clock interval between two kills is aggregated: with no intervening reward mutations, additive refill followed by burst clamp is equivalent to native 0.25-second ticks.

## Method and limits / 方法・限界
- Ordinary reference: 20 Combat rooms + 4 actual bosses per 35 active combat minutes, 10 Lesser + 8 Normal per Combat room, independent 25% MiniBoss chance. Every encounter node takes 35/24 minutes. Ordinary profiles secure at each zone boundary (Heat 0); victory at four zones starts the next ordinary expedition through real Rules. Only the first of its four bosses uses the registered Forest source; the remaining three have no registered boss set. This is an explicit encounter fixture, not a measurement of Forest boss incidence. Boss-set drop depth equals chosen DreamDepth; native bosses are not nightmare-promoted.
- Infinity compares all **10/15/20 periods × 30/60/120 minute** combinations at 1x and 4x encounter throughput. Boss encounters consume time too. Each cycle uses actual boss/soul completion and Delve, so Heat grows to 5 and pressure grows from cumulative cleared rooms. Graph identity advances at Delve; native zone and item level stay fixed. Partial final rooms earn time credit and completed kills but do not count as cleared rooms.
- Promotion sensitivity uses DreamDepth 5 and nightmare chance multiplier 1.5 (the existing strong-gear maximum), calling `Nightmares.Roll` on the current Heat, not assigning a promoted reward tier directly. Hoard sensitivity uses the real x3 holding/release path. Fixed waypoint fixtures isolate each modifier rather than modeling player offer-selection probability; the real before-generation guarantee gate still applies. No paid merchant/events, old-asset crafting, old-item recovery, or discretionary bounty actions are modeled; naturally completed kill/room/secure bounties and feats remain enabled.
- All modeled session time is active combat: no travel, pause, loading, choice or idle refill. This is an intentionally generous active-time supply exposure, **not measured native clear speed**. Runtime uses synchronized native elapsed game time only in eligible active Combat/ExitBoss while the local hero is in combat and not KO; inactive/rejoin baselines are discarded. Native base-game Gold/Dust and paid goods funded from old assets are outside a global numeric cap; MOD-added native kill currency is disabled (cap 0). Infinity MOD dust-conversion/merchant opportunities are separately pre-authorized before payment.
- The separate **funded native boss** observation isolates #48 after 135 minutes of combat credit accrued through the real refill API, with ten room-budget entries and no intervening reward admissions. It is a stored-credit sensitivity case, not typical 35-minute encounter throughput. The boss is an actual Boss-tier reward source at Heat 0 with the registered native ID; observed set wins demonstrate the unchanged 10% roll after whole-opportunity authorization. Zero observed sets in the shorter ordinary-throughput Infinity rows do not mean the path is disabled.
- Found outputs are counted from actual Drop events, even if capacity overflow converts them into shards. Boss sets are a separated subset of Legendary, not an extra additive count. Hoard final-output reservations include x3 at generation; released outputs are not charged again. Resource totals below include currently held supplies and secured supplies, not merely what fits in the stash; an unfinished session is not forced into an illegal non-boss return. The separate return/codec observation below exercises legal return.
- Memory is bounded by one live profile/encounter, existing satchel/stash/Hoard capacity, and scalar sums per row. The simulator stores no per-kill history and no all-player sample array.

## Analytic random high-rare budget / 抽選高レアの解析予算
These numbers come from **`InfinityRewards.NormalHighRarePerHour`** and **`NormalLegendaryPerHour`**, not a simulator copy of their formulas. The lower ordinary reference has Heat 0, no nightmares, no waypoint amplification or guarantees, and conservatively **excludes all boss-set output** because native eligible-boss incidence is not measured. Infinity refill always uses the DreamDepth 0 lower reference, even at higher depth: persisted high-depth credit cannot inflate later low-depth runs. Admission reserves the full current-modifier conservative Epic+ AND Legendary expectations **before rolling**, including promotion, luck, duplication/Hoard and every actual registered boss-set chance. The Legendary budget prevents trading Epic expectation into higher Legendary production.

| DreamDepth | Ordinary non-set Epic+ EV / hour | Infinity Epic+ authorization / hour | Ordinary non-set Legendary EV / hour | Infinity Legendary authorization / hour | Guaranteed authorization / hour |
|---:|---:|---:|---:|---:|---:|
| 0 | 0.586805 | 0.586805 | 0.052875 | 0.052875 | 0.250 |
| 5 | 1.214786 | 0.586805 | 0.136383 | 0.052875 | 0.250 |

Zero initial time credit plus pre-roll EV debit bounds cumulative authorized **random** high-rare EV by accumulated active combat hours × the depth-0 reference rate. Stored earned credit can burst later; this is not a strict rolling-hour or arbitrary fresh-session window limit. The scalar burst capacities are bounded and no run/rejoin resets them. This is an expectation authorization bound, **not a promise that each random sample (or finite Monte Carlo mean) stays below the ordinary observed count**. Guarantees are separate: their .25/hour opportunity and final-output budgets are not hidden inside random EV. The first single-output guarantee requires **4 active combat hours**; a whole boss guarantee reserves its two potential outputs before generation. Fresh 30/60/120-minute profiles cannot receive one.

## Observed outputs: normal depth0

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| normal | 30 | 17.0 | 3.0 | 0 / 0 | 23928 | 510 | 248 | 192 | 0.510 ± 0.044 | 0.248 ± 0.031 | 0.192 |
| normal | 60 | 35.0 | 6.0 | 0 / 0 | 48410 | 992 | 502 | 402 | 0.496 ± 0.031 | 0.251 ± 0.022 | 0.201 |
| normal | 120 | 69.0 | 13.0 | 0 / 0 | 99138 | 2064 | 979 | 785 | 0.516 ± 0.022 | 0.245 ± 0.015 | 0.196 |

## Observed outputs: normal depth5

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| normal | 30 | 17.0 | 3.0 | 0 / 0 | 24108 | 974 | 413 | 280 | 0.974 ± 0.061 | 0.413 ± 0.040 | 0.280 |
| normal | 60 | 35.0 | 6.0 | 0 / 0 | 48598 | 1974 | 819 | 567 | 0.987 ± 0.044 | 0.410 ± 0.028 | 0.284 |
| normal | 120 | 69.0 | 13.0 | 0 / 0 | 99405 | 4198 | 1649 | 1125 | 1.050 ± 0.032 | 0.412 ± 0.020 | 0.281 |

## Observed outputs: baseline 1x

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 19.0 | 1.0 | 1 / 1 | 8963 | 132 | 8 | 0 | 0.132 ± 0.023 | 0.008 ± 0.006 | 0.000 |
| 15 | 30 | 19.0 | 1.0 | 1 / 1 | 7729 | 80 | 6 | 0 | 0.080 ± 0.018 | 0.006 ± 0.005 | 0.000 |
| 20 | 30 | 20.0 | 0.0 | 0 / 1 | 6693 | 42 | 3 | 0 | 0.042 ± 0.013 | 0.003 ± 0.003 | 0.000 |
| 10 | 60 | 38.0 | 3.0 | 3 / 3 | 25676 | 490 | 35 | 0 | 0.245 ± 0.022 | 0.018 ± 0.006 | 0.000 |
| 15 | 60 | 39.0 | 2.0 | 2 / 2 | 22235 | 370 | 34 | 0 | 0.185 ± 0.019 | 0.017 ± 0.006 | 0.000 |
| 20 | 60 | 40.0 | 1.0 | 1 / 2 | 19730 | 282 | 23 | 0 | 0.141 ± 0.016 | 0.012 ± 0.005 | 0.000 |
| 10 | 120 | 75.0 | 7.0 | 5 / 7 | 68916 | 1952 | 143 | 0 | 0.488 ± 0.022 | 0.036 ± 0.006 | 0.000 |
| 15 | 120 | 77.0 | 5.0 | 5 / 5 | 61296 | 1513 | 102 | 0 | 0.378 ± 0.019 | 0.026 ± 0.005 | 0.000 |
| 20 | 120 | 79.0 | 3.0 | 3 / 3 | 54984 | 1133 | 104 | 0 | 0.283 ± 0.016 | 0.026 ± 0.005 | 0.000 |

## Observed outputs: fast 4x

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 75.0 | 7.0 | 5 / 7 | 19615 | 434 | 15 | 0 | 0.434 ± 0.041 | 0.015 ± 0.008 | 0.000 |
| 15 | 30 | 77.0 | 5.0 | 5 / 5 | 19382 | 401 | 24 | 0 | 0.401 ± 0.039 | 0.024 ± 0.010 | 0.000 |
| 20 | 30 | 79.0 | 3.0 | 3 / 3 | 19170 | 370 | 26 | 0 | 0.370 ± 0.038 | 0.026 ± 0.010 | 0.000 |
| 10 | 60 | 150.0 | 14.0 | 5 / 15 | 39294 | 1010 | 15 | 0 | 0.505 ± 0.031 | 0.008 ± 0.004 | 0.000 |
| 15 | 60 | 154.0 | 10.0 | 5 / 10 | 40159 | 970 | 26 | 0 | 0.485 ± 0.031 | 0.013 ± 0.005 | 0.000 |
| 20 | 60 | 157.0 | 7.0 | 5 / 7 | 40847 | 935 | 33 | 0 | 0.468 ± 0.030 | 0.017 ± 0.006 | 0.000 |
| 10 | 120 | 300.0 | 29.0 | 5 / 30 | 80947 | 2157 | 15 | 0 | 0.539 ± 0.023 | 0.004 ± 0.002 | 0.000 |
| 15 | 120 | 309.0 | 20.0 | 5 / 20 | 81702 | 2153 | 26 | 0 | 0.538 ± 0.023 | 0.007 ± 0.002 | 0.000 |
| 20 | 120 | 314.0 | 15.0 | 5 / 15 | 82173 | 2118 | 33 | 0 | 0.530 ± 0.023 | 0.008 ± 0.003 | 0.000 |

## Observed outputs: promotion 4x / depth5 / gear1.5

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 75.0 | 7.0 | 5 / 7 | 12615 | 519 | 3 | 0 | 0.519 ± 0.045 | 0.003 ± 0.003 | 0.000 |
| 15 | 30 | 77.0 | 5.0 | 5 / 5 | 14243 | 510 | 5 | 0 | 0.510 ± 0.044 | 0.005 ± 0.004 | 0.000 |
| 20 | 30 | 79.0 | 3.0 | 3 / 3 | 15120 | 513 | 16 | 0 | 0.513 ± 0.044 | 0.016 ± 0.008 | 0.000 |
| 10 | 60 | 150.0 | 14.0 | 5 / 15 | 23714 | 1090 | 3 | 0 | 0.545 ± 0.032 | 0.002 ± 0.002 | 0.000 |
| 15 | 60 | 154.0 | 10.0 | 5 / 10 | 25436 | 1095 | 5 | 0 | 0.548 ± 0.032 | 0.003 ± 0.002 | 0.000 |
| 20 | 60 | 157.0 | 7.0 | 5 / 7 | 26784 | 1083 | 16 | 0 | 0.542 ± 0.032 | 0.008 ± 0.004 | 0.000 |
| 10 | 120 | 300.0 | 29.0 | 5 / 30 | 45909 | 2249 | 3 | 0 | 0.562 ± 0.023 | 0.001 ± 0.001 | 0.000 |
| 15 | 120 | 309.0 | 20.0 | 5 / 20 | 47592 | 2298 | 5 | 0 | 0.575 ± 0.023 | 0.001 ± 0.001 | 0.000 |
| 20 | 120 | 314.0 | 15.0 | 5 / 15 | 49313 | 2334 | 16 | 0 | 0.584 ± 0.024 | 0.004 ± 0.002 | 0.000 |

## Observed outputs: Hoard 4x / depth5 / gear1.5

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 75.0 | 7.0 | 5 / 7 | 7623 | 309 | 0 | 0 | 0.309 ± 0.034 | 0.000 ± 0.000 | 0.000 |
| 15 | 30 | 77.0 | 5.0 | 5 / 5 | 8931 | 306 | 0 | 0 | 0.306 ± 0.034 | 0.000 ± 0.000 | 0.000 |
| 20 | 30 | 79.0 | 3.0 | 3 / 3 | 7122 | 213 | 0 | 0 | 0.213 ± 0.029 | 0.000 ± 0.000 | 0.000 |
| 10 | 60 | 150.0 | 14.0 | 5 / 15 | 17967 | 807 | 0 | 0 | 0.404 ± 0.028 | 0.000 ± 0.000 | 0.000 |
| 15 | 60 | 154.0 | 10.0 | 5 / 10 | 20034 | 867 | 0 | 0 | 0.434 ± 0.029 | 0.000 ± 0.000 | 0.000 |
| 20 | 60 | 157.0 | 7.0 | 5 / 7 | 19344 | 759 | 0 | 0 | 0.380 ± 0.027 | 0.000 ± 0.000 | 0.000 |
| 10 | 120 | 300.0 | 29.0 | 5 / 30 | 40440 | 1857 | 0 | 0 | 0.464 ± 0.021 | 0.000 ± 0.000 | 0.000 |
| 15 | 120 | 309.0 | 20.0 | 5 / 20 | 42330 | 2028 | 0 | 0 | 0.507 ± 0.022 | 0.000 ± 0.000 | 0.000 |
| 20 | 120 | 314.0 | 15.0 | 5 / 15 | 42513 | 1932 | 0 | 0 | 0.483 ± 0.022 | 0.000 ± 0.000 | 0.000 |

## Observed outputs: EpicMirage authorization / 4x

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 300 | 748.0 | 74.0 | 5 / 74 | 137 | 136 | 1 | 0 | 0.014 ± 0.002 | 0.000 ± 0.000 | 0.000 |

## Observed outputs: funded native boss / stored combat credit

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 135 | 10.0 | 1.0 | 0 / 1 | 3426 | 135 | 216 | 200 | 0.030 ± 0.005 | 0.048 ± 0.006 | 0.044 |

The ± columns are the approximate 95% Poisson Monte Carlo sampling error `1.96*sqrt(count)/exposure-hours`, not gameplay prediction intervals and not valid rare-event confidence limits near zero. Zero observed events do **not** establish zero probability. Outcomes share profile state (bounties/overflow/waypoints), so this approximation is descriptive, not a proof or an independent identically distributed event assumption. Each interval's real exposure is players × session minutes/60; integer total counts are included for auditability.

## Actual authorizations / 実際の認可・抑止

Accepted/rejected counts are the persisted Core `AcceptedKills` / `RejectedKills`, not inferred from empty rolls. EV and output reservation columns are actual scalar debit around OnKill after refill. Hoard final-output credits can be reserved before release. Guarantee output credits reserve the whole possible output before rolling; an empty roll or smaller output does not refund them. These credits are not actual guaranteed-drop counts; actual drops appear above.

| Scenario | Period | Minutes | Attempted kills | Accepted | Rejected | Nightmare kills | Random Epic+ EV reserved / hour | Epic+ reference / hour | Random Legendary EV reserved / hour | Legendary reference / hour | Final free outputs reserved / hour | Guarantee opportunities reserved | Guaranteed output credits reserved |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| baseline 1x | 10 | 30 | 715534 | 355780 | 359754 | 5957 | 0.138113 | 0.586805 | 0.008993 | 0.052875 | 8.963 | 0 | 0 |
| baseline 1x | 15 | 30 | 715563 | 355360 | 360203 | 2785 | 0.098111 | 0.586805 | 0.005592 | 0.052875 | 7.729 | 0 | 0 |
| baseline 1x | 20 | 30 | 730078 | 362885 | 367193 | 0 | 0.054681 | 0.586805 | 0.00152 | 0.052875 | 6.693 | 0 | 0 |
| baseline 1x | 10 | 60 | 1396935 | 697695 | 699240 | 32110 | 0.281089 | 0.586805 | 0.02077 | 0.052875 | 12.838 | 0 | 0 |
| baseline 1x | 15 | 60 | 1431423 | 715073 | 716350 | 20150 | 0.197795 | 0.586805 | 0.013893 | 0.052875 | 11.118 | 0 | 0 |
| baseline 1x | 20 | 60 | 1461869 | 728547 | 733322 | 12447 | 0.1474 | 0.586805 | 0.009644 | 0.052875 | 9.865 | 0 | 0 |
| baseline 1x | 10 | 120 | 2761427 | 1381826 | 1379601 | 130549 | 0.535201 | 0.586805 | 0.039519 | 0.052875 | 17.229 | 0 | 0 |
| baseline 1x | 15 | 120 | 2830251 | 1417518 | 1412733 | 93903 | 0.398429 | 0.586805 | 0.030042 | 0.052875 | 15.324 | 0 | 0 |
| baseline 1x | 20 | 120 | 2899139 | 1452953 | 1446186 | 69395 | 0.301226 | 0.586805 | 0.022227 | 0.052875 | 13.746 | 0 | 0 |
| fast 4x | 10 | 30 | 2761427 | 612000 | 2149427 | 130549 | 0.575198 | 0.586805 | 0.018774 | 0.052875 | 19.615 | 0 | 0 |
| fast 4x | 15 | 30 | 2830251 | 620419 | 2209832 | 93903 | 0.519201 | 0.586805 | 0.02829 | 0.052875 | 19.382 | 0 | 0 |
| fast 4x | 20 | 30 | 2899139 | 622349 | 2276790 | 69395 | 0.483001 | 0.586805 | 0.033134 | 0.052875 | 19.170 | 0 | 0 |
| fast 4x | 10 | 60 | 5503140 | 1143674 | 4359466 | 344608 | 0.583826 | 0.586805 | 0.009387 | 0.052875 | 19.647 | 0 | 0 |
| fast 4x | 15 | 60 | 5660783 | 1180212 | 4480571 | 314571 | 0.584948 | 0.586805 | 0.014632 | 0.052875 | 20.080 | 0 | 0 |
| fast 4x | 20 | 60 | 5763946 | 1197700 | 4566246 | 280671 | 0.585143 | 0.586805 | 0.021316 | 0.052875 | 20.424 | 0 | 0 |
| fast 4x | 10 | 120 | 11008429 | 2211021 | 8797408 | 772389 | 0.585955 | 0.586805 | 0.004693 | 0.052875 | 20.237 | 0 | 0 |
| fast 4x | 15 | 120 | 11322556 | 2245663 | 9076893 | 756019 | 0.586312 | 0.586805 | 0.007316 | 0.052875 | 20.426 | 0 | 0 |
| fast 4x | 20 | 120 | 11494809 | 2262807 | 9232002 | 727864 | 0.58626 | 0.586805 | 0.010658 | 0.052875 | 20.543 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 10 | 30 | 2761401 | 512819 | 2248582 | 195886 | 0.585228 | 0.586805 | 0.004813 | 0.052875 | 12.615 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 15 | 30 | 2830577 | 573675 | 2256902 | 141334 | 0.585444 | 0.586805 | 0.007327 | 0.052875 | 14.243 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 20 | 30 | 2899448 | 597719 | 2301729 | 104110 | 0.584496 | 0.586805 | 0.016934 | 0.052875 | 15.120 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 10 | 60 | 5503081 | 900779 | 4602302 | 517023 | 0.583031 | 0.586805 | 0.002407 | 0.052875 | 11.857 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 15 | 60 | 5661124 | 963982 | 4697142 | 472516 | 0.583605 | 0.586805 | 0.003663 | 0.052875 | 12.718 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 20 | 60 | 5764507 | 1001719 | 4762788 | 421350 | 0.585465 | 0.586805 | 0.008467 | 0.052875 | 13.392 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 10 | 120 | 11008069 | 1686847 | 9321222 | 1158474 | 0.585386 | 0.586805 | 0.001203 | 0.052875 | 11.477 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 15 | 120 | 11322634 | 1747980 | 9574654 | 1134281 | 0.585946 | 0.586805 | 0.001832 | 0.052875 | 11.898 | 0 | 0 |
| promotion 4x / depth5 / gear1.5 | 20 | 120 | 11495012 | 1786894 | 9708118 | 1091716 | 0.586368 | 0.586805 | 0.004234 | 0.052875 | 12.328 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 10 | 30 | 2761401 | 300000 | 2461401 | 195886 | 0.582253 | 0.586805 | 0 | 0.052875 | 8.442 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 15 | 30 | 2830577 | 360585 | 2469992 | 141334 | 0.583037 | 0.586805 | 0 | 0.052875 | 9.408 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 20 | 30 | 2899448 | 412000 | 2487448 | 104110 | 0.584724 | 0.586805 | 0 | 0.052875 | 10.368 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 10 | 60 | 5503081 | 468000 | 5035081 | 517023 | 0.581832 | 0.586805 | 0 | 0.052875 | 9.758 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 15 | 60 | 5661124 | 530556 | 5130568 | 472516 | 0.585634 | 0.586805 | 0 | 0.052875 | 10.491 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 20 | 60 | 5764507 | 595369 | 5169138 | 421350 | 0.585592 | 0.586805 | 0 | 0.052875 | 10.970 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 10 | 120 | 11008069 | 808000 | 10200069 | 1158474 | 0.585082 | 0.586805 | 0 | 0.052875 | 10.481 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 15 | 120 | 11322634 | 868588 | 10454046 | 1134281 | 0.585281 | 0.586805 | 0 | 0.052875 | 10.904 | 0 | 0 |
| Hoard 4x / depth5 / gear1.5 | 20 | 120 | 11495012 | 933997 | 10561015 | 1091716 | 0.585775 | 0.586805 | 0 | 0.052875 | 11.144 | 0 | 0 |
| EpicMirage authorization / 4x | 10 | 300 | 27481338 | 2000 | 27479338 | 2057188 | 0 | 0.586805 | 0 | 0.052875 | 0.014 | 2000 | 2000 |
| funded native boss / stored combat credit | 10 | 135 | 2000 | 2000 | 0 | 0 | 0.074913 | 0.586805 | 0.047579 | 0.052875 | 0.761 | 0 | 0 |

## Resources and implemented caps / 資源・実装上限

Rates from Core: free relics **24/hour**; shards **180/hour**; tuning **6/hour**; Dream XP **1200/hour**; Star XP and aggregate equipped-relic awakening **780/hour**. Refill stays at the global lower baseline; current depth/waypoint multipliers increase the requested reward, not the budget. Merchant and dust-conversion authorization rates are **6** and **6** per hour. Native MOD bonus Gold/Dust: **0** (base-game currency remains unchanged). Failed pre-payment attempts conservatively retain reservations; paid countervalue is never confiscated.

| Scenario | Period | Minutes | Shards/hour | Tuning/hour | Dream XP/hour | Star XP/hour | Equipped relic awakening/hour |
|---|---:|---:|---:|---:|---:|---:|---:|
| normal depth0 | normal | 30 | 305.999 | 7.827 | 1474.680 | 914.855 | 794.855 |
| normal depth0 | normal | 60 | 306.639 | 7.932 | 1580.124 | 994.135 | 794.135 |
| normal depth0 | normal | 120 | 316.557 | 8.363 | 1651.956 | 1046.863 | 796.863 |
| normal depth5 | normal | 30 | 306.175 | 7.813 | 1476.200 | 1829.710 | 1628.281 |
| normal depth5 | normal | 60 | 307.953 | 7.926 | 1583.572 | 1988.270 | 1627.097 |
| normal depth5 | normal | 120 | 317.703 | 8.374 | 1654.491 | 2093.725 | 1634.898 |
| baseline 1x | 10 | 30 | 89.038 | 1.452 | 601.720 | 370.702 | 370.702 |
| baseline 1x | 15 | 30 | 80.427 | 1.117 | 582.585 | 368.344 | 368.344 |
| baseline 1x | 20 | 30 | 68.154 | 0.576 | 571.858 | 374.425 | 374.425 |
| baseline 1x | 10 | 60 | 104.331 | 2.228 | 622.980 | 367.534 | 367.534 |
| baseline 1x | 15 | 60 | 97.077 | 1.901 | 618.658 | 375.039 | 375.039 |
| baseline 1x | 20 | 60 | 90.442 | 1.595 | 614.330 | 381.187 | 381.187 |
| baseline 1x | 10 | 120 | 116.871 | 2.555 | 628.717 | 365.799 | 365.799 |
| baseline 1x | 15 | 120 | 111.608 | 2.471 | 636.848 | 374.894 | 374.894 |
| baseline 1x | 20 | 120 | 106.533 | 2.272 | 640.635 | 383.306 | 383.306 |
| fast 4x | 10 | 30 | 117.704 | 1.067 | 976.993 | 620.661 | 620.661 |
| fast 4x | 15 | 30 | 136.080 | 2.183 | 1039.894 | 642.485 | 642.485 |
| fast 4x | 20 | 30 | 149.452 | 2.744 | 1071.541 | 652.336 | 652.336 |
| fast 4x | 10 | 60 | 85.433 | 0.534 | 852.464 | 576.168 | 576.168 |
| fast 4x | 15 | 60 | 97.880 | 1.128 | 911.455 | 601.241 | 601.241 |
| fast 4x | 20 | 60 | 113.220 | 1.623 | 953.885 | 615.291 | 615.291 |
| fast 4x | 10 | 120 | 84.544 | 0.267 | 788.273 | 554.921 | 554.921 |
| fast 4x | 15 | 120 | 91.424 | 0.564 | 817.520 | 566.983 | 566.983 |
| fast 4x | 20 | 120 | 99.275 | 0.811 | 838.521 | 573.922 | 573.922 |
| promotion 4x / depth5 / gear1.5 | 10 | 30 | 72.702 | 0.409 | 739.493 | 775.919 | 775.919 |
| promotion 4x / depth5 / gear1.5 | 15 | 30 | 90.147 | 0.561 | 869.570 | 777.693 | 777.693 |
| promotion 4x / depth5 / gear1.5 | 20 | 30 | 108.721 | 0.921 | 937.942 | 778.000 | 778.000 |
| promotion 4x / depth5 / gear1.5 | 10 | 60 | 50.175 | 0.205 | 589.315 | 773.998 | 773.998 |
| promotion 4x / depth5 / gear1.5 | 15 | 60 | 58.676 | 0.283 | 655.404 | 777.001 | 777.001 |
| promotion 4x / depth5 / gear1.5 | 20 | 60 | 69.645 | 0.475 | 704.050 | 777.296 | 777.296 |
| promotion 4x / depth5 / gear1.5 | 10 | 120 | 38.580 | 0.102 | 516.233 | 778.029 | 778.029 |
| promotion 4x / depth5 / gear1.5 | 15 | 120 | 42.954 | 0.141 | 548.994 | 778.499 | 778.499 |
| promotion 4x / depth5 / gear1.5 | 20 | 120 | 48.393 | 0.237 | 573.229 | 779.229 | 779.229 |
| Hoard 4x / depth5 / gear1.5 | 10 | 30 | 63.182 | 0.409 | 390.636 | 550.000 | 550.000 |
| Hoard 4x / depth5 / gear1.5 | 15 | 30 | 80.348 | 0.558 | 474.195 | 643.698 | 643.698 |
| Hoard 4x / depth5 / gear1.5 | 20 | 30 | 87.052 | 0.587 | 543.286 | 722.000 | 722.000 |
| Hoard 4x / depth5 / gear1.5 | 10 | 60 | 44.671 | 0.205 | 280.752 | 443.000 | 443.000 |
| Hoard 4x / depth5 / gear1.5 | 15 | 60 | 53.634 | 0.281 | 323.573 | 491.820 | 491.820 |
| Hoard 4x / depth5 / gear1.5 | 20 | 60 | 60.308 | 0.308 | 365.552 | 544.369 | 544.369 |
| Hoard 4x / depth5 / gear1.5 | 10 | 120 | 36.121 | 0.102 | 226.030 | 391.500 | 391.500 |
| Hoard 4x / depth5 / gear1.5 | 15 | 120 | 40.342 | 0.141 | 246.964 | 414.926 | 414.926 |
| Hoard 4x / depth5 / gear1.5 | 20 | 120 | 44.007 | 0.154 | 268.091 | 441.499 | 441.499 |
| EpicMirage authorization / 4x | 10 | 300 | 2.387 | 0.047 | 6.368 | 0.210 | 0.210 |
| funded native boss / stored combat credit | 10 | 135 | 11.236 | 0.463 | 22.222 | 8.889 | 0.000 |

Resource caps apply to free positive supply, including capacity overflow, heat secure bonus, naturally earned free rewards, and conversions. Paid-item recovery/crafting is excluded. The equipped existing Legendary fixture exposes actual awakening, but can reach its finite maximum; observed awakening can therefore be lower than available authorization. Dream XP reconstructs level thresholds plus residual XP; reaching maximum level similarly truncates credited XP. No resource-limit claim is inferred solely from these sample values.

## Legal secured return and persistence observation / 合法帰還・保存の観測

Actual SecuredReturn → Profile.Clone → ProfileCodec.Write/Read: completed=True, active run=False; pressure before secure=1; 11:Zone_Forest:10:0:10:diffNormal: returns=1, best rooms=10, pressure=1; persisted high-rare credit=0.0342; accepted/rejected=10/1.
Codec notes: none.

This requested simulator is a real Core economics/serialization smoke surface, not a Unity scene, multiplayer, or wall-clock benchmark. The 35-minute ordinary reference is an explicit balancing assumption, not native measurement. If measured ordinary same-condition supply is lower, its conservative authorization reference must be lowered; this report does not replace that measurement.

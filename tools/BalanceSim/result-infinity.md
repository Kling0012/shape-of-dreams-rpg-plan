# Infinity stage 2: actual Core economics / 実コード経済計測

## Reproduction / 再現
```sh
DOTNET_ROLL_FORWARD=Major dotnet run --project tools/BalanceSim -c Release -- --mode infinity --infinity-scope comparison --players 2000 --seed 95 --item-level 1 --out tools/BalanceSim/result-infinity.md
```
Independent profiles per row: **2000**; deterministic Core SplitMix64 seed: **95**; item level: **1**. Runtime: 737.2 s. Each row uses a fresh profile; the same player seed stream is reused across scenarios. Generated star content is registered with `StarClusters.RegisterAllGenerated()`. Native difficulty fixture: **diffNormal**, boss nightmare flag **false**. Boss source: `Mon_Forest_BossDemon` (the registered native Forest boss-set source). Ordinary uses one eligible source plus three unregistered bosses per expedition; Infinity fixes that source for every cycle.
Observed .NET runtime: **10.0.11**. Major roll-forward lets the net8.0 simulator run on the installed newer runtime without changing its target framework.

The simulator executes `Rules.BeginRun`, `Rules.OnKill` → real `Loot`/`BossSets`/`Waypoints`, `Rules.OnRoomsCleared`, `Rules.ReachInfinityChoice`, `Rules.Delve` and actual Infinity graph/phase transitions. Infinity rewards are unbudgeted: every roll above flows through the same unrestricted Core path as the runtime. There is no second limiter, rarity reroll, or rewritten loot distribution here.

## Method and limits / 方法・限界
- Ordinary reference: 20 Combat rooms + 4 actual bosses per 35 active combat minutes, 10 Lesser + 8 Normal per Combat room, independent 25% MiniBoss chance. Every encounter node takes 35/24 minutes. Ordinary profiles secure at each zone boundary (Heat 0); victory at four zones starts the next ordinary expedition through real Rules. Only the first of its four bosses uses the registered Forest source; the remaining three have no registered boss set. This is an explicit encounter fixture, not a measurement of Forest boss incidence. Boss-set drop depth equals chosen DreamDepth; native bosses are not nightmare-promoted.
- Lightweight comparison scope: seven fixed 30-minute rows (normal depth 0/5, a normal depth-0 fixture with the same room/boss cadence as Infinity, and four Infinity scenarios at Core default period 10). Long EpicMirage and funded-boss sensitivities are not measured; legal return/codec observation remains included.
- The normal-matched fixture keeps normal Rules (normal secure/victory rewards and Heat 0), but uses the default Infinity boss cadence. At speed 1 its time, completed Combat rooms and bosses match the Infinity baseline exactly. This isolates the reward path from boss-frequency differences; it does not claim normal native maps have that cadence.
- Boss encounters consume time too. Each cycle uses actual boss/soul completion and Delve, so Heat grows to its game cap and pressure grows from cumulative cleared rooms. Graph identity advances at Delve; native zone and item level stay fixed. Partial final rooms earn completed kills but do not count as cleared rooms.
- Promotion sensitivity fixes DreamDepth 5 and a model nightmare chance multiplier of 1.5, calling Nightmares.Roll on the current Heat rather than assigning a promoted reward tier directly. Hoard sensitivity uses the real holding/release path. Fixed waypoint fixtures isolate each modifier instead of modeling offer-selection probability. No paid merchant/events, old-asset crafting, old-item recovery, or discretionary bounty actions are modeled; naturally completed kill/room/secure bounties and feats remain enabled.
- All modeled session time is active combat: no travel, pause, loading, choice or idle time. This is an intentionally generous active-time supply exposure, **not measured native clear speed**. Native base-game Gold/Dust and MOD currency-star bonuses use normal-mode rules and are not simulated here.
- Enemy counts derive from the current profile DreamLevel, zero allocated star points (this fixture never spends earned points), DreamDepth, active waypoint pressure and actual Infinity PressureStage. `InfinityIntervalScaling.EnemyCountMultiplier` adds the independent interval bonus to the capped pressure count multiplier. Each synthetic Combat room is one wave with uniformly initialized fractional spawn credit; every original accrues that bonus and produces same-tier replicas. Originals call `Rules.OnKill` with full rewards; only replicas use `PressureCountRewards.ScaleForBonus(totalMultiplier - 1)` through the actual Rules path. Bosses are not replicated. Bonus nightmares are rolled separately; original encounter RNG is preserved.
- Room duration is explicitly fixed, shared among all original and extra kills. Increasing enemy counts does **not** apply a guessed combat slowdown: this is a fixed-active-room-time economic fixture, not native clear-speed prediction. Native wave composition, concurrent population admission/stalls, combat survival and resulting room duration are not simulated. Actual extra kills are reported, but their native feasibility in that time is not asserted.
- The interval relic multiplier is **ordinary-roll expected value**: its independent bonus rolls can produce only ordinary Epic-or-below relics, not Legendary or limited boss-set sources; fixed guaranteed waypoint outputs remain unamplified. More ordinary rolls or extra kills do not imply a Legendary/hour increase.
- Found outputs are counted from actual Drop events, even if capacity overflow converts them into shards. Boss sets are a separated subset of Legendary, not an extra additive count. Resource totals below include currently held supplies and secured supplies, not merely what fits in the stash; an unfinished session is not forced into an illegal non-boss return. The separate return/codec observation below exercises legal return.
- Memory is bounded by one live profile/encounter, existing satchel/stash/Hoard capacity, and scalar sums per row. The simulator stores no per-kill history and no all-player sample array.

## Fixed exposure interval comparison / 固定時間周期比較

| Period | Minutes | Profile-hours | Ordinary-roll multiplier | Relics/hour | Legendary/hour | Original kills | Bonus kills |
|---:|---:|---:|---:|---:|---:|---:|---:|

## Observed outputs: normal depth0

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| normal | 30 | 17.0 | 3.0 | 0 / 0 | 23928 | 510 | 248 | 192 | 0.510 ± 0.044 | 0.248 ± 0.031 | 0.192 |

## Observed outputs: normal depth5

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| normal | 30 | 17.0 | 3.0 | 0 / 0 | 26873 | 1058 | 420 | 293 | 1.058 ± 0.064 | 0.420 ± 0.040 | 0.293 |

## Observed outputs: normal matched room/boss count

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 19.0 | 1.0 | 0 / 0 | 19266 | 252 | 223 | 198 | 0.252 ± 0.031 | 0.223 ± 0.029 | 0.198 |

## Observed outputs: baseline 1x

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 19.0 | 1.0 | 1 / 5 | 60139 | 945 | 208 | 186 | 0.945 ± 0.060 | 0.208 ± 0.028 | 0.186 |

## Observed outputs: fast 4x

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 75.0 | 7.0 | 5 / 11 | 609704 | 20597 | 2063 | 1462 | 20.597 ± 0.281 | 2.063 ± 0.089 | 1.462 |

## Observed outputs: promotion 4x / depth5 / gear1.5

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 75.0 | 7.0 | 5 / 11 | 744865 | 46062 | 3807 | 2072 | 46.062 ± 0.421 | 3.807 ± 0.121 | 2.072 |

## Observed outputs: Hoard 4x / depth5 / gear1.5

| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 30 | 75.0 | 7.0 | 5 / 11 | 2009327 | 124146 | 6953 | 2126 | 124.146 ± 0.691 | 6.953 ± 0.163 | 2.126 |

The ± columns are the approximate 95% Poisson Monte Carlo sampling error `1.96*sqrt(count)/exposure-hours`, not gameplay prediction intervals and not valid rare-event confidence limits near zero. Zero observed events do **not** establish zero probability. Outcomes share profile state (bounties/overflow/waypoints), so this approximation is descriptive, not a proof or an independent identically distributed event assumption. Each interval's real exposure is players × session minutes/60; integer total counts are included for auditability.

## Resources / 資源

| Scenario | Period | Minutes | Shards/hour | Tuning/hour | Dream XP/hour | Star XP/hour | Equipped relic awakening/hour |
|---|---:|---:|---:|---:|---:|---:|---:|
| normal depth0 | normal | 30 | 305.999 | 7.827 | 1474.680 | 914.855 | 794.855 |
| normal depth5 | normal | 30 | 334.000 | 8.121 | 1681.105 | 2101.988 | 1902.435 |
| normal matched room/boss count | 10 | 30 | 211.510 | 3.959 | 1291.839 | 831.665 | 791.665 |
| baseline 1x | 10 | 30 | 330.444 | 6.714 | 1608.593 | 955.099 | 955.153 |
| fast 4x | 10 | 30 | 5803.226 | 63.719 | 8056.110 | 3972.098 | 3973.113 |
| promotion 4x / depth5 / gear1.5 | 10 | 30 | 8148.157 | 83.975 | 9075.844 | 8161.755 | 8528.445 |
| Hoard 4x / depth5 / gear1.5 | 10 | 30 | 24680.117 | 236.324 | 9075.499 | 8160.283 | 8529.015 |

The equipped existing Legendary fixture exposes actual awakening, but can reach its finite maximum; observed awakening can therefore be lower than the raw award rate. Dream XP reconstructs level thresholds plus residual XP; reaching maximum level similarly truncates credited XP. No supply-rate claim is inferred solely from these sample values.

## Per completed room / boss supply

Same encounter fixture as above. Combat includes kill and room-clear rewards; boss includes its kill rewards, but excludes subsequent secure/victory/Delve rewards. Partial final encounters are excluded. Shards include capacity overflow; relics count found drops before overflow. These are node-attributed means, not hourly totals divided by boss count.

| Scenario | Period | Minutes | Source | Relics | Shards | Tuning | Dream XP | Star XP |
|---|---:|---:|---|---:|---:|---:|---:|---:|
| normal depth0 | normal | 30 | Combat room | 0.411 | 4.118 | 0.050 | 29.576 | 19.260 |
| normal depth0 | normal | 30 | Boss | 1.629 | 26.419 | 1.024 | 52.713 | 20.000 |
| normal depth5 | normal | 30 | Combat room | 0.491 | 4.937 | 0.058 | 35.466 | 46.230 |
| normal depth5 | normal | 30 | Boss | 1.663 | 26.380 | 1.024 | 52.698 | 40.000 |
| normal matched room/boss count | 10 | 30 | Combat room | 0.414 | 4.077 | 0.050 | 29.508 | 19.254 |
| normal matched room/boss count | 10 | 30 | Boss | 1.698 | 26.928 | 1.034 | 53.550 | 20.000 |
| baseline 1x | 10 | 30 | Combat room | 1.383 | 7.119 | 0.120 | 38.623 | 23.391 |
| baseline 1x | 10 | 30 | Boss | 3.287 | 26.650 | 1.033 | 53.483 | 20.000 |
| fast 4x | 10 | 30 | Combat room | 3.746 | 34.312 | 0.329 | 48.734 | 24.526 |
| fast 4x | 10 | 30 | Boss | 3.290 | 45.376 | 1.013 | 51.346 | 20.000 |
| promotion 4x / depth5 / gear1.5 | 10 | 30 | Combat room | 4.640 | 49.583 | 0.463 | 55.507 | 50.500 |
| promotion 4x / depth5 / gear1.5 | 10 | 30 | Boss | 3.317 | 48.575 | 1.014 | 51.354 | 40.000 |
| Hoard 4x / depth5 / gear1.5 | 10 | 30 | Combat room | 0.000 | 17.681 | 0.465 | 55.427 | 50.489 |
| Hoard 4x / depth5 / gear1.5 | 10 | 30 | Boss | 143.523 | 1572.592 | 11.880 | 52.187 | 40.000 |

## Legal secured return and persistence observation / 合法帰還・保存の観測

Actual SecuredReturn → Profile.Clone → ProfileCodec.Write/Read: completed=True, active run=False; pressure before secure=5; 11:Zone_Forest:10:0:10:diffNormal: returns=1, best rooms=10, pressure=5.
Codec notes: none.

This requested simulator is a real Core economics/serialization smoke surface, not a Unity scene, multiplayer, or wall-clock benchmark. The 35-minute ordinary reference is an explicit balancing assumption, not native measurement. This report does not replace that measurement.

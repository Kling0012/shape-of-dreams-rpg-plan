**Multiplayer review: run flow and synchronization**

Reviewed HEAD: `6856d5d246e1592aabd528689abe96bf56569eb2` (protocol 12). Five findings, ranked by impact: two high and three medium.

This is a read-only source review. Only this report was written. No code changes, commits, game-folder writes, game launch, builds, or test execution were performed. Existing test sources were inspected. “Verified” below means confirmed in the checked-out source or local game decompilation; runtime timing and gameplay consequences are explicitly identified as inferred.

Paths beginning `Dew.Core/` refer to `%LOCALAPPDATA%/sod-decomp/Dew.Core/`. Other paths are relative to the repository. The decompilation provides behavior; `.ref/dump` corroborates API signatures. `ScopedBuildCodec.cs` is absent from this checkout; the active multipart codec is `src/SodRpg.Core/Game/BuildTransfer.cs`.

**1. High — A client can submit fabricated combat effects while declaring zero allocated stars**

Primary locations: `src/SodRpg.Core/Game/Build.cs:377-380`, `src/SodRpg.Core/Game/Build.cs:409-424`, `src/SodRpg.Core/Game/Gimmicks.cs:193-218`, `src/SodRpg.Mod/HostAuthority.cs:1130-1140`.

Scenario: a connected modified client sends a correctly framed protocol-12 Build with this payload and uses the matching memory:

```text
a:0;d:1;g:fake1:St_D_IcyVeins:2:2:1000000:0:0:0:0:0:0
```

The decoder independently accepts zero spent points, dream level one, an invented star ID, an OnHit Burst worth 1000%, and zero cooldown. `ValidStarId` checks character syntax and length, not whether the star exists. `Gimmicks.Clamp` checks generic effect/memory validity and numeric caps, not whether that star grants that effect on the caller's hero tree. The sample memory is registered (`src/SodRpg.Core/Game/Links.cs:92`); Burst permits a zero cooldown (`src/SodRpg.Core/Game/GimmicksV129.cs:10-16`).

The client can repeat the entry with distinct invented IDs up to the 300-entry limit (`src/SodRpg.Core/Game/BuildLimits.cs:24`). Every matching entry produces an independent request (`src/SodRpg.Core/Game/Gimmicks.cs:408-445`), which the host queues and executes as area damage (`src/SodRpg.Mod/HostAuthority.PairCombos.cs:91-108`, `:186-192`). Meanwhile pressure uses the independently supplied `SpentStarPoints == 0` (`src/SodRpg.Core/Game/DreamPressure.cs:41-50`). This changes combat for the entire party. Normal allocation-budget validation in `Build.ComputeTree` (`src/SodRpg.Core/Game/Build.cs:54-62`) runs on the sending client and does not protect the receiver.

Game API verification: this is a real client-to-authority path, not caller spoofing. `Dew.Core/Actor.cs:1809-1817` serializes the client message; `Dew.Core/Actor.cs:2584-2591` resolves the caller through `conn.GetPlayer()`. The handler APIs are also present at `.ref/dump/Actor.txt:120` and `:125`.

Fix: transmit canonical allocation IDs/ranks/choices and the necessary gear inputs, then reconstruct or validate their derived effects against the caller's actual hero tree on the host. Enforce the aggregate legal point budget and derive pressure from the validated allocations. At minimum, reject unknown or mismatched gimmick definitions and cross-field cost contradictions. This can enforce structural legality even though saved progression remains client-owned.

Verification boundary: acceptance, queuing, damage execution, and pressure calculation were verified statically. The payload was not injected into a running game. A regression should reject the sample and repeated fake IDs while accepting an equivalent legal allocation.

**2. High — Rejoining after a zone change can block rewards, secure choices, and run completion**

Primary locations: `src/SodRpg.Core/Game/RunChoiceProgress.cs:21-25`, `src/SodRpg.Mod/ClientSession.cs:394`, `src/SodRpg.Mod/ClientSession.cs:502-507`. Related gates: `src/SodRpg.Core/Game/RunChoiceProgress.cs:61-64`, `:80-108`.

Scenario: a remote player disconnects in zone N, the host travels to N+1, and the player rejoins the same expedition without restarting the application. The mod and its `RunChoiceProgress` survive. Changing the server Actor resets the snapshot connection but preserves run history (`src/SodRpg.Mod/ClientSession.cs:307`; `src/SodRpg.Core/Game/RunChoiceProgress.cs:110-114`). `TrackRun` passes the new native zone to `BeginRun`, but its same-run early return leaves the tracked `ZoneIndex` at N.

Native rejoin does not replay the missed travel event. Consequently N+1 snapshots fail `ApplyCurrent`'s zone equality check, secure/delve is unavailable, and N+1 kills remain queued because `FlushRewards` also requires matching zones. This blocks progress for the rejoined zone. If the player also missed N's committed choice—for example, disconnecting before the host selected Secure/Delve—the next travel still cannot recover: `TryAdvance` waits for a committed N snapshot that the host no longer retransmits.

The previous-zone finalization is a one-time broadcast (`src/SodRpg.Mod/ClientSession.RunChoices.cs:77-84`). Periodic choice publication and pressure catch-up carry only the current zone (`:64-74`, `:87-95`; `src/SodRpg.Mod/HostAuthority.cs:338-343`). There is no history request. Pending arrivals or kills prevent conclusion (`src/SodRpg.Core/Game/RunChoiceProgress.cs:108`; `src/SodRpg.Mod/ClientSession.RunChoices.cs:187-200`). The host subsequently publishes empty run state, which the still-active remote session rejects (`:165`). Starting another expedition can then settle the unresolved previous run as a defeat (`src/SodRpg.Core/Game/Rules.cs:106-109`), losing the intended victory settlement.

Game API verification: `Dew.Core/DewMod.cs:475-477` makes the mod container survive scene changes. `Dew.Core/DewNetworkManager.cs:571-590` returns a disconnected client to title. Rejoin waits for the current transition and restores player data (`Dew.Core/DewPlayer.cs:4151-4182`), then places the hero beside the party and replays monster prewarm (`:4249-4278`). Zone-loaded notification is emitted on an actual new-zone load (`Dew.Core/ZoneManager.cs:1584-1586`, `:3528-3530`); `.ref/dump/ZoneManager.txt:4` and `:45` corroborate the event and zone index. CustomRpc has no missing-message replay queue (`Dew.Core/Actor.cs:1872-1903`).

Fix: add explicit reconnect reconciliation between the native current zone and tracked progress. Retain and replay finalized per-zone snapshots for the run, including a terminal snapshot long enough for lagging participants to finish. Settle already-recorded old-zone rewards using their original rules before advancing. Merely overwriting `ZoneIndex` would strand or misattribute those rewards.

Verification boundary: object lifetime, rejoin behavior, and all blocking gates were verified in source. The described disconnect timing and gameplay outcome are inferred, not live reproduced. Regressions should cover rejoining a later zone both with and without the previous commit, followed by victory and defeat.

**3. Medium — A special monster dying before metadata catch-up permanently loses its special rewards for the joining client**

Primary location: `src/SodRpg.Mod/ClientSession.cs:467-489`. Related: `src/SodRpg.Mod/HostAuthority.cs:287-290`, `:364-388`; `src/SodRpg.Core/Game/PendingRunRewards.cs:18-22`.

Scenario: a remote player joins, reconnects, or reloads the mod while an already-created nightmare or variant is alive. The original classification message predates that client's handler. Before the next five-second metadata resync, the party kills the monster while the client is already receiving native death events.

`OnDeath` turns the absent dictionary entries into `NightmareAffix.None` and a null variant ID, then permanently copies those values into `PendingRunKill`. That player gets a normal reward roll rather than the special tier, special star/awakening calculation, applicable variant shard bonus, and special kill/bounty credit (`src/SodRpg.Core/Game/Rules.cs:158-172`, `:204-212`, `:238-244`). Other participants use the correct classification. Delaying rewards for run-choice synchronization does not help: the queued classification is already wrong. Periodic monster replay does not restore the original reward record after the monster is removed.

Game API verification: unmatched/unregistered CustomRpc messages are discarded (`Dew.Core/Actor.cs:1872-1903`; API at `.ref/dump/Actor.txt:115-127`). Native deaths dispatch whenever the victim exists, independently of Dreamforge metadata readiness (`Dew.Core/ClientEventManager.cs:471-475`). The native join replay at `Dew.Core/DewPlayer.cs:4274-4278` replays its own monster prewarm, not Dreamforge classification.

Fix: include authoritative monster classification in a run-scoped kill/reward record with a unique event ID. Alternatively retain dead-monster metadata and wait for classification completion before finalizing queued rewards. An immediate join snapshot reduces the window but needs ordering or acknowledgement to close it. Visual resync can remain periodic.

Verification boundary: five-second replay, dictionary lookup, immutable reward capture, and reward differences are source-verified; the death timing is inferred. A regression should deliver a death before classification and prove the special reward is settled exactly once after catch-up.

**4. Medium — Host reload leaves obsolete nightmare/variant tags on remote clients**

Primary locations: `src/SodRpg.Mod/ClientSession.cs:667-695`, `src/SodRpg.Mod/HostAuthority.cs:507-510`, `:977-992`.

Scenario: the host alone reloads the mod while a special monster remains alive. Teardown removes the host's special monster bonuses and clears its runtimes (`src/SodRpg.Mod/DreamforgeMod.cs:251-257`; `src/SodRpg.Mod/HostAuthority.cs:559-560`, `:1068`, `:1095`). The new authority scans existing entities (`:992`), creates fresh runtime state, and rerolls classifications (`:751-774`). A former nightmare or variant can become ordinary while retaining the same native monster and netId.

Remote clients retain the same native server Actor, so their Actor-change metadata reset does not execute (`src/SodRpg.Mod/ClientSession.cs:275-300`). No removal/reset message accompanies host cleanup. Periodic replay sends positive classifications only (`src/SodRpg.Mod/HostAuthority.cs:364-389`), and a `None` nightmare or unknown variant is ignored (`src/SodRpg.Mod/ClientSession.cs:669-686`). The old tag survives while that monster remains active (`:710-720`). Its eventual death therefore grants special rewards and credit on remote profiles even though the host now treats it as ordinary (`:467-489`; `src/SodRpg.Core/Game/Rules.cs:158-171`). This divergence survives repeated periodic resyncs.

Game API verification: CustomRpc sends transient messages and dispatches only their supplied contents; it provides no automatic state reconciliation (`Dew.Core/Actor.cs:1831-1839`, `:1872-1903`; `.ref/dump/Actor.txt:115-127`).

Fix: preserve authoritative monster classifications across host reload, or publish a host-instance epoch plus a complete classification snapshot with explicit ordinary/removal entries. Clients must reconcile or clear obsolete classifications when that epoch changes. The run-choice authority generation currently does not reset the separate monster dictionaries.

Verification boundary: cleanup, reroll, missing removal semantics, and reward consumption are source-verified. The scenario assumes a host-only mod reload preserving the native game objects; no runtime reload was performed. Regressions should cover special-to-ordinary and variant-to-nightmare transitions on the same live netId.

**5. Medium — The host's personal Secure/Delve decision rejects another player's Dream Omen**

Primary location: `src/SodRpg.Mod/HostAuthority.NewPowers.cs:519`. Related: `src/SodRpg.Mod/ClientSession.NewPowers.cs:13-22`, `src/SodRpg.Mod/HostAuthority.NewPowers.cs:523-526`.

Scenario: at a secure point, a remote player with Dream Omen receives a valid personal dream-event offer. The host chooses Secure/Delve, or host combat automatically resolves that choice, before the remote player's notification reaches the server. The run and generation still match, and the remote player still has the personal offer, but the handler rejects the notification because the host's `run.AwaitingChoice` is false.

The client retries while its own offer remains pending, but every retry encounters the same unrelated host-state gate. The remote hero never receives the cooldown reduction or ten-second shield (`src/SodRpg.Mod/HostAuthority.NewPowers.cs:525-526`). Ordinary round-trip delay or a slower client suffices; no malformed packet is necessary. Personal offers are created separately by `Rules.ReachSecurePoint` (`src/SodRpg.Core/Game/Rules.cs:301-308`), and the host's Secure clears only its own choice (`:368`).

Game API verification: the callback already has the authenticated caller and therefore a per-player runtime (`Dew.Core/Actor.cs:1763-1777`, `:2584-2591`; `.ref/dump/Actor.txt:120`, `:124`). This is an ownership-boundary error, not missing sender identity.

Fix: validate the current run and offered generation independently of the host's personal `AwaitingChoice`. Keep the existing per-player `OmenRun`/`OmenGeneration` deduplication. If an expiry boundary is necessary, track issued offers or generation expiry for each player instead of using the host's personal completion flag.

Verification boundary: sending conditions, rejection, and missing effect path are source-verified; relative arrival timing is inferred. A regression should settle the host's choice before delivering a valid remote notification, then verify one effect application despite retries.

**Other verified boundaries and limits**

- Multipart transport validates total length, part count, indices, transfer IDs, and exact per-part lengths before allocating a bounded receiver (`src/SodRpg.Core/Game/BuildTransfer.cs:58-68`, `:88-108`). Fragment sizing budgets worst-case JSON escaping against the supplied 65,534-byte native string limit (`:18-30`). Both request and acknowledgment use this transport (`src/SodRpg.Mod/NetMessages.cs:20-50`). Native CustomRpc does serialize the complete message to JSON and then write it as a string (`Dew.Core/Actor.cs:1809-1817`, `:1842-1848`). The supplied Dew.Core decompilation contains the call site, but not `Mirror.NetworkWriterExtensions.WriteString` itself, so the numeric Mirror limit was taken from the review brief rather than independently reverified from that implementation. No additional transport overflow or caller-spoofing finding was established.
- Existing tests inspect reordered parts, duplicate/inconsistent metadata, maximum envelopes, reversed zone commits, and authority replacement (`tests/SodRpg.Core.Tests/BuildTransferV131Tests.cs:15`, `:146`, `:187`; `tests/SodRpg.Core.Tests/RunChoiceProgressTests.cs:16`; `tests/SodRpg.Core.Tests/RunChoiceSnapshotStreamTests.cs:11`). These were read, not executed. Receiving delayed history in a test does not prove that production can replay history that was never received.
- Normal host-session replacement issues a new run-choice authority generation (`src/SodRpg.Core/Game/RunChoicePublisher.cs:14-19`). When a remote client successfully applies its new current-zone snapshot, it marks the Build dirty (`src/SodRpg.Mod/ClientSession.RunChoices.cs:171-176`), bypassing the periodic resend deadline. Therefore a general 30-second loss of Build effects after host reload is not established; the blocked catch-up case is covered by finding 2.
- DepthRooms applies its temporary offset only to server-side normal generation and restores it through Postfix/Finalizer (`src/SodRpg.Mod/DepthRooms.cs:24-43`). Native generation is server guarded and consumes that offset (`Dew.Core/ZoneManager.cs:1092-1100`, `:2075-2078`); generated nodes are a synchronized list (`.ref/dump/ZoneManager.txt:28`; `Dew.Core/ZoneManager.cs:3437`). No client-side world-generation mismatch was established.
- Pressure uses the game-player roster, including dead participants, and removes departed-player Build state (`src/SodRpg.Mod/HostAuthority.cs:301-315`). Native final-stat processing marks stats dirty and preserves current/max HP proportion on recalculation (`Dew.Core/EntityStatus.cs:958`, `:1327-1328`). No pressure HP compounding defect was established in this scope.
- ClientSession's inspected event/RPC subscriptions have corresponding removal paths (`src/SodRpg.Mod/ClientSession.cs:222-357`). Mod initialization and destruction pair `PatchAll` and `UnpatchAll` (`src/SodRpg.Mod/DreamforgeMod.cs:50`, `:267`). The reload findings concern lost or stale synchronized state, not an asserted handler leak.
- PressureDividend's native admission method has no production caller (`src/SodRpg.Mod/HostAuthority.PressureDividend.cs:52-69`), and the implementation notes explicitly identify the native kill adapter as unfinished (`docs/specs/v1.31-impl-astra2.md:96`). Its receive-side code was inspected, but it is not counted as a demonstrated active co-op feature failure.
- Host migration was not exercised. No claim is made that snapshot authority replacement alone proves complete native host migration, reliable reconnect recovery, or reward durability across process termination.

Report path: `docs/reviews/mp-sync.md`

1. Five findings: two high severity and three medium severity.
2. High: fabricated Build effects bypass allocation legality and understate pressure.
3. High: reconnecting across a zone change can strand choices, rewards, and conclusion.
4. Medium: death before metadata catch-up permanently downgrades special rewards.
5. Medium: host reload leaves obsolete special-monster tags on remote clients.
6. Medium: the host's completed personal choice suppresses a remote Dream Omen.
7. Each finding includes an exact scenario, source locations, and a concrete fix.
8. Findings cite checked-out source, local game decompilation, and API dumps.
9. Runtime timing is inferred; no game, build, or test execution was performed.
10. Only this report was written; no code changes or commits were made.

**Multiplayer review: host-side combat authority**

Reviewed 2026-10-03 at commit `6856d5d246e1592aabd528689abe96bf56569eb2`. Result: **11 findings, ranked by impact: 2 high and 9 medium**.

This was a read-only source review. The only written artifact is this report. No code was changed, no commit was made, no game was launched, and nothing was written to the game folder. Builds and tests were not run because they would produce additional files. The control flows and native API behavior below were verified in source; the described multiplayer outcomes are deductions from those flows, not live reproductions.

Path notation: `Mod/` means `src/SodRpg.Mod/`; `Core/` means `src/SodRpg.Core/Game/`; `Dew.Core/` means `%LOCALAPPDATA%/sod-decomp/Dew.Core/`. `.ref/dump/` paths are relative to the repository. Each reference includes a line number in the inspected copy.

**1. High — Build acceptance permits fabricated, independently firing combat effects without a legal allocation.**

Location: `Mod/HostAuthority.cs:1130-1139`; validation at `Core/Build.cs:375-424` and `Core/Gimmicks.cs:192-222`.

Scenario: a remote player equips a recognized memory and sends a protocol-12 build claiming dream level 1 and zero spent star points, but containing several invented star IDs with maximum-strength OnUse Burst definitions for that memory. The host accepts the IDs because they only need valid identifier syntax; it never checks that a star exists or that the supplied memory, trigger, effect, value, and modifiers follow from an allowed allocation. Each invented ID gets its own firing state. The player casts once and the host applies every burst to nearby enemies, affecting the entire party's fight while the declared progression contributes no corresponding pressure.

A concrete source-level acceptance example is `d:1;a:0;g:audit0:St_L_Blizzard:1:2:1000000:0:60:0:0:0:0,audit1:St_L_Blizzard:1:2:1000000:0:60:0:0:0:0`. This supplies two separate 1,000% bursts and requires no allocated star. This packet was traced through the decoder, not transmitted or executed.

Verified evidence: `Core/Build.cs:379` clamps the claimed point count independently; `:413-424` accepts the caller's definitions after `Gimmicks.Clamp`; `Core/Gimmicks.cs:193-199` only validates ID characters. `Core/GimmicksV129.cs:13-16` permits the legacy Burst effect on recognized memories. `Core/Gimmicks.cs:408-445` fires each matching entry independently, and `Mod/HostAuthority.PairCombos.cs:95-108` and `:186-194` queue and dispatch each damage award. `Core/DreamPressure.cs:47-50` trusts the separately declared progression. Native RPC caller binding is real (`Dew.Core/Actor.cs:2584-2592`), so another identity need not be impersonated. `.ref/dump/Actor.txt:120` confirms the caller-aware handler API.

Fix: transmit a bounded allocation/loadout representation and recompute its allowed effects on the host. Validate canonical star identity, ranks, memory binding, allowed modifiers, and total point expenditure together. If retaining the current wire format, reject definitions that cannot be derived from registered content and enforce a combined allocation budget; per-entry magnitude and count caps alone do not establish a legal build. Keep the existing packet-size and numeric bounds.

**2. High — The trade handler grants arbitrary native dream dust and replays the same transaction.**

Location: `Mod/HostAuthority.cs:1164-1173`.

Scenario: a remote client sends a current-protocol trade with `spendGold=0`, `spendDust=0`, and `earnDust=2000`. Every condition passes and the host calls `EarnDreamDust(2000)`. Repeating the same token awards it again: the token is only echoed in the response at `:1181`. This creates unlimited native currency in the co-op run, and duplicate delivery of a legitimate trade can also repeat its host-side changes.

Verified evidence: `Core/Economy.cs:17` permits 2,000 dust per request, but the handler has no trade-kind, item-entitlement, or replay check. `Mod/NetMessages.cs:136-139` carries the token and client-selected amounts. `Dew.Core/DewPlayer.cs:2209-2218` actually adds positive dream dust on the server and emits its event; `.ref/dump/DewPlayer.txt:188` confirms the API. Native RPC binding supplies the actual connected caller (`Dew.Core/Actor.cs:2584-2592`), without validating the economic entitlement. The branch behavior is verified; no currency was generated during review.

Fix: accept a specific trade operation and host-verifiable entitlement, calculate its cost/reward on the host, and consume its item/receipt atomically. Cache the result by connection/session plus transaction token and return that result on replay without repeating mutations. Zero-cost salvage is legitimate only after validating and consuming its entitlement; simply forbidding all zero-cost trades would break that operation.

**3. Medium — A stale pooled shield handle can destroy another hero's shield or its own replacement.**

Location: `Mod/HostAuthority.NewPowers.cs:156-171`, particularly `:160-170`.

Scenario: hero A receives a power shield, which expires. The game reuses that native status-effect object for a shield on hero B. A later receives the same power again. A's ledger still points at B's reused object: the comparison either refreshes B's shield while destroying A's new shield, or destroys B's shield before recording A's replacement. If the new `GiveShield` itself reuses the cached object, `old == shield`; the equal-amount branch destroys the freshly granted shield immediately.

Verified evidence: `_powerShields` stores a raw native handle, with no activation identity, and validates only activity and the current `shield` field after creating the replacement. `Dew.Core/Se_GenericShield_OneShot.cs:14` declares `reuseInRoom => true`, and `:16-25` assigns a new underlying shield on creation. `Dew.Core/SpawnManager.cs:258-269` uses the pool even when general pooling is disabled for reusable prefabs; `:281-299` returns the same component instance, and `:511-526` parks it on destruction. `.ref/dump/Se_GenericShield_OneShot.txt:4-5` confirms the field and reuse property. Actual allocation order was not measured, but both reuse outcomes follow from the verified implementation.

Fix: retain the exact `ShieldEffect` reference, recipient, and activation identity with each handle. Validate and prune the previous activation before creating a replacement. Never refresh or destroy a recycled handle belonging to another activation. Reuse the identity check already present in `Mod/HostAuthority.ModShieldPools.cs:122-123`. Verify same-recipient reuse and cross-hero reuse explicitly.

**4. Medium — One hero's generated kill suppresses a teammate's defensive powers during an enemy death explosion.**

Location: `Mod/HostAuthority.NewPowers.cs:414`; originating scope at `:184-217`.

Scenario: hero A's queued power damage kills a DeathBurst variant while `_gimmickDamageDepth` is positive. The monster's synchronous death callback explodes onto nearby hero B. B's real hostile damage reaches B's runtime, but `OnNewPowerTaken` rejects it because A's shared counter remains positive. B loses ShieldbreakBurst eligibility, TollOfGrudge accumulation, and ReadyGuard processing for that hit, depending on how A landed the killing blow.

Verified evidence: `Mod/HostAuthority.cs:450-479` dispatches the explosion during the death callback. `:2198-2202` selects the damaged hero's runtime correctly before calling the rejected handler. `Dew.Core/Entity.cs:832-838` invokes death callbacks before destroying the monster, so the attacking monster is still active for the enemy check. `Dew.Core/Actor.cs:1040-1056` delivers damage events synchronously. The shared counter, rather than an incoming source marker, causes the rejection. This is a traced nested-event path, not a claimed thread race or live reproduction.

Fix: identify generated outgoing effects by their source runtime and damage/chain metadata. Allow another hero's genuine incoming enemy hit to activate defensive powers while preserving recursion suppression for the generated effect itself. Include the A-kills/B-takes-explosion case in regression coverage.

**5. Medium — Shared waypoint combat rules omit heroes without an accepted Build.**

Location: `Mod/HostAuthority.Waypoints.cs:61-78`.

Scenario: a modded host selects Glass Aegis while an unmodded teammate is present. The modded heroes receive half healing and doubled shields; the teammate receives neither receiver multiplier because they have no equipment runtime. Trail of the Pack likewise omits that teammate's maximum-health penalty and summon benefit. A newly joined or respawned modded hero has the same gap until its Build is accepted, while shared enemy pressure is already active.

Verified evidence: waypoint recipients are removed when absent from `_runtimes` and are created only from `_runtimes.Keys`. `Mod/HostAuthority.cs:1284-1289` creates those runtimes from received builds. Summon multiplication is attached through that same runtime at `:1438-1455`; waypoint memory cooldown handling is also below the runtime lookup at `Mod/HostAuthority.NewPowers.cs:364-371`. `docs/specs/v1.30-depth-runtime-implementation.md:23-29` specifies shared recipient, summon, and health rules. Native recipients only execute their installed processors (`Dew.Core/Entity.cs:681-694`; declarations at `.ref/dump/Entity.txt:24-26`). A complete hero roster exists independently at `Dew.Core/ActorManager.cs:195-198` and `.ref/dump/ActorManager.txt:17`. No mixed-install session was launched.

Fix: maintain waypoint recipient state from the authoritative active hero roster, independently of equipment-build state. Attach shared receiver and summon rules on spawn, remove them on despawn, and run shared cast rules even when the caster lacks a Build. Keep equipment-specific powers conditional on a validated Build.

**6. Medium — Host reload leaves obsolete monster classifications on peers, producing different reward inputs for the same kill.**

Location: `Mod/HostAuthority.cs:507-519` and `:1093-1095`.

Scenario: the host reloads while a variant or nightmare is alive. Detach removes its host modifiers and classification, but sends no classification reset. The new authority scans the surviving monster and can roll it as ordinary. The host's replacement client session then considers it ordinary, while remote clients retain the old identity and visual. On death, the remote client records variant/nightmare reward facts for a kill the host treats as ordinary.

Verified evidence: `Mod/HostAuthority.cs:992` rescans existing entities; `:752` and `:773-774` can finish without a special classification. The periodic sync at `:364-390` only sends positive entries. `Mod/ClientSession.cs:670-671` ignores nightmare `None`, `:685-686` ignores empty/unknown variants, and `:706-723` retains classifications for active monsters rather than expiring them by age. `:467-489` copies those identities into pending rewards. `Dew.Core/DewMod.cs:568-578` unloads mod containers rather than native monster actors; ordinary membership removal requires actor removal (`Dew.Core/ActorManager.cs:202-214`, `.ref/dump/ActorManager.txt:6`). The reroll outcome is conditional; the missing repair path is verified.

Fix: synchronize an authority epoch and an explicit reset or full replacement classification snapshot on reload. Alternatively send tombstones for removed classifications. Clients must accept removals and restore visuals before processing subsequent reward facts. Repeating only existing positive classifications cannot repair an old entry that is now absent.

**7. Medium — Dreamforge-created MirageSkin effects and their health bonuses survive host cleanup.**

Location: `Mod/HostAuthority.cs:905`; incomplete cleanup at `:522-563`.

Scenario: the host disables or reloads the mod while a mod-created nightmare skin is active. The party continues fighting a monster with the old native elite shield and special attacks after Dreamforge has removed its other tracked modifiers. On reload, the surviving skin prevents the newly chosen classification from installing its intended skin. If the original shield broke before reload, its health addition remains and another skin can add health again when the monster is selected anew.

Verified evidence: `AttachMirageSkin` discards the `CreateStatusEffect` result, and `Unhook(MonsterRuntime)` has no owned-skin removal. `:888` treats an existing skin as a reason to skip attachment. `Dew.Core/MirageSkinEffect.cs:59-75` grants unstoppable state, directly adds a maximum-health bonus, and creates its shield; `:98-116` installs damage handling and performs server-side special attacks. Its teardown at `:120-136` removes the damage subscription but does not undo the direct health addition at `:69-72`. `.ref/dump/MirageSkinEffect.txt:8-19` confirms the relevant effect API. Native effects remain outside the unloaded mod container (`Dew.Core/DewMod.cs:568-578`). Persistence is source-verified; repeated health growth additionally requires another eligible skin roll.

Fix: retain and destroy only skins created by this authority. Make the health addition reversible too: for example, set `customAmount` to avoid the native untracked health addition and supply an equivalent tracked Dreamforge stat bonus. Destroying the skin alone does not remove that native direct bonus. Preserve naturally occurring game skins.

**8. Medium — Host reload loses the references needed to lift remote players' pact curses.**

Location: `Mod/HostAuthority.cs:1065`.

Scenario: a remote player accepts a pact and receives a native curse. The host reloads the mod, then the player secures their loot and clears the pact in their profile. The curse remains in combat because the replacement host authority no longer knows which status effect to remove. It lasts until a native completion or knockout condition removes it.

Verified evidence: `Mod/HostAuthority.cs:1225-1232` retains curse ownership only in `_pactCurses`; Detach clears the dictionary without destroying those effects. The later `OnCurseClear` immediately returns from the empty lookup at `:1248`. `Dew.Core/CurseStatusEffect.cs:292-303` installs a kill tracker, room/zone/knockout handlers, and quest; cleanup runs when the effect itself is destroyed at `:306-338`. Native completion destroys it at `:400-415`. `.ref/dump/CurseStatusEffect.txt:23-25` confirms its lifecycle methods. The mod unload path at `Dew.Core/DewMod.cs:568-578` does not destroy these native actors. No live reload was performed.

Fix: destroy every still-owned pact curse before clearing the dictionary, isolating each cleanup so one exception does not skip the rest. If reload should preserve active pacts and curses, persist a stable ownership marker and rebuild their mapping before accepting clear requests. Do not clear unrelated native curses.

**9. Medium — Reload abandons live power shields and permits same-power replacements to stack.**

Location: `Mod/HostAuthority.NewPowers.cs:273-282`.

Scenario: hero A grants a SharedWard to hero B. The host reloads while it remains active. The old native shield stays on B, but the new authority starts with an empty strongest-only ledger. A subsequent qualifying award creates another SharedWard instead of comparing it with the first. Both absorb damage until the old shield expires. Disabling the mod similarly leaves the old benefit active for its remaining duration.

Verified evidence: `ClearNewPowerZone` clears `_powerShields` without destroying its effects; `UnhookNewPowers` at `:260-270` does not remove them either. `Mod/HostAuthority.cs:1042` and `Mod/DreamforgeMod.cs:256` use this path during reload. These shields are created under `serverActor` (`Mod/HostAuthority.NewPowers.cs:154-161`), and native effects expire on their timer or inactive victim (`Dew.Core/StatusEffect.cs:354-377`), not when the managed dictionary is cleared. `Dew.Core/StatusEffect.cs:1067-1092` registers the native shield effect; `.ref/dump/Actor.txt:97` confirms the shield-creation API. `docs/specs/v1.29-new-powers.md:41-46` requires strongest-only shared wards. The overlap requires a replacement before the first duration ends and was not timed in-game.

Fix: destroy all still-owned shield activations before clearing this ledger on detach/reset. Use the activation-identity checks from finding 3 so cleanup cannot destroy a pooled object that now belongs to another effect. Define recipient and owner lifecycle cleanup explicitly.

**10. Medium — Summon kills fail to activate their owner's original kill powers.**

Location: `Mod/HostAuthority.cs:2130-2134` and `:2176-2179`.

Scenario: a remote player's summon, or an ability beneath that summon, kills an enemy. `RuntimeOf` selects the nearest entity, which is the summon, then rejects it because it is not a Hero. The owner receives no Momentum, Tailwind, Shatter, or SoulSiphon processing for that kill. Other owner-level kill paths can still receive the same kill through native event propagation, making summon builds internally inconsistent.

Verified evidence: `Dew.Core/Actor.cs:348` defines `firstEntity` through `FindFirstOfType<Entity>()`, and `:2237-2254` searches from the source upward. Native summons resolve their owning hero separately at `Dew.Core/Summon.cs:193`. `Dew.Core/Entity.cs:832-835` and `Dew.Core/Actor.cs:2107-2114` show that kill events propagate to the owning hero. `.ref/dump/Actor.txt:16` and `:48` confirm the kill event and nearest-entity property. `Core/PowerRuntime.cs:382-415` implements the omitted effects; `Core/Content.cs:5193`, `:5200`, `:5205`, and `:5215` describe them as kill powers without excluding summons. This attribution omission is source-verified, not runtime-tested.

Fix: resolve the owning Hero through the actor ancestry for kill attribution while retaining the intended generated-damage guards. Ensure a summon kill reaches the owner exactly once. Review the same helper's elemental call at `Mod/HostAuthority.cs:2229` under the same ownership policy.

**11. Medium — Support effects cast through serverActor never credit their originating player's support bounties.**

Location: `Mod/HostAuthority.NewPowers.cs:141-175`; reporting hooks at `Mod/HostAuthority.cs:1372-1388`.

Scenario: a client hero with TriumphSong uses an ultimate and restores an injured teammate's health. The host applies the heal successfully, but the client's AllyHealer bounty does not advance. PowerShield awards likewise miss ShieldGiver credit. Pair/gimmick ally healing has the same problem because it also uses the isolated server actor.

Verified evidence: TriumphSong dispatch is at `Mod/HostAuthority.NewPowers.cs:373-376`; the support source is `serverActor` at `:144-147`. Pair/gimmick healing uses it at `Mod/HostAuthority.PairCombos.cs:199-211`. The only producers of the two bounty report kinds are the originating hero's actor-event subscriptions at `Mod/HostAuthority.cs:1372-1388`. Native actual healing is recorded at `Dew.Core/Actor.cs:789-801`, but giver events propagate only up their source's parent chain at `:2077-2084` and `:2147-2154`. Native shield events use the shield's parent chain (`Dew.Core/ShieldEffect.cs:64-73`). `.ref/dump/Actor.txt:21` and `:147` confirm the shield event API. `Core/Bounty.cs:122-123` defines generic teammate-healing and shield-giving objectives. The successful mutation and missing attribution path are verified statically; no bounty was exercised live.

Fix: preserve serverActor isolation while explicitly attributing each successful support operation to its source runtime. Report actual positive HP restoration and accepted shield awards once. Exclude overheal-only events, rejected shield candidates, and duplicate reporting of awards already observed through hero events.

**Coverage and checks that did not establish additional findings**

- Reviewed `HostAuthority.cs` and all present `HostAuthority.*.cs` partials, including GimmicksV129, PairCombos, Reactions, NewPowers, SupportPowersV129, NativePowers, UnbowedMind, Bonds, GemSlots, Monsters, Waypoints, ModShieldPools, SacrificeShield, AlliedWard, PressureDividend, and PilingLuckV129. Also reviewed both Core PowerRuntime files, Gimmicks files and GimmickRuntime, PairComboRuntime, Links, Patches, and DreamforgeMod. Adjacent Build, transport, ClientSession, reward, and native files were inspected to establish consequences.
- Build transfer receivers are keyed per player (`Mod/HostAuthority.cs:135-136`, `:1117-1119`); normal application selects `caller.hero` at `:1120`. The inspected path does not copy one connected caller's Build onto another caller's hero.
- Transport bounds are materially present: `Core/BuildTransfer.cs:20-29` budgets chunks against the specified 65,534-byte JSON-string limit, `:58-68` bounds transfer metadata, and `Core/Build.cs:357-472` rejects malformed input and clamps known numeric fields. Native CustomRpc JSON serialization was checked at `Dew.Core/Actor.cs:1809-1817`. Mirror writer implementation was not present in the provided decompilation; its byte ceiling was taken from the review's stated API constraint. No packet-loss/duplication transport simulation was run. These protections do not cure findings 1 and 2.
- Departure removes player build/transfer state (`Mod/HostAuthority.cs:214-221`). Native disconnect destroys owned actors (`Dew.Core/DewNetworkManager.cs:318-339`), and inactive hero runtimes are pruned at `Mod/HostAuthority.cs:1273-1289`. Knockout is distinct from actor destruction, and `Alive` explicitly excludes it at `:1806`. No separate confirmed cross-player runtime reuse or 0.25-second scan/death crash was established. Host migration and reconnect were inspected only as code paths, not exercised.
- Harmony registration has a matching owner-specific teardown: `Mod/DreamforgeMod.cs:50` and `:267`. Static native contexts restore their prior values in finalizers (`Mod/HostAuthority.NativePowers.cs:28`, `:48`, `:102`; `Mod/Patches.cs:30`, `:78`). Synchronous nesting alone is not a finding; finding 4 identifies the concrete counter-dependent failure.
- Monster behavior/tag subscriptions and processors have removal paths; the missing cleanup findings concern specifically owned native effects and replicated classifications. The main mod-shield adapter already checks underlying effect identity at `Mod/HostAuthority.ModShieldPools.cs:122-123`.
- Global pair-death notifications with no source memory do not themselves award or consume a marked pair combo. Core power/gimmick/pair state is principally owned by per-hero runtime instances. No additional confirmed issue was established in Bonds, GemSlots, UnbowedMind, SupportPowersV129, or PilingLuckV129. Dormant integration helpers in SacrificeShield, AlliedWard, and PressureDividend were not treated as executed gameplay paths merely because their implementations exist.

**10-line summary**

1. High: accepted Builds can fabricate independent combat procs outside any legal star allocation.
2. High: trade RPCs can mint native dream dust and repeat transactions with the same token.
3. Medium: pooled shield handles can remove another hero's shield or their own replacement.
4. Medium: shared damage depth suppresses a teammate's defenses during enemy death explosions.
5. Medium: shared waypoint rules skip heroes that have not submitted an accepted Build.
6. Medium: reload leaves stale monster identities on clients and changes their reward inputs.
7. Medium: native MirageSkin effects and pact curses outlive the host's cleanup/tracking.
8. Medium: reload discards live shield tracking and permits strongest-only wards to stack.
9. Medium: summon kills miss owner kill powers; serverActor support misses owner bounty credit.
10. Source/native API review only; no live reproduction, game launch, code changes, or commit.

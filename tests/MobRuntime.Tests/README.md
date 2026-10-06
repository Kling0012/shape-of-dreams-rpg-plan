# MOB runtime adapter tests

This project compiles the production MOB runtime and Core MOB files against small deterministic doubles. It is independent of the game installation and the existing solution. It uses the real Newtonsoft.Json package for the adapter's wire envelopes and manifest parsing.

Run:

```sh
dotnet test tests/MobRuntime.Tests/MobRuntime.Tests.csproj -c Release
```

Model-set identity in this suite is the manifest content version, the declared bundle size in bytes, the exact monster type mapping token, the build target and the game/Unity versions; no file digest is computed anywhere.

Coverage includes pack identity by content version / bundle size / type mapping, challenge-time revision floors, per-room replay isolation, model-only prefab validation, all-participant bootstrap, missing/disabled/mismatched peers, readiness expiry and replay, host heartbeat expiry, delayed spawn, room changes, known despawn/netId reuse, death and already-dead entities, ownership loss, partial load recovery, bundle retention after failed restore, subscription cleanup, opt-out/re-enable, and MOD reload.

These tests establish adapter behavior under the explicitly simulated ordering. They do **not** establish compatibility with the actual Dew/Mirror assemblies, the serializer used by the game transport, Unity's destroyed-object equality behavior, asset serialization, shaders, animation playback or real multiplayer delivery. Game-assembly compilation (Release build of `src/SodRpg.Mod` against the installed game) and the documented in-game verification are separate required checks.

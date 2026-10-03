# MOB runtime adapter tests

This project compiles the production MOB runtime and Core MOB files against small deterministic doubles. It is independent of the game installation and the existing solution. It uses the real Newtonsoft.Json package for the adapter's wire envelopes and manifest parsing.

Run:

```sh
dotnet test tests/MobRuntime.Tests/MobRuntime.Tests.csproj -c Release
```

The current suite contains 47 deterministic cases. Coverage includes MOD DLL identity, challenge-time revision floors, per-room replay isolation, model-only prefab validation, all-participant bootstrap, missing/disabled/mismatched peers, readiness expiry and replay, host heartbeat expiry, delayed spawn, room changes, known despawn/netId reuse, death and already-dead entities, ownership loss, partial load recovery, bundle retention after failed restore, subscription cleanup, opt-out/re-enable, and MOD reload.

These tests establish adapter behavior under the explicitly simulated ordering. They do **not** establish compatibility with the actual Dew/Mirror assemblies, the serializer used by the game transport, Unity's destroyed-object equality behavior, asset serialization, shaders, animation playback or real multiplayer delivery. Game-assembly compilation and the documented in-game verification are separate required checks.

The review environment had no dotnet SDK or C# compiler available. Test execution is therefore not claimed in this document; record the CI/local runner's actual result separately.

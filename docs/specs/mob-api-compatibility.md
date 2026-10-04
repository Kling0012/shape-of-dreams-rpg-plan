# MOB model API compatibility findings

Checked 2026-10-03 against the official Shape of Dreams API documentation and repository base `d64e744`. These are public API declarations and documented contracts, not a decompilation or an in-game verification. No game assemblies, Unity project or tested model template were supplied in that base checkout. No Unity editor was run during this review. A subsequent repository investigation records **Unity 6000.0.77f1** and Windows game label **r.1.4.0.13_s** from the installed files; see [Issue #19 research at beaa1ab](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/beaa1ab5685fa248ba2d47c1fbaaebee3181a42e/docs/specs/issue19-model-import-research.md). That is separate evidence from this public-API review. Exact URP package version and this cloud task's game/Unity execution remain unverified.

## Supported replacement and restoration

[EntityVisual API](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityVisual.html) exposes `LoadModelLocal(EntityModel)`, `LoadModelDefaultLocal()` and the current `model` field. The custom load accepts a prefab's model component, but its input must not have been initialized previously. Therefore a component from an AssetBundle prefab is an allowed input; an already-used live model is not a reusable rollback template.

Prefer the native default-load method for fallback. The documentation does not promise transactional replacement, describe cloning/destruction order, or guarantee recovery after an exception. Never assume that catching a load exception means the old visual survived.

Implementation recommendation: save the resulting current model after a successful load as an ownership marker. Restore only while that exact model is still current; if another system replaces it, relinquish ownership. Do not send a previously initialized model back through the loader. This ownership policy is our design, not an additional game guarantee.

[Entity API](https://lizardsmoothie.com/sod/moddoc/api/Global.Entity.html) also exposes `LoadEntityModelLocal()` and the virtual callback `OnModelLoaded()`. The latter is a callback, not a subscribable event. No model-change event was found in the checked public documentation; the later actual-game inspection reports `ClientEvent_OnModelLoaded`. This adapter avoids depending on its unverified signature.

## Prefab and animation contract

[EntityModel API](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityModel.html) identifies a game-provided MonoBehaviour in Dew.Core. Relevant members:

| Member | Published type or purpose |
| --- | --- |
| `bodyRenderers` | Renderer array |
| `healthBarPosition`, `weapon`, `holsteredWeapon` | Transform references |
| `customMappings` | List of EntityModelCustomMapping |
| `idle`, `stagger`, `death` | AnimationClipWithSpeed values |
| `locomotion` | EntityAnimation.LocomotionType |
| `runForwardClip` and other directional run clips | AnimationClip references |
| `isInitialized` | Read-only boolean |

The public page does not document which optional/null fields each monster supports. In particular, it does not expose `abilityAnimationReplacements`, despite that name appearing in the older research report. The later actual-game investigation confirms the member exists in this game build, but does not supply its exact serialized entry shape or per-Ability mapping keys. Builder bindings therefore remain explicit, reviewed input; the public-page omission is not evidence that the field is absent.

[EntityAnimation](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityAnimation.html) exposes an Animator field and `SetupModel()`. It exposes no public Animancer component requirement. This establishes the visible API, not the absence of an internal animation implementation. Arbitrary controller parameter names or clip-state names cannot be inferred from it.

The related official pages verify:

- [LocomotionType](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityAnimation.LocomotionType.html): Simple, FourDirections, EightDirections; numeric values are not published
- [AnimationClipWithSpeed](https://lizardsmoothie.com/sod/moddoc/api/Global.AnimationClipWithSpeed.html): a struct with an animation clip, a floating-point speed and a static default value
- [EntityModelCustomMapping](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityModelCustomMapping.html): a struct pairing a string `id` with a GameObject `target`

Use a locally supplied, same-build, validated model template. Preserve required mapping targets and skeleton/attachment references when replacing its mesh. An import that preserves mapping IDs while destroying their target hierarchy is not sufficient. A conservative validator may require populated body renderers, a health anchor, an Animator and valid locomotion clips; this is the integration's acceptance policy, not a claim that every native monster requires every field. Confirm required mappings and ability animations against the particular donor monster in-game.

## Paths, versions and bundle lifetime

[ModBehaviour](https://lizardsmoothie.com/sod/moddoc/api/Global.ModBehaviour.html) exposes its ModItem through `mod`; [ModItem](https://lizardsmoothie.com/sod/moddoc/api/Global.ModItem.html) publishes the installation `path`. The existing `DreamforgeMod.Awake` already passes this path to RelicIcons. Resolve assets relative to this installation directory, including Workshop installations, rather than a hard-coded Steam directory or a rewritten assembly location.

Use the literal [Application.unityVersion](https://docs.unity3d.com/ScriptReference/Application-unityVersion.html) and [Application.version](https://docs.unity3d.com/ScriptReference/Application-version.html) values captured from the running game as explicitly named compatibility tokens. Do not populate the application token from the README's displayed game label without comparing them. The README's `r.1.4.0.13_s` is not verified as Application.version. Requiring exact matches is a deliberately conservative integration policy.

[Dew](https://lizardsmoothie.com/sod/moddoc/api/Global.Dew.html) provides a multiplayer-compatibility version used across distribution platforms; this is not documented as an exact asset identity. [DewBuildProfile](https://lizardsmoothie.com/sod/moddoc/api/Global.DewBuildProfile.html) has a version-producing method requiring a codename, but the reviewed API does not establish a current exact build identifier through that route.

[AssetBundle.LoadFromFile](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetBundle.LoadFromFile.html) is synchronous and accepts an optional CRC. Validate paths, expected file hash, target platform and compatibility metadata before loading. A checksum detects corruption; it does not make an arbitrary untrusted asset safe. Unity's [integrity guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/AssetBundles-Integrity.html) explicitly warns that serialized data can exploit application or runtime vulnerabilities even though bundles do not embed executable code.

Keep loaded assets alive while model instances reference them. [Unload(true)](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetBundle.Unload.html) destroys loaded objects; false preserves them but can leave duplicates after reload. Track ownership and restore/remove all owned visuals before destructive unloading. Avoid global unload operations affecting other mods.

The current Mod project uses explicit game-assembly references. New AssetBundle and animation APIs require the installed game's corresponding UnityEngine.AssetBundleModule and UnityEngine.AnimationModule references. A Core-only test pass cannot validate these game bindings.

## Required verification before enabling a shipped model

1. Record the running game application/engine tokens and platform; build in the matching editor with the actual game API bindings
2. Compile the Mod against that installed game, then load a validated prefab on the intended donor monster
3. Verify idle, motion, attacks, damage/stagger, death, material/shader appearance, health bar and attachment positions
4. Verify missing/corrupt/incompatible bundles leave the native appearance usable
5. Verify disable, unload, model replacement by another mod, room change and pooled respawn do not retain custom visuals or destroy another owner's assets
6. Verify clients with and without the optional model package; a local visual replacement is not a network-spawn or prefab-registration feature

No step in this list has been run in a game instance during this API review.

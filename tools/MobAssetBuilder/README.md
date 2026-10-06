# Dreamborne Unity model pipeline (Windows, Unity 6000.0.77f1)

This is the build tooling for the ten checked-in Dreamborne enemy models. The repository contains the ten real authored source models, an editor importer/builder, a runtime manifest contract and a packaging validator. Generated AssetBundles, prefabs and the game's DLLs are never committed.

Equality of a model pack between coop players is established by the runtime manifest's `contentVersion` string (stamped anew on every rebuild), the per-bundle file sizes, the exact Unity version, the exact game version and the build target. Hash-based verification is intentionally not used anywhere in this pipeline.

## What the tooling does

- Imports the ten checked-in FBX files recorded in `assets/dreamborne/manifest.json`; GLB/Blend remain reference/edit sources and are not Unity runtime inputs
- Checks recorded source sizes and uses each actual FBX `takeName`; imports Generic avatars and the real `Idle`, `Walk`, `Attack` clips
- Disables root motion, removes clip events, preserves addressable bones, and requires actual animation curves
- Creates URP/Lit palette materials with base color and emission; converts glTF metallic/roughness B/G into Unity metallic/smoothness R/A when a Unity-ready map is absent
- **Sources build** (`BuildSources`): builds ten model-only AssetBundles (one per model) with no game SDK, no exported URP pipeline asset and no EntityModel templates. The prefabs contain only Transform/Animator/renderer/mesh components with the palette material
- **Full build** (`Build`): starts from user-supplied same-version, model-only `EntityModel` templates; preserves/rebinds their anchors and custom mapping targets instead of synthesizing a guessed model component. This game-integrated path is not runnable until the open points under 「未確認」 in [docs/specs/mob-model-integration.md](../../docs/specs/mob-model-integration.md) are resolved
- Verifies renderer skinning, UVs, materials, bindposes, allowed components, clip paths and sampled animation bounds
- Builds one platform-specific bundle per model, reloads each, checks prefab/event integrity, and only then writes the runtime manifest
- `tools/package_mob_mod.py` produces a review ZIP (and optionally an unpacked tree) with `about/`, `DreamforgeRPG.dll` and `models/`; it never copies to a game's `Mods` folder

## Environment on this PC

- Unity **6000.0.77f1** at `D:\app\Unity\Hub\Editor\6000.0.77f1\Editor\Unity.exe` (same editor line as the installed game)
- `urpVersion` **17.0.4** is pinned from the URP version that this editor's own Universal 3D project template installs. The game-side exact URP package version cannot be read from its DLLs and remains unconfirmed
- `gameVersion` **r.1.4.0.13_s** is the label recorded in the installed game's `version.txt`; whether it equals the runtime `Application.version` token is unconfirmed
- Python 3.12 (any 3.11+ works) for the repository tools

## 1. Materialize the sources and prepare the isolated project

Run from the repository root (`C:\Temp\wt-mobassets` below; adjust to your checkout):

```bat
python tools\materialize_mob_assets.py
python tools\MobAssetBuilder\prepare_project.py --sources-only ^
  --source-root assets\dreamborne ^
  --config tools\MobAssetBuilder\mob-build.example.json ^
  --project C:\Temp\mob-unity-project
```

`prepare_project.py` validates the config and the recorded source sizes before writing anything, then copies only the checked source FBX/textures, the editor script, pins URP in `Packages/manifest.json` and the editor in `ProjectSettings/ProjectVersion.txt`. A nonempty destination is rejected. The project must live outside this repository and outside every `Mods` directory.

For a full game-integrated build, drop `--sources-only`, add `--managed-dir "D:\app\stm\steamapps\common\Shape of Dreams\Shape of Dreams_Data\Managed"`, list the needed SDK assemblies (starting with `Dew.Core.dll`) under `sdkAssemblies`, set `renderPipelineAsset` to an exported same-game URP asset under `Assets/LocalTemplates/`, and fill every model binding:

- `baseMonsterTypes`: explicit, curated game type names, verified against the exact SDK. The builder verifies they derive from `Monster`; it does not choose types by anatomy or name similarity. Hypotheses are listed in the issue #19 spec §5.2 and stay empty in a sources build
- `templatePrefab`: `Assets/LocalTemplates/...prefab` from the same game's SDK/reference model. Copy associated `.meta` files and dependencies so GUID references survive
- `replaceChildPath`: the exact child hierarchy branch replaced by the FBX. The branch's position/rotation/scale are retained
- `clipBindings`: exact serialized `AnimationClip` object-reference paths on the root `EntityModel`. Idle uses `idle.clip`; this forward-only pack requires `Simple` locomotion and `runForwardClip`
- `objectBindings`: explicit `{ "propertyPath": "...", "targetPath": "..." }` mappings for anchors living under the replaced branch

**Attack is an unresolved integration gate.** The public `EntityModel` documentation exposes idle, stagger, death and locomotion clips but no attack slot. `abilityAnimationReplacements` exists in the game build but its element shape, per-Ability keys and timing are unconfirmed. A sources build simply leaves the clip unbundled from any game slot; a full build must not put Attack into `death.clip`/`stagger.clip`, invent an `attack` property, or silently omit the clip.

## 2. Build the ten AssetBundles (sources mode)

```bat
"D:\app\Unity\Hub\Editor\6000.0.77f1\Editor\Unity.exe" -batchmode -nographics -quit ^
  -projectPath C:\Temp\mob-unity-project ^
  -buildTarget Win64 ^
  -executeMethod Dreamforge.MobAssets.MobAssetBuilder.BuildSources ^
  -mobOutput C:\Temp\mob-build-output ^
  -logFile C:\Temp\mob-unity-build.log
```

`-mobOutput` must be a new or empty directory outside every `Mods` folder. Unity CLI target names differ from the manifest enum names:

| Manifest `buildTarget` | Unity CLI `-buildTarget` |
| --- | --- |
| `StandaloneWindows64` | `Win64` |
| `StandaloneLinux64` | `Linux64` |
| `StandaloneOSX` | `OSXUniversal` |

The editor's active target must match. The build imports all ten rigs, re-derives clip loops/bounds, writes one prefab per model, builds and reloads ten `<modelId>_<target>.bundle` files and then writes `manifest.json`. The method exits nonzero on any validation failure; check `-logFile` first.

Import-only inspection (no bundles) uses `-executeMethod Dreamforge.MobAssets.MobAssetBuilder.ImportSources` with the same other flags.

### Runtime `models/manifest.json` schema

```json
{
  "schemaVersion": 1,
  "packId": "dreamborne_enemies_vol01",
  "contentVersion": "20261006T091500Z-001",
  "unityVersion": "6000.0.77f1",
  "gameVersion": "r.1.4.0.13_s",
  "buildTarget": "StandaloneWindows64",
  "bundles": [
    { "file": "ember_warden_standalonewindows64.bundle", "size": 123456 }
  ],
  "models": [{
    "id": "ember_warden",
    "prefab": "assets/mobassetbuilder/generated/prefabs/ember_warden.prefab",
    "bundle": "ember_warden_standalonewindows64.bundle",
    "baseMonsterTypes": []
  }]
}
```

The real output contains all ten bundle/model entries. `contentVersion` is a fresh UTC timestamp plus a short counter on every rebuild; the packager stamps a new one for every packaged ZIP, so two independently packaged revisions never compare equal. Coop equality is judged on `contentVersion`, the ten bundle sizes, `unityVersion`, `gameVersion` and `buildTarget`; a sources build carries empty `baseMonsterTypes` and is not installable as a game-integrated pack.

## 3. Package without deploying

Build the mod assembly without touching the installed game. With the .NET SDK installed, redirect the automatic deploy target away from the real `Mods` folder:

```bat
dotnet build src\SodRpg.Mod\SodRpg.Mod.csproj -c Release ^
  -p:GameDir="D:\app\stm\steamapps\common\Shape of Dreams" ^
  -p:ModDeployDir=C:\Temp\mob-deploy-out
```

This PC has no .NET SDK (runtime only), so the same compile was reproduced with the Roslyn compiler that ships inside the Unity editor, using the game's own assemblies as references (same source set, `netstandard2.1`, C# 9, references exactly the `GameRef` list of `SodRpg.Mod.csproj`):

```bat
"C:\Program Files\dotnet\dotnet.exe" exec ^
  "D:\app\Unity\Hub\Editor\6000.0.77f1\Editor\Data\DotNetSdkRoslyn\csc.dll" ^
  @C:\Temp\mob-csc.rsp
```

Either way the output is `C:\Temp\mob-deploy-out\DreamforgeRPG.dll`; the response file lists `-nostdlib -noconfig -target:library -optimize+ -langversion:9.0`, every game reference DLL and all 321 mod/core sources.

Then package:

```bat
python tools\package_mob_mod.py ^
  --dll C:\Temp\mob-deploy-out\DreamforgeRPG.dll ^
  --about-dir src\SodRpg.Mod\about ^
  --models-dir C:\Temp\mob-build-output ^
  --output C:\Temp\mob-package\DreamforgeRPG-dreamborne-windows.zip ^
  --output-dir C:\Temp\mob-package ^
  --unity-version 6000.0.77f1 ^
  --game-version r.1.4.0.13_s ^
  --build-target StandaloneWindows64
```

The ZIP allowlist is exactly one mod DLL, the four required `about` files, the runtime manifest and the ten bundles; `--output-dir` additionally writes the identical unpacked tree. It rejects size mismatches against the manifest, unknown versions, target mismatch, missing/duplicate models, wrong filenames, symlink input files, oversized bundles, existing output files and `Mods` output paths. Sources, SDK DLLs, templates, project files and Unity build manifests are never recursively copied.

## Validation and release gates

Repository-only checks:

```bat
python tools\validate_mob_assets.py
python -m unittest discover -s tools\MobAssetBuilder/tests -v
python -m unittest discover -s tests/assets -v
```

The packaging unit tests use explicitly synthetic test bytes. They prove validation/ZIP behavior, not a real managed assembly or Unity build.

Before calling this game-ready, record evidence for all ten models: exact SDK/editor/URP locks, real type/animation bindings, in-editor visuals (facing, scale, anchors, bounds, no pink materials), idle/walk/attack playback through the game's animation flow, repeated spawn/despawn and unload cycles, host/client identical-pack behavior, and distribution permission. Visual and multiplayer verification happens by actually playing; until then the pack stays 「未確認」.

## Verified references and remaining assumptions

- [EntityModel public API](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityModel.html): model renderers, idle/stagger/death and locomotion, anchors, initialized state
- [AnimationClipWithSpeed](https://lizardsmoothie.com/sod/moddoc/api/Global.AnimationClipWithSpeed.html): `clip` and `speed`
- [EntityModelCustomMapping](https://lizardsmoothie.com/sod/moddoc/api/Global.EntityModelCustomMapping.html): `id` and GameObject `target`
- [Unity clip import settings](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/modelimporterclipanimation): actual takes, loops, events and root baking
- [Unity Generic avatar creation](https://docs.unity3d.com/cn/2021.2/ScriptReference/ModelImporterAvatarSetup.CreateFromThisModel.html): avatar subasset and root Animator
- [Unity AssetBundle address names](https://docs.unity3d.com/cn/6000.0/ScriptReference/AssetBundleBuild-addressableNames.html): explicit per-asset loading addresses

These docs support API shape, not the target game's undisclosed exact engine/package versions or private attack-animation behavior. The same-version SDK and real editor/game tests remain authoritative.

### Dependency provenance failures

An unapproved dependency error requires reviewing the named reference in the local template. Clear it only if optional, or rebind it to an authored source/generated asset. Required behavior needs an authored replacement and verified mapping; copying game assets into Generated does not approve them.

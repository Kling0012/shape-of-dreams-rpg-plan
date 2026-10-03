> Environment evidence: Issue #19's later research records Unity **6000.0.77f1** from the installed Windows game (label r.1.4.0.13_s). The example now pins that editor; exact URP package version, Application.version token, template and ability mappings still require verification.

# Dreamborne Unity model pipeline

This is **unverified Unity build tooling, not a built game-ready model pack**. The repository contains the ten real authored source models, an editor importer/builder, a fail-closed runtime contract, and a packaging validator. There is no checked-in `models/manifest.json`, fabricated prefab, AssetBundle, game DLL or distributable installation ZIP.

No Unity editor or game DLLs are available in the implementation environment. Python tests run here; the editor script has **not been compiled or executed**. Exact Unity patch, URP version, game version, curated monster bindings, same-version templates, and the game's attack-animation hookup are still required. A successful source import alone does not close those gates.

## What the tooling does

- Imports the ten checked-in FBX files from `assets/dreamborne/manifest.json`; GLB/Blend remain reference/edit sources and are not Unity runtime inputs
- Checks source bytes against SHA256 and uses each actual FBX `takeName`; imports Generic avatars and the real `Idle`, `Walk`, `Attack` clips
- Disables root motion, removes clip events, preserves addressable bones, and requires actual animation curves
- Creates URP/Lit palette materials with base color and emission; converts glTF metallic/roughness B/G into Unity metallic/smoothness R/A when a Unity-ready map is absent
- Starts from a user-supplied same-version, model-only `EntityModel` template; preserves/rebinds its anchors and custom mapping targets instead of synthesizing a guessed model component
- Verifies renderer skinning, UVs, materials, bindposes, uninitialized model state, allowed components, clip paths and sampled animation bounds
- Builds one platform-specific bundle, reloads it, checks prefab/event integrity, and only then writes a SHA256-locked runtime manifest
- Produces a separate review ZIP with `about/`, `DreamforgeRPG.dll` and `models/`; never copies to a game's `Mods` folder

## 1. Supply exact local prerequisites

Use Python 3.11+ and the exact editor version that built the target game. Read the game's own version/log/SDK evidence. Do not substitute an assumed Unity LTS line or `latest`. `gameVersion` must be the exact `Application.version` token the runtime checks, not a Steam marketing version guessed from a news post. Record the exact URP package version, an exported same-game URP pipeline asset, and all required game SDK DLL hashes. The actual values are intentionally blank in `mob-build.example.json`.

`Dew.Core.dll` must come from the target game's `Managed` folder. Add its required non-Unity dependencies by exact filename and SHA256. Do not copy UnityEngine/System assemblies over the editor's assemblies. If the exact SDK requires additional official Unity packages (for example Input System or TextMeshPro), record them as `additionalPackages: [{ "name": "com.unity.PACKAGE", "version": "EXACT.VERSION.HERE" }]`; the bootstrap pins those packages and the editor checks their resolved versions. Do not guess package versions. If exact SDK dependencies cannot compile in the isolated editor project, stop and resolve the real matching package/SDK setup; do not patch the game DLL or fabricate API stubs.

Game assemblies and template assets are local build inputs only. Never commit or redistribute them. Source-pack inclusion does not establish a general redistribution license; resolve the source manifest's licensing note before public distribution.

### Required model bindings

Copy `mob-build.example.json` outside the repository and fill every entry:

- `baseMonsterTypes`: explicit, curated game type names for each model, verified against the exact SDK. The builder verifies they derive from `Monster`; it does not choose types by anatomy or name similarity
- `templatePrefab`: `Assets/LocalTemplates/...prefab` from the same game's SDK/reference model, plus SHA256 of those exact prefab bytes. Copy associated `.meta` files and dependencies so GUID references survive
- `replaceChildPath`: the exact child hierarchy branch that can be replaced with the FBX. The branch's position/rotation/scale are retained; review these for the new model's size and facing
- `clipBindings`: exact serialized `AnimationClip` object-reference paths on the root `EntityModel`. Idle uses `idle.clip`; this forward-only pack requires `Simple` locomotion and `runForwardClip`
- `objectBindings`: explicit `{ "propertyPath": "...", "targetPath": "..." }` mappings for anchors that live under the replaced branch. Paths target the resulting prefab hierarchy; the builder distinguishes GameObject and Transform slots by serialized type

**Attack is an unresolved integration gate.** The official public `EntityModel` documentation exposes idle, stagger, death and locomotion clips. It does not establish an attack slot. `customMappings` contains `id` and GameObject `target`, not animation clips. The blank Attack binding is intentional. A verified private/serialized slot in the exact SDK could be used if it really exists and is exercised by the game. Otherwise, a separate verified game-specific animation adapter must be implemented and reviewed before this builder can produce a full pack. Do not put Attack in `death.clip`/`stagger.clip`, invent an `attack` property, silently omit the clip, or report full integration based on import success.

The three source clips also do not provide authored death, stagger, strafe or backward motion. A template that retains old-skeleton clips fails validation. Required behaviors need real authored clips plus a verified adapter, or a confirmed same-game template that safely does not require them. The current builder allows only a root `EntityModel`, Transform, Animator, renderer and mesh-filter components; gameplay/network/physics scripts and external FX dependencies fail closed. Loosening this allowlist requires a separately verified runtime contract.

## 2. Prepare an isolated project

Choose a new empty folder outside this checkout and all game/`Mods` directories. The script does not install Unity, launch the game, or publish anything.

```sh
python3 tools/MobAssetBuilder/prepare_project.py \
  --source-root assets/dreamborne \
  --config /your/local/mob-build.json \
  --managed-dir "/your/game/Shape of Dreams_Data/Managed" \
  --project /your/isolated/DreamborneBuilder
```

This validates inputs before writing and copies only the checked source FBX/textures, editor code and explicitly locked SDK DLLs. It pins URP in `Packages/manifest.json` and Unity in `ProjectSettings/ProjectVersion.txt`. A nonempty destination is rejected. Opening the project in your already installed editor will resolve its pinned Unity package dependencies using the editor's normal workflow.

Import the local templates, their `.meta`/dependency assets and exact URP pipeline asset into `Assets/LocalTemplates`. The editor build checks their hashes again and uses the supplied rendering pipeline. Review SDK import errors and exact dependency requirements before running a build.

### Import-only inspection while bindings are unresolved

`--sources-only` permits unresolved template/monster/clip bindings in the config while still requiring exact environment, URP pipeline and SDK locks. It does not authorize generating a model pack. After importing the locked URP pipeline asset, run:

```sh
"/path/to/exact/Unity" -batchmode \
  -projectPath /your/isolated/DreamborneBuilder -buildTarget Win64 \
  -executeMethod Dreamforge.MobAssets.MobAssetBuilder.ImportSources \
  -logFile /your/local/dreamborne-import.log
```

This creates imported rigs, thirty standalone clips and materials for inspection; it emits no model prefabs, bundle or runtime manifest. Inspect each model in the editor: face/facing/scale, skin deformation, palette UVs, emission, clip loops, bounds/culling and no root motion. The method exits with nonzero status on error. Use a fresh project to keep import-only experiments separate from a previous release build.

## 3. Build and verify a single platform

Only after every exact-template binding is confirmed, run the full entry point. `-mobOutput` must be a new or empty directory.

```sh
"/path/to/exact/Unity" -batchmode \
  -projectPath /your/isolated/DreamborneBuilder -buildTarget Win64 \
  -executeMethod Dreamforge.MobAssets.MobAssetBuilder.Build \
  -mobOutput /your/staging/windows-models \
  -logFile /your/local/dreamborne-build.log
```

Unity CLI target names differ from manifest enum names:

| Manifest `buildTarget` | Unity CLI `-buildTarget` |
| --- | --- |
| `StandaloneWindows64` | `Win64` |
| `StandaloneLinux64` | `Linux64` |
| `StandaloneOSX` | `OSXUniversal` |

The editor's active target must match. Install the correct platform build support yourself if missing. This builder also requires successful bundle reload in the build editor; use a matching-platform editor/runner rather than treating a cross-OS build as verified. Build and test each supported platform separately. A Windows bundle is not a Linux/macOS bundle.

The output bundle is named `dreamborne_enemies_vol01_<lowercase-buildTarget>.bundle`. Unity may also write its own build manifests; the packaging tool intentionally excludes those unrelated build files. The runtime `manifest.json` schema is:

```json
{
  "schemaVersion": 1,
  "packId": "dreamborne_enemies_vol01",
  "contentVersion": "1",
  "unityVersion": "<exact editor version>",
  "gameVersion": "<exact Application.version>",
  "buildTarget": "StandaloneWindows64",
  "bundleFile": "dreamborne_enemies_vol01_standalonewindows64.bundle",
  "bundleSha256": "<SHA256 of bundle bytes>",
  "models": [{
    "id": "ember_warden",
    "prefab": "assets/mobassetbuilder/generated/prefabs/ember_warden.prefab",
    "baseMonsterTypes": ["<verified exact game type>"]
  }]
}
```

The real output contains all ten entries, not the shortened illustrative list above. Model IDs and prefab addresses are stable; monster bindings are not guessed. `contentVersion` is a JSON string. The exact manifest bytes are part of multiplayer compatibility, so do not reformat a release manifest independently on different clients.

## 4. Package without deploying

Build the mod against the same local game SDK with deployment explicitly disabled:

```sh
dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release \
  -p:GameDir="/your/game" -p:DeployModToGame=false

python3 tools/package_mob_mod.py \
  --dll src/SodRpg.Mod/bin/Release/netstandard2.1/DreamforgeRPG.dll \
  --about-dir src/SodRpg.Mod/about \
  --models-dir /your/staging/windows-models \
  --unity-version YOUR_EXACT_UNITY_VERSION \
  --game-version YOUR_EXACT_GAME_VERSION \
  --build-target StandaloneWindows64 \
  --output /your/review/DreamforgeRPG-dreamborne-windows.zip
```

The ZIP allowlist is exactly one mod DLL, the four required `about` files, and the runtime manifest/bundle. It rejects stale bundle hashes, unknown versions, target mismatch, missing/duplicate models, empty mappings, wrong filenames, symlink input files, bundles larger than the runtime's 256 MiB limit, existing output files and `Mods` output paths. Sources, SDK DLLs, templates, project files and Unity build manifests are never recursively copied. Packaging checks file/hash structure; it cannot establish Unity correctness or game compatibility by itself.

## Validation and release gates

Repository-only checks:

```sh
python3 tools/validate_mob_assets.py
python3 -m unittest discover -s tools/MobAssetBuilder/tests -v
```

The packaging unit tests use explicitly synthetic test bytes. They prove validation/ZIP behavior, not a real managed assembly or Unity build. The test-only sample versions and type names are not claims about the game.

Before calling this game-ready, record evidence for all ten models:

1. Exact SDK/editor/URP version locks and actual game type/animation bindings reviewed
2. Editor script compiles; Generic skins and all thirty actual clips import; no missing scripts, events, colliders, network components or pink materials
3. Idle, walk and attack really play through the game's animation flow; target-required death/stagger behavior is verified
4. Correct facing, scale, anchors, bounds/culling, instancing and source palette appearance in the target game
5. Fresh uninitialized prefab replacement and restoration to the default model pass repeated spawn/despawn, disable/enable and unload cycles
6. Host/client identical-pack compatibility, missing pack, different bundle/manifest, wrong Unity/game/platform, late join and disconnect pass the runtime's fail-closed behavior
7. Zip contents checked; proprietary build inputs excluded; distribution permission confirmed

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

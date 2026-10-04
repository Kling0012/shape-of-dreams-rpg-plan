# Dreamborne Enemies Vol. 01 source assets

The ten previously created models requested for this project are included here as
actual binary assets, not placeholders. The source pack is
`Dreamborne_Enemies_Vol01`, supplied from the user's earlier model-creation work.
This directory contains 10 FBX, 10 GLB, 10 editable Blender sources, and 33 PNGs.
The exported models total **10 skinned meshes, 20,562 triangles, 209 bones, and
30 animations** (Idle, Walk, Attack for each model).

## Provenance and permission

The user explicitly approved publishing these ten models in this public repository PR on 2026-10-04.
The original delivery did **not** contain an explicit distribution license.
No new license or third-party authorship claim is invented here. Inclusion in this
PR does not establish a general license to redistribute the pack; establish the
intended asset license before distributing it independently. The repository's
code licensing should not be assumed to settle the asset licensing question.

Only the model binaries, texture images, and curated metadata are included.
Local absolute paths, machine directory remnants, delivery logs, screenshots,
and unrelated material are excluded. Essential FBX object IDs, bone names, and
animation take names remain intact for import compatibility.

## Which files to use

- **FBX is the authoritative Unity import input.** Each contains one runtime mesh,
  its armature, and three animation takes. Use the exact `clips[].takeName` values
  in `manifest.json`; FBX take names carry an armature prefix.
- **GLB is the self-contained reference export.** It includes a skinned mesh,
  embedded PNG textures, and Idle/Walk/Attack. It is not loaded by the Unity
  pipeline, so a glTF runtime dependency is unnecessary.
- **Blender is the editable source.** These files contain multiple editable mesh
  parts; that count is distinct from the single mesh in each runtime export.
  They were created with Blender 4.3.2. Image paths are relative to `Textures/`.
- BaseColor and Emission are color textures. MetallicRoughness follows glTF
  packing: green = roughness, blue = metallic. Do not assign it directly as a
  Unity metallic/smoothness map. Models 04–06 additionally supply a
  MetallicSmoothness texture. Atlas sizes are 128x128 (01–03) and 256x256 (04–10).
- The source convention is meters, Blender Z-up / -Y-forward, in-place motion.
  Ground placement, import axes, shader behavior, materials, combat timing, and
  animation behavior still require validation in the target game.

## Stable model IDs

| Model ID | Source folder | Triangles | Bones |
| --- | --- | ---: | ---: |
| `ember_warden` | `01_ember_warden` | 2,712 | 16 |
| `mirecap_stomper` | `02_mirecap_stomper` | 2,340 | 16 |
| `duskwing_oracle` | `03_duskwing_oracle` | 2,100 | 16 |
| `thorncrown_stag` | `04_thorncrown_stag` | 1,922 | 18 |
| `frostjaw_prowler` | `05_frostjaw_prowler` | 2,610 | 20 |
| `runestone_tortoise` | `06_runestone_tortoise` | 3,020 | 19 |
| `glasswing_moth` | `07_glasswing_moth` | 1,400 | 16 |
| `obsidian_scorpion` | `08_obsidian_scorpion` | 1,936 | 33 |
| `lantern_wisp` | `09_lantern_wisp` | 880 | 29 |
| `abyssal_bell` | `10_abyssal_bell` | 1,642 | 26 |

## Manifest contract (schemaVersion 1)

`manifest.json` is the source catalog for the later Unity editor build step.
All paths are case-sensitive, forward-slash paths relative to this directory;
they are not Unity `Assets/` paths or machine-local paths.

- Root: `schemaVersion`, `packId`, `sourcePackName`, `sourceDescription`,
  `licenseStatus`, `authoritativeUnityFormat`, `runtimeVerified`, `sanitization`,
  `models`, `totals`, `files`
- Model: stable `modelId`, original `sourceAssetId`, `name`, `nameJa`, `designJa`,
  relative `fbx`, `glb`, `blend`, `textures`, `triangles`, `bones`, `meshCount`,
  `materialCount`, `clips`, and `source`
- Textures: `baseColor`, `metallicRoughness`, `emission`, and optional
  `metallicSmoothness` relative filenames
- Clip: `name` (Idle/Walk/Attack), exact FBX `takeName`, `frameStart`, `frameEnd`,
  `fps`, `durationSeconds`, `loop`. Source frame ranges start at 1 and use 30 fps;
  duration is `(frameEnd - frameStart) / fps`. GLB time samples likewise start at
  1/30 second, so clip duration is the last timestamp minus the first timestamp.
- Source: Blender version, units/axes, root-motion flag, dimensions in meters,
  editable mesh and source vertex counts, weighting description, texture sizes,
  and `runtimeVerified: false`
- File: `path`, `bytes`, checked-in `sha256`, original-delivery `sourceSha256`,
  and boolean `sanitized`. Hashes are lowercase SHA-256 hex, computed over the
  entire raw file. The manifest does not hash itself.

The 43 GLB/PNG files are byte-identical to the original delivery. The 20 FBX/Blend
files only have path metadata sanitized. FBX string lengths and node offsets are
rebuilt when path strings become shorter; Blender fixed-size string fields and
stale path fragments are cleared in place, preserving binary block offsets. The
algorithm is `portable-paths-v1`, implemented in `tools/validate_mob_assets.py`.
Original and checked-in hashes are recorded separately so this change is explicit.

## Materialize the checked-in source archive

The actual source bytes are stored in this repository as 88 ordered parts,
`source-pack.zip.001` through `source-pack.zip.088` (64 KiB maximum per part).
They form one deterministic ZIP archive containing exactly the 63 source files
listed in `manifest.json`. This is a compact, self-contained source distribution;
no external asset download, credentials, Git LFS, or Python dependencies are
required. The materialized model folders are ignored by Git.

After a fresh clone, run from the repository root before validation or the Unity
editor build:

```sh
python tools/materialize_mob_assets.py
```

`source-archive.json` records the ordered chunk hashes, combined archive hash,
manifest hash, file count, and compressed/uncompressed byte counts. The tool
verifies all archive entries against the unchanged source manifest before writing
files. It rejects unsafe paths, symlinks, extra ZIP entries, corrupt data, and
modified existing files; it never overwrites local source edits. Re-running it is
safe. Use `--verify-only` to check the archive and extracted files without writing,
or `--destination PATH` to verify extraction into a separate empty directory.

The ZIP uses sorted relative filenames, fixed 1980-01-01 timestamps, regular-file
permissions, DEFLATE level 9, and no comments or extra fields. No workspace paths
or delivery metadata are added. Archive packaging does not change asset bytes,
source provenance, or the licensing status described above.

## Reproducible verification

Run from the repository root with Python 3.9 or newer. No third-party Python
packages, Blender, Unity, internet access, or game installation are required:

```sh
python tools/materialize_mob_assets.py
python tools/validate_mob_assets.py
python -m unittest discover -s tests/assets -v
```

The validator checks every file hash and size, safe paths, all model IDs and
references, binary FBX structure and take names, PNG CRCs and dimensions, and GLB
chunk/accessor bounds, mesh/triangle/material/bone counts, normalized weights,
joint indices, inverse bind matrices, embedded textures, clip names/timing, and
animation target/sampler references. `runtimeVerified` remains false.

If the original delivery is available, verify both original hashes and exact
reproduction of the sanitized binary files:

```sh
python tools/validate_mob_assets.py --source-pack /path/to/Dreamborne_Enemies_Vol01
```

For an optional independent Blender import check (tested with Blender 4.3.2):

```sh
blender --background --disable-autoexec --python-exit-code 1 \
  --python tests/assets/verify_blender_source_equivalence.py -- \
  --source-pack /path/to/Dreamborne_Enemies_Vol01
```

That check opens all ten editable sources and imports all ten FBX exports. With
`--source-pack`, it compares topology, vertices, weights, object transforms, bone
rest matrices, and animation key fingerprints before and after sanitization.
All 20 format/model comparisons passed when this pack was prepared. Omit
`--source-pack` for an import/count-only check. It never saves the Blender files.

These are offline source checks. **Unity AssetBundle build, actual game loading,
visuals, physics, attack-event synchronization, and multiplayer behavior are not
verified by this pack.** Keep the in-game feature disabled until those separate
integration checks pass against the supported game and Unity versions.

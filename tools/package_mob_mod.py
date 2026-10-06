#!/usr/bin/env python3
"""Validate an exact-platform Dreamborne pack and create review outputs, never deploy it.

The runtime manifest is identified by its required `contentVersion` string plus
per-bundle `size` entries; this tool stamps a fresh `contentVersion` (UTC
timestamp + same-timestamp counter) on every rebuild so two independently
packaged revisions never compare equal. Hash-based verification is not used.
"""
import argparse
import datetime
import json
import os
import re
import tempfile
import zipfile
from pathlib import Path

PACK_ID = "dreamborne_enemies_vol01"
MODEL_IDS = {
    "ember_warden", "mirecap_stomper", "duskwing_oracle", "thorncrown_stag",
    "frostjaw_prowler", "runestone_tortoise", "glasswing_moth",
    "obsidian_scorpion", "lantern_wisp", "abyssal_bell",
}
TARGETS = {"StandaloneWindows64", "StandaloneLinux64", "StandaloneOSX"}
ABOUT_FILES = {"metadata.json", "description.txt", "icon.png", "preview.png"}
ROOT = "DreamforgeRPG"
MAX_BUNDLE_BYTES = 256 * 1024 * 1024
_STAMP_COUNTER = [0]


def content_version(now=None):
    """Fresh pack identity for every rebuild: UTC timestamp + short counter."""
    _STAMP_COUNTER[0] += 1
    moment = datetime.datetime.now(datetime.timezone.utc) if now is None else now
    return moment.strftime("%Y%m%dT%H%M%SZ") + f"-{_STAMP_COUNTER[0]:03d}"


def _distinct_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_distinct_keys)


def bundle_name(model_id, build_target):
    return f"{model_id}_{build_target.lower()}.bundle"


def validate_manifest(manifest, unity_version, game_version, build_target):
    if type(manifest.get("schemaVersion")) is not int or manifest["schemaVersion"] != 1:
        raise ValueError("Expected manifest schemaVersion 1")
    if manifest.get("packId") != PACK_ID:
        raise ValueError("Expected Dreamborne pack")
    if not isinstance(manifest.get("contentVersion"), str) or not manifest["contentVersion"]:
        raise ValueError("Runtime manifest requires a nonempty contentVersion string")
    if not re.fullmatch(r"\d+\.\d+\.\d+[abfp]\d+", unity_version):
        raise ValueError("Supply the exact Unity editor version, including patch/build suffix")
    if (not isinstance(game_version, str) or len(game_version) > 128
            or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+_-]*", game_version)
            or game_version.lower() in {"unknown", "todo", "replace_me", "latest"}):
        raise ValueError("Supply an exact game version; unknown/wildcard versions are forbidden")
    if build_target not in TARGETS:
        raise ValueError("Supply exactly one supported Standalone build target")
    for key, value in (("unityVersion", unity_version), ("gameVersion", game_version), ("buildTarget", build_target)):
        if manifest.get(key) != value:
            raise ValueError(f"Manifest {key} does not match the intended release")
    models = manifest.get("models", [])
    if len(models) != 10 or {m.get("id") for m in models} != MODEL_IDS:
        raise ValueError("Runtime manifest must include all ten distinct model IDs")
    for model in models:
        expected_prefab = f"assets/mobassetbuilder/generated/prefabs/{model['id']}.prefab"
        if model.get("prefab") != expected_prefab:
            raise ValueError(f"Unexpected prefab address for {model['id']}")
        types = model.get("baseMonsterTypes", [])
        if not isinstance(types, list) or len(types) != len(set(types)) or any(
                not isinstance(t, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.+]*", t) for t in types):
            raise ValueError(f"{model['id']}: baseMonsterTypes must be distinct valid type names or empty")
    bundles = manifest.get("bundles", [])
    if len(bundles) != 10 or len({b.get("file") for b in bundles}) != 10:
        raise ValueError("Runtime manifest must list exactly ten distinct per-model bundles")
    for model in models:
        if model.get("bundle") != bundle_name(model["id"], build_target):
            raise ValueError(f"{model['id']}: bundle file must be {bundle_name(model['id'], build_target)}")
    if {b.get("file") for b in bundles} != {m.get("bundle") for m in models}:
        raise ValueError("Every model must reference one distinct listed bundle file")
    for entry in bundles:
        if not isinstance(entry.get("size"), int) or not 0 < entry["size"] <= MAX_BUNDLE_BYTES:
            raise ValueError(f"Bundle entry needs an exact positive size: {entry.get('file')}")
    return manifest
def regular_file(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"Expected a regular non-symlink file: {path}")

def collect_bundle_inputs(models_dir, manifest, build_target):
    inputs = {}
    for entry in manifest["bundles"]:
        path = models_dir / entry["file"]
        regular_file(path)
        actual = path.stat().st_size
        if actual != entry["size"]:
            raise ValueError(f"Bundle size differs from manifest entry: {entry['file']} ({actual} != {entry['size']})")
        if actual > MAX_BUNDLE_BYTES:
            raise ValueError(f"Bundle exceeds runtime loader limit of 256 MiB: {entry['file']}")
        with path.open("rb") as stream:
            if not stream.read(8).startswith(b"UnityFS\x00"):
                raise ValueError(f"Expected a UnityFS AssetBundle: {entry['file']}")
        inputs[f"{ROOT}/models/{entry['file']}"] = path
    return inputs


def package(dll, about_dir, models_dir, output, unity_version, game_version, build_target, output_dir=None):
    if dll.name != "DreamforgeRPG.dll":
        raise ValueError("Only the compiled DreamforgeRPG.dll is allowed, never SDK/game assemblies")
    regular_file(dll)
    with dll.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError("DreamforgeRPG.dll is not a PE assembly")
    # Use an explicit allowlist. No recursive copy of project/build directories.
    inputs = {f"{ROOT}/DreamforgeRPG.dll": dll}
    for name in sorted(ABOUT_FILES):
        path = about_dir / name
        regular_file(path)
        inputs[f"{ROOT}/about/{name}"] = path
    metadata = load_json(about_dir / "metadata.json")
    if not isinstance(metadata, dict) or not metadata:
        raise ValueError("about/metadata.json must be a nonempty JSON object")
    for name in ("icon.png", "preview.png"):
        with (about_dir / name).open("rb") as stream:
            if stream.read(8) != b"\x89PNG\r\n\x1a\n":
                raise ValueError(f"about/{name} is not PNG")
    manifest_path = models_dir / "manifest.json"
    regular_file(manifest_path)
    validated = validate_manifest(load_json(manifest_path), unity_version, game_version, build_target)
    inputs.update(collect_bundle_inputs(models_dir, validated, build_target))
    # A rebuild never reuses the previous identity: stamp a fresh contentVersion.
    manifest = dict(validated, contentVersion=content_version())
    manifest_bytes = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    resolved_output = output.resolve()
    if output.suffix.lower() != ".zip" or any(p.lower() == "mods" for p in resolved_output.parts):
        raise ValueError("Output must be a review .zip outside every live Mods directory")
    if output_dir is not None:
        resolved_dir = output_dir.resolve()
        if any(p.lower() == "mods" for p in resolved_dir.parts):
            raise ValueError("Output directory must be outside every live Mods directory")
    if output.exists():
        raise ValueError("Output already exists; choose a new filename instead of replacing a release")
    if any(path.resolve() == resolved_output for path in inputs.values()):
        raise ValueError("Output overlaps an input")
    output.parent.mkdir(parents=True, exist_ok=True)
    if output_dir is not None:
        output_dir.mkdir(parents=True, exist_ok=True)
    # Take a stable snapshot and recheck the source files; mutation during packaging is a failure.
    snapshots = {name: path.read_bytes() for name, path in inputs.items()}
    for entry in manifest["bundles"]:
        if len(snapshots[f"{ROOT}/models/{entry['file']}"]) != entry["size"]:
            raise ValueError(f"Bundle changed while packaging: {entry['file']}")
    if load_json(manifest_path) != validated:
        raise ValueError("Manifest changed while packaging")
    snapshots[f"{ROOT}/models/manifest.json"] = manifest_bytes
    _write_zip(output, snapshots)
    if output_dir is not None:
        for name, contents in sorted(snapshots.items()):
            destination = output_dir / name
            if destination.exists():
                raise ValueError(f"Output directory already contains {name}")
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(contents)
    print(f"Created {output} ({build_target}), contentVersion {manifest['contentVersion']}")
    if output_dir is not None:
        print(f"Unpacked the same reviewed tree into {output_dir}")
    print("Packaging passed. Unity import and in-game/multiplayer playtests are separate release gates.")
    return output


def _write_zip(output, snapshots):
    fd, temporary = tempfile.mkstemp(prefix=".dreamborne-", suffix=".zip", dir=output.parent)
    os.close(fd)
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, contents in sorted(snapshots.items()):
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                archive.writestr(info, contents)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None:
                raise ValueError("ZIP contents/integrity verification failed")
        # Same-filesystem hard link is atomic and fails if the destination already exists.
        # If the filesystem cannot support it, fail instead of leaving a partial release ZIP.
        os.link(temporary, output)
    finally:
        Path(temporary).unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True, type=Path)
    parser.add_argument("--about-dir", required=True, type=Path)
    parser.add_argument("--models-dir", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path, help="Review ZIP path; must not exist yet")
    parser.add_argument("--output-dir", type=Path,
                        help="Additionally write the exact reviewed tree (unpacked) into this directory")
    parser.add_argument("--unity-version", required=True)
    parser.add_argument("--game-version", required=True)
    parser.add_argument("--build-target", required=True)
    args = parser.parse_args()
    try:
        package(args.dll, args.about_dir, args.models_dir, args.output, args.unity_version,
                args.game_version, args.build_target, args.output_dir)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(2, f"Blocked: {error}\n")


if __name__ == "__main__":
    main()

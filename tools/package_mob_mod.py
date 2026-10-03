#!/usr/bin/env python3
"""Validate an exact-platform Dreamborne pack and create a review ZIP, never deploy it."""
import argparse
import hashlib
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


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _distinct_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_distinct_keys)


def validate_manifest(manifest, unity_version, game_version, build_target):
    if type(manifest.get("schemaVersion")) is not int or manifest["schemaVersion"] != 1:
        raise ValueError("Expected manifest schemaVersion 1")
    if manifest.get("packId") != PACK_ID or manifest.get("contentVersion") != "1":
        raise ValueError('Expected Dreamborne pack and contentVersion "1"')
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
    filename = manifest.get("bundleFile", "")
    expected = f"{PACK_ID}_{build_target.lower()}.bundle"
    if filename != expected:
        raise ValueError(f"Expected exact target bundle filename: {expected}")
    if not re.fullmatch(r"[a-f0-9]{64}", manifest.get("bundleSha256", "")):
        raise ValueError("bundleSha256 must be lowercase hexadecimal SHA256")
    models = manifest.get("models", [])
    if len(models) != 10 or {m.get("id") for m in models} != MODEL_IDS:
        raise ValueError("Runtime manifest must include all ten distinct model IDs")
    for model in models:
        expected_prefab = f"assets/mobassetbuilder/generated/prefabs/{model['id']}.prefab"
        if model.get("prefab") != expected_prefab:
            raise ValueError(f"Unexpected prefab address for {model['id']}")
        types = model.get("baseMonsterTypes", [])
        if not types or len(types) != len(set(types)) or any(not isinstance(t, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.+]*", t) for t in types):
            raise ValueError(f"{model['id']}: nonempty exact curated baseMonsterTypes required")
    return manifest


def regular_file(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"Expected a regular non-symlink file: {path}")


def package(dll, about_dir, models_dir, output, unity_version, game_version, build_target):
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
    manifest = validate_manifest(load_json(manifest_path), unity_version, game_version, build_target)
    bundle_path = models_dir / manifest["bundleFile"]
    regular_file(bundle_path)
    if bundle_path.stat().st_size > 256 * 1024 * 1024:
        raise ValueError("Bundle exceeds runtime loader limit of 256 MiB")
    if digest(bundle_path) != manifest["bundleSha256"]:
        raise ValueError("AssetBundle SHA256 mismatch; refusing to package stale/tampered bytes")
    with bundle_path.open("rb") as stream:
        if not stream.read(8).startswith(b"UnityFS\x00"):
            raise ValueError("Expected a UnityFS AssetBundle; a source archive is not a runtime pack")
    inputs[f"{ROOT}/models/manifest.json"] = manifest_path
    inputs[f"{ROOT}/models/{bundle_path.name}"] = bundle_path
    resolved_output = output.resolve()
    if output.suffix.lower() != ".zip" or any(p.lower() == "mods" for p in resolved_output.parts):
        raise ValueError("Output must be a review .zip outside every live Mods directory")
    if output.exists():
        raise ValueError("Output already exists; choose a new filename instead of replacing a release")
    if any(path.resolve() == resolved_output for path in inputs.values()):
        raise ValueError("Output overlaps an input")
    # Take a stable snapshot and recheck the source files; mutation during packaging is a failure.
    snapshots = {name: path.read_bytes() for name, path in inputs.items()}
    if hashlib.sha256(snapshots[f"{ROOT}/models/{bundle_path.name}"]).hexdigest() != manifest["bundleSha256"]:
        raise ValueError("Bundle changed while packaging")
    manifest_bytes = snapshots[f"{ROOT}/models/manifest.json"]
    if json.loads(manifest_bytes, object_pairs_hook=_distinct_keys) != manifest:
        raise ValueError("Manifest changed while packaging")
    output.parent.mkdir(parents=True, exist_ok=True)
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
            if archive.testzip() is not None or set(archive.namelist()) != set(inputs):
                raise ValueError("ZIP contents/integrity verification failed")
        # Same-filesystem hard link is atomic and fails if the destination already exists.
        # If the filesystem cannot support it, fail instead of leaving a partial release ZIP.
        os.link(temporary, output)
    finally:
        Path(temporary).unlink(missing_ok=True)
    print(f"Created {output} ({build_target}), SHA256 {digest(output)}")
    print(f"Manifest SHA256 {hashlib.sha256(manifest_bytes).hexdigest()}")
    print("Packaging passed. Unity import and in-game/multiplayer playtests are separate release gates.")
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=Path, required=True)
    parser.add_argument("--about-dir", type=Path, required=True)
    parser.add_argument("--models-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--unity-version", required=True)
    parser.add_argument("--game-version", required=True)
    parser.add_argument("--build-target", required=True, choices=sorted(TARGETS))
    args = parser.parse_args()
    try:
        package(args.dll, args.about_dir, args.models_dir, args.output,
                args.unity_version, args.game_version, args.build_target)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(2, f"Blocked: {error}\n")


if __name__ == "__main__":
    main()

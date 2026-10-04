#!/usr/bin/env python3
"""Create an isolated, version-pinned Unity project. Never touches a live Mods folder."""
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

PACK_ID = "dreamborne_enemies_vol01"
MODEL_IDS = (
    "ember_warden", "mirecap_stomper", "duskwing_oracle", "thorncrown_stag",
    "frostjaw_prowler", "runestone_tortoise", "glasswing_moth",
    "obsidian_scorpion", "lantern_wisp", "abyssal_bell",
)
TARGETS = {"StandaloneWindows64", "StandaloneLinux64", "StandaloneOSX"}
SHA256 = re.compile(r"[a-f0-9]{64}\Z")


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def exact_version(value, label):
    if (not isinstance(value, str) or len(value) > 128
            or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+_-]*", value)
            or value.lower() in {"unknown", "todo", "replace_me", "latest"}):
        raise ValueError(f"{label} must be an exact recorded version, not empty/unknown/wildcard")


def safe_relative(value):
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ValueError(f"Unsafe relative path: {value!r}")
    path = Path(value)
    if path.is_absolute() or any(p in {"", ".", ".."} for p in value.split("/")):
        raise ValueError(f"Unsafe relative path: {value!r}")
    return path


def validate_config(config, require_bindings=True):
    if config.get("schemaVersion") != 1:
        raise ValueError("Expected build config schemaVersion 1")
    if not re.fullmatch(r"\d+\.\d+\.\d+[abfp]\d+", config.get("unityVersion", "")):
        raise ValueError("unityVersion must include the exact editor patch/build (for example N.N.NfN)")
    if not re.fullmatch(r"\d+\.\d+\.\d+", config.get("urpVersion", "")):
        raise ValueError("urpVersion must be an exact stable package version")
    exact_version(config.get("gameVersion"), "gameVersion")
    if config.get("contentVersion") != "1":
        raise ValueError('This pack requires contentVersion "1" (a JSON string)')
    if config.get("buildTarget") not in TARGETS:
        raise ValueError("Select exactly one supported Standalone build target")
    package_names = {"com.unity.render-pipelines.universal"}
    for package in config.get("additionalPackages", []):
        name, version = package.get("name", ""), package.get("version", "")
        if not re.fullmatch(r"com\.unity\.[a-z0-9.-]+", name) or name in package_names:
            raise ValueError("Additional packages must be distinct official Unity registry package names")
        if not re.fullmatch(r"\d+\.\d+\.\d+", version):
            raise ValueError("Additional Unity dependencies require exact stable versions")
        package_names.add(name)
    pipeline = str(safe_relative(config.get("renderPipelineAsset", "")))
    if not pipeline.startswith("Assets/LocalTemplates/") or not pipeline.endswith(".asset"):
        raise ValueError("An exact-game URP pipeline asset under Assets/LocalTemplates is required")
    if not SHA256.fullmatch(config.get("renderPipelineSha256", "")):
        raise ValueError("Record the URP pipeline asset SHA256")
    assemblies = config.get("sdkAssemblies", [])
    names = set()
    for assembly in assemblies:
        name = assembly.get("file", "")
        if not re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", name) or name.lower().startswith(("unity", "system", "mscorlib", "netstandard")):
            raise ValueError(f"Not an allowed game SDK assembly filename: {name}")
        if name in names or not SHA256.fullmatch(assembly.get("sha256", "")):
            raise ValueError("SDK assemblies require distinct filenames and lowercase SHA256 locks")
        names.add(name)
    if "Dew.Core.dll" not in names:
        raise ValueError("The exact game's Dew.Core.dll is required, with SHA256")
    models = config.get("models", [])
    if len(models) != 10 or {m.get("id") for m in models} != set(MODEL_IDS):
        raise ValueError("All ten distinct Dreamborne model IDs must be configured")
    if not require_bindings:
        return config
    for model in models:
        types = model.get("baseMonsterTypes", [])
        if not types or len(types) != len(set(types)) or any(not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.+]*", t) for t in types):
            raise ValueError(f"{model['id']}: provide exact, distinct curated Monster types")
        template = str(safe_relative(model.get("templatePrefab", "")))
        if not template.startswith("Assets/LocalTemplates/") or not template.endswith(".prefab"):
            raise ValueError("Templates must be local Assets/LocalTemplates/*.prefab assets")
        if not SHA256.fullmatch(model.get("templateSha256", "")):
            raise ValueError(f"{model['id']}: record the exact template prefab SHA256")
        safe_relative(model.get("replaceChildPath", ""))
        semantics = set()
        bindings = model.get("clipBindings", [])
        paths = set()
        for binding in bindings:
            semantic = binding.get("semantic")
            if semantic not in {"Idle", "Walk", "Attack"}:
                raise ValueError("Only the three real source clip semantics are available")
            if binding.get("componentType") != "EntityModel" or binding.get("componentPath", ""):
                raise ValueError("Animation bindings must target the verified root EntityModel")
            path = binding.get("propertyPath", "")
            if not path or path in paths:
                raise ValueError("Every animation binding needs a distinct verified serialized property path")
            if semantic == "Idle" and path != "idle.clip":
                raise ValueError("Idle must bind verified EntityModel.idle.clip")
            if semantic == "Walk" and path != "runForwardClip":
                raise ValueError("This three-clip pack requires Simple locomotion and runForwardClip")
            if semantic == "Attack" and (path in {"idle.clip", "stagger.clip", "death.clip"} or path.startswith("run")):
                raise ValueError("Do not repurpose idle/walk/stagger/death as an Attack slot")
            paths.add(path)
            semantics.add(semantic)
        if semantics != {"Idle", "Walk", "Attack"}:
            raise ValueError(f"{model['id']}: explicit Idle, Walk and Attack bindings are all required")
        for binding in model.get("objectBindings", []):
            if not binding.get("propertyPath"):
                raise ValueError("Object binding requires an exact EntityModel serialized property path")
            safe_relative(binding.get("targetPath", ""))
    return config


def prepare(source_root, project, config_file, managed_dir, sources_only=False):
    config = validate_config(json.loads(config_file.read_text(encoding="utf-8")), require_bindings=not sources_only)
    source_root, project, managed_dir = (p.resolve() for p in (source_root, project, managed_dir))
    repo = Path(__file__).resolve().parents[2]
    if project == repo or repo in project.parents or any(p.lower() == "mods" for p in project.parts):
        raise ValueError("Use a separate empty Unity project outside the repository and every Mods directory")
    if managed_dir == project or managed_dir in project.parents or project in managed_dir.parents:
        raise ValueError("The isolated project must not overlap the game's Managed directory")
    if project.exists() and any(project.iterdir()):
        raise ValueError("Refusing to overwrite a nonempty project; create a fresh isolated folder")
    manifest = json.loads((source_root / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1 or manifest.get("packId") != PACK_ID:
        raise ValueError("Unexpected Dreamborne source manifest")
    if len(manifest.get("models", [])) != 10 or {m["modelId"] for m in manifest["models"]} != set(MODEL_IDS):
        raise ValueError("Source manifest must describe the actual ten models")
    files = {}
    for entry in manifest["files"]:
        relative = safe_relative(entry["path"])
        path = source_root / relative
        if path.is_symlink() or source_root not in path.resolve().parents:
            raise ValueError(f"Source file escapes pack: {relative}")
        if entry["path"] in files or digest(path) != entry["sha256"] or path.stat().st_size != entry["bytes"]:
            raise ValueError(f"Source hash/size mismatch or duplicate: {relative}")
        files[entry["path"]] = path
    needed = set()
    for model in manifest["models"]:
        needed.add(model["fbx"])
        needed.update(model["textures"].values())
    if not needed <= files.keys():
        raise ValueError("Source manifest has unhashed FBX/textures")
    for assembly in config["sdkAssemblies"]:
        path = managed_dir / assembly["file"]
        if path.is_symlink() or digest(path) != assembly["sha256"]:
            raise ValueError(f"Game SDK hash mismatch: {assembly['file']}")
    # All user inputs are checked before creating any files.
    editor_dir = project / "Assets/MobAssetBuilder/Editor"
    editor_dir.mkdir(parents=True)
    shutil.copy2(Path(__file__).parent / "Editor/MobAssetBuilder.cs", editor_dir)
    destination = project / "Assets/MobAssetBuilder/Source"
    for relative in sorted(needed):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(files[relative], target)
    shutil.copy2(source_root / "manifest.json", destination / "manifest.json")
    sdk_dir = project / "Assets/ThirdParty/GameSDK"
    sdk_dir.mkdir(parents=True)
    for assembly in config["sdkAssemblies"]:
        shutil.copy2(managed_dir / assembly["file"], sdk_dir / assembly["file"])
    (project / "Assets/LocalTemplates").mkdir()
    (project / "Packages").mkdir()
    packages = {"com.unity.render-pipelines.universal": config["urpVersion"]}
    packages.update({p["name"]: p["version"] for p in config.get("additionalPackages", [])})
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": packages}, indent=2) + "\n", encoding="utf-8")
    (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text(
        f"m_EditorVersion: {config['unityVersion']}\n", encoding="utf-8")
    (project / "mob-build.json").write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    (project / ".gitignore").write_text("Library/\nTemp/\nLogs/\nBuild/\nAssets/ThirdParty/\nAssets/LocalTemplates/\n", encoding="utf-8")
    print(f"Prepared {project}. No Unity import or bundle build has run.")
    print("Import your exact-game model-only SDK templates into Assets/LocalTemplates, then run the documented Unity command.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--managed-dir", required=True, type=Path)
    parser.add_argument("--sources-only", action="store_true", help="Allow unresolved model/template bindings for import inspection; cannot build a runtime bundle")
    args = parser.parse_args()
    try:
        prepare(args.source_root, args.project, args.config, args.managed_dir, args.sources_only)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(2, f"Blocked: {error}\n")


if __name__ == "__main__":
    main()

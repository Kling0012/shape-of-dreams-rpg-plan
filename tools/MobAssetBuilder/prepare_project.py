#!/usr/bin/env python3
"""Create an isolated, version-pinned Unity project. Never touches a live Mods folder.

Two configuration shapes are supported:

- sources build (``--sources-only``): no game inputs. ``sdkAssemblies`` may be
  empty, ``renderPipelineAsset`` may be blank and every model binding may be
  empty. The Unity project can then build model-only AssetBundles from the
  checked-in FBX sources.
- full build (default): same-game SDK assemblies, an exported URP pipeline
  asset and explicit per-model template bindings are required.

Integrity of local inputs is established by exact recorded versions, filenames
and sizes; hash-based verification is intentionally not used.
"""
import argparse
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
REMOVED_FIELDS = ("contentVersion",)
DIGEST_WORD = "sha"  # rejected lock-field name fragment (digest locks were removed with hash verification)


def exact_version(value, label):
    if (not isinstance(value, str) or len(value) > 128
            or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+_-]*", value)
            or value.lower() in {"unknown", "todo", "replace_me", "latest"}):
        raise ValueError(f"{label} must be an exact recorded version, not empty/unknown/wildcard")
    return value


def safe_relative(value):
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ValueError(f"Unsafe relative path: {value!r}")
    path = Path(value)
    if path.is_absolute() or any(p in {"", ".", ".."} for p in value.split("/")):
        raise ValueError(f"Unsafe relative path: {value!r}")
    return path


def _binding_fields(model):
    return ("baseMonsterTypes", "templatePrefab", "replaceChildPath", "clipBindings", "objectBindings")


def _bindings_empty(model):
    return all(not model.get(field) for field in _binding_fields(model))


def validate_config(config, require_bindings=True):
    if config.get("schemaVersion") != 1:
        raise ValueError("Expected build config schemaVersion 1")
    present = [field for field in REMOVED_FIELDS if field in config]
    if present:
        raise ValueError(f"Config fields no longer used (the manifest identity is its contentVersion): {present}")
    nested = list(config.get("sdkAssemblies", [])) + list(config.get("models", [])) + list(config.get("additionalPackages", []))
    if any(DIGEST_WORD in str(key).lower() for key in config) or any(
            DIGEST_WORD in str(key).lower() for item in nested if isinstance(item, dict) for key in item):
        raise ValueError("Digest-lock config fields are no longer used; record exact versions and sizes instead")
    if not re.fullmatch(r"\d+\.\d+\.\d+[abfp]\d+", config.get("unityVersion", "")):
        raise ValueError("unityVersion must include the exact editor patch/build (for example N.N.NfN)")
    if not re.fullmatch(r"\d+\.\d+\.\d+", config.get("urpVersion", "")):
        raise ValueError("urpVersion must be an exact stable package version")
    exact_version(config.get("gameVersion"), "gameVersion")
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
    assemblies = config.get("sdkAssemblies", [])
    if not isinstance(assemblies, list):
        raise ValueError("sdkAssemblies must be a list of {file} entries")
    names = set()
    for assembly in assemblies:
        name = assembly.get("file", "")
        if not re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", name) or name.lower().startswith(("unity", "system", "mscorlib", "netstandard")):
            raise ValueError(f"Not an allowed game SDK assembly filename: {name}")
        if name in names:
            raise ValueError(f"Duplicate SDK assembly filename: {name}")
        names.add(name)
    pipeline = config.get("renderPipelineAsset", "")
    models = config.get("models", [])
    if len(models) != 10 or {m.get("id") for m in models} != set(MODEL_IDS):
        raise ValueError("All ten distinct Dreamborne model IDs must be configured")
    if require_bindings:
        if "Dew.Core.dll" not in names:
            raise ValueError("The exact game's Dew.Core.dll is required for a full build")
        pipeline = safe_relative(pipeline).as_posix()
        if not pipeline.startswith("Assets/LocalTemplates/") or not pipeline.endswith(".asset"):
            raise ValueError("An exact-game URP pipeline asset under Assets/LocalTemplates is required")
    else:
        if pipeline != "":
            pipeline = safe_relative(pipeline).as_posix()
            if not pipeline.startswith("Assets/LocalTemplates/") or not pipeline.endswith(".asset"):
                raise ValueError("renderPipelineAsset must be blank or an Assets/LocalTemplates/*.asset")
    if not require_bindings:
        for model in models:
            if not _bindings_empty(model):
                raise ValueError(f"{model['id']}: leave all binding fields empty in a sources-only config")
        return config
    for model in models:
        types = model.get("baseMonsterTypes", [])
        if not types or len(types) != len(set(types)) or any(not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.+]*", t) for t in types):
            raise ValueError(f"{model['id']}: provide exact, distinct curated Monster types")
        template = safe_relative(model.get("templatePrefab", "")).as_posix()
        if not template.startswith("Assets/LocalTemplates/") or not template.endswith(".prefab"):
            raise ValueError("Templates must be local Assets/LocalTemplates/*.prefab assets")
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


def prepare(source_root, project, config_file, managed_dir=None, sources_only=False):
    config = validate_config(json.loads(config_file.read_text(encoding="utf-8")), require_bindings=not sources_only)
    if config.get("sdkAssemblies") and managed_dir is None:
        raise ValueError("--managed-dir is required when sdkAssemblies is not empty")
    source_root, project = (p.resolve() for p in (source_root, project))
    repo = Path(__file__).resolve().parents[2]
    if project == repo or repo in project.parents or any(p.lower() == "mods" for p in project.parts):
        raise ValueError("Use a separate empty Unity project outside the repository and every Mods directory")
    if managed_dir is not None:
        managed_dir = managed_dir.resolve()
        if managed_dir == project or managed_dir in project.parents or project in managed_dir.parents:
            raise ValueError("The isolated project must not overlap the game's Managed directory")
    if project.exists() and any(project.iterdir()):
        raise ValueError("Refusing to overwrite a nonempty project; create a fresh isolated folder")
    manifest = json.loads((source_root / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1 or manifest.get("packId") != PACK_ID:
        raise ValueError("Unexpected Dreamborne source manifest")
    if not isinstance(manifest.get("contentVersion"), str) or not manifest["contentVersion"]:
        raise ValueError("Source manifest must declare its contentVersion string")
    if len(manifest.get("models", [])) != 10 or {m["modelId"] for m in manifest["models"]} != set(MODEL_IDS):
        raise ValueError("Source manifest must describe the actual ten models")
    files = {}
    for entry in manifest["files"]:
        relative = safe_relative(entry["path"])
        path = source_root / relative
        if path.is_symlink() or source_root not in path.resolve().parents:
            raise ValueError(f"Source file escapes pack: {relative}")
        if entry["path"] in files or path.stat().st_size != entry["bytes"]:
            raise ValueError(f"Source size mismatch or duplicate: {relative}")
        files[entry["path"]] = path
    needed = set()
    for model in manifest["models"]:
        needed.add(model["fbx"])
        needed.update(model["textures"].values())
    if not needed <= files.keys():
        raise ValueError("Source manifest does not list every FBX/texture")
    sdk_files = {}
    for assembly in config.get("sdkAssemblies", []):
        if managed_dir is None:
            raise ValueError("--managed-dir is required when sdkAssemblies is not empty")
        path = managed_dir / assembly["file"]
        if path.is_symlink() or not path.is_file():
            raise ValueError(f"Game SDK assembly missing: {assembly['file']}")
        sdk_files[assembly["file"]] = path
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
    if sdk_files:
        sdk_dir = project / "Assets/ThirdParty/GameSDK"
        sdk_dir.mkdir(parents=True)
        for name, path in sdk_files.items():
            shutil.copy2(path, sdk_dir / name)
    (project / "Assets/LocalTemplates").mkdir()
    (project / "Packages").mkdir()
    # Built-in modules are required: every generated prefab carries a Generic
    # Animator and skinned rig (animation), and BuildAssetBundles emits a
    # manifest AssetBundle asset (assetbundle) into the output directory.
    packages = {"com.unity.render-pipelines.universal": config["urpVersion"],
                "com.unity.modules.animation": "1.0.0",
                "com.unity.modules.assetbundle": "1.0.0"}
    packages.update({p["name"]: p["version"] for p in config.get("additionalPackages", [])})
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": packages}, indent=2) + "\n", encoding="utf-8")
    (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text(
        f"m_EditorVersion: {config['unityVersion']}\n", encoding="utf-8")
    (project / "mob-build.json").write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    (project / ".gitignore").write_text("Library/\nTemp/\nLogs/\nBuild/\nAssets/ThirdParty/\nAssets/LocalTemplates/\n", encoding="utf-8")
    print(f"Prepared {project}. No Unity import or bundle build has run.")
    if sources_only:
        print("Sources-only project: model-only AssetBundles can be built without game SDK or templates.")
    else:
        print("Import your exact-game model-only SDK templates into Assets/LocalTemplates, then run the documented Unity command.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--managed-dir", type=Path,
                        help="Game Managed folder; required only when sdkAssemblies is not empty")
    parser.add_argument("--sources-only", action="store_true",
                        help="Allow a config without game SDK, pipeline asset and model bindings")
    args = parser.parse_args()
    try:
        prepare(args.source_root, args.project, args.config, args.managed_dir, args.sources_only)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(2, f"Blocked: {error}\n")


if __name__ == "__main__":
    main()

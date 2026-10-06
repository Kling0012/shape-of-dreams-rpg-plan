import contextlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
import zipfile
from copy import deepcopy
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


prepare = load("prepare_mob_project", HERE / "prepare_project.py")
pack = load("package_mob_mod", HERE.parent / "package_mob_mod.py")


def config(require_bindings=True):
    data = {
        "schemaVersion": 1, "unityVersion": "2022.3.1f1", "urpVersion": "14.0.8",
        "gameVersion": "1.0.0-test", "buildTarget": "StandaloneWindows64",
        "renderPipelineAsset": "", "sdkAssemblies": [], "additionalPackages": [],
        "models": [{"id": id_} for id_ in prepare.MODEL_IDS],
    }
    if not require_bindings:
        return data
    data["renderPipelineAsset"] = "Assets/LocalTemplates/RenderPipeline.asset"
    data["sdkAssemblies"] = [{"file": "Dew.Core.dll"}]
    for model in data["models"]:
        model.update(baseMonsterTypes=["Monster_Test"],
                     templatePrefab=f"Assets/LocalTemplates/{model['id']}.prefab",
                     replaceChildPath="Visual", objectBindings=[],
                     clipBindings=[{"semantic": semantic, "componentType": "EntityModel", "componentPath": "",
                                    "propertyPath": path} for semantic, path in
                                   [("Idle", "idle.clip"), ("Walk", "runForwardClip"), ("Attack", "SDKVerifiedAttackSlot")]])
    return data

def runtime():
    return {"schemaVersion": 1, "packId": pack.PACK_ID, "contentVersion": "20261006T000000Z-001",
            "unityVersion": "2022.3.1f1", "gameVersion": "1.0.0-test", "buildTarget": "StandaloneWindows64",
            "bundles": [{"file": pack.bundle_name(id_, "StandaloneWindows64"), "size": 100}
                        for id_ in prepare.MODEL_IDS],
            "models": [{"id": id_, "prefab": f"assets/mobassetbuilder/generated/prefabs/{id_}.prefab",
                        "bundle": pack.bundle_name(id_, "StandaloneWindows64"),
                        "baseMonsterTypes": ["Monster_Test"]} for id_ in prepare.MODEL_IDS]}


class ConfigTests(unittest.TestCase):
    def test_exact_config_accepted(self):
        self.assertIsNotNone(prepare.validate_config(config()))

    def test_sources_only_config_accepted_without_game_inputs(self):
        self.assertIsNotNone(prepare.validate_config(config(require_bindings=False), require_bindings=False))

    def test_unknown_versions_rejected(self):
        for field, value in [("unityVersion", "2022.3"), ("unityVersion", "*"), ("urpVersion", "latest"),
                             ("urpVersion", ""), ("gameVersion", "unknown"), ("buildTarget", "StandaloneWindows64 "),
                             ("buildTarget", "Android")]:
            with self.subTest(field=field, value=value):
                data = config()
                data[field] = value
                with self.assertRaises(ValueError):
                    prepare.validate_config(data)

    def test_removed_digest_and_identity_fields_rejected(self):
        lock_pipeline = "renderPipeline" + prepare.DIGEST_WORD.capitalize() + "256"
        lock_template = "template" + prepare.DIGEST_WORD.capitalize() + "256"
        lock_key = prepare.DIGEST_WORD + "256"
        for mutate in [lambda c: c.update(contentVersion="1"),
                       lambda c: c.update({lock_pipeline: "0" * 64}),
                       lambda c: c["models"][0].update({lock_template: "0" * 64}),
                       lambda c: c["sdkAssemblies"].append({"file": "Dew.Core.dll", lock_key: "0" * 64})]:
            data = config()
            mutate(data)
            with self.assertRaises(ValueError):
                prepare.validate_config(data)

    def test_letter_prefixed_exact_game_version_accepted(self):
        data = config()
        data["gameVersion"] = "r.1.4.0.13_s"
        self.assertIsNotNone(prepare.validate_config(data))

    def test_game_version_is_bounded_token(self):
        for value in [None, "", "TODO", "replace_ME", "LATEST", "Unknown", "r.*", "r.?", "r/1", "r\\1",
                      "r 1", "r.1\n", "a" * 129]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                prepare.exact_version(value, "gameVersion")
        prepare.exact_version("a" * 128, "gameVersion")

    def test_editor_dependency_guard_contract(self):
        # Source contract only: executing Unity's dependency graph requires the real editor/SDK.
        editor = (HERE / "Editor/MobAssetBuilder.cs").read_text(encoding="utf-8")
        self.assertLess(editor.index("ValidateDependencyClosure(config, source, prefabs);"),
                        editor.index("BuildPipeline.BuildAssetBundles"))
        for requirement in ["new HashSet<string>(ProducedAssets", "VerifySourceFile(source, path)",
                            "AssetDatabase.GetDependencies(prefabs.ToArray(), true)",
                            'properties.propertyPath == "m_Script"', "properties.objectReferenceValue is MonoScript",
                            "sdk.Contains(dependency) && scriptReferences.Contains(dependency)",
                            "package.resolvedDependencies", "https://packages.unity.com",
                            "PackageSource.Registry", "Unapproved bundle content dependency",
                            "bundle.LoadAllAssets<TextAsset>().Length == 0",
                            "VerifyBuiltBundle(Path.Combine(output, model.bundle)",
                            "ContentVersion()", "BuildSources"]:
            with self.subTest(requirement=requirement):
                self.assertIn(requirement, editor)
        self.assertNotIn("Cryptography", editor)
        self.assertNotIn(prepare.DIGEST_WORD.capitalize() + "256", editor)
        self.assertNotIn('if (dependency.StartsWith(Generated', editor)
        self.assertNotIn('if (dependency.StartsWith(Source', editor)

    def test_missing_or_duplicate_models_rejected(self):
        for models in [config()["models"][:-1], config()["models"][:-1] + [config()["models"][0]]]:
            data = config()
            data["models"] = models
            with self.assertRaises(ValueError):
                prepare.validate_config(data)

    def test_exact_binding_and_sdk_locks_required(self):
        for mutate in [lambda c: c["models"][0].update(baseMonsterTypes=[]),
                       lambda c: c["models"][0].update(templatePrefab=""),
                       lambda c: c["models"][0].update(clipBindings=c["models"][0]["clipBindings"][:2]),
                       lambda c: c["sdkAssemblies"].clear(),
                       lambda c: c["sdkAssemblies"].append({"file": "UnityEngine.CoreModule.dll"}),
                       lambda c: c.update(renderPipelineAsset="")]:
            data = config()
            mutate(data)
            with self.assertRaises(ValueError):
                prepare.validate_config(data)

    def test_sources_only_must_leave_bindings_empty(self):
        data = config(require_bindings=False)
        data["models"][0].update(baseMonsterTypes=["Monster_Test"])
        with self.assertRaises(ValueError):
            prepare.validate_config(data, require_bindings=False)
        with self.assertRaises(ValueError):
            prepare.validate_config(data)

    def test_attack_cannot_reuse_death_and_packages_are_pinned(self):
        data = config()
        data["models"][0]["clipBindings"][2]["propertyPath"] = "death.clip"
        with self.assertRaises(ValueError):
            prepare.validate_config(data)
        for package in [{"name": "com.unity.inputsystem", "version": "latest"},
                        {"name": "https://example.invalid/repo", "version": "1.0.0"},
                        {"name": "com.unity.render-pipelines.universal", "version": "14.0.9"}]:
            data = config()
            data["additionalPackages"] = [package]
            with self.assertRaises(ValueError):
                prepare.validate_config(data)
        data = config()
        data["additionalPackages"] = [{"name": "com.unity.inputsystem", "version": "1.0.0"}]
        self.assertIsNotNone(prepare.validate_config(data))

    def test_path_traversal_rejected(self):
        for path in ["../secret", "/tmp/secret", "a/../secret", "a\\secret", "C:/secret", "a//secret", "./secret"]:
            with self.subTest(path=path), self.assertRaises(ValueError):
                prepare.safe_relative(path)

    def test_prepare_isolated_project_checks_sources(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root = Path(temp)
            source, project = root / "source", root / "project"
            source.mkdir()
            data = config(require_bindings=False)
            config_path = root / "config.json"
            config_path.write_text(json.dumps(data), encoding="utf-8")
            models, files = [], []
            for id_ in prepare.MODEL_IDS:
                (source / f"{id_}.fbx").write_bytes(b"fixture-fbx")
                (source / f"{id_}.png").write_bytes(b"fixture-texture")
                models.append({"modelId": id_, "fbx": f"{id_}.fbx", "textures": {"baseColor": f"{id_}.png"}})
                for suffix in ["fbx", "png"]:
                    path = source / f"{id_}.{suffix}"
                    files.append({"path": path.name, "bytes": path.stat().st_size})
            manifest = {"schemaVersion": 1, "packId": prepare.PACK_ID, "contentVersion": "20261006T000000Z-001",
                        "models": models, "files": files}
            (source / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                prepare.prepare(source, project, config_path, None, True)
            self.assertTrue((project / "Assets/MobAssetBuilder/Editor/MobAssetBuilder.cs").is_file())
            self.assertFalse((project / "Assets/ThirdParty").exists())
            self.assertEqual({"com.unity.render-pipelines.universal": "14.0.8",
                              "com.unity.modules.animation": "1.0.0",
                              "com.unity.modules.assetbundle": "1.0.0"},
                             json.loads((project / "Packages/manifest.json").read_text(encoding="utf-8"))["dependencies"])
            self.assertFalse((project / "Build").exists())
            with self.assertRaises(ValueError):
                prepare.prepare(source, project, config_path)
            (source / f"{prepare.MODEL_IDS[0]}.fbx").write_bytes(b"changed")
            with self.assertRaises(ValueError):
                prepare.prepare(source, root / "second-project", config_path)
            self.assertFalse((root / "second-project").exists())

    def test_prepare_full_config_requires_managed_dir(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            config_path = root / "config.json"
            config_path.write_text(json.dumps(config()), encoding="utf-8")
            with self.assertRaises(ValueError):
                prepare.prepare(source, root / "project", config_path)


class PackageTests(unittest.TestCase):
    def test_manifest_fail_closed(self):
        for field, value in [("unityVersion", "2022.3.2f1"), ("gameVersion", "1.0.1"),
                             ("buildTarget", "StandaloneLinux64"), ("contentVersion", 1),
                             ("contentVersion", ""), ("schemaVersion", True), ("packId", "other_pack")]:
            data = runtime()
            data[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                pack.validate_manifest(data, "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")

    def test_manifest_bundle_entries_fail_closed(self):
        for mutate in [lambda d: d.__setitem__("bundles", d["bundles"][:-1]),
                       lambda d: d["bundles"][0].__setitem__("size", 0),
                       lambda d: d["bundles"][0].__setitem__("size", "123"),
                       lambda d: d["bundles"][0].__setitem__("file", "../Dew.Core.dll"),
                       lambda d: d["models"][0].__setitem__("bundle", d["models"][1]["bundle"]),
                       lambda d: d["models"][0].__setitem__("bundle", "renamed.bundle"),
                       lambda d: d["models"][0].__setitem__("prefab", "assets/other.prefab"),
                       lambda d: d["models"][0]["baseMonsterTypes"].__setitem__(0, "bad type!")]:
            data = runtime()
            mutate(data)
            with self.subTest(mutate=mutate.__code__.co_firstlineno), self.assertRaises(ValueError):
                pack.validate_manifest(data, "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")

    def test_package_accepts_letter_prefixed_version_and_rejects_unsafe_tokens(self):
        data = runtime()
        data["gameVersion"] = "r.1.4.0.13_s"
        self.assertIsNotNone(pack.validate_manifest(data, "2022.3.1f1", data["gameVersion"], "StandaloneWindows64"))
        for value in ["TODO", "unknown", "replace_me", "latest", "r.*", "r/?", "r\\1", "r.1\n", "a" * 129]:
            data["gameVersion"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                pack.validate_manifest(data, "2022.3.1f1", value, "StandaloneWindows64")

    def test_content_version_stamped_fresh_every_rebuild(self):
        first = pack.content_version()
        second = pack.content_version()
        self.assertNotEqual(first, second)
        self.assertRegex(first, r"^\d{8}T\d{6}Z-\d{3}$")

    def test_duplicate_json_keys_rejected(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            path = Path(temp) / "manifest.json"
            path.write_text('{"schemaVersion":1,"schemaVersion":2}')
            with self.assertRaises(ValueError):
                pack.load_json(path)

    def fixture(self, temp):
        root = Path(temp)
        dll = root / "DreamforgeRPG.dll"
        dll.write_bytes(b"MZfixture-not-a-production-assembly")
        about, models = root / "about", root / "models"
        about.mkdir()
        models.mkdir()
        (about / "metadata.json").write_text('{"name":"test"}')
        (about / "description.txt").write_text("Test fixture")
        for name in ("icon.png", "preview.png"):
            (about / name).write_bytes(b"\x89PNG\r\n\x1a\nfixture")
        data = runtime()
        for entry in data["bundles"]:
            bundle = models / entry["file"]
            bundle.write_bytes(b"UnityFS\x00fixture-not-a-production-bundle")
            entry["size"] = bundle.stat().st_size
        (models / "manifest.json").write_text(json.dumps(data), encoding="utf-8")
        return root, dll, about, models, data

    def test_package_allowlist_is_deterministic_and_no_game_dll(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root, dll, about, models, data = self.fixture(temp)
            (models / "Dew.Core.dll").write_bytes(b"must never ship")
            (about / "secret.txt").write_text("must never ship")
            paths = []
            for number in [1, 2]:
                output = root / f"release-{number}.zip"
                with contextlib.redirect_stdout(io.StringIO()):
                    pack.package(dll, about, models, output, "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")
                paths.append(output)
            with zipfile.ZipFile(paths[0]) as first, zipfile.ZipFile(paths[1]) as second:
                self.assertEqual(first.namelist(), second.namelist())
                self.assertEqual(len(first.namelist()), 16)
                self.assertNotIn("Dew.Core.dll", str(first.namelist()))
                self.assertNotIn("secret.txt", str(first.namelist()))
                manifest_one = json.loads(first.read("DreamforgeRPG/models/manifest.json"))
                manifest_two = json.loads(second.read("DreamforgeRPG/models/manifest.json"))
                self.assertNotEqual(manifest_one["contentVersion"], manifest_two["contentVersion"])
                for entry in data["bundles"]:
                    self.assertEqual(first.read("DreamforgeRPG/models/" + entry["file"]),
                                     (models / entry["file"]).read_bytes())
            with self.assertRaises(ValueError):
                pack.package(dll, about, models, paths[0], "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")

    def test_output_dir_contains_the_same_reviewed_tree(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root, dll, about, models, data = self.fixture(temp)
            output, outdir = root / "release.zip", root / "review-tree"
            with contextlib.redirect_stdout(io.StringIO()):
                pack.package(dll, about, models, output, "2022.3.1f1", "1.0.0-test", "StandaloneWindows64", outdir)
            unpacked = sorted(p.relative_to(outdir).as_posix() for p in outdir.rglob("*") if p.is_file())
            with zipfile.ZipFile(output) as archive:
                self.assertEqual(unpacked, sorted(archive.namelist()))
                self.assertEqual(archive.read("DreamforgeRPG/models/manifest.json"),
                                 (outdir / "DreamforgeRPG/models/manifest.json").read_bytes())
            with self.assertRaises(ValueError):
                pack.package(dll, about, models, root / "release2.zip", "2022.3.1f1", "1.0.0-test",
                             "StandaloneWindows64", outdir)

    def test_tampered_bundle_rejected(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root, dll, about, models, data = self.fixture(temp)
            bundle = models / data["bundles"][0]["file"]
            bundle.write_bytes(b"UnityFS\x00tampered")
            with self.assertRaises(ValueError):
                pack.package(dll, about, models, root / "release.zip", "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")
            self.assertFalse((root / "release.zip").exists())

    def test_wrong_dll_symlink_and_live_deploy_rejected(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            root, dll, about, models, data = self.fixture(temp)
            other = root / "Dew.Core.dll"
            other.write_bytes(b"MZtest")
            for source, output in [(other, root / "release.zip"), (dll, root / "Mods/release.zip")]:
                with self.assertRaises(ValueError):
                    pack.package(source, about, models, output, "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")
            (about / "description.txt").unlink()
            (about / "description.txt").symlink_to(dll)
            with self.assertRaises(ValueError):
                pack.package(dll, about, models, root / "release.zip", "2022.3.1f1", "1.0.0-test", "StandaloneWindows64")


if __name__ == "__main__":
    unittest.main()

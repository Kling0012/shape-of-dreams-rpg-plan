"""Offline regression tests; run with python -m unittest discover -s tests/assets."""
import copy
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("validate_mob_assets", ROOT / "tools/validate_mob_assets.py")
assets = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(assets)
PACK = ROOT / "assets/dreamborne"
MANIFEST = json.loads((PACK / "manifest.json").read_text(encoding="utf-8"))


def encode_glb(document, binary):
    payload = json.dumps(document, separators=(",", ":")).encode("utf-8")
    payload += b" " * (-len(payload) % 4)
    binary += bytes(-len(binary) % 4)
    body = (struct.pack("<II", len(payload), 0x4E4F534A) + payload +
            struct.pack("<II", len(binary), 0x004E4942) + binary)
    return struct.pack("<4sII", b"glTF", 2, len(body) + 12) + body


class PackTests(unittest.TestCase):
    def test_complete_delivery(self):
        result = assets.validate_pack(PACK)
        for key, value in assets.EXPECTED_TOTALS.items():
            self.assertEqual(value, result[key])
        self.assertEqual(63, result["files"])
        self.assertFalse(result["runtimeVerified"])

    def test_relative_paths_cannot_escape_pack(self):
        for path in ("../secret.fbx", "/tmp/secret.fbx", "a/../../secret", "a\\secret"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                assets.safe_file(PACK, path)

    def test_missing_file_fails(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            (root / "manifest.json").write_text(json.dumps(MANIFEST), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "missing asset"):
                assets.validate_pack(root)

    def test_duplicate_model_id_fails(self):
        manifest = copy.deepcopy(MANIFEST)
        manifest["models"][1]["modelId"] = manifest["models"][0]["modelId"]
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate models"):
                assets.validate_pack(root)

    def test_tampered_size_fails(self):
        manifest = copy.deepcopy(MANIFEST)
        manifest["files"][0]["bytes"] += 1
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            entry = manifest["files"][0]
            dest = root / entry["path"]
            dest.parent.mkdir(parents=True)
            dest.write_bytes((PACK / entry["path"]).read_bytes())
            (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "file size mismatch"):
                assets.validate_pack(root)

    def test_sanitization_is_idempotent(self):
        for entry in MANIFEST["files"]:
            if entry["sanitized"]:
                path = PACK / entry["path"]
                with self.subTest(path=entry["path"]):
                    self.assertEqual(path.read_bytes(), assets.sanitized_source(path.read_bytes(), path.suffix))

    def test_blend_paths_cleaned_without_changing_length_or_adjacent_bytes(self):
        path = b"/workspace/private/dream_mob_collection/a/Textures/BaseColor.png"
        original = b"BLENDER-v403\0" + path + b"\0\x01\x02\x03"
        result = assets.sanitize_blend(original)
        self.assertEqual(len(original), len(result))
        self.assertEqual(b"\0\x01\x02\x03", result[-4:])
        self.assertIn(b"//Textures/BaseColor.png\0", result)
        self.assertNotIn(b"/workspace/", result)

    def test_truncated_fbx_fails(self):
        data = (PACK / MANIFEST["models"][0]["fbx"]).read_bytes()
        with self.assertRaises(ValueError):
            assets.fbx_tree(data[:100])

    def test_fbx_takes_equal_manifest(self):
        def take_names(nodes):
            values = []
            for name, properties, children, _ in nodes:
                if name == b"Take":
                    values += [p[5:].decode("utf-8") for p in properties if p[:1] == b"S"]
                values += take_names(children)
            return values
        for model in MANIFEST["models"]:
            nodes, _ = assets.fbx_tree((PACK / model["fbx"]).read_bytes())
            self.assertEqual({c["takeName"] for c in model["clips"]}, set(take_names(nodes)))

    def test_corrupt_png_crc_fails(self):
        data = bytearray((PACK / MANIFEST["models"][0]["textures"]["baseColor"]).read_bytes())
        data[20] ^= 1
        with self.assertRaisesRegex(ValueError, "CRC"):
            assets.inspect_png(data)


class GlbTests(unittest.TestCase):
    def setUp(self):
        self.model = copy.deepcopy(MANIFEST["models"][0])
        self.data = (PACK / self.model["glb"]).read_bytes()
        self.doc, self.binary = assets.read_glb(self.data)

    def inspect(self):
        return assets.inspect_glb(encode_glb(self.doc, self.binary), self.model)

    def test_bad_header_fails(self):
        with self.assertRaisesRegex(ValueError, "header"):
            assets.read_glb(b"NOPE" + self.data[4:])

    def test_missing_skin_fails(self):
        self.doc["skins"] = []
        with self.assertRaisesRegex(ValueError, "one skin"):
            self.inspect()

    def test_missing_weights_fails(self):
        del self.doc["meshes"][0]["primitives"][0]["attributes"]["WEIGHTS_0"]
        with self.assertRaisesRegex(ValueError, "skinning attributes"):
            self.inspect()

    def test_missing_animation_fails(self):
        self.doc["animations"].pop()
        with self.assertRaisesRegex(ValueError, "animation names/count"):
            self.inspect()

    def test_wrong_triangle_count_fails(self):
        self.model["triangles"] += 1
        with self.assertRaisesRegex(ValueError, "triangle count"):
            self.inspect()

    def test_out_of_bounds_accessor_fails(self):
        self.doc["accessors"][0]["count"] += 100000
        with self.assertRaisesRegex(ValueError, "exceeds bufferView"):
            self.inspect()

    def test_bad_bone_index_fails(self):
        index = self.doc["meshes"][0]["primitives"][0]["attributes"]["JOINTS_0"]
        acc = self.doc["accessors"][index]
        view = self.doc["bufferViews"][acc["bufferView"]]
        offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        binary = bytearray(self.binary)
        struct.pack_into("<H" if acc["componentType"] == 5123 else "<B", binary, offset, 255)
        self.binary = bytes(binary)
        with self.assertRaisesRegex(ValueError, "out-of-range skin joint"):
            self.inspect()

    def test_non_finite_weight_fails(self):
        index = self.doc["meshes"][0]["primitives"][0]["attributes"]["WEIGHTS_0"]
        acc = self.doc["accessors"][index]
        view = self.doc["bufferViews"][acc["bufferView"]]
        offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        binary = bytearray(self.binary)
        self.assertEqual(5126, acc["componentType"])
        struct.pack_into("<f", binary, offset, float("nan"))
        self.binary = bytes(binary)
        with self.assertRaisesRegex(ValueError, "non-finite"):
            self.inspect()

    def test_animation_duration_fails(self):
        self.model["clips"][0]["durationSeconds"] += 1
        with self.assertRaisesRegex(ValueError, "duration mismatch"):
            self.inspect()

    def test_external_buffer_fails(self):
        self.doc["buffers"][0]["uri"] = "external.bin"
        with self.assertRaisesRegex(ValueError, "self-contained"):
            self.inspect()


if __name__ == "__main__":
    unittest.main()

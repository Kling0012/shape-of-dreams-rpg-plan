"""Forge generation freshness and input safety; run with unittest discovery."""

import contextlib
import copy
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("forge_generator", ROOT / "tools" / "balance" / "gen_cs.py")
forge = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(forge)


class ForgeBalanceTests(unittest.TestCase):
    def test_stale_check_fails_without_overwrite_and_regeneration_repairs_it(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "Forge.Generated.cs"
            stale = b"stale generated source\n"
            output.write_bytes(stale)
            with patch.object(forge, "OUTPUT_PATH", output):
                with patch.object(sys, "argv", ["gen_cs.py", "--check"]), contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(1, forge.main())
                self.assertEqual(stale, output.read_bytes())
                self.assertTrue(forge.generate())
                self.assertTrue(forge.generate(check=True))

    def test_raw_input_rejects_unknown_schema_types_and_unsafe_coefficients(self):
        original = forge.load_forge()
        invalid = []
        for name, value in (("currentLevelOffset", True), ("currentLevelOffset", -1),
                            ("currentLevelOffset", 21), ("percentPerLevel", -1),
                            ("percentPerLevel", 1.5), ("percentPerLevel", 1 << 31),
                            ("maximumPercent", 101), ("maximumPercent", -1)):
            data = copy.deepcopy(original)
            data["enhanceFailure"][name] = value
            invalid.append(data)
        for version in (True, 2):
            data = copy.deepcopy(original)
            data["schemaVersion"] = version
            invalid.append(data)
        data = copy.deepcopy(original)
        data["enhanceFailure"]["unknown"] = 1
        invalid.append(data)
        data = copy.deepcopy(original)
        del data["enhanceFailure"]["maximumPercent"]
        invalid.append(data)
        data = copy.deepcopy(original)
        data["unknown"] = 1
        invalid.append(data)
        # Check the negative endpoint too: the multiplication precedes clamping.
        for offset in (0, 20):
            data = copy.deepcopy(original)
            data["enhanceFailure"]["currentLevelOffset"] = offset
            data["enhanceFailure"]["percentPerLevel"] = forge.INT_MAX // 20 + 1
            invalid.append(data)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "forge.json"
            for data in invalid:
                with self.subTest(data=data):
                    path.write_text(json.dumps(data), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        forge.load_forge(path)
            path.write_text('{"schemaVersion":1,"schemaVersion":1,"enhanceFailure":{}}', encoding="utf-8")
            with self.assertRaises(ValueError):
                forge.load_forge(path)


if __name__ == "__main__":
    unittest.main()

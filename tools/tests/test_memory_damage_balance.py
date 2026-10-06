"""MemoryDamage resolver boundaries and adopted-content identity."""
import copy
from decimal import Decimal
import json
import runpy
import sys
import unittest
from pathlib import Path
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "balance"))
import star_values


class MemoryDamageBalanceTests(unittest.TestCase):
    def test_decimal_multiplier_is_applied_once_and_shared_outer_ignores_hero(self):
        table = {"multipliers": {"byKind": {"MemoryDamage": Decimal("1.10")},
                                "byHero": {"Hero_Yubar": {"MemoryDamage": Decimal("1.20")}}},
                 "effects": {"yubar.damage/value": 4, "outer.damage/value": 4}}
        private = {"hero": "Hero_Yubar", "stars": [
            {"id": "yubar.damage", "kind": "MemoryDamage", "valueRef": "yubar.damage/value"}]}
        shared = {"hero": "shared", "stars": [
            {"id": "outer.damage", "kind": "MemoryDamage", "valueRef": "outer.damage/value"}]}
        resolved, values = star_values.resolve_manifest("yubar", private, table)
        self.assertEqual(Decimal("5.28"), values["yubar.damage/value"])
        _, outer_values = star_values.resolve_manifest("outer", shared, table)
        self.assertEqual(Decimal("4.4"), outer_values["outer.damage/value"])
        again, _ = star_values.resolve_manifest("yubar", private, table)
        self.assertEqual(resolved, again)
        self.assertIn("valueRef", private["stars"][0])

    def test_unrepresentable_memory_amount_and_mixed_reference_report_the_effect_path(self):
        key = "yubar.damage/value"
        table = {"multipliers": {}, "effects": {key: Decimal("1.0001")}}
        manifest = {"hero": "Hero_Yubar", "stars": [
            {"id": "yubar.damage", "kind": "MemoryDamage", "valueRef": key}]}
        with self.assertRaisesRegex(ValueError, key):
            star_values.resolve_manifest("yubar", manifest, table)
        table["effects"][key] = 1
        manifest["stars"][0]["value"] = 1
        with self.assertRaisesRegex(ValueError, "valueRef only"):
            star_values.resolve_manifest("yubar", manifest, table)

    def test_content_identity_uses_effective_values_not_multiplier_shape_or_replaced_legacy(self):
        original = star_values.load_table()
        manifests = star_values.load_manifests()
        resolved, values = star_values.resolve_all(original, manifests)
        identity = star_values.fingerprint_record(resolved, values)
        changed = copy.deepcopy(original)
        changed["multipliers"]["byKind"] = {"MemoryDamage": 2}
        for key in changed["effects"]:
            changed["effects"][key] = Decimal(changed["effects"][key]) / 2
        compensated, effective = star_values.resolve_all(changed, manifests)
        self.assertEqual(identity, star_values.fingerprint_record(compensated, effective))
        migrated = next(key for key in values if key.endswith("/link/value") and any(
            row["id"] == key[:-len("/link/value")] and row["region"] == "migration"
            for data in manifests.values() for row in data["stars"]))
        replaced_values = dict(values)
        replaced_values[migrated] += 1
        self.assertEqual(identity, star_values.fingerprint_record(resolved, replaced_values))
        adopted = "yubar.mem.exotic-matter.c1.e1/value"
        effective_values = dict(values)
        effective_values[adopted] += Decimal("0.001")
        self.assertNotEqual(identity, star_values.fingerprint_record(resolved, effective_values))

    def test_efficiency_comparison_keeps_decimal_values_across_success_baselines(self):
        runner = runpy.run_path(str(ROOT / "tools" / "balance" / "run"))
        original = Decimal("2.360000000000000000000000001")
        current_value = Decimal("2.596")
        previous = {"createdAt": "before", "validation": {"testsPassed": True}, "snapshots": [
            {"mode": "star-efficiency", "modelVersion": 1, "conditions": {},
             "contentFingerprint": "before", "metrics": [
                 {"id": "efficiency", "label": "fixture", "status": "measured",
                  "unit": "percent/point", "value": original}]}]}
        current = copy.deepcopy(previous)
        current["snapshots"][0]["metrics"][0]["value"] = current_value
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "last-success.json"
            runner["atomic_json"](path, previous)
            persisted = json.loads(path.read_text(encoding="utf-8"), parse_float=Decimal)
            report = runner["comparison"](current, persisted)
        row = next(line for line in report.splitlines() if line.startswith("| fixture |"))
        values = [Decimal(cell.strip().split()[0].rstrip("%")) for cell in row.split("|")[2:6]]
        difference = current_value - original
        self.assertEqual([current_value, original, difference, difference / original * 100], values)


if __name__ == "__main__":
    unittest.main()

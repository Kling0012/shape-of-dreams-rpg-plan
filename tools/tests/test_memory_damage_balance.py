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

    def test_growth_references_reject_literals_missing_values_and_wrong_paths(self):
        table = star_values.load_table()
        growth_table = star_values.load_growth_table()
        manifest = copy.deepcopy(star_values.load_manifests()["husk"])
        entry = next(row for row in manifest["stars"] if row["kind"] == "RunGrowth")
        key = entry["growth"]["threshold"]["valueRef"]
        for invalid in (1, {"valueRef": key + "-wrong"}, {"valueRef": key, "value": 1}):
            entry["growth"]["threshold"] = invalid
            with self.subTest(invalid=invalid), self.assertRaisesRegex(ValueError, "canonical valueRef"):
                star_values.resolve_manifest("husk", manifest, table, growth_table)
        entry["growth"]["threshold"] = {"valueRef": key}
        del growth_table["effects"][key]
        with self.assertRaisesRegex(ValueError, "missing runGrowth reference"):
            star_values.resolve_manifest("husk", manifest, table, growth_table)

    def test_content_identity_uses_effective_values_not_multiplier_shape_or_replaced_legacy(self):
        original = star_values.load_table()
        manifests = star_values.load_manifests()
        resolved, values = star_values.resolve_all(original, manifests)
        identity = star_values.fingerprint_record(resolved, values)
        changed = copy.deepcopy(original)
        changed["multipliers"]["byKind"] = {"MemoryDamage": 2}
        damage_keys = {key for key in changed["effects"]
                       if key.endswith("/link/value") and not key.startswith("legacy/")}
        for data in resolved.values():
            for star in data["stars"]:
                for prefix, effect in [("", star)] + [
                        (f"options/{i}/", option) for i, option in enumerate(star.get("options") or [])]:
                    if effect.get("kind") == "MemoryDamage":
                        damage_keys.add(f"{star['id']}/{prefix}value")
        for key in damage_keys:
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
        values = [Decimal(cell.strip().split()[0].rstrip("%")) for cell in row.split("|")[3:7]]
        difference = current_value - original
        self.assertEqual([current_value, original, difference, difference / original * 100], values)

    def test_stage2_choice_quantities_use_child_kind_once_without_scaling_structural_args(self):
        table = {"multipliers": {
            "byKind": {"GimmickBoost": Decimal("1.25"), "Notable": 2},
            "byHero": {"Hero_Yubar": {"GimmickBoost": 2, "Notable": 3}}},
            "effects": {"choice/options/0/value": 4, "choice/options/1/gimmick/value": 2,
                        "choice/options/1/gimmick/cooldown": Decimal("0.5")}}
        raw = {"hero": "Hero_Yubar", "stars": [{"id": "choice", "kind": "Choice", "options": [
            {"kind": "GimmickBoost", "valueRef": "choice/options/0/value"},
            {"kind": "Notable", "gimmick": {
                "effect": "Element", "arg": 3,
                "value": {"valueRef": "choice/options/1/gimmick/value"},
                "cooldown": {"valueRef": "choice/options/1/gimmick/cooldown"}}}]}]}
        private, _ = star_values.resolve_manifest("yubar", raw, table)
        shared, _ = star_values.resolve_manifest("outer", raw, table)
        self.assertEqual(10, private["stars"][0]["options"][0]["value"])
        self.assertEqual(5, shared["stars"][0]["options"][0]["value"])
        private_gimmick = private["stars"][0]["options"][1]["gimmick"]
        shared_gimmick = shared["stars"][0]["options"][1]["gimmick"]
        self.assertEqual((12, 3, 3), (private_gimmick["value"], private_gimmick["cooldown"],
                                     private_gimmick["arg"]))
        self.assertEqual((4, 1, 3), (shared_gimmick["value"], shared_gimmick["cooldown"],
                                    shared_gimmick["arg"]))
        self.assertIn("valueRef", raw["stars"][0]["options"][0])

    def test_stage2_integer_and_fixed_precision_quantities_are_never_silently_rounded(self):
        cases = [
            ("MemoryHaste", None, Decimal("1.1")),
            ("GimmickParam", "ExtraTargets", Decimal("0.5")),
            ("GimmickBoost", None, Decimal("0.001")),
        ]
        for kind, param, amount in cases:
            with self.subTest(kind=kind, param=param):
                raw = {"hero": "Hero_Yubar", "stars": [
                    {"id": "quantity", "kind": kind, "param": param, "valueRef": "quantity/value"}]}
                table = {"multipliers": {}, "effects": {"quantity/value": amount}}
                with self.assertRaisesRegex(ValueError, "quantity/value"):
                    star_values.resolve_manifest("yubar", raw, table)

    def test_all_kinds_preserve_content_identity_when_base_and_multiplier_changes_cancel(self):
        original = star_values.load_table()
        manifests = star_values.load_manifests()
        resolved, values = star_values.resolve_all(original, manifests)
        changed = copy.deepcopy(original)
        by_kind = changed["multipliers"].setdefault("byKind", {})
        for kind in star_values.KINDS:
            by_kind[kind] = Decimal(by_kind.get(kind, 1)) * 2
        for key in changed["effects"]:
            changed["effects"][key] = Decimal(changed["effects"][key]) / 2
        compensated, effective = star_values.resolve_all(changed, manifests)
        self.assertEqual(star_values.fingerprint_record(resolved, values),
                         star_values.fingerprint_record(compensated, effective))
        raw = manifests["yubar"]
        boost = next(star for star in raw["stars"] if star.get("kind") == "GimmickBoost")
        changed = copy.deepcopy(original)
        changed["effects"][boost["valueRef"]] += 1
        adjusted, effective = star_values.resolve_all(changed, manifests)
        self.assertNotEqual(star_values.fingerprint_record(resolved, values),
                            star_values.fingerprint_record(adjusted, effective))


if __name__ == "__main__":
    unittest.main()

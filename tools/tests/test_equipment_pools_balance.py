"""Equipment coverage, integer safety and derived band rounding contracts."""
import copy
from decimal import Decimal
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/balance"))
import equipment_pools_values as equipment
from equipment_common import fingerprint_records, semantic_identity

spec = importlib.util.spec_from_file_location("equipment_named_tests", ROOT / "tools/lowrarity/gen_named_cs.py")
named = importlib.util.module_from_spec(spec)
spec.loader.exec_module(named)


class EquipmentPoolsBalanceTests(unittest.TestCase):
    def test_missing_and_unknown_rows_or_fields_are_rejected(self):
        for domain, load in (("affixes", equipment.load_affixes), ("power-pools", equipment.load_power_pools)):
            for mutation in ("missing-slot", "missing-row", "unknown-row", "unknown-field"):
                with self.subTest(domain=domain, mutation=mutation):
                    table = load()
                    rows = table["pools"]["Weapon"]
                    key = next(iter(rows))
                    if mutation == "missing-slot":
                        del table["pools"]["Feet"]
                    elif mutation == "missing-row":
                        del rows[key]
                    elif mutation == "unknown-row":
                        rows["NotAnEnum"] = rows[key]
                    else:
                        rows[key]["extra"] = 1
                    with self.assertRaises(ValueError):
                        equipment.validate_pool(table, domain, domain)
        caps = equipment.load_caps()
        del caps["stat"]["AttackPct"]
        with self.assertRaises(ValueError):
            equipment.validate_caps(caps, "caps")

    def test_integer_order_and_weight_overflow_boundaries_are_rejected(self):
        for field, value in (("min", -1), ("max", 2147483648), ("min", True),
                             ("min", Decimal("1.5")), ("weight", 0),
                             ("weight", 2147483647)):
            with self.subTest(field=field, value=value):
                table = equipment.load_affixes()
                table["pools"]["Weapon"]["AttackFlat"][field] = value
                with self.assertRaises(ValueError):
                    equipment.validate_pool(table, "affixes", "affixes")
        inverted = equipment.load_power_pools()
        inverted["pools"]["Weapon"]["Momentum"] = {"min": 1, "max": 0}
        with self.assertRaises(ValueError):
            equipment.validate_pool(inverted, "power-pools", "power-pools")
        caps = equipment.load_caps()
        caps["power"]["Momentum"] = -1
        with self.assertRaises(ValueError):
            equipment.validate_caps(caps, "caps")

    def test_duplicate_keys_are_rejected_by_canonical_load(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "caps.json"
            path.write_text('{"schemaVersion":1,"schemaVersion":1}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                equipment.load_caps(path)

    def test_semantic_cap_changes_are_order_independent_and_typed(self):
        original = equipment.load_caps()
        changed = copy.deepcopy(original)
        changed["power"]["Momentum"] = (changed["power"]["Momentum"] + 1) % 2147483648
        original_records = equipment.cap_records(original)
        changed_records = equipment.cap_records(changed)
        self.assertNotEqual(semantic_identity(original_records), semantic_identity(changed_records))
        legacy = semantic_identity(original_records)
        self.assertEqual([], fingerprint_records("caps", original_records, legacy))
        records = fingerprint_records("caps", changed_records, legacy)
        self.assertEqual(records, fingerprint_records("caps", list(reversed(changed_records)), legacy))
        self.assertIn(f'balance:equipment:caps:v1:power/Momentum/cap:int32:value={changed["power"]["Momentum"]}', records)

    def test_named_band_rounding_handles_halfway_and_int32_edges(self):
        self.assertEqual(1, named.band_value(0, 3, "low"))
        self.assertEqual(7, named.band_value(5, 10, "mid"))
        self.assertEqual(18, named.band_value(5, 25, "high"))
        self.assertEqual(2147483647, named.band_value(2147483647, 2147483647, "high"))
        self.assertEqual(1395864371, named.band_value(0, 2147483647, "high"))


if __name__ == "__main__":
    unittest.main()

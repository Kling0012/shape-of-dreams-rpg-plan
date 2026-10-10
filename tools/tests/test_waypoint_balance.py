"""Keep frozen waypoint compatibility separate from current authored validation."""
import copy
from decimal import Decimal
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "balance"))
import waypoint_values as waypoints


class WaypointBalanceTests(unittest.TestCase):
    def test_legacy_snapshot_is_compatible_and_historical_changes_are_fingerprinted(self):
        legacy = copy.deepcopy(waypoints.LEGACY)
        self.assertIsNone(waypoints.fingerprint_record(legacy))
        legacy["definitions"]["WeaponRoad"]["luck"] = Decimal("1.0000")
        self.assertIsNone(waypoints.fingerprint_record(legacy))
        legacy["definitions"]["WeaponRoad"]["luck"] = Decimal("2.0")
        self.assertEqual("balance:waypoints:v1:definitions/WeaponRoad/luck:double:luck:2.0",
                         waypoints.fingerprint_record(legacy))

    def test_current_definition_is_fingerprinted_even_at_its_default_values(self):
        current = copy.deepcopy(waypoints.SCHEMA)
        record = waypoints.fingerprint_record(current)
        self.assertIn("definitions/DeepeningNight/pressureMultiplier:double:multiplier:1.5", record)
        self.assertIn("definitions/DeepeningNight/awakeningMultiplier:double:multiplier:3.0", record)
        current["definitions"]["DeepeningNight"]["pressureMultiplier"] = Decimal("1.5000")
        self.assertEqual(record, waypoints.fingerprint_record(current))
        current["definitions"]["DeepeningNight"]["pressureMultiplier"] = Decimal("2.0")
        self.assertNotEqual(record, waypoints.fingerprint_record(current))

    def test_authored_input_cannot_omit_new_definition(self):
        historical = copy.deepcopy(waypoints.LEGACY)
        for operation in (waypoints.validate, waypoints.render_outputs):
            with self.subTest(operation=operation.__name__), self.assertRaisesRegex(ValueError, "DeepeningNight"):
                operation(historical)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "waypoints.json"
            path.write_text(json.dumps(historical, default=float), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "DeepeningNight"):
                waypoints.load(path)

    def test_both_fingerprint_schemas_reject_invalid_numbers_and_shapes(self):
        for reference in (waypoints.LEGACY, waypoints.SCHEMA):
            for value in (True, -1, Decimal("NaN"), Decimal("Infinity"), Decimal("1e-400"),
                          Decimal("1.0000000000000000001")):
                data = copy.deepcopy(reference)
                data["definitions"]["WeaponRoad"]["luck"] = value
                with self.subTest(current=reference is waypoints.SCHEMA, value=value), self.assertRaises(ValueError):
                    waypoints.fingerprint_record(data)
            for mutate in (
                lambda d: d["definitions"].pop("WeaponRoad"),
                lambda d: d["definitions"]["WeaponRoad"].update(unknown=1),
                lambda d: d.update(schemaVersion=True),
                lambda d: d.update(offered=25),
                lambda d: d["rewards"].update(twinRelicCopies=257),
            ):
                data = copy.deepcopy(reference)
                mutate(data)
                with self.subTest(current=reference is waypoints.SCHEMA, mutate=mutate), self.assertRaises(ValueError):
                    waypoints.fingerprint_record(data)
        current = copy.deepcopy(waypoints.SCHEMA)
        del current["definitions"]["DeepeningNight"]["pressureMultiplier"]
        with self.assertRaisesRegex(ValueError, "DeepeningNight"):
            waypoints.fingerprint_record(current)

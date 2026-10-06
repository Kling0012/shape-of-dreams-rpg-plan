"""Star progression fingerprint compatibility and generation-time arithmetic safety."""
import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "balance"))
import star_progression_values as progression


class StarProgressionBalanceTests(unittest.TestCase):
    def test_original_compatibility_has_no_extra_fingerprint_record_and_tuning_is_canonical(self):
        original = copy.deepcopy(progression.LEGACY)
        self.assertIsNone(progression.fingerprint_record(original))
        changed = copy.deepcopy(original)
        changed["pointCost"]["perPoint"] = 7
        changed["keystone"]["unlockLevels"] = [201, 401]
        self.assertEqual(
            "balance:star-progression:v1:pointCost/perPoint:int:xp/point:7;keystone/unlockLevels:int[]:points:[201,401]",
            progression.fingerprint_record(changed),
        )
        reordered = {key: changed[key] for key in reversed(changed)}
        self.assertEqual(progression.fingerprint_record(changed), progression.fingerprint_record(reordered))
        for path in (path for path in progression.FIELDS if path != "keystone.maxSlots"):
            with self.subTest(path=path):
                data = copy.deepcopy(original)
                section, _, key = path.rpartition(".")
                target = data[section] if section else data
                if key == "unlockLevels":
                    target[key][0] += 1
                elif key == "maxPoints":
                    target[key] -= 1
                else:
                    target[key] += 1
                self.assertIsNotNone(progression.fingerprint_record(data))

    def test_rejects_overflow_order_and_capacity_violations_before_publication(self):
        for section, key, value in (
            (None, "maxPoints", 501),
            (None, "maxPoints", True),
            ("pointCost", "perPoint", progression.INT_MAX),
            ("pointCost", "offset", progression.INT_MAX // 500 + 1),
            ("rewards", "nightmareMultiplier", progression.INT_MAX),
            ("rewards", "normalKillXp", 6),
            ("keystone", "maxSlots", 4),
            ("keystone", "maxSlots", 2),
            ("keystone", "unlockLevels", [200, 200]),
            ("keystone", "unlockLevels", [200, 501]),
        ):
            with self.subTest(section=section, key=key, value=value):
                data = copy.deepcopy(progression.LEGACY)
                (data[section] if section else data)[key] = value
                with self.assertRaises(ValueError):
                    progression.render_outputs(data)
        boundary = copy.deepcopy(progression.LEGACY)
        boundary["maxPoints"] = 2
        boundary["keystone"] = {"maxSlots": 3, "unlockLevels": [1, 2]}
        boundary["pointCost"] = {"perPoint": 0, "offset": progression.INT_MAX // 2}
        self.assertEqual(boundary, progression.validate(boundary))
        boundary["pointCost"]["offset"] += 1
        with self.assertRaises(ValueError):
            progression.validate(boundary)

"""Stage6 compatibility and effective-number fingerprint boundaries."""
import copy
from decimal import Decimal
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "balance"))
import stage6_values


class Stage6BalanceTests(unittest.TestCase):
    def test_original_content_is_compatible_and_numeric_spelling_is_not_content(self):
        for name, module in stage6_values.DOMAINS.items():
            if name == "bossSets":
                continue  # Existing boss-drop fingerprint covers this table.
            with self.subTest(domain=name):
                self.assertIsNone(module.fingerprint_record(copy.deepcopy(module.LEGACY)))
        module = stage6_values.DOMAINS["events"]
        changed = copy.deepcopy(module.LEGACY)
        changed["relicWager"]["rareWinChance"] = Decimal("0.25")
        same = dict(reversed(list(changed.items())))
        same["relicWager"] = {"lowerWinChance": Decimal("0.5000"), "rareWinChance": Decimal("0.2500")}
        self.assertNotEqual(module.fingerprint_record(module.LEGACY), module.fingerprint_record(changed))
        self.assertEqual(module.fingerprint_record(changed), module.fingerprint_record(same))

    def test_unrepresentable_probability_and_boolean_count_are_rejected(self):
        module = stage6_values.DOMAINS["loot"]
        changed = copy.deepcopy(module.LEGACY)
        changed["dropChance"]["normal"] = Decimal("0.027000000000000000001")
        with self.assertRaisesRegex(ValueError, "normal"):
            module.render_outputs(changed)
        module = stage6_values.DOMAINS["events"]
        changed = copy.deepcopy(module.LEGACY)
        changed["cauldron"]["relicCount"] = True
        with self.assertRaisesRegex(ValueError, "relicCount"):
            module.render_outputs(changed)

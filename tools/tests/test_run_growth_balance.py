"""Growth cutover fingerprint compatibility and reference validation."""
import copy
import json
import re
import sys
import unittest
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/balance"))
import star_values

# Captured before the stage4 cutover; independent inputs, not current-table expectations.
ORIGINAL_GROWTH = json.loads(r'''{
  "schemaVersion": 1,
  "effects": {
    "cetus.run.choice/options/0/growth/capBonus": 0,
    "cetus.run.choice/options/0/growth/effectPct": 50,
    "cetus.run.choice/options/1/growth/capBonus": 0,
    "cetus.run.choice/options/1/growth/effectPct": 0,
    "cetus.run.g1/growth/cap": 60,
    "cetus.run.g1/growth/effects/0/amount": 0.5,
    "cetus.run.g1/growth/threshold": 9,
    "cetus.run.m1/growth/capBonus": 20,
    "cetus.run.m1/growth/effectPct": 0,
    "cetus.run.m2/growth/capBonus": 20,
    "cetus.run.m2/growth/effectPct": 0,
    "husk.run.choice/options/0/growth/capBonus": 0,
    "husk.run.choice/options/0/growth/effectPct": 58,
    "husk.run.choice/options/1/growth/capBonus": 0,
    "husk.run.choice/options/1/growth/effectPct": 0,
    "husk.run.g1/growth/cap": 45,
    "husk.run.g1/growth/effects/0/amount": 1.38,
    "husk.run.g1/growth/threshold": 1,
    "husk.run.m1/growth/capBonus": 12,
    "husk.run.m1/growth/effectPct": 0,
    "husk.run.m2/growth/capBonus": 12,
    "husk.run.m2/growth/effectPct": 0,
    "mist.run.choice/options/0/growth/capBonus": 0,
    "mist.run.choice/options/0/growth/effectPct": 50,
    "mist.run.choice/options/1/growth/capBonus": 0,
    "mist.run.choice/options/1/growth/effectPct": 0,
    "mist.run.g1/growth/cap": 60,
    "mist.run.g1/growth/effects/0/amount": 1,
    "mist.run.g1/growth/effects/1/amount": 0.3,
    "mist.run.g1/growth/threshold": 1,
    "mist.run.m1/growth/capBonus": 20,
    "mist.run.m1/growth/effectPct": 0,
    "mist.run.m2/growth/capBonus": 20,
    "mist.run.m2/growth/effectPct": 0,
    "vesper.run.choice/options/0/growth/capBonus": 0,
    "vesper.run.choice/options/0/growth/effectPct": 50,
    "vesper.run.choice/options/1/growth/capBonus": 0,
    "vesper.run.choice/options/1/growth/effectPct": 0,
    "vesper.run.g1/growth/cap": 60,
    "vesper.run.g1/growth/effects/0/amount": 1,
    "vesper.run.g1/growth/effects/1/amount": 4,
    "vesper.run.g1/growth/threshold": 11,
    "vesper.run.m1/growth/capBonus": 20,
    "vesper.run.m1/growth/effectPct": 0,
    "vesper.run.m2/growth/capBonus": 20,
    "vesper.run.m2/growth/effectPct": 0
  }
}''', parse_float=Decimal)

# Exact pre-stage4 semantic record, captured from the original compiled payload.
ORIGINAL_RANK_RECORD = (
    "balance:stars:rank:v1|gain:1.5|denominator:500|maxPoints:504|growth:"
    "cetus.run.choice/options/0/growth/capBonus:milli:0|"
    "cetus.run.choice/options/1/growth/capBonus:milli:0|"
    "cetus.run.g1/growth/cap:milli:60000|"
    "cetus.run.g1/growth/effects/0/amount:milli:500|"
    "cetus.run.g1/growth/threshold:milli:9000|"
    "cetus.run.m1/growth/capBonus:milli:20000|"
    "cetus.run.m2/growth/capBonus:milli:20000|"
    "husk.run.choice/options/0/growth/capBonus:milli:0|"
    "husk.run.choice/options/1/growth/capBonus:milli:0|"
    "husk.run.g1/growth/cap:milli:45000|"
    "husk.run.g1/growth/effects/0/amount:milli:1380|"
    "husk.run.g1/growth/threshold:milli:1000|"
    "husk.run.m1/growth/capBonus:milli:12000|"
    "husk.run.m2/growth/capBonus:milli:12000|"
    "mist.run.choice/options/0/growth/capBonus:milli:0|"
    "mist.run.choice/options/1/growth/capBonus:milli:0|"
    "mist.run.g1/growth/cap:milli:60000|"
    "mist.run.g1/growth/effects/0/amount:milli:1000|"
    "mist.run.g1/growth/effects/1/amount:milli:300|"
    "mist.run.g1/growth/threshold:milli:1000|"
    "mist.run.m1/growth/capBonus:milli:20000|"
    "mist.run.m2/growth/capBonus:milli:20000|"
    "vesper.run.choice/options/0/growth/capBonus:milli:0|"
    "vesper.run.choice/options/1/growth/capBonus:milli:0|"
    "vesper.run.g1/growth/cap:milli:60000|"
    "vesper.run.g1/growth/effects/0/amount:milli:1000|"
    "vesper.run.g1/growth/effects/1/amount:milli:4000|"
    "vesper.run.g1/growth/threshold:milli:11000|"
    "vesper.run.m1/growth/capBonus:milli:20000|"
    "vesper.run.m2/growth/capBonus:milli:20000")


def fingerprint_record(growth):
    table = star_values.load_table()
    table["rankScaling"] = {"damageGain": Decimal("1.5"), "pointDenominator": 500, "maxPoints": 504}
    return star_values.rank_fingerprint_record(table, star_values.validate_growth_table(growth))


class RunGrowthBalanceTests(unittest.TestCase):
    def test_original_rank_record_is_byte_identical_and_parent_or_choice_percent_changes_identity(self):
        record = fingerprint_record(ORIGINAL_GROWTH)
        self.assertEqual(ORIGINAL_RANK_RECORD, record)
        for key in ("husk.run.m1/growth/effectPct", "husk.run.choice/options/0/growth/effectPct"):
            changed = copy.deepcopy(ORIGINAL_GROWTH)
            changed["effects"][key] += 1
            with self.subTest(key=key):
                self.assertNotEqual(record, fingerprint_record(changed))
                self.assertNotEqual(star_values.content_identity(record),
                                    star_values.content_identity(fingerprint_record(changed)))

    def test_invalid_missing_extra_and_mixed_growth_references_are_rejected(self):
        table = star_values.load_table()
        manifests = star_values.load_manifests()
        # Live manifests need the current complete table, not the frozen fingerprint fixture.
        growth = star_values.load_growth_table()
        # First prove the unmodified fixture resolves, so unrelated failures cannot mask a rejection.
        star_values.resolve_all(table, manifests, growth_table=growth)
        for key in ("husk.run.m1/growth/effectPct", "husk.run.choice/options/0/growth/effectPct"):
            for invalid in (True, "50", -1, 501, Decimal("0.1"), Decimal("NaN")):
                changed = copy.deepcopy(growth)
                changed["effects"][key] = invalid
                with self.subTest(key=key, invalid=invalid), self.assertRaisesRegex(ValueError, re.escape(key)):
                    star_values.resolve_all(table, manifests, growth_table=changed)
            changed = copy.deepcopy(growth)
            del changed["effects"][key]
            with self.subTest(missing=key), self.assertRaisesRegex(ValueError, "missing runGrowth reference " + re.escape(key)):
                star_values.resolve_all(table, manifests, growth_table=changed)
        changed = copy.deepcopy(growth)
        changed["effects"]["unused/growth/capBonus"] = 1
        with self.assertRaisesRegex(ValueError, "unused/growth/capBonus: extra unreferenced effect"):
            star_values.resolve_all(table, manifests, growth_table=changed)
        raw = copy.deepcopy(manifests["husk"])
        choice = next(row for row in raw["stars"] if row["id"] == "husk.run.choice")
        key = "husk.run.choice/options/0/growth/effectPct"
        for invalid in (58, {"valueRef": key, "value": 58}, {"valueRef": key + "-wrong"}):
            choice["options"][0]["growth"]["effectPct"] = invalid
            with self.subTest(reference=invalid), self.assertRaisesRegex(ValueError, "canonical valueRef " + re.escape(key)):
                star_values.resolve_manifest("husk", raw, table, growth)


if __name__ == "__main__":
    unittest.main()

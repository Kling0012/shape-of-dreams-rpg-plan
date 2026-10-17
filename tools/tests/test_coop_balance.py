"""#700: coop.json の担当外の減衰（世界ごと）の検証。"""
import copy
from decimal import Decimal
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "balance"))
import coop_values as coop
from star_values import read_json


class CoopBalanceTests(unittest.TestCase):
    def setUp(self):
        self.data = read_json(coop.PATH)

    def render(self, decay):
        data = copy.deepcopy(self.data)
        data["ownership"]["helpDecayPercentByWorld"] = decay
        return coop.render_outputs(data)[coop.OUTPUT]

    def test_authored_list_renders_the_stage_switch(self):
        self.assertEqual([0, 30, 60, 90], self.data["ownership"]["helpDecayPercentByWorld"])
        text = self.render([0, 30, 60, 90])
        self.assertIn("public const int HelpDecayWorlds = 4;", text)
        self.assertIn("case 2: return 60;", text)
        self.assertIn("default: return 90;", text)
        self.assertIn("default: return 50;", self.render([50]))

    def test_invalid_lists_are_rejected(self):
        cases = {
            "empty": ([], "non-empty"),
            "not a list": (90, "non-empty"),
            "negative": ([0, -1], r"\[1\].*0 to 100"),
            "above 100": ([0, 101], r"\[1\].*0 to 100"),
            "decimal": ([0, Decimal("30.5")], r"\[1\].*integer"),
            "boolean": ([True], r"\[0\].*integer"),
            "decreasing": ([0, 60, 30], r"\[2\].*non-decreasing"),
        }
        for name, (decay, message) in cases.items():
            with self.subTest(name), self.assertRaisesRegex(ValueError, "helpDecayPercentByWorld" + ".*" + message):
                self.render(decay)

    def test_old_world_factor_keys_are_rejected(self):
        data = copy.deepcopy(self.data)
        data["ownership"]["helpFactor"] = Decimal("0.5")
        with self.assertRaisesRegex(ValueError, "coop.ownership: invalid schema"):
            coop.render_outputs(data)
        del data["ownership"]["helpFactor"]
        del data["ownership"]["helpDecayPercentByWorld"]
        with self.assertRaisesRegex(ValueError, "coop.ownership: invalid schema"):
            coop.render_outputs(data)


if __name__ == "__main__":
    unittest.main()

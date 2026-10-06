"""Unreached arrival zones remain states, not invented zero measurements."""
import copy
from importlib.machinery import SourceFileLoader
import importlib.util
from pathlib import Path
import unittest

RUN = Path(__file__).resolve().parents[1] / "balance" / "run"
SPEC = importlib.util.spec_from_loader("balance_run", SourceFileLoader("balance_run", str(RUN)))
balance = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(balance)


class BalanceComparisonTests(unittest.TestCase):
    def test_arrival_transition_has_no_numeric_or_relative_difference(self):
        previous = {
            "createdAt": "before", "validation": {"testsPassed": True},
            "snapshots": [{"mode": "v132stars", "modelVersion": 1, "conditions": {"zones": 4},
                           "contentFingerprint": "old", "metrics": [
                               {"id": "growth/capZone", "label": "arrival", "unit": "zones",
                                "value": None, "status": "not-reached"}]}],
        }
        current = copy.deepcopy(previous)
        current["createdAt"] = "after"
        current["snapshots"][0]["contentFingerprint"] = "new"
        current["snapshots"][0]["metrics"][0].update(value=4, status="measured")
        def cells(report):
            return [cell.strip() for cell in next(
                line for line in report.splitlines() if line.startswith("| arrival |")).split("|")[1:-1]]
        row = cells(balance.comparison(current, previous))
        self.assertEqual("4", row[2])
        self.assertIn("→ 4", row[4])
        self.assertEqual("—", row[5])
        reverse = cells(balance.comparison(previous, current))
        self.assertIn("4 →", reverse[4])
        self.assertEqual("—", reverse[5])
        current["snapshots"][0]["conditions"]["zones"] = 5
        self.assertEqual(["—", "—"], cells(balance.comparison(current, previous))[4:])

    def test_equipment_type_change_is_not_a_numeric_delta(self):
        previous = {
            "createdAt": "before", "validation": {"testsPassed": True},
            "snapshots": [{"mode": "equipment", "modelVersion": 1, "conditions": {},
                           "contentFingerprint": "old", "metrics": [
                               {"id": "equipment/link", "label": "link", "type": "int32",
                                "unit": "percent", "value": 4, "status": "measured"}]}],
        }
        current = copy.deepcopy(previous)
        current["snapshots"][0]["metrics"][0].update(type="decimal", value=4.5)
        row = next(line for line in balance.comparison(current, previous).splitlines()
                   if "| percent |" in line)
        cells = [cell.strip() for cell in row.split("|")[1:-1]]
        self.assertEqual(["4.5", "4", "—", "—"], cells[2:])

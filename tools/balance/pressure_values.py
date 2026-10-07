"""Compile pressure/depth coefficients; legacy values are comparison-only, never defaults."""
from decimal import Decimal
import json
from pathlib import Path

from star_values import number, read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/pressure.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Pressure.Generated.cs"
# Frozen cutover reference, not an editable source or missing-cell fallback.
LEGACY = {
    "schemaVersion": 1,
    "dreamPressure": {
        "freeDreamLevels": 5,
        "healthPerLevel": Decimal("0.025"), "healthPerStarPoint": Decimal("0.005"),
        "healthPerInfinityStage": Decimal("0.10"), "damagePerLevel": Decimal("0.012"),
        "damagePerStarPoint": Decimal("0.0025"), "damagePerInfinityStage": Decimal("0.04"),
        "enemyCountHealthPerStage": Decimal("0.10"),
        "enemyCountPerStage": Decimal("0"),
        "enemyCountMaximumBonus": Decimal("0.60"),
        "enemyCountAdditionalRewardBudget": Decimal("0"),
        "shardDropPerPressure": Decimal("0"), "shardDropMaximum": Decimal("1"),
        "nightmareChancePerPressure": Decimal("0"), "nightmareChanceMaximum": Decimal("1"),
    },
    "dreamDepth": {
        "maximum": 5, "healthPerDepth": Decimal("0.15"), "damagePerDepth": Decimal("0.08"),
        "rarityLuckPerDepth": Decimal("0.25"), "awakeningPerDepth": Decimal("0.25"),
        "starXpPerDepth": Decimal("0.2"), "extraNodesPerDepth": 2,
    },
}
FIELDS = {
    "dreamPressure": {
        "freeDreamLevels": ("FreeDreamLevels", "int", "levels", 1, 30),
        "healthPerLevel": ("HealthPerLevel", "double", "ratio/level", 0, 100),
        "healthPerStarPoint": ("HealthPerStarPoint", "double", "ratio/point", 0, 100),
        "healthPerInfinityStage": ("HealthPerInfinityStage", "double", "ratio/stage", 0, 100),
        "damagePerLevel": ("DamagePerLevel", "double", "ratio/level", 0, 100),
        "damagePerStarPoint": ("DamagePerStarPoint", "double", "ratio/point", 0, 100),
        "damagePerInfinityStage": ("DamagePerInfinityStage", "double", "ratio/stage", 0, 100),
        "enemyCountHealthPerStage": ("EnemyCountHealthPerStage", "double", "health-ratio/stage", 0.000001, 100),
        "enemyCountPerStage": ("EnemyCountPerStage", "double", "ratio/stage", 0, 1),
        "enemyCountMaximumBonus": ("EnemyCountMaximumBonus", "double", "ratio", 0, 1),
        "enemyCountAdditionalRewardBudget": ("EnemyCountAdditionalRewardBudget", "double", "ratio", 0, 1),
        "shardDropPerPressure": ("ShardDropPerPressure", "double", "multiplier/hp-bonus", 0, 10),
        "shardDropMaximum": ("ShardDropMaximum", "double", "multiplier", 1, 10),
        "nightmareChancePerPressure": ("NightmareChancePerPressure", "double", "multiplier/hp-bonus", 0, 10),
        "nightmareChanceMaximum": ("NightmareChanceMaximum", "double", "multiplier", 1, 10),
    },
    "dreamDepth": {
        "maximum": ("MaximumDepth", "int", "depth", 1, 5),
        "healthPerDepth": ("HealthPerDepth", "double", "ratio/depth", 0, 100),
        "damagePerDepth": ("DamagePerDepth", "double", "ratio/depth", 0, 100),
        "rarityLuckPerDepth": ("RarityLuckPerDepth", "double", "luck/depth", 0, 100),
        "awakeningPerDepth": ("AwakeningPerDepth", "double", "ratio/depth", 0, 100),
        "starXpPerDepth": ("StarXpPerDepth", "double", "ratio/depth", 0, 100),
        "extraNodesPerDepth": ("ExtraNodesPerDepth", "int", "nodes/depth", 0, 100),
    },
}


def validate(data):
    if type(data) is not dict or set(data) != set(LEGACY):
        raise ValueError("pressure: expected exactly schemaVersion, dreamPressure, dreamDepth")
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1:
        raise ValueError("pressure.schemaVersion: expected integer 1")
    for section, fields in FIELDS.items():
        if type(data[section]) is not dict or set(data[section]) != set(fields):
            raise ValueError(f"pressure.{section}: expected exactly {', '.join(fields)}")
        for key, (_, kind, _, low, high) in fields.items():
            path = f"pressure.{section}.{key}"
            value = number(data[section][key], path)
            if kind == "int" and (type(data[section][key]) is not int):
                raise ValueError(f"{path}: expected Int32 integer (not boolean)")
            if not low <= value <= high:
                raise ValueError(f"{path}: expected {low}..{high}")
            # No silent decimal quantization; double is the existing runtime carrier.
            if kind == "double" and value.normalize().as_tuple().exponent < -6:
                raise ValueError(f"{path}: expected at most six decimal places")
    return data


def load(path=PATH):
    return validate(read_json(path))


def canonical(value):
    return format(Decimal(value).normalize(), "f")


def fingerprint_record(data):
    validate(data)
    records = []
    for section, fields in FIELDS.items():
        for key, (_, kind, unit, _, _) in fields.items():
            value = data[section][key]
            if value != LEGACY[section][key]:
                records.append(f"{section}/{key}:{kind}:{unit}:{canonical(value)}")
    return "balance:pressure:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/pressure.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class PressureBalance", "    {"]
    for section, fields in FIELDS.items():
        for key, (name, kind, _, _, _) in fields.items():
            suffix = "d" if kind == "double" else ""
            lines.append(f"        internal const {kind} {name} = {canonical(data[section][key])}{suffix};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " +
                 (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}

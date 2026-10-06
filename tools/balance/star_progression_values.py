"""Compile star XP and keystone balance without publishing generated files."""
import json
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/star-progression.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/StarProgression.Generated.cs"
INT_MAX = (1 << 31) - 1
# Frozen compatibility reference, not an editable balance source or fallback.
LEGACY = {
    "schemaVersion": 1,
    "maxPoints": 500,
    "pointCost": {"perPoint": 6, "offset": 50},
    "rewards": {"secureXp": 20, "victoryXp": 100, "normalKillXp": 1,
                "miniBossKillXp": 5, "bossKillXp": 20, "nightmareMultiplier": 2},
    "keystone": {"maxSlots": 3, "unlockLevels": [200, 400]},
}
FIELDS = {
    "maxPoints": ("MaxPoints", "points"),
    "pointCost.perPoint": ("PointCostPerPoint", "xp/point"),
    "pointCost.offset": ("PointCostOffset", "xp"),
    "rewards.secureXp": ("SecureXp", "xp"),
    "rewards.victoryXp": ("VictoryXp", "xp"),
    "rewards.normalKillXp": ("NormalKillXp", "xp"),
    "rewards.miniBossKillXp": ("MiniBossKillXp", "xp"),
    "rewards.bossKillXp": ("BossKillXp", "xp"),
    "rewards.nightmareMultiplier": ("NightmareMultiplier", "multiplier"),
    "keystone.maxSlots": ("MaxKeystoneSlots", "slots"),
    "keystone.unlockLevels": ("KeystoneUnlockLevels", "points"),
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="star-progression"):
    if isinstance(reference, dict):
        if type(data) is not dict or set(data) != set(reference):
            raise ValueError(f"{path}: expected exactly {', '.join(reference)}")
        for key in reference:
            _shape(data[key], reference[key], f"{path}.{key}")
    elif isinstance(reference, list):
        if type(data) is not list:
            raise ValueError(f"{path}: expected an integer array")
        for index, value in enumerate(data):
            _shape(value, 0, f"{path}[{index}]")
    elif type(data) is not int or not 0 <= data <= INT_MAX:
        raise ValueError(f"{path}: expected nonnegative Int32 (not boolean)")


def validate(data):
    _shape(data, LEGACY)
    if data["schemaVersion"] != 1:
        raise ValueError("star-progression.schemaVersion: expected integer 1")
    maximum = data["maxPoints"]
    if maximum < 1 or maximum > 500:
        raise ValueError("star-progression.maxPoints: expected 1..500 (existing point/wire capacity)")
    cost = data["pointCost"]
    if cost["perPoint"] + cost["offset"] < 1:
        raise ValueError("star-progression.pointCost: every point must cost positive XP")
    # Python integers validate the whole cumulative curve before C# publication.
    # Positivity makes its largest cost/total occur at maxPoints.
    if cost["perPoint"] * maximum + cost["offset"] > INT_MAX:
        raise ValueError("star-progression.pointCost: maximum point cost exceeds Int32")
    total = cost["perPoint"] * maximum * (maximum + 1) // 2 + cost["offset"] * maximum
    if total > INT_MAX:
        raise ValueError("star-progression.pointCost: cumulative XP exceeds Int32")
    reward = data["rewards"]
    if reward["nightmareMultiplier"] < 1:
        raise ValueError("star-progression.rewards.nightmareMultiplier: expected positive multiplier")
    if not 0 <= reward["normalKillXp"] <= reward["miniBossKillXp"] <= reward["bossKillXp"]:
        raise ValueError("star-progression.rewards: kill XP must be ordered normal <= miniBoss <= boss")
    if reward["bossKillXp"] * reward["nightmareMultiplier"] > INT_MAX:
        raise ValueError("star-progression.rewards: nightmare kill XP exceeds Int32")
    keystone = data["keystone"]
    # Three slots is the existing serialized save/wire shape, not a tunable capacity.
    if keystone["maxSlots"] != 3:
        raise ValueError("star-progression.keystone.maxSlots: expected 3 (fixed save/protocol capacity)")
    levels = keystone["unlockLevels"]
    if len(levels) != keystone["maxSlots"] - 1:
        raise ValueError("star-progression.keystone.unlockLevels: expected maxSlots - 1 entries")
    previous = 0
    for level in levels:
        if not previous < level <= maximum:
            raise ValueError("star-progression.keystone.unlockLevels: strictly increasing positive levels <= maxPoints required")
        previous = level
    return data


def load(path=PATH):
    return validate(read_json(path))


def fingerprint_record(data):
    validate(data)
    records = []
    for path, (_, unit) in FIELDS.items():
        value, old = _get(data, path), _get(LEGACY, path)
        if value != old:
            if isinstance(value, list):
                # Include the complete adopted array, including removed entries.
                literal = ",".join(map(str, value))
                records.append(f"{path.replace('.', '/')}:int[]:{unit}:[{literal}]")
            else:
                records.append(f"{path.replace('.', '/')}:int:{unit}:{value}")
    return "balance:star-progression:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/star-progression.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class StarProgressionBalance", "    {"]
    for path, (name, _) in FIELDS.items():
        value = _get(data, path)
        if isinstance(value, list):
            lines.append(f"        internal static readonly int[] {name} = {{ " + ", ".join(map(str, value)) + " };")
        else:
            lines.append(f"        internal const int {name} = {value};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}

"""Validate boss behavior tables and render typed C# without publishing files.

The tables under tools/balance/bosses/<boss>.json are the sole numeric source for
BossProfiles.*.cs payloads (channels, actions, reward actions and shared helper
constants).  The runtime never reads JSON; it consumes the generated constants.
"""
import hashlib
import json
import re
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
TABLE_DIR = ROOT / "tools" / "balance" / "bosses"
OUTPUT_DIR = ROOT / "src" / "SodRpg.Core" / "Game" / "Balance"

BOSS_ORDER = ("demon", "skoll", "infernus", "ink", "nyx", "erebos", "seeker",
              "azurak", "primus", "light", "maw", "obliviax", "polaris")

# BossAction constructor bounds (src/SodRpg.Core/Game/BossProfiles.cs).
ACTION_FIELDS = {
    "cooldownMillis": (0, 2147483647), "delayMillis": (0, 2147483647),
    "lifetimeMillis": (0, 2147483647), "intervalMillis": (0, 2147483647),
    "count": (1, 64), "maxTargets": (1, 64), "maxInstances": (1, 4),
    "mainHits": (0, 6), "counterLifetimeMillis": (0, 2147483647),
    "generationLimit": (0, 1), "radiusMilli": (0, 2147483647),
    "rangeMilli": (0, 2147483647), "widthMilli": (0, 2147483647),
    "speedMilli": (0, 2147483647), "angleMilli": (0, 360000),
    "hitGateMillis": (0, 2147483647), "magnitudeMilli": (0, 2147483647),
    "requiredMode": (-1, 3), "requiredMarks": (0, 3),
}
# BossRewardAction constructor bounds; order stays structural in C# and is not a cell.
REWARD_FIELDS = {
    "valueMilli": (-2147483648, 2147483647), "capMilli": (0, 2147483647),
    "cooldownMillis": (0, 2147483647), "durationMillis": (0, 2147483647),
    "count": (1, 64), "rangeMilli": (0, 2147483647), "magnitudeMilli": (0, 2147483647),
    "intervalMillis": (0, 2147483647), "targetCapMilli": (0, 2147483647),
    "budgetMilli": (0, 2147483647), "dwellMillis": (0, 2147483647),
    "gapToleranceMillis": (0, 2147483647), "speedMilli": (0, 2147483647),
    "widthMilli": (0, 2147483647),
}
IDENT = re.compile(r"^[a-z][A-Za-z0-9]*$")
PROFILE_KEY = re.compile(r"^[a-z][A-Za-z0-9_]*$")
CHANNEL_ID = re.compile(r"^[A-Z][A-Za-z0-9]*$")
# Shared and reward cells whose suffix matches a payload field get that field's bounds;
# these cells are intentionally negative or unconstrained by construction order.
SHARED_NEGATIVE_OK = {"nativeHysteriaSpeedMilli"}
CAP_KEYS = {"cap", "capMilli", "targetCapMilli", "budgetMilli"}


def _int(value, path):
    if type(value) is not int:
        raise ValueError(f"{path}: expected integer (not boolean or decimal)")
    if not -2147483648 <= value <= 2147483647:
        raise ValueError(f"{path}: expected Int32 integer")
    return value


def _check_bound(name, value, path, table):
    for suffix, (low, high) in table.items():
        if name.endswith(suffix) and len(suffix) < len(name) + 1:
            if value < low or value > high:
                raise ValueError(f"{path}: {name}={value} outside {low}..{high}")
            return


def validate(boss, data):
    path = TABLE_DIR / f"{boss}.json"
    if data.get("schemaVersion") != 1:
        raise ValueError(f"{path}: expected schemaVersion 1")
    if set(data) != {"schemaVersion", "shared", "moves", "rewards"}:
        raise ValueError(f"{path}: expected exactly schemaVersion, shared, moves, rewards")
    shared = data["shared"]
    if not isinstance(shared, dict) or not all(IDENT.match(k) for k in shared):
        raise ValueError(f"{path}/shared: expected camelCase cell names")
    for key, value in shared.items():
        _int(value, f"{path}/shared/{key}")
        if value < 0 and key not in SHARED_NEGATIVE_OK:
            raise ValueError(f"{path}/shared/{key}: expected nonnegative")
        _check_bound(key, value, f"{path}/shared/{key}", ACTION_FIELDS)
        _check_bound(key, value, f"{path}/shared/{key}", REWARD_FIELDS)
    moves = data["moves"]
    if not isinstance(moves, dict) or not moves or not all(PROFILE_KEY.match(k) for k in moves):
        raise ValueError(f"{path}/moves: expected nonempty profile keys like weapon or white_weapon")
    for pid, profile in moves.items():
        if set(profile) != {"channels", "actions"}:
            raise ValueError(f"{path}/moves/{pid}: expected exactly channels, actions")
        channels = profile["channels"]
        if not isinstance(channels, dict) or not channels or not all(CHANNEL_ID.match(k) for k in channels):
            raise ValueError(f"{path}/moves/{pid}/channels: expected PascalCase channel ids")
        for cid, cell in channels.items():
            where = f"{path}/moves/{pid}/channels/{cid}"
            if set(cell) not in ({"value"}, {"value", "cap"}):
                raise ValueError(f"{where}: expected value or value+cap")
            value = _int(cell["value"], f"{where}/value")
            if value <= 0:
                raise ValueError(f"{where}/value: expected positive")
            if "cap" in cell:
                cap = _int(cell["cap"], f"{where}/cap")
                if cap < value:
                    raise ValueError(f"{where}: cap {cap} below value {value}")
        actions = profile["actions"]
        if not isinstance(actions, list) or not 1 <= len(actions) <= 16:
            raise ValueError(f"{path}/moves/{pid}/actions: expected 1..16 actions")
        for i, action in enumerate(actions):
            if not isinstance(action, dict):
                raise ValueError(f"{path}/moves/{pid}/actions/{i}: expected an object")
            for field, value in action.items():
                if field not in ACTION_FIELDS:
                    raise ValueError(f"{path}/moves/{pid}/actions/{i}/{field}: unknown BossAction field")
                low, high = ACTION_FIELDS[field]
                _int(value, f"{path}/moves/{pid}/actions/{i}/{field}")
                if not low <= value <= high:
                    raise ValueError(f"{path}/moves/{pid}/actions/{i}/{field}: {value} outside {low}..{high}")
    rewards = data["rewards"]
    if not isinstance(rewards, dict) or not all(IDENT.match(k) for k in rewards):
        raise ValueError(f"{path}/rewards: expected camelCase cell names")
    for key, value in rewards.items():
        _int(value, f"{path}/rewards/{key}")
        _check_bound(key, value, f"{path}/rewards/{key}", REWARD_FIELDS)
    return data


def load(boss):
    return validate(boss, read_json(TABLE_DIR / f"{boss}.json"))


def load_all():
    return {boss: load(boss) for boss in BOSS_ORDER}


def tables_digest(tables):
    """Canonical digest of all tables; detects any cell edit (guard for pinned values)."""
    payload = json.dumps({boss: tables[boss] for boss in BOSS_ORDER},
                         sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


# Frozen migration reference. The initial tables reproduced the compiled values exactly;
# any cell edit moves away from it and legitimately changes the boss content fingerprint.
FROZEN_DIGEST = "5fda4892782c3a651333df23949cb502f6d5abf587aba884352c102ae4e5445c"


def _pascal(name):
    parts = re.split(r"[_.-]", name)
    out = []
    for part in parts:
        if not part:
            raise ValueError(f"empty identifier part in {name}")
        out.append(part[:1].upper() + part[1:])
    return "".join(out)


def _const(camel):
    return camel[:1].upper() + camel[1:]


def _channel_const_names(data):
    """Reproduce the collision-aware channel constant naming used by BossProfiles."""
    assigned = set()
    mapping = {}
    for pid, profile in data["moves"].items():
        for cid in profile["channels"]:
            base = cid
            value_name, cap_name = base + "Value", base + "Cap"
            if value_name in assigned:
                base2 = _pascal(pid) + cid
                value_name, cap_name = base2 + "Value", base2 + "Cap"
            assigned.add(value_name)
            mapping[(pid, cid)] = (value_name, cap_name)
    return mapping


def render_boss(boss, data):
    header = ["// <auto-generated />",
              f"// Source: tools/balance/bosses/{boss}.json; regenerate with python tools/balance/gen_cs.py.",
              "namespace SodRpg.Core.Game", "{",
              f"    internal static class Boss{_pascal(boss)}Balance", "    {"]
    lines = list(header)
    for key, value in data["shared"].items():
        lines.append(f"        internal const int {_const(key)} = {value};")
    channel_names = _channel_const_names(data)
    for pid, profile in data["moves"].items():
        for cid, cell in profile["channels"].items():
            value_name, cap_name = channel_names[(pid, cid)]
            lines.append(f"        internal const int {value_name} = {cell['value']};")
            if "cap" in cell:
                lines.append(f"        internal const int {cap_name} = {cell['cap']};")
        for i, action in enumerate(profile["actions"]):
            for field, value in action.items():
                const = _pascal(pid) + "A" + str(i) + _const(field)
                lines.append(f"        internal const int {const} = {value};")
    for key, value in data["rewards"].items():
        lines.append(f"        internal const int Reward{_const(key)} = {value};")
    lines += ["    }", "}", ""]
    return "\n".join(lines)


def tables_summary(tables=None):
    """Compact metadata for the comparison baseline; full tables stay in the JSON sources."""
    tables = load_all() if tables is None else tables
    summary = {boss: hashlib.sha256(json.dumps(data, sort_keys=True, separators=(",", ":"),
                                               ensure_ascii=True).encode("utf-8")).hexdigest()
               for boss, data in tables.items()}
    summary["tablesDigest"] = tables_digest(tables)
    return summary

def render_outputs(tables=None):
    tables = load_all() if tables is None else tables
    outputs = {}
    for boss, data in tables.items():
        target = OUTPUT_DIR / f"Boss{_pascal(boss)}Balance.Generated.cs"
        outputs[target] = render_boss(boss, data)
    aggregate = ["// <auto-generated />",
                 "// Source: tools/balance/bosses/*.json; regenerate with python tools/balance/gen_cs.py.",
                 "namespace SodRpg.Core.Game", "{",
                 "    internal static class BossBalanceValues", "    {",
                 f"        internal static readonly bool MatchesFrozenReference = {str(tables_digest(tables) == FROZEN_DIGEST).lower()};",
                 "    }", "}", ""]
    outputs[OUTPUT_DIR / "BossBalanceValues.Generated.cs"] = "\n".join(aggregate)
    return outputs

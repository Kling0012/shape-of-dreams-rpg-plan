"""Compile enemy tuning into typed C#; legacy data is a frozen identity comparator only."""
import json
from decimal import Decimal
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/monsters.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Monsters.Generated.cs"
# Frozen pre-table content, never used to fill missing table values.
_LEGACY_JSON = r"""{
  "schemaVersion": 1,
  "behavior": {
    "Range": 6.0,
    "InnerRange": 3.0,
    "AllyRadius": 5.0,
    "GuardReduction": 0.3,
    "MaxGuardReduction": 0.4,
    "BossGuardReduction": 0.2,
    "WarmupSeconds": 2.0,
    "PulseHalfPeriod": 3.0,
    "RecoverySeconds": 1.5,
    "HitSlowSeconds": 2.0,
    "HealDelaySeconds": 4.0,
    "HealPctPerSecond": 1.0,
    "HealBudgetPct": 10.0,
    "ShieldPct": 15.0,
    "ShieldSeconds": 6.0,
    "PhaseOpeningSeconds": 3.0,
    "WeaknessTakenBonus": 0.3,
    "MaxTagResistance": 0.3,
    "TagHunterDealtBonus": 0.5,
    "LightEaterMinStacks": 3,
    "OpeningIncomingMultiplier": 1.2,
    "FacingDot": 0.5,
    "HitMovementPct": -15.0,
    "UnhitMovementPct": 20.0,
    "LastStandHealthRatio": 0.35,
    "FirstPhaseHealthRatio": 0.7,
    "SecondPhaseHealthRatio": 0.4
  },
  "nightmare": {
    "BaseHealthPct": 40,
    "WardShieldPct": 25,
    "ThornsReflectPct": 1.5,
    "ThornsReflectInterval": 0.4,
    "RavenousLeechPct": 15,
    "SunderArmor": 20,
    "SunderSeconds": 4.0,
    "BossDepthMinimum": 4,
    "BossHealthPctPerDepth": 10,
    "HealthPctPerDepth": 8,
    "AttackPctPerDepth": 4,
    "ArmorDepthMinimum": 3,
    "DepthArmor": 10,
    "GearHealthDivisor": 2,
    "GearMaximumChanceBonus": 0.5,
    "GearScoreDivisor": 400.0,
    "LesserChancePerDepth": 0.01,
    "NormalChancePerDepth": 0.02,
    "MiniBossBaseChance": 0.2,
    "MiniBossChancePerDepth": 0.08,
    "TwoAffixDepth": 3,
    "ThreeAffixDepth": 5,
    "BaseAffixCount": 1,
    "TwoAffixCount": 2,
    "ThreeAffixCount": 3,
    "IroncladArmor": 60,
    "BerserkAttackPct": 40,
    "BerserkAttackSpeedPct": 30,
    "ColossalHealthPct": 150,
    "SwiftMoveSpeedPct": 35,
    "SwiftAttackSpeedPct": 20,
    "RegenerationPctPerSecond": 2.0,
    "ArcanePowerPct": 50,
    "ArcaneHaste": 40,
    "WardedArmor": 20,
    "ThornedArmor": 30,
    "RavenousAttackPct": 15,
    "SunderingAttackPct": 10
  },
  "variants": {
    "MinDepth": 2,
    "HitCapPct": 8,
    "DeathBurstPct": 20,
    "DeathBurstRadius": 4.0,
    "BaseChance": 0.06,
    "ChancePerDepth": 0.02,
    "DefaultShardBonusPct": 100,
    "DevourerShardBonusPct": 300,
    "BonusShards": 10
  },
  "variantStats": [
    {
      "id": "var.corroding_hound",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        },
        {
          "stat": "MoveSpeedPct",
          "value": 50
        },
        {
          "stat": "AttackSpeedPct",
          "value": 30
        }
      ]
    },
    {
      "id": "var.mirror_scarab",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 80
        },
        {
          "stat": "Armor",
          "value": 40
        }
      ]
    },
    {
      "id": "var.elder_treant",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 250
        },
        {
          "stat": "Armor",
          "value": 30
        }
      ]
    },
    {
      "id": "var.blood_bat",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 80
        },
        {
          "stat": "AttackSpeedPct",
          "value": 40
        },
        {
          "stat": "MoveSpeedPct",
          "value": 30
        }
      ]
    },
    {
      "id": "var.abyss_oppressor",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 200
        },
        {
          "stat": "AttackPct",
          "value": 30
        }
      ]
    },
    {
      "id": "var.hollow_gunner",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 200
        },
        {
          "stat": "AttackPct",
          "value": 40
        }
      ]
    },
    {
      "id": "var.blazing_martyr",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        },
        {
          "stat": "MoveSpeedPct",
          "value": 40
        }
      ]
    },
    {
      "id": "var.ink_scholar",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 80
        },
        {
          "stat": "AttackPct",
          "value": 25
        }
      ]
    },
    {
      "id": "var.thousand_arrows",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        },
        {
          "stat": "AttackSpeedPct",
          "value": 50
        },
        {
          "stat": "AttackPct",
          "value": 20
        }
      ]
    },
    {
      "id": "var.molten_core",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 120
        }
      ]
    },
    {
      "id": "var.star_stalker",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 80
        },
        {
          "stat": "MoveSpeedPct",
          "value": 40
        },
        {
          "stat": "AttackPct",
          "value": 30
        }
      ]
    },
    {
      "id": "var.frost_alpha",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 150
        },
        {
          "stat": "AttackPct",
          "value": 30
        },
        {
          "stat": "MoveSpeedPct",
          "value": 20
        }
      ]
    },
    {
      "id": "var.shard_devourer",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 200
        },
        {
          "stat": "MoveSpeedPct",
          "value": 30
        }
      ]
    },
    {
      "id": "var.mist_spitter",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.brood_warden",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 70
        }
      ]
    },
    {
      "id": "var.hollow_elemental",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.flicker_olm",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 50
        }
      ]
    },
    {
      "id": "var.timorous_displacer",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.overreaching_bug",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.last_thaw",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.rime_sentinel",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.furnace_ram",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 70
        }
      ]
    },
    {
      "id": "var.ember_mender",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 50
        }
      ]
    },
    {
      "id": "var.mist_tiger",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.lantern_seed",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 40
        }
      ]
    },
    {
      "id": "var.eclipse_demon",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 20
        }
      ]
    },
    {
      "id": "var.rust_scavenger",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.broodfly",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 50
        }
      ]
    },
    {
      "id": "var.stardust_shell",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 60
        }
      ]
    },
    {
      "id": "var.web_ripper",
      "stats": [
        {
          "stat": "MaxHealthPct",
          "value": 50
        }
      ]
    }
  ]
}
"""
LEGACY = json.loads(_LEGACY_JSON, parse_float=Decimal)


def _unit(name):
    if "Chance" in name: return "probability"
    if name.endswith("HealthRatio") or name == "FacingDot": return "ratio"
    if "Multiplier" in name: return "multiplier"
    if name.endswith("Reduction") or name.endswith("Bonus"): return "ratio"
    if "Pct" in name: return "percent"
    if "Seconds" in name or name in ("PulseHalfPeriod", "ThornsReflectInterval"): return "seconds"
    if "Range" in name or "Radius" in name: return "meters"
    if "Depth" in name: return "depth"
    if name == "BonusShards": return "shards"
    if "Count" in name or "Stacks" in name: return "count"
    return "stat" if "Armor" in name or "Haste" in name else "divisor"


FIELDS = {
    f"{group}.{name}": (name, "int" if type(value) is int else "float" if group == "behavior" or name in
                           ("ThornsReflectPct", "ThornsReflectInterval", "SunderSeconds", "RegenerationPctPerSecond", "DeathBurstRadius") else "double", _unit(name))
    for group in ("behavior", "nightmare", "variants")
    for name, value in LEGACY[group].items()
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="monsters"):
    if isinstance(reference, dict):
        if type(data) is not dict or set(data) != set(reference):
            raise ValueError(f"{path}: expected exactly {', '.join(reference)}")
        for key in reference:
            _shape(data[key], reference[key], f"{path}.{key}")
    elif isinstance(reference, list):
        if type(data) is not list or len(data) != len(reference):
            raise ValueError(f"{path}: expected {len(reference)} entries")
        for index, (value, old) in enumerate(zip(data, reference)):
            _shape(value, old, f"{path}[{index}]")
    elif isinstance(reference, str):
        if type(data) is not str or data != reference:
            raise ValueError(f"{path}: fixed definition identity must be {reference!r}")
    elif isinstance(reference, Decimal):
        if type(data) not in (int, Decimal) or not Decimal(data).is_finite() or not -1000000 <= data <= 1000000:
            raise ValueError(f"{path}: expected finite number in -1000000..1000000, not boolean")
        if Decimal(data).as_tuple().exponent < -6:
            raise ValueError(f"{path}: at most six fractional digits")
    elif type(data) is not int or not 0 <= data <= 1000000:
        raise ValueError(f"{path}: expected nonnegative integer <= 1000000, not boolean")


def validate(data):
    _shape(data, LEGACY)
    if data["schemaVersion"] != 1:
        raise ValueError("monsters.schemaVersion: expected 1")
    for path, (name, _, unit) in FIELDS.items():
        value = _get(data, path)
        if unit == "ratio" and not 0 <= value <= 1:
            raise ValueError(f"monsters.{path}: expected ratio in 0..1")
        if value < 0 and name != "HitMovementPct":
            raise ValueError(f"monsters.{path}: expected nonnegative value")
        if unit == "divisor" and value <= 0:
            raise ValueError(f"monsters.{path}: expected positive divisor")
    b, n, v = data["behavior"], data["nightmare"], data["variants"]
    if b["PulseHalfPeriod"] <= 0 or b["OpeningIncomingMultiplier"] < 1 or not -100 <= b["HitMovementPct"] <= 0:
        raise ValueError("monsters.behavior: positive pulse period, opening multiplier >= 1 and hit movement in -100..0 required")
    if not 0 <= b["SecondPhaseHealthRatio"] < b["FirstPhaseHealthRatio"] <= 1:
        raise ValueError("monsters.behavior: strictly ordered health phase ratios required")
    for group, fields in ((n, ("BossDepthMinimum", "ArmorDepthMinimum", "TwoAffixDepth", "ThreeAffixDepth")), (v, ("MinDepth",))):
        if any(not 1 <= group[key] <= 5 for key in fields):
            raise ValueError("monsters: depth thresholds must fit existing depth range 1..5")
    if not n["TwoAffixDepth"] < n["ThreeAffixDepth"]:
        raise ValueError("monsters.nightmare: two-affix depth must precede three-affix depth")
    if not 1 <= n["BaseAffixCount"] <= n["TwoAffixCount"] <= n["ThreeAffixCount"] <= 19:
        raise ValueError("monsters.nightmare: ordered affix counts in 1..19 required (regeneration pair is mutually exclusive)")
    if not 0 <= n["GearMaximumChanceBonus"] <= 1:
        raise ValueError("monsters.nightmare.GearMaximumChanceBonus: expected 0..1")
    chances = (n["LesserChancePerDepth"] * 5, n["NormalChancePerDepth"] * 5,
               n["MiniBossBaseChance"] + n["MiniBossChancePerDepth"] * 4,
               v["BaseChance"] + v["ChancePerDepth"] * (5 - v["MinDepth"]))
    if any(not 0 <= chance <= 1 for chance in chances):
        raise ValueError("monsters: chance curve must remain in 0..1 at every supported depth")
    return data


def load(path=PATH):
    return validate(read_json(path))


def _number(value):
    if type(value) is int: return str(value)
    return format(Decimal(value).normalize(), "f")


def fingerprint_record(data):
    validate(data)
    records = []
    for path, (_, kind, unit) in FIELDS.items():
        value = _get(data, path)
        if value != _get(LEGACY, path):
            records.append(f"{path.replace('.', '/')}:{kind}:{unit}:{_number(value)}")
    for row, old in zip(data["variantStats"], LEGACY["variantStats"]):
        for field, before in zip(row["stats"], old["stats"]):
            if field["value"] != before["value"]:
                records.append(f"variantStats/{row['id']}/{field['stat']}:int:stat:{field['value']}")
    return "balance:monsters:v1:" + ";".join(sorted(records)) if records else None


def stats_method(id_):
    return "Create" + "".join(word.capitalize() for word in id_.removeprefix("var.").split("_")) + "Stats"


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/monsters.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class MonstersBalance", "    {"]
    for path, (name, kind, _) in FIELDS.items():
        literal = _number(_get(data, path))
        if kind != "int":
            if "." not in literal: literal += ".0"
            literal += "f" if kind == "float" else "d"
        lines.append(f"        internal const {kind} {name} = {literal};")
    for row in data["variantStats"]:
        fields = ", ".join(f"new StatLine(Stat.{field['stat']}, {field['value']})" for field in row["stats"])
        lines.append(f"        internal static StatLine[] {stats_method(row['id'])}() => new[] {{ {fields} }};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}

"""Strict forge table compiler. Frozen legacy values only preserve migration identity."""
import json
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/forge.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Forge.Generated.cs"
INT_MAX = (1 << 31) - 1
# Compatibility reference, never a fallback or editable balance source.
_LEGACY_JSON = r"""{
  "schemaVersion": 2,
  "enhanceFailure": {
    "currentLevelOffset": 2,
    "percentPerLevel": 3,
    "maximumPercent": 45,
    "demotionChance": 0.5,
    "demotionSteps": 1
  },
  "enhancement": {
    "baseCap": 5,
    "stepPerBreak": 5,
    "milestones": [3, 5, 10, 15, 20],
    "milestonePowerPercent": 120,
    "statPercents": [100, 106, 111, 116, 121, 126, 129, 132, 135, 138, 140, 142, 144, 146, 148, 150, 152, 153, 154, 155, 156],
    "powerPercents": [100, 104, 108, 112, 116, 120, 123, 126, 128, 130, 132, 134, 136, 138, 139, 140, 141, 142, 143, 144, 145],
    "shardCosts": [20, 35, 60, 90, 130, 180, 230, 290, 360, 440, 270, 345, 435, 540, 660, 360, 460, 580, 720, 880],
    "epicMaterialMultiplier": 2
  },
  "awakening": {
    "thresholds": [0, 5000, 15000, 37500],
    "powerPercents": [100, 125, 150, 180],
    "affixPercents": [100, 110, 120, 130],
    "normalPoints": 1,
    "miniBossPoints": 5,
    "bossPoints": 20,
    "nightmareMultiplier": 2
  },
  "limitBreak": {
    "maxByRarity": [0, 0, 1, 2, 3],
    "shardCosts": [200, 400, 800],
    "tuningCosts": [5, 10, 20]
  },
  "retune": {
    "maximum": 3,
    "choices": 3,
    "baseTuningCost": 1,
    "tuningCostPerRetune": 1
  },
  "affixReroll": {
    "baseShards": 60,
    "baseTuning": 2,
    "growthMultiplier": 1.5
  },
  "craft": {
    "shardCosts": [60, 150],
    "tuningCosts": [0, 2],
    "luck": [0.5, 1.0]
  },
  "synthesis": {
    "inputs": [5, 5, 12, 16],
    "shardCosts": [10, 20, 60, 300],
    "tuningCosts": [0, 0, 0, 4],
    "targetCostPercent": 150
  },
  "salvage": {
    "shards": [3, 6, 12, 30, 60],
    "epicTuning": 1,
    "enhanceRefundDivisor": 2
  },
  "guaranteedEnhancement": {
    "fountainSteps": 1,
    "forgeShrineSteps": 1,
    "forgeShrineShards": 20,
    "temperingAltarSteps": 2
  },
  "workshop": {
    "bigSatchel": {
      "costs": [[100, 0], [200, 2], [400, 4], [600, 6], [800, 8], [1000, 10], [1300, 12], [1600, 14], [2000, 16], [2500, 20]],
      "capacityPerLevel": 5
    },
    "wideStash": {
      "costs": [[80, 0], [160, 1], [320, 3], [500, 5], [700, 7], [900, 9], [1200, 11], [1500, 13], [1900, 16], [2400, 20]],
      "firstBandLevels": 3,
      "firstBandCapacity": 20,
      "laterCapacity": 40
    },
    "bountyReroll": {"costs": [[150, 2], [300, 4]], "rerollsPerLevel": 1},
    "echoLantern": {"costs": [[200, 2], [400, 5]], "basePercent": 25, "percentPerLevel": 5},
    "lostMap": {"costs": [[250, 3]], "roomsPerLevel": 1, "minimumRooms": 2},
    "pactStars": {"costs": [[350, 6]], "offersPerLevel": 1}
  }
}
"""
LEGACY = json.loads(_LEGACY_JSON, parse_float=Decimal)

# Paths also define stable fingerprint keys; units are part of their identity.
FIELDS = {
    "enhanceFailure.currentLevelOffset": ("CurrentLevelOffset", "level"),
    "enhanceFailure.percentPerLevel": ("PercentPerLevel", "percent"),
    "enhanceFailure.maximumPercent": ("MaximumPercent", "percent"),
    "enhanceFailure.demotionChance": ("DemotionChance", "probability"),
    "enhanceFailure.demotionSteps": ("DemotionSteps", "level"),
    "enhancement.baseCap": ("BaseCap", "level"),
    "enhancement.stepPerBreak": ("StepPerBreak", "level"),
    "enhancement.milestones": ("Milestones", "level"),
    "enhancement.milestonePowerPercent": ("MilestonePowerPercent", "percent"),
    "enhancement.statPercents": ("StatPercents", "percent"),
    "enhancement.powerPercents": ("PowerPercents", "percent"),
    "enhancement.shardCosts": ("EnhanceShardCosts", "shards"),
    "enhancement.epicMaterialMultiplier": ("EpicMaterialMultiplier", "multiplier"),
    "awakening.thresholds": ("AwakenThresholds", "points"),
    "awakening.powerPercents": ("AwakenPowerPercents", "percent"),
    "awakening.affixPercents": ("AwakenAffixPercents", "percent"),
    "awakening.normalPoints": ("AwakenNormalPoints", "points"),
    "awakening.miniBossPoints": ("AwakenMiniBossPoints", "points"),
    "awakening.bossPoints": ("AwakenBossPoints", "points"),
    "awakening.nightmareMultiplier": ("AwakenNightmareMultiplier", "multiplier"),
    "limitBreak.maxByRarity": ("MaxBreaks", "count"),
    "limitBreak.shardCosts": ("BreakShardCosts", "shards"),
    "limitBreak.tuningCosts": ("BreakTuningCosts", "tuning"),
    "retune.maximum": ("MaxRetunes", "count"),
    "retune.choices": ("RetuneChoices", "count"),
    "retune.baseTuningCost": ("RetuneBaseCost", "tuning"),
    "retune.tuningCostPerRetune": ("RetuneCostPerLevel", "tuning"),
    "affixReroll.baseShards": ("RerollBaseShards", "shards"),
    "affixReroll.baseTuning": ("RerollBaseTuning", "tuning"),
    "affixReroll.growthMultiplier": ("RerollGrowthMultiplier", "multiplier"),
    "craft.shardCosts": ("CraftShardCosts", "shards"),
    "craft.tuningCosts": ("CraftTuningCosts", "tuning"),
    "craft.luck": ("CraftLuck", "luck"),
    "synthesis.inputs": ("SynthesisInputs", "count"),
    "synthesis.shardCosts": ("SynthesisShardCosts", "shards"),
    "synthesis.tuningCosts": ("SynthesisTuningCosts", "tuning"),
    "synthesis.targetCostPercent": ("SynthesisTargetCostPercent", "percent"),
    "salvage.shards": ("SalvageShards", "shards"),
    "salvage.epicTuning": ("SalvageEpicTuning", "tuning"),
    "salvage.enhanceRefundDivisor": ("SalvageRefundDivisor", "divisor"),
    "guaranteedEnhancement.fountainSteps": ("FountainSteps", "level"),
    "guaranteedEnhancement.forgeShrineSteps": ("ForgeShrineSteps", "level"),
    "guaranteedEnhancement.forgeShrineShards": ("ForgeShrineShards", "shards"),
    "guaranteedEnhancement.temperingAltarSteps": ("TemperingAltarSteps", "level"),
}
for key, fields in {
    "bigSatchel": {"capacityPerLevel": "count"},
    "wideStash": {"firstBandLevels": "level", "firstBandCapacity": "count", "laterCapacity": "count"},
    "bountyReroll": {"rerollsPerLevel": "count"},
    "echoLantern": {"basePercent": "percent", "percentPerLevel": "percent"},
    "lostMap": {"roomsPerLevel": "count", "minimumRooms": "count"},
    "pactStars": {"offersPerLevel": "count"},
}.items():
    prefix = key[0].upper() + key[1:]
    FIELDS[f"workshop.{key}.costs"] = (prefix + "Costs", "materials")
    for field, unit in fields.items():
        FIELDS[f"workshop.{key}.{field}"] = (prefix + field[0].upper() + field[1:], unit)


def _unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"forge duplicate key: {key}")
        result[key] = value
    return result


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="forge"):
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
    elif isinstance(reference, Decimal):
        if type(data) not in (int, Decimal) or not Decimal(data).is_finite() or not 0 <= data <= 1000000:
            raise ValueError(f"{path}: expected finite nonnegative decimal <= 1000000")
        if Decimal(data).as_tuple().exponent < -6:
            raise ValueError(f"{path}: at most six fractional digits")
    elif type(data) is not int or not 0 <= data <= INT_MAX:
        raise ValueError(f"{path}: expected nonnegative Int32 (not boolean)")


def validate(data):
    _shape(data, LEGACY)
    if data["schemaVersion"] != 2:
        raise ValueError("forge.schemaVersion: expected 2")
    e, f, a = data["enhancement"], data["enhanceFailure"], data["awakening"]
    # Fixed table/save extents are not tunables; values inside them are.
    if not 1 <= e["baseCap"] <= 20 or not 1 <= e["stepPerBreak"] <= 20:
        raise ValueError("enhancement cap and break step must be in 1..20")
    if any(e["baseCap"] + e["stepPerBreak"] * count > 20 for count in data["limitBreak"]["maxByRarity"]):
        raise ValueError("enhancement maximum must fit the 20-level table")
    if any(count > 3 for count in data["limitBreak"]["maxByRarity"]):
        raise ValueError("limitBreak.maxByRarity: at most three breaks")
    if e["milestones"] != sorted(set(e["milestones"])) or not 1 <= e["milestones"][0] or e["milestones"][-1] > 20:
        raise ValueError("enhancement.milestones: five increasing levels in 1..20")
    if e["milestonePowerPercent"] < 1:
        raise ValueError("enhancement.milestonePowerPercent: must be positive (source bonus is inverted by Build)")
    if a["thresholds"][0] != 0 or a["thresholds"] != sorted(set(a["thresholds"])):
        raise ValueError("awakening.thresholds: start at zero, strictly increase")
    if not 0 <= f["currentLevelOffset"] <= 20 or f["maximumPercent"] > 100:
        raise ValueError("enhanceFailure offset must be in 0..20 and cap in 0..100")
    if max(f["currentLevelOffset"], 20-f["currentLevelOffset"]) * f["percentPerLevel"] > INT_MAX:
        raise ValueError("percentPerLevel product must fit Int32 at every legal enhancement level")
    if f["demotionChance"] > 1 or not 1 <= f["demotionSteps"] <= 20:
        raise ValueError("enhanceFailure demotion chance must be in 0..1 and steps in 1..20")
    if not 1 <= data["retune"]["maximum"] <= 100 or not 1 <= data["retune"]["choices"] <= 16:
        raise ValueError("retune maximum must be in 1..100 and choices in 1..16")
    growth = Decimal(data["affixReroll"]["growthMultiplier"])
    if not 1 <= growth <= 8 or growth * 2 != (growth * 2).to_integral_value():
        raise ValueError("affixReroll.growthMultiplier: expected 1..8 in exact half steps")
    if e["epicMaterialMultiplier"] < 1 or data["salvage"]["enhanceRefundDivisor"] < 1 or any(n < 1 for n in data["synthesis"]["inputs"]):
        raise ValueError("material multiplier, refund divisor and synthesis inputs must be positive")
    for key in ("fountainSteps", "forgeShrineSteps", "temperingAltarSteps"):
        if not 1 <= data["guaranteedEnhancement"][key] <= 20:
            raise ValueError(f"guaranteedEnhancement.{key}: expected 1..20")
    multiplier = e["epicMaterialMultiplier"]
    if multiplier > INT_MAX // 5:
        raise ValueError("enhancement.epicMaterialMultiplier: rarity multiplier product must fit Int32")
    for path in ("enhancement.shardCosts", "limitBreak.shardCosts", "limitBreak.tuningCosts"):
        if max(_get(data, path)) * multiplier > INT_MAX:
            raise ValueError(f"{path}: charged cost must fit Int32")
    retune = data["retune"]
    if (retune["baseTuningCost"] + retune["tuningCostPerRetune"] * (retune["maximum"]-1)) * multiplier > INT_MAX:
        raise ValueError("retune: charged cost must fit Int32")
    for key in ("baseShards", "baseTuning"):
        if data["affixReroll"][key] * 5 * multiplier > INT_MAX:
            raise ValueError(f"affixReroll.{key}: initial charged cost must fit Int32")
    if max(data["synthesis"]["shardCosts"]) * data["synthesis"]["targetCostPercent"] > INT_MAX:
        raise ValueError("synthesis: targeted cost product must fit Int32")
    if data["guaranteedEnhancement"]["forgeShrineShards"] * multiplier > INT_MAX:
        raise ValueError("guaranteedEnhancement.forgeShrineShards: charged cost must fit Int32")
    if max(a["normalPoints"], a["miniBossPoints"], a["bossPoints"]) * a["nightmareMultiplier"] > INT_MAX:
        raise ValueError("awakening: nightmare reward must fit Int32")
    if sum(e["shardCosts"]) + max(data["salvage"]["shards"]) > INT_MAX:
        raise ValueError("salvage: enhancement refund accumulation must fit Int32")
    workshop = data["workshop"]
    for key, entry in workshop.items():
        if any(value > INT_MAX // 100 for pair in entry["costs"] for value in pair):
            raise ValueError(f"workshop.{key}.costs: cumulative costs must fit Int32")
    for key, field in (("bigSatchel", "capacityPerLevel"), ("bountyReroll", "rerollsPerLevel"), ("lostMap", "roomsPerLevel"), ("pactStars", "offersPerLevel")):
        if workshop[key][field] * len(workshop[key]["costs"]) > 1000000:
            raise ValueError(f"workshop.{key}.{field}: effect exceeds supported capacity")
    stash = workshop["wideStash"]
    if not 1 <= stash["firstBandLevels"] <= len(stash["costs"]) or max(stash["firstBandCapacity"], stash["laterCapacity"]) > 100000:
        raise ValueError("workshop.wideStash: invalid band or capacity")
    echo = workshop["echoLantern"]
    if echo["basePercent"] + echo["percentPerLevel"] * len(echo["costs"]) > 100:
        raise ValueError("workshop.echoLantern: echo percentage must not exceed 100")
    return data


def load(path=PATH):
    return validate(json.loads(Path(path).read_text(encoding="utf-8"), parse_float=Decimal, object_pairs_hook=_unique))


def _number(value):
    return format(Decimal(value).normalize(), "f")


def effective_fields(data):
    validate(data)
    return {name: _get(data, path) for path, (name, _) in FIELDS.items()}


def fingerprint_records(data):
    validate(data)
    failure = data["enhanceFailure"]
    coefficients = tuple(failure[key] for key in ("currentLevelOffset", "percentPerLevel", "maximumPercent"))
    records = [] if coefficients == (2, 3, 45) else ["balance:forge:v1:" + ":".join(map(str, coefficients))]
    for path, (_, unit) in FIELDS.items():
        if path in ("enhanceFailure.currentLevelOffset", "enhanceFailure.percentPerLevel", "enhanceFailure.maximumPercent"):
            continue
        value, old = _get(data, path), _get(LEGACY, path)
        type_name = "double" if isinstance(old, Decimal) or path == "craft.luck" else "int"
        def changed(current, previous, key):
            if isinstance(current, list):
                for index, (item, was) in enumerate(zip(current, previous)):
                    changed(item, was, f"{key}/{index}")
            elif current != previous:
                leaf_unit = ("shards" if key.endswith("/0") else "tuning") if unit == "materials" else unit
                records.append(f"balance:forge:v2:{key}:{type_name}:{leaf_unit}:{_number(current)}")
        changed(value, old, path.replace(".", "/"))
    return records


def render(data):
    validate(data)
    effective = effective_fields(data)
    lines = ["// <auto-generated />", "// Source: tools/balance/forge.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class ForgeBalance", "    {"]
    for index, name in enumerate(("First", "Second", "Third", "Fourth", "Fifth")):
        lines.append(f"        internal const int Milestone{name} = {data['enhancement']['milestones'][index]};")
    lines.append(f"        internal const int RerollGrowthNumerator = {int(data['affixReroll']['growthMultiplier'] * 2)};")
    lines.append(f"        internal const int MaxAwakenLevel = {len(data['awakening']['thresholds']) - 1};")
    records = fingerprint_records(data)
    for path, (name, unit) in FIELDS.items():
        value, old = effective[name], _get(LEGACY, path)
        decimal = isinstance(old, Decimal) or path == "craft.luck"
        type_name = "double" if decimal else "int"
        if isinstance(value, list):
            if path.endswith(".costs"):
                literal = ", ".join("(" + ", ".join(map(str, pair)) + ")" for pair in value)
                lines.append(f"        internal static readonly (int Shards, int Tuning)[] {name} = {{ {literal} }};")
            else:
                lines.append(f"        internal static readonly {type_name}[] {name} = {{ " + ", ".join(_number(v) + ("d" if decimal else "") for v in value) + " };")
        else:
            lines.append(f"        internal const {type_name} {name} = {_number(value)}" + ("d" if decimal else "") + ";")
    lines.append("        internal static readonly string[] ContentFingerprintRecords = { " + ", ".join(json.dumps(record) for record in records) + " };")
    lines += ["    }", "}", ""]
    return "\n".join(lines)

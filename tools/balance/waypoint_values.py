"""Compile waypoints numeric definitions without publishing generated files."""
from decimal import Decimal
import json
import math
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/waypoints.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Waypoints.Generated.cs"
# Frozen compatibility reference, never an editable source or runtime fallback.
LEGACY = json.loads(r'''{
  "schemaVersion": 1,
  "offered": 3,
  "definitions": {
    "WeaponRoad": {
      "luck": 1
    },
    "ArmorRoad": {
      "luck": 1
    },
    "CharmRoad": {
      "luck": 1
    },
    "HeadRoad": {
      "luck": 1
    },
    "HandsRoad": {
      "luck": 1
    },
    "FeetRoad": {
      "luck": 1
    },
    "NightmareHunt": {
      "nightmareChanceMultiplier": 2,
      "nightmareRewardMultiplier": 2
    },
    "GlassAegis": {
      "healingMultiplier": 0.5,
      "shieldMultiplier": 2
    },
    "ResonantRoad": {
      "reactionMultiplier": 2
    },
    "EndlessNight": {
      "awakeningMultiplier": 3
    },
    "TemperedFinds": {
      "enhancement": 2,
      "maxRelicsPerRoom": 1
    },
    "FleetingMemories": {
      "memoryCooldownMultiplier": 0.8,
      "pressureMultiplier": 1.25
    },
    "SummonerTrail": {
      "summonPowerMultiplier": 1.5,
      "heroHealthMultiplier": 0.85
    },
    "EpicMirage": {
      "shardMultiplier": 0
    },
    "ShardRoad": {
      "relicSalvageMultiplier": 3
    },
    "TwinCache": {
      "tuningMultiplier": 0
    },
    "HumbleForge": {
      "enhancement": 4
    },
    "SixfoldRoad": {
      "luck": 0.5
    },
    "AwakeningPilgrimage": {
      "awakeningPerRelic": 20
    },
    "FirstClaim": {
      "maxRelicsPerRoom": 1
    }
  },
  "rewards": {
    "twinRelicCopies": 2,
    "bossSalvageMultiplier": 4,
    "starXpPerShard": 4,
    "hoardRewardMultiplier": 3,
    "tuningPerNonBossRelic": 1
  }
}''', parse_float=Decimal)
# Closed numeric schema: names and enum/definition relationships stay in Core.
FIELDS = {
    'offered': ('Offered', 'int', 'choices'),
    'definitions.WeaponRoad.luck': ('WeaponRoadLuck', 'double', 'luck'),
    'definitions.ArmorRoad.luck': ('ArmorRoadLuck', 'double', 'luck'),
    'definitions.CharmRoad.luck': ('CharmRoadLuck', 'double', 'luck'),
    'definitions.HeadRoad.luck': ('HeadRoadLuck', 'double', 'luck'),
    'definitions.HandsRoad.luck': ('HandsRoadLuck', 'double', 'luck'),
    'definitions.FeetRoad.luck': ('FeetRoadLuck', 'double', 'luck'),
    'definitions.NightmareHunt.nightmareChanceMultiplier': ('NightmareHuntNightmareChanceMultiplier', 'double', 'multiplier'),
    'definitions.NightmareHunt.nightmareRewardMultiplier': ('NightmareHuntNightmareRewardMultiplier', 'double', 'multiplier'),
    'definitions.GlassAegis.healingMultiplier': ('GlassAegisHealingMultiplier', 'double', 'multiplier'),
    'definitions.GlassAegis.shieldMultiplier': ('GlassAegisShieldMultiplier', 'double', 'multiplier'),
    'definitions.ResonantRoad.reactionMultiplier': ('ResonantRoadReactionMultiplier', 'double', 'multiplier'),
    'definitions.EndlessNight.awakeningMultiplier': ('EndlessNightAwakeningMultiplier', 'double', 'multiplier'),
    'definitions.TemperedFinds.enhancement': ('TemperedFindsEnhancement', 'int', 'enhancement-level'),
    'definitions.TemperedFinds.maxRelicsPerRoom': ('TemperedFindsMaxRelicsPerRoom', 'int', 'relics/room'),
    'definitions.FleetingMemories.memoryCooldownMultiplier': ('FleetingMemoriesMemoryCooldownMultiplier', 'double', 'multiplier'),
    'definitions.FleetingMemories.pressureMultiplier': ('FleetingMemoriesPressureMultiplier', 'double', 'multiplier'),
    'definitions.SummonerTrail.summonPowerMultiplier': ('SummonerTrailSummonPowerMultiplier', 'double', 'multiplier'),
    'definitions.SummonerTrail.heroHealthMultiplier': ('SummonerTrailHeroHealthMultiplier', 'double', 'multiplier'),
    'definitions.EpicMirage.shardMultiplier': ('EpicMirageShardMultiplier', 'double', 'multiplier'),
    'definitions.ShardRoad.relicSalvageMultiplier': ('ShardRoadRelicSalvageMultiplier', 'int', 'multiplier'),
    'definitions.TwinCache.tuningMultiplier': ('TwinCacheTuningMultiplier', 'double', 'multiplier'),
    'definitions.HumbleForge.enhancement': ('HumbleForgeEnhancement', 'int', 'enhancement-level'),
    'definitions.SixfoldRoad.luck': ('SixfoldRoadLuck', 'double', 'luck'),
    'definitions.AwakeningPilgrimage.awakeningPerRelic': ('AwakeningPilgrimageAwakeningPerRelic', 'int', 'awakening/relic'),
    'definitions.FirstClaim.maxRelicsPerRoom': ('FirstClaimMaxRelicsPerRoom', 'int', 'relics/room'),
    'rewards.twinRelicCopies': ('TwinRelicCopies', 'int', 'copies/relic'),
    'rewards.bossSalvageMultiplier': ('BossSalvageMultiplier', 'int', 'multiplier'),
    'rewards.starXpPerShard': ('StarXpPerShard', 'int', 'star-xp/shard'),
    'rewards.hoardRewardMultiplier': ('HoardRewardMultiplier', 'int', 'multiplier'),
    'rewards.tuningPerNonBossRelic': ('TuningPerNonBossRelic', 'int', 'tuning/relic'),
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="waypoints"):
    if isinstance(reference, dict):
        if type(data) is not dict or set(data) != set(reference):
            raise ValueError(f"{path}: expected exactly {', '.join(reference)}")
        for key in reference:
            _shape(data[key], reference[key], f"{path}.{key}")
    elif isinstance(data, bool) or not isinstance(data, (int, Decimal)) or not Decimal(data).is_finite():
        raise ValueError(f"{path}: expected a finite number (not boolean)")


def validate(data):
    _shape(data, LEGACY)
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1:
        raise ValueError("waypoints.schemaVersion: expected integer 1")
    for path, (_, kind, _) in FIELDS.items():
        value = _get(data, path)
        if kind == "int":
            if type(value) is not int or not 0 <= value <= 2147483647:
                raise ValueError(f"waypoints.{path}: expected nonnegative Int32 (not boolean)")
        else:
            try:
                adopted = float(value)
            except OverflowError as exc:
                raise ValueError(f"waypoints.{path}: exceeds double range") from exc
            if value < 0 or not math.isfinite(adopted) or (value != 0 and adopted == 0):
                raise ValueError(f"waypoints.{path}: expected a nonnegative finite representable double")
            if Decimal(repr(adopted)) != Decimal(value):
                raise ValueError(f"waypoints.{path}: double precision would change the authored value")
    if not 1 <= data["offered"] <= 24:
        raise ValueError("waypoints.offered: expected 1..24 (definition count)")
    if data["rewards"]["twinRelicCopies"] < 1 or data["rewards"]["hoardRewardMultiplier"] < 1:
        raise ValueError("waypoints.rewards: relic copy counts must be positive")
    multiplier = data["definitions"]["NightmareHunt"]["nightmareRewardMultiplier"]
    if multiplier != int(multiplier) or not 1 <= multiplier <= 256:
        raise ValueError("waypoints.definitions.NightmareHunt.nightmareRewardMultiplier: expected integral 1..256 (relic copies)")
    for name in ("twinRelicCopies", "hoardRewardMultiplier"):
        if data["rewards"][name] > 256:
            raise ValueError(f"waypoints.rewards.{name}: exceeds existing deferred relic capacity 256")
    for path in ("definitions.ShardRoad.relicSalvageMultiplier", "definitions.AwakeningPilgrimage.awakeningPerRelic",
                 "rewards.bossSalvageMultiplier", "rewards.tuningPerNonBossRelic"):
        if _get(data, path) > 2147483647 // 256:
            raise ValueError(f"waypoints.{path}: conversion for the existing relic capacity exceeds Int32")
    salvage = max(read_json(ROOT / "tools/balance/forge.json")["salvage"]["shards"])
    for path in ("definitions.ShardRoad.relicSalvageMultiplier", "rewards.bossSalvageMultiplier"):
        if salvage * _get(data, path) > 2147483647:
            raise ValueError(f"waypoints.{path}: adopted salvage value product exceeds Int32")
    return data


def load(path=PATH):
    return validate(read_json(path))


def _literal(value):
    return format(Decimal(value), "f").rstrip("0").rstrip(".") if Decimal(value) != Decimal(value).to_integral_value() else str(int(value))


def fingerprint_record(data):
    validate(data)
    records = []
    for path, (_, kind, unit) in FIELDS.items():
        value = _get(data, path)
        if value != _get(LEGACY, path):
            literal = _literal(value)
            if kind == "double":
                # The fingerprint describes the adopted IEEE double, not JSON spelling.
                literal = repr(float(value))
                if float(value) == float(_get(LEGACY, path)):
                    continue
            records.append(f"{path.replace('.', '/')}:{kind}:{unit}:{literal}")
    return "balance:waypoints:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/waypoints.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class WaypointBalance", "    {"]
    for path, (name, kind, _) in FIELDS.items():
        literal = _literal(_get(data, path))
        if kind == "double":
            literal += "d"
        lines.append(f"        internal const {kind} {name} = {literal};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}

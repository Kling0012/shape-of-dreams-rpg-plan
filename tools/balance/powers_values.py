"""Compile power time/distance/condition tuning into typed constants."""
from pathlib import Path

from equipment_common import fingerprint_records, load_json, quote, semantic_identity

ROOT = Path(__file__).resolve().parents[2]
TABLE_PATH = ROOT / "tools" / "balance" / "powers.json"
OUTPUT_PATH = ROOT / "src" / "SodRpg.Core" / "Game" / "Balance" / "Powers.Generated.cs"
INT32_MAX = 2147483647
RECORD_PREFIX = "balance:powers:v1:"

# key -> (C# type, unit). Units drive validation bounds and fingerprint records.
SPEC = {
    "runtime": {
        "MomentumMaxStacks": ("int", "count"),
        "MomentumDuration": ("float", "seconds"),
        "RetaliationDuration": ("float", "seconds"),
        "TailwindDuration": ("float", "seconds"),
        "BarrierInterval": ("float", "seconds"),
        "BarrierDuration": ("float", "seconds"),
        "BarrierFirstDelay": ("float", "seconds"),
        "StarShieldDuration": ("float", "seconds"),
        "SecondWindCooldown": ("float", "seconds"),
        "SecondWindThreshold": ("float", "ratio"),
        "LifestealInterval": ("float", "seconds"),
        "ThornsInterval": ("float", "seconds"),
        "ExecuteThreshold": ("float", "ratio"),
        "BulwarkEnemies": ("int", "count"),
        "ResonanceRange": ("float", "meters"),
        "ChainChance": ("double", "ratio"),
        "ChainTargets": ("int", "count"),
        "ChainRange": ("float", "meters"),
        "ShatterRadius": ("float", "meters"),
        "AegisThreshold": ("float", "ratio"),
        "AegisCooldown": ("float", "seconds"),
        "BloodlustThreshold": ("float", "ratio"),
        "ConvergenceCooldown": ("float", "seconds"),
        "SurgeDuration": ("float", "seconds"),
        "SoulSiphonInterval": ("float", "seconds"),
        "WhirlwindInterval": ("float", "seconds"),
        "WhirlwindRadius": ("float", "meters"),
        "FrenzyMaxEnemies": ("int", "count"),
        "OpeningStrikeThreshold": ("float", "ratio"),
        "SprintDuration": ("float", "seconds"),
        "EchoingDodgeWindow": ("float", "seconds"),
        "ShadowStepWindow": ("float", "seconds"),
        "VigorThreshold": ("float", "ratio"),
        "OverloadDuration": ("float", "seconds"),
        "FinaleWindow": ("float", "seconds"),
        "FinaleCooldown": ("float", "seconds"),
        "CriticalEchoCooldown": ("float", "seconds"),
        "CrystalResonanceMaxTiers": ("int", "count"),
        "PreyPrideMaxLevel": ("int", "count"),
        "DevotionMaxStacks": ("int", "count"),
        "OverflowingLifeDuration": ("float", "seconds"),
        "OverflowingLifeMaxHealthRatio": ("float", "ratio"),
        "WildfireMinStacks": ("int", "count"),
        "WildfireRange": ("float", "meters"),
        "WildfireCooldown": ("float", "seconds"),
        "StillWaterDuration": ("float", "seconds"),
        "StillWaterCooldown": ("float", "seconds"),
        "SpendersWardGold": ("int", "gold"),
        "SpendersWardMaxStacks": ("int", "count"),
        "SpendersWardDuration": ("float", "seconds"),
        "PerfectReadDuration": ("float", "seconds"),
        "PerfectReadCooldown": ("float", "seconds"),
        "LucidBoonMaxDreams": ("int", "count"),
        "LinkSurgeDuration": ("float", "seconds"),
    },
    "newPowers": {
        "ConditionalPowerCap": ("int", "percent"),
        "RunUpDistance": ("float", "meters"),
        "ImmovableStanceStillSeconds": ("float", "seconds"),
        "ImmovableStanceMaxReduction": ("int", "percent"),
        "MedleyMaxTypes": ("int", "count"),
        "MedleyDuration": ("float", "seconds"),
        "SpellsweepWindow": ("float", "seconds"),
        "PileOnUses": ("int", "count"),
        "WatchfulHandThreshold": ("float", "ratio"),
        "WatchfulHandCooldown": ("float", "seconds"),
        "WatchfulHandDuration": ("float", "seconds"),
        "RelayHandCooldown": ("float", "seconds"),
        "ShieldbreakBurstRadius": ("float", "meters"),
        "ShieldbreakBurstCooldown": ("float", "seconds"),
        "SharedWardAllyRange": ("float", "meters"),
        "SharedWardDuration": ("float", "seconds"),
        "TriumphSongRange": ("float", "meters"),
        "WanderersEdgeCooldown": ("float", "seconds"),
        "FocusFireHits": ("int", "count"),
        "FocusFireWindow": ("float", "seconds"),
        "BasicSplashRadius": ("float", "meters"),
        "BreakoutMinEnemies": ("int", "count"),
        "BreakoutTriggerEnemies": ("int", "count"),
        "BreakoutRange": ("float", "meters"),
        "BreakoutShieldDuration": ("float", "seconds"),
        "BreakoutSlowPercent": ("int", "percent"),
        "BreakoutSlowDuration": ("float", "seconds"),
        "BreakoutCooldown": ("float", "seconds"),
        "DuelistsWayRange": ("float", "meters"),
        "StrafeShotSlowDuration": ("float", "seconds"),
        "StardustCycleLightStacks": ("int", "count"),
        "StardustCycleGlobalCooldown": ("float", "seconds"),
        "StardustCyclePerEnemyCooldown": ("float", "seconds"),
        "UmbralHeritageMinStacks": ("int", "count"),
        "UmbralHeritageRange": ("float", "meters"),
        "UmbralHeritageMaxTargets": ("int", "count"),
        "ElementalHarvestMinTypes": ("int", "count"),
        "ElementalHarvestMaxTypes": ("int", "count"),
        "ElementalHarvestRange": ("float", "meters"),
        "PrismShiftCooldown": ("float", "seconds"),
        "PrismShiftDuration": ("float", "seconds"),
        "PackFeastDuration": ("float", "seconds"),
        "DeathBloomRange": ("float", "meters"),
        "ShardBoonCooldown": ("float", "seconds"),
        "LifelineDuration": ("float", "seconds"),
        "DreamOmenShieldDuration": ("float", "seconds"),
        "TollOfGrudgeThreshold": ("float", "ratio"),
        "TollOfGrudgeRange": ("float", "meters"),
        "PilingLuckMaxStacks": ("int", "count"),
        "WeakPointWoundCrits": ("int", "count"),
        "WeakPointWoundWindow": ("float", "seconds"),
        "ReadyGuardWindow": ("float", "seconds"),
        "ReadyGuardDuration": ("float", "seconds"),
        "ApothecaryAllyRange": ("float", "meters"),
        "ApothecaryAllyHealShare": ("float", "ratio"),
        "SpilloverStrikeRange": ("float", "meters"),
        "BareHandedPrideCooldown": ("float", "seconds"),
        "UnbowedMindDuration": ("float", "seconds"),
        "UnbowedMindCooldown": ("float", "seconds"),
        "PrimedWindowBase": ("float", "seconds"),
        "PrimedPercentCap": ("int", "percent"),
    },
    "host": {
        "AegisShieldDuration": ("float", "seconds"),
        "BulwarkRange": ("float", "meters"),
        "FrenzyRange": ("float", "meters"),
    },
    "lastStarlight": {
        "LastStarlightDelayReductionMax": ("float", "seconds"),
        "LastStarlightDelayFloor": ("float", "seconds"),
        "LastStarlightAttractionBonusMax": ("float", "meters"),
        "LastStarlightAttractionCap": ("float", "meters"),
        "LastStarlightTickRadiusBonusMax": ("float", "meters"),
        "LastStarlightTickRadiusCap": ("float", "meters"),
        "LastStarlightDurationBonusMax": ("float", "seconds"),
        "LastStarlightDurationCap": ("float", "seconds"),
        "LastStarlightRelocateRange": ("float", "meters"),
    },
}

# Identity of the hand-written values at the stage8b cutover; unchanged tables add no records.
LEGACY_IDENTITY = "127-8c12651beca5a705"

_CSHARP_TYPES = {"int": "int", "float": "float", "double": "double"}
_RECORD_TYPES = {"int": "int32", "float": "float32", "double": "float64"}


def _decimal_string(value):
    if isinstance(value, bool):
        raise ValueError(f"boolean is not a number: {value}")
    return str(value) if isinstance(value, int) else format(value, "f")



def validate(table, path=TABLE_PATH):
    if not isinstance(table, dict) or set(table) != {"schemaVersion"} | set(SPEC):
        raise ValueError(f"{path}: expected schemaVersion and sections {', '.join(SPEC)}")
    if table["schemaVersion"] != 1:
        raise ValueError(f"{path}: unsupported schemaVersion {table['schemaVersion']}")
    for section, expected in SPEC.items():
        rows = table[section]
        if not isinstance(rows, dict) or set(rows) != set(expected):
            missing = sorted(set(expected) - set(rows)) if isinstance(rows, dict) else ["<all>"]
            unknown = sorted(set(rows) - set(expected)) if isinstance(rows, dict) else []
            raise ValueError(f"{path}: {section} missing {missing} unknown {unknown}")
        for key, (kind, unit) in expected.items():
            value = rows[key]
            where = f"{path}: {section}.{key}"
            if kind == "int":
                if isinstance(value, bool) or not isinstance(value, int) or not 0 < value <= INT32_MAX:
                    raise ValueError(f"{where}: expected positive Int32")
            else:
                if isinstance(value, bool) or not isinstance(value, (int, float)) and not hasattr(value, "as_tuple"):
                    raise ValueError(f"{where}: expected a number for {kind}")
                number = float(value)
                if not number > 0 or number == float("inf"):
                    raise ValueError(f"{where}: expected a finite positive value")
                if unit == "ratio" and not number < 1:
                    raise ValueError(f"{where}: expected a ratio strictly between 0 and 1")
    return table


def load(path=None):
    path = TABLE_PATH if path is None else Path(path)
    return validate(load_json(path), path)


def records(table):
    result = []
    for section, expected in SPEC.items():
        for key, (kind, unit) in expected.items():
            value = _decimal_string(table[section][key])
            result.append(f"powers/{section}/{key}:{_RECORD_TYPES[kind]}:{unit}={value}")
    return result


def render_cs(table):
    lines = ["// Generated from tools/balance/powers.json. Do not edit.",
             "namespace SodRpg.Core.Game", "{", "    internal static class PowersBalance", "    {"]
    for section, expected in SPEC.items():
        for key, (kind, _unit) in expected.items():
            suffix = "" if kind == "double" else ("f" if kind == "float" else "")
            literal = _decimal_string(table[section][key]) + suffix
            lines.append(f"        internal const {_CSHARP_TYPES[kind]} {key} = {literal};")
    effective = fingerprint_records("powers", records(table), LEGACY_IDENTITY)
    lines.append("")
    if effective:
        lines.extend(["        internal static readonly string[] ContentFingerprintRecords = new string[]", "        {"])
        lines.extend("            " + quote(record) + "," for record in effective)
        lines.append("        };")
    else:
        lines.append("        internal static readonly string[] ContentFingerprintRecords = System.Array.Empty<string>();")
    lines.extend(["    }", "}", ""])
    return "\n".join(lines)


def render_outputs(table=None):
    table = load() if table is None else validate(table)
    return {OUTPUT_PATH: render_cs(table)}

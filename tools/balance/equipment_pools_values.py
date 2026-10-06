"""Compile canonical equipment roll ranges/caps and derived named definitions."""
from pathlib import Path
import importlib.util

from equipment_common import fingerprint_records, load_json, quote

ROOT = Path(__file__).resolve().parents[2]
TABLE_DIR = ROOT / "tools/balance/equipment"
OUTPUT_DIR = ROOT / "src/SodRpg.Core/Game/Balance"
INT32_MAX = 2147483647

AFFIX_KEYS = {'Weapon': ('AttackFlat', 'AttackPct', 'PowerFlat', 'PowerPct', 'AttackSpeedPct', 'CritChancePct', 'CritDamagePct', 'Haste', 'FireAmp', 'LightAmp', 'DarkAmp', 'AttackRangePct', 'ColdAmp', 'MaxHealthPct', 'Tenacity'), 'Armor': ('MaxHealthPct', 'MaxHealthFlat', 'Armor', 'HealthRegen', 'Tenacity', 'MoveSpeedPct', 'Haste', 'LightAmp', 'PowerFlat', 'PowerPct', 'AttackFlat', 'AttackPct', 'ColdAmp', 'DarkAmp', 'FireAmp', 'HealPower', 'ShieldPower'), 'Charm': ('Haste', 'MoveSpeedPct', 'CritChancePct', 'AttackFlat', 'AttackPct', 'PowerFlat', 'PowerPct', 'MaxHealthPct', 'HealthRegen', 'ColdAmp', 'FireAmp', 'LightAmp', 'DarkAmp', 'Tenacity', 'CritDamagePct', 'HealPower', 'ShieldPower'), 'Head': ('PowerFlat', 'PowerPct', 'Haste', 'CritChancePct', 'MaxHealthPct', 'Armor', 'Tenacity', 'LightAmp', 'DarkAmp', 'HealthRegen', 'AttackFlat', 'AttackPct', 'MoveSpeedPct', 'ColdAmp', 'FireAmp', 'HealPower', 'ShieldPower'), 'Hands': ('AttackFlat', 'AttackPct', 'AttackSpeedPct', 'CritChancePct', 'CritDamagePct', 'PowerFlat', 'PowerPct', 'FireAmp', 'ColdAmp', 'AttackRangePct', 'Armor', 'MaxHealthFlat', 'LightAmp', 'DarkAmp', 'Haste'), 'Feet': ('MoveSpeedPct', 'Tenacity', 'Armor', 'MaxHealthPct', 'MaxHealthFlat', 'HealthRegen', 'AttackSpeedPct', 'Haste', 'ColdAmp', 'FireAmp', 'CritChancePct', 'LightAmp', 'DarkAmp', 'AttackFlat', 'AttackPct', 'PowerFlat', 'PowerPct')}

POWER_POOL_KEYS = {'Weapon': ('ShieldBash', 'WanderersEdge', 'FocusFire', 'DuelistsWay', 'RunUp', 'BrittleIce', 'ElementalHarvest', 'Medley', 'Spellsweep', 'BareHandedPride', 'OpeningSalvo', 'PileOn', 'AceInHand', 'PilingLuck', 'WeakPointWound', 'CritSplash', 'ReturningBlade', 'SpilloverStrike', 'Momentum', 'Lifesteal', 'Executioner', 'Blaze', 'ChainLightning', 'Bloodlust', 'Ember', 'Radiance', 'UltimateSurge', 'SoulSiphon', 'Frenzy', 'OpeningStrike', 'Vigor', 'Overload', 'Fetters', 'CriticalEcho', 'Wildfire'), 'Armor': ('UnbowedMind', 'ShieldbreakBurst', 'SharedWard', 'TriumphSong', 'WatchfulHand', 'Breakout', 'ImmovableStance', 'VanguardsOath', 'DeathBloom', 'DreamOmen', 'RearguardsWay', 'TollOfGrudge', 'ReadyGuard', 'Apothecary', 'Retaliation', 'Bulwark', 'Thorns', 'Barrier', 'Aegis', 'EchoingDodge', 'Whirlwind', 'Frenzy', 'StarShield', 'Sprint', 'OverflowingLife', 'Fetters', 'StillWater'), 'Charm': ('ShieldbreakBurst', 'SharedWard', 'GleamingWard', 'CoStar', 'TriumphSong', 'WatchfulHand', 'KindnessReturns', 'RelayHand', 'UmbralHeritage', 'ElementalHarvest', 'PackFeast', 'VanguardsOath', 'Medley', 'BareHandedPride', 'AceInHand', 'CrystalCircuit', 'ShardBoon', 'Lifeline', 'DreamOmen', 'RearguardsWay', 'ReadyGuard', 'Apothecary', 'SpilloverStrike', 'Resonance', 'Tailwind', 'SecondWind', 'Shatter', 'Frost', 'Umbra', 'Convergence', 'Steam', 'Eclipse', 'Cinder', 'FrostCrystal', 'SoulSiphon', 'Whirlwind', 'StarShield', 'Sprint', 'Vigor', 'Overload', 'Devotion', 'CrystalResonance', 'PreyPride', 'SpendersWard', 'LucidBoon'), 'Head': ('GleamingWard', 'CoStar', 'FocusFire', 'DuelistsWay', 'ImmovableStance', 'StardustCycle', 'PrismShift', 'PackFeast', 'Medley', 'OpeningSalvo', 'PileOn', 'AceInHand', 'CrystalCircuit', 'Lifeline', 'DreamOmen', 'RearguardsWay', 'PilingLuck', 'ReturningBlade', 'Eclipse', 'FrostCrystal', 'Overload', 'UltimateSurge', 'StarShield', 'Resonance', 'SecondWind', 'Radiance', 'Umbra', 'Barrier', 'Vigor', 'Bloodlust', 'Finale', 'CrystalResonance', 'Devotion', 'LucidBoon'), 'Hands': ('ShieldBash', 'KindnessReturns', 'WanderersEdge', 'FocusFire', 'DuelistsWay', 'RunUp', 'StrafeShot', 'RelayHand', 'StardustCycle', 'UmbralHeritage', 'BrittleIce', 'ElementalHarvest', 'PrismShift', 'DeathBloom', 'Spellsweep', 'BareHandedPride', 'PileOn', 'PilingLuck', 'WeakPointWound', 'CritSplash', 'ReturningBlade', 'Apothecary', 'SpilloverStrike', 'Steam', 'Cinder', 'Executioner', 'Blaze', 'ChainLightning', 'Ember', 'Frost', 'Frenzy', 'Lifesteal', 'OpeningStrike', 'Shatter', 'Momentum', 'CriticalEcho', 'Fetters', 'Wildfire', 'StillWater', 'Overload', 'Finale'), 'Feet': ('UnbowedMind', 'ShieldbreakBurst', 'WatchfulHand', 'Breakout', 'ImmovableStance', 'RunUp', 'StrafeShot', 'VanguardsOath', 'DeathBloom', 'ShardBoon', 'TollOfGrudge', 'ReadyGuard', 'Sprint', 'Tailwind', 'Whirlwind', 'EchoingDodge', 'Momentum', 'Aegis', 'Bulwark', 'Thorns', 'SoulSiphon', 'Retaliation', 'PreyPride', 'OverflowingLife', 'PerfectRead')}

CAP_KEYS = {'power': ('Momentum', 'Retaliation', 'Bulwark', 'Lifesteal', 'Thorns', 'Executioner', 'Resonance', 'Tailwind', 'Barrier', 'SecondWind', 'Blaze', 'ChainLightning', 'Shatter', 'Aegis', 'Bloodlust', 'Ember', 'Frost', 'Radiance', 'Umbra', 'Convergence', 'EchoingDodge', 'UltimateSurge', 'SoulSiphon', 'Whirlwind', 'Frenzy', 'OpeningStrike', 'StarShield', 'Sprint', 'Vigor', 'Overload', 'Finale', 'CriticalEcho', 'Fetters', 'CrystalResonance', 'PreyPride', 'OverflowingLife', 'Devotion', 'Wildfire', 'StillWater', 'SpendersWard', 'PerfectRead', 'LucidBoon', 'ShadowStep', 'Steam', 'Eclipse', 'Cinder', 'FrostCrystal', 'ShieldbreakBurst', 'SharedWard', 'ShieldBash', 'GleamingWard', 'CoStar', 'TriumphSong', 'WatchfulHand', 'KindnessReturns', 'WanderersEdge', 'FocusFire', 'Breakout', 'DuelistsWay', 'ImmovableStance', 'RunUp', 'StrafeShot', 'RelayHand', 'StardustCycle', 'UmbralHeritage', 'BrittleIce', 'ElementalHarvest', 'PrismShift', 'PackFeast', 'VanguardsOath', 'DeathBloom', 'Medley', 'Spellsweep', 'BareHandedPride', 'OpeningSalvo', 'PileOn', 'AceInHand', 'CrystalCircuit', 'ShardBoon', 'Lifeline', 'DreamOmen', 'RearguardsWay', 'TollOfGrudge', 'UnbowedMind', 'PilingLuck', 'WeakPointWound', 'CritSplash', 'ReturningBlade', 'ReadyGuard', 'Apothecary', 'SpilloverStrike', 'KillGoldPct', 'EliteKillGoldPct', 'DreamDustPct', 'DreamDustDelvePct'), 'stat': ('AttackPct', 'PowerPct', 'AttackFlat', 'PowerFlat', 'AttackSpeedPct', 'CritChancePct', 'CritDamagePct', 'MaxHealthPct', 'MaxHealthFlat', 'Armor', 'HealthRegen', 'Haste', 'MoveSpeedPct', 'Tenacity', 'FireAmp', 'ColdAmp', 'LightAmp', 'DarkAmp', 'AttackRangePct', 'FourthAttackShift', 'EssenceSlotIdentity', 'EssenceSlotMovement', 'HealPower', 'ShieldPower', 'SummonPower', 'SacrificeReduction')}

LEGACY_IDENTITIES = {'affixes': '294-f5113161bf862012', 'power-pools': '414-01da29ae92e8ca89', 'caps': '121-883c0ce6e8fd9cf8'}

def _keys(value, expected, path):
    if not isinstance(value, dict) or set(value) != set(expected):
        raise ValueError(f"{path}: expected exactly {', '.join(expected)}")


def _integer(value, path, positive=False):
    if type(value) is not int or not (1 if positive else 0) <= value <= INT32_MAX:
        raise ValueError(f"{path}: expected {'positive' if positive else 'nonnegative'} Int32")


def validate_pool(table, domain, path):
    keys = AFFIX_KEYS if domain == "affixes" else POWER_POOL_KEYS
    fields = ("min", "max", "weight") if domain == "affixes" else ("min", "max")
    _keys(table, ("schemaVersion", "pools"), path)
    if type(table["schemaVersion"]) is not int or table["schemaVersion"] != 1:
        raise ValueError(f"{path}/schemaVersion: expected integer 1")
    _keys(table["pools"], keys, f"{path}/pools")
    for slot, expected in keys.items():
        rows = table["pools"][slot]
        prefix = f"{path}/pools/{slot}"
        _keys(rows, expected, prefix)
        for key in expected:
            row = rows[key]
            _keys(row, fields, f"{prefix}/{key}")
            for field in fields:
                _integer(row[field], f"{prefix}/{key}/{field}", positive=field == "weight")
            if row["min"] > row["max"]:
                raise ValueError(f"{prefix}/{key}: min must not exceed max")
        if domain == "affixes" and sum(row["weight"] for row in rows.values()) > INT32_MAX // 2:
            raise ValueError(f"{prefix}: doubled family weights exceed Int32")
    return table


def validate_caps(table, path):
    _keys(table, ("schemaVersion", "power", "stat"), path)
    if type(table["schemaVersion"]) is not int or table["schemaVersion"] != 1:
        raise ValueError(f"{path}/schemaVersion: expected integer 1")
    for kind, expected in CAP_KEYS.items():
        _keys(table[kind], expected, f"{path}/{kind}")
        for key in expected:
            _integer(table[kind][key], f"{path}/{kind}/{key}")
    return table


def load_affixes(path=None):
    path = TABLE_DIR / "affixes.json" if path is None else Path(path)
    return validate_pool(load_json(path), "affixes", path)


def load_power_pools(path=None):
    path = TABLE_DIR / "power-pools.json" if path is None else Path(path)
    return validate_pool(load_json(path), "power-pools", path)


def load_caps(path=None):
    path = TABLE_DIR / "caps.json" if path is None else Path(path)
    return validate_caps(load_json(path), path)


def pool_records(table, kind):
    return [f"{slot}/{kind}.{key}/{field}:int32:{'weight' if field == 'weight' else 'value'}={value}"
            for slot, rows in table["pools"].items() for key, row in rows.items() for field, value in row.items()]


def cap_records(table):
    return [f"{kind}/{key}/cap:int32:value={value}"
            for kind in ("power", "stat") for key, value in table[kind].items()]


def _render_class(name, domain, constants, records):
    lines = [f"// Generated from tools/balance/equipment/{domain}.json. Do not edit.",
             "namespace SodRpg.Core.Game", "{", f"    internal static class {name}", "    {"]
    lines.extend(f"        internal const int {key} = {value};" for key, value in constants)
    effective_records = fingerprint_records(domain, records, LEGACY_IDENTITIES[domain])
    lines.append("")
    if effective_records:
        lines.extend(["        internal static readonly string[] ContentFingerprintRecords = new string[]", "        {"])
        lines.extend("            " + quote(record) + "," for record in effective_records)
        lines.append("        };")
    else:
        lines.append("        internal static readonly string[] ContentFingerprintRecords = System.Array.Empty<string>();")
    lines.extend(["    }", "}", ""])
    return "\n".join(lines)


def render_outputs(affixes=None, power_pools=None, caps=None):
    affixes = load_affixes() if affixes is None else validate_pool(affixes, "affixes", "affixes")
    power_pools = load_power_pools() if power_pools is None else validate_pool(power_pools, "power-pools", "power-pools")
    caps = load_caps() if caps is None else validate_caps(caps, "caps")
    outputs = {}
    for domain, name, table, keys, kind in (
            ("affixes", "AffixPoolsBalance", affixes, AFFIX_KEYS, "Stat"),
            ("power-pools", "PowerPoolsBalance", power_pools, POWER_POOL_KEYS, "Power")):
        fields = ("min", "max", "weight") if domain == "affixes" else ("min", "max")
        constants = [(f"{slot}_{key}_{field.title()}", table["pools"][slot][key][field])
                     for slot, expected in keys.items() for key in expected for field in fields]
        outputs[OUTPUT_DIR / (name + ".Generated.cs")] = _render_class(name, domain, constants, pool_records(table, kind))
    constants = [(f"{kind.title()}_{key}", caps[kind][key]) for kind, expected in CAP_KEYS.items() for key in expected]
    outputs[OUTPUT_DIR / "EquipmentCapsBalance.Generated.cs"] = _render_class("EquipmentCapsBalance", "caps", constants, cap_records(caps))
    spec = importlib.util.spec_from_file_location("equipment_named_generator", ROOT / "tools/lowrarity/gen_named_cs.py")
    named_generator = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(named_generator)
    outputs.update(named_generator.render_outputs(power_pools=power_pools))
    return outputs

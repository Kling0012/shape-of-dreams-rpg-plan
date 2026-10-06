"""Compile pacts numeric definitions without publishing generated files."""
from decimal import Decimal
import json
import math
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/pacts.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Pacts.Generated.cs"
# Frozen compatibility reference, never an editable source or runtime fallback.
LEGACY = json.loads(r'''{
  "schemaVersion": 1,
  "offered": 3,
  "doubleDepthBonusMultiplier": 2,
  "definitions": {
    "GlassHeart": {
      "curseStrength": 1,
      "dropBonus": 0.4
    },
    "DullBlade": {
      "curseStrength": 1,
      "luck": 0.6
    },
    "Unguarded": {
      "curseStrength": 1,
      "shardMult": 1.5
    },
    "LeadenFeet": {
      "curseStrength": 1,
      "xpMult": 1.5
    },
    "Frenzy": {
      "curseStrength": 2,
      "boons": {
        "AttackPct": 15,
        "PowerPct": 15
      }
    },
    "CursedHoard": {
      "curseStrength": 1
    },
    "DryDream": {
      "curseStrength": 2,
      "tuningOnElite": 1
    },
    "Burden": {
      "curseStrength": 3,
      "luck": 1.4
    },
    "Glutton": {
      "curseStrength": 2,
      "dropBonus": 0.6
    },
    "Scholar": {
      "curseStrength": 1,
      "xpMult": 1.3,
      "luck": 0.3
    },
    "Gambler": {
      "curseStrength": 2,
      "shardMult": 2.0
    },
    "AbyssEye": {
      "curseStrength": 3,
      "dropBonus": 0.5,
      "luck": 0.8,
      "tuningOnElite": 1
    },
    "BloodPrice": {
      "curseStrength": 2,
      "boons": {
        "AttackSpeedPct": 15
      }
    },
    "IronOath": {
      "curseStrength": 1,
      "boons": {
        "Armor": 10
      }
    },
    "HollowCrown": {
      "curseStrength": 3,
      "dropBonus": 0.8,
      "shardMult": 1.5
    },
    "ThiefsBargain": {
      "curseStrength": 1,
      "shardMult": 1.3,
      "dropBonus": 0.2
    },
    "Stargazer": {
      "curseStrength": 1,
      "luck": 0.4,
      "dropBonus": 0.15
    },
    "Wanderer": {
      "curseStrength": 2,
      "xpMult": 1.5,
      "dropBonus": 0.3
    },
    "Miser": {
      "curseStrength": 2,
      "shardMult": 1.3
    },
    "BloodMoon": {
      "curseStrength": 3,
      "boons": {
        "AttackPct": 20,
        "PowerPct": 20
      }
    },
    "SaltOath": {
      "curseStrength": 2,
      "shardMult": 1.4,
      "luck": 0.5
    },
    "BlankMap": {
      "curseStrength": 2,
      "tuningOnElite": 2
    },
    "HoneyedChains": {
      "curseStrength": 2,
      "xpMult": 1.8
    },
    "StardustDebt": {
      "curseStrength": 3,
      "dropBonus": 1.0
    },
    "BoneDice": {
      "curseStrength": 3,
      "luck": 2.0
    },
    "NightOfVeils": {
      "curseStrength": 1,
      "boons": {
        "DarkAmp": 12
      }
    },
    "OathOfDawn": {
      "curseStrength": 1,
      "boons": {
        "LightAmp": 12
      }
    },
    "AshenChalice": {
      "curseStrength": 2,
      "boons": {
        "MaxHealthFlat": 50
      }
    },
    "CrackedMirror": {
      "curseStrength": 3,
      "boons": {
        "CritChancePct": 4
      }
    },
    "ClangoringHeart": {
      "curseStrength": 2,
      "boons": {
        "AttackSpeedPct": 10
      }
    },
    "LayeredLamps": {
      "curseStrength": 2,
      "boons": {
        "ShieldPower": 12
      }
    },
    "MossboundPact": {
      "curseStrength": 1,
      "boons": {
        "HealthRegen": 4
      }
    },
    "PuppetStrings": {
      "curseStrength": 2,
      "boons": {
        "SummonPower": 20
      }
    },
    "GraciousRain": {
      "curseStrength": 1,
      "boons": {
        "HealPower": 12
      }
    },
    "StagnantSpring": {
      "curseStrength": 2,
      "shardMult": 1.75
    },
    "HourglassLie": {
      "curseStrength": 2,
      "xpMult": 1.6
    },
    "LongWayRound": {
      "curseStrength": 2,
      "xpMult": 1.4,
      "dropBonus": 0.25
    },
    "ShellBargain": {
      "curseStrength": 1,
      "dropBonus": 0.3,
      "shardMult": 1.2
    },
    "DeepmirePromise": {
      "curseStrength": 3,
      "tuningOnElite": 1
    },
    "GildedWound": {
      "curseStrength": 3,
      "boons": {
        "AttackFlat": 10,
        "PowerFlat": 10
      }
    }
  }
}''', parse_float=Decimal)
# Closed numeric schema: names and enum/definition relationships stay in Core.
FIELDS = {
    'offered': ('Offered', 'int', 'choices'),
    'definitions.GlassHeart.curseStrength': ('GlassHeartCurseStrength', 'int', 'curse-strength'),
    'definitions.GlassHeart.dropBonus': ('GlassHeartDropBonus', 'double', 'ratio'),
    'definitions.DullBlade.curseStrength': ('DullBladeCurseStrength', 'int', 'curse-strength'),
    'definitions.DullBlade.luck': ('DullBladeLuck', 'double', 'luck'),
    'definitions.Unguarded.curseStrength': ('UnguardedCurseStrength', 'int', 'curse-strength'),
    'definitions.Unguarded.shardMult': ('UnguardedShardMult', 'double', 'multiplier'),
    'definitions.LeadenFeet.curseStrength': ('LeadenFeetCurseStrength', 'int', 'curse-strength'),
    'definitions.LeadenFeet.xpMult': ('LeadenFeetXpMult', 'double', 'multiplier'),
    'definitions.Frenzy.curseStrength': ('FrenzyCurseStrength', 'int', 'curse-strength'),
    'definitions.Frenzy.boons.AttackPct': ('FrenzyBoonAttackPct', 'int', 'percent'),
    'definitions.Frenzy.boons.PowerPct': ('FrenzyBoonPowerPct', 'int', 'percent'),
    'definitions.CursedHoard.curseStrength': ('CursedHoardCurseStrength', 'int', 'curse-strength'),
    'definitions.DryDream.curseStrength': ('DryDreamCurseStrength', 'int', 'curse-strength'),
    'definitions.DryDream.tuningOnElite': ('DryDreamTuningOnElite', 'int', 'tuning/elite-or-boss'),
    'definitions.Burden.curseStrength': ('BurdenCurseStrength', 'int', 'curse-strength'),
    'definitions.Burden.luck': ('BurdenLuck', 'double', 'luck'),
    'definitions.Glutton.curseStrength': ('GluttonCurseStrength', 'int', 'curse-strength'),
    'definitions.Glutton.dropBonus': ('GluttonDropBonus', 'double', 'ratio'),
    'definitions.Scholar.curseStrength': ('ScholarCurseStrength', 'int', 'curse-strength'),
    'definitions.Scholar.xpMult': ('ScholarXpMult', 'double', 'multiplier'),
    'definitions.Scholar.luck': ('ScholarLuck', 'double', 'luck'),
    'definitions.Gambler.curseStrength': ('GamblerCurseStrength', 'int', 'curse-strength'),
    'definitions.Gambler.shardMult': ('GamblerShardMult', 'double', 'multiplier'),
    'definitions.AbyssEye.curseStrength': ('AbyssEyeCurseStrength', 'int', 'curse-strength'),
    'definitions.AbyssEye.dropBonus': ('AbyssEyeDropBonus', 'double', 'ratio'),
    'definitions.AbyssEye.luck': ('AbyssEyeLuck', 'double', 'luck'),
    'definitions.AbyssEye.tuningOnElite': ('AbyssEyeTuningOnElite', 'int', 'tuning/elite-or-boss'),
    'definitions.BloodPrice.curseStrength': ('BloodPriceCurseStrength', 'int', 'curse-strength'),
    'definitions.BloodPrice.boons.AttackSpeedPct': ('BloodPriceBoonAttackSpeedPct', 'int', 'percent'),
    'definitions.IronOath.curseStrength': ('IronOathCurseStrength', 'int', 'curse-strength'),
    'definitions.IronOath.boons.Armor': ('IronOathBoonArmor', 'int', 'armor'),
    'definitions.HollowCrown.curseStrength': ('HollowCrownCurseStrength', 'int', 'curse-strength'),
    'definitions.HollowCrown.dropBonus': ('HollowCrownDropBonus', 'double', 'ratio'),
    'definitions.HollowCrown.shardMult': ('HollowCrownShardMult', 'double', 'multiplier'),
    'definitions.ThiefsBargain.curseStrength': ('ThiefsBargainCurseStrength', 'int', 'curse-strength'),
    'definitions.ThiefsBargain.shardMult': ('ThiefsBargainShardMult', 'double', 'multiplier'),
    'definitions.ThiefsBargain.dropBonus': ('ThiefsBargainDropBonus', 'double', 'ratio'),
    'definitions.Stargazer.curseStrength': ('StargazerCurseStrength', 'int', 'curse-strength'),
    'definitions.Stargazer.luck': ('StargazerLuck', 'double', 'luck'),
    'definitions.Stargazer.dropBonus': ('StargazerDropBonus', 'double', 'ratio'),
    'definitions.Wanderer.curseStrength': ('WandererCurseStrength', 'int', 'curse-strength'),
    'definitions.Wanderer.xpMult': ('WandererXpMult', 'double', 'multiplier'),
    'definitions.Wanderer.dropBonus': ('WandererDropBonus', 'double', 'ratio'),
    'definitions.Miser.curseStrength': ('MiserCurseStrength', 'int', 'curse-strength'),
    'definitions.Miser.shardMult': ('MiserShardMult', 'double', 'multiplier'),
    'definitions.BloodMoon.curseStrength': ('BloodMoonCurseStrength', 'int', 'curse-strength'),
    'definitions.BloodMoon.boons.AttackPct': ('BloodMoonBoonAttackPct', 'int', 'percent'),
    'definitions.BloodMoon.boons.PowerPct': ('BloodMoonBoonPowerPct', 'int', 'percent'),
    'definitions.SaltOath.curseStrength': ('SaltOathCurseStrength', 'int', 'curse-strength'),
    'definitions.SaltOath.shardMult': ('SaltOathShardMult', 'double', 'multiplier'),
    'definitions.SaltOath.luck': ('SaltOathLuck', 'double', 'luck'),
    'definitions.BlankMap.curseStrength': ('BlankMapCurseStrength', 'int', 'curse-strength'),
    'definitions.BlankMap.tuningOnElite': ('BlankMapTuningOnElite', 'int', 'tuning/elite-or-boss'),
    'definitions.HoneyedChains.curseStrength': ('HoneyedChainsCurseStrength', 'int', 'curse-strength'),
    'definitions.HoneyedChains.xpMult': ('HoneyedChainsXpMult', 'double', 'multiplier'),
    'definitions.StardustDebt.curseStrength': ('StardustDebtCurseStrength', 'int', 'curse-strength'),
    'definitions.StardustDebt.dropBonus': ('StardustDebtDropBonus', 'double', 'ratio'),
    'definitions.BoneDice.curseStrength': ('BoneDiceCurseStrength', 'int', 'curse-strength'),
    'definitions.BoneDice.luck': ('BoneDiceLuck', 'double', 'luck'),
    'definitions.NightOfVeils.curseStrength': ('NightOfVeilsCurseStrength', 'int', 'curse-strength'),
    'definitions.NightOfVeils.boons.DarkAmp': ('NightOfVeilsBoonDarkAmp', 'int', 'percent'),
    'definitions.OathOfDawn.curseStrength': ('OathOfDawnCurseStrength', 'int', 'curse-strength'),
    'definitions.OathOfDawn.boons.LightAmp': ('OathOfDawnBoonLightAmp', 'int', 'percent'),
    'definitions.AshenChalice.curseStrength': ('AshenChaliceCurseStrength', 'int', 'curse-strength'),
    'definitions.AshenChalice.boons.MaxHealthFlat': ('AshenChaliceBoonMaxHealthFlat', 'int', 'health'),
    'definitions.CrackedMirror.curseStrength': ('CrackedMirrorCurseStrength', 'int', 'curse-strength'),
    'definitions.CrackedMirror.boons.CritChancePct': ('CrackedMirrorBoonCritChancePct', 'int', 'percent'),
    'definitions.ClangoringHeart.curseStrength': ('ClangoringHeartCurseStrength', 'int', 'curse-strength'),
    'definitions.ClangoringHeart.boons.AttackSpeedPct': ('ClangoringHeartBoonAttackSpeedPct', 'int', 'percent'),
    'definitions.LayeredLamps.curseStrength': ('LayeredLampsCurseStrength', 'int', 'curse-strength'),
    'definitions.LayeredLamps.boons.ShieldPower': ('LayeredLampsBoonShieldPower', 'int', 'percent'),
    'definitions.MossboundPact.curseStrength': ('MossboundPactCurseStrength', 'int', 'curse-strength'),
    'definitions.MossboundPact.boons.HealthRegen': ('MossboundPactBoonHealthRegen', 'int', 'health/second'),
    'definitions.PuppetStrings.curseStrength': ('PuppetStringsCurseStrength', 'int', 'curse-strength'),
    'definitions.PuppetStrings.boons.SummonPower': ('PuppetStringsBoonSummonPower', 'int', 'percent'),
    'definitions.GraciousRain.curseStrength': ('GraciousRainCurseStrength', 'int', 'curse-strength'),
    'definitions.GraciousRain.boons.HealPower': ('GraciousRainBoonHealPower', 'int', 'percent'),
    'definitions.StagnantSpring.curseStrength': ('StagnantSpringCurseStrength', 'int', 'curse-strength'),
    'definitions.StagnantSpring.shardMult': ('StagnantSpringShardMult', 'double', 'multiplier'),
    'definitions.HourglassLie.curseStrength': ('HourglassLieCurseStrength', 'int', 'curse-strength'),
    'definitions.HourglassLie.xpMult': ('HourglassLieXpMult', 'double', 'multiplier'),
    'definitions.LongWayRound.curseStrength': ('LongWayRoundCurseStrength', 'int', 'curse-strength'),
    'definitions.LongWayRound.xpMult': ('LongWayRoundXpMult', 'double', 'multiplier'),
    'definitions.LongWayRound.dropBonus': ('LongWayRoundDropBonus', 'double', 'ratio'),
    'definitions.ShellBargain.curseStrength': ('ShellBargainCurseStrength', 'int', 'curse-strength'),
    'definitions.ShellBargain.dropBonus': ('ShellBargainDropBonus', 'double', 'ratio'),
    'definitions.ShellBargain.shardMult': ('ShellBargainShardMult', 'double', 'multiplier'),
    'definitions.DeepmirePromise.curseStrength': ('DeepmirePromiseCurseStrength', 'int', 'curse-strength'),
    'definitions.DeepmirePromise.tuningOnElite': ('DeepmirePromiseTuningOnElite', 'int', 'tuning/elite-or-boss'),
    'definitions.GildedWound.curseStrength': ('GildedWoundCurseStrength', 'int', 'curse-strength'),
    'definitions.GildedWound.boons.AttackFlat': ('GildedWoundBoonAttackFlat', 'int', 'attack'),
    'definitions.GildedWound.boons.PowerFlat': ('GildedWoundBoonPowerFlat', 'int', 'power'),
    'doubleDepthBonusMultiplier': ('DoubleDepthBonusMultiplier', 'int', 'multiplier'),
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="pacts"):
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
        raise ValueError("pacts.schemaVersion: expected integer 1")
    for path, (_, kind, _) in FIELDS.items():
        value = _get(data, path)
        if kind == "int":
            if type(value) is not int or not 0 <= value <= 2147483647:
                raise ValueError(f"pacts.{path}: expected nonnegative Int32 (not boolean)")
        else:
            try:
                adopted = float(value)
            except OverflowError as exc:
                raise ValueError(f"pacts.{path}: exceeds double range") from exc
            if value < 0 or not math.isfinite(adopted) or (value != 0 and adopted == 0):
                raise ValueError(f"pacts.{path}: expected a nonnegative finite representable double")
            if Decimal(repr(adopted)) != Decimal(value):
                raise ValueError(f"pacts.{path}: double precision would change the authored value")
    if not 1 <= data["offered"] <= 40:
        raise ValueError("pacts.offered: expected 1..40 (definition count)")
    for name, row in data["definitions"].items():
        if not 1 <= row["curseStrength"] <= 3:
            raise ValueError(f"pacts.definitions.{name}.curseStrength: expected 1..3 (native curse tiers)")
    divisor = read_json(ROOT / "tools/balance/economy.json")["expedition"]["secureBonusDivisor"]
    if type(divisor) is not int or divisor < 1:
        raise ValueError("economy.expedition.secureBonusDivisor: expected positive Int32")
    # Secure uses Int64 before saturation; five is the existing maximum heat.
    if ((2147483647 * 5) // divisor) * data["doubleDepthBonusMultiplier"] > 9223372036854775807:
        raise ValueError("pacts.doubleDepthBonusMultiplier: secure bonus product exceeds Int64")
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
    return "balance:pacts:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/pacts.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class PactBalance", "    {"]
    for path, (name, kind, _) in FIELDS.items():
        literal = _literal(_get(data, path))
        if kind == "double":
            literal += "d"
        lines.append(f"        internal const {kind} {name} = {literal};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}

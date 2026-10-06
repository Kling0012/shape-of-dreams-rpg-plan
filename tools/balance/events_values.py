"""Compile dream-event balance; stage source without publishing it."""
import json
from decimal import Decimal
from pathlib import Path

from star_values import read_json

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools/balance/events.json"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/Events.Generated.cs"
INT_MAX = (1 << 31) - 1
# Frozen compatibility reference, never an editable source or runtime fallback.
LEGACY = {
    "schemaVersion": 1,
    "offerChance": Decimal('0.5'),
    "stargazerDropBonus": Decimal('0.3'),
    "luckyStarLuck": Decimal('0.4'),
    "merchant": {'baseShards': 50, 'shardsPerHeat': 10},
    "twinMirror": {'shards': 30, 'epicShards': 60},
    "chalice": {'winChance': Decimal('0.5'), 'bonusMultiplier': 1},
    "cauldron": {'relicCount': 3},
    "tapir": {'relicsPerTuning': 3},
    "courageGate": {'heatIncrement': 1, 'shards': 40},
    "archive": {'baseXp': 40, 'xpPerHeat': 20},
    "memoryWell": {'tuning': 1, 'epicTuning': 2},
    "shadowExchange": {'shards': 25, 'epicShards': 50},
    "lostMausoleum": {'shards': 60},
    "relicWager": {'rareWinChance': Decimal('0.2'), 'lowerWinChance': Decimal('0.5')},
    "stoneBroker": {'shards': 35, 'tuning': 2},
    "shardKiln": {'tuning': 2, 'shards': 45},
    "starOffering": {'xp': 40},
    "dreamOffering": {'baseXp': 40, 'xpPerHeat': 20},
    "abyssalChest": {'shards': 75, 'heatIncrement': 1},
    "sealedVault": {'shards': 40},
    "offerWeights": {
        "Merchant": 8,
        "Fountain": 8,
        "Chalice": 8,
        "Lantern": 8,
        "ForgeShrine": 8,
        "TwinMirror": 8,
        "Stargazer": 8,
        "Cauldron": 8,
        "Tapir": 8,
        "CourageGate": 8,
        "Archive": 8,
        "LuckyStar": 8,
        "MemoryWell": 6,
        "ShadowExchange": 8,
        "LostMausoleum": 5,
        "RelicWager": 6,
        "TemperingAltar": 6,
        "StoneBroker": 8,
        "ShardKiln": 8,
        "StarOffering": 6,
        "DreamOffering": 6,
        "AbyssalChest": 5,
        "RelicExchange": 8,
        "SealedVault": 5,
        "PowerCrucible": 5,
    },
}
FIELDS = {
    "offerChance": ('OfferChance', 'double', 'probability'),
    "stargazerDropBonus": ('StargazerDropBonus', 'double', 'drop-rate'),
    "luckyStarLuck": ('LuckyStarLuck', 'double', 'luck'),
    "merchant.baseShards": ('MerchantBaseShards', 'int', 'shards'),
    "merchant.shardsPerHeat": ('MerchantShardsPerHeat', 'int', 'shards/heat'),
    "twinMirror.shards": ('TwinMirrorShards', 'int', 'shards'),
    "twinMirror.epicShards": ('TwinMirrorEpicShards', 'int', 'shards'),
    "chalice.winChance": ('ChaliceWinChance', 'double', 'probability'),
    "chalice.bonusMultiplier": ('ChaliceBonusMultiplier', 'int', 'multiplier'),
    "cauldron.relicCount": ('CauldronRelicCount', 'int', 'relics'),
    "tapir.relicsPerTuning": ('TapirRelicsPerTuning', 'int', 'relics'),
    "courageGate.heatIncrement": ('CourageGateHeatIncrement', 'int', 'heat'),
    "courageGate.shards": ('CourageGateShards', 'int', 'shards'),
    "archive.baseXp": ('ArchiveBaseXp', 'int', 'dream-xp'),
    "archive.xpPerHeat": ('ArchiveXpPerHeat', 'int', 'dream-xp/heat'),
    "memoryWell.tuning": ('MemoryWellTuning', 'int', 'tuning'),
    "memoryWell.epicTuning": ('MemoryWellEpicTuning', 'int', 'tuning'),
    "shadowExchange.shards": ('ShadowExchangeShards', 'int', 'shards'),
    "shadowExchange.epicShards": ('ShadowExchangeEpicShards', 'int', 'shards'),
    "lostMausoleum.shards": ('LostMausoleumShards', 'int', 'shards'),
    "relicWager.rareWinChance": ('RelicWagerRareWinChance', 'double', 'probability'),
    "relicWager.lowerWinChance": ('RelicWagerLowerWinChance', 'double', 'probability'),
    "stoneBroker.shards": ('StoneBrokerShards', 'int', 'shards'),
    "stoneBroker.tuning": ('StoneBrokerTuning', 'int', 'tuning'),
    "shardKiln.tuning": ('ShardKilnTuning', 'int', 'tuning'),
    "shardKiln.shards": ('ShardKilnShards', 'int', 'shards'),
    "starOffering.xp": ('StarOfferingXp', 'int', 'star-xp'),
    "dreamOffering.baseXp": ('DreamOfferingBaseXp', 'int', 'dream-xp'),
    "dreamOffering.xpPerHeat": ('DreamOfferingXpPerHeat', 'int', 'dream-xp/heat'),
    "abyssalChest.shards": ('AbyssalChestShards', 'int', 'shards'),
    "abyssalChest.heatIncrement": ('AbyssalChestHeatIncrement', 'int', 'heat'),
    "sealedVault.shards": ('SealedVaultShards', 'int', 'shards'),
    "offerWeights.Merchant": ('OfferWeightsMerchant', 'int', 'weight'),
    "offerWeights.Fountain": ('OfferWeightsFountain', 'int', 'weight'),
    "offerWeights.Chalice": ('OfferWeightsChalice', 'int', 'weight'),
    "offerWeights.Lantern": ('OfferWeightsLantern', 'int', 'weight'),
    "offerWeights.ForgeShrine": ('OfferWeightsForgeShrine', 'int', 'weight'),
    "offerWeights.TwinMirror": ('OfferWeightsTwinMirror', 'int', 'weight'),
    "offerWeights.Stargazer": ('OfferWeightsStargazer', 'int', 'weight'),
    "offerWeights.Cauldron": ('OfferWeightsCauldron', 'int', 'weight'),
    "offerWeights.Tapir": ('OfferWeightsTapir', 'int', 'weight'),
    "offerWeights.CourageGate": ('OfferWeightsCourageGate', 'int', 'weight'),
    "offerWeights.Archive": ('OfferWeightsArchive', 'int', 'weight'),
    "offerWeights.LuckyStar": ('OfferWeightsLuckyStar', 'int', 'weight'),
    "offerWeights.MemoryWell": ('OfferWeightsMemoryWell', 'int', 'weight'),
    "offerWeights.ShadowExchange": ('OfferWeightsShadowExchange', 'int', 'weight'),
    "offerWeights.LostMausoleum": ('OfferWeightsLostMausoleum', 'int', 'weight'),
    "offerWeights.RelicWager": ('OfferWeightsRelicWager', 'int', 'weight'),
    "offerWeights.TemperingAltar": ('OfferWeightsTemperingAltar', 'int', 'weight'),
    "offerWeights.StoneBroker": ('OfferWeightsStoneBroker', 'int', 'weight'),
    "offerWeights.ShardKiln": ('OfferWeightsShardKiln', 'int', 'weight'),
    "offerWeights.StarOffering": ('OfferWeightsStarOffering', 'int', 'weight'),
    "offerWeights.DreamOffering": ('OfferWeightsDreamOffering', 'int', 'weight'),
    "offerWeights.AbyssalChest": ('OfferWeightsAbyssalChest', 'int', 'weight'),
    "offerWeights.RelicExchange": ('OfferWeightsRelicExchange', 'int', 'weight'),
    "offerWeights.SealedVault": ('OfferWeightsSealedVault', 'int', 'weight'),
    "offerWeights.PowerCrucible": ('OfferWeightsPowerCrucible', 'int', 'weight'),
}


def _get(data, path):
    for key in path.split("."):
        data = data[key]
    return data


def _shape(data, reference, path="events"):
    if isinstance(reference, dict):
        if type(data) is not dict or set(data) != set(reference):
            raise ValueError(f"{path}: expected exactly {', '.join(reference)}")
        for key in reference:
            _shape(data[key], reference[key], f"{path}.{key}")
    elif isinstance(reference, Decimal):
        if type(data) not in (int, Decimal) or not Decimal(data).is_finite() or data < 0:
            raise ValueError(f"{path}: expected finite nonnegative double (not boolean)")
        if FIELDS[path.removeprefix("events.")][2] == "probability" and data > 1:
            raise ValueError(f"{path}: expected probability in 0..1")
        effective = float(data)
        if not Decimal(str(effective)).is_finite() or Decimal(str(effective)) != Decimal(data):
            raise ValueError(f"{path}: value must round-trip through finite double")
    elif type(data) is not int or not 0 <= data <= INT_MAX:
        raise ValueError(f"{path}: expected nonnegative Int32 (not boolean)")


def validate(data):
    _shape(data, LEGACY)
    if data["schemaVersion"] != 1:
        raise ValueError("events.schemaVersion: expected integer 1")
    for event in ("merchant", "archive", "dreamOffering"):
        base, slope = ("baseShards", "shardsPerHeat") if event == "merchant" else ("baseXp", "xpPerHeat")
        if data[event][base] + 5 * data[event][slope] > INT_MAX:
            raise ValueError(f"events.{event}: maximum heat result exceeds Int32")
    if not 1 <= data["cauldron"]["relicCount"] <= 30:
        raise ValueError("events.cauldron.relicCount: expected 1..30 (existing base satchel capacity)")
    if data["tapir"]["relicsPerTuning"] < 1:
        raise ValueError("events.tapir.relicsPerTuning: expected positive divisor")
    for event in ("courageGate", "abyssalChest"):
        if not 1 <= data[event]["heatIncrement"] <= 5:
            raise ValueError(f"events.{event}.heatIncrement: expected 1..5 (existing heat limit)")
    # Bet multiplication precedes the existing reward limiter; every accepted
    # Int32 satchel balance must remain representable without changing arithmetic.
    if data["chalice"]["bonusMultiplier"] > 1:
        raise ValueError("events.chalice.bonusMultiplier: expected 0..1 (Int32 bet multiplication safety)")
    if sum(data["offerWeights"].values()) > INT_MAX:
        raise ValueError("events.offerWeights: total exceeds Int32")
    return data


def load(path=PATH):
    return validate(read_json(path))


def _literal(value):
    if isinstance(value, Decimal):
        if value == 0:
            return "0"
        text = format(value, "f")
        return text.rstrip("0").rstrip(".") if "." in text else text
    return str(value)


def fingerprint_record(data):
    validate(data)
    records = []
    for path, (_, kind, unit) in FIELDS.items():
        value = _get(data, path)
        if value != _get(LEGACY, path):
            records.append(f"{path.replace('.', '/')}:{kind}:{unit}:{_literal(value)}")
    return "balance:events:v1:" + ";".join(records) if records else None


def render_outputs(data=None):
    data = load() if data is None else validate(data)
    lines = ["// <auto-generated />",
             "// Source: tools/balance/events.json; regenerate with python tools/balance/gen_cs.py.",
             "namespace SodRpg.Core.Game", "{", "    internal static class EventsBalance", "    {"]
    for path, (name, kind, _) in FIELDS.items():
        lines.append(f"        internal const {kind} {name} = {_literal(_get(data, path))};")
    record = fingerprint_record(data)
    lines.append("        internal static readonly string ContentFingerprintRecord = " + (json.dumps(record) if record is not None else "null") + ";")
    lines += ["    }", "}", ""]
    return {OUTPUT: "\n".join(lines)}

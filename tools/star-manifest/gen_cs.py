"""Compile canonical star manifests to C# 9. Never publish a partial hero.

    python tools/star-manifest/gen_cs.py vesper
    python tools/star-manifest/gen_cs.py --all --report
    python tools/star-manifest/gen_cs.py --all --report --markdown docs/specs/v1.31-gen-report.md

Report mode performs the same translation without writing C# or registration files.
A failed star is counted once, but every field diagnostic is retained. Exit status is
nonzero whenever any selected hero has an unmapped row, including in report mode.
"""
import argparse
from collections import defaultdict
from dataclasses import dataclass
from decimal import Decimal
import json
from pathlib import Path
import re
import sys
sys.dont_write_bytecode = True

import validate as canonical

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
OUTPUT = ROOT / "src/SodRpg.Core/Game/StarClusters"
HEROES = tuple(sorted(name for name in canonical.EXPECTED if name != "outer"))
TRIGGERS = {"OnUse": "ConfirmedUse", "OnHit": "Hit", "OnKill": "Kill",
            "OnCrit": "CriticalHit", "OnBasicAttack": "OwnedBasicAttackFired"}
FIELDS = {"Value": "Value", "Damage": "Value", "ValuePerType": "Value", "Total": "Value",
          "Duration": "Duration", "Lifetime": "Duration", "Radius": "Radius", "Delay": "Delay",
          "ExtraTargets": "TargetCount", "Chance": "Probability", "Arg": "Argument", "EveryN": "EveryN",
          "SelfValue": "Value", "AllyValue": "Value"}
ORDINARY = {"Element", "ElementEdge", "Echo", "Burst", "Sap", "Wound", "Shield", "Rampart",
            "Heal", "PackMend", "Expose", "Ricochet", "Crescendo", "Daze", "Empower", "Quicken",
            "Weakspot", "Siphon", "Reload"}
RECHARGE = {"Recharge", "RechargeTarget", "RechargeOther", "ReceiverRecharge"}
WARDS = {"AlliedWard", "SummonWard", "AllyWard", "AllyShield"}
# This is the native capability table, not a claim that every effect of a memory
# supports every parameter. Explicit targets are checked against concrete rows.
DURATION = {"Shield", "Empower", "Quicken", "Wound", "Daze", "Rampart", "Primed", "Crescendo", "Sap", "Weakspot"}


# Redesigned outer bridges have no PairCombos entry (the 62 legacy pairs stay untouched). A pair is only
# authored when the design fixes both endpoints and every gate field; the endpoint stars come from the layout
# (b7/b8 sit on the sixth stars of the two routes they join: HeroTreeLayout, order 5).
AUTHORED_PAIRS = {
    # Bismuth b8 (docs/specs/v1.31-clusters-bismuth.md, bridge table): I x S, I hit -> S remaining cooldown 1/2/3/4/5%,
    # once per I activation, five retained ranks, no mark (direct receiver gate).
    "h.bismuth.ring.renewal": {"index": 8, "endpoints": (("innocence", 6), ("distorting-sprint", 6)),
                               "source": "St_QR_Innocence", "trigger": "OnHit", "recipient": "St_M_Sprint"},
}
# A bridge anchor that is a migrated receiver boost, never a pair: its cluster declares the two memories it owns.
# Mist renewal: "FL / LU" (docs/specs/v1.31-clusters-mist.md); "no new pair" and "not an eighth existing combo".
RECEIVER_ONLY_BRIDGES = {"h.mist.ring.renewal": ("St_Q_Fleche", "St_Q_Lunge")}
# Design-table gaps that must be answered by the design owner before a real pair can be registered.
UNRESOLVED_BRIDGES = {
    "h.aurena.ring.renewal": "Aurena B8 is a Mark pair with five retained ranks (AlliedWard 4/5/6/7/8). Open questions: "
        "(1) what Expose does the mark give at ranks 4 and 5 (the design fixes only 2/3/4% for ranks 1-3, BridgeSuccessDefinition rejects a marked rank above 3, "
        "and the mark Expose is rank+1)? (2) BridgePayloadKind has no AlliedWard payload, so a pair whose base payoff is AlliedWard needs a new engine payload kind",
    "h.bismuth.ring.resolve": "Bismuth b7 is a Mark pair (P x I, Sap 1/2/3/4/5%) with five retained ranks. Open question: "
        "what Expose does the mark give at ranks 4 and 5 (the design fixes only 2/3/4% for ranks 1-3, BridgeSuccessDefinition rejects a marked rank above 3, "
        "and the mark Expose is rank+1)?",
    "h.nachia.ring.renewal": "Nachia b8 is a Window pair (Sylvan Call use opens the window, Circle of Life basic attack inside it recharges Sylvan Call 1/2/3/4/5%). Open questions: "
        "(1) which route star of Circle of Life is the endpoint (the layout places the bridge between pack-heart.6 and sylvan-call.6, not next to circle-life; existing pairs use the fourth stars)? "
        "(2) BridgeSuccessDefinition rejects a non-direct gate above rank 3: may a window pair keep its five retained ranks?",
}


def cs(value):
    return "null" if value is None else json.dumps(value, ensure_ascii=False)


def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float, Decimal)):
        raise ValueError("expected a number")
    result = Decimal(str(value))
    if not result.is_finite():
        raise ValueError("non-finite number")
    return result


def dec(value):
    return format(number(value), "f") + "m"


def whole(value):
    n = number(value)
    if n != n.to_integral_value() or n < -2147483648 or n > 2147483647:
        raise ValueError("requires an exact Int32; fractional values cannot be rounded")
    return str(int(n))


def units(value):
    return whole(number(value) * 100)


def array(values, kind="string"):
    values = list(values)
    return "Array.Empty<" + kind + ">()" if not values else "new " + kind + "[] { " + ", ".join(cs(v) if kind == "string" else str(v) for v in values) + " }"


def obj(kind, members):
    return "new " + kind + " { " + ", ".join(key + " = " + value for key, value in members.items()) + " }"


def selector(value):
    return "null" if value is None else "MemorySelector.Parse(" + cs(value) + ")"


def enum_members(file, name):
    text = (ROOT / file).read_text(encoding="utf-8")
    text = re.sub(r"//[^\n]*|/\*.*?\*/", "", text, flags=re.S)
    match = re.search(r"enum\s+" + re.escape(name) + r"\s*\{([^}]+)\}", text)
    if not match:
        raise ValueError("Missing native enum: " + name)
    return set(re.findall(r"(?:^|,)\s*(\w+)\s*(?:=|,|$)", match.group(1)))


@dataclass(frozen=True)
class Failure:
    star: str
    field: str
    value: str
    why: str

    @property
    def reason(self):
        return f"{self.field} = {self.value}: {self.why}"


def display_value(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, default=str, separators=(",", ":"))


class Compiler:
    def __init__(self, name, data, outer):
        self.name, self.hero = name, data["hero"]
        self.source = data["source"]
        self.rows = data["stars"] + outer["stars"]
        self.sources = {s["id"]: data["source"] for s in data["stars"]}
        self.sources.update({s["id"]: outer["source"] for s in outer["stars"]})
        self.by_id = {s["id"]: s for s in self.rows}
        self.failures = []
        self.mapped = {}
        self.legacy, self.route_rows, self.effects = canonical.legacy_data()
        self.powers = enum_members("src/SodRpg.Core/Game/Ids.cs", "Power")
        self.stats = enum_members("src/SodRpg.Core/Game/Ids.cs", "Stat")
        self.routes = {}
        text = (ROOT / "src/SodRpg.Core/Game/HeroStarRoutes.cs").read_text(encoding="utf-8")
        for hero, slug, memory in re.findall(r'new Route\(nodes,\s*"(\w+)",\s*"([^"]+)",\s*"([^"]+)"', text):
            if hero.lower() == name:
                self.routes[memory] = "h." + name + ".route." + slug
        # Explicit registered real pairs: no invented pair for an eighth region.
        text = (ROOT / "src/SodRpg.Core/Game/PairCombos.cs").read_text(encoding="utf-8")
        self.pairs = {}
        slugs = ("force", "insight", "vessel", "armor", "recall", "rhythm", "resolve")
        for line in text.splitlines():
            match = re.search(r'D\("(\w+)",\s*(\d+),\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"', line)
            if match and match[1].lower() == name:
                index = int(match[2])
                bridge = "h." + name + ".ring." + slugs[index - 1]
                tail = line[match.end():]
                tokens = re.findall(r'"([^"]*)"|PairComboTrigger\.(\w+)|PairComboStep\.(\w+)|GimmickEffect\.(\w+)', tail)
                self.pairs[bridge] = {"id": "h." + name + ".pair." + str(index),
                    "a": match[4], "b": match[6], "line": line,
                    "payoff": next((e for _, _, _, e in tokens if e), None)}
        memory_of = {route: memory for memory, route in self.routes.items()}
        for bridge, spec in AUTHORED_PAIRS.items():
            if not bridge.startswith("h." + name + "."):
                continue
            (slug_a, order_a), (slug_b, order_b) = spec["endpoints"]
            route_a, route_b = "h." + name + ".route." + slug_a, "h." + name + ".route." + slug_b
            self.pairs[bridge] = {"id": "h." + name + ".pair." + str(spec["index"]),
                "a": memory_of[route_a], "b": memory_of[route_b], "line": "", "payoff": None, "authored": spec,
                "star_a": route_a + "." + str(order_a), "star_b": route_b + "." + str(order_b)}
        self.known_memories = set(self.routes)
        contract = (ROOT / "src/SodRpg.Core/Game/Mechanisms/AuthoredStarContract.cs").read_text(encoding="utf-8")
        common = re.search(r"CommonMemories\s*=.*?\{(.*?)\};", contract, re.S)
        if common:
            self.known_memories.update(re.findall(r'"(St_\w+)"', common[1]))

    def fail(self, sid, field, value, why):
        failure = Failure(sid, field, display_value(value), why)
        if failure not in self.failures:
            self.failures.append(failure)

    def attempt(self, sid, field, value, fn):
        try:
            return fn()
        except (ValueError, KeyError, TypeError, IndexError) as error:
            self.fail(sid, field, value, str(error))
            return None

    def memory(self, sid, field, value, source=False):
        if value is None:
            return
        memories = re.findall(r"St_\w+", value)
        if not canonical.memory_ok(value) and value != "@OTHER":
            self.fail(sid, field, value, "not a canonical MemorySelector expression")
        for memory in memories:
            if memory not in self.known_memories:
                self.fail(sid, field, value, "unknown or foreign native memory: " + memory)
            if source and memory.startswith("St_M_"):
                self.fail(sid, field, value, "movement is a recipient, not an ordinary native source")
        if source and ("@M" in value or value == "@OTHER"):
            self.fail(sid, field, value, "selector is not an admitted source")

    def effect_set(self, sid, field, value):
        effects = [] if value is None else value.split("/")
        resolved = []
        for effect in effects:
            concrete = "Recharge" if effect in RECHARGE else "Shield" if effect in WARDS else None if effect in ("RelayWindow", "PressureDividend") else effect
            if concrete is None:
                continue  # These typed payloads are bound by exact target IDs.
            if concrete not in self.effects or concrete == "None":
                self.fail(sid, field, value, "no concrete GimmickEffect member or typed payload mapping for " + effect)
            elif concrete not in resolved:
                resolved.append(concrete)
        return array(["GimmickEffect." + e for e in resolved], "GimmickEffect")

    def supporting_rows(self, memory, effect_ids, effects, param):
        def supports(g):
            e, t = g["effect"], g["trigger"]
            if e in WARDS:
                return param in ("Duration", "Radius", "ExtraTargets")
            if e == "RelayWindow" or e == "Primed":
                return param == "Duration"
            if e in RECHARGE or e == "PressureDividend":
                return param == "Chance"
            if param == "Duration":
                return e in DURATION or e == "Expose" and t in ("OnHit", "OnCrit")
            if param == "Radius":
                return e in ("Burst", "Ricochet") or e == "Element" and t in ("OnUse", "OnKill") or e in ("Heal", "Siphon") and g["arg"] == 1
            return e in ("Ricochet", "Rampart") if param == "ExtraTargets" else e == "Element"
        found = []
        for row in self.rows:
            if effect_ids and row["id"] not in effect_ids:
                continue
            for option in [row] + (row.get("options") or []):
                g = option.get("gimmick")
                if g and (option.get("receiver") or option.get("memory")) == memory and (not effects or g["effect"] in effects):
                    if param is None or supports(g):
                        found.append(row["id"])
        for sid, (kind, args, mem) in self.route_rows.items():
            if sid in self.by_id or mem != memory or effect_ids and sid not in effect_ids or kind not in ("G", "CapG"):
                continue
            match = re.search(r"GimmickTrigger\.(\w+),\s*GimmickEffect\.(\w+),\s*\d+(?:,\s*(\d+))?", args)
            if match and (not effects or match[2] in effects) and (param is None or supports({"effect": match[2], "trigger": match[1], "arg": int(match[3] or 0)})):
                found.append(sid)
        return sorted(set(found))

    def mechanism(self, sid, row, prefix="gimmick"):
        g = row["gimmick"]
        if not g:
            return None
        self.memory(sid, "memory", row.get("memory"), source=True)
        self.memory(sid, "receiver", row.get("receiver"))
        e, source = g["effect"], row.get("memory")
        trigger = TRIGGERS[g["trigger"]]
        budget = "PerKill" if trigger == "Kill" else "PerOwnedBasicAttack" if trigger == "OwnedBasicAttackFired" else "PerActivationVictim" if trigger in ("Hit", "CriticalHit") and e in ("Sap", "Wound", "Expose", "Daze", "Rampart") and not g.get("once") else "PerActivation"
        pair = self.pairs.get(sid)
        if pair and pair.get("authored"):
            return self.authored_pair_center(sid, row, g, prefix, pair)
        members = {"ChannelId": cs(sid), "Source": selector(source), "Trigger": "MemoryEventKind." + trigger,
                   "Budget": "AttributionBudget." + budget}
        if "condition" in g:
            condition, bridge = g["condition"].split(":", 1)
            if bridge not in self.pairs:
                self.fail(sid, prefix + ".condition", g["condition"], UNRESOLVED_BRIDGES.get(bridge, "no registered real pair or complete authored pair definition"))
            else:
                authored = self.pairs[bridge].get("authored")
                if condition == "BridgeSuccess" and authored and g["trigger"] != authored["trigger"]:
                    self.fail(sid, prefix + ".condition", g["condition"], "the pair succeeds on " + authored["trigger"] + " but this star triggers on " + g["trigger"]
                              + ": a BridgeSuccess channel is dispatched only from the success transaction of the same event kind, so it could never fire. "
                              "The design table lists it without a pair condition; the manifest row needs that condition removed (manifests are not edited here)")
                members.update(Condition="AuthoredMechanismCondition." + condition, PairId=cs(self.pairs[bridge]["id"]))
                members["RequiredMemories"] = array([self.pairs[bridge]["a"], self.pairs[bridge]["b"]])
        for key, field in (("once", "Once"), ("everyN", "EveryN")):
            if key in g:
                members[field] = str(g[key]).lower()
        if "valuesByRank" in g:
            if len(g["valuesByRank"]) != row.get("maxRank", 1):
                self.fail(sid, prefix + ".valuesByRank", g["valuesByRank"], "rank table must cover every retained paid rank")
            members["ValuesByRank"] = array([units(x) for x in g["valuesByRank"]], "int")
        if "replaces" in g:
            replaces = g["replaces"]
            members["Replaces"] = array([replaces] if isinstance(replaces, str) else replaces)
        if "triggerByIdentity" in g:
            entries = []
            for memory, branch in sorted(g["triggerByIdentity"].items()):
                self.memory(sid, prefix + ".triggerByIdentity", memory, source=True)
                entries.append("{ " + cs(memory) + ", MemoryEventKind." + TRIGGERS[branch] + " }")
            members["TriggerByIdentity"] = "new Dictionary<string, MemoryEventKind>(StringComparer.Ordinal) { " + ", ".join(entries) + " }"
        if e in RECHARGE:
            recipient = g.get("target") or row.get("receiver") or ("@OTHER" if e == "RechargeOther" else source if e == "Recharge" else None)
            if recipient is None:
                self.fail(sid, prefix + ".target", recipient, "directed recharge requires an explicit recipient")
            if source is None:
                self.fail(sid, "memory", source, "DirectedRechargeChannel requires a source selector; null must not be approximated by Q/R only")
            if g["cooldown"] != 0:
                self.fail(sid, prefix + ".cooldown", g["cooldown"], "directed recharge has activation quotas/cadence, not a seconds cooldown")
            extras = ""
            if e == "ReceiverRecharge":
                conditions = {0: "Always", 1: "ElementTypesAtLeast", 2: "Shielded", 3: "ChangedTarget"}
                if self.name != "bismuth" or g["arg"] not in conditions:
                    self.fail(sid, prefix + ".arg", g["arg"], "no row-specific canonical receiver predicate; Bismuth defines only Arg 0/1/2/3")
                else:
                    extras = ", probabilityUnits: 5000, condition: RechargeConditionKind." + conditions[g["arg"]]
                    if g["arg"] == 1:
                        extras += ", requiredElementTypes: 2"
            members.update(Kind="AuthoredMechanismKind.DirectedRecharge",
                Recharge="new DirectedRechargeChannel(" + cs(sid) + ", " + selector(source) + ", MemoryEventKind." + trigger + ", " + selector(recipient) + ", new int[] { " + units(g["value"]) + " }, AttributionBudget." + budget + extras + ")")
            if sid.startswith("h.") and ".ring." in sid and sid in self.pairs:
                if "valuesByRank" not in g:
                    self.fail(sid, prefix + ".valuesByRank", None, "retained bridge receiver requires its exact non-linear rank table")
                return "ManifestPair(" + cs(self.hero) + ", " + cs(sid) + ", " + cs(source) + ", " + cs(recipient) + ", MemoryEventKind." + trigger + ", " + array([units(x) for x in g.get("valuesByRank", [])], "int") + ")"
        elif e == "Primed":
            if not source or source.startswith("@"):
                self.fail(sid, "memory", source, "MemoryPrimedDefinition requires one verified exact source memory")
            members.update(Kind="AuthoredMechanismKind.MemoryPrimed", Primed="new MemoryPrimedDefinition(" + cs(sid) + ", " + cs(source) + ", MemoryEventKind." + trigger + ", " + units(g["value"]) + ", budget: AttributionBudget." + budget + ")")
        elif e == "RelayWindow":
            target = g.get("target")
            if source != "St_R_Tranquility" or not target or target.startswith("@"):
                self.fail(sid, prefix + ".target", target, "RelayWindow requires its actual Tranquility source and one named native normal receiver")
            members.update(Kind="AuthoredMechanismKind.RelayWindow", Relay="new RelayWindowDefinition(" + cs(sid) + ", " + cs(target) + ", " + units(g["value"]) + ")")
        elif e in WARDS:
            summoned = e == "SummonWard" or e == "AlliedWard" and g["arg"] == 1
            basis, pool = g.get("basis", "CasterMaxOffense"), g.get("pool", "Allied")
            if e == "AllyShield" and ("basis" not in g or "pool" not in g):
                self.fail(sid, prefix, g, "recipient-HP allied shield must declare RecipientMaxHP basis and Ordinary pool")
            if basis not in ("CasterMaxOffense", "RecipientMaxHP") or pool not in ("Allied", "Ordinary"):
                self.fail(sid, prefix, g, "unknown ward basis or pool")
            health = summoned and basis == "RecipientMaxHP"
            members.update(Kind="AuthoredMechanismKind.AlliedWard", Ward="new AlliedWardDefinition(" + cs(sid) + ", WardRecipientKind." + ("OwnedSummons" if summoned else "AlliedTravelers") + ", WardAmountBasis." + basis + ", ModShieldPoolKind." + pool + ", " + units(g["value"]) + ", " + ("true" if e == "AllyShield" else "false") + (", durationSeconds: 3f, baseTargets: 1, limits: WardLimitProfile.SummonRecipientHealth" if health else "") + ")")
        elif e == "PressureDividend":
            if source not in ("St_L_CoinExplosion", "St_U_ShoutOfOblivion") or trigger != "Kill":
                self.fail(sid, prefix, g, "dividend requires verified CoinExplosion/Shout native kill provenance")
            members.update(Kind="AuthoredMechanismKind.PressureDividend", Dividend="new PressureDividendChannel(new[] { new PressureDividendContribution(" + cs(sid) + ", " + cs(source) + ", " + units(g["value"]) + ") })")
        elif e in ORDINARY:
            payload_trigger = g["trigger"]
            if trigger == "OwnedBasicAttackFired":
                if e != "PackMend" or source != "St_D_CircleOfLife":
                    self.fail(sid, prefix + ".trigger", payload_trigger, "ordinary owned-basic-fired payload supports only the canonical CircleOfLife PackMend; other effects have unfulfilled victim/damage constraints")
                payload_trigger = "OnUse"
            members.update(Kind="AuthoredMechanismKind.Gimmick", Gimmick=obj("GimmickDef", {
                "Trigger": "GimmickTrigger." + payload_trigger, "Effect": "GimmickEffect." + e,
                "Value": dec(g["value"]), "Arg": whole(g["arg"]), "Cooldown": format(number(g["cooldown"]), "f") + "f"}))
            if e == "Rampart":
                members["ShieldPool"] = "ModShieldPoolKind.Rampart"
        else:
            self.fail(sid, prefix + ".effect", e, "no executable AuthoredMechanismSpec translation")
            return None
        # Extras must never disappear merely because a constructor ignores them.
        if e not in WARDS:
            for key in ("basis", "pool"):
                if key in g:
                    self.fail(sid, prefix + "." + key, g[key], "basis/pool metadata is only defined for recipient wards")
        if e not in RECHARGE and e not in ORDINARY and g["cooldown"] != 0:
            self.fail(sid, prefix + ".cooldown", g["cooldown"], "typed payload has no seconds-cooldown field")
        return obj("AuthoredMechanismSpec", members)

    def authored_pair_center(self, sid, row, g, prefix, pair):
        """The retained center of a newly authored pair is the typed base binding of that pair."""
        spec = pair["authored"]
        recipient = g.get("target")
        if g["effect"] != "ReceiverRecharge" or g["arg"] != 0 or g["cooldown"] != 0 or "condition" in g:
            self.fail(sid, prefix, g, "the authored direct-receiver pair base is a deterministic ReceiverRecharge (Arg 0, CD0, no pair condition)")
        if row.get("memory") != spec["source"] or recipient != spec["recipient"] or row.get("receiver") != spec["recipient"] \
                or g["trigger"] != spec["trigger"] or spec["source"] not in (pair["a"], pair["b"]) or spec["recipient"] != pair["b"]:
            self.fail(sid, prefix, g, "center source/trigger/recipient differ from the design table pair " + display_value(spec))
        if not g.get("once") or len(g.get("valuesByRank", [])) != row.get("maxRank", 1):
            self.fail(sid, prefix + ".valuesByRank", g.get("valuesByRank"), "the retained pair base needs its once-per-activation flag and an exact table for every retained rank")
        return ("ManifestNewDirectRechargePair(" + cs(self.hero) + ", " + cs(sid) + ", " + cs(pair["id"]) + ", " + cs(pair["star_a"]) + ", " + cs(pair["a"]) + ", "
                + cs(pair["star_b"]) + ", " + cs(pair["b"]) + ", " + cs(spec["source"]) + ", MemoryEventKind." + TRIGGERS[spec["trigger"]] + ", " + cs(spec["recipient"]) + ", "
                + array([units(x) for x in g.get("valuesByRank", [])], "int") + ", true)")

    def key_spec(self, sid, spec, path, wound_lifetime=None):
        effect, field = spec["effect"], spec["field"]
        members = {"Layer": "KeystoneLayer.ModEffect"}
        scope = []
        if effect in ("DirectQR", "DirectDamage", "NativeDamage"):
            members["Layer"] = "KeystoneLayer.NativeDamage"
            scope.append("sourceKind: KeystoneSourceKind.NativeMemory")
            if effect == "DirectQR":
                scope.append("sourceSelectors: new[] { MemorySelector.Parse(\"@Q|@R\") }")
        elif effect == "MemoryDamage":
            members["Layer"] = "KeystoneLayer.StarMemoryDamage"
        elif effect in RECHARGE:
            scope += ["payloadKind: KeystonePayloadKind.DirectedRecharge", "targetEffectSet: new[] { GimmickEffect.Recharge }"]
        elif effect in WARDS:
            scope.append("payloadKind: KeystonePayloadKind.AlliedWard")
        elif effect == "RelayWindow":
            scope.append("payloadKind: KeystonePayloadKind.RelayWindow")
        elif effect == "PressureDividend":
            scope.append("payloadKind: KeystonePayloadKind.PressureDividend")
        elif effect in ("NativeExplosion", "NativePierceShot", "NativeAddedFire"):
            family, memory = {"NativeExplosion": ("native.salamander.explosion", "St_D_SalamanderPowder"),
                              "NativePierceShot": ("native.quick-trigger.pierce", "St_R_QuickTrigger"),
                              "NativeAddedFire": ("native.incendiary.additional", "St_Q_IncendiaryRounds")}[effect]
            self.memory(sid, path + ".effect", memory, source=True)
            members["Layer"] = "KeystoneLayer.NativeDamage"
            scope += ["sourceKind: KeystoneSourceKind.NativeMemory", "targetEffectIds: " + array([family]),
                      "sourceSelectors: new[] { " + selector(memory) + " }"]
        elif effect == "OnBasicAttack":
            scope.append("sourceKind: KeystoneSourceKind.OwnedBasicAttack")
        elif effect == "Recv.OnKill":
            targets = sorted({r["id"] for r in self.rows if r["region"] != "migration"
                for o in [r] + (r.get("options") or []) if (o.get("gimmick") or {}).get("effect") in RECHARGE
                and o["gimmick"]["trigger"] == "OnKill" and (o.get("receiver") or "").startswith("St_M_")})
            if not targets:
                self.fail(sid, path + ".effect", effect, "no exact new movement-recipient kill channels")
            scope += ["payloadKind: KeystonePayloadKind.DirectedRecharge", "targetEffectIds: " + array(targets)]
        else:
            scope.append("targetEffectSet: " + self.effect_set(sid, path + ".effect", effect))
            if effect == "Primed":
                scope.append("payloadKind: KeystonePayloadKind.MemoryPrimed")
        for member, named in (("memory", "sourceSelectors"), ("receiver", "receiverSelectors")):
            if spec.get(member):
                receiver = member == "receiver" or effect in RECHARGE | {"Recv.OnKill"} and spec[member].startswith("St_M_")
                self.memory(sid, path + "." + member, spec[member], source=not receiver)
                scope.append(("receiverSelectors" if receiver else named) + ": new[] { " + selector(spec[member]) + " }")
        if spec.get("memories"):
            for memory in spec["memories"]:
                self.memory(sid, path + ".memories", memory, source=True)
            scope.append("sourceSelectors: new[] { " + ", ".join(selector(x) for x in spec["memories"]) + " }")
        named = spec.get("scope")
        if named:
            if named == "equippedQR":
                scope.append("sourceSelectors: new[] { MemorySelector.Parse(\"@Q|@R\") }")
            elif named == "star-map MD only":
                members["Layer"] = "KeystoneLayer.StarMemoryDamage"
            elif named in ("star-map added self shield", "self-only MOD"):
                scope.append("recipient: KeystoneRecipientKind.Self")
            elif named in ("HIQS", "nonMovement6"):
                memories = ["St_Q_HandCannon", "St_Q_IncendiaryRounds", "St_R_QuickTrigger", "St_R_PrecisionShot"] if named == "HIQS" else [
                    "St_D_IcyVeins", "St_D_ChargedAnguillian", "St_Q_EmbracingTheChill", "St_Q_BigBorealChunk", "St_R_BackOff", "St_R_FrozenFists"]
                for memory in memories:
                    self.memory(sid, path + ".scope", memory, source=True)
                scope.append("targetMemorySet: " + array(memories))
            elif named == "star-map F effects fire every 2 shots":
                scope += ["sourceKind: KeystoneSourceKind.OwnedBasicAttack", "sourceSelectors: new[] { MemorySelector.Parse(\"St_D_CircleOfLife\") }"]
            elif named == "all new Recv; excludes B3/B4 b0":
                targets = sorted({r["id"] for r in self.rows if r["region"] != "migration"
                    for o in [r] + (r.get("options") or []) if (o.get("gimmick") or {}).get("effect") in RECHARGE
                    and (o.get("receiver") or "").startswith("St_M_")})
                if not targets:
                    self.fail(sid, path + ".scope", named, "no exact newly-authored movement receiver channels")
                if effect != "Recv.OnKill":
                    scope.append("targetEffectIds: " + array(targets))
            elif named == "all star-map ElementEdge":
                pass  # The concrete ElementEdge effect set above is this exact scope.
                if effect != "ElementEdge":
                    self.fail(sid, path + ".scope", named, "scope requires ElementEdge effect")
            else:
                self.fail(sid, path + ".scope", named, "named scope has no exact generation-time channel/memory/provenance expansion")
        if field in ("AllyValue", "SelfValue"):
            scope.append("recipient: KeystoneRecipientKind." + ("AlliedHero" if field == "AllyValue" else "Self"))
            scope.append("argument: 1")
        if sid == "vesper.key.shared-flame" and field == "Radius":
            scope.append("argument: 1")
        members["Scope"] = "new KeystoneScope(" + ", ".join(dict.fromkeys(scope)) + ")"
        if field == "Enabled":
            if spec.get("to") != 0:
                self.fail(sid, path + ".to", spec.get("to"), "Enabled only supports exact Disable (to: 0)")
            members["Disable"] = "true"
        elif field == "Grant":
            grant_row = {"id": sid + ".grant", "memory": spec.get("memory"), "receiver": spec.get("receiver"), "gimmick": spec.get("gimmick"), "maxRank": 1}
            if not grant_row["gimmick"]:
                self.fail(sid, path + ".gimmick", None, "Grant requires a concrete payload")
            else:
                grant = self.mechanism(sid + ".grant", grant_row, path + ".gimmick")
                if grant:
                    members["Grant"] = grant
        else:
            if field not in FIELDS:
                self.fail(sid, path + ".field", field, "no AuthoredKeystoneCompiler numeric field mapping")
            else:
                members["Field"] = "KeystoneField." + FIELDS[field]
            for key, target in (("pct", "Percent"), ("from", "From"), ("to", "To"), ("delta", "Delta"), ("max", "Maximum")):
                if key in spec:
                    members[target] = dec(spec[key])
            if wound_lifetime is not None:
                members["WoundLifetimePercent"] = dec(wound_lifetime)
        return obj("AuthoredKeystoneSpec", members)

    def keystone(self, sid, row):
        key = row["keystone"]
        if key["upsideSpec"] is None or key["downsideSpec"] is None:
            if sid in ("h.vesper.key", "h.vesper.key2"):
                power = "Power.Retaliation=25" if sid.endswith(".key") else "Power.Frenzy=4"
                self.fail(sid, "keystone.upsideSpec", None, power + " is a real retained Power upside, but KeystoneDefinition has no retained-Power field and rejects an empty typed upside; requires an out-of-scope schema/compiler contract change")
            else:
                self.fail(sid, "keystone.upsideSpec" if key["upsideSpec"] is None else "keystone.downsideSpec", None,
                          "prose-only key requires an explicit design-to-typed translation for both sides; no translation for this ID")
            return None
        up, down = list(key["upsideSpec"]), list(key["downsideSpec"])
        lifetime = next((s for s in down if s["effect"] == "Wound" and s["field"] in ("Duration", "Lifetime") and "pct" in s), None)
        total = next((s for s in up if s["effect"] == "Wound" and s["field"] == "Total"), None)
        if lifetime and total:
            down.remove(lifetime)
        sides = []
        for title, specs in (("upsideSpec", up), ("downsideSpec", down)):
            expressions = [self.key_spec(sid, s, "keystone." + title + "[" + str(i) + "]",
                lifetime["pct"] if s is total and lifetime else None) for i, s in enumerate(specs)]
            sides.append(array(expressions, "AuthoredKeystoneSpec"))
        required = sorted({s["receiver"] for s in up + down if s.get("receiver") and s["receiver"].startswith("St_")})
        cost = "Content.KeystoneCost" if row["region"] == "migration" else whole(row["rankCost"])
        return "AuthoredKeystoneCompiler.Compile(" + cs(sid) + ", " + array(required) + ", " + ", ".join(sides) + ", prerequisites: " + array(row["requires"]) + ", cost: " + cost + ")"

    def effect(self, sid, row, max_rank, cost, option_path=""):
        kind = row["kind"]
        if kind is None:
            self.fail(sid, "kind", None, "migration has no replacement effect; behavioral notes need an explicit typed translation")
            return None
        members = {"Kind": "ClusterStarKind." + kind, "Name": "new Txt(" + cs(row["nameJa"]) + ", " + cs(row["nameEn"]) + ")",
                   "MaxRank": str(max_rank), "RankCost": str(cost)}
        self.memory(sid, option_path + "memory", row.get("memory"))
        self.memory(sid, option_path + "receiver", row.get("receiver"))
        if kind == "Choice":
            members["Options"] = array([self.effect(sid, o, max_rank, cost, "options[" + str(i) + "].") or "null" for i, o in enumerate(row["options"])], "ClusterStarDef")
        elif kind in ("MemoryDamage", "MemoryHaste"):
            if row.get("receiver") or row["memory"].startswith("@"):
                self.fail(sid, option_path + "memory", row["memory"], "native modifiers require one actual nonmovement memory, not a receiver/slot selector")
            members["Memory"] = cs(row["memory"])
            # Legacy integer native links preserve the engine's declared cap.
            # Fractional native links require an explicit registered cap profile.
            if number(row["value"]) != number(row["value"]).to_integral_value():
                self.fail(sid, option_path + "value", row["value"], "fractional NativeMemoryModifierDef requires an explicitly declared CapProfileId; manifest does not specify one")
            else:
                members["Amount"] = whole(row["value"])
        elif kind in ("GimmickBoost", "GimmickParam"):
            memory = row.get("receiver") or row.get("memory")
            target = row["target"]
            ids = [target["star"]] if target["star"] else []
            effects = target["effect"].split("/") if target["effect"] else []
            scope = "Receiver" if row.get("receiver") else "EffectChannel" if ids else "Memory"
            if memory is None and ids and ids[0] in self.pairs:
                pair = self.pairs[ids[0]]
                # Only the named pair payoff is modified; opening memory GB is not a substitute.
                memory = next((m for m in (pair["a"], pair["b"]) if re.search(r'PairComboStep\.\w+,\s*"' + re.escape(m) + r'"', pair["line"])), None)
                if memory is None:
                    memory = next((r.get("receiver") or r.get("memory") for r in self.rows if r["id"] == ids[0]), None)
            if not memory or memory.startswith("@"):
                self.fail(sid, option_path + "memory", memory, "ScopedModifierDef needs one concrete owned scope memory")
            members["Memory"] = cs(memory)
            param = row["param"] if kind == "GimmickParam" else None
            # ScopeKind.Receiver needs an explicit recipient effect set even when
            # authored target is broad. It means recharge receipt, not movement use.
            if scope == "Receiver" and not effects and not ids:
                effects = ["Recharge"]
            # Untargeted parameters apply to meaningful fields, not every effect
            # that happens to use this memory. Bind exact supported IDs once.
            if param and not ids and scope != "Receiver":
                ids = self.supporting_rows(memory, [], effects, param)
                scope = "EffectChannel"
                if not ids:
                    self.fail(sid, option_path + "param", param, "no concrete supporting authored or retained native effect for this memory")
            if target["effect"] and any(e in WARDS or e in RECHARGE or e in ("RelayWindow", "PressureDividend") for e in effects) and not ids:
                ids = self.supporting_rows(memory, [], effects, param)
                scope = "Receiver" if row.get("receiver") else "EffectChannel"
                if not ids:
                    self.fail(sid, option_path + "target.effect", target["effect"], "typed alias requires an exact executable target ID set")
            modifier = {"ScopeKind": "ScopeKind." + scope, "ScopeMemory": cs(memory),
                "TargetEffectIds": array(ids), "TargetEffects": self.effect_set(sid, option_path + "target.effect", "/".join(effects) if effects else None)}
            if param:
                modifier["Param"] = "GimmickParam." + param
            if param == "Chance":
                modifier["Probability"] = "ProbabilityUnits.FromPercent(" + dec(row["value"]) + ")"
            elif param == "ExtraTargets":
                modifier["ExtraTargets"] = whole(row["value"])
            else:
                modifier["Amount"] = "ModifierUnits.FromPercent(" + dec(row["value"]) + ")"
            members["ScopedModifier"] = obj("ScopedModifierDef", modifier)
        elif kind == "Notable":
            if row.get("gimmick"):
                expression = self.mechanism(sid, row, option_path + "gimmick")
                if expression:
                    members["Mechanism"] = expression
            elif row.get("power"):
                power = row["power"]
                if power["name"] not in self.powers or power["name"] == "None":
                    self.fail(sid, option_path + "power.name", power["name"], "no concrete Power enum member")
                members.update(Power="Power." + power["name"], Amount=whole(power["perRank"]))
            else:
                self.fail(sid, option_path + "gimmick", None, "Notable requires one concrete executable payload")
        elif kind == "Stat":
            stat = row["stat"]
            if stat["name"] not in self.stats:
                self.fail(sid, option_path + "stat.name", stat["name"], "no concrete Stat enum member")
            members.update(Stat="Stat." + stat["name"], Amount=whole(stat["perRank"]))
        elif kind == "Keystone":
            expression = self.keystone(sid, row)
            if expression:
                members["KeystoneDefinition"] = expression
        else:
            self.fail(sid, option_path + "kind", kind, "no ClusterStarKind mapping")
        return obj("ClusterStarDef", members)

    def region(self, sid, row):
        if row["region"] == "memory":
            cluster = row["cluster"]
            route = next((route for route in self.routes.values() if cluster.startswith(self.name + ".mem." + route.split(".route.")[1] + ".")), None)
            if route is None:
                # Slugs are design labels, not native names. Resolve from the real
                # external anchor's route when author labels differ from baseline.
                group = [s for s in self.rows if s.get("cluster") == cluster]
                anchors = [s.get("anchor") for s in group if s.get("anchor") in self.route_rows]
                if anchors:
                    route = self.routes[self.route_rows[anchors[0]][2]]
            if route is None:
                self.fail(sid, "cluster", cluster, "memory cluster has no actual hero-owned route/anchor mapping")
                return None
            return "ClusterRegion.Memory(" + cs(route) + ")"
        if row["region"] == "bridge":
            anchors = [s.get("anchor") for s in self.rows if s.get("cluster") == row["cluster"] and s.get("anchor")]
            bridge = next((a for a in anchors if a in self.pairs or a in RECEIVER_ONLY_BRIDGES), None)
            if bridge is None:
                unresolved = next((a for a in anchors if a in UNRESOLVED_BRIDGES), None)
                self.fail(sid, "anchor", anchors, UNRESOLVED_BRIDGES[unresolved] if unresolved
                          else "bridge region has no registered real pair or explicit receiver-only ownership definition")
                return None
            return "ClusterRegion.Bridge(" + cs(bridge) + ")"
        if row["region"] == "outer":
            group = [s for s in self.rows if s.get("cluster") == row["cluster"]]
            if not any(s["kind"] == "Stat" and s.get("stat") and s["stat"]["perRank"] > 0 for s in group):
                anchors = sorted({s["anchor"] for s in group if s.get("anchor")})
                self.fail(sid, "region", "outer", "outer cluster " + row["cluster"] + " has no effectful Stat root; authored entry anchors " + display_value(anchors) + " cannot satisfy AuthoredStarContract.VerifyOwnership; native-route outer ownership needs an out-of-scope contract change")
            return "ClusterRegion.Outer"
        return "new ClusterRegion { Kind = ClusterRegionKind.Keystone }"

    def receiver_only_bridge(self, row):
        if row["region"] != "bridge":
            return None
        anchors = [s.get("anchor") for s in self.rows if s.get("cluster") == row["cluster"] and s.get("anchor")]
        return next((a for a in anchors if a in RECEIVER_ONLY_BRIDGES and a not in self.pairs), None)

    def star(self, row, seen_edges):
        sid = row["id"]
        start_failures = len(self.failures)
        expression = self.attempt(sid, "effect", {k: row[k] for k in ("kind", "memory", "value", "param")},
            lambda: self.effect(sid, row, row["maxRank"], row.get("rankCost", 1)))
        if not expression:
            return
        edges = []
        for other in row.get("edges", []):
            identity = tuple(sorted((sid, other)))
            if identity not in seen_edges:
                edges.append("new AuthoredStarEdge(" + cs(sid) + ", " + cs(other) + ")")
                seen_edges.add(identity)
        # Canonical migration notes explicitly change access from the core to
        # the first effectful route node. These are graph edges, not fake nodes.
        if self.name == "vesper" and row["region"] == "migration" and ".route." in sid:
            if row["requires"] and any(x.endswith(".2") for x in row["requires"]):
                target = next(x for x in row["requires"] if x.endswith(".2"))
                edges.append("ManifestRouteEntry(baselineLayout, " + cs(target) + ")")
            if sid in ("h.vesper.route.charge.2", "h.vesper.route.charge.4", "h.vesper.route.charge.7"):
                edges.append("ManifestRouteEntry(baselineLayout, " + cs(sid) + ")")
        ownership = "null"
        if row.get("receiver") and row["receiver"].startswith("St_"):
            sources = sorted({m for m in re.findall(r"St_\w+", row.get("memory") or "") if m in self.known_memories})
            ownership = obj("MemoryOwnership", {"TargetMemory": cs(row["receiver"]), "SourceMemories": array(sources)})
        receiver_only = self.receiver_only_bridge(row)
        if receiver_only:
            owned = RECEIVER_ONLY_BRIDGES[receiver_only]
            used = set(re.findall(r"St_\w+", json.dumps([row.get(k) for k in ("memory", "receiver", "target", "gimmick", "options")], ensure_ascii=False)))
            if not used or not used <= set(owned):
                self.fail(sid, "memory", sorted(used), "a receiver-only bridge star may only use the memories its cluster declares: " + display_value(owned))
            ownership = obj("MemoryOwnership", {"TargetMemory": cs(owned[0]), "SourceMemories": array(list(owned[1:]))})
        if row["region"] == "migration":
            expression = "ManifestRetained(" + cs(self.hero) + ", " + cs(sid) + ", " + expression + ", " + array(row["requires"]) + ", " + array(row["requiresAny"]) + ", " + array(edges, "AuthoredStarEdge") + ", " + ownership + ", " + cs(self.sources[sid]) + ", " + array(row["mechanisms"]) + ", " + cs(row["notes"]) + ")"
        else:
            region = self.region(sid, row)
            members = {"HeroKey": cs(self.hero), "LocalStarId": cs(sid), "ClusterId": cs(row["cluster"]),
                "Region": region or "null", "AnchorId": cs(row["anchor"]), "Shape": "ClusterShape." + (row["shape"] or "fan").title(),
                "RequiredStarIds": array(row["requires"]), "RequiredAnyStarIds": array(row["requiresAny"]),
                "Edges": array(edges, "AuthoredStarEdge"), "MemoryOwnership": ownership, "Effect": expression,
                "RequiresExplicitSelection": str(row["kind"] == "Choice").lower(),
                **({"ReceiverOnlyBridge": "true"} if receiver_only else {}),
                "SourceDocument": cs(self.sources[sid]), "MechanismIds": array(row["mechanisms"]), "Notes": cs(row["notes"])}
            expression = obj("AuthoredStarDef", members)
        if len(self.failures) == start_failures:
            self.mapped[sid] = expression

    def compile(self):
        expected_hero = "Hero_" + self.name.title()
        if self.hero != expected_hero:
            for row in self.rows:
                self.fail(row["id"], "hero", self.hero, "expected " + expected_hero + "; shared outer must expand into the concrete owner")
            return self
        schema_errors, _ = canonical.check(self.name, self.legacy, self.route_rows, self.effects, canonical.collect_effects())
        outer_errors, _ = canonical.check("outer", self.legacy, self.route_rows, self.effects, canonical.collect_effects())
        for error in schema_errors + outer_errors:
            owner = error.split(":", 1)[0].split("#", 1)[0].split(".upsideSpec", 1)[0].split(".downsideSpec", 1)[0]
            affected = [owner] if owner in self.by_id else [s["id"] for s in self.rows]
            for sid in affected:
                self.fail(sid, "canonical", None, error)
        groups = defaultdict(list)
        for row in self.rows:
            if row["region"] != "migration":
                groups[row["cluster"]].append(row)
        for cluster, group in groups.items():
            shapes = sorted({row["shape"] for row in group if row["shape"] is not None})
            if len(shapes) > 1:
                for row in group:
                    self.fail(row["id"], "shape", row["shape"], "cluster " + cluster + " mixes " + display_value(shapes) + "; AuthoredStarContract requires one Shape per ClusterId; preserving this compound geometry needs a contract change")
        seen_edges = set()
        for row in self.rows:
            self.attempt(row["id"], "star", row["id"], lambda r=row: self.star(r, seen_edges))
        failed = {f.star.split(".grant", 1)[0] for f in self.failures}
        for sid in failed:
            self.mapped.pop(sid, None)
        return self

    def render(self):
        if self.failures:
            raise ValueError("Refusing partial hero output: " + self.name)
        title = self.name.title()
        # Implicit base bindings replace already-saved pair nodes; they add no
        # graph IDs. Only pairs actually referenced by conditioned rows need them.
        bridges = set()
        for row in self.rows:
            for effect in [row] + (row.get("options") or []):
                condition = (effect.get("gimmick") or {}).get("condition")
                if condition:
                    bridges.add(condition.split(":", 1)[1])
        rows = list(self.mapped.values())
        rows.extend("ManifestBaselinePair(" + cs(self.hero) + ", " + cs(bridge) + ")"
                    for bridge in sorted(bridges) if bridge not in self.by_id)
        needs_layout = any("ManifestRouteEntry(" in expression for expression in rows)
        layout_arg = ", baselineLayout" if needs_layout else ""
        chunks = [rows[i:i + 32] for i in range(0, len(rows), 32)]
        lines = ["// Generated by tools/star-manifest/gen_cs.py; do not edit.", "using System;", "using System.Collections.Generic;", "",
                 "namespace SodRpg.Core.Game", "{", "    public static partial class StarClusters", "    {",
                 "        public static AuthoredStarDef[] Create" + title + "Authored()", "        {",
                 "            var definitions = new AuthoredStarDef[" + str(len(rows)) + "];"]
        if needs_layout:
            lines.append("            var baselineLayout = HeroTreeLayout.ForTalents(HeroSigils.BaselineTreeFor(" + cs(self.hero) + "));")
        for i in range(len(chunks)):
            lines.append("            Fill" + title + "Manifest" + str(i) + "(definitions" + layout_arg + ");")
        lines += ["            return definitions;", "        }"]
        for i, chunk in enumerate(chunks):
            lines += ["", "        private static void Fill" + title + "Manifest" + str(i) + "(AuthoredStarDef[] definitions" + (", HeroTreeLayout baselineLayout" if needs_layout else "") + ")", "        {"]
            lines += ["            definitions[" + str(i * 32 + j) + "] = " + expression + ";" for j, expression in enumerate(chunk)]
            lines.append("        }")
        return "\n".join(lines + ["    }", "}", ""])


def load(name):
    with (HERE / (name + ".json")).open(encoding="utf-8") as stream:
        return json.load(stream)


def registration(names):
    names = sorted(names)
    lines = ["// Generated by tools/star-manifest/gen_cs.py; do not edit.", "namespace SodRpg.Core.Game", "{",
             "    public static partial class StarClusters", "    {", "        public static void RegisterGenerated()", "        {"]
    for name in names:
        lines.append("            RegisterAuthored(" + cs("Hero_" + name.title()) + ", Create" + name.title() + "Authored());")
    return "\n".join(lines + ["        }", "    }", "}", ""])


def report(results):
    lines = ["# v1.31 manifest generation report", "", "Generated with `python tools/star-manifest/gen_cs.py --all --report`.", "",
             "Report mode writes no C# and performs no production registration. A clean row has an exact C# field translation; it is **not** a successful whole-tree registration, producer-envelope check or consumer smoke. A hero with any failed row cannot be published.", "",
             "Counts include migration rows and shared outer 160 expanded per hero; options are not additional stars. Failure totals count unique stars, while the groups below list every failing field (a star can appear in several groups).", "",
             "| Hero | Manifest rows | Outer rows | Clean | Failed stars |", "|---|---:|---:|---:|---:|"]
    for result in results:
        failed = {f.star.split(".grant", 1)[0] for f in result.failures}
        lines.append(f"| {result.hero} | {len(result.rows) - 160} | 160 | {len(result.mapped)} | {len(failed)} |")
    lines += ["", "## Pilot admission blocker", "",
              "`h.vesper.key` must preserve `Power.Retaliation=25`; `h.vesper.key2` must preserve `Power.Frenzy=4`. Both manifest key spec arrays are null. `Build.Compute` already adds the selected retained power independently (`Build.cs:247–252`), but `KeystoneDefinition` rejects an empty typed upside (`Mechanisms/ScopedKeystoneModifiers.cs:239–240`). Its schema/compiler has no retained-Power upside representation. An arbitrary payload flag or inert transform would hide the missing binding and is not generated.", "",
              "The required schema/compiler change lies outside the listed editing scope. Therefore no Vesper generated file or production registration is published; Vesper registration/reachability/300-point/consumer acceptance remains blocked. Baseline Vesper has 73 purchase nodes; intended complete tree has 73 + 645 private new + 160 outer = **878 purchase stars, plus the layout start node**. Manifest rows include 33 baseline replacements, not 33 extra graph nodes.", ""]
    for result in results:
        lines += ["## " + result.hero, ""]
        groups = defaultdict(list)
        for failure in result.failures:
            groups[failure.reason].append(failure.star)
        if not groups:
            lines += ["No field-translation failures. Whole-tree/runtime admission still requires verification.", ""]
        for reason, stars in sorted(groups.items()):
            reason = reason.replace("|", "\\|").replace("\n", " ").replace("`", "'")
            stars = sorted(set(stars))
            lines += ["### " + reason, "", str(len(stars)) + " affected star(s):", "", ", ".join("`" + sid + "`" for sid in stars), ""]
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("hero", nargs="?", choices=HEROES)
    parser.add_argument("--all", action="store_true", help="compile every concrete hero")
    parser.add_argument("--report", action="store_true", help="report only; never write generated C#")
    parser.add_argument("--markdown", type=Path, help="write the report (requires --report)")
    args = parser.parse_args(argv)
    if bool(args.hero) == bool(args.all):
        parser.error("select exactly one hero or --all")
    if args.markdown and not args.report:
        parser.error("--markdown requires --report")
    try:
        outer = load("outer")
        results = [Compiler(name, load(name), outer).compile() for name in (HEROES if args.all else (args.hero,))]
        for result in results:
            failed = {f.star.split(".grant", 1)[0] for f in result.failures}
            print(f"{result.name}: clean={len(result.mapped)} failed={len(failed)} total={len(result.rows)}")
            for failure in sorted(result.failures, key=lambda f: (f.star, f.field, f.value, f.why)):
                print(f"  {failure.star}: {failure.reason}")
        if args.markdown:
            args.markdown.write_text(report(results), encoding="utf-8", newline="\n")
        if any(result.failures for result in results):
            print("No generated files written: at least one selected hero cannot be mapped completely.", file=sys.stderr)
            return 1
        if not args.report:
            # Translate all selected rows before the first output mutation. Atomic
            # failure means an existing good hero/entry point is left untouched.
            outputs = [(OUTPUT / (r.name.title() + ".Generated.cs"), r.render()) for r in results]
            available = {name for name in HEROES if (OUTPUT / (name.title() + ".Generated.cs")).is_file()}
            available.update(r.name for r in results)
            outputs.append((OUTPUT / "GeneratedRegistration.cs", registration(available)))
            for path, content in outputs:
                path.write_text(content, encoding="utf-8", newline="\n")
                print("Wrote " + path.relative_to(ROOT).as_posix())
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Generator failed: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

"""Compile canonical star manifests to C# 9. Never publish a partial hero.

    python tools/star-manifest/gen_cs.py vesper
    python tools/star-manifest/gen_cs.py --all --report
    python tools/star-manifest/gen_cs.py --all --report --markdown docs/specs/v1.31-gen-report.md

Report mode performs the same translation without writing C# or registration files.
A failed star is counted once, but every field diagnostic is retained. Exit status is
nonzero whenever any selected hero has an unmapped row, including in report mode.

Output mode writes <Hero>.Generated.cs only for heroes with zero failures (never a partial
hero), plus GeneratedRegistration.cs: StarClusters.CompiledHeroes (every hero with generated C#),
StarClusters.GeneratedHeroes (only the heroes listed in registered.txt, i.e. verified to pass the
real StarClusters.RegisterAuthored; a hero that fails registration must never be listed because the
game would crash at startup) and the one entry point StarClusters.RegisterAllGenerated(), which
registers each generated hero's authored tree and its migration rules.

    python tools/star-manifest/gen_cs.py --all --diagnostic

additionally writes guarded, test-only maps (tests/SodRpg.Core.Tests/Diagnostics, git-ignored) for
RegistrationDiagnostics, which lists every registration rejection of every hero.
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
DIAGNOSTICS = ROOT / "tests/SodRpg.Core.Tests/Diagnostics"
HEROES = tuple(sorted(name for name in canonical.EXPECTED if name != "outer"))
GATE_PARAMS = {"WindowDuration": "Window", "MarkDuration": "Mark"}
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

# Legacy keystones whose upside is their existing Power, kept unchanged; their manifest carries no typed spec.
# h.yubar.key2 keeps its Power and migrates the value 20->15 in the same ID (row `power` field; ManifestKeystone's
# migratedPowerValue), which is still the baseline Power as the upside, never a re-typed second source.
RETAINED_POWER_KEYS = {"h.vesper.key", "h.vesper.key2", "h.lacerta.key", "h.lacerta.key2", "h.cetus.key",
                       "h.yubar.key", "h.yubar.key2", "h.husk.key", "h.husk.key2", "h.nachia.key", "h.nachia.key2", "h.bismuth.key",
                       "h.mist.key", "h.mist.key2"}
BASELINE_KEYS = {}
for _hero, _id, _power, _value in re.findall(r'Key\("Hero_(\w+)",\s*"(\w+\.key2?)",\s*"[^"]*",\s*"[^"]*",\s*Power\.(\w+),\s*(\d+)',
                                            (ROOT / "src/SodRpg.Core/Game/HeroSigils.cs").read_text(encoding="utf-8")):
    BASELINE_KEYS["h." + _id] = (_power, _value)


# Redesigned outer bridges have no PairCombos entry (the 62 legacy pairs stay untouched). A pair is only
# authored when the design fixes both endpoints and every gate field; the endpoint stars come from the layout
# (b7/b8 sit on the sixth stars of the two routes they join: HeroTreeLayout, order 5).
AUTHORED_PAIRS = {
    # Bismuth b8 (docs/specs/v1.31-clusters-bismuth.md, bridge table): I x S, I hit -> S remaining cooldown 1/2/3/4/5%,
    # once per I activation, five retained ranks, no mark (direct receiver gate).
    "h.bismuth.ring.renewal": {"index": 8, "endpoints": (("innocence", 6), ("distorting-sprint", 6)), "gate": "DirectReceiver",
                               "source": "St_QR_Innocence", "trigger": "OnHit", "recipient": "St_M_Sprint", "payload": "Recharge"},
    # Bismuth b7: P x I, P hit marks (4 s), I hit on the marked enemy Sap 1/2/3/4/5%, once per I activation, five retained ranks.
    "h.bismuth.ring.resolve": {"index": 7, "endpoints": (("prismatic-eyes", 6), ("innocence", 6)), "gate": "Mark",
                               "opening": ("St_D_PrismaticEyes", "OnHit"), "source": "St_QR_Innocence", "trigger": "OnHit",
                               "payload": "Gimmick", "effect": "Sap", "arg": 0},
    # Aurena B8: P(C,G), G hit marks, C hit on the marked enemy AlliedWard 4/5/6/7/8, five retained ranks (the original C/G connection: sixth stars).
    "h.aurena.ring.renewal": {"index": 8, "endpoints": (("claw", 6), ("golden-burst", 6)), "gate": "Mark",
                              "opening": ("St_Q_GoldenBurst", "OnHit"), "source": "St_D_DisintegratingClaw", "trigger": "OnHit",
                              "payload": "AlliedWard"},
    # Nachia b8: Circle of Life x Sylvan Call (the two memories named by the design; endpoints are the fourth route stars like every pair).
    # Sylvan Call use opens a 4 s window; a Circle of Life owned basic attack inside it recharges Sylvan Call 1/2/3/4/5%.
    "h.nachia.ring.renewal": {"index": 8, "endpoints": (("circle-life", 4), ("sylvan-call", 4)), "gate": "Window",
                              "opening": ("St_Q_SylvanCall", "OnUse"), "source": "St_D_CircleOfLife", "trigger": "OnBasicAttack",
                              "recipient": "St_Q_SylvanCall", "payload": "Recharge"},
}
# A bridge anchor that is a migrated receiver boost, never a pair: its cluster declares the two memories it owns.
# Mist renewal: "FL / LU" (docs/specs/v1.31-clusters-mist.md); "no new pair" and "not an eighth existing combo".
# Design-table migrations of a registered pair's mark trigger. Aurena B2: the Dangerous Theory payload is not a guaranteed crit,
# so the mark opens on Hit instead of Crit (docs/specs/v1.31-clusters-aurena.md, section B).
LEGACY_OPENING_OVERRIDES = {"h.aurena.ring.insight": "OnHit"}
RECEIVER_ONLY_BRIDGES = {"h.mist.ring.renewal": ("St_Q_Fleche", "St_Q_Lunge")}
# Design-table gaps that must be answered by the design owner before a real pair can be registered.
UNRESOLVED_BRIDGES = {}


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
                seq = [a or b for _, a, b, _ in tokens if a or b]
                table = re.search(r'"[^"]*",\s*"[^"]*",\s*"([^"]*)",\s*PairComboTrigger\.(\w+),\s*PairComboStep\.(\w+),\s*(?:"([^"]*)"|null),\s*PairComboTrigger\.(\w+),\s*'
                                  r'GimmickEffect\.(\w+),\s*(\d+),\s*(\d+),\s*(\d+)(?:,\s*(\d+))?(?:,\s*[\d.]+f)?(?:,\s*(?:"([^"]*)"|null))?', tail)
                self.pairs[bridge] = {"table": table, "once": "oncePerActivation: true" in line, "id": "h." + name + ".pair." + str(index),
                    "a": match[4], "b": match[6], "line": line,
                    "payoff": next((e for _, _, _, e in tokens if e), None),
                    # opening trigger, step, payoff trigger: a pair without a step succeeds on its opening trigger
                    "success": seq[2] if seq[1] != "None" else seq[0]}
        memory_of = {route: memory for memory, route in self.routes.items()}
        for bridge, spec in AUTHORED_PAIRS.items():
            if not bridge.startswith("h." + name + "."):
                continue
            (slug_a, order_a), (slug_b, order_b) = spec["endpoints"]
            route_a, route_b = "h." + name + ".route." + slug_a, "h." + name + ".route." + slug_b
            self.pairs[bridge] = {"id": "h." + name + ".pair." + str(spec["index"]),
                "a": memory_of[route_a], "b": memory_of[route_b], "line": "", "payoff": None, "authored": spec, "success": spec["trigger"],
                "star_a": route_a + "." + str(order_a), "star_b": route_b + "." + str(order_b)}
        # Registered real outer anchor stats (StarClusters.OuterAnchors): the legacy
        # contract-accepted outer entrances without an authored Stat root.
        text = (ROOT / "src/SodRpg.Core/Game/StarClusters.cs").read_text(encoding="utf-8")
        self.outer_anchors = set()
        for match in re.finditer(r'new TalentDef\("(h\.[\w.-]+)"[\s\S]*?\{([^}]*)\}', text):
            hero = re.search(r'HeroKey = "(\w+)"', match.group(2))
            if hero and "IsOuterAnchor = true" in match.group(2):
                self.outer_anchors.add((hero.group(1), match.group(1)))
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
            concrete = "Recharge" if effect in RECHARGE else "Shield" if effect in WARDS else None if effect in ("RelayWindow", "PressureDividend", "IdentityStrike", "MemoryTuning") else effect
            if concrete is None:
                continue  # These typed payloads are bound by exact target IDs.
            if concrete not in self.effects or concrete == "None":
                self.fail(sid, field, value, "no concrete GimmickEffect member or typed payload mapping for " + effect)
            elif concrete not in resolved:
                resolved.append(concrete)
        return array(["GimmickEffect." + e for e in resolved], "GimmickEffect")

    def bridge_gate(self, bridge):
        """Mark / Window / DirectReceiver: the gate of a bridge's pair (registered PairCombos step or the authored pair's declared gate)."""
        pair = self.pairs[bridge]
        if pair.get("authored"):
            return pair["authored"]["gate"]
        table = pair["table"]
        if table is None:
            raise ValueError("no pair table row for " + bridge)
        return {"Mark": "Mark", "Window": "Window"}.get(table[3], "DirectReceiver")

    def payoff_memory(self, bridge):
        """The memory whose events pay a bridge off (AuthoredMechanisms.SourceMemory of its BridgeSuccess spec)."""
        pair = self.pairs[bridge]
        if pair.get("authored"):
            return pair["authored"]["source"]
        table = pair["table"]
        if table is None:
            raise ValueError("no pair table row for " + bridge)
        return table[4] if table[3] != "None" else table[1]

    def boost_recipients(self, sid, path, memory):
        """None when a whole-memory boost of this memory may use the plain Memory scope; otherwise (ids, effect names) of the exact
        recipients: the memory's own gimmicks, without its Reload effects and without its Recv recharges delivered to another memory."""
        included, excluded, included_effects, excluded_effects = [], set(), [], set()
        for sid_, (kind, args, mem) in self.route_rows.items():
            if sid_ in self.by_id or mem != memory or kind not in ("G", "CapG"):
                continue
            match = re.search(r"GimmickEffect\.(\w+)", args)
            if not match:
                continue
            if match[1] == "Reload":
                excluded_effects.add("Reload")
            else:
                included.append(sid_)
                included_effects.append(match[1])
        for row in self.rows:
            for option in [row] + (row.get("options") or []):
                g = option.get("gimmick")
                if not g or option.get("memory") != memory:
                    continue
                foreign = bool(option.get("receiver")) and option["receiver"] != memory
                if g["effect"] == "Reload" or foreign:
                    excluded.add(row["id"])
                    excluded_effects.add("Reload" if g["effect"] == "Reload" else g["effect"])
                else:
                    included.append(row["id"])
                    included_effects.append(g["effect"])
        if not excluded_effects:
            return None
        ids = sorted(set(included))
        if not ids:
            self.fail(sid, path + "memory", memory, "whole-memory boost: the memory has no effect that is neither Reload nor a Recv delivered elsewhere, so no meaningful recipient")
            return None
        mixed = excluded & set(ids)
        effects = []
        if mixed:
            # A Choice's options share the star ID: only the effect set can tell the included option from the excluded one.
            effects = sorted(set(included_effects))
            if set(effects) & excluded_effects:
                self.fail(sid, path + "memory", memory, "a Choice mixes an included and an excluded option of the same effect; the boost cannot name only one")
        return ids, effects

    def supporting_rows(self, memory, effect_ids, effects, param, with_detail=False):
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
        found, detail, mixed = [], set(), set()
        for row in self.rows:
            if effect_ids and row["id"] not in effect_ids:
                continue
            for option in [row] + (row.get("options") or []):
                g = option.get("gimmick")
                if g and (option.get("receiver") or option.get("memory")) == memory and (not effects or g["effect"] in effects):
                    if param is None or supports(g):
                        found.append(row["id"])
                        detail.add((row["id"], g["effect"]))
                    else:
                        mixed.add(row["id"])
                elif g and option.get("memory") == memory:
                    mixed.add(row["id"])  # sourced by this memory, delivered elsewhere (a directed recharge): never a recipient of the field
        for sid, (kind, args, mem) in self.route_rows.items():
            if sid in self.by_id or mem != memory or effect_ids and sid not in effect_ids or kind not in ("G", "CapG"):
                continue
            match = re.search(r"GimmickTrigger\.(\w+),\s*GimmickEffect\.(\w+),\s*\d+(?:,\s*(\d+))?", args)
            if match and (not effects or match[2] in effects) and (param is None or supports({"effect": match[2], "trigger": match[1], "arg": int(match[3] or 0)})):
                found.append(sid)
                detail.add((sid, match[2]))
        if with_detail:
            return sorted(set(found)), sorted({e for i, e in detail}), sorted(mixed & set(found))
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
        if pair and not pair.get("authored"):
            kind, _, why = self.legacy_center(sid)
            if kind == "mismatch":
                self.fail(sid, prefix, g, "the center row cannot be bound to registered pair " + pair["id"] + ": " + ", ".join(why))
                return None
            if kind == "baseline":
                override = LEGACY_OPENING_OVERRIDES.get(sid)
                return "ManifestPair(" + cs(self.hero) + ", " + cs(sid) + (", openingOverride: MemoryEventKind." + TRIGGERS[override] if override else "") + ")"
        if e == "IdentityStrike":
            return self.identity_strike(sid, row, g, prefix)
        if e == "MemoryTuning":
            return self.memory_tuning(sid, row, g, prefix)
        members = {"ChannelId": cs(sid), "Source": selector(source), "Trigger": "MemoryEventKind." + trigger,
                   "Budget": "AttributionBudget." + budget}
        if "condition" in g:
            condition, bridge = g["condition"].split(":", 1)
            if bridge not in self.pairs:
                self.fail(sid, prefix + ".condition", g["condition"], UNRESOLVED_BRIDGES.get(bridge, "no registered real pair or complete authored pair definition"))
            else:
                success = self.pair_success(bridge)
                if condition == "BridgeSuccess" and g["trigger"] != success:
                    self.fail(sid, prefix + ".condition", g["condition"], "the pair succeeds on " + success + " but this star triggers on " + g["trigger"]
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
            members.update(Kind="AuthoredMechanismKind.AlliedWard", Ward="new AlliedWardDefinition(" + cs(sid) + ", WardRecipientKind." + ("OwnedSummons" if summoned else "AlliedTravelers") + ", WardAmountBasis." + basis + ", ModShieldPoolKind." + pool + ", " + units(g["value"]) + ", " + ("true" if e == "AllyShield" or e == "AlliedWard" and self.name == "aurena" and not summoned else "false") + (", durationSeconds: 3f, baseTargets: 1, limits: WardLimitProfile.SummonRecipientHealth" if health else "") + ")")
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
        elif e in ("SacrificeShield", "StunSourceFilter"):
            # C11/C08 named adapters: the flag payload is the whole typed mechanism; the host reads its fixed
            # design constants (50%/4s/10% cap, 6%HP/3s/2s interval) from the runtime, so no source/payload fields exist.
            if sid != ("h.aurena.key2.grant" if e == "SacrificeShield" else "h.cetus.key2.grant") or g["cooldown"] != (0 if e == "SacrificeShield" else 2) or g["target"] is not None:
                self.fail(sid, prefix + ".effect", e, "native adapter grants belong only to their named legacy keystone (no target, cooldown 0/2)")
                return None
            return obj("AuthoredMechanismSpec", {"ChannelId": cs(sid), "Kind": "AuthoredMechanismKind." + e})
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

    def identity_strike(self, sid, row, g, prefix):
        """IdentityStrike: damage dealt by the equipped identity memory itself (the SkillTrigger is the damage Actor), see
        HostAuthority.IdentityStrikes.cs. Units: gimmick.value = percent of the higher of AD/AP; strike.bonusSpeed = extra percent per 1% converted bonus attack speed."""
        source, st = row.get("memory"), g["strike"]
        mode = st["mode"]
        if not source or source.startswith("@") or source not in ("St_D_ScarOfTheWind", "St_D_TheKillingFlow"):
            self.fail(sid, "memory", source, "IdentityStrike needs one concrete verified Husk identity memory (St_D_ScarOfTheWind or St_D_TheKillingFlow)")
            return None
        if row.get("receiver") or self.pairs.get(sid):
            self.fail(sid, prefix, g, "IdentityStrike has no receiver and cannot be a bridge payload")
            return None
        if mode == "DashBonusAsMemory":
            if source != "St_D_ScarOfTheWind":
                self.fail(sid, "memory", source, "the dash-bonus attribution exists only for St_D_ScarOfTheWind")
                return None
            payload = "IdentityStrikeDefinition.DashBonusAsMemory(" + cs(sid) + ")"
        else:
            if mode == "AfterDisplacement" and source != "St_D_ScarOfTheWind" or mode == "EveryNthBasicAttack" and source != "St_D_TheKillingFlow":
                self.fail(sid, prefix + ".strike.mode", mode, "AfterDisplacement belongs to Wind Scar and EveryNthBasicAttack to Killing Flow")
                return None
            common = ", IdentityStrikeElement." + st["element"] + ", IdentityStrikeShape." + st["shape"] + ", " + format(number(st["range"]), "f") + "f, "                 + format(number(st["width"]), "f") + "f, " + str(st.get("maxTargets", 8))
            if mode == "AfterDisplacement":
                payload = "IdentityStrikeDefinition.AfterDisplacement(" + cs(sid) + ", " + cs(source) + ", " + units(g["value"]) + common                     + ", windowSeconds: " + format(number(st["windowSeconds"]), "f") + "f)"
            else:
                payload = "IdentityStrikeDefinition.EveryNth(" + cs(sid) + ", " + cs(source) + ", " + str(g["everyN"]) + ", " + units(g["value"])                     + ", " + units(st.get("bonusSpeed", 0)) + common + ")"
        return obj("AuthoredMechanismSpec", {"ChannelId": cs(sid), "Kind": "AuthoredMechanismKind.IdentityStrike", "Source": selector(source),
                   "Trigger": "MemoryEventKind.OwnedBasicAttackHit", "Budget": "AttributionBudget.PerOwnedBasicAttack", "IdentityStrike": payload})

    def memory_tuning(self, sid, row, g, prefix):
        """MemoryTuning: a static, host-authoritative change of one named native memory (HostAuthority.MemoryTunings.cs / StanceTuning.cs)."""
        source, kind = row.get("memory"), g["tuning"]["kind"]
        expected, _, _ = canonical.TUNINGS[kind]
        if source != expected:
            self.fail(sid, "memory", source, "tuning " + kind + " belongs to " + expected)
            return None
        if row.get("receiver") or self.pairs.get(sid):
            self.fail(sid, prefix, g, "MemoryTuning has no receiver and cannot be a bridge payload")
            return None
        factory = {"KillingFlowKeepSpeed": "KeepSpeed(" + cs(sid) + ", " + units(g["value"]) + ")",
                   "KillingFlowOnHitHealScale": "HealScale(" + cs(sid) + ", " + units(g["value"]) + ")",
                   "StanceSwordQiAttackBasis": "SwordQiAttackBasis(" + cs(sid) + ")"}[kind]
        return obj("AuthoredMechanismSpec", {"ChannelId": cs(sid), "Kind": "AuthoredMechanismKind.MemoryTuning", "Source": selector(source),
                   "Trigger": "MemoryEventKind.ConfirmedUse", "Budget": "AttributionBudget.PerActivation", "Tuning": "MemoryTuningDefinition." + factory})

    def pair_success(self, bridge):
        pair = self.pairs[bridge]
        if pair.get("authored"):
            return pair["success"]
        kind, success, _ = self.legacy_center(bridge)
        return success if kind in ("baseline", "direct") else pair["success"]

    def legacy_center(self, bridge):
        """Classify the manifest center row of a registered legacy pair.

        ("baseline", success, [])  the row restates the registered pair, which is emitted as its typed base (ManifestPair)
        ("direct", trigger, [])    the design changed the pair into a direct receiver (no mark): the row defines it
        ("mismatch", None, why)    neither; the row cannot be bound to the registered pair
        """
        pair = self.pairs[bridge]
        row = self.by_id.get(bridge)
        match = pair["table"]
        g = row.get("gimmick") if row else None
        if match and row is None:
            # No migration row: the implicit registered pair is bound by ManifestBaselinePair.
            return ("baseline", match.group(5) if match.group(3) != "None" else match.group(2), [])
        if not match or not g:
            return ("mismatch", None, ["no registered pair table or center gimmick"])
        origin, trigger, step, payoff, payoff_trigger, effect, v1, v2, v3, arg = match.groups()[:10]
        recharge = match.group(11)
        success = payoff_trigger if step != "None" else trigger
        source = payoff if step != "None" else origin
        family = {"Recharge", "RechargeTarget", "ReceiverRecharge"}
        same_effect = g["effect"] == effect or effect == "Recharge" and g["effect"] in family
        wrong = []
        if not same_effect:
            wrong.append("effect " + g["effect"] + " vs " + effect)
        if [number(x) for x in g.get("valuesByRank", [])] != [Decimal(v1), Decimal(v2), Decimal(v3)]:
            wrong.append("rank table")
        if g["arg"] != int(arg or 0):
            wrong.append("arg")
        if g["cooldown"] != 0:
            wrong.append("cooldown")
        if effect == "Recharge" and g.get("target") != recharge:
            wrong.append("recipient")
        if g["trigger"] == success and row.get("memory") == source and not wrong:
            # The design text states that the mark was removed and the pair became a direct receiver ("直接Recv").
            if step != "None" and "直接" in row.get("notes", ""):
                return ("direct", g["trigger"], [])
            return ("baseline", success, [])
        if g["effect"] in family | {"RechargeOther"}:
            # The design redefined this pair as a direct receiver; the center row (source, trigger, recipient, table) defines it.
            return ("direct", g["trigger"], [])
        wrong.append("trigger/source " + g["trigger"] + "/" + str(row.get("memory")) + " vs " + success + "/" + str(source))
        return ("mismatch", None, wrong)

    def authored_pair_center(self, sid, row, g, prefix, pair):
        """The retained center of a newly authored pair is the typed base binding of that pair."""
        spec = pair["authored"]
        gate = spec["gate"]
        effects = {"Recharge": ("ReceiverRecharge", "RechargeTarget", "Recharge"), "Gimmick": (spec.get("effect"),), "AlliedWard": ("AlliedWard",)}[spec["payload"]]
        recipient = g.get("target")
        bad = []
        if g["effect"] not in effects or g["arg"] != spec.get("arg", 0) or g["cooldown"] != 0:
            bad.append("effect/arg/cooldown")
        if "condition" in g and g["condition"].split(":", 1)[1] != sid:
            bad.append("condition names a different bridge")
        if g["trigger"] != spec["trigger"] or row.get("memory") != spec["source"] or spec["source"] not in (pair["a"], pair["b"]):
            bad.append("source/trigger")
        if spec["payload"] == "Recharge" and (recipient != spec["recipient"] or row.get("receiver") != spec["recipient"] or spec["recipient"] not in (pair["a"], pair["b"])):
            bad.append("recipient")
        if gate == "DirectReceiver" and "condition" in g:
            bad.append("a direct receiver base has no pair condition")
        if bad:
            self.fail(sid, prefix, g, "center differs from the design table pair " + display_value(spec) + ": " + ", ".join(bad))
        if (gate == "DirectReceiver" and not g.get("once")) or len(g.get("valuesByRank", [])) != row.get("maxRank", 1):
            self.fail(sid, prefix + ".valuesByRank", g.get("valuesByRank"), "the retained pair base needs an exact table for every retained rank (and its once flag when direct)")
        values = array([units(x) for x in g.get("valuesByRank", [])], "int")
        head = cs(self.hero) + ", " + cs(sid) + ", " + cs(pair["id"]) + ", " + cs(pair["star_a"]) + ", " + cs(pair["a"]) + ", " + cs(pair["star_b"]) + ", " + cs(pair["b"])
        if gate == "DirectReceiver":
            return ("ManifestNewDirectRechargePair(" + head + ", " + cs(spec["source"]) + ", MemoryEventKind." + TRIGGERS[spec["trigger"]] + ", "
                    + cs(spec["recipient"]) + ", " + values + ", true)")
        opening, opening_trigger = spec["opening"]
        return ("ManifestNewPair(" + head + ", BridgeGateKind." + gate + ", " + cs(opening) + ", MemoryEventKind." + TRIGGERS[opening_trigger] + ", "
                + cs(spec["source"]) + ", MemoryEventKind." + TRIGGERS[spec["trigger"]] + ", BridgePayloadKind." + {"Recharge": "Recharge", "Gimmick": "Gimmick", "AlliedWard": "AlliedWard"}[spec["payload"]]
                + ", " + cs(spec.get("recipient")) + ", GimmickEffect." + (spec.get("effect") or "None") + ", " + str(spec.get("arg", 0)) + ", " + values + ", "
                + ("true" if g.get("once") else "false") + ")")

    def key_spec(self, sid, spec, path):
        effect, field = spec["effect"], spec["field"]
        members = {"Layer": "KeystoneLayer.ModEffect"}
        scope = []
        if effect in ("DirectQR", "DirectDamage", "NativeDamage"):
            members["Layer"] = "KeystoneLayer.NativeDamage"
            scope.append("sourceKind: KeystoneSourceKind.NativeMemory")
            if effect == "DirectQR":
                scope.append("sourceSelectors: new[] { MemorySelector.Parse(\"@Q|@R\") }")
        elif effect == "DirectBasicAttack":
            members["Layer"] = "KeystoneLayer.NativeDamage"
            scope.append("sourceKind: KeystoneSourceKind.OwnedBasicAttack")
        elif effect == "SummonDirectDamage":
            members["Layer"] = "KeystoneLayer.NativeDamage"
            scope.append("sourceKind: KeystoneSourceKind.OwnedSummon")
        elif effect in ("SacrificeShield", "StunSourceFilter"):
            # Named native adapters (C11/C08): the typed flag payload is the whole effect; no effect-set scope here.
            if field != "Grant" or sid not in ("h.aurena.key2", "h.cetus.key2"):
                self.fail(sid, path + ".effect", effect, "native adapter grants belong only to their named legacy keystone and are Grants")
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
        if field == "Grant":
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
        return obj("AuthoredKeystoneSpec", members)

    def keystone(self, sid, row):
        key = row["keystone"]
        retained = sid in RETAINED_POWER_KEYS and key["upsideSpec"] is None
        if retained:
            # The upside is the baseline keystone's own Power. The value is never re-typed here: the C# helper
            # (ManifestKeystone) reads it from the baseline node. This only confirms the manifest says the Power is kept.
            baseline = BASELINE_KEYS.get(sid)
            if baseline is None or not re.search(r"Power\." + baseline[0] + r"\s*=?\s*" + baseline[1] + r"(?!\d)", key["upside"]):
                self.fail(sid, "keystone.upside", key["upside"], "retained-Power key does not name the baseline Power " + display_value(baseline))
                return None
        if key["upsideSpec"] is None and not retained:
            self.fail(sid, "keystone.upsideSpec", None,
                      "prose-only key requires an explicit design-to-typed translation; no translation for this ID")
            return None
        up = [] if retained else list(key["upsideSpec"])
        expressions = [self.key_spec(sid, s, "keystone.upsideSpec[" + str(i) + "]") for i, s in enumerate(up)]
        sides = array(expressions, "AuthoredKeystoneSpec")
        required = sorted({s["receiver"] for s in up if s.get("receiver") and s["receiver"].startswith("St_")})
        cost = "Content.KeystoneCost" if row["region"] == "migration" else whole(row["rankCost"])
        # A migration row's `power` re-states the baseline Power as the typed upside. The Power must be the baseline
        # keystone's own; a different value is the design's explicit same-ID migration (旧StarShield20→15).
        migrated = self.typed_power(sid, row)
        if retained:
            return ("ManifestKeystone(" + cs(self.hero) + ", " + cs(sid) + ", " + array(required) + ", " + sides
                + ", prerequisites: " + array(row["requires"]) + ", cost: " + cost
                + (", migratedPowerValue: " + migrated[1] if migrated and migrated[1] != BASELINE_KEYS[sid][1] else "") + ")")
        return ("AuthoredKeystoneCompiler.Compile(" + cs(sid) + ", " + array(required) + ", " + sides
            + ", prerequisites: " + array(row["requires"]) + ", cost: " + cost
            + (", retainedPower: Power." + migrated[0] + ", retainedPowerValue: " + migrated[1] if migrated else "") + ")")

    def typed_power(self, sid, row):
        """(Power name, value) for a migration Keystone row that re-states its Power; None when the row has none."""
        power = row.get("power") if row["region"] == "migration" else None
        if not power:
            return None
        baseline = BASELINE_KEYS.get(sid)
        name, value = power["name"], number(power["perRank"])
        if baseline is None or name != baseline[0]:
            self.fail(sid, "power.name", name, "a typed keystone Power must be the baseline keystone's own Power " + display_value(baseline))
        elif name not in self.powers:
            self.fail(sid, "power.name", name, "no concrete Power enum member")
        elif value != value.to_integral_value() or value < 1:
            self.fail(sid, "power.perRank", power["perRank"], "a typed keystone Power value is a positive whole number")
        else:
            return name, whole(value)
        return None

    def run_growth(self, sid, row, path):
        g = row["growth"]
        effects = []
        for i, e in enumerate(g["effects"]):
            if e["stat"] not in self.stats:
                self.fail(sid, path + "growth.effects[" + str(i) + "].stat", e["stat"], "no concrete Stat enum member")
                continue
            try:
                milli = whole(number(e["amount"]) * 1000)
            except ValueError as ex:
                self.fail(sid, path + "growth.effects[" + str(i) + "].amount", e["amount"], "RunGrowth amounts are exact thousandths: " + str(ex))
                continue
            effects.append("new RunGrowthEffect(Stat." + e["stat"] + ", " + milli + ")")
        return "new RunGrowthDef(RunGrowthTrigger." + g["trigger"] + ", " + whole(g["threshold"]) + ", " + whole(g["cap"]) \
            + ", new RunGrowthEffect[] { " + ", ".join(effects) + " })"

    def run_growth_mod(self, sid, row, path):
        g = row["growth"]
        target = g["target"]
        if target is not None and self.by_id.get(target, {}).get("kind") != "RunGrowth":
            self.fail(sid, path + "growth.target", target, "a RunGrowth modifier targets a RunGrowth star of the same hero (or null for every RunGrowth of the hero)")
        return "new RunGrowthModifierDef(" + cs(target) + ", " + whole(g["capBonus"]) + ", " + whole(g["effectPct"]) + ", " + ("true" if g["doubleGain"] else "false") + ")"

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
        elif kind == "RunGrowth":
            # The important star that grants a run-long stacking mechanism: a Notable carrying the typed RunGrowthDef payload only.
            members["Kind"] = "ClusterStarKind.Notable"
            members["RunGrowth"] = self.run_growth(sid, row, option_path)
        elif kind == "RunGrowthMod":
            members["Kind"] = "ClusterStarKind.RunGrowthModifier"
            members["RunGrowthModifier"] = self.run_growth_mod(sid, row, option_path)
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
            if ids and ids[0] in self.pairs and not target["effect"]:
                # B/T/R of a retained bridge modify that bridge's own payoff payload (design N3: effectId = the old ring ID), never the memory as a
                # whole and never a receiver. The scoped memory is the pair's real payoff source: the row's memory/receiver is only the design
                # table's convenience label (for example ring.resolve names the mark side, its payoff source is the other endpoint).
                memory = self.payoff_memory(ids[0])
                scope = "EffectChannel"
            elif memory is None and ids and ids[0] in self.pairs:
                pair = self.pairs[ids[0]]
                # Only the named pair payoff is modified; opening memory GB is not a substitute.
                memory = next((m for m in (pair["a"], pair["b"]) if re.search(r'PairComboStep\.\w+,\s*"' + re.escape(m) + r'"', pair["line"])), None)
                if memory is None:
                    memory = next((r.get("receiver") or r.get("memory") for r in self.rows if r["id"] == ids[0]), None)
            if not memory or memory.startswith("@"):
                self.fail(sid, option_path + "memory", memory, "ScopedModifierDef needs one concrete owned scope memory")
            members["Memory"] = cs(memory)
            param = row["param"] if kind == "GimmickParam" else None
            if param in GATE_PARAMS:
                # WindowDuration / MarkDuration lengthen one bridge's own gate: only on a bridge-region row that names that bridge's pair, and
                # only when the pair has that gate (Window / Mark). The contract (AuthoredMechanisms.Supports) enforces the same.
                bridge = target["star"]
                if self.by_id[sid]["region"] != "bridge" or bridge not in self.pairs or target["effect"]:
                    self.fail(sid, option_path + "param", param, param + " is only valid on a bridge-region row whose target.star is the bridge's ring ID (target.effect null)")
                elif self.bridge_gate(bridge) != GATE_PARAMS[param]:
                    self.fail(sid, option_path + "param", param, param + " needs a " + GATE_PARAMS[param] + " pair but " + bridge + " has a " + self.bridge_gate(bridge) + " gate")
            # ScopeKind.Receiver needs an explicit recipient effect set even when
            # authored target is broad. It means recharge receipt, not movement use.
            if scope == "Receiver" and not effects and not ids:
                effects = ["Recharge"]
            # A whole-memory boost must not reach (a) a Reload, whose 1-charge value no percentage changes (FractionalScopedModifiers.ValidateTree
            # rejects it), nor (b) a directed Recv recharge delivered to another memory: that entry is boosted by its receiver-scope stars only,
            # and one entry may never take both a source and a receiver boost (AuthoredMechanisms.ComposeEntry). When the memory has either,
            # the boost names its exact recipients (like untargeted parameters do) instead of the whole memory.
            if kind == "GimmickBoost" and not ids and not effects and scope == "Memory":
                restricted = self.boost_recipients(sid, option_path, memory)
                if restricted is not None:
                    ids, effects = restricted
                    scope = "EffectChannel"
            # Untargeted parameters apply to meaningful fields, not every effect
            # that happens to use this memory. Bind exact supported IDs once.
            if param and not ids and scope != "Receiver":
                ids, supported, mixed = self.supporting_rows(memory, [], effects, param, with_detail=True)
                scope = "EffectChannel"
                # A Choice's options share the star ID. When one option supports the field and a sibling does not (a Shield option next to
                # a Recharge option), the ID alone would also reach the sibling, which has no meaningful recipient for the field: name
                # the supported effects too, so only the options that carry them are recipients.
                if mixed and not effects:
                    effects = supported
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
                # The effectful Stat root override in NormalizeAuthored is absent, so the
                # row's own anchor applies; stars without one inherit the cluster entry.
                anchor = row.get("anchor") or next((s["anchor"] for s in group if s.get("anchor")), None)
                if not self.outer_anchor_owned(anchor):
                    self.fail(sid, "region", "outer", "outer cluster " + row["cluster"] + " anchor " + display_value(anchor) + " is neither a same-hero native route star, a same-hero authored memory entrance, a same-cluster effectful Stat root nor a registered outer anchor stat; AuthoredStarContract.VerifyOwnership rejects it")
            return "ClusterRegion.Outer"
        return "new ClusterRegion { Kind = ClusterRegionKind.Keystone }"

    def receiver_only_bridge(self, row):
        if row["region"] != "bridge":
            return None
        anchors = [s.get("anchor") for s in self.rows if s.get("cluster") == row["cluster"] and s.get("anchor")]
        return next((a for a in anchors if a in RECEIVER_ONLY_BRIDGES and a not in self.pairs), None)

    def outer_anchor_owned(self, anchor):
        """Mirror AuthoredStarContract.VerifyOwnership: the outer entrances the contract owns."""
        if anchor is None:
            return False
        if anchor.startswith("h." + self.name + ".route."):
            route = anchor.rsplit(".", 1)[0]
            if any(key.startswith(route + ".") for key in self.route_rows):
                return True
        row = self.by_id.get(anchor)
        if row is not None and row.get("region") == "memory" and anchor.startswith(self.name + "."):
            return True
        return (self.hero, anchor) in self.outer_anchors

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
        # A Choice carries its effects in its options: the receiver and every source memory of every option are owned by the star.
        effects = [row] + list(row.get("options") or [])
        receivers = sorted({e["receiver"] for e in effects if e.get("receiver") and e["receiver"].startswith("St_")})
        if receivers:
            # One target memory per star; the further receivers of a Choice's other options are owned as explicit cross-sources.
            # Identity-triggered payloads (an "@ID" memory) name the identities that start them: those are owned memories too.
            identities = {m for e in effects for m in ((e.get("gimmick") or {}).get("triggerByIdentity") or {})}
            sources = sorted({m for e in effects for m in re.findall(r"St_\w+", e.get("memory") or "") if m in self.known_memories}
                             | set(receivers[1:]) | identities)
            ownership = obj("MemoryOwnership", {"TargetMemory": cs(receivers[0]), "SourceMemories": array(sources)})
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
            owner = error.split(":", 1)[0].split("#", 1)[0].split(".upsideSpec", 1)[0]
            affected = [owner] if owner in self.by_id else [s["id"] for s in self.rows]
            for sid in affected:
                self.fail(sid, "canonical", None, error)
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
                # A star that modifies a retained bridge's payload (target.star = h.<hero>.ring.*) needs that bridge's typed base binding.
                modified = (effect.get("target") or {}).get("star")
                if modified in self.pairs:
                    bridges.add(modified)
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
        # One rule per manifest migration row: the star keeps its ID and ranks, its effect changed. Cost is read from the baseline node.
        migrations = ["ManifestMigration(" + cs(self.hero) + ", " + cs(row["id"]) + ", " + str(row["maxRank"]) + ")"
                      for row in self.rows if row["region"] == "migration"]
        lines += ["", "        public static LegacyStarMigration[] Create" + title + "Migrations()", "        {",
                  "            return new LegacyStarMigration[]", "            {"]
        lines += ["                " + expression + ("," if i < len(migrations) - 1 else "") for i, expression in enumerate(migrations)]
        lines += ["            };", "        }"]
        return "\n".join(lines + ["    }", "}", ""])


def render_diagnostic(result):
    """tests/SodRpg.Core.Tests/Diagnostics/<Hero>.cs: every compilable row, each guarded so that a definition whose construction throws
    (a typed payload constructor rejecting its arguments) is reported per star instead of aborting the hero. Failed rows are omitted.
    Test-only and git-ignored: the registration diagnostics find it by reflection."""
    title = result.name.title()
    failed_ids = sorted({f.star.split(".grant", 1)[0] for f in result.failures})
    bridges = set()
    for row in result.rows:
        for effect in [row] + (row.get("options") or []):
            condition = (effect.get("gimmick") or {}).get("condition")
            if condition:
                bridges.add(condition.split(":", 1)[1])
            modified = (effect.get("target") or {}).get("star")
            if modified in result.pairs:
                bridges.add(modified)
    rows = list(result.mapped.items())
    rows.extend((bridge, "ManifestBaselinePair(" + cs(result.hero) + ", " + cs(bridge) + ")") for bridge in sorted(bridges) if bridge not in result.by_id)
    needs_layout = any("ManifestRouteEntry(" in expression for _, expression in rows)
    layout_arg = ", baselineLayout" if needs_layout else ""
    chunks = [rows[i:i + 32] for i in range(0, len(rows), 32)]
    lines = ["// Generated by tools/star-manifest/gen_cs.py --diagnostic; do not edit. Test-only (git-ignored).",
             "using System;", "using System.Collections.Generic;", "using SodRpg.Core.Game;", "using static SodRpg.Core.Game.StarClusters;", "",
             "namespace SodRpg.Core.Tests.DiagnosticMaps", "{", "    public static class " + title, "    {",
             "        public static readonly string[] Omitted = " + array(failed_ids) + ";", "",
             "        private static AuthoredStarDef Guard(List<string> failures, string id, Func<AuthoredStarDef> create)",
             "        {", "            try { return create(); }", "            catch (Exception error) { failures.Add(id + \": \" + error.GetType().Name + \": \" + error.Message); return null; }", "        }", "",
             "        public static AuthoredStarDef[] Create(List<string> constructionFailures)", "        {",
             "            var definitions = new AuthoredStarDef[" + str(len(rows)) + "];"]
    if needs_layout:
        lines.append("            var baselineLayout = HeroTreeLayout.ForTalents(HeroSigils.BaselineTreeFor(" + cs(result.hero) + "));")
    for i in range(len(chunks)):
        lines.append("            Fill" + str(i) + "(definitions, constructionFailures" + layout_arg + ");")
    lines += ["            return Array.FindAll(definitions, d => d != null);", "        }"]
    for i, chunk in enumerate(chunks):
        lines += ["", "        private static void Fill" + str(i) + "(AuthoredStarDef[] definitions, List<string> failures" + (", HeroTreeLayout baselineLayout" if needs_layout else "") + ")", "        {"]
        lines += ["            definitions[" + str(i * 32 + j) + "] = Guard(failures, " + cs(sid) + ", () => " + expression + ");" for j, (sid, expression) in enumerate(chunk)]
        lines.append("        }")
    migrations = ["ManifestMigration(" + cs(result.hero) + ", " + cs(row["id"]) + ", " + str(row["maxRank"]) + ")"
                  for row in result.rows if row["region"] == "migration" and row["id"] not in failed_ids]
    lines += ["", "        public static LegacyStarMigration[] Migrations()", "        {", "            return new LegacyStarMigration[]", "            {"]
    lines += ["                " + expression + ("," if i < len(migrations) - 1 else "") for i, expression in enumerate(migrations)]
    lines += ["            };", "        }", "    }", "}", ""]
    return chr(10).join(lines)


def load(name):
    with (HERE / (name + ".json")).open(encoding="utf-8") as stream:
        return json.load(stream)


def registered_heroes():
    """Heroes whose generated map passed the real registration (tools/star-manifest/registered.txt, one hero per line)."""
    path = HERE / "registered.txt"
    if not path.is_file():
        return set()
    return {line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip() and not line.startswith("#")}


def registration(compiled):
    """The single production entry point. Every compiled hero can be registered by RegisterGeneratedHero (tests and the
    registration diagnostics); only heroes listed in registered.txt (verified to register cleanly) are in GeneratedHeroes
    and so installed in the game. A hero that fails real registration must never be listed."""
    compiled = sorted(compiled)
    allowed = registered_heroes()
    unknown = allowed - set(compiled)
    if unknown:
        raise ValueError("registered.txt lists heroes without generated C#: " + ", ".join(sorted(unknown)))
    names = compiled
    heroes = ", ".join(cs("Hero_" + name.title()) for name in names if name in allowed)
    all_heroes = ", ".join(cs("Hero_" + name.title()) for name in names)
    lines = ["// Generated by tools/star-manifest/gen_cs.py; do not edit.", "using System;", "using System.Collections.Generic;", "",
             "namespace SodRpg.Core.Game", "{", "    public static partial class StarClusters", "    {",
             "        private static readonly object GeneratedLock = new object();",
             "        private static bool generatedRegistered;", "",
             "        /// <summary>Heroes whose complete star map was generated from the manifest (never a partial hero).</summary>",
             "        public static readonly IReadOnlyList<string> GeneratedHeroes = " + (
                 "Array.AsReadOnly(new string[] { " + heroes + " });" if heroes else "Array.AsReadOnly(new string[0]);"), "",
             "        /// <summary>Every hero whose manifest compiled to C# (a superset of GeneratedHeroes; used by the registration diagnostics).</summary>",
             "        public static readonly IReadOnlyList<string> CompiledHeroes = " + (
                 "Array.AsReadOnly(new string[] { " + all_heroes + " });" if all_heroes else "Array.AsReadOnly(new string[0]);"), "",
             "        /// <summary>Install one generated hero's authored tree, then its migration rules (tests and tools; production uses RegisterAllGenerated).</summary>",
             "        public static void RegisterGeneratedHero(string heroKey)", "        {", "            switch (heroKey)", "            {"]
    for name in names:
        title = name.title()
        lines += ["                case " + cs("Hero_" + title) + ":",
                  "                    RegisterAuthored(" + cs("Hero_" + title) + ", Create" + title + "Authored());",
                  "                    RegisterMigrations(" + cs("Hero_" + title) + ", Create" + title + "Migrations());",
                  "                    return;"]
    lines += ["                default: throw new ArgumentException(\"No generated star map for \" + heroKey);", "            }", "        }", ""]
    for what in ("Authored", "Migrations"):
        lines += ["        /// <summary>The generated " + what.lower() + " of one compiled hero (registration diagnostics).</summary>",
                  "        public static " + ("AuthoredStarDef[]" if what == "Authored" else "LegacyStarMigration[]") + " CreateGenerated" + what + "(string heroKey)",
                  "        {", "            switch (heroKey)", "            {"]
        lines += ["                case " + cs("Hero_" + name.title()) + ": return Create" + name.title() + what + "();" for name in names]
        lines += ["                default: throw new ArgumentException(\"No generated star map for \" + heroKey);", "            }", "        }", ""]
    lines += [
              "        /// <summary>Install every generated hero's authored tree and its migration rules. Idempotent and thread-safe.</summary>",
              "        public static void RegisterAllGenerated()", "        {", "            lock (GeneratedLock)", "            {",
              "                if (generatedRegistered) return;",
              "                foreach (string hero in GeneratedHeroes) RegisterGeneratedHero(hero);",
              "                generatedRegistered = true;", "            }", "        }", "    }", "}", ""]
    return chr(10).join(lines)


def report(results):
    lines = ["# v1.31 manifest generation report", "", "Generated with `python tools/star-manifest/gen_cs.py --all --report`.", "",
             "Report mode writes no C# and performs no production registration. A clean row has an exact C# field translation; it is **not** a successful whole-tree registration, producer-envelope check or consumer smoke. A hero with any failed row cannot be published.", "",
             "Counts include migration rows and shared outer 160 expanded per hero; options are not additional stars. Failure totals count unique stars, while the groups below list every failing field (a star can appear in several groups).", "",
             "| Hero | Manifest rows | Outer rows | Clean | Failed stars |", "|---|---:|---:|---:|---:|"]
    for result in results:
        failed = {f.star.split(".grant", 1)[0] for f in result.failures}
        lines.append(f"| {result.hero} | {len(result.rows) - 160} | 160 | {len(result.mapped)} | {len(failed)} |")
    lines += ["", "## Retained-Power keystones", "",
              "A legacy keystone whose manifest upside is its existing Power (`h.<hero>.key` / `key2`) compiles with `ManifestKeystone`, which reads the Power and its value from the baseline node (`KeystoneDefinition.RetainedPower`). `Build.Compute` adds that Power once from the keystone node. Keys whose Power is replaced stay failures below.", "",
              "Baseline Vesper has 73 purchase nodes; the intended complete tree has 73 + 645 private new + 160 outer = **878 purchase stars, plus the layout start node**. Manifest rows include 33 baseline replacements, not 33 extra graph nodes.", ""]
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
    parser.add_argument("--diagnostic", action="store_true", help="also write the guarded test-only maps used by RegistrationDiagnostics (tests/SodRpg.Core.Tests/Diagnostics, git-ignored)")
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
        failed_heroes = [result for result in results if result.failures]
        if args.report:
            if failed_heroes:
                print("Report only: no generated files written; some selected heroes have unmapped rows.", file=sys.stderr)
            return 1 if failed_heroes else 0
        # A hero is written only when it compiles with zero failures, never partially. Every selected hero is
        # translated before the first output mutation, so a render error leaves existing output untouched.
        clean = [result for result in results if not result.failures]
        outputs = [(OUTPUT / (r.name.title() + ".Generated.cs"), r.render()) for r in clean]
        selected_failed = {r.name for r in failed_heroes}
        available = {name for name in HEROES if (OUTPUT / (name.title() + ".Generated.cs")).is_file() and name not in selected_failed}
        available.update(r.name for r in clean)
        outputs.append((OUTPUT / "GeneratedRegistration.cs", registration(available)))
        if args.diagnostic:
            outputs.extend((DIAGNOSTICS / (r.name.title() + ".cs"), render_diagnostic(r)) for r in results)
        for path, content in outputs:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8", newline=chr(10))
            print("Wrote " + path.relative_to(ROOT).as_posix())
        for result in failed_heroes:
            stale = OUTPUT / (result.name.title() + ".Generated.cs")
            print("Not generated: " + result.name + " has unmapped rows" + ("; its older " + stale.name + " is left in place but is NOT registered. Delete it or regenerate." if stale.is_file() else ""), file=sys.stderr)
        return 1 if failed_heroes else 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("Generator failed: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

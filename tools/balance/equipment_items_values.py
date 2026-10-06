"""Compile equipment numeric sources into scalar C# constants (no runtime JSON)."""
import re
from decimal import Decimal
from pathlib import Path
from equipment_common import load_json, fingerprint_records, quote

ROOT = Path(__file__).resolve().parents[2]
TABLES = Path(__file__).resolve().parent / "equipment"
OUTPUT = ROOT / "src/SodRpg.Core/Game/Balance/EquipmentItems.Generated.cs"
LEGACY_IDENTITY = "3049-074d129c4d1603c0"


def constant_name(domain, item_id, suffix=""):
    return domain + "_" + re.sub(r"\W", "_", item_id) + suffix


def _keys(value, keys, path):
    if not isinstance(value, dict) or set(value) != set(keys):
        raise ValueError(f"{path}: expected fields {', '.join(keys)}")


def _integer(value, path):
    if type(value) is not int or not -2147483648 <= value <= 2147483647:
        raise ValueError(f"{path}: expected int32")
    return value


def _decimal(value, path):
    if type(value) not in (int, Decimal):
        raise ValueError(f"{path}: expected decimal")
    number = Decimal(value)
    if not number.is_finite() or number < 0 or number > Decimal("2147483.647") or number * 1000 != (number * 1000).to_integral_value():
        raise ValueError(f"{path}: expected nonnegative Link.Value representable as int32 milli")
    return number


def _authored_coverage():
    text = (ROOT / "src/SodRpg.Core/Game/Content.cs").read_text(encoding="utf-8")
    bases = dict(re.findall(r'new BaseDef\("([^"\n]+)",.*?Stat\.(\w+),', text))
    section = text[text.index("IReadOnlyList<UniqueDef> Uniques"):text.index("IReadOnlyList<SetDef> Sets")]
    uniques = {}
    for item_id, body in re.findall(r'new UniqueDef\("([^"\n]+)"(.*?)(?=new UniqueDef|\Z)', section, re.S):
        uniques[item_id] = (re.findall(r"Power\.(\w+),", body), re.search(r"Kind = LinkKind\.(\w+)", body))
    for path in (ROOT / "src/SodRpg.Core/Game").glob("BossProfiles.*.cs"):
        body = path.read_text(encoding="utf-8")
        for item_id in re.findall(r'new UniqueDef\("([^"\n]+)"', body):
            uniques[item_id] = ([], None)
        if path.name == "BossProfiles.Ink.cs":
            constants = dict(re.findall(r'const string (\w+) = "([^"]+)"', body))
            for key, slot in re.findall(r'Add\((\w+),"(\w+)"', body):
                uniques[constants[key] + "." + slot] = ([], None)
    return bases, uniques


def load_bases(path=None):
    data = load_json(path or TABLES / "bases.json")
    _keys(data, ("schemaVersion", "bases"), "bases")
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1 or not isinstance(data["bases"], dict):
        raise ValueError("bases: unsupported schema")
    authored, _ = _authored_coverage()
    if set(data["bases"]) != set(authored):
        raise ValueError("bases: IDs must cover every authored base exactly")
    for item_id, row in data["bases"].items():
        _keys(row, ("stat", "implicitValue"), item_id)
        if row["stat"] != authored[item_id]:
            raise ValueError(f"{item_id}/stat: must match authored relation")
        _integer(row["implicitValue"], item_id + "/implicitValue")
    return data["bases"]


def load_uniques(path=None):
    data = load_json(path or TABLES / "uniques.json")
    _keys(data, ("schemaVersion", "uniques"), "uniques")
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1 or not isinstance(data["uniques"], dict):
        raise ValueError("uniques: unsupported schema")
    _, authored = _authored_coverage()
    if set(data["uniques"]) != set(authored):
        raise ValueError("uniques: IDs must cover every authored unique exactly")
    for item_id, row in data["uniques"].items():
        _keys(row, ("powers", "link"), item_id)
        powers, link = authored[item_id]
        if not isinstance(row["powers"], list) or len(row["powers"]) != len(powers):
            raise ValueError(f"{item_id}/powers: must match authored power slots")
        for index, power in enumerate(row["powers"]):
            _keys(power, ("power", "value"), f"{item_id}/powers/{index}")
            if power["power"] != powers[index]:
                raise ValueError(f"{item_id}/powers/{index}/power: must match authored relation")
            _integer(power["value"], f"{item_id}/powers/{index}/value")
        if link is None:
            if row["link"] is not None:
                raise ValueError(f"{item_id}/link: not authored")
        else:
            _keys(row["link"], ("kind", "value"), item_id + "/link")
            if row["link"]["kind"] != link.group(1):
                raise ValueError(f"{item_id}/link/kind: must match authored relation")
            _decimal(row["link"]["value"], item_id + "/link/value")
    return data["uniques"]


def load():
    return load_bases(), load_uniques()


def semantic_records(bases, uniques):
    records = [f'base:{key}:{row["stat"]}:int:{row["implicitValue"]}' for key, row in bases.items()]
    for key, row in uniques.items():
        records.extend(f'unique:{key}:power:{index}:{power["power"]}:int:{power["value"]}' for index, power in enumerate(row["powers"]))
        if row["link"] is not None:
            link = row["link"]
            records.append(f'unique:{key}:link:{link["kind"]}:milli:{int(Decimal(link["value"]) * 1000)}')
    return records


def render_outputs():
    bases, uniques = load()
    lines = ["// <auto-generated />", "namespace SodRpg.Core.Game", "{", "    internal static class EquipmentItemsBalanceValues", "    {"]
    names = set()
    def scalar(kind, name, value):
        if name in names:
            raise ValueError(f"constant name collision: {name}")
        names.add(name)
        lines.append(f"        internal const {kind} {name} = {value};")
    for key, row in sorted(bases.items()):
        scalar("int", constant_name("Base", key), row["implicitValue"])
    for key, row in sorted(uniques.items()):
        for index, power in enumerate(row["powers"]):
            scalar("int", constant_name("Unique", key, f"_Power{index}"), power["value"])
        if row["link"] is not None:
            scalar("decimal", constant_name("Unique", key, "_Link"), format(Decimal(row["link"]["value"]), "f") + "m")
    records = fingerprint_records("items", semantic_records(bases, uniques), LEGACY_IDENTITY)
    if records:
        lines.extend(["        internal static readonly string[] ContentFingerprintRecords =", "        {"])
        lines.extend("            " + quote(record) + "," for record in records)
        lines.append("        };")
    else:
        lines.append("        internal static readonly string[] ContentFingerprintRecords = System.Array.Empty<string>();")
    lines.extend(["    }", "}", ""])
    outputs = {OUTPUT: "\n".join(lines)}
    import importlib.util
    spec = importlib.util.spec_from_file_location(
        "equipment_base_metadata", ROOT / "tools/lowrarity/gen_lrdata.py")
    metadata = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(metadata)
    outputs.update(metadata.render_outputs())
    return outputs

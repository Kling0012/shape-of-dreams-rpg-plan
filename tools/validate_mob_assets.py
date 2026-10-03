#!/usr/bin/env python3
"""Verify the Dreamborne source pack with Python's standard library only.

This is an offline integrity/structure check, not a Unity runtime validation.
Use --source-pack ORIGINAL_DIRECTORY to additionally reproduce path sanitization
and compare every checked-in binary with the original delivery.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import struct
import sys
import zlib

ROOT = Path(__file__).resolve().parents[1]
PACK = ROOT / "assets" / "dreamborne"
MODEL_IDS = (
    "ember_warden", "mirecap_stomper", "duskwing_oracle", "thorncrown_stag",
    "frostjaw_prowler", "runestone_tortoise", "glasswing_moth",
    "obsidian_scorpion", "lantern_wisp", "abyssal_bell",
)
EXPECTED_TOTALS = dict(models=10, meshes=10, triangles=20562, bones=209, clips=30)
PRIVATE_PATH = re.compile(rb"/(?:workspace|home|root)/|[A-Za-z]:[\\/]")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def safe_file(root, relative):
    require(isinstance(relative, str) and relative, "empty/non-string asset path")
    path = PurePosixPath(relative)
    require(not path.is_absolute() and ".." not in path.parts and "\\" not in relative,
            f"unsafe asset path: {relative}")
    result = root.joinpath(*path.parts)
    require(result.resolve().is_relative_to(root.resolve()), f"asset escapes pack: {relative}")
    return result


def fbx_tree(data):
    """Parse FBX 7.4 nodes, retaining every non-string property byte verbatim."""
    require(data.startswith(b"Kaydara FBX Binary  \x00\x1a\x00"), "invalid binary FBX header")
    require(len(data) >= 27 and struct.unpack_from("<I", data, 23)[0] == 7400,
            "this pack requires binary FBX 7400")
    scalar_sizes = {ord("Y"): 2, ord("C"): 1, ord("I"): 4, ord("F"): 4,
                    ord("D"): 8, ord("L"): 8}

    def node(offset):
        require(offset + 13 <= len(data), "truncated FBX node")
        end, count, prop_length, name_length = struct.unpack_from("<IIIB", data, offset)
        require(offset + 13 + name_length <= end <= len(data), "invalid FBX node bounds")
        name = data[offset + 13:offset + 13 + name_length]
        cursor = offset + 13 + name_length
        prop_end = cursor + prop_length
        require(prop_end <= end, "FBX properties exceed node")
        properties = []
        for _ in range(count):
            require(cursor < prop_end, "truncated FBX property")
            start = cursor
            kind = data[cursor]
            cursor += 1
            if kind in scalar_sizes:
                cursor += scalar_sizes[kind]
            elif kind in b"SR":
                require(cursor + 4 <= prop_end, "truncated FBX string length")
                length = struct.unpack_from("<I", data, cursor)[0]
                cursor += 4 + length
            elif kind in b"fdlibc":
                require(cursor + 12 <= prop_end, "truncated FBX array length")
                length = struct.unpack_from("<I", data, cursor + 8)[0]
                cursor += 12 + length
            else:
                raise ValueError(f"unsupported FBX property type: {kind}")
            require(cursor <= prop_end, "FBX property exceeds bounds")
            properties.append(data[start:cursor])
        require(cursor == prop_end, "FBX property count/length mismatch")
        children = []
        has_null = False
        while cursor < end:
            if data[cursor:cursor + 13] == bytes(13):
                cursor += 13
                has_null = True
                break
            child, cursor = node(cursor)
            children.append(child)
        require(cursor == end, "FBX child length mismatch")
        return (name, properties, children, has_null), end

    nodes, cursor = [], 27
    while data[cursor:cursor + 13] != bytes(13):
        item, cursor = node(cursor)
        nodes.append(item)
    require(cursor + 13 <= len(data), "missing FBX root terminator")
    return nodes, data[cursor + 13:]


def fbx_take_names(nodes):
    names = []
    for name, properties, children, _ in nodes:
        if name == b"Take":
            names.extend(prop[5:].decode("utf-8") for prop in properties if prop[:1] == b"S")
        names.extend(fbx_take_names(children))
    return names


def sanitize_fbx(data):
    """Only rewrite source/texture path strings; retain all geometry and animation."""
    nodes, footer = fbx_tree(data)

    def clean_string(value):
        # Texture paths are made portable, including original .fbm references.
        if (b"/" in value or b"\\" in value) and value.lower().endswith(b".png"):
            return b"Textures/" + value.replace(b"\\", b"/").rsplit(b"/", 1)[-1]
        if PRIVATE_PATH.search(value):
            return value.replace(b"\\", b"/").rsplit(b"/", 1)[-1]
        return value

    def encode(item, offset):
        name, properties, children, has_null = item
        props = []
        for prop in properties:
            if prop[:1] == b"S":
                value = clean_string(prop[5:])
                prop = b"S" + struct.pack("<I", len(value)) + value
            props.append(prop)
        properties = b"".join(props)
        cursor = offset + 13 + len(name) + len(properties)
        encoded_children = []
        for child in children:
            result = encode(child, cursor)
            encoded_children.append(result)
            cursor += len(result)
        null = bytes(13) if has_null else b""
        cursor += len(null)
        return (struct.pack("<IIIB", cursor, len(props), len(properties), len(name)) +
                name + properties + b"".join(encoded_children) + null)

    parts, cursor = [data[:27]], 27
    for item in nodes:
        result = encode(item, cursor)
        parts.append(result)
        cursor += len(result)
    return b"".join(parts) + bytes(13) + footer


def sanitize_blend(data):
    """Clear machine-local string fields in-place, preserving all block offsets.

    Blender fixed-size C strings can retain tails of earlier absolute paths after
    the first NUL. Those stale printable fragments are also zeroed, while image
    paths are changed to //Textures/<original filename>. No mesh/rig data changes.
    """
    require(data.startswith(b"BLENDER"), "expected uncompressed Blender source")
    result = bytearray(data)
    for match in re.finditer(rb"[ -~]{4,}", data):
        value = match.group()
        if (re.search(rb"/(?:workspace|home|root)/", value) or b"dream_mob_collection/" in value or
                value in (b"/tmp/", b"//../../../../Desktop/")):
            replacement = b""
            if value.startswith(b"/") and b"/Textures/" in value and value.endswith(b".png"):
                replacement = b"//Textures/" + value.rsplit(b"/", 1)[-1]
            require(len(replacement) <= len(value), "replacement exceeds Blender string field")
            result[match.start():match.end()] = replacement.ljust(len(value), b"\x00")
    return bytes(result)


def sanitized_source(data, suffix):
    if suffix == ".fbx":
        return sanitize_fbx(data)
    if suffix == ".blend":
        return sanitize_blend(data)
    return data


def read_glb(data):
    require(len(data) >= 20, "truncated GLB")
    magic, version, length = struct.unpack_from("<4sII", data)
    require((magic, version, length) == (b"glTF", 2, len(data)), "invalid GLB header/length")
    chunks, cursor = [], 12
    while cursor < len(data):
        require(cursor + 8 <= len(data), "truncated GLB chunk header")
        size, kind = struct.unpack_from("<II", data, cursor)
        require(size % 4 == 0 and cursor + 8 + size <= len(data), "invalid GLB chunk length")
        chunks.append((kind, data[cursor + 8:cursor + 8 + size]))
        cursor += 8 + size
    require([kind for kind, _ in chunks] == [0x4E4F534A, 0x004E4942],
            "expected a self-contained JSON+BIN GLB")
    return json.loads(chunks[0][1]), chunks[1][1]


def accessor_values(document, binary, index):
    accessors = document.get("accessors", [])
    require(isinstance(index, int) and 0 <= index < len(accessors), "invalid accessor index")
    accessor = accessors[index]
    require("sparse" not in accessor, "sparse accessors not used by this pack")
    formats = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
    widths = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
    require(accessor.get("componentType") in formats and accessor.get("type") in widths,
            "unsupported accessor format")
    view_index = accessor.get("bufferView", -1)
    views = document.get("bufferViews", [])
    require(0 <= view_index < len(views), "invalid accessor bufferView")
    view = views[view_index]
    require(view.get("buffer", 0) == 0, "external GLB buffer")
    format_string = "<" + formats[accessor["componentType"]] * widths[accessor["type"]]
    size = struct.calcsize(format_string)
    stride = view.get("byteStride", size)
    count = accessor.get("count", 0)
    offset = accessor.get("byteOffset", 0)
    require(count > 0 and stride >= size and offset >= 0, "invalid accessor dimensions")
    end = offset + (count - 1) * stride + size
    require(end <= view["byteLength"], "accessor exceeds bufferView")
    start = view.get("byteOffset", 0) + offset
    require(start >= 0 and start + (count - 1) * stride + size <= len(binary), "accessor exceeds BIN")
    values = [struct.unpack_from(format_string, binary, start + i * stride) for i in range(count)]
    require(all(math.isfinite(v) for row in values for v in row), "non-finite accessor value")
    return values


def inspect_glb(data, model):
    doc, binary = read_glb(data)
    require(doc.get("asset", {}).get("version") == "2.0", "invalid glTF asset version")
    buffers = doc.get("buffers", [])
    require(len(buffers) == 1 and "uri" not in buffers[0], "GLB must be self-contained")
    require(0 <= len(binary) - buffers[0]["byteLength"] <= 3, "BIN byteLength mismatch")
    for view in doc.get("bufferViews", []):
        start, size = view.get("byteOffset", 0), view["byteLength"]
        require(view.get("buffer", 0) == 0 and start >= 0 and size > 0 and
                start + size <= buffers[0]["byteLength"], "invalid bufferView bounds")
    meshes, skins, nodes = doc.get("meshes", []), doc.get("skins", []), doc.get("nodes", [])
    require(len(meshes) == model["meshCount"] == 1, "runtime mesh count mismatch")
    require(len(skins) == 1, "expected one skin")
    joints = skins[0].get("joints", [])
    require(len(joints) == model["bones"] and len(set(joints)) == len(joints), "bone count mismatch")
    require(all(0 <= joint < len(nodes) for joint in joints), "invalid joint node")
    require(doc["accessors"][skins[0]["inverseBindMatrices"]]["type"] == "MAT4",
            "inverse bind accessor must be MAT4")
    require(len(accessor_values(doc, binary, skins[0]["inverseBindMatrices"])) == len(joints),
            "inverse bind matrix count mismatch")
    require(any(node.get("mesh") == 0 and node.get("skin") == 0 for node in nodes),
            "mesh is not attached to skin")
    triangles = 0
    for primitive in meshes[0]["primitives"]:
        require(primitive.get("mode", 4) == 4, "non-triangle primitive")
        attributes = primitive["attributes"]
        require({"POSITION", "NORMAL", "TEXCOORD_0", "JOINTS_0", "WEIGHTS_0"} <= attributes.keys(),
                "missing mesh/skinning attributes")
        for semantic, expected_type in (("POSITION", "VEC3"), ("NORMAL", "VEC3"),
                                        ("TEXCOORD_0", "VEC2"), ("JOINTS_0", "VEC4"), ("WEIGHTS_0", "VEC4")):
            require(doc["accessors"][attributes[semantic]]["type"] == expected_type,
                    f"invalid {semantic} accessor type")
        require(doc["accessors"][attributes["JOINTS_0"]]["componentType"] in {5121, 5123},
                "skin joint accessor must use unsigned bytes or shorts")
        require(doc["accessors"][attributes["WEIGHTS_0"]]["componentType"] == 5126,
                "this pack uses float skin weights")
        positions = accessor_values(doc, binary, attributes["POSITION"])
        for semantic in ("NORMAL", "TEXCOORD_0"):
            require(len(accessor_values(doc, binary, attributes[semantic])) == len(positions),
                    f"{semantic} vertex count mismatch")
        weights = accessor_values(doc, binary, attributes["WEIGHTS_0"])
        joint_rows = accessor_values(doc, binary, attributes["JOINTS_0"])
        require(len(positions) == len(weights) == len(joint_rows), "skinning vertex count mismatch")
        for joint_row, weight_row in zip(joint_rows, weights):
            require(all(isinstance(j, int) and 0 <= j < len(joints) for j in joint_row),
                    "out-of-range skin joint")
            require(all(0 <= w <= 1 for w in weight_row) and abs(sum(weight_row) - 1) < 1e-5,
                    "invalid skin weights")
        indices = accessor_values(doc, binary, primitive["indices"])
        require(len(indices) % 3 == 0, "triangle index count is not divisible by three")
        require(all(len(row) == 1 and isinstance(row[0], int) and 0 <= row[0] < len(positions)
                    for row in indices), "out-of-range triangle index")
        triangles += len(indices) // 3
    require(triangles == model["triangles"], "triangle count mismatch")
    require(len(doc.get("materials", [])) == model["materialCount"], "material count mismatch")
    images = doc.get("images", [])
    require(len(images) == 3 and all("uri" not in im and im.get("mimeType") == "image/png" for im in images),
            "expected three embedded PNG textures")
    for im in images:
        view = doc["bufferViews"][im["bufferView"]]
        start = view.get("byteOffset", 0)
        inspect_png(binary[start:start + view["byteLength"]])
    animations = doc.get("animations", [])
    expected_clips = {clip["name"]: clip for clip in model["clips"]}
    require(set(expected_clips) == {"Idle", "Walk", "Attack"} and len(model["clips"]) == 3,
            "manifest clips must be unique Idle/Walk/Attack")
    require(len(animations) == 3 and {clip.get("name") for clip in animations} == set(expected_clips),
            "animation names/count mismatch")
    for animation in animations:
        samplers = animation.get("samplers", [])
        require(samplers and animation.get("channels"), "empty animation")
        max_time, min_time = 0, math.inf
        for sampler in samplers:
            times = accessor_values(doc, binary, sampler["input"])
            require(all(len(row) == 1 and row[0] >= 0 for row in times), "invalid animation time")
            require(all(a[0] < b[0] for a, b in zip(times, times[1:])), "unsorted animation time")
            values = accessor_values(doc, binary, sampler["output"])
            require(sampler.get("interpolation", "LINEAR") in {"LINEAR", "STEP"},
                    "unexpected interpolation in source pack")
            require(len(values) == len(times), "animation input/output count mismatch")
            max_time = max(max_time, times[-1][0])
            min_time = min(min_time, times[0][0])
        require(abs(max_time - min_time - expected_clips[animation["name"]]["durationSeconds"]) < 1e-4,
                f"{animation['name']} duration mismatch")
        for channel in animation["channels"]:
            target = channel["target"]
            require(0 <= channel["sampler"] < len(samplers) and target["node"] in joints,
                    "invalid animation target/sampler")
            require(target["path"] in {"translation", "rotation", "scale"}, "invalid animation path")
    return dict(meshes=len(meshes), triangles=triangles, bones=len(joints), clips=len(animations))


def inspect_png(data):
    require(data.startswith(b"\x89PNG\r\n\x1a\n"), "invalid PNG signature")
    cursor, dimensions, idat, saw_end = 8, None, bytearray(), False
    while cursor < len(data):
        require(cursor + 12 <= len(data), "truncated PNG chunk")
        size = struct.unpack_from(">I", data, cursor)[0]
        kind = data[cursor + 4:cursor + 8]
        content = data[cursor + 8:cursor + 8 + size]
        require(cursor + 12 + size <= len(data), "PNG chunk exceeds file")
        crc = struct.unpack_from(">I", data, cursor + 8 + size)[0]
        require(zlib.crc32(kind + content) & 0xffffffff == crc, "PNG CRC mismatch")
        if kind == b"IHDR":
            require(size == 13 and dimensions is None, "invalid PNG IHDR")
            dimensions = struct.unpack_from(">II", content)
            require(dimensions in {(128, 128), (256, 256)}, "unexpected source atlas dimensions")
        elif kind == b"IDAT":
            idat.extend(content)
        elif kind == b"IEND":
            require(size == 0 and cursor + 12 == len(data), "invalid PNG IEND")
            saw_end = True
        cursor += 12 + size
    require(dimensions and idat and saw_end, "incomplete PNG")
    require(zlib.decompress(idat), "empty PNG image data")
    return dimensions


def validate_pack(pack=PACK, source_pack=None):
    manifest = json.loads((pack / "manifest.json").read_text(encoding="utf-8"))
    require(manifest.get("schemaVersion") == 1, "unsupported manifest schema")
    require(manifest.get("packId") == "dreamborne_enemies_vol01", "unexpected pack ID")
    models, files = manifest["models"], manifest["files"]
    require(tuple(model["modelId"] for model in models) == MODEL_IDS, "missing/reordered/duplicate models")
    require(manifest["totals"] == EXPECTED_TOTALS, "unexpected declared pack totals")
    paths = [entry["path"] for entry in files]
    require(len(set(paths)) == len(paths), "duplicate manifest file path")
    referenced = set()
    totals = dict(models=len(models), meshes=0, triangles=0, bones=0, clips=0)
    for entry in files:
        path = safe_file(pack, entry["path"])
        require(path.is_file(), f"missing asset: {entry['path']}")
        data = path.read_bytes()
        require(len(data) == entry["bytes"] and sha256(data) == entry["sha256"],
                f"file hash/size mismatch: {entry['path']}")
        require(re.fullmatch(r"[0-9a-f]{64}", entry["sourceSha256"]), "invalid source hash")
        if source_pack is not None:
            source = safe_file(source_pack, entry["path"]).read_bytes()
            require(sha256(source) == entry["sourceSha256"], f"source hash mismatch: {entry['path']}")
            require(sanitized_source(source, path.suffix) == data, f"source content mismatch: {entry['path']}")
        if path.suffix in {".fbx", ".blend"}:
            require(not re.search(rb"/(?:workspace|home|root)/|dream_mob_collection/", data),
                    f"machine-local path in {entry['path']}")
        if path.suffix == ".fbx":
            fbx_tree(data)
        elif path.suffix == ".blend":
            require(data.startswith(b"BLENDER-v403"), "unexpected Blender source format")
        elif path.suffix == ".png":
            inspect_png(data)
        elif path.suffix != ".glb":
            raise ValueError(f"unexpected asset extension: {entry['path']}")
        if not entry["sanitized"]:
            require(entry["sha256"] == entry["sourceSha256"], "unchanged asset source hash mismatch")
    for model in models:
        for kind, suffix in (("fbx", ".fbx"), ("glb", ".glb"), ("blend", ".blend")):
            relative = model[kind]
            require(relative in paths and relative.endswith(suffix), f"missing {kind} for {model['modelId']}")
            referenced.add(relative)
        require({"baseColor", "metallicRoughness", "emission"} <= model["textures"].keys(),
                "missing source texture role")
        for texture in model["textures"].values():
            require(texture in paths and texture.endswith(".png"), f"missing texture: {texture}")
            referenced.add(texture)
        fbx_nodes, _ = fbx_tree(safe_file(pack, model["fbx"]).read_bytes())
        takes = fbx_take_names(fbx_nodes)
        require(len(takes) == 3 and set(takes) == {clip["takeName"] for clip in model["clips"]},
                f"FBX take names mismatch: {model['modelId']}")
        for clip in model["clips"]:
            require(clip["fps"] == 30 and clip["frameStart"] == 1 and
                    clip["frameEnd"] > clip["frameStart"] and
                    abs((clip["frameEnd"] - clip["frameStart"]) / clip["fps"] - clip["durationSeconds"]) < 1e-6,
                    "invalid clip frame/timing metadata")
            require(clip["loop"] == (clip["name"] != "Attack"), "invalid source clip loop metadata")
        for texture in model["textures"].values():
            dimensions = inspect_png(safe_file(pack, texture).read_bytes())
            require(dimensions == (model["source"]["textureAtlasWidth"], model["source"]["textureAtlasHeight"]),
                    "texture dimensions disagree with source metadata")
        result = inspect_glb(safe_file(pack, model["glb"]).read_bytes(), model)
        for key, value in result.items():
            totals[key] += value
    require(referenced == set(paths), "unreferenced/missing manifest file")
    binaries = {p.relative_to(pack).as_posix() for p in pack.rglob("*")
                if p.suffix in {".fbx", ".glb", ".blend", ".png"}}
    require(binaries == referenced, "unlisted binary asset")
    require(totals == EXPECTED_TOTALS, f"observed pack totals mismatch: {totals}")
    return dict(totals, files=len(files), bytes=sum(entry["bytes"] for entry in files),
                sourceCompared=source_pack is not None, runtimeVerified=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pack", type=Path, default=PACK)
    parser.add_argument("--source-pack", type=Path)
    args = parser.parse_args()
    try:
        result = validate_pack(args.pack, args.source_pack)
    except (ValueError, KeyError, IndexError, TypeError, OSError, struct.error, zlib.error) as error:
        print(f"Mob asset validation FAILED: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

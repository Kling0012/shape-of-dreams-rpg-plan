"""Optional Blender 4.3.2 check for editable sources and authoritative FBX files.

Run from the repository root (Blender is not required by the normal validator):
blender --background --disable-autoexec --python tests/assets/verify_blender_source_equivalence.py -- --source-pack /path/to/Dreamborne_Enemies_Vol01

Omit --source-pack to check counts and loading without original-byte equivalence.
This never saves or rewrites a .blend file and does not exercise the game runtime.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy


def snapshot(path, kind):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if kind == "blend":
        bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
    else:
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    rigs = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    data, triangles = [], 0
    for obj in sorted(meshes, key=lambda obj: obj.name):
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)
        data.append(("mesh", obj.name, tuple(tuple(row) for row in obj.matrix_world),
                     [(tuple(v.co), [(g.group, g.weight) for g in v.groups]) for v in obj.data.vertices],
                     [tuple(p.vertices) for p in obj.data.polygons], [g.name for g in obj.vertex_groups]))
    for obj in sorted(rigs, key=lambda obj: obj.name):
        data.append(("rig", obj.name,
                     [(bone.name, bone.parent.name if bone.parent else "",
                       tuple(tuple(row) for row in bone.matrix_local)) for bone in obj.data.bones]))
    for action in sorted(bpy.data.actions, key=lambda action: action.name):
        data.append(("action", action.name, list(action.frame_range),
                     [(curve.data_path, curve.array_index,
                       [(tuple(key.co), key.interpolation) for key in curve.keyframe_points])
                      for curve in action.fcurves]))
    return dict(meshes=len(meshes), triangles=triangles,
                bones=sum(len(obj.data.bones) for obj in rigs), clips=len(bpy.data.actions),
                geometryRigAnimationSha256=hashlib.sha256(
                    json.dumps(data, separators=(",", ":")).encode("utf-8")).hexdigest())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pack", type=Path, default=Path(__file__).resolve().parents[2] / "assets/dreamborne")
    parser.add_argument("--source-pack", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    manifest = json.loads((args.pack / "manifest.json").read_text(encoding="utf-8"))
    results = []
    for model in manifest["models"]:
        for kind in ("fbx", "blend"):
            checked = snapshot(args.pack / model[kind], kind)
            expected_meshes = 1 if kind == "fbx" else model["source"]["editableMeshCount"]
            expected = dict(meshes=expected_meshes, triangles=model["triangles"], bones=model["bones"], clips=3)
            if any(checked[key] != value for key, value in expected.items()):
                raise RuntimeError(f"{model['modelId']} {kind} counts differ: {checked}")
            if args.source_pack:
                original = snapshot(args.source_pack / model[kind], kind)
                if original != checked:
                    raise RuntimeError(f"{model['modelId']} {kind} geometry, rig, or animation changed")
            results.append(dict(modelId=model["modelId"], format=kind, **checked))
            print(f"BLENDER_IMPORT_PASS {model['modelId']} {kind}", flush=True)
    report = dict(blenderVersion=bpy.app.version_string, runtimeVerified=False,
                  sourceCompared=args.source_pack is not None, results=results)
    if args.report:
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"ALL_BLENDER_IMPORTS_VERIFIED {len(results)}", flush=True)


if __name__ == "__main__":
    main()

"""Export an edited, already-prepared Grace blend with refreshed frozen masks.

Run with --factory-startup --disable-autoexec and --python-exit-code 1.
This script exports GLB only; it never saves the opened blend.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
out = Path(parser.parse_args(sys.argv[sys.argv.index("--") + 1:]).output)
assert bpy.data.objects.get("rig_D") is not None, "Use the prepared authoring source"
assert not bpy.data.texts and not bpy.data.actions, "Source must remain frozen"
arm = bpy.data.objects["rig_D"]
assert not arm.animation_data and not arm.data.animation_data, "Rig controls must remain frozen"
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    for vertex in obj.data.vertices:
        weights = [g.weight for g in vertex.groups if obj.vertex_groups[g.group].name in arm.data.bones and g.weight > 0]
        assert 1 <= len(weights) <= 8 and abs(sum(weights) - 1) < 0.002, "Normalize to at most eight skin weights in Blender"

def point(v):
    return [round(v.co.x * 10000), round(v.co.z * 10000), round(-v.co.y * 10000)]

for name in ["grace_crowne", "grace_crowne_hair"]:
    obj = bpy.data.objects[name]
    assert all(len(p.vertices) == 3 for p in obj.data.polygons), "Triangulate in Blender before exporting"
    if obj.data.shape_keys:
        assert all(k.value == 0 for k in list(obj.data.shape_keys.key_blocks)[1:]), "Save the neutral face"
    spec = json.loads(obj["grace_masks"])
    flags = None
    if name == "grace_crowne":
        flags = obj.data.attributes.get("grace_mask_bits")
        assert flags and flags.domain == "FACE" and flags.data_type == "INT", "Keep the authored body face-mask attribute"
        assert all(0 <= value.value < 16 for value in flags.data), "Body mask flags must select the four outfits"
    spec["triangles"] = [[sum((point(obj.data.vertices[v]) for v in p.vertices), []),
        flags.data[p.index].value if flags else 1]
        for p in obj.data.polygons]
    obj["grace_masks"] = json.dumps(spec, separators=(",", ":"))
assert bpy.ops.export_scene.gltf(filepath=str(out), export_format="GLB", export_animations=False,
    export_apply=False, export_extras=True, export_all_influences=True, export_morph=True,
    export_rest_position_armature=True, export_def_bones=True, export_texcoords=True,
    export_normals=True, export_tangents=True, export_materials="EXPORT",
    use_visible=False, use_renderable=False) == {"FINISHED"}
print("GRACE_PREPARED_EXPORT_COMPLETE", out)

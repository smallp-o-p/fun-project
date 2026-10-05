"""Derive game assets from a COPY opened with --factory-startup --disable-autoexec.

blender --background --factory-startup --disable-autoexec source-copy.blend
  --python tools/models/prepare_grace_crowne.py -- --output /absolute/output
The creator file is never saved. Output is a clean editable blend and runtime GLB.
"""
import argparse
import hashlib
import json
import sys
import subprocess
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix

args = argparse.ArgumentParser()
args.add_argument("--output", required=True)
out = Path(args.parse_args(sys.argv[sys.argv.index("--") + 1:]).output)
out.mkdir(parents=True, exist_ok=True)
authoring = out / "authoring"
authoring.mkdir(exist_ok=True)
(authoring / ".gdignore").write_text("")
source = Path(bpy.data.filepath)
source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
arm = bpy.data.objects["grace_crowne_armature"]
arm.data.animation_data_clear()
body = bpy.data.objects["grace_crowne"]
# Extract static shader inputs in a separate safe process before stripping the
# creator graphs. DD/FPv4 view terms belong exclusively to the live Godot shader.
subprocess.run([bpy.app.binary_path, "--background", "--factory-startup", "--disable-autoexec",
    str(source), "--python-exit-code", "1", "--python",
    str(Path(__file__).with_name("extract_grace_materials.py")), "--",
    "--output", str(out / "materials")], check=True)
if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")
body.animation_data_clear()
for modifier in body.modifiers:
    if modifier.type not in {"ARMATURE", "CORRECTIVE_SMOOTH", "SUBSURF"}:
        modifier.show_viewport = False
        modifier.show_render = False
bpy.context.preferences.filepaths.save_version = 0
meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("grace_crowne")]
for obj in list(bpy.data.objects):
    if obj not in meshes and obj != arm:
        bpy.data.objects.remove(obj, do_unlink=True)
for obj in [arm] + meshes:
    obj.animation_data_clear()
    for key in list(obj.keys()):
        del obj[key]
for bone in arm.pose.bones:
    for constraint in list(bone.constraints):
        bone.constraints.remove(constraint)
    bone.matrix_basis = Matrix.Identity(4)
    bone.custom_shape = None
    for key in list(bone.keys()):
        del bone[key]
arm.data.pose_position = "REST"
arm.name = "rig_D"

def membership(obj, group):
    index = obj.vertex_groups[group].index
    return {v.index for v in obj.data.vertices
            if any(g.group == index and g.weight > 0 for g in v.groups)}

# Mark original parent faces before subdivision so each child retains the
# creator's KEEP semantics without guessing from interpolated group weights.
mask_names = ["suit", "exposed", "unzipped", "zipped"]
groups = [membership(body, "mask_suit" + suffix) for suffix in ["", "_exposed", "_unzipped", "_zipped"]]
flags = body.data.attributes.new(name="grace_mask_bits", type="INT", domain="FACE")
for polygon in body.data.polygons:
    flags.data[polygon.index].value = sum(1 << i for i, members in enumerate(groups)
                                          if not all(v in members for v in polygon.vertices))

# Freeze identity/fit, corrective smoothing and body subdivision in Basis.
# Evaluating each facial target separately retains all morphs, while evaluating
# the frozen face attribute carries masks through the source topology change.
face_names = []
for obj in meshes:
    keys = obj.data.shape_keys
    if not keys:
        continue
    keys.animation_data_clear()
    blocks = list(keys.key_blocks)
    face_start = next((i for i, k in enumerate(blocks) if k.name == "browDownL"), len(blocks))
    for key in blocks[face_start:]:
        key.value = 0
    visibility = [(modifier, modifier.show_viewport) for modifier in obj.modifiers]
    for modifier, _ in visibility:
        modifier.show_viewport = modifier.type == "CORRECTIVE_SMOOTH" or (obj == body and modifier.type == "SUBSURF")
    frozen_mesh = None

    def evaluated_positions():
        global frozen_mesh
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        if frozen_mesh is None:
            frozen_mesh = mesh.copy()
        assert len(mesh.vertices) == len(frozen_mesh.vertices), "Facial target changed frozen topology"
        positions = [v.co.copy() for v in mesh.vertices]
        evaluated.to_mesh_clear()
        return positions

    frozen = evaluated_positions()
    facial = []
    if obj == body:
        for key in blocks[face_start:]:
            key.value = 1
            positions = evaluated_positions()
            facial.append((key.name, [v - frozen[i] for i, v in enumerate(positions)]))
            key.value = 0
    for modifier, visible in visibility:
        modifier.show_viewport = visible
    obj.shape_key_clear()
    obj.data = frozen_mesh
    for i, vertex in enumerate(obj.data.vertices):
        vertex.co = frozen[i]
    if facial:
        obj.shape_key_add(name="Basis")
        for name, deltas in facial:
            key = obj.shape_key_add(name=name)
            key.value = 0
            for i, vertex in enumerate(key.data):
                vertex.co = frozen[i] + deltas[i]
            face_names.append(name)
    obj.data.normals_split_custom_set([(0, 0, 0)] * len(obj.data.loops))

def split_piece(obj, group, name):
    """Freeze a creator KEEP mask into two ordinary authored garment meshes."""
    keep = membership(obj, group)
    extra_faces = {p.index for p in obj.data.polygons if not all(v in keep for v in p.vertices)}
    if not extra_faces:
        return None
    extra = obj.copy()
    extra.data = obj.data.copy()
    extra.name = name
    bpy.context.scene.collection.objects.link(extra)
    for target, remove in [(obj, extra_faces), (extra, set(range(len(obj.data.polygons))) - extra_faces)]:
        bm = bmesh.new()
        bm.from_mesh(target.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index in remove], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(target.data)
        bm.free()
    return extra

hair = bpy.data.objects["grace_crowne_hair"]
headband = split_piece(hair, "mask_headband", "Headband")
suit = bpy.data.objects["grace_crowne_suit"]
gadgets = split_piece(suit, "mask_gadgets", "Gadgets")
meshes = [o for o in bpy.data.objects if o.type == "MESH"]

# Frozen parent-face flags survive deterministic triangulation and are compiled
# against the actual imported geometry rather than relying on vertex ordering.

pruned = 0
max_discarded = 0.0
weighted = set()
for obj in meshes:
    for modifier in list(obj.modifiers):
        if modifier.type != "ARMATURE":
            obj.modifiers.remove(modifier)
        else:
            modifier.use_deform_preserve_volume = False
    for vertex in obj.data.vertices:
        weights = sorted([(g.group, g.weight) for g in vertex.groups
                          if obj.vertex_groups[g.group].name in arm.data.bones and g.weight > 0],
                         key=lambda pair: pair[1], reverse=True)
        assert weights, (obj.name, vertex.index, "unweighted vertex")
        if len(weights) > 8:
            pruned += 1
            max_discarded = max(max_discarded, sum(w for _, w in weights[8:]) / sum(w for _, w in weights))
        for group, _ in weights:
            obj.vertex_groups[group].remove([vertex.index])
        total = sum(w for _, w in weights[:8])
        for group, weight in weights[:8]:
            obj.vertex_groups[group].add([vertex.index], weight / total, "REPLACE")
            weighted.add(obj.vertex_groups[group].name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="FIXED", ngon_method="EAR_CLIP")
    bm.to_mesh(obj.data)
    bm.free()
    obj.hide_set(False)
    obj.hide_viewport = False
    obj.hide_render = False
    obj["wardrobe_source_id"] = obj.name

def position(vertex):
    v = vertex.co
    return [round(v.x * 10000), round(v.z * 10000), round(-v.y * 10000)]

body["grace_masks"] = json.dumps({"names": mask_names, "default_bits": 1,
    "triangles": [[sum((position(body.data.vertices[v]) for v in p.vertices), []),
                   body.data.attributes["grace_mask_bits"].data[p.index].value]
                  for p in body.data.polygons]}, separators=(",", ":"))
hair["grace_masks"] = json.dumps({"names": ["goggles"], "default_bits": 0,
    "triangles": [[sum((position(hair.data.vertices[v]) for v in p.vertices), []), 1]
                  for p in hair.data.polygons]}, separators=(",", ":"))
# Keep this Blender face attribute as the editable frozen mask authoring data.

# Keep skin bones and their ancestry. Their rest hierarchy already follows the
# limbs; runtime does not interpret the creator's control constraints/drivers.
mapping = {"Root": "root", "Hips": "pelvis", "Spine": "spine_01", "Chest": "spine_03",
           "UpperChest": "spine_05", "Neck": "neck_01", "Head": "head"}
for side, suffix in [("Left", "l"), ("Right", "r")]:
    for target, original in [("Shoulder", "clavicle"), ("UpperArm", "upperarm"), ("LowerArm", "lowerarm"),
                             ("Hand", "hand"), ("UpperLeg", "thigh"), ("LowerLeg", "calf"), ("Foot", "foot"), ("Toes", "ball")]:
        mapping[side + target] = original + "_" + suffix
    for target, original in [("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky")]:
        segments = ["Metacarpal", "Proximal", "Distal"] if target == "Thumb" else ["Proximal", "Intermediate", "Distal"]
        for i, segment in enumerate(segments, 1):
            mapping[side + target + segment] = f"{original}_{i:02}_{suffix}"
keep = weighted | set(mapping.values())
for name in list(keep):
    bone = arm.data.bones[name].parent
    while bone:
        keep.add(bone.name)
        bone = bone.parent
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
for bone in list(arm.data.edit_bones):
    if bone.name not in keep:
        arm.data.edit_bones.remove(bone)
bpy.ops.object.mode_set(mode="OBJECT")
for bone in arm.data.bones:
    bone.use_deform = bone.name in keep

# Static PBR transport keeps the extracted source channels. Godot's material
# adapter restores camera-dependent skin, hair, emission and eye effects.
sys.path.insert(0, str(Path(__file__).parent))
from apply_grace_materials import apply
apply(out / "materials")

variants = ["Suit", "Exposed", "Unzipped", "Zipped"]
suit_sources = ["grace_crowne_suit", "grace_crowne_suit_exposed", "grace_crowne_suit_unzipped", "grace_crowne_suit_zipped"]
components = {"Suit": {"visible": True, "pieces": [{"source": s, "variant": i} for i, s in enumerate(suit_sources)]},
              "Mask": {"visible": False, "pieces": [{"source": "grace_crowne_mask", "variant": -1}]},
              "Goggles": {"visible": False, "pieces": [{"source": "grace_crowne_goggles", "variant": -1}]}}
for piece in [headband, gadgets]:
    if piece:
        components[piece.name] = {"visible": True, "pieces": [{"source": piece.name, "variant": 0 if piece == gadgets else -1}]}
rules = [{"source": body.name, "name": name, "index": i, "component": "Suit", "enabled": True, "variant": i}
         for i, name in enumerate(mask_names)]
rules.append({"source": hair.name, "name": "goggles", "index": 0, "component": "Goggles", "enabled": True, "variant": -1})
arm["wardrobe_catalog"] = json.dumps({"version": 1, "variants": variants, "default_variant": 0,
                                      "components": components, "masks": rules}, separators=(",", ":"))
for text in list(bpy.data.texts):
    bpy.data.texts.remove(text)
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
for group in list(bpy.data.node_groups):
    group.use_fake_user = False
for image in bpy.data.images:
    image.use_fake_user = False
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(authoring / "GraceCrowne.blend"), check_existing=False)
assert bpy.ops.export_scene.gltf(filepath=str(out / "GraceCrowne.glb"), export_format="GLB",
    export_animations=False, export_apply=False, export_extras=True, export_all_influences=True,
    export_morph=True, export_rest_position_armature=True, export_def_bones=True,
    export_texcoords=True, export_normals=True, export_tangents=True, export_materials="EXPORT",
    use_visible=False, use_renderable=False) == {"FINISHED"}
(out / "authoring-data.json").write_text(json.dumps({"source_sha256": source_hash,
    "bone_map": mapping, "facial_shapes": face_names, "pruned_vertices": pruned,
    "max_discarded_weight_fraction": max_discarded, "retained_bones": len(keep)}, indent=2))
print("GRACE_AUTHORING_COMPLETE", len(face_names), "facial shapes", len(keep), "bones", pruned, "pruned vertices")

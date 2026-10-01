@tool
extends RefCounted
# Import-time presentation companion for humanoid_post_import.gd. Applies the
# committed presentation.res manifest to a freshly imported, structurally
# repaired scene: surface override materials, mesh visibility/transform/blend
# shape defaults and the captured hidden groups. Mesh, skin, geometry and
# hierarchy stay source-owned; any failure returns a nonempty error so the
# import callback discards the scene.

const MODELS: Dictionary = {
	"res://scenes/models/ZhuYuan/ZhuYuan.blend": {
		"key": "zhu_yuan",
		"manifest": "res://resources/models/zhu_yuan/presentation/presentation.res",
		"hidden_paths": ["char_grp_002", "rig_D/GeneralSkeleton/Weapons"],
	},
	"res://scenes/models/Trigger/Trigger4.2.blend": {
		"key": "trigger",
		"manifest": "res://resources/models/trigger/presentation/presentation.res",
		"hidden_paths": ["char_grp", "Trigger_Weapon"],
	},
}
# Legacy weapon role segments accepted in the saved manifests.
const WEAPON_ROLES: Dictionary = {
	"BackModule": "BackModule",
	"LeftShoulderModule": "LeftShoulder",
	"RightShoulderModule": "RightShoulder",
	"BeltModule": "Belt",
	"ArmModules": "ArmModules",
	"ArmModules/LeftArmModule": "ArmModules/LeftArmModule",
	"ArmModules/RightArmModule": "ArmModules/RightArmModule",
}

static func apply(scene: Node, source_file: String) -> String:
	if scene == null:
		return "import presentation: null scene"
	var spec: Dictionary = MODELS.get(source_file, {}) as Dictionary
	if spec.is_empty():
		return "import presentation: unknown import source " + source_file
	var key: String = String(spec["key"])
	var manifest: Resource = ResourceLoader.load(String(spec["manifest"]), "", ResourceLoader.CACHE_MODE_IGNORE_DEEP) as Resource
	if manifest == null:
		return key + "/manifest: failed to load " + String(spec["manifest"])
	var err: String = _apply_surfaces(scene, key, manifest)
	if not err.is_empty():
		return err
	err = _apply_defaults(scene, key, manifest)
	if not err.is_empty():
		return err
	err = _hide_groups(scene, key, spec)
	if not err.is_empty():
		return err
	print("Humanoid presentation %s: surfaces=%d defaults=%d hidden=%d" % [source_file, (manifest.get_meta("surfaces", []) as Array).size(), (manifest.get_meta("mesh_defaults", []) as Array).size(), (spec["hidden_paths"] as Array).size()])
	return ""

# Maps the node paths saved in the committed manifests to paths in the
# imported scene; "" means the path matches no accepted destination rule.
static func _map_legacy_path(old_path: String) -> String:
	var rig_prefix: String = "Model/rig_D/Skeleton3D/"
	if old_path.begins_with(rig_prefix):
		return "rig_D/GeneralSkeleton/" + old_path.substr(rig_prefix.length())
	var tongue_prefix: String = "Model/TongueAttachment/"
	if old_path.begins_with(tongue_prefix):
		return "rig_D/GeneralSkeleton/TongueAttachment/Placement/" + old_path.substr(tongue_prefix.length())
	var weapons_prefix: String = "Weapons/"
	if old_path.begins_with(weapons_prefix):
		var weapon_rest: String = old_path.substr(weapons_prefix.length())
		var geometry_suffix: String = "/Model/Skeleton3D/Geometry"
		var with_geometry: bool = weapon_rest.ends_with(geometry_suffix)
		if with_geometry:
			weapon_rest = weapon_rest.substr(0, weapon_rest.length() - geometry_suffix.length())
		if not WEAPON_ROLES.has(weapon_rest):
			return ""
		var mapped: String = "rig_D/GeneralSkeleton/Weapons/" + String(WEAPON_ROLES[weapon_rest])
		return mapped + ("/Geometry" if with_geometry else "")
	return ""

static func _resolve_mesh(scene: Node, instance_path: String) -> MeshInstance3D:
	return scene.get_node_or_null(NodePath(instance_path)) as MeshInstance3D

static func _apply_surfaces(scene: Node, key: String, manifest: Resource) -> String:
	var rows_variant: Variant = manifest.get_meta("surfaces", null)
	if not (rows_variant is Array):
		return key + "/surfaces: missing or not an Array"
	var faces: Array[Dictionary] = []
	for row_variant: Variant in rows_variant as Array:
		var row: Dictionary = row_variant as Dictionary
		var old_path: String = str(row.get("node_path", ""))
		var label: String = key + "/surfaces/" + old_path
		var instance_path: String = _map_legacy_path(old_path)
		if instance_path.is_empty():
			return label + ": unmapped destination path"
		var mesh_instance: MeshInstance3D = _resolve_mesh(scene, instance_path)
		if mesh_instance == null or mesh_instance.mesh == null:
			return label + ": destination MeshInstance3D is missing: " + instance_path
		var index: int = int(row.get("surface_index", -1))
		if index < 0 or index >= mesh_instance.mesh.get_surface_count():
			return label + ": surface_index out of range"
		var material := row.get("material") as Material
		if material == null:
			return label + ": material missing or not a Material"
		mesh_instance.set_surface_override_material(index, material)
		if material is ShaderMaterial and material.get_shader_parameter("use_face_sdf") == true:
			var skeleton := mesh_instance.get_node_or_null(mesh_instance.skeleton) as Skeleton3D
			if skeleton == null:
				return label + ": face skeleton is missing"
			var head: int = skeleton.find_bone("Head")
			if head < 0:
				return label + ": face skeleton has no Head bone"
			faces.append({
				"mesh_path": NodePath(instance_path), "surface_index": index,
				"skeleton_path": scene.get_path_to(skeleton), "head_bone": head,
				"inverse_rest": skeleton.get_bone_global_rest(head).basis.inverse(),
			})
	# Paths and bone indices belong to this repaired import, so reimport rebuilds
	# them together. Runtime resolves exact instance slots, never shared materials.
	scene.set_meta("face_lighting", faces)
	return ""

static func _apply_defaults(scene: Node, key: String, manifest: Resource) -> String:
	var rows_variant: Variant = manifest.get_meta("mesh_defaults", null)
	if not (rows_variant is Array):
		return key + "/mesh_defaults: missing or not an Array"
	for row_variant: Variant in rows_variant as Array:
		var row: Dictionary = row_variant as Dictionary
		var old_path: String = str(row.get("node_path", ""))
		var label: String = key + "/mesh_defaults/" + old_path
		var instance_path: String = _map_legacy_path(old_path)
		if instance_path.is_empty():
			return label + ": unmapped destination path"
		var mesh_instance: MeshInstance3D = _resolve_mesh(scene, instance_path)
		if mesh_instance == null or mesh_instance.mesh == null:
			return label + ": destination MeshInstance3D is missing: " + instance_path
		var visible_variant: Variant = row.get("visible", null)
		var transform_variant: Variant = row.get("transform", null)
		if typeof(visible_variant) != TYPE_BOOL or typeof(transform_variant) != TYPE_TRANSFORM3D:
			return label + ": visible/transform missing or wrong type"
		mesh_instance.visible = visible_variant
		mesh_instance.transform = transform_variant
		if not row.has("blend_shapes"):
			return label + ": blend_shapes array missing"
		for shape_variant: Variant in row["blend_shapes"]:
			var shape: Dictionary = shape_variant as Dictionary
			var shape_name: String = str(shape.get("name", ""))
			var shape_index: int = mesh_instance.find_blend_shape_by_name(shape_name)
			if shape_index < 0:
				return label + ": blend shape not found on the mesh: " + shape_name
			if not shape.has("value"):
				return label + ": blend shape value missing for " + shape_name
			mesh_instance.set_blend_shape_value(shape_index, float(shape["value"]))
	return ""

static func _hide_groups(scene: Node, key: String, spec: Dictionary) -> String:
	for raw_path: Variant in spec["hidden_paths"]:
		var rel: String = str(raw_path)
		var node: Node3D = scene.get_node_or_null(NodePath(rel)) as Node3D
		if node == null:
			return key + "/hidden: " + rel + " is missing or not a Node3D"
		node.visible = false
	return ""

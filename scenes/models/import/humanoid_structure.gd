@tool
extends RefCounted
# Import-time structural companion for humanoid_post_import.gd. Applies the
# committed import_bindings.res recipe to a freshly imported scene: reparents
# the tongue rig under a Head BoneAttachment3D and, for Zhu Yuan, adds the
# weapon copies plus the two derived arm meshes. Arm subsets are partitioned
# per the recipe's per-triangle roles and copy the source encoded
# normal/tangent stream per selected vertex so native octahedral encoding
# survives unchanged. Any failure returns a nonempty error so the import
# callback discards the scene.

const MAIN_SKELETON_PATH: String = "rig_D/GeneralSkeleton"
const TONGUE_SKELETON_PATH: String = "Tongue_rig/Skeleton3D"
const TONGUE_MESH_PATH: String = "Tongue_rig/Skeleton3D/Tongue"
const ROLE_LEFT: int = 1
const ROLE_RIGHT: int = 2
const ROLE_NAMES: Array = ["LeftArmModule", "RightArmModule"]
const ARM_SOURCE: String = "ZhuYuan_9"
const WEAPON_COPIES: Array = [
	{"container": "BackModule", "source": "ZhuYuan_2"},
	{"container": "LeftShoulder", "source": "ZhuYuan_3"},
	{"container": "RightShoulder", "source": "ZhuYuan_4"},
	{"container": "Belt", "source": "ZhuYuan_7"},
]
const RECIPES: Dictionary = {
	"res://scenes/models/ZhuYuan/ZhuYuan.blend": {"key": "zhu_yuan", "recipe": "res://resources/models/zhu_yuan/import_bindings.res", "arm": true},
	"res://scenes/models/Trigger/Trigger4.2.blend": {"key": "trigger", "recipe": "res://resources/models/trigger/import_bindings.res", "arm": false},
}

static func apply(scene: Node, source_file: String) -> String:
	if scene == null:
		return "import structure: null scene"
	var spec: Dictionary = RECIPES.get(source_file, {}) as Dictionary
	if spec.is_empty():
		return "import structure: unknown import source " + source_file
	var ctx: Dictionary = {"scene": scene, "key": String(spec["key"]), "has_arm": bool(spec["arm"])}
	var err: String = _load_recipe(ctx, String(spec["recipe"]))
	if not err.is_empty():
		return err
	err = _verify_recipe(ctx)
	if not err.is_empty():
		return err
	err = _verify_structure_targets(ctx)
	if not err.is_empty():
		return err
	err = _verify_tongue_source(ctx)
	if not err.is_empty():
		return err
	if bool(ctx["has_arm"]):
		err = _build_arm_meshes(ctx)
		if not err.is_empty():
			return err
	err = _attach_tongue(ctx)
	if not err.is_empty():
		return err
	if bool(ctx["has_arm"]):
		err = _attach_weapons(ctx)
		if not err.is_empty():
			return err
	return ""

static func _load_recipe(ctx: Dictionary, recipe_path: String) -> String:
	var resource: Resource = ResourceLoader.load(recipe_path, "", ResourceLoader.CACHE_MODE_IGNORE_DEEP) as Resource
	if resource == null:
		return String(ctx["key"]) + "/recipe: failed to load " + recipe_path
	ctx["recipe"] = resource
	return ""

static func _verify_recipe(ctx: Dictionary) -> String:
	var key: String = String(ctx["key"])
	var resource: Resource = ctx["recipe"] as Resource
	var tongue_variant: Variant = resource.get_meta("tongue", null)
	if typeof(tongue_variant) != TYPE_DICTIONARY:
		return key + "/recipe/tongue: missing dictionary"
	ctx["tongue"] = tongue_variant as Dictionary
	if bool(ctx["has_arm"]):
		var arm_variant: Variant = resource.get_meta("arm_partition", null)
		if typeof(arm_variant) != TYPE_DICTIONARY:
			return key + "/recipe/arm_partition: missing dictionary"
		ctx["arm"] = arm_variant as Dictionary
	return ""

static func _verify_structure_targets(ctx: Dictionary) -> String:
	var main: Skeleton3D = (ctx["scene"] as Node).get_node_or_null(NodePath(MAIN_SKELETON_PATH)) as Skeleton3D
	if main == null:
		return String(ctx["key"]) + "/structure: missing " + MAIN_SKELETON_PATH
	ctx["main"] = main
	return ""

static func _verify_tongue_source(ctx: Dictionary) -> String:
	var key: String = String(ctx["key"])
	var scene: Node = ctx["scene"] as Node
	var tongue: Dictionary = ctx["tongue"] as Dictionary
	if typeof(tongue.get("source_path", null)) != TYPE_NODE_PATH:
		return key + "/recipe/tongue: source_path is not a NodePath"
	var rig: Node3D = scene.get_node_or_null(tongue["source_path"] as NodePath) as Node3D
	var skeleton: Node = scene.get_node_or_null(NodePath(TONGUE_SKELETON_PATH))
	var mesh_node: Node = scene.get_node_or_null(NodePath(TONGUE_MESH_PATH))
	if rig == null or skeleton == null or mesh_node == null:
		return key + "/tongue: missing Tongue_rig skeleton or mesh node"
	ctx["tongue_rig"] = rig
	return ""

static func _build_arm_meshes(ctx: Dictionary) -> String:
	var key: String = String(ctx["key"])
	var arm: Dictionary = ctx["arm"] as Dictionary
	var surfaces_variant: Variant = arm.get("surfaces", null)
	if typeof(surfaces_variant) != TYPE_ARRAY:
		return key + "/arm: surfaces missing"
	var surfaces: Array = surfaces_variant as Array
	var mesh_node: MeshInstance3D = (ctx["scene"] as Node).get_node_or_null(NodePath(MAIN_SKELETON_PATH + "/" + ARM_SOURCE)) as MeshInstance3D
	if mesh_node == null:
		return key + "/arm: missing " + MAIN_SKELETON_PATH + "/" + ARM_SOURCE
	var mesh: ArrayMesh = mesh_node.mesh as ArrayMesh
	if mesh == null or mesh.get_surface_count() != surfaces.size():
		return key + "/arm: live surface count mismatch"
	var left_mesh: ArrayMesh = ArrayMesh.new()
	var right_mesh: ArrayMesh = ArrayMesh.new()
	for s: int in surfaces.size():
		var surface: Dictionary = surfaces[s] as Dictionary
		if surface == null:
			return key + "/arm/" + str(s) + ": recipe surface is not a dictionary"
		var err: String = _partition_surface(ctx, mesh, surface, s, left_mesh, right_mesh)
		if not err.is_empty():
			return err
	ctx["left_mesh"] = left_mesh
	ctx["right_mesh"] = right_mesh
	ctx["arm_source_node"] = mesh_node
	return ""

@warning_ignore("integer_division")
static func _partition_surface(ctx: Dictionary, mesh: ArrayMesh, surface: Dictionary, s: int, left_mesh: ArrayMesh, right_mesh: ArrayMesh) -> String:
	var key: String = String(ctx["key"])
	var name_live: String = mesh.surface_get_name(s)
	var label: String = key + "/arm/" + name_live
	var arrays: Array = mesh.surface_get_arrays(s)
	var positions: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX] as PackedVector3Array
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX] as PackedInt32Array
	if positions == null or indices == null or positions.is_empty() or indices.is_empty():
		return label + ": source surface is missing positions or indices"
	var vertices: int = positions.size()
	var triangles: int = indices.size() / 3
	var roles_variant: Variant = surface.get("roles", null)
	if typeof(roles_variant) != TYPE_PACKED_BYTE_ARRAY or (roles_variant as PackedByteArray).size() != triangles:
		return label + ": roles buffer size mismatch"
	var roles: PackedByteArray = roles_variant as PackedByteArray
	var vertex_roles: PackedByteArray = PackedByteArray()
	vertex_roles.resize(vertices)
	for t: int in triangles:
		if roles[t] != ROLE_LEFT and roles[t] != ROLE_RIGHT:
			return label + ": triangle " + str(t) + " has role " + str(roles[t])
		for c: int in 3:
			var v: int = indices[t * 3 + c]
			if v < 0 or v >= vertices:
				return label + ": triangle " + str(t) + " has corner index " + str(v) + " out of range"
			if vertex_roles[v] == 0:
				vertex_roles[v] = roles[t]
			elif vertex_roles[v] != roles[t]:
				return label + ": vertex " + str(v) + " is referenced by both roles"
	for v2: int in vertices:
		if vertex_roles[v2] == 0:
			return label + ": vertex " + str(v2) + " is not covered by any triangle"
	var surface_data: Dictionary = RenderingServer.mesh_get_surface(mesh.get_rid(), s)
	var index_data_variant: Variant = surface_data.get("index_data")
	var index_count_variant: Variant = surface_data.get("index_count")
	if typeof(index_data_variant) != TYPE_PACKED_BYTE_ARRAY or typeof(index_count_variant) != TYPE_INT:
		return label + ": native index buffer missing"
	var index_data: PackedByteArray = index_data_variant as PackedByteArray
	var index_count: int = int(index_count_variant)
	if index_count != indices.size() or index_data.is_empty() or index_data.size() % index_count != 0:
		return label + ": native index buffer size mismatch"
	var stride: int = index_data.size() / index_count
	if stride != 2 and stride != 4:
		return label + ": unsupported index stride " + str(stride)
	var format: int = mesh.surface_get_format(s)
	var out_meshes: Dictionary = {ROLE_LEFT: left_mesh, ROLE_RIGHT: right_mesh}
	for role: int in [ROLE_LEFT, ROLE_RIGHT]:
		var build: Dictionary = {}
		var build_err: String = _extract_role_surface(label, arrays, indices, roles, vertex_roles, role, stride, surface_data, format, build)
		if not build_err.is_empty():
			return build_err
		var target: ArrayMesh = out_meshes[role] as ArrayMesh
		var out_index: int = target.get_surface_count()
		target.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, build["arrays"] as Array, [], build["lods"] as Dictionary, 0)
		target.surface_set_name(out_index, name_live)
		target.surface_set_material(out_index, mesh.surface_get_material(s))
		var nt_err: String = _update_derived_nt(target, out_index, format, build, label + ("/left" if role == ROLE_LEFT else "/right"))
		if not nt_err.is_empty():
			return nt_err
	return ""

@warning_ignore("integer_division")
static func _extract_role_surface(label: String, arrays: Array, indices: PackedInt32Array, roles: PackedByteArray, vertex_roles: PackedByteArray, role: int, index_stride: int, surface_data: Dictionary, format: int, build: Dictionary) -> String:
	var vertices: int = vertex_roles.size()
	var remap_indices: PackedInt32Array = PackedInt32Array()
	remap_indices.resize(vertices)
	remap_indices.fill(-1)
	var selected: PackedInt32Array = PackedInt32Array()
	for v: int in vertices:
		if vertex_roles[v] == role:
			remap_indices[v] = selected.size()
			selected.append(v)
	var source_positions: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX] as PackedVector3Array
	var source_normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL] as PackedVector3Array
	var source_tangents: PackedFloat32Array = arrays[Mesh.ARRAY_TANGENT] as PackedFloat32Array
	var source_colors: PackedColorArray = arrays[Mesh.ARRAY_COLOR] as PackedColorArray
	var source_uvs: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV] as PackedVector2Array
	var source_uv2s: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV2] as PackedVector2Array
	var source_bones: PackedInt32Array = arrays[Mesh.ARRAY_BONES] as PackedInt32Array
	var source_weights: PackedFloat32Array = arrays[Mesh.ARRAY_WEIGHTS] as PackedFloat32Array
	var positions: PackedVector3Array = PackedVector3Array()
	var normals: PackedVector3Array = PackedVector3Array()
	var colors: PackedColorArray = PackedColorArray()
	var uvs: PackedVector2Array = PackedVector2Array()
	var uv2s: PackedVector2Array = PackedVector2Array()
	var bones: PackedInt32Array = PackedInt32Array()
	var weights: PackedFloat32Array = PackedFloat32Array()
	var tangents: PackedFloat32Array = PackedFloat32Array()
	for i: int in selected.size():
		var v: int = selected[i]
		positions.append(source_positions[v])
		normals.append(source_normals[v])
		colors.append(source_colors[v])
		uvs.append(source_uvs[v])
		uv2s.append(source_uv2s[v])
		for k: int in 4:
			tangents.append(source_tangents[v * 4 + k])
			bones.append(source_bones[v * 4 + k])
			weights.append(source_weights[v * 4 + k])
	var out_indices: PackedInt32Array = PackedInt32Array()
	var triangles: int = indices.size() / 3
	for t: int in triangles:
		if roles[t] != role:
			continue
		for c: int in 3:
			var mapped: int = remap_indices[indices[t * 3 + c]]
			if mapped < 0:
				return label + ": triangle " + str(t) + " corner is not in the role"
			out_indices.append(mapped)
	var out_arrays: Array = []
	out_arrays.resize(Mesh.ARRAY_MAX)
	out_arrays[Mesh.ARRAY_VERTEX] = positions
	out_arrays[Mesh.ARRAY_NORMAL] = normals
	out_arrays[Mesh.ARRAY_TANGENT] = tangents
	out_arrays[Mesh.ARRAY_COLOR] = colors
	out_arrays[Mesh.ARRAY_TEX_UV] = uvs
	out_arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	out_arrays[Mesh.ARRAY_BONES] = bones
	out_arrays[Mesh.ARRAY_WEIGHTS] = weights
	out_arrays[Mesh.ARRAY_INDEX] = out_indices
	var lods: Dictionary = {}
	var lods_variant: Variant = surface_data.get("lods")
	if typeof(lods_variant) != TYPE_ARRAY:
		return label + ": native LOD data missing"
	for raw_lod: Variant in (lods_variant as Array):
		var lod: Dictionary = raw_lod as Dictionary
		if lod == null:
			return label + ": LOD row invalid"
		var edge_variant: Variant = lod.get("edge_length")
		var data_variant: Variant = lod.get("index_data")
		if typeof(edge_variant) != TYPE_FLOAT or not is_finite(float(edge_variant)) or typeof(data_variant) != TYPE_PACKED_BYTE_ARRAY:
			return label + ": LOD row fields invalid"
		var edge: float = float(edge_variant)
		if lods.has(edge):
			return label + ": duplicate LOD edge length"
		var lod_data: PackedByteArray = data_variant as PackedByteArray
		if lod_data.is_empty() or lod_data.size() % index_stride != 0 or lod_data.size() / index_stride % 3 != 0:
			return label + ": LOD buffer size invalid"
		var lod_indices: PackedInt32Array = PackedInt32Array()
		var lod_count: int = lod_data.size() / index_stride
		for t2: int in lod_count / 3:
			var corner_role: int = 0
			var bad: bool = false
			for c: int in 3:
				var v2: int = _index_at(lod_data, t2 * 3 + c, index_stride)
				if v2 < 0 or v2 >= vertices or vertex_roles[v2] == 0:
					bad = true
					break
				if corner_role == 0:
					corner_role = vertex_roles[v2]
				elif corner_role != vertex_roles[v2]:
					bad = true
					break
			if bad:
				return label + ": LOD triangle " + str(t2) + " has invalid or cross-role corners"
			if corner_role != role:
				continue
			for c2: int in 3:
				lod_indices.append(remap_indices[_index_at(lod_data, t2 * 3 + c2, index_stride)])
		lods[edge] = lod_indices
	var nt_err: String = _extract_nt_bytes(label, surface_data, format, vertices, selected, build)
	if not nt_err.is_empty():
		return nt_err
	build["arrays"] = out_arrays
	build["lods"] = lods
	build["vertex_count"] = selected.size()
	return ""

static func _extract_nt_bytes(label: String, surface_data: Dictionary, format: int, vertices: int, selected: PackedInt32Array, build: Dictionary) -> String:
	var vertex_data_variant: Variant = surface_data.get("vertex_data")
	if typeof(vertex_data_variant) != TYPE_PACKED_BYTE_ARRAY or (vertex_data_variant as PackedByteArray).is_empty():
		return label + ": native vertex buffer missing"
	var vertex_data: PackedByteArray = vertex_data_variant as PackedByteArray
	var normal_offset: int = RenderingServer.mesh_surface_get_format_offset(format, vertices, Mesh.ARRAY_NORMAL)
	var tangent_offset: int = RenderingServer.mesh_surface_get_format_offset(format, vertices, Mesh.ARRAY_TANGENT)
	var stride: int = RenderingServer.mesh_surface_get_format_normal_tangent_stride(format, vertices)
	if stride != 8 or tangent_offset != normal_offset + 4 or normal_offset != 12 * vertices or vertex_data.size() != normal_offset + stride * vertices:
		return label + ": unsupported encoded normal/tangent layout (stride " + str(stride) + ", normal offset " + str(normal_offset) + ", tangent offset " + str(tangent_offset) + ", bytes " + str(vertex_data.size()) + ")"
	var nt_bytes: PackedByteArray = PackedByteArray()
	for i: int in selected.size():
		var start: int = normal_offset + selected[i] * stride
		nt_bytes.append_array(vertex_data.slice(start, start + stride))
	build["nt_bytes"] = nt_bytes
	build["nt_stride"] = stride
	return ""

static func _update_derived_nt(target: ArrayMesh, out_index: int, format: int, build: Dictionary, label: String) -> String:
	var count: int = int(build["vertex_count"])
	var dest_normal: int = RenderingServer.mesh_surface_get_format_offset(format, count, Mesh.ARRAY_NORMAL)
	var dest_tangent: int = RenderingServer.mesh_surface_get_format_offset(format, count, Mesh.ARRAY_TANGENT)
	var dest_stride: int = RenderingServer.mesh_surface_get_format_normal_tangent_stride(format, count)
	if dest_stride != 8 or dest_tangent != dest_normal + 4 or dest_normal != 12 * count:
		return label + ": unsupported derived encoded layout (stride " + str(dest_stride) + ", normal offset " + str(dest_normal) + ", tangent offset " + str(dest_tangent) + ")"
	var nt_bytes: PackedByteArray = build["nt_bytes"] as PackedByteArray
	if nt_bytes.size() != count * dest_stride:
		return label + ": compacted normal/tangent byte count mismatch"
	# Godot 4.7.2: surface_update_vertex_region routes through a GPU buffer
	# update that the Dummy/headless RenderingServer silently ignores, so the
	# exact encoded bytes must instead go through the initial surface upload.
	# Rewrite only this surface's normal/tangent byte range in ArrayMesh's
	# version-sensitive "_surfaces" serialized property, then clear and
	# re-assign all surfaces so every dict is re-uploaded unchanged.
	var surfaces_variant: Variant = target.get("_surfaces")
	if typeof(surfaces_variant) != TYPE_ARRAY:
		return label + ": ArrayMesh internal surface cache unavailable"
	var surfaces: Array = surfaces_variant as Array
	if out_index < 0 or out_index >= target.get_surface_count() or surfaces.size() != target.get_surface_count():
		return label + ": internal surface cache count mismatch (" + str(surfaces.size()) + " cached, " + str(target.get_surface_count()) + " live)"
	var surface: Dictionary = surfaces[out_index] as Dictionary
	if surface == null:
		return label + ": internal surface cache entry type invalid"
	var vertex_bytes: PackedByteArray = surface.get("vertex_data", null) as PackedByteArray
	if vertex_bytes == null or vertex_bytes.size() != dest_normal + dest_stride * count:
		return label + ": internal surface cache vertex data size mismatch (" + str(vertex_bytes.size() if vertex_bytes != null else -1) + ")"
	var patched: PackedByteArray = vertex_bytes.slice(0, dest_normal)
	patched.append_array(nt_bytes)
	surface["vertex_data"] = patched
	target.clear_surfaces()
	target.set("_surfaces", surfaces)
	if target.get_surface_count() != surfaces.size():
		return label + ": surface rebuild count mismatch (" + str(target.get_surface_count()) + " rebuilt, " + str(surfaces.size()) + " expected)"
	return ""

static func _index_at(data: PackedByteArray, i: int, stride: int) -> int:
	return data.decode_u16(i * stride) if stride == 2 else data.decode_u32(i * stride)

static func _attach_tongue(ctx: Dictionary) -> String:
	var key: String = String(ctx["key"])
	var scene: Node = ctx["scene"] as Node
	var main: Skeleton3D = ctx["main"] as Skeleton3D
	var tongue: Dictionary = ctx["tongue"] as Dictionary
	var head_bone: String = str(tongue.get("head_bone", ""))
	if main.find_bone(head_bone) < 0:
		return key + "/tongue: bone " + head_bone + " missing from " + MAIN_SKELETON_PATH
	var transform_variant: Variant = tongue.get("local_transform", null)
	if typeof(transform_variant) != TYPE_TRANSFORM3D:
		return key + "/recipe/tongue: local_transform is not Transform3D"
	var rig: Node3D = ctx["tongue_rig"] as Node3D
	var attachment: BoneAttachment3D = BoneAttachment3D.new()
	attachment.name = "TongueAttachment"
	main.add_child(attachment)
	attachment.bone_name = head_bone
	attachment.owner = scene
	var placement: Node3D = Node3D.new()
	placement.name = "Placement"
	placement.transform = transform_variant
	attachment.add_child(placement)
	placement.owner = scene
	var rig_owner: Node = rig.owner
	rig.owner = null
	rig.reparent(placement, false)
	rig.owner = rig_owner
	return ""

static func _attach_weapons(ctx: Dictionary) -> String:
	var key: String = String(ctx["key"])
	var scene: Node = ctx["scene"] as Node
	var main: Skeleton3D = ctx["main"] as Skeleton3D
	var weapons: Node3D = Node3D.new()
	weapons.name = "Weapons"
	main.add_child(weapons)
	weapons.visible = false
	weapons.owner = scene
	for copy_row: Variant in WEAPON_COPIES:
		var row: Dictionary = copy_row as Dictionary
		var source: MeshInstance3D = main.get_node_or_null(NodePath(String(row["source"]))) as MeshInstance3D
		if source == null:
			return key + "/weapons: missing source " + MAIN_SKELETON_PATH + "/" + String(row["source"])
		var copy_err: String = _add_geometry_leaf(ctx, _new_container(scene, weapons, String(row["container"])), source, null)
		if not copy_err.is_empty():
			return copy_err
	var arm_modules: Node3D = _new_container(scene, weapons, "ArmModules")
	var left_module: Node3D = _new_container(scene, arm_modules, String(ROLE_NAMES[0]))
	var right_module: Node3D = _new_container(scene, arm_modules, String(ROLE_NAMES[1]))
	var arm_source: MeshInstance3D = ctx["arm_source_node"] as MeshInstance3D
	var left_err: String = _add_geometry_leaf(ctx, left_module, arm_source, ctx["left_mesh"] as ArrayMesh)
	if not left_err.is_empty():
		return left_err
	var right_err: String = _add_geometry_leaf(ctx, right_module, arm_source, ctx["right_mesh"] as ArrayMesh)
	if not right_err.is_empty():
		return right_err
	return ""

static func _new_container(scene: Node, parent: Node3D, node_name: String) -> Node3D:
	var node: Node3D = Node3D.new()
	node.name = node_name
	parent.add_child(node)
	node.owner = scene
	return node

static func _add_geometry_leaf(ctx: Dictionary, container: Node3D, source: MeshInstance3D, replacement_mesh: ArrayMesh) -> String:
	var scene: Node = ctx["scene"] as Node
	var leaf: MeshInstance3D = source.duplicate(0) as MeshInstance3D
	if leaf == null:
		return String(ctx["key"]) + "/weapons: duplicate of " + String(source.name) + " is not a MeshInstance3D"
	if leaf.skin != source.skin:
		leaf.free()
		return String(ctx["key"]) + "/weapons: duplicate of " + String(source.name) + " lost the source skin"
	if replacement_mesh != null:
		leaf.mesh = replacement_mesh
	leaf.name = "Geometry"
	container.add_child(leaf)
	leaf.owner = scene
	leaf.skeleton = leaf.get_path_to(ctx["main"] as Skeleton3D)
	return ""

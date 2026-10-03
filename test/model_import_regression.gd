extends SceneTree
# Run without importing assets: godot --headless --path . --script res://test/model_import_regression.gd

const MASK_VALIDATION = preload("res://scenes/models/import/ModelImportValidation.cs")
const STRUCTURE = preload("res://scenes/models/import/humanoid_structure.gd")
const PRESENTATION = preload("res://scenes/models/import/humanoid_presentation.gd")
const POST_IMPORT = preload("res://scenes/models/import/humanoid_post_import.gd")
const FORMAT: int = Mesh.ARRAY_FORMAT_VERTEX | Mesh.ARRAY_FORMAT_NORMAL | Mesh.ARRAY_FORMAT_TANGENT
var source_vertex_roles := PackedByteArray([1, 2, 1, 2, 1, 2])
var source_indices := PackedInt32Array([0, 2, 4, 5, 3, 1])
var checks: int = 0
var failures: int = 0

func _initialize() -> void:
	_test_surface_materials()
	_test_hidden_groups()
	_test_recipes()
	_test_role_surfaces()
	_test_mask_import_geometry()
	_test_mask_import_presentation()
	_test_wardrobe_compiler()
	_test_imported_wardrobes()
	_test_imported_rest_poses()
	_test_hierarchy_repair_with_stale_pose_cache()
	print("MODEL IMPORT REGRESSION: %d checks, %d failures" % [checks, failures])
	quit(0 if failures == 0 else 1)

func _expect(actual: Variant, expected: Variant, label: String) -> void:
	checks += 1
	if actual != expected:
		failures += 1
		printerr("FAIL %s: expected %s, got %s" % [label, str(expected), str(actual)])

func _test_imported_rest_poses() -> void:
	for path: String in [
		"res://scenes/models/ZhuYuan/ZhuYuan.blend",
		"res://scenes/models/ZhuYuan/ZhuYuan.scn",
		"res://scenes/models/Trigger/Trigger4.2.blend",
		"res://scenes/models/Trigger/Trigger.scn",
	]:
		var scene: Node = (load(path) as PackedScene).instantiate()
		var skeleton: Skeleton3D = scene.get_node("rig_D/GeneralSkeleton" if path.ends_with(".blend") else "Model/rig_D/GeneralSkeleton")
		for i: int in skeleton.get_bone_count():
			_expect(skeleton.get_bone_pose(i).is_equal_approx(skeleton.get_bone_rest(i)), true,
				path + " imported rest pose " + skeleton.get_bone_name(i))
		# Exercise the actual weighted geometry, including the unmapped deform
		# bones that the mapped arm animation tests do not cover.
		var max_displacement: float = 0.0
		for node: Node in skeleton.find_children("*", "MeshInstance3D", false, false):
			var mesh: MeshInstance3D = node as MeshInstance3D
			if mesh.skin == null or mesh.mesh == null:
				continue
			var poses: Array[Transform3D] = []
			var rests: Array[Transform3D] = []
			for bind: int in mesh.skin.get_bind_count():
				var bone: int = skeleton.find_bone(mesh.skin.get_bind_name(bind))
				_expect(bone >= 0, true, path + " resolves skin bind " + str(bind))
				poses.append(_composed_pose(skeleton, bone) * mesh.skin.get_bind_pose(bind))
				rests.append(skeleton.get_bone_global_rest(bone) * mesh.skin.get_bind_pose(bind))
			for surface: int in mesh.mesh.get_surface_count():
				var arrays: Array = mesh.mesh.surface_get_arrays(surface)
				var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
				var bones: PackedInt32Array = arrays[Mesh.ARRAY_BONES]
				var weights: PackedFloat32Array = arrays[Mesh.ARRAY_WEIGHTS]
				if bones.is_empty():
					continue
				var stride: int = bones.size() / vertices.size()
				for v: int in vertices.size():
					var posed := Vector3.ZERO
					var rested := Vector3.ZERO
					for slot: int in stride:
						var index: int = v * stride + slot
						posed += (poses[bones[index]] * vertices[v]) * weights[index]
						rested += (rests[bones[index]] * vertices[v]) * weights[index]
					max_displacement = maxf(max_displacement, posed.distance_to(rested))
		_expect(max_displacement < 0.0001, true, path + " weighted rest deformation (max=" + str(max_displacement) + ")")
		scene.free()

func _composed_pose(skeleton: Skeleton3D, bone: int) -> Transform3D:
	var pose: Transform3D = skeleton.get_bone_pose(bone)
	var parent: int = skeleton.get_bone_parent(bone)
	while parent >= 0:
		pose = skeleton.get_bone_pose(parent) * pose
		parent = skeleton.get_bone_parent(parent)
	return pose

func _test_hierarchy_repair_with_stale_pose_cache() -> void:
	for resting: bool in [true, false]:
		# Import callbacks receive a skeleton outside the scene tree. Retargeting
		# reads global transforms, then changes local poses without invalidating
		# that cache. Include a descendant and a parent with a higher bone index.
		var skeleton := Skeleton3D.new()
		for name: String in ["Deform", "Hand", "OldParent", "NewParent"]:
			skeleton.add_bone(name)
		skeleton.set_bone_parent(0, 2)
		skeleton.set_bone_parent(1, 0)
		for i: int in 4:
			skeleton.set_bone_rest(i, Transform3D(Basis(Vector3.FORWARD, 0.1 * i), Vector3(0.1 * i, 0.3, 0)))
			skeleton.set_bone_pose(i, Transform3D(Basis(Vector3.FORWARD, -0.3 * i), Vector3(0.2, 0.1 * i, 0)))
		for i: int in 4:
			skeleton.get_bone_global_rest(i)
			skeleton.get_bone_global_pose(i)
		if resting:
			skeleton.reset_bone_poses()
		else:
			skeleton.set_bone_pose(2, Transform3D(Basis(Vector3.RIGHT, 0.5), Vector3(0.3, 0.2, 0.1)))
		var expected_poses: Array[Transform3D] = []
		var expected_rests: Array[Transform3D] = []
		for i: int in 4:
			expected_poses.append(_composed_pose(skeleton, i))
			expected_rests.append(skeleton.get_bone_global_rest(i))
		POST_IMPORT._repair_hierarchy(skeleton, {0: 3})
		for i: int in 4:
			_expect(_composed_pose(skeleton, i).is_equal_approx(expected_poses[i]), true, "repair preserves current global pose resting=%s bone=%d" % [resting, i])
			_expect(skeleton.get_bone_global_rest(i).is_equal_approx(expected_rests[i]), true, "repair preserves global rest resting=%s bone=%d" % [resting, i])
		skeleton.free()

func _test_surface_materials() -> void:
	var scene := Node3D.new()
	var mesh := MeshInstance3D.new()
	mesh.mesh = BoxMesh.new()
	_add_path(scene, "rig_D/GeneralSkeleton/Face", mesh)
	var shader := Shader.new()
	shader.code = "shader_type spatial;"
	var material := ShaderMaterial.new()
	material.shader = shader
	material.resource_local_to_scene = true
	var manifest := Resource.new()
	manifest.set_meta("surfaces", [{"node_path": "Model/rig_D/Skeleton3D/Face", "surface_index": 0, "material": material}])
	_expect(PRESENTATION._apply_surfaces(scene, "fixture", manifest), "", "surface material import without skeleton binding")
	_expect(mesh.get_surface_override_material(0), material, "surface material preserved")
	_expect(scene.has_meta("face_lighting"), false, "no face lighting runtime bindings")
	scene.free()

func _test_hidden_groups() -> void:
	var expected: Dictionary = {
		"res://scenes/models/ZhuYuan/ZhuYuan.blend": ["char_grp_002", "rig_D/GeneralSkeleton/Weapons"],
		"res://scenes/models/Trigger/Trigger4.2.blend": ["char_grp", "Trigger_Weapon"],
	}
	for source: String in expected:
		var spec: Dictionary = PRESENTATION.MODELS[source]
		var paths: Array = expected[source]
		var key: String = spec["key"]
		var scene := Node3D.new()
		_expect(PRESENTATION._hide_groups(scene, key, spec), key + "/hidden: " + paths[0] + " is missing or not a Node3D", key + " first required path")
		var first: Node3D = _add_path(scene, paths[0], Node3D.new()) as Node3D
		_expect(PRESENTATION._hide_groups(scene, key, spec), key + "/hidden: " + paths[1] + " is missing or not a Node3D", key + " second required path")
		_expect(first.visible, false, key + " hides in order before later failure")
		var wrong: Node = _add_path(scene, paths[1], Node.new())
		_expect(PRESENTATION._hide_groups(scene, key, spec), key + "/hidden: " + paths[1] + " is missing or not a Node3D", key + " rejects non-Node3D")
		wrong.free()
		var second: Node3D = _add_path(scene, paths[1], Node3D.new()) as Node3D
		var unrelated: Node3D = _add_path(scene, "Unrelated", Node3D.new()) as Node3D
		first.visible = true
		_expect(PRESENTATION._hide_groups(scene, key, spec), "", key + " hides valid groups")
		_expect([first.visible, second.visible, unrelated.visible], [false, false, true], key + " visibility")
		scene.free()

func _add_path(scene: Node, path: String, leaf: Node) -> Node:
	var parts: PackedStringArray = path.split("/")
	var parent: Node = scene
	for i: int in parts.size() - 1:
		var child: Node = parent.get_node_or_null(NodePath(parts[i]))
		if child == null:
			child = Node3D.new()
			child.name = parts[i]
			parent.add_child(child)
		parent = child
	leaf.name = parts[-1]
	parent.add_child(leaf)
	return leaf

func _test_recipes() -> void:
	var path: String = ProjectSettings.globalize_path("user://model_import_regression_%d.tres" % OS.get_process_id())
	var tongue: Dictionary = {"source_path": NodePath("Tongue_rig"), "head_bone": "Head", "local_transform": Transform3D.IDENTITY}
	var arm: Dictionary = {"surfaces": [{"roles": PackedByteArray([1, 2])}]}
	for key: String in ["trigger", "zhu_yuan"]:
		var has_arm: bool = key == "zhu_yuan"
		for row: Dictionary in [
			{"metadata": {}, "error": "/recipe/tongue: missing dictionary"},
			{"metadata": {"tongue": "wrong type"}, "error": "/recipe/tongue: missing dictionary"},
			{"metadata": {"tongue": tongue}, "error": "/recipe/arm_partition: missing dictionary" if has_arm else ""},
			{"metadata": {"tongue": tongue, "arm_partition": []}, "error": "/recipe/arm_partition: missing dictionary" if has_arm else ""},
			{"metadata": {"tongue": tongue, "arm_partition": arm}, "error": ""},
		]:
			var resource := Resource.new()
			for meta: String in row["metadata"]:
				resource.set_meta(meta, row["metadata"][meta])
			var saved: Error = ResourceSaver.save(resource, path)
			_expect(saved, OK, key + " saves recipe fixture")
			if saved != OK:
				continue
			var ctx: Dictionary = {"key": key, "has_arm": has_arm}
			var error: String = STRUCTURE._load_recipe(ctx, path)
			_expect(error, key + row["error"] if row["error"] != "" else "", key + " recipe validation")
			if error.is_empty():
				_expect(ctx.get("tongue"), tongue, key + " tongue metadata")
				if has_arm:
					_expect(ctx.get("arm"), arm, key + " arm metadata")
	_expect(DirAccess.remove_absolute(path), OK, "removes recipe fixture")

func _test_role_surfaces() -> void:
	var arrays: Array = _source_arrays()
	var vertex_data := PackedByteArray()
	vertex_data.resize(6 * 20)
	for i: int in vertex_data.size():
		vertex_data[i] = (i * 37 + 11) % 256
	for stride: int in [2, 4]:
		for role: int in [1, 2]:
			var label: String = "role %d stride %d" % [role, stride]
			var data: Dictionary = {"vertex_data": vertex_data, "lods": [{"edge_length": 2.5, "index_data": _encode_indices([4, 0, 2, 1, 5, 3], stride)}]}
			var build: Dictionary = {}
			_expect(_extract(arrays, data, role, stride, source_vertex_roles, build), "", label + " extracts")
			if not build.has("arrays"):
				continue
			var selected: Array = [0, 2, 4] if role == 1 else [1, 3, 5]
			var output: Array = build["arrays"]
			for channel: int in [Mesh.ARRAY_VERTEX, Mesh.ARRAY_NORMAL, Mesh.ARRAY_TANGENT, Mesh.ARRAY_COLOR, Mesh.ARRAY_TEX_UV, Mesh.ARRAY_TEX_UV2, Mesh.ARRAY_BONES, Mesh.ARRAY_WEIGHTS]:
				var width: int = 4 if channel in [Mesh.ARRAY_TANGENT, Mesh.ARRAY_BONES, Mesh.ARRAY_WEIGHTS] else 1
				var expected: Variant = arrays[channel].slice(0, 0)
				for v: int in selected:
					expected.append_array(arrays[channel].slice(v * width, (v + 1) * width))
				_expect(output[channel], expected, label + " channel " + str(channel))
			_expect(output[Mesh.ARRAY_INDEX], PackedInt32Array([0, 1, 2] if role == 1 else [2, 1, 0]), label + " base indices")
			_expect(build["lods"], {2.5: PackedInt32Array([2, 0, 1] if role == 1 else [0, 2, 1])}, label + " LOD indices")
			_expect(build["vertex_count"], 3, label + " vertex count")
			var expected_nt := PackedByteArray()
			for v: int in selected:
				expected_nt.append_array(vertex_data.slice(72 + v * 8, 80 + v * 8))
			_expect(build["nt_bytes"], expected_nt, label + " preserves encoded NT bytes")
			var target := ArrayMesh.new()
			target.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, output)
			_expect(STRUCTURE._update_derived_nt(target, 0, FORMAT, build, label), "", label + " uploads encoded NT bytes")
			var uploaded: Dictionary = RenderingServer.mesh_get_surface(target.get_rid(), 0)
			_expect((uploaded["vertex_data"] as PackedByteArray).slice(36), expected_nt, label + " uploaded NT bytes unchanged")
			for bad_indices: Array in [[0, 2, 1], [0, 2, 6], [65535, 0, 2], [0, 2, 1, 0, 2, 4]]:
				data["lods"][0]["index_data"] = _encode_indices(bad_indices, stride)
				_expect(_extract(arrays, data, role, stride, source_vertex_roles, {}), "fixture: LOD triangle 0 has invalid or cross-role corners", label + " rejects " + str(bad_indices))
			if stride == 4:
				data["lods"][0]["index_data"] = _encode_indices([65536, 0, 2], stride)
				_expect(_extract(arrays, data, role, stride, source_vertex_roles, {}), "fixture: LOD triangle 0 has invalid or cross-role corners", label + " validates full 32-bit index")
			var uncovered: PackedByteArray = source_vertex_roles.duplicate()
			var v: int = 1 if role == 1 else 0
			uncovered[v] = 0
			data["lods"][0]["index_data"] = _encode_indices([v, v, v], stride)
			_expect(_extract(arrays, data, role, stride, uncovered, {}), "fixture: LOD triangle 0 has invalid or cross-role corners", label + " rejects unassigned role before skipping")

func _extract(arrays: Array, data: Dictionary, role: int, stride: int, vertex_roles: PackedByteArray, build: Dictionary) -> String:
	return STRUCTURE._extract_role_surface("fixture", arrays, source_indices, PackedByteArray([1, 2]), vertex_roles, role, stride, data, FORMAT, build)

func _encode_indices(indices: Array, stride: int) -> PackedByteArray:
	var bytes := PackedByteArray()
	bytes.resize(indices.size() * stride)
	for i: int in indices.size():
		if stride == 2:
			bytes.encode_u16(i * stride, indices[i])
		else:
			bytes.encode_u32(i * stride, indices[i])
	return bytes

func _source_arrays() -> Array:
	var arrays: Array = []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = PackedVector3Array()
	arrays[Mesh.ARRAY_NORMAL] = PackedVector3Array()
	arrays[Mesh.ARRAY_TANGENT] = PackedFloat32Array()
	arrays[Mesh.ARRAY_COLOR] = PackedColorArray()
	arrays[Mesh.ARRAY_TEX_UV] = PackedVector2Array()
	arrays[Mesh.ARRAY_TEX_UV2] = PackedVector2Array()
	arrays[Mesh.ARRAY_BONES] = PackedInt32Array()
	arrays[Mesh.ARRAY_WEIGHTS] = PackedFloat32Array()
	arrays[Mesh.ARRAY_INDEX] = source_indices
	for v: int in 6:
		arrays[Mesh.ARRAY_VERTEX].append(Vector3(v, v % 2, 0))
		arrays[Mesh.ARRAY_NORMAL].append(Vector3.UP)
		arrays[Mesh.ARRAY_TANGENT].append_array(PackedFloat32Array([1, 0, 0, 1]))
		arrays[Mesh.ARRAY_COLOR].append(Color(v / 6.0, 0.5, 1.0))
		arrays[Mesh.ARRAY_TEX_UV].append(Vector2(v, v + 1))
		arrays[Mesh.ARRAY_TEX_UV2].append(Vector2(v + 2, v + 3))
		arrays[Mesh.ARRAY_BONES].append_array(PackedInt32Array([v, 0, 0, 0]))
		arrays[Mesh.ARRAY_WEIGHTS].append_array(PackedFloat32Array([1, 0, 0, 0]))
	return arrays

func _test_wardrobe_compiler() -> void:
	var path := "res://scenes/models/import/humanoid_wardrobe.gd"
	_expect(ResourceLoader.exists(path), true, "wardrobe compiler exists")
	if not ResourceLoader.exists(path):
		return
	var compiler: Script = load(path)
	var scene := Node3D.new()
	var rig := _add_path(scene, "rig_D", Node3D.new())
	var garment := MeshInstance3D.new()
	garment.mesh = BoxMesh.new()
	garment.set_meta("extras", {"wardrobe_source_id": "garment"})
	_add_path(scene, "rig_D/GeneralSkeleton/Name With Spaces", garment)
	_add_mask_sources(scene, "res://scenes/models/Trigger/Trigger4.2.blend")
	var data := {"version": 1, "variants": ["Original", "Alternate"], "default_variant": 1,
		"components": {"UpperBody": {"visible": false, "pieces": [{"source": "garment", "variant": -1}]}}, "masks": []}
	_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend").is_empty(), false, "rejects absent wardrobe metadata")
	rig.set_meta("extras", {"wardrobe_catalog": "{"})
	_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend").is_empty(), false, "rejects malformed JSON")
	for field: String in ["version", "variants", "default_variant", "components", "masks"]:
		var bad := data.duplicate(true)
		bad.erase(field)
		rig.set_meta("extras", {"wardrobe_catalog": JSON.stringify(bad)})
		_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend").is_empty(), false, "rejects missing " + field)
	for bad: Dictionary in [
		{"components": {"UpperBody": {"visible": false, "pieces": []}}},
		{"components": {"UpperBody": {"visible": true, "pieces": [{"source": "garment", "variant": -1}, {"source": "garment", "variant": 1}]}}},
		{"components": {"UpperBody": {"visible": true, "pieces": [{"source": "garment", "variant": -1}]}, "Other": {"visible": true, "pieces": [{"source": "garment", "variant": -1}]}}},
		{"variants": ["Original", "Original"]}, {"variants": []}, {"default_variant": 2}, {"default_variant": 0.5},
		{"components": {"UpperBody": {"visible": "false", "pieces": []}}},
		{"components": {"UpperBody": {"visible": false, "pieces": [{"source": "missing", "variant": -1}]}}},
		{"components": {"UpperBody": {"visible": false, "pieces": [{"source": "garment", "variant": 2}]}}},
		{"components": {"UpperBody": {"visible": false, "pieces": [{"role": "NotARole", "variant": -1}]}}},
		{"masks": [{"source": "garment", "name": "missing", "index": 0, "component": "Missing", "enabled": true, "variant": -1}]},
	]:
		var altered := data.duplicate(true)
		altered.merge(bad, true)
		rig.set_meta("extras", {"wardrobe_catalog": JSON.stringify(altered)})
		_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend").is_empty(), false, "rejects malformed wardrobe " + str(bad))
		_expect(scene.has_meta("wardrobe_catalog"), false, "invalid input publishes no catalog")
	rig.set_meta("extras", {"wardrobe_catalog": JSON.stringify(data)})
	var duplicate := MeshInstance3D.new()
	duplicate.set_meta("extras", {"wardrobe_source_id": "garment"})
	scene.add_child(duplicate)
	_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend").is_empty(), false, "rejects duplicate source IDs")
	duplicate.free()
	_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend"), "", "compiles valid source catalog")
	_expect(scene.has_meta("wardrobe_catalog"), true, "publishes generated resource")
	if scene.has_meta("wardrobe_catalog"):
		var catalog: Resource = scene.get_meta("wardrobe_catalog")
		var setups: Variant = catalog.get("_maskSetups")
		_expect(setups is Array, true, "compiler owns fixed mask bindings")
		if setups is Array:
			_expect(setups.size(), 2, "every fixed mask compiled even without wardrobe rules")
			for setup: Resource in setups:
				var mesh_path := str(setup.get("MeshPath")).trim_prefix("Model/")
				var mesh_node := scene.get_node_or_null(NodePath(mesh_path)) as MeshInstance3D
				_expect(mesh_node != null, true, "compiled mask path resolves")
				_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", setup.get("Configuration"), mesh_node.mesh), "", "compiled pairing validated")
		_expect(catalog.get("_variants"), PackedStringArray(["Original", "Alternate"]), "preserves ordered outfit IDs")
		_expect(catalog.get("_defaultVariant"), 1, "preserves default outfit")
		var component: Resource = catalog.get("_components")["UpperBody"]
		_expect(component.get("_visible"), false, "preserves hidden component default")
		var piece: Resource = component.get("_pieces")[0]
		_expect(piece.get("_path"), NodePath("Model/rig_D/GeneralSkeleton/Name With Spaces"), "resolves actual node path without sanitization")
		_expect(piece.get("_variant"), -1, "preserves common garment")
	var body: MeshInstance3D = scene.get_node("rig_D/GeneralSkeleton/ZZZ_Size02_C")
	var original_mesh: Mesh = body.mesh
	body.mesh = ArrayMesh.new()
	_expect(compiler.apply(scene, "res://scenes/models/Trigger/Trigger4.2.blend").is_empty(), false, "empty rules still reject stale fixed mask geometry")
	body.mesh = original_mesh
	_add_mask_sources(scene, "res://scenes/models/ZhuYuan/ZhuYuan.blend")
	var weapons := _add_path(scene, "rig_D/GeneralSkeleton/Weapons", Node3D.new())
	var copy := MeshInstance3D.new()
	copy.set_meta("extras", {"wardrobe_source_id": "garment"})
	weapons.add_child(copy)
	_add_path(scene, "rig_D/GeneralSkeleton/Weapons/BackModule", Node3D.new())
	_add_path(scene, "rig_D/GeneralSkeleton/Weapons/ArmModules", Node3D.new())
	data["components"]["Equipment"] = {"visible": true, "pieces": [{"role": "BackModule", "variant": -1}, {"role": "ArmModules", "variant": -1}]}
	rig.set_meta("extras", {"wardrobe_catalog": JSON.stringify(data)})
	_expect(compiler.apply(scene, "res://scenes/models/ZhuYuan/ZhuYuan.blend"), "", "compiles existing equipment roles despite copied identity metadata")
	if scene.has_meta("wardrobe_catalog"):
		var catalog: Resource = scene.get_meta("wardrobe_catalog")
		var equipment: Resource = catalog.get("_components")["Equipment"]
		_expect(equipment.get("_pieces")[0].get("_path"), NodePath("Model/rig_D/GeneralSkeleton/Weapons/BackModule"), "resolves existing back module subgroup")
		_expect(equipment.get("_pieces")[1].get("_path"), NodePath("Model/rig_D/GeneralSkeleton/Weapons/ArmModules"), "resolves existing arm modules subgroup")
	scene.free()

func _test_mask_import_geometry() -> void:
	var config: Resource = load("res://scenes/models/ModelMaskConfiguration.cs").new()
	_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", config, null).is_empty(), false, "rejects null mask source mesh")
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, _source_arrays())
	_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", config, mesh).is_empty(), false, "rejects stale mask geometry")
	var real_scene: Node = (load("res://scenes/models/Trigger/Trigger4.2.blend") as PackedScene).instantiate()
	var real_mesh: ArrayMesh = real_scene.get_node("rig_D/GeneralSkeleton/ZZZ_Size02_C").mesh
	var real_config: Resource = load("res://resources/models/trigger/presentation/body_mask.res")
	_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", real_config, real_mesh), "", "valid real mask source")
	for kind: String in ["mask count", "default bits", "triangle flags", "default mesh", "default remap", "default primitive"]:
		var broken: Resource = _copy_mask_config(real_config)
		if kind == "mask count":
			var names: Dictionary = broken.get("Masks")
			for i: int in 65:
				names["extra_" + str(i)] = false
		elif kind == "default bits":
			broken.set("DefaultBits", 1 << 62)
		elif kind == "default mesh":
			broken.set("DefaultMesh", real_mesh)
		elif kind == "default primitive":
			var original: ArrayMesh = real_config.get("DefaultMesh")
			var points := ArrayMesh.new()
			points.blend_shape_mode = original.blend_shape_mode
			for i: int in original.get_blend_shape_count():
				points.add_blend_shape(original.get_blend_shape_name(i))
			for i: int in original.get_surface_count():
				points.add_surface_from_arrays(Mesh.PRIMITIVE_POINTS, original.surface_get_arrays(i), original.surface_get_blend_shape_arrays(i), {}, original.surface_get_format(i))
				points.surface_set_material(i, original.surface_get_material(i))
			points.set_meta("source_vertex_indices", original.get_meta("source_vertex_indices"))
			broken.set("DefaultMesh", points)
		elif kind == "default remap":
			var default_mesh: ArrayMesh = (real_config.get("DefaultMesh") as ArrayMesh).duplicate()
			default_mesh.remove_meta("source_vertex_indices")
			broken.set("DefaultMesh", default_mesh)
		else:
			var surfaces: Array = broken.get("TriangleMasks")
			var flags: PackedInt64Array = surfaces[0]
			flags[0] = 1 << 62
			surfaces[0] = flags
		_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", broken, real_mesh).is_empty(), false, "rejects invalid " + kind)
	real_scene.free()


func _test_mask_import_presentation() -> void:
	var validator = MASK_VALIDATION.new()
	_expect(validator.has_method("ValidateMaskPresentation"), true, "import checks outline data before runtime")
	if not validator.has_method("ValidateMaskPresentation"):
		return
	var body := MeshInstance3D.new()
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, _source_arrays())
	body.mesh = mesh
	var material := ShaderMaterial.new()
	var outline := ShaderMaterial.new()
	material.resource_local_to_scene = true
	outline.resource_local_to_scene = true
	material.next_pass = outline
	body.set_surface_override_material(0, material)
	for weights: Variant in ["wrong type", PackedFloat32Array([1]), PackedFloat32Array([1, 1, 1, 1, 1, 1])]:
		outline.set_meta("source_weights", weights)
		_expect(validator.call("ValidateMaskPresentation", body).is_empty(), weights is PackedFloat32Array and weights.size() == 6, "outline weights match source vertices")
	outline.resource_local_to_scene = false
	_expect(validator.call("ValidateMaskPresentation", body).is_empty(), false, "mutable outline must be instance local")
	body.free()


func _test_imported_wardrobes() -> void:
	var compiler: Script = load("res://scenes/models/import/humanoid_wardrobe.gd")
	for source: String in compiler.MASKS:
		var scene: Node = (load(source) as PackedScene).instantiate()
		var key: String = "trigger" if source.contains("Trigger") else "zhu_yuan"
		_expect(scene.has_meta("wardrobe_catalog"), true, key + " packed scene owns generated wardrobe")
		var rig := scene.get_node("rig_D")
		_expect(compiler._extras(rig).has("wardrobe_catalog"), true, key + " retains Blender-owned catalog metadata")
		if not compiler._extras(rig).has("wardrobe_catalog") or not scene.has_meta("wardrobe_catalog"):
			scene.free()
			continue
		var data: Dictionary = JSON.parse_string(compiler._extras(rig)["wardrobe_catalog"])
		var catalog: Resource = scene.get_meta("wardrobe_catalog")
		_expect((catalog.get("_maskSetups") as Array).size(), compiler.MASKS[source].size(), key + " packed import owns all mask setups")
		var components: Dictionary = catalog.get("_components")
		var masks: Array = catalog.get("_masks")
		_expect(components.size(), 5 if key == "trigger" else 7, key + " migrated component count")
		_expect(masks.size(), 11 if key == "trigger" else 6, key + " migrated mask count")
		_expect(catalog.get("_variants"), PackedStringArray(["Original", "Clothing 2", "Clothing Voluptuous"] if key == "trigger" else ["Original"]), key + " exact outfit IDs")
		_expect(catalog.get("_defaultVariant"), 1 if key == "trigger" else 0, key + " exact default outfit")
		var piece_count := 0
		for id: String in components:
			var component: Resource = components[id]
			_expect(component.get("_visible"), data["components"][id]["visible"], key + "/" + id + " visibility")
			var pieces: Array = component.get("_pieces")
			piece_count += pieces.size()
			_expect(pieces.size(), data["components"][id]["pieces"].size(), key + "/" + id + " piece count")
			for i: int in pieces.size():
				var piece: Resource = pieces[i]
				var path: String = str(piece.get("_path"))
				_expect(path.begins_with("Model/"), true, key + " model-relative garment path")
				_expect(scene.get_node_or_null(NodePath(path.trim_prefix("Model/"))) is Node3D, true, key + " garment target exists")
				_expect(piece.get("_variant"), int(data["components"][id]["pieces"][i]["variant"]), key + " garment variant")
		_expect(piece_count, 15 if key == "trigger" else 16, key + " exact garment count")
		for i: int in masks.size():
			var rule: Resource = masks[i]
			var row: Dictionary = data["masks"][i]
			for pair: Array in [["_name", "name"], ["_index", "index"], ["_component", "component"], ["_enabled", "enabled"], ["_variant", "variant"]]:
				_expect(rule.get(pair[0]), row[pair[1]], key + " preserves mask " + str(pair[1]))
		var before := _catalog_snapshot(catalog)
		_expect(compiler.apply(scene, source), "", key + " revalidates actual mask geometry and source IDs")
		_expect(_catalog_snapshot(scene.get_meta("wardrobe_catalog")), before, key + " deterministic recompilation matches packed catalog")
		_expect(compiler.apply(scene, source), "", key + " repeated compile succeeds")
		_expect(_catalog_snapshot(scene.get_meta("wardrobe_catalog")), before, key + " repeated compile has no accumulated entries")
		for source_id: String in compiler.MASKS[source]:
			var config: Resource = load(compiler.MASKS[source][source_id])
			var sources := {}
			_expect(compiler._collect_sources(scene, scene, sources), "", key + " indexes source identities")
			var mesh: ArrayMesh = sources[source_id].mesh
			_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", config, mesh), "", key + " mask geometry matches")
			var too_many: Resource = _copy_mask_config(config)
			var names: Dictionary = too_many.get("Masks")
			for i: int in 65:
				names["extra_" + str(i)] = false
			_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", too_many, mesh).is_empty(), false, key + " rejects more than 64 masks")
			var bad_default: Resource = _copy_mask_config(config)
			bad_default.set("DefaultBits", 1 << 62)
			_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", bad_default, mesh).is_empty(), false, key + " rejects unknown default bits")
			var bad_flags: Resource = _copy_mask_config(config)
			var surfaces: Array = bad_flags.get("TriangleMasks")
			var flags: PackedInt64Array = surfaces[0]
			flags[0] = 1 << 62
			surfaces[0] = flags
			_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", bad_flags, mesh).is_empty(), false, key + " rejects unknown triangle flags")
			var bad_count: Resource = _copy_mask_config(config)
			var bad_surfaces: Array = bad_count.get("TriangleMasks")
			bad_surfaces[0] = PackedInt64Array()
			_expect(MASK_VALIDATION.new().call("ValidateMaskGeometry", bad_count, mesh).is_empty(), false, key + " rejects wrong triangle count")

		for patch: Dictionary in [{"name": "missing"}, {"index": 999}, {"component": "missing"}, {"variant": 99}, {"source": "missing"}]:
			var bad: Dictionary = data.duplicate(true)
			bad["masks"][0].merge(patch, true)
			rig.set_meta("extras", {"wardrobe_catalog": JSON.stringify(bad)})
			_expect(compiler.apply(scene, source).is_empty(), false, key + " rejects broken mask " + str(patch))
			_expect(scene.has_meta("wardrobe_catalog"), false, key + " broken mask publishes no resource")
		rig.set_meta("extras", {"wardrobe_catalog": JSON.stringify(data)})
		_expect(compiler.apply(scene, source), "", key + " recovers after corrected metadata")
		scene.free()

func _add_mask_sources(scene: Node, source: String) -> void:
	var compiler: Script = load("res://scenes/models/import/humanoid_wardrobe.gd")
	var original: Node = (load(source) as PackedScene).instantiate()
	for id: String in compiler.MASKS[source]:
		var path := "rig_D/GeneralSkeleton/" + id
		var old := scene.get_node_or_null(path)
		if old != null:
			old.free()
		var mesh := MeshInstance3D.new()
		mesh.mesh = (original.get_node(path) as MeshInstance3D).mesh
		mesh.set_meta("extras", {"wardrobe_source_id": id})
		_add_path(scene, path, mesh)
	original.free()

func _catalog_snapshot(catalog: Resource) -> Dictionary:
	var result := {"variants": catalog.get("_variants"), "default": catalog.get("_defaultVariant"), "components": {}, "masks": []}
	for id: String in catalog.get("_components"):
		var component: Resource = catalog.get("_components")[id]
		var row := {"visible": component.get("_visible"), "pieces": []}
		for piece: Resource in component.get("_pieces"):
			row["pieces"].append([piece.get("_path"), piece.get("_variant")])
		result["components"][id] = row
	for rule: Resource in catalog.get("_masks"):
		result["masks"].append([rule.get("_path"), rule.get("_name"), rule.get("_index"), rule.get("_component"), rule.get("_enabled"), rule.get("_variant")])
	return result

func _copy_mask_config(original: Resource) -> Resource:
	# Construct fixtures explicitly instead of deep-duplicating C# script handles.
	var copy: Resource = load("res://scenes/models/ModelMaskConfiguration.cs").new()
	copy.set("Masks", (original.get("Masks") as Dictionary).duplicate())
	copy.set("DefaultBits", original.get("DefaultBits"))
	copy.set("DefaultMesh", original.get("DefaultMesh"))
	copy.set("GeometryHash", original.get("GeometryHash"))
	copy.set("TriangleMasks", (original.get("TriangleMasks") as Array).duplicate(true))
	return copy

extends SceneTree
# Run without importing assets: godot --headless --path . --script res://test/model_import_regression.gd

const STRUCTURE = preload("res://scenes/models/import/humanoid_structure.gd")
const PRESENTATION = preload("res://scenes/models/import/humanoid_presentation.gd")
const FORMAT: int = Mesh.ARRAY_FORMAT_VERTEX | Mesh.ARRAY_FORMAT_NORMAL | Mesh.ARRAY_FORMAT_TANGENT
var source_vertex_roles := PackedByteArray([1, 2, 1, 2, 1, 2])
var source_indices := PackedInt32Array([0, 2, 4, 5, 3, 1])
var checks: int = 0
var failures: int = 0

func _initialize() -> void:
	_test_hidden_groups()
	_test_recipes()
	_test_role_surfaces()
	print("MODEL IMPORT REGRESSION: %d checks, %d failures" % [checks, failures])
	quit(0 if failures == 0 else 1)

func _expect(actual: Variant, expected: Variant, label: String) -> void:
	checks += 1
	if actual != expected:
		failures += 1
		printerr("FAIL %s: expected %s, got %s" % [label, str(expected), str(actual)])

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

@tool
extends EditorScenePostImport

const WARDROBE = preload("res://scenes/models/import/humanoid_wardrobe.gd")
const MATERIALS = preload("res://scenes/models/import/grace_crowne_materials.gd")

const MASK_IMPORT = preload("res://scenes/models/import/GraceCrowneMaskImport.cs")

func _post_import(scene: Node) -> Object:
	var skeleton := scene.get_node_or_null("rig_D/GeneralSkeleton") as Skeleton3D
	if skeleton == null:
		return _fail("Missing normalized Grace skeleton")
	var profile := SkeletonProfileHumanoid.new()
	for i in profile.bone_size:
		if String(profile.get_bone_name(i)) in ["Jaw", "LeftEye", "RightEye"]:
			continue
		if skeleton.find_bone(profile.get_bone_name(i)) < 0:
			return _fail("Missing humanoid bone " + String(profile.get_bone_name(i)))
	if scene.find_child("AnimationPlayer", true, false) != null:
		return _fail("Grace source must have frozen controls and no source animation")
	var material_error := MATERIALS.apply(scene)
	if not material_error.is_empty():
		return _fail(material_error)
	var configs := {}
	for node in scene.find_children("*", "MeshInstance3D", true, false):
		var mesh := node as MeshInstance3D
		var extras: Dictionary = mesh.get_meta("extras", {})
		if not extras.has("wardrobe_source_id") or not mesh.mesh is ArrayMesh or mesh.skin == null:
			return _fail("Missing source identity or skinned mesh: " + String(mesh.name))
		for surface in mesh.mesh.get_surface_count():
			var arrays := mesh.mesh.surface_get_arrays(surface)
			var weights: PackedFloat32Array = arrays[Mesh.ARRAY_WEIGHTS]
			var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
			var stride := weights.size() / vertices.size()
			if stride != 4 and stride != 8:
				return _fail("Unsupported skin weight stride")
			for v in vertices.size():
				var total := 0.0
				for w in stride:
					total += weights[v * stride + w]
				if abs(total - 1.0) > 0.002:
					return _fail("Unnormalized Grace skin weights")
		for shape in mesh.mesh.get_blend_shape_count():
			mesh.set_blend_shape_value(shape, 0.0)
		if extras.has("grace_masks"):
			var config: Resource = MASK_IMPORT.new().call("Compile", mesh.mesh, extras["grace_masks"])
			if config == null:
				return _fail("Could not compile frozen masks for " + String(mesh.name))
			configs[extras["wardrobe_source_id"]] = config
	if configs.size() != 2:
		return _fail("Expected body and hair masks")
	var error := WARDROBE.apply(scene, get_source_file(), configs)
	if not error.is_empty():
		return _fail(error)
	print("Grace import: ", skeleton.get_bone_count(), " bones, validated masks and wardrobe")
	return scene

func _fail(message: String) -> Object:
	push_error(get_source_file() + ": Grace import failed: " + message)
	return null

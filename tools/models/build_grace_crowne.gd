extends SceneTree
## Headless authoring only. Configure the GLB, import it, then build native resources.
## godot --headless --path . --script tools/models/build_grace_crowne.gd -- configure
## godot --headless --editor --import --path .
## godot --headless --path . --script tools/models/build_grace_crowne.gd -- wrapper

const ASSET := "res://scenes/models/GraceCrowne/"
const RESOURCES := "res://resources/models/grace_crowne/"

func _initialize() -> void:
	if "materials" in OS.get_cmdline_user_args():
		_configure_materials()
	elif "configure" in OS.get_cmdline_user_args():
		_configure()
	else:
		_wrapper()
	quit()

func _data() -> Dictionary:
	return JSON.parse_string(FileAccess.get_file_as_string(ASSET + "authoring-data.json"))

func _save(resource: Resource, path: String) -> void:
	assert(ResourceSaver.save(resource, path, ResourceSaver.FLAG_CHANGE_PATH) == OK, "Cannot save " + path)
	resource.take_over_path(path)

func _configure() -> void:
	DirAccess.make_dir_recursive_absolute(RESOURCES)
	var map := BoneMap.new()
	map.profile = SkeletonProfileHumanoid.new()
	for name: String in _data()["bone_map"]:
		map.set_skeleton_bone_name(name, _data()["bone_map"][name])
	_save(map, RESOURCES + "bone_map.res")
	var settings := ConfigFile.new()
	settings.set_value("remap", "importer", "scene")
	settings.set_value("remap", "importer_version", 1)
	settings.set_value("remap", "type", "PackedScene")
	settings.set_value("deps", "source_file", ASSET + "GraceCrowne.glb")
	settings.set_value("params", "import_script/path", "res://scenes/models/import/grace_crowne_post_import.gd")
	settings.set_value("params", "animation/import", false)
	settings.set_value("params", "meshes/generate_lods", false)
	settings.set_value("params", "meshes/create_shadow_meshes", false)
	settings.set_value("params", "meshes/force_disable_compression", true)
	settings.set_value("params", "_subresources", {"nodes": {"PATH:rig_D/Skeleton3D": {
		"retarget/bone_map": map,
		"retarget/bone_renamer/rename_bones": true,
		"retarget/bone_renamer/unique_node/make_unique": true,
		"retarget/bone_renamer/unique_node/skeleton_name": "GeneralSkeleton",
		"retarget/rest_fixer/apply_node_transforms": false,
		"retarget/rest_fixer/fix_silhouette/enable": false,
		"retarget/rest_fixer/keep_global_rest_on_leftovers": true,
		"retarget/rest_fixer/reset_all_bone_poses_after_import": true,
		"retarget/rest_fixer/retarget_method": 1,
	}}})
	assert(settings.save(ASSET + "GraceCrowne.glb.import") == OK)
	print("Grace import configured")

func _animation(library: AnimationLibrary, name: String, targets: Array, value: float) -> void:
	var animation := Animation.new()
	animation.length = 1.0
	animation.loop_mode = Animation.LOOP_LINEAR
	for target: String in targets:
		var track := animation.add_track(Animation.TYPE_BLEND_SHAPE)
		animation.track_set_path(track, NodePath(target))
		animation.blend_shape_track_insert_key(track, 0.0, value)
		animation.blend_shape_track_insert_key(track, 1.0, value)
	library.add_animation(name, animation)

func _clip(graph: AnimationNodeBlendTree, name: String, animation: String, at: Vector2) -> void:
	var node := AnimationNodeAnimation.new()
	node.animation = animation
	graph.add_node(name, node, at)

func _filter(node: AnimationNode, paths: Array) -> void:
	node.filter_enabled = true
	for path: String in paths:
		node.set_filter_path(NodePath(path), true)

func _wrapper() -> void:
	var imported := load(ASSET + "GraceCrowne.glb") as PackedScene
	assert(imported != null, "Grace import must succeed before building its wrapper")
	var root: Node3D = load("res://scenes/models/CharacterModel.cs").new()
	root.name = "GraceCrowne"
	var model := imported.instantiate()
	model.name = "Model"
	root.add_child(model)
	model.owner = root
	var body := model.find_child("grace_crowne", true, false) as MeshInstance3D
	assert(body != null and body.mesh.get_blend_shape_count() == 73, "Facial shapes were lost")
	var face := AnimationLibrary.new()
	var paths: Array = []
	var shapes: Array = []
	for i in body.mesh.get_blend_shape_count():
		var name := String(body.mesh.get_blend_shape_name(i))
		shapes.append(name)
		paths.append(String(root.get_path_to(body)) + ":" + name)
	_animation(face, "neutral", paths, 0.0)
	_animation(face, "RESET", paths, 0.0)
	for i in shapes.size():
		_animation(face, "expr_" + shapes[i], [paths[i]], 1.0)
	_save(face, RESOURCES + "humanoid_face.res")
	var player := AnimationPlayer.new()
	player.name = "ModelAnimationPlayer"
	player.add_animation_library("body", load("res://resources/models/shared/humanoid_body.res"))
	player.add_animation_library("face", face)
	root.add_child(player)
	player.owner = root
	var graph := AnimationNodeBlendTree.new()
	_clip(graph, "BodyNeutral", "body/neutral", Vector2(-600, -200))
	_clip(graph, "Motion", "body/motion_demo", Vector2(-600, -100))
	_clip(graph, "HeadTurnClip", "body/head_turn", Vector2(-600, 0))
	graph.add_node("Movement", AnimationNodeBlend2.new(), Vector2(-350, -200))
	graph.connect_node("Movement", 0, "BodyNeutral")
	graph.connect_node("Movement", 1, "Motion")
	var head := AnimationNodeBlend2.new()
	_filter(head, ["Model/rig_D/GeneralSkeleton:Head"])
	graph.add_node("HeadTurn", head, Vector2(-100, -200))
	graph.connect_node("HeadTurn", 0, "Movement")
	graph.connect_node("HeadTurn", 1, "HeadTurnClip")
	_clip(graph, "FaceNeutral", "face/neutral", Vector2(-350, 100))
	var base := AnimationNodeAdd2.new()
	_filter(base, paths)
	graph.add_node("FaceBase", base, Vector2(150, -200))
	graph.connect_node("FaceBase", 0, "HeadTurn")
	graph.connect_node("FaceBase", 1, "FaceNeutral")
	var previous := "FaceBase"
	for i in shapes.size():
		var name: String = shapes[i]
		var y := 300.0 + i * 180.0
		_clip(graph, name + "Clip", "face/expr_" + name, Vector2(-600, y))
		_clip(graph, name + "Ref", "face/neutral", Vector2(-600, y + 80))
		var delta := AnimationNodeSub2.new()
		_filter(delta, [paths[i]])
		graph.add_node(name + "Delta", delta, Vector2(-300, y))
		graph.connect_node(name + "Delta", 0, name + "Clip")
		graph.connect_node(name + "Delta", 1, name + "Ref")
		var add := AnimationNodeAdd2.new()
		_filter(add, [paths[i]])
		graph.add_node(name, add, Vector2(150, y))
		graph.connect_node(name, 0, previous)
		graph.connect_node(name, 1, name + "Delta")
		previous = name
	graph.connect_node("output", 0, previous)
	_save(graph, RESOURCES + "humanoid_face_tree.res")
	var tree := AnimationTree.new()
	tree.name = "ModelAnimationTree"
	tree.tree_root = graph
	tree.anim_player = NodePath("../ModelAnimationPlayer")
	root.add_child(tree)
	tree.owner = root
	tree.set("parameters/FaceBase/add_amount", 1.0)
	for name: String in shapes:
		tree.set("parameters/" + name + "Delta/sub_amount", 1.0)
	tree.active = true
	var scene := PackedScene.new()
	assert(scene.pack(root) == OK)
	_save(scene, ASSET + "GraceCrowne.scn")
	root.free()
	print("Grace CharacterModel wrapper saved with 73 native expression channels")

func _configure_materials() -> void:
	var directory := ASSET + "materials/"
	for filename in DirAccess.get_files_at(directory):
		if not filename.ends_with(".png") and not filename.ends_with(".exr"):
			continue
		var path := directory + filename + ".import"
		var settings := ConfigFile.new()
		if settings.load(path) != OK:
			settings.set_value("remap", "importer", "texture")
			settings.set_value("remap", "type", "CompressedTexture2D")
			settings.set_value("deps", "source_file", directory + filename)
		settings.set_value("params", "mipmaps/generate", true)
		assert(settings.save(path) == OK)
	print("Grace static material textures configured with mipmaps")

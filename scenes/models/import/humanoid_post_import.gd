@tool
extends EditorScenePostImport

const STRUCTURE := preload("res://scenes/models/import/humanoid_structure.gd")
const WARDROBE := preload("res://scenes/models/import/humanoid_wardrobe.gd")
const PRESENTATION := preload("res://scenes/models/import/humanoid_presentation.gd")
const SKELETON := "rig_D/GeneralSkeleton"
const MAP := [
	"Root", "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
	"LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
	"LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
	"LeftThumbMetacarpal", "LeftThumbProximal", "LeftThumbDistal",
	"LeftIndexProximal", "LeftIndexIntermediate", "LeftIndexDistal",
	"LeftMiddleProximal", "LeftMiddleIntermediate", "LeftMiddleDistal",
	"LeftRingProximal", "LeftRingIntermediate", "LeftRingDistal",
	"LeftLittleProximal", "LeftLittleIntermediate", "LeftLittleDistal",
	"RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
	"RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
	"RightThumbMetacarpal", "RightThumbProximal", "RightThumbDistal",
	"RightIndexProximal", "RightIndexIntermediate", "RightIndexDistal",
	"RightMiddleProximal", "RightMiddleIntermediate", "RightMiddleDistal",
	"RightRingProximal", "RightRingIntermediate", "RightRingDistal",
	"RightLittleProximal", "RightLittleIntermediate", "RightLittleDistal",
]
const LINKS := {
	"head_scale_fix.x": "Neck",
	"LeftLowerArm": "LeftUpperArm",
	"RightLowerArm": "RightUpperArm",
	"LeftFoot": "LeftLowerLeg",
	"RightFoot": "RightLowerLeg",
	"thigh_twist.l": "LeftUpperLeg",
	"thigh_twist.r": "RightUpperLeg",
	"thigh_stretch.l": "LeftUpperLeg",
	"thigh_stretch.r": "RightUpperLeg",
	"leg_stretch.l": "LeftLowerLeg",
	"leg_stretch.r": "RightLowerLeg",
	"arm_twist.l": "LeftUpperArm",
	"arm_twist.r": "RightUpperArm",
	"arm_stretch.l": "LeftUpperArm",
	"arm_stretch.r": "RightUpperArm",
	"forearm_stretch.l": "LeftLowerArm",
	"forearm_stretch.r": "RightLowerArm",
}

func _post_import(scene: Node) -> Object:
	var skeleton := scene.get_node_or_null(SKELETON) as Skeleton3D
	if skeleton == null:
		return _fail("Missing " + SKELETON)
	var ids := {}
	for i in skeleton.get_bone_count():
		ids[String(skeleton.get_bone_name(i))] = i
	for bone_name in MAP:
		if not ids.has(bone_name):
			return _fail("Missing mapped bone " + bone_name)
	var repairs := {}
	for child in LINKS:
		var parent: String = LINKS[child]
		if not ids.has(child) or not ids.has(parent):
			return _fail("Missing repair bone %s -> %s" % [child, parent])
		repairs[ids[child]] = ids[parent]
	var clip_error := _unsupported_skeletal_clip(scene, skeleton)
	if not clip_error.is_empty():
		return _fail(clip_error)
	_repair_hierarchy(skeleton, repairs)
	print("Humanoid repair %s: bones=%d links=%d" % [get_source_file(), skeleton.get_bone_count(), repairs.size()])
	var structure_error: String = STRUCTURE.apply(scene, get_source_file())
	if not structure_error.is_empty():
		return _fail(structure_error)
	var presentation_error: String = PRESENTATION.apply(scene, get_source_file())
	if not presentation_error.is_empty():
		return _fail(presentation_error)
	var wardrobe_result: Variant = WARDROBE.apply(scene, get_source_file())
	if typeof(wardrobe_result) != TYPE_STRING:
		return _fail("Wardrobe compiler did not return a result")
	if not wardrobe_result.is_empty():
		return _fail(wardrobe_result)
	var catalog: Variant = scene.get_meta("wardrobe_catalog") if scene.has_meta("wardrobe_catalog") else null
	if not catalog is Resource or catalog.get_script() != load("res://scenes/models/ModelWardrobeConfiguration.cs"):
		return _fail("Wardrobe compiler did not publish a typed catalog")
	return scene

static func _repair_hierarchy(skeleton: Skeleton3D, repairs: Dictionary) -> void:
	# Capture composed transforms under the original hierarchy, move the linked
	# bones, then re-derive their locals so global transforms are unchanged.
	var rests: Array[Transform3D] = []
	var poses: Array[Transform3D] = []
	for i in skeleton.get_bone_count():
		rests.append(skeleton.get_bone_global_rest(i))
		# Retargeting updates local poses outside the scene tree, where Godot
		# does not invalidate its global pose cache. Compose the current locals
		# instead of baking stale pre-retarget transforms into repaired bones.
		var pose := skeleton.get_bone_pose(i)
		var parent := skeleton.get_bone_parent(i)
		while parent >= 0:
			pose = skeleton.get_bone_pose(parent) * pose
			parent = skeleton.get_bone_parent(parent)
		poses.append(pose)
	for i in repairs:
		skeleton.set_bone_parent(i, repairs[i])
	for i in repairs:
		var parent_index: int = repairs[i]
		skeleton.set_bone_rest(i, rests[parent_index].affine_inverse() * rests[i])
		skeleton.set_bone_pose(i, poses[parent_index].affine_inverse() * poses[i])

func _unsupported_skeletal_clip(scene: Node, skeleton: Skeleton3D) -> String:
	for node in scene.find_children("*", "AnimationPlayer", true, false):
		var player := node as AnimationPlayer
		var animation_root := player.get_node_or_null(player.root_node)
		if animation_root == null:
			return "AnimationPlayer %s has unresolved root_node %s" % [str(scene.get_path_to(player)), str(player.root_node)]
		for animation_name in player.get_animation_list():
			var clip := player.get_animation(animation_name)
			for track in clip.get_track_count():
				var track_type := clip.track_get_type(track)
				if track_type != Animation.TYPE_POSITION_3D and track_type != Animation.TYPE_ROTATION_3D and track_type != Animation.TYPE_SCALE_3D:
					continue
				var path: NodePath = clip.track_get_path(track)
				if path.get_subname_count() == 0:
					continue
				var target := animation_root.get_node_or_null(NodePath(String(path.get_concatenated_names())))
				if target == skeleton:
					return "Unsupported skeletal clip %s in AnimationPlayer %s targets %s:%s" % [String(animation_name), str(scene.get_path_to(player)), str(path.get_concatenated_names()), str(path.get_subname(0))]
	return ""

func _fail(message: String) -> Object:
	var source := get_source_file()
	if source.is_empty():
		source = "unknown import source"
	push_error("%s: humanoid post-import repair failed: %s" % [source, message])
	return null

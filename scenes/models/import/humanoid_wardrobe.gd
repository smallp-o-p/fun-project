@tool
extends RefCounted
# Blender rig_D owns the JSON catalog. Only import parses it; the packed scene
# owns the compiled storage-only Resource. Source IDs are explicit Blender
# properties, never inferred from collection names or sanitized object names.
const STRUCTURE = preload("res://scenes/models/import/humanoid_structure.gd")
const MASK_VALIDATION = preload("res://scenes/models/import/ModelImportValidation.cs")
const ROOT := "res://scenes/models/"
const MASKS := {
	"res://scenes/models/ZhuYuan/ZhuYuan.blend": {
		"ZZZ_Size02_C": "res://resources/models/zhu_yuan/presentation/body_mask.res",
	},
	"res://scenes/models/Trigger/Trigger4.2.blend": {
		"ZZZ_Size02_C": "res://resources/models/trigger/presentation/body_mask.res",
		"Pantyhose": "res://resources/models/trigger/presentation/pantyhose_mask.res",
	},
}

static func apply(scene: Node, source_file: String) -> String:
	if scene == null or not MASKS.has(source_file):
		return "wardrobe: missing scene or unknown source " + source_file
	# A rejected recompile must not leave a stale compiled definition behind.
	if scene.has_meta("wardrobe_catalog"):
		scene.remove_meta("wardrobe_catalog")
	var rig := scene.get_node_or_null("rig_D")
	var metadata := _extras(rig)
	if typeof(metadata.get("wardrobe_catalog")) != TYPE_STRING:
		return "wardrobe: rig_D requires wardrobe_catalog JSON string"
	var parser := JSON.new()
	if parser.parse(metadata["wardrobe_catalog"]) != OK or not parser.data is Dictionary:
		return "wardrobe: malformed catalog JSON object"
	var data: Dictionary = parser.data
	var sources: Dictionary = {}
	var error := _collect_sources(scene, scene, sources)
	if not error.is_empty():
		return error
	var resolved: Dictionary = {"sources": sources, "scene": scene, "source_file": source_file, "mask_configs": {}}
	error = _validate(data, resolved)
	if not error.is_empty():
		return error
	var catalog: Resource = load(ROOT + "ModelWardrobeConfiguration.cs").new()
	var setups: Array = catalog.get("_maskSetups")
	for id: String in resolved["mask_configs"]:
		var setup: Resource = load(ROOT + "ModelMeshMaskSetup.cs").new()
		setup.set("MeshPath", _path(resolved, {"source": id}))
		setup.set("Configuration", resolved["mask_configs"][id])
		setups.append(setup)
	catalog.set("_variants", PackedStringArray(data["variants"]))
	catalog.set("_defaultVariant", int(data["default_variant"]))
	var components: Dictionary = catalog.get("_components")
	for id: String in data["components"]:
		var row: Dictionary = data["components"][id]
		var component: Resource = load(ROOT + "ModelWardrobeComponent.cs").new()
		component.set("_visible", row["visible"])
		var pieces: Array = component.get("_pieces")
		for entry: Dictionary in row["pieces"]:
			var garment: Resource = load(ROOT + "ModelWardrobeGarment.cs").new()
			garment.set("_path", _path(resolved, entry))
			garment.set("_variant", int(entry["variant"]))
			pieces.append(garment)
		components[id] = component
	var masks: Array = catalog.get("_masks")
	for entry: Dictionary in data["masks"]:
		var rule: Resource = load(ROOT + "ModelWardrobeMaskRule.cs").new()
		rule.set("_path", _path(resolved, entry))
		rule.set("_name", StringName(entry["name"]))
		rule.set("_index", int(entry["index"]))
		rule.set("_component", entry["component"])
		rule.set("_enabled", entry["enabled"])
		rule.set("_variant", int(entry["variant"]))
		masks.append(rule)
	scene.set_meta("wardrobe_catalog", catalog)
	return ""

static func _collect_sources(root: Node, node: Node, sources: Dictionary) -> String:
	# Structural weapon copies inherit custom properties; only Blender nodes
	# participate in source identity. Generated groups use the explicit roles.
	if String(root.get_path_to(node)) == STRUCTURE.MAIN_SKELETON_PATH + "/Weapons":
		return ""
	var metadata := _extras(node)
	if metadata.has("wardrobe_source_id"):
		var id: Variant = metadata["wardrobe_source_id"]
		if not _text(id) or not node is MeshInstance3D:
			return "wardrobe: source ID requires a nonempty string on a MeshInstance3D"
		if sources.has(id):
			return "wardrobe: duplicate source ID " + id
		sources[id] = node
	for child: Node in node.get_children():
		var error := _collect_sources(root, child, sources)
		if not error.is_empty():
			return error
	return ""

static func _validate(data: Dictionary, ctx: Dictionary) -> String:
	if not _fields(data, ["version", "variants", "default_variant", "components", "masks"]) or not _integer(data.get("version")) or data["version"] != 1:
		return "wardrobe: expected version 1 catalog fields"
	if not data["variants"] is Array or data["variants"].is_empty():
		return "wardrobe: variants must be a nonempty array"
	var ids := {}
	for id: Variant in data["variants"]:
		if not _text(id) or ids.has(id):
			return "wardrobe: outfit IDs must be nonempty and unique"
		ids[id] = true
	var count: int = ids.size()
	if not _integer(data["default_variant"]) or data["default_variant"] < 0 or data["default_variant"] >= count:
		return "wardrobe: default variant is outside outfits"
	if not data["components"] is Dictionary or not data["masks"] is Array:
		return "wardrobe: components must be an object and masks an array"
	var garments := {}
	for id: Variant in data["components"]:
		var row: Variant = data["components"][id]
		if not _text(id) or not _fields(row, ["visible", "pieces"]) or typeof(row["visible"]) != TYPE_BOOL or not row["pieces"] is Array or row["pieces"].is_empty():
			return "wardrobe: invalid component " + str(id)
		for entry: Variant in row["pieces"]:
			if not entry is Dictionary or not (_fields(entry, ["source", "variant"]) or _fields(entry, ["role", "variant"])) or not _variant(entry["variant"], count):
				return "wardrobe: invalid garment in " + str(id)
			var target := _target(ctx, entry)
			if target == null:
				return "wardrobe: unresolved garment " + str(entry)
			if garments.has(target):
				return "wardrobe: duplicate garment ownership: " + str(entry)
			garments[target] = true
	var masks_error := _validate_mask_sources(ctx)
	if not masks_error.is_empty():
		return masks_error
	for entry: Variant in data["masks"]:
		if not _fields(entry, ["source", "name", "index", "component", "enabled", "variant"]) or not _text(entry["name"]) or not _integer(entry["index"]) or entry["index"] < 0 or typeof(entry["enabled"]) != TYPE_BOOL or not _variant(entry["variant"], count):
			return "wardrobe: invalid mask rule"
		if typeof(entry["component"]) != TYPE_STRING or (entry["component"] != "" and not data["components"].has(entry["component"])):
			return "wardrobe: mask references unknown component"
		var node := _target(ctx, entry) as MeshInstance3D
		if node == null or not node.mesh is ArrayMesh:
			return "wardrobe: mask target requires an ArrayMesh"
		var configs: Dictionary = ctx["mask_configs"]
		var id: String = entry["source"]
		if not configs.has(id):
			return "wardrobe: source has no mask configuration: " + id
		var config: Resource = configs[id]
		var names: Array = (config.get("Masks") as Dictionary).keys()
		if entry["index"] >= names.size() or names[int(entry["index"])] != entry["name"]:
			return "wardrobe: mask name/index mismatch: " + id + "/" + entry["name"]
	return ""

static func _target(ctx: Dictionary, entry: Dictionary) -> Node3D:
	if entry.has("source"):
		if not _text(entry["source"]):
			return null
		return ctx["sources"].get(entry["source"]) as MeshInstance3D
	if not _text(entry.get("role")) or not STRUCTURE.RECIPES[ctx["source_file"]]["arm"]:
		return null
	var roles: Array = ["ArmModules"]
	for row: Dictionary in STRUCTURE.WEAPON_COPIES:
		roles.append(row["container"])
	if not roles.has(entry["role"]):
		return null
	return (ctx["scene"] as Node).get_node_or_null(NodePath(STRUCTURE.MAIN_SKELETON_PATH + "/Weapons/" + entry["role"])) as Node3D

static func _path(ctx: Dictionary, entry: Dictionary) -> NodePath:
	return NodePath("Model/" + String((ctx["scene"] as Node).get_path_to(_target(ctx, entry))))

static func _fields(value: Variant, fields: Array) -> bool:
	if not value is Dictionary or value.size() != fields.size():
		return false
	for field: String in fields:
		if not value.has(field):
			return false
	return true

static func _text(value: Variant) -> bool:
	return typeof(value) == TYPE_STRING and not value.strip_edges().is_empty()

static func _integer(value: Variant) -> bool:
	return (typeof(value) == TYPE_INT or typeof(value) == TYPE_FLOAT) and is_finite(float(value)) and float(value) == floor(float(value))

static func _variant(value: Variant, count: int) -> bool:
	return _integer(value) and value >= -1 and value < count

# Godot transports glTF extras as one metadata dictionary, not individual keys.
static func _extras(node: Node) -> Dictionary:
	if node == null or not node.has_meta("extras"):
		return {}
	var value: Variant = node.get_meta("extras")
	return value if value is Dictionary else {}

# Wrappers always initialize these fixed masks, even if no wardrobe rule uses
# them. Validate all geometry here so an empty rules array cannot bypass import.
static func _validate_mask_sources(ctx: Dictionary) -> String:
	var paths: Dictionary = MASKS[ctx["source_file"]]
	for id: String in paths:
		var node: MeshInstance3D = ctx["sources"].get(id) as MeshInstance3D
		if node == null or not node.mesh is ArrayMesh:
			return "wardrobe: fixed mask source requires an ArrayMesh: " + id
		var config: Resource = load(paths[id])
		if config == null:
			return "wardrobe: mask configuration lacks import geometry validation: " + id
		var result: Variant = MASK_VALIDATION.new().call("ValidateMaskGeometry", config, node.mesh)
		if typeof(result) != TYPE_STRING:
			return "wardrobe: mask geometry validation did not return a result: " + id
		if not result.is_empty():
			return "wardrobe: " + id + ": " + result
		result = MASK_VALIDATION.new().call("ValidateMaskPresentation", node)
		if typeof(result) != TYPE_STRING or not result.is_empty():
			return "wardrobe: " + id + ": invalid mask presentation: " + str(result)
		ctx["mask_configs"][id] = config
	return ""

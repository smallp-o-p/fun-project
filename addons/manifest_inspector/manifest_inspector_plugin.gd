@tool
extends EditorInspectorPlugin

const MODEL_PATH_PREFIX := "res://resources/models/"
const SUMMARY_LENGTH := 60

func _can_handle(object: Object) -> bool:
	var resource := object as Resource
	return resource != null and resource.resource_path.begins_with(MODEL_PATH_PREFIX)

func _parse_begin(object: Object) -> void:
	var resource := object as Resource
	var meta_names := resource.get_meta_list()
	if meta_names.is_empty():
		return
	var box := VBoxContainer.new()
	var header := Label.new()
	header.text = "Manifest metadata (read-only)"
	var settings := LabelSettings.new()
	settings.font_size = 14
	header.label_settings = settings
	box.add_child(header)
	for meta_name in meta_names:
		var row := Label.new()
		row.text = "%s: %s" % [meta_name, _summarize(resource.get_meta(meta_name))]
		row.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		box.add_child(row)
	var button := Button.new()
	button.text = "Print full manifest to Output"
	button.pressed.connect(_print_manifest.bind(resource))
	box.add_child(button)
	add_custom_control(box)

func _summarize(value: Variant) -> String:
	var text := ""
	match typeof(value):
		TYPE_DICTIONARY:
			text = "Dictionary (%d keys)" % (value as Dictionary).size()
		TYPE_ARRAY:
			text = "Array (%d items)" % (value as Array).size()
		_:
			if value is Resource:
				var nested := value as Resource
				text = "%s %s" % [nested.get_class(), nested.resource_path]
			else:
				text = str(value)
	if text.length() > SUMMARY_LENGTH:
		text = text.substr(0, SUMMARY_LENGTH - 1) + "…"
	return text

func _print_manifest(resource: Resource) -> void:
	print("[manifest] ", resource.resource_path)
	for meta_name in resource.get_meta_list():
		print("[manifest] %s = %s" % [meta_name, _dump(resource.get_meta(meta_name))])

static func _dump(value: Variant) -> String:
	if value is Resource:
		var nested := value as Resource
		return "%s(%s)" % [nested.get_class(), nested.resource_path]
	if value is Array:
		var items := PackedStringArray()
		for item: Variant in value:
			items.append(_dump(item))
		return "[%s]" % ", ".join(items)
	if value is Dictionary:
		var entries := PackedStringArray()
		for key: Variant in value:
			entries.append("%s: %s" % [str(key), _dump(value[key])])
		return "{%s}" % ", ".join(entries)
	return str(value)

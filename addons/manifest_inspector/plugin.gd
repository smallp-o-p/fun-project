@tool
extends EditorPlugin

const MANIFEST_INSPECTOR := preload("res://addons/manifest_inspector/manifest_inspector_plugin.gd")

var _inspector_plugin: EditorInspectorPlugin

func _enter_tree() -> void:
	_inspector_plugin = MANIFEST_INSPECTOR.new()
	add_inspector_plugin(_inspector_plugin)

func _exit_tree() -> void:
	if _inspector_plugin != null:
		remove_inspector_plugin(_inspector_plugin)
		_inspector_plugin = null

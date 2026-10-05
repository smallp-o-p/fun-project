@tool
extends RefCounted
## Source-specific material adapter. Shader/textures are shared; material state
## is scene-local so instances and mask render meshes retain isolated overrides.
const ROOT := "res://scenes/models/GraceCrowne/materials/"
const SHADER := preload("res://scenes/models/GraceCrowne/grace_crowne.gdshader")

static func apply(scene: Node) -> String:
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(ROOT + "materials.json"))
	if not data is Dictionary or data.get("version") != 1:
		return "Missing Grace material extraction manifest"
	var materials: Dictionary = {}
	for mesh: MeshInstance3D in scene.find_children("*", "MeshInstance3D", true, false):
		for surface in mesh.mesh.get_surface_count():
			var original := mesh.mesh.surface_get_material(surface)
			if original == null or not data.materials.has(original.resource_name):
				return "No source material for " + String(mesh.name) + " surface " + str(surface)
			var name: String = original.resource_name
			if not materials.has(name):
				var row: Dictionary = data.materials[name]
				var material := ShaderMaterial.new()
				material.shader = SHADER
				material.resource_name = name
				material.resource_local_to_scene = true
				for channel: String in row.textures:
					var texture := load(ROOT + row.textures[channel]) as Texture2D
					if texture == null:
						return "Missing Grace " + channel + " texture for " + name
					material.set_shader_parameter(channel + "_texture", texture)
				material.set_shader_parameter("metal_atlas_texture", load(ROOT + data.atlas))
				for parameter: String in row:
					if parameter in ["textures", "ranges"]:
						continue
					var value: Variant = row[parameter]
					if parameter in ["metal_lut_mask", "metal_lut_channel"]:
						value = Vector3(value[0], value[1], value[2])
					material.set_shader_parameter(parameter, value)
				material.set_shader_parameter("skin_scatter_strength", minf(1.0, 0.005 / float(ProjectSettings.get_setting("rendering/environment/subsurface_scattering/subsurface_scattering_scale", 0.05))))
				material.set_shader_parameter("hair_enabled", 1.0 if row.has("hair_exponent") else 0.0)
				material.set_shader_parameter("eyes_enabled", name == "grace_crowne_eyes")
				materials[name] = material
			mesh.mesh.surface_set_material(surface, materials[name])
			mesh.set_surface_override_material(surface, materials[name])
	return ""

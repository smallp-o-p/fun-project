shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

// Static source shader inputs, evaluated without lighting or camera terms.
uniform sampler2D base_texture : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D pbr_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D effects_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D emission_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D hair_emission_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D hair_mask_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D hair_tangent_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D fx_mask_texture : filter_linear_mipmap_anisotropic;
uniform sampler2D metal_atlas_texture : filter_linear, repeat_disable;
uniform float skin_exponent = 1.0;
uniform float skin_scatter_strength = 0.1; // Source 5 mm / Godot default 50 mm profile.
uniform float skin_specular_scale = 0.8434; // ((1.45-1)/(1.45+1))^2 / 0.04.
uniform float hair_enabled = 0.0;
uniform float hair_brightness = 0.5;
uniform float hair_exponent = 3.5;
uniform float hair_multiple = 8.0;
uniform float metal_lut_enabled = 0.0;
uniform float metal_lut_intensity = 0.0;
uniform vec3 metal_lut_mask = vec3(0.0);
uniform vec3 metal_lut_channel = vec3(0.0);
uniform float metal_lut_curve = 0.0;
uniform float emissive_fresnel_enabled = 0.0;
uniform float emissive_fresnel_power = 4.0;
uniform float emissive_fresnel_inverted = 0.0;
uniform bool eyes_enabled = false;
uniform float eye_refraction = 0.02;

void fragment() {
	vec2 sample_uv = UV;
	// FPv4's corneal parallax is evaluated in the deformed mesh's tangent frame.
	// The transport bake contains only the frontal iris and fixed UV highlight.
	if (eyes_enabled) {
		vec2 center = UV.y > 0.76 ? vec2(0.94, 0.878) : vec2(0.94, 0.65);
		vec2 iris = (UV - center) * vec2(1.0, 2.0);
		float cornea = 1.0 - smoothstep(0.0624, 0.078, length(iris));
		vec2 tangent_view = vec2(dot(VIEW, TANGENT), dot(VIEW, BINORMAL));
		sample_uv -= cornea * eye_refraction * tangent_view * vec2(1.0, 0.5);
	}
	vec3 base = texture(base_texture, sample_uv).rgb;
	vec3 pbr = texture(pbr_texture, sample_uv).rgb;
	vec3 effects = texture(effects_texture, sample_uv).rgb;
	vec3 mapped_normal = texture(normal_texture, sample_uv).xyz * 2.0 - 1.0;
	vec3 surface_normal = normalize(TANGENT * mapped_normal.x + BINORMAL * mapped_normal.y + NORMAL * mapped_normal.z);
	float facing = clamp(abs(dot(surface_normal, VIEW)), 0.0, 1.0);
	// DD Fresnel: 0.04 + 0.96 * pow(1 - N.V, Skin Boost).
	// Static DD colour was extracted at Fresnel=0, so apply only its ratio.
	float dd_fresnel = 0.04 + 0.96 * pow(1.0 - facing, skin_exponent);
	base *= mix(1.0, (2.0 - dd_fresnel) * 0.5, effects.r);
	float hair_mask = texture(hair_mask_texture, UV).r * hair_enabled;
	float hair_fresnel = hair_brightness * pow(1.0 - facing, hair_exponent);
	base = mix(base, clamp(base * (1.0 + hair_multiple * hair_fresnel), vec3(0.0), vec3(1.0)), hair_mask);
	// FPv4 FX Mask returns the BLUE component of the masked colour, not a dot.
	float metal_mask = texture(fx_mask_texture, UV).b * metal_lut_mask.b * metal_lut_enabled;
	float lut = texture(metal_atlas_texture, vec2(facing, (metal_lut_curve + 0.5) / 4.0)).b * metal_lut_channel.b;
	base *= mix(1.0, mix(1.0, lut, metal_lut_intensity), metal_mask);
	ALBEDO = base;
	NORMAL_MAP = mapped_normal * 0.5 + 0.5;
	METALLIC = pbr.r;
	ROUGHNESS = clamp(pbr.g, 0.02, 1.0);
	SPECULAR = pbr.b * mix(1.0, skin_specular_scale, effects.r);
	SSS_STRENGTH = effects.r * skin_scatter_strength;
	CLEARCOAT = effects.g;
	CLEARCOAT_ROUGHNESS = effects.b;
	vec3 emission = texture(emission_texture, sample_uv).rgb;
	float emissive_facing = mix(facing, 1.0 - facing, emissive_fresnel_inverted);
	emission *= mix(1.0, pow(emissive_facing, emissive_fresnel_power), emissive_fresnel_enabled);
	vec3 hair_emission = texture(hair_emission_texture, sample_uv).rgb;
	EMISSION = emission + max(hair_emission - texture(emission_texture, sample_uv).rgb, vec3(0.0)) * hair_fresnel;
	// The selected source disables anisotropic BSDF shading. Its tangent/strand
	// inputs still shape the source normal and roughness maps extracted above.
}

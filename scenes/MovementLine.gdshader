shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform vec4 shine_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float shine_speed = 0.8;
uniform float shine_width = 0.12;

void fragment() {
	float head = fract(TIME * shine_speed);
	float distance_to_head = min(abs(UV.x - head), 1.0 - abs(UV.x - head));
	float shine = 1.0 - smoothstep(0.0, shine_width, distance_to_head);

	ALBEDO = shine_color.rgb;
	ALPHA = shine * shine_color.a;
}

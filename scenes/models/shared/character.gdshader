shader_type spatial;
render_mode cull_disabled, ambient_light_disabled;

// Source colors and image masks are retained. EEVEE's Shader-to-RGB lighting
// is translated to Godot per-light shading; this is not a byte-exact renderer port.
uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap_anisotropic;
uniform vec4 base_color : source_color = vec4(1.0);
uniform vec4 shadow_tint : source_color = vec4(0.193595, 0.195101, 0.300677, 1.0);
uniform float fill_strength : hint_range(0.0, 1.0) = 0.15;
uniform float shadow_softness : hint_range(0.001, 0.5) = 0.035;
uniform vec3 diffuse_ramp = vec3(0.08181819,0.16136391,0.17727327);
uniform bool use_packed_maps = false;
// These images are sRGB in the original Blender file, including the N image.
uniform sampler2D shading_map : source_color, hint_default_white, filter_linear_mipmap_anisotropic;
uniform sampler2D metal_mask : source_color, hint_default_black, filter_linear_mipmap_anisotropic;
uniform sampler2D spec_mask : source_color, hint_default_black, filter_linear_mipmap_anisotropic;
uniform sampler2D metal_matcap : source_color, hint_default_white, filter_linear_mipmap;
uniform float metal_amount : hint_range(0.0, 1.0) = 0.7;
uniform float metal_contrast = 1.0;
uniform bool normal_specular = false;
uniform float normal_strength : hint_range(0.0,1.0) = 0.24;
uniform float metal_saturation = 2.0;
uniform float metal_value = 1.8;
uniform float highlight_saturation = 1.2;
uniform float highlight_value = 1.7;
uniform float specular_strength : hint_range(0.0, 2.0) = 0.0;
uniform float highlight_roughness : hint_range(0.05, 1.0) = 0.283333;
uniform float highlight_transition : hint_range(0.0, 1.0) = 0.35;
uniform float rim_strength : hint_range(0.0, 1.0) = 0.18;
uniform sampler2D rim_map : source_color, hint_default_black, filter_linear_mipmap;
uniform int rim_count = 0;
uniform vec4 rim_color_0 : source_color = vec4(0.0);
uniform vec4 rim_color_1 : source_color = vec4(0.0);
uniform vec4 rim_color_2 : source_color = vec4(0.0);
// angle in radians, thickness, width (same units as the Blender group).
uniform vec3 rim_settings_0 = vec3(0.0);
uniform vec3 rim_settings_1 = vec3(0.0);
uniform vec3 rim_settings_2 = vec3(0.0);
varying vec3 glossy_normal;
varying vec3 packed_values;
varying vec3 highlight_color;

// Compatibility samples color textures in sRGB, whereas Forward+/Mobile use
// linear samples. Source node masks and HSV math must operate in linear space.
vec3 to_linear(vec3 c) {
    if (!OUTPUT_IS_SRGB) return c;
    return mix(c/12.92,pow((c+vec3(0.055))/1.055,vec3(2.4)),step(vec3(0.04045),c));
}
vec3 to_output(vec3 c) {
    c=max(c,vec3(0.0));
    if (!OUTPUT_IS_SRGB) return c;
    return mix(c*12.92,1.055*pow(c,vec3(1.0/2.4))-vec3(0.055),step(vec3(0.0031308),c));
}
vec3 adjust_sv(vec3 c,float saturation,float value) {
    // Source hue is neutral (0.5). Scaling HSV saturation around max RGB
    // reproduces the source HSV nodes without hue conversion singularities.
    float maximum=max(c.r,max(c.g,c.b));
    return max(vec3(0.0),mix(vec3(maximum),c,saturation))*value;
}
float source_diffuse(float value) {
    float low=0.5*clamp((value-diffuse_ramp.x)/(diffuse_ramp.y-diffuse_ramp.x),0.0,1.0);
    float high=0.5*clamp((value-diffuse_ramp.y)/(diffuse_ramp.z-diffuse_ramp.y),0.0,1.0);
    return low+high;
}

float gray(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
float source_rim(vec3 n, vec3 v, vec3 settings) {
    float c = cos(settings.x); float s = sin(settings.x);
    vec2 uv = mat2(vec2(c,s), vec2(-s,c)) * n.xy * 0.49 + vec2(0.5);
    uv.y = 1.0 - uv.y; // Blender image coordinates to Godot image coordinates.
    float cap = gray(to_linear(texture(rim_map, uv).rgb));
    float facing = clamp(abs(dot(n, v)), 0.0001, 1.0);
    // Exact dielectric Fresnel for the source IOR 2.0.
    float g = sqrt(4.0 - 1.0 + facing * facing);
    float a = (g-facing)/(g+facing);
    float b = (facing*(g+facing)-1.0)/(facing*(g-facing)+1.0);
    float fresnel = 0.5*a*a*(1.0+b*b);
    float mask = pow(max(cap + settings.z*0.3-0.3, 0.0), max(1.0-settings.y*0.9, 0.01))*fresnel;
    float aa=max(fwidth(mask),0.002);
    return smoothstep(0.1-aa, 0.1+aa, mask);
}
void fragment() {
    vec3 n = normalize(NORMAL);
    vec3 base = to_linear(texture(albedo_texture, UV).rgb)*to_linear(base_color.rgb);
    ALBEDO = to_output(base);
    ROUGHNESS = highlight_roughness;
    SPECULAR = 0.0;
    packed_values = vec3(0.8, 0.0, 1.0);
    glossy_normal = n;
    if (use_packed_maps) {
        vec3 packed_n = to_linear(texture(shading_map, UV).rgb);
        packed_values = vec3(gray(packed_n), to_linear(texture(metal_mask,UV).rgb).b,
            clamp((to_linear(texture(spec_mask,UV).rgb).b-0.01363636)/0.07727314,0.0,1.0));
        vec2 cap_uv = n.xy*0.49+vec2(0.5); cap_uv.y=1.0-cap_uv.y;
        float metal = pow(max(gray(to_linear(texture(metal_matcap,cap_uv).rgb)),0.00001),metal_contrast+0.5);
        vec3 metallic_color=adjust_sv(base,metal_saturation,metal_value)*mix(1.0,metal,metal_amount);
        ALBEDO=to_output(mix(base,metallic_color,packed_values.y));
        if (normal_specular) {
            vec3 nt = packed_n*2.0-1.0;
            nt=normalize(mix(vec3(0.0,0.0,1.0),nt,normal_strength));
            glossy_normal = normalize(TANGENT*nt.x + BINORMAL*nt.y + n*nt.z);
        }
    }
    highlight_color=to_output(mix(adjust_sv(base,highlight_saturation,highlight_value),vec3(1.0),packed_values.y));
    EMISSION = to_output(to_linear(ALBEDO) * to_linear(shadow_tint.rgb) * fill_strength);
    vec3 rim = vec3(0.0);
    if (rim_count>0) rim += to_linear(rim_color_0.rgb)*source_rim(n,VIEW,rim_settings_0);
    if (rim_count>1) rim += to_linear(rim_color_1.rgb)*source_rim(n,VIEW,rim_settings_1);
    if (rim_count>2) rim += to_linear(rim_color_2.rgb)*source_rim(n,VIEW,rim_settings_2);
    EMISSION = to_output(to_linear(EMISSION)+rim*rim_strength);
}
void light() {
    float ndl = max(dot(NORMAL,LIGHT),0.0);
    // Blender's diffuse BSDF -> Shader-to-RGB supplies irradiance / PI.
    // Godot LIGHT_COLOR already includes PI; undo it before that conversion.
    float irradiance = gray(LIGHT_COLOR)/PI;
    float diffuse_input = ndl*packed_values.x*irradiance/PI;
    float shade = use_packed_maps ? source_diffuse(diffuse_input) : smoothstep(0.16-shadow_softness,0.16+shadow_softness,ndl*packed_values.x);
    vec3 palette_light=LIGHT_COLOR/PI;
    DIFFUSE_LIGHT += to_output(mix(to_linear(shadow_tint.rgb),vec3(1.0),shade))*ATTENUATION*palette_light;
    float h=pow(max(dot(glossy_normal,normalize(LIGHT+VIEW)),0.0),max(2.0,2.0/(highlight_roughness*highlight_roughness)-2.0));
    float aa=max(fwidth(h),0.015);
    float highlight=mix(clamp((h-0.1)/0.9,0.0,1.0),smoothstep(0.5-aa,0.5+aa,h),highlight_transition);
    // The source metallic branch retains 0.216 of its highlight in shadow.
    SPECULAR_LIGHT += highlight_color*highlight*specular_strength*packed_values.z*max(0.216074,shade)*ATTENUATION*LIGHT_COLOR/PI;
}

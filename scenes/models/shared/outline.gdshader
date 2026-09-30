shader_type spatial;
render_mode unshaded, cull_front, shadows_disabled;
uniform bool enabled = true;
uniform float width_scale : hint_range(0.0, 3.0) = 1.0;
uniform float source_width = 0.001;
uniform vec4 line_color : source_color = vec4(0.0,0.0,0.0,1.0);
uniform bool distance_scaled = false;
uniform bool use_vertex_weights = false;
uniform sampler2D vertex_weights : filter_nearest, repeat_disable;
varying float outline_weight;
void vertex() {
    float weight=1.0;
    if (use_vertex_weights) weight=texelFetch(vertex_weights,ivec2(VERTEX_ID%256,VERTEX_ID/256),0).r;
    // The source Geometry Nodes group uses Width * Group * atan(distance to
    // its unassigned reference object, i.e. the object's local origin).
    float scale=distance_scaled ? atan(length(VERTEX)) : 1.0;
    VERTEX+=normalize(NORMAL)*source_width*width_scale*weight*scale;
    outline_weight=weight;
}
void fragment() {
    if (!enabled || width_scale<=0.0 || outline_weight<=0.00001) discard;
    ALBEDO=line_color.rgb;
}

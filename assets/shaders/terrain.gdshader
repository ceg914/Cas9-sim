// 지형 표면. 생물군계 색과 식생량 텍스처를 섞어 '식생이 실제로 줄고 느는 모습'을 보여 준다.
shader_type spatial;

uniform sampler2D lush_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D bare_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D veg_tex : filter_linear, repeat_disable;
uniform float water_level = 0.0;
uniform float cell_count = 256.0;
uniform vec3 highlight_center = vec3(0.0);
uniform float highlight_radius = 0.0;
uniform vec4 highlight_color : source_color = vec4(1.0, 0.9, 0.3, 1.0);

varying vec3 world_pos;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float veg = texture(veg_tex, UV).r;
	vec3 lush = texture(lush_tex, UV).rgb;
	vec3 bare = texture(bare_tex, UV).rgb;
	vec3 col = mix(bare, lush, smoothstep(0.04, 0.8, veg));

	// 셀보다 잔 무늬를 조금 넣어 평평한 색면을 피한다.
	float grain = hash21(floor(UV * cell_count * 4.0));
	col *= 0.93 + 0.14 * grain;

	// 물에 잠긴 바닥은 어둡게.
	float depth = clamp((water_level - world_pos.y) / 1.6, 0.0, 1.0);
	col = mix(col, vec3(0.10, 0.17, 0.19), depth * 0.85);

	// 배치·교란 도구의 범위 표시.
	if (highlight_radius > 0.0) {
		float d = distance(world_pos.xz, highlight_center.xz);
		float ring = smoothstep(highlight_radius, highlight_radius - 0.6, d) * smoothstep(highlight_radius - 2.2, highlight_radius - 1.0, d);
		float fill = smoothstep(highlight_radius, highlight_radius - 0.6, d) * 0.12;
		col = mix(col, highlight_color.rgb, clamp(ring * 0.9 + fill, 0.0, 1.0));
	}

	ALBEDO = col;
	ROUGHNESS = 0.95;
	SPECULAR = 0.1;
}

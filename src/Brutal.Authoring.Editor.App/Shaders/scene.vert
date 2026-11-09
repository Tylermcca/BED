#version 450
layout(location=0) out vec3 linearColor;
layout(location=1) out vec2 uv;
layout(location=2) flat out float mode;
layout(push_constant) uniform Triangle { vec4 positions[3]; vec4 colors[3]; vec4 uvAB; vec4 uvCMode; } triangle;
void main(){int i=gl_VertexIndex;gl_Position=triangle.positions[i];linearColor=triangle.colors[i].rgb;
uv=i==0?triangle.uvAB.xy:(i==1?triangle.uvAB.zw:triangle.uvCMode.xy);mode=triangle.uvCMode.z;}

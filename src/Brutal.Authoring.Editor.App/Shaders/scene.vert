#version 450
layout(location=0) in vec3 position;
layout(location=1) in vec2 uv0;
layout(location=0) out vec3 linearColor;
layout(location=1) out vec2 uv;
layout(location=2) flat out float mode;
layout(push_constant) uniform Mesh { mat4 matrix; vec4 color; vec4 flags; } mesh;
void main(){
    if(mesh.flags.z<0){gl_Position=vec4(position,1);linearColor=gl_VertexIndex==0?vec3(1,0,0):(gl_VertexIndex==1?vec3(0,1,0):vec3(0,0,1));mode=0;}
    else{gl_Position=mesh.matrix*vec4(position,1);gl_Position.y=-gl_Position.y;linearColor=mesh.color.rgb;mode=mesh.flags.z;}
    uv=uv0;
}

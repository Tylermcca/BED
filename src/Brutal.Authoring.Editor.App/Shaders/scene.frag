#version 450
layout(location=0) in vec3 linearColor;
layout(location=1) in vec2 uv;
layout(location=2) flat in float mode;
layout(location=0) out vec4 displayColor;
vec3 encode(vec3 c){c=clamp(c,0,1);return mix(12.92*c,1.055*pow(c,vec3(1.0/2.4))-0.055,greaterThan(c,vec3(0.0031308)));}
void main(){vec3 c=linearColor;if(mode>1.5)c=mod(floor(gl_FragCoord.x/8)+floor(gl_FragCoord.y/8),2)<1?vec3(1,0,1):vec3(0.04);
displayColor=vec4(encode(c),1);}

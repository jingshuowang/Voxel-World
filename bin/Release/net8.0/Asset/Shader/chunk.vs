#version 460 core
layout(location=0) in uint d;
layout(location=1) in vec2 p;
layout(std430, binding=0) readonly buffer P { vec4 o[]; };
out vec3 c, n, vP;
out float dpt, dst;
out vec2 uv;
uniform mat4 vM, pM;
uniform float day;
const vec3 N[6] = vec3[](vec3(0,0,1),vec3(0,0,-1),vec3(0,1,0),vec3(0,-1,0),vec3(1,0,0),vec3(-1,0,0));
void main() {
    uint pf = d & 0x1FFFu;
    float x = float(pf % 17u), y = float((pf / 17u) % 17u), z = float(pf / 289u);
    uint t = (d >> 13) & 0x7Fu;
    uint f = uint(o[gl_DrawID].w);
    vec3 lp;
    if(f==0u) lp=vec3(x+p.x, y+p.y, z+1.); else if(f==1u) lp=vec3(x+p.x, y+p.y, z);
    else if(f==2u) lp=vec3(x+p.x, y+1., z+p.y); else if(f==3u) lp=vec3(x+p.x, y, z+p.y);
    else if(f==4u) lp=vec3(x+1., y+p.x, z+p.y); else lp=vec3(x, y+p.x, z+p.y);
    vec3 wp = lp + o[gl_DrawID].xyz;
    n = N[f];
    c = vec3(0.8);
    vP = wp;
    vec4 vp = vM * vec4(wp, 1.);
    gl_Position = pM * vp;
    dpt = gl_Position.z / gl_Position.w;
    dst = length(vp.xyz);
    uv = p;
}

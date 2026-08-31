// ============================================================================
//  syncmaster3_accurate.glsl  -  Faithful Samsung SyncMaster 3 emulation
//  Single-pass shader for 86Box (OpenGL 3.0 Core, v5.0+ / 7.0 master)
//
//  Target: Intel UHD Graphics 730 (i3-14100, Xe-LP, 24 EU) @ 2048x1536 native.
//  There is plenty of GPU headroom here, so this trades the "fast" cuts back
//  for physical accuracy:
//
//    * REAL linear light  - input is linearized (CRT gamma), all beam/mask/glow
//                           math happens in linear, then re-encoded. Correct
//                           gradients and correct scanline energy.
//    * 4-tap Gaussian beam - models the electron-beam spot; width grows with
//                           luminance (real scanline "bloom" / gap fill).
//    * Dot-trio shadow mask - anti-aliased RGB phosphor dots on a staggered
//                           triad grid. This is THE SyncMaster 3 signature
//                           (shadow-mask tube, ~0.28mm), not aperture grille.
//    * Halation glow      - cheap single-pass phosphor glow (8-tap ring).
//    * Sharp horizontal   - PC monitors were sharp; sharp-bilinear, no mush.
//
//  For TRUE multi-pass halation, this is the single-pass approximation; a
//  proper .glslp (linearize -> blur H -> blur V -> combine) goes further.
// ============================================================================

#pragma parameter GAMMA_IN      "CRT gamma (linearize input)"             2.40  1.8  2.8  0.05
#pragma parameter GAMMA_OUT     "Panel gamma (encode output)"             2.20  1.8  2.8  0.05
#pragma parameter BRIGHT_BOOST  "Brightness compensation"                 1.25  1.0  2.0  0.01
#pragma parameter SCAN_WIDTH    "Scanline beam sigma (source lines)"      0.32  0.15 0.80 0.01
#pragma parameter SCAN_BLOOM    "Beam bloom on bright areas"              0.55  0.0  2.0  0.01
#pragma parameter SCAN_MAX      "Fade scanlines above this many lines"    600.0 0.0  9999.0 1.0
#pragma parameter SHARP_H       "Horizontal sharpness"                    1.00  0.30 2.0  0.01
#pragma parameter MASK_SIZE     "Phosphor dot spacing (output px)"        3.0   1.0  8.0  1.0
#pragma parameter MASK_ASPECT   "Dot row height (x triad width)"          1.0   0.5  2.0  0.05
#pragma parameter MASK_STRENGTH "Shadow-mask strength"                    0.40  0.0  1.0  0.01
#pragma parameter MASK_AA       "Dot edge softness"                       0.55  0.05 1.0  0.01
#pragma parameter GLOW          "Halation glow amount"                    0.10  0.0  0.5  0.01
#pragma parameter GLOW_RADIUS   "Glow radius (source texels)"             2.0   0.5  6.0  0.1
#pragma parameter CURVATURE     "Curvature (SyncMaster is fairly flat)"   0.03  0.0  0.30 0.01
#pragma parameter CORNER_VIGN   "Corner vignette"                         0.12  0.0  1.0  0.01

#ifdef GL_ES
precision mediump float;
#endif

uniform float GAMMA_IN;
uniform float GAMMA_OUT;
uniform float BRIGHT_BOOST;
uniform float SCAN_WIDTH;
uniform float SCAN_BLOOM;
uniform float SCAN_MAX;
uniform float SHARP_H;
uniform float MASK_SIZE;
uniform float MASK_ASPECT;
uniform float MASK_STRENGTH;
uniform float MASK_AA;
uniform float GLOW;
uniform float GLOW_RADIUS;
uniform float CURVATURE;
uniform float CORNER_VIGN;

// ----------------------------------------------------------------------------
#if defined(VERTEX)

#if __VERSION__ >= 130
#define COMPAT_VARYING out
#define COMPAT_ATTRIBUTE in
#else
#define COMPAT_VARYING varying
#define COMPAT_ATTRIBUTE attribute
#endif

uniform mat4 MVPMatrix;
COMPAT_ATTRIBUTE vec4 VertexCoord;
COMPAT_ATTRIBUTE vec4 TexCoord;
COMPAT_VARYING vec4 TEX0;

void main()
{
    gl_Position = MVPMatrix * VertexCoord;
    TEX0.xy = TexCoord.xy;
}

// ----------------------------------------------------------------------------
#elif defined(FRAGMENT)

#if __VERSION__ >= 130
#define COMPAT_VARYING in
#define COMPAT_TEXTURE texture
out vec4 FragColor;
#else
#define COMPAT_VARYING varying
#define COMPAT_TEXTURE texture2D
#define FragColor gl_FragColor
#endif

#ifdef GL_ES
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#endif
#endif

uniform vec2 InputSize;
uniform vec2 TextureSize;
uniform vec2 OutputSize;
uniform sampler2D Texture;
COMPAT_VARYING vec4 TEX0;

const vec3 LUMA = vec3(0.299, 0.587, 0.114);

vec2 g_imgScale;   // InputSize / TextureSize

// Raw texture fetch over the active image (handles padded textures).
vec3 texRaw(vec2 uv)
{
    return COMPAT_TEXTURE(Texture, clamp(uv, 0.0, 1.0) * g_imgScale).rgb;
}

// Gaussian beam profile.
float gauss(float d, float sigma)
{
    return exp(-(d * d) / (2.0 * sigma * sigma));
}

// Sharp-bilinear horizontal coordinate: crisp edges, no blockiness.
float sharpenX(float uvx)
{
    float scale = max((OutputSize.x / InputSize.x) * SHARP_H, 1.0);
    float tx  = uvx * InputSize.x;
    float txf = floor(tx);
    float s   = fract(tx);
    float region = max(0.5 - 0.5 / scale, 0.0);
    float cd = s - 0.5;
    float f  = (cd - clamp(cd, -region, region)) * scale + 0.5;
    return (txf + f) / InputSize.x;
}

// Anti-aliased dot-trio shadow mask, locked to physical output pixels.
vec3 shadowMask(vec2 fc)
{
    float sub    = MASK_SIZE;
    float triadW = 3.0 * sub;
    float rowH   = triadW * MASK_ASPECT;

    // staggered rows -> shadow-mask dot lattice (not vertical grille lines)
    float row     = floor(fc.y / rowH);
    float stagger = mod(row, 2.0) * 1.5 * sub;

    float x = mod(fc.x + stagger, triadW);
    float y = mod(fc.y, rowH);

    // vertical dot falloff (soft, AA)
    float ry = abs(y - rowH * 0.5) / (rowH * 0.5);
    float vy = 1.0 - smoothstep(1.0 - MASK_AA, 1.0, ry);

    // per-channel horizontal dot falloff, centers at 0.5/1.5/2.5 * sub
    float hw = 0.5 * sub;
    float aa0 = hw * (1.0 - MASK_AA);
    vec3 dots;
    dots.r = 1.0 - smoothstep(aa0, hw, abs(x - 0.5 * sub));
    dots.g = 1.0 - smoothstep(aa0, hw, abs(x - 1.5 * sub));
    dots.b = 1.0 - smoothstep(aa0, hw, abs(x - 2.5 * sub));
    dots *= vy;

    return mix(vec3(1.0), dots, MASK_STRENGTH);
}

void main()
{
    g_imgScale = InputSize / TextureSize;
    vec2 uv = TEX0.xy / g_imgScale;

    // --- Curvature + vignette ----------------------------------------------
    vec2 c = uv * 2.0 - 1.0;
    if (CURVATURE > 0.001) { c += c * (c.yx * c.yx) * CURVATURE; }
    uv = c * 0.5 + 0.5;

    vec2 inXY = step(0.0, uv) * step(0.0, 1.0 - uv);
    float inside = inXY.x * inXY.y;
    float vign = 1.0 - CORNER_VIGN * dot(c * c, c * c);

    // --- Sharp horizontal position -----------------------------------------
    float sx = sharpenX(uv.x);
    float ty = uv.y * InputSize.y;

    // --- 4-tap vertical Gaussian beam, in LINEAR light ---------------------
    float base = floor(ty - 0.5) + 0.5;   // nearest lower scanline center
    vec3  acc = vec3(0.0);
    float wsum = 0.0;

    for (int i = -1; i <= 2; i++)
    {
        float ly = base + float(i);
        vec3 texel = texRaw(vec2(sx, ly / InputSize.y));
        vec3 lin   = pow(texel, vec3(GAMMA_IN));           // linearize
        float lum  = dot(lin, LUMA);
        float sigma = SCAN_WIDTH * (1.0 + SCAN_BLOOM * lum);
        float w = gauss(ty - ly, sigma);
        acc  += lin * w;
        wsum += w;
    }

    // scanlines = un-normalized sum; flat = normalized (no darkening)
    vec3 scanned = acc;
    vec3 flatCol = acc / max(wsum, 1e-4);
    float scanFactor = 1.0 - smoothstep(SCAN_MAX * 0.9, SCAN_MAX, InputSize.y);
    vec3 col = mix(flatCol, scanned, scanFactor);

    // --- Halation glow (8-tap ring, linear) --------------------------------
    if (GLOW > 0.0)
    {
        vec2 r = GLOW_RADIUS / InputSize;
        vec3 blur =
              texRaw(uv + vec2( r.x, 0.0)) + texRaw(uv + vec2(-r.x, 0.0))
            + texRaw(uv + vec2(0.0,  r.y)) + texRaw(uv + vec2(0.0, -r.y))
            + texRaw(uv + vec2( r.x,  r.y) * 0.7) + texRaw(uv + vec2(-r.x,  r.y) * 0.7)
            + texRaw(uv + vec2( r.x, -r.y) * 0.7) + texRaw(uv + vec2(-r.x, -r.y) * 0.7);
        col += GLOW * pow(blur * 0.125, vec3(GAMMA_IN));
    }

    // --- Mask, brightness, vignette ----------------------------------------
    col *= shadowMask(gl_FragCoord.xy);
    col *= BRIGHT_BOOST * vign;

    // --- Encode to panel gamma ---------------------------------------------
    col = pow(clamp(col, 0.0, 1.0), vec3(1.0 / GAMMA_OUT));

    FragColor = vec4(col * inside, 1.0);
}

#endif

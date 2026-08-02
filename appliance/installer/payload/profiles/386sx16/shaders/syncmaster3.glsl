// ============================================================================
//  syncmaster3_fast.glsl  -  FAST SyncMaster 3 shadow-mask CRT for 86Box
//
//  Same look as syncmaster3.glsl, but stripped for Intel HD Graphics (Ironlake,
//  Gen5, 12 EU). Every transcendental is gone:
//    - NO pow(): works directly in gamma space (no linearize/encode).
//    - NO exp(): scanline beam is a squared-Lorentzian (one divide).
//  Still 2 vertical taps + sharp-bilinear X + shadow mask.
//
//  If this STILL doesn't reach 100%, the wall is pixel count at 2048x1536,
//  not the shader. See notes: lower the panel mode and let the board upscale.
//
//  Extra perf knobs, in order of impact:
//    A) CURVATURE 0.0  (default here) - avoids scattered texture reads.
//    B) Set ONE_TAP below to 1 - halves texture fetches, softer scanlines.
//    C) MASK_STRENGTH 0.0 - removes the mask math entirely if you must.
// ============================================================================

#define ONE_TAP 0   // 0 = two vertical taps (nicer). 1 = single tap (cheaper).

#pragma parameter CURVATURE     "Curvature"                               0.0   0.0  0.40 0.01
#pragma parameter CORNER_VIGN   "Corner vignette"                         0.12  0.0  1.0  0.01
#pragma parameter SCAN_WIDTH    "Scanline beam width"                     0.35  0.10 0.90 0.01
#pragma parameter SCAN_BLOOM    "Beam bloom on bright areas"              0.55  0.0  2.0  0.01
#pragma parameter SCAN_MAX      "Fade scanlines above this many lines"    600.0 0.0  9999.0 1.0
#pragma parameter SHARP_H       "Horizontal sharpness"                    1.00  0.30 2.0  0.01
#pragma parameter MASK_SIZE     "Mask subpixel width (output px)"         2.0   1.0  6.0  1.0
#pragma parameter SLOT_HEIGHT   "Slot height (in triads)"                 1.0   0.5  4.0  0.5
#pragma parameter MASK_STRENGTH "Shadow-mask strength"                    0.30  0.0  1.0  0.01
#pragma parameter BRIGHT_BOOST  "Brightness compensation"                 1.30  1.0  2.0  0.01

#ifdef GL_ES
precision mediump float;
#endif

uniform float CURVATURE;
uniform float CORNER_VIGN;
uniform float SCAN_WIDTH;
uniform float SCAN_BLOOM;
uniform float SCAN_MAX;
uniform float SHARP_H;
uniform float MASK_SIZE;
uniform float SLOT_HEIGHT;
uniform float MASK_STRENGTH;
uniform float BRIGHT_BOOST;

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

// Squared-Lorentzian beam: smooth tails like a Gaussian, but just one divide.
float beam(float dist, float lum)
{
    float w = SCAN_WIDTH * (1.0 + SCAN_BLOOM * lum);
    float x = dist / w;
    float b = 1.0 / (1.0 + x * x);
    return b * b;
}

// Sharp-bilinear on X only. No transcendentals.
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

vec3 shadowMask(vec2 fc)
{
    float sub   = MASK_SIZE;
    float triad = 3.0 * sub;
    float slotH = SLOT_HEIGHT * triad;

    float rowPhase = mod(floor(fc.y / slotH), 2.0);
    float xoff = rowPhase * 1.5 * sub;

    float xi = mod(fc.x + xoff, triad) / sub;
    vec3 m;
    m.r = step(xi, 1.0);
    m.g = step(1.0, xi) * step(xi, 2.0);
    m.b = step(2.0, xi);

    float gy = mod(fc.y, slotH);
    float slotLine = 1.0 - 0.35 * MASK_STRENGTH * step(slotH - 1.0, gy);

    return mix(vec3(1.0), m, MASK_STRENGTH) * slotLine;
}

void main()
{
    vec2 imgScale = InputSize / TextureSize;
    vec2 uv = TEX0.xy / imgScale;

    vec2 c = uv * 2.0 - 1.0;
    if (CURVATURE > 0.001) { c += c * (c.yx * c.yx) * CURVATURE; }
    uv = c * 0.5 + 0.5;

    vec2 inXY = step(0.0, uv) * step(0.0, 1.0 - uv);
    float inside = inXY.x * inXY.y;
    float vign = 1.0 - CORNER_VIGN * dot(c * c, c * c);

    float sx = sharpenX(uv.x);
    float ty = uv.y * InputSize.y;

#if ONE_TAP
    float cy = floor(ty) + 0.5;
    vec3 s0 = COMPAT_TEXTURE(Texture, vec2(sx, cy / InputSize.y) * imgScale).rgb;
    float w0 = beam(ty - cy, dot(s0, LUMA));
    vec3 scanned = s0 * w0;
    vec3 flatCol = s0;
#else
    float c0 = floor(ty - 0.5) + 0.5;
    float c1 = c0 + 1.0;
    vec3 s0 = COMPAT_TEXTURE(Texture, vec2(sx, c0 / InputSize.y) * imgScale).rgb;
    vec3 s1 = COMPAT_TEXTURE(Texture, vec2(sx, c1 / InputSize.y) * imgScale).rgb;
    float w0 = beam(ty - c0, dot(s0, LUMA));
    float w1 = beam(ty - c1, dot(s1, LUMA));
    vec3 scanned = s0 * w0 + s1 * w1;
    vec3 flatCol = (s0 + s1) * 0.5;
#endif

    float scanFactor = 1.0 - smoothstep(SCAN_MAX * 0.9, SCAN_MAX, InputSize.y);
    vec3 col = mix(flatCol, scanned, scanFactor);

    col *= shadowMask(gl_FragCoord.xy);
    col *= BRIGHT_BOOST * vign;

    FragColor = vec4(clamp(col, 0.0, 1.0) * inside, 1.0);
}

#endif

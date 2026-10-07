namespace ScreenRecorderApp.Services.Capture.Gpu;

/// <summary>
/// The HLSL for every pass <see cref="GpuFrameProcessor"/> runs, kept as source and compiled at
/// startup rather than precompiled into bytecode at build time.
/// </summary>
/// <remarks>
/// Runtime compilation costs a few milliseconds once per session and keeps the build free of an fxc
/// dependency and the shader blobs free of version skew against the C# that feeds them. d3dcompiler_47
/// ships with every supported Windows build, and a compile failure is handled the same way as any
/// other GPU-setup failure: the CPU path takes over (see <see cref="GpuFrameProcessor.TryCreate"/>).
///
/// Every pass works in straight, non-premultiplied BGRA at 8 bits, matching what the CPU kernels these
/// replace produced, so the two implementations can be held against each other pixel for pixel.
/// </remarks>
internal static class FrameShaders
{
    /// <summary>
    /// Fullscreen triangle, generated from the vertex id — no vertex or index buffer to bind, and one
    /// fewer triangle than a quad, which also avoids the diagonal seam a two-triangle quad can show
    /// under some interpolation.
    /// </summary>
    public const string FullscreenVs = """
struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

VSOut main(uint id : SV_VertexID)
{
    VSOut o;
    o.uv  = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv.x * 2.0 - 1.0, 1.0 - o.uv.y * 2.0, 0.0, 1.0);
    return o;
}
""";

    /// <summary>
    /// Catmull-Rom (B=0, C=0.5) bicubic resample of a crop rectangle onto the whole target — the GPU
    /// counterpart of VideoCaptureService.ResampleCatmullRomInto, and deliberately the same kernel
    /// rather than the hardware bilinear a texture sampler would give for free: bilinear's positive-only
    /// weights soften edges, which is exactly what makes zoomed text look blurry.
    /// </summary>
    public const string ZoomPs = """
Texture2D<float4> Source : register(t0);
SamplerState      Point  : register(s0);

cbuffer ZoomCb : register(b0)
{
    float2 SrcSize;      // source texture size in pixels
    float2 CropOrigin;   // top-left of the crop rect, in source pixels
    float2 CropSize;     // crop rect size, in source pixels
    float2 DstSize;      // render target size in pixels
};

float4 Weights(float t)
{
    // Catmull-Rom evaluated for the four taps at once: distances t+1, t, t-1, t-2.
    float t2 = t * t;
    float t3 = t2 * t;
    return float4(
        -0.5 * t3 +       t2 - 0.5 * t,
         1.5 * t3 - 2.5 * t2            + 1.0,
        -1.5 * t3 + 2.0 * t2 + 0.5 * t,
         0.5 * t3 - 0.5 * t2);
}

float4 Tap(float2 texel)
{
    return Source.SampleLevel(Point, (clamp(texel, float2(0, 0), SrcSize - 1.0) + 0.5) / SrcSize, 0);
}

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    // Map this output pixel back to a source coordinate. The CPU kernel indexes source pixel centres
    // directly, so the same convention is used here rather than the half-texel-offset UV convention —
    // otherwise the two would disagree by half a pixel everywhere.
    float2 src = CropOrigin + (pos.xy - 0.5) * (CropSize / DstSize);
    float2 base = floor(src);
    float2 f = src - base;

    float4 wx = Weights(f.x);
    float4 wy = Weights(f.y);

    float4 acc = 0;
    [unroll] for (int j = 0; j < 4; j++)
    {
        float4 row = 0;
        [unroll] for (int i = 0; i < 4; i++)
        {
            row += Tap(base + float2(i - 1, j - 1)) * wx[i];
        }
        acc += row * wy[j];
    }
    return float4(saturate(acc.rgb), 1.0);
}
""";

    /// <summary>
    /// 3x3 unsharp mask with a 1-2-1 binomial blur — the GPU counterpart of
    /// VideoCaptureService.SharpenInto, radius 1 for the same reason: anything wider puts visible bright
    /// rims around dark-on-light text.
    /// </summary>
    public const string SharpenPs = """
Texture2D<float4> Source : register(t0);
SamplerState      Point  : register(s0);

cbuffer SharpenCb : register(b0)
{
    float2 TexSize;
    float  Amount;
    float  Pad;
};

float3 Tap(float2 texel)
{
    return Source.SampleLevel(Point, (clamp(texel, float2(0, 0), TexSize - 1.0) + 0.5) / TexSize, 0).rgb;
}

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float2 p = pos.xy - 0.5;
    float3 c = Tap(p);

    float3 corners = Tap(p + float2(-1, -1)) + Tap(p + float2(1, -1))
                   + Tap(p + float2(-1,  1)) + Tap(p + float2(1,  1));
    float3 edges   = Tap(p + float2( 0, -1)) + Tap(p + float2(-1, 0))
                   + Tap(p + float2( 1,  0)) + Tap(p + float2(0,  1));
    float3 blur = (corners + edges * 2.0 + c * 4.0) / 16.0;

    return float4(saturate(c + Amount * (c - blur)), 1.0);
}
""";

    /// <summary>
    /// Click ripples and the cursor spotlight in a single full-screen pass, in that order, matching the
    /// CPU stack's z-order (a ripple outside the spotlight's clear circle gets dimmed along with
    /// everything else).
    /// </summary>
    public const string EffectsPs = """
Texture2D<float4> Source : register(t0);
SamplerState      Point  : register(s0);

#define MAX_RIPPLES 16

cbuffer EffectsCb : register(b0)
{
    float2 TexSize;
    float2 Cursor;            // spotlight centre, in pixels
    float  SpotRadius;        // sharp inner radius; <= 0 disables the spotlight
    float  SpotFeather;       // width of the linear falloff band outside the radius
    float  SpotDimAlpha;      // how much of the original brightness the dim removes
    float  RippleCount;
    float  RippleThickness;
    float3 RipplePad;
    // xy = centre in pixels, z = current radius in pixels, w = opacity
    float4 Ripples[MAX_RIPPLES];
};

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float2 p = pos.xy - 0.5;
    float3 c = Source.SampleLevel(Point, uv, 0).rgb;

    int count = (int)RippleCount;
    for (int i = 0; i < count; i++)
    {
        float4 r = Ripples[i];
        float dist = length(p - r.xy);
        float half_ = RippleThickness * 0.5;
        // A hard in/out test, matching the CPU ring exactly rather than antialiasing the edge — the two
        // have to agree for the kernel comparison to mean anything.
        if (dist >= r.z - half_ && dist <= r.z + half_)
        {
            c = float3(0.235, 0.863, 1.0) * r.w + c * (1.0 - r.w); // same cyan accent as the CPU path
        }
    }

    if (SpotRadius > 0.0)
    {
        float dist = length(p - Cursor);
        float outer = SpotRadius + SpotFeather;
        float clear = dist <= SpotRadius ? 1.0
                    : (dist >= outer ? 0.0 : (outer - dist) / SpotFeather);
        c *= (1.0 - SpotDimAlpha) + SpotDimAlpha * clear;
    }

    return float4(saturate(c), 1.0);
}
""";

    /// <summary>
    /// Vertex shader for the small composited bitmaps — cursor, webcam PiP, keystroke toast. The quad's
    /// position and size come from the constant buffer, so there is still no vertex buffer to manage;
    /// it is drawn as a 4-vertex triangle strip.
    /// </summary>
    public const string QuadVs = """
cbuffer QuadCb : register(b0)
{
    float4 Rect;       // x, y, width, height in destination pixels
    float2 TargetSize;
    float  PassMode;   // 0 = normal alpha blend, 1 = the invert-only pass
    float  Pad;
};

struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

VSOut main(uint id : SV_VertexID)
{
    float2 corner = float2(id & 1, (id >> 1) & 1);
    float2 px = Rect.xy + corner * Rect.zw;

    VSOut o;
    o.uv  = corner;
    o.pos = float4(px.x / TargetSize.x * 2.0 - 1.0, 1.0 - px.y / TargetSize.y * 2.0, 0.0, 1.0);
    return o;
}
""";

    /// <summary>
    /// Straight-alpha overlay bitmap, blended by fixed-function output merger rather than by reading the
    /// destination back — a shader cannot sample the render target it is writing to.
    /// </summary>
    /// <remarks>
    /// A handful of classic Windows cursors (the I-beam's centre line, for one) invert whatever is
    /// underneath instead of painting a colour of their own, which no single blend state can express
    /// alongside ordinary alpha. So the caller draws the quad twice: once in <c>PassMode</c> 0 with a
    /// normal alpha blend, which discards the invert pixels, and once in <c>PassMode</c> 1 with an
    /// invert blend state, which discards everything else and emits white so the blend resolves to
    /// 1 - dst. VideoCaptureService flags those pixels with a reserved alpha of 254.
    /// </remarks>
    public const string QuadPs = """
Texture2D<float4> Overlay : register(t0);
SamplerState      Point   : register(s0);

cbuffer QuadCb : register(b0)
{
    float4 Rect;
    float2 TargetSize;
    float  PassMode;
    float  Pad;
};

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float4 o = Overlay.SampleLevel(Point, uv, 0);
    bool isInvert = abs(o.a - 254.0 / 255.0) < 0.002;

    if (PassMode != 0.0)
    {
        if (!isInvert) discard;
        return float4(1.0, 1.0, 1.0, 1.0); // blend state turns this into 1 - dst
    }

    if (o.a <= 0.0 || isInvert) discard;
    return o;
}
""";

    /// <summary>
    /// BGRA to NV12, BT.709 studio swing — the compute counterpart of <see cref="Nv12Converter"/>, and
    /// the pass that means only 1.5 bytes per pixel ever cross the bus back to the CPU. One thread per
    /// chroma sample: each writes the four luma samples of its 2x2 block and the one interleaved
    /// Cb/Cr pair, which is also what keeps the 2x2 chroma average free.
    /// </summary>
    public const string Nv12Cs = """
Texture2D<float4>   Source : register(t0);
RWTexture2D<float>  LumaOut   : register(u0);
RWTexture2D<float2> ChromaOut : register(u1);

cbuffer Nv12Cb : register(b0)
{
    uint2 ChromaSize;   // source size / 2
    uint2 SourceSize;
};

// A B8G8R8A8_UNORM texture is BGRA in *memory* only: the sampler resolves the swizzle, so a shader
// reading it gets .x = red, .y = green, .z = blue like any other format. Coefficients otherwise match
// Nv12Converter exactly.
float Luma(float3 c)
{
    return (0.18258 * c.x + 0.61423 * c.y + 0.06201 * c.z) * 255.0 + 16.0;
}

[numthreads(8, 8, 1)]
void main(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= ChromaSize.x || tid.y >= ChromaSize.y) return;

    uint2 p = tid.xy * 2;
    float3 c00 = Source[p].rgb;
    float3 c01 = Source[p + uint2(1, 0)].rgb;
    float3 c10 = Source[p + uint2(0, 1)].rgb;
    float3 c11 = Source[p + uint2(1, 1)].rgb;

    LumaOut[p]                = Luma(c00) / 255.0;
    LumaOut[p + uint2(1, 0)]  = Luma(c01) / 255.0;
    LumaOut[p + uint2(0, 1)]  = Luma(c10) / 255.0;
    LumaOut[p + uint2(1, 1)]  = Luma(c11) / 255.0;

    // Average in RGB before the matrix, exactly as the CPU converter does.
    float3 avg = (c00 + c01 + c10 + c11) * 0.25 * 255.0;
    float u = -0.10064 * avg.x - 0.33857 * avg.y + 0.43922 * avg.z + 128.0;
    float v =  0.43922 * avg.x - 0.39894 * avg.y - 0.04027 * avg.z + 128.0;

    ChromaOut[tid.xy] = float2(saturate(u / 255.0), saturate(v / 255.0));
}
""";
}

namespace ScreenRecorderApp.Services.Capture;

/// <summary>
/// Converts composited BGRA frames to NV12 — the pixel format actually handed to ffmpeg.
/// </summary>
/// <remarks>
/// <para>
/// Feeding ffmpeg BGRA meant paying for the same colour conversion twice over: 4 bytes per pixel went
/// through the named pipe (500MB/s at 1080p60, 2GB/s at 4K60) and ffmpeg then ran swscale over every
/// frame to reach the yuv420p its encoders actually want. NV12 is that same 4:2:0 content in the layout
/// the encoders consume, at 1.5 bytes per pixel — so the pipe carries 62% less, and ffmpeg's conversion
/// pass disappears entirely rather than moving somewhere else.
/// </para>
/// <para>
/// The layout is a full-resolution Y plane followed by a half-resolution interleaved Cb/Cr plane, which
/// is what <see cref="FrameByteSize"/> sizes for. Chroma is averaged over each 2x2 block rather than
/// point-sampled from one corner: point sampling throws away three quarters of the colour information
/// and shows up as ragged edges on coloured text, which is exactly the content a screen recorder exists
/// to capture.
/// </para>
/// <para>
/// Coefficients are BT.709 studio swing (Y in 16..235, chroma in 16..240). That is a deliberate,
/// visible change from what the BGRA pipeline produced: swscale defaults to BT.601 for an RGB source
/// and tags the output with nothing at all, so an HD recording was converted as 601 and then decoded as
/// 709 by every player — a real if mild saturation and hue error that had been there all along.
/// FFmpegEncoderService now tags the stream bt709 to match what this produces, so the two finally
/// agree.
/// </para>
/// <para>
/// This is the CPU fallback. When the GPU pipeline is available the same conversion happens in a
/// compute shader as the last step before readback (see <c>GpuFrameProcessor</c>), so these bytes never
/// cross the bus as BGRA at all; the two implementations are held to the same reference in the kernel
/// tests.
/// </para>
/// </remarks>
public static class Nv12Converter
{
    /// <summary>Bytes one NV12 frame occupies: a full Y plane plus a half-resolution interleaved chroma plane.</summary>
    public static int FrameByteSize(int width, int height) => width * height + (width / 2) * (height / 2) * 2;

    // BT.709 limited range, in 16.16 fixed point. Integer arithmetic throughout: every input is a byte
    // and every output is a byte, so the extra range of floating point buys nothing here and costs a
    // conversion in both directions per channel.
    private const int Shift = 16;
    private const int Half = 1 << (Shift - 1);

    private const int YR = (int)(0.18258 * (1 << Shift) + 0.5);
    private const int YG = (int)(0.61423 * (1 << Shift) + 0.5);
    private const int YB = (int)(0.06201 * (1 << Shift) + 0.5);

    private const int UR = (int)(-0.10064 * (1 << Shift) - 0.5);
    private const int UG = (int)(-0.33857 * (1 << Shift) - 0.5);
    private const int UB = (int)(0.43922 * (1 << Shift) + 0.5);

    private const int VR = (int)(0.43922 * (1 << Shift) + 0.5);
    private const int VG = (int)(-0.39894 * (1 << Shift) - 0.5);
    private const int VB = (int)(-0.04027 * (1 << Shift) - 0.5);

    /// <summary>Y for black and the neutral chroma value — what an "empty" NV12 frame has to be filled with.</summary>
    public const byte BlackY = 16;
    public const byte NeutralChroma = 128;

    /// <summary>
    /// Fills <paramref name="nv12"/> with a legal black frame. Zeroing the buffer is <em>not</em>
    /// equivalent: chroma 0 is not neutral, and a zero-filled NV12 frame decodes to saturated green
    /// rather than to black.
    /// </summary>
    public static void FillBlack(byte[] nv12, int width, int height)
    {
        int lumaBytes = width * height;
        Array.Fill(nv12, BlackY, 0, lumaBytes);
        Array.Fill(nv12, NeutralChroma, lumaBytes, nv12.Length - lumaBytes);
    }

    /// <summary>
    /// Converts a tightly packed BGRA frame into <paramref name="nv12"/>, which must be at least
    /// <see cref="FrameByteSize"/> bytes. Width and height must both be even — every capture path here
    /// produces even dimensions, and 4:2:0 has no meaning otherwise.
    /// </summary>
    public static unsafe void Convert(byte[] bgra, byte[] nv12, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        int chromaWidth = width / 2;
        int chromaHeight = height / 2;
        int lumaBytes = width * height;
        int srcStride = width * 4;

        fixed (byte* srcBase = bgra)
        fixed (byte* dstBase = nv12)
        {
            byte* src = srcBase;
            byte* luma = dstBase;
            byte* chroma = dstBase + lumaBytes;

            // One band per pair of rows, because a chroma row spans two luma rows: splitting anywhere
            // else would have two workers writing the same chroma row.
            Parallel.For(0, chromaHeight, cy =>
            {
                int y0 = cy * 2;
                byte* row0 = src + (long)y0 * srcStride;
                byte* row1 = row0 + srcStride;
                byte* luma0 = luma + (long)y0 * width;
                byte* luma1 = luma0 + width;
                byte* chromaRow = chroma + (long)cy * chromaWidth * 2;

                for (int cx = 0; cx < chromaWidth; cx++)
                {
                    int x = cx * 2;
                    byte* p00 = row0 + x * 4;
                    byte* p01 = p00 + 4;
                    byte* p10 = row1 + x * 4;
                    byte* p11 = p10 + 4;

                    luma0[x] = Luma(p00);
                    luma0[x + 1] = Luma(p01);
                    luma1[x] = Luma(p10);
                    luma1[x + 1] = Luma(p11);

                    // Average the 2x2 block in RGB before converting, rather than converting four times
                    // and averaging the results — the transform is linear in the encoded values, so the
                    // two agree to within rounding, and this way the matrix runs once per chroma sample
                    // instead of four times.
                    int b = (p00[0] + p01[0] + p10[0] + p11[0] + 2) >> 2;
                    int g = (p00[1] + p01[1] + p10[1] + p11[1] + 2) >> 2;
                    int r = (p00[2] + p01[2] + p10[2] + p11[2] + 2) >> 2;

                    chromaRow[cx * 2] = Clamp8((UR * r + UG * g + UB * b + Half >> Shift) + 128);
                    chromaRow[cx * 2 + 1] = Clamp8((VR * r + VG * g + VB * b + Half >> Shift) + 128);
                }
            });
        }
    }

    private static unsafe byte Luma(byte* bgra)
        => Clamp8((YR * bgra[2] + YG * bgra[1] + YB * bgra[0] + Half >> Shift) + 16);

    private static byte Clamp8(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}

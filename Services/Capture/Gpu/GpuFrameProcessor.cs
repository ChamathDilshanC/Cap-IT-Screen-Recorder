using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ScreenRecorderApp.Services.Capture.Gpu;

/// <summary>One click ripple, already advanced to its current radius and opacity by the caller.</summary>
internal readonly record struct GpuRipple(float X, float Y, float Radius, float Opacity);

/// <summary>
/// Everything one composited frame needs. The springs, the activity gating, the ripple lifetimes and
/// the cursor lookup all stay on the CPU where they were; this carries only what the pixel work reads.
/// </summary>
internal readonly record struct GpuFrameParams(
    bool DrawCursor,
    int CursorX,
    int CursorY,
    double CropX,
    double CropY,
    double CropWidth,
    double CropHeight,
    double SharpenAmount,
    bool SpotlightEnabled,
    float SpotlightRadius,
    float SpotlightFeather,
    float SpotlightDimAlpha,
    float RippleThickness);

/// <summary>
/// Runs the whole per-frame pixel pipeline — cursor, zoom, unsharp mask, click ripples, spotlight,
/// webcam and keystroke overlays, and the final BGRA to NV12 conversion — as GPU passes, reading back
/// only the finished NV12.
/// </summary>
/// <remarks>
/// <para>
/// The captured frame is already a D3D11 texture when Desktop Duplication or WGC hands it over. The CPU
/// path downloads all four bytes per pixel of it, spends most of a frame budget resampling and
/// sharpening with vector code, composites the effects, converts to NV12 and publishes the result.
/// </para>
/// <para>
/// Two measurements shaped this design. First, the readback dominates: pulling a composited BGRA frame
/// back to system memory measured 2.8ms at 1080p and 10.2ms at 4K, 72-81% of the GPU frame time, and it
/// is transfer-bound at roughly PCIe bandwidth, so no amount of overlapping removes it — double-buffering
/// the staging texture was tried and bought nothing but a frame of latency. That is why the NV12
/// conversion happens <em>here</em>, before the readback rather than after it: 4:2:0 is 1.5 bytes per
/// pixel against BGRA's 4, so the transfer that dominates shrinks to 37% of itself. Running the kernels
/// on the GPU but still reading back BGRA would have been barely worth the code.
/// </para>
/// <para>
/// Second, with that in place the whole pipeline — cursor, zoom, sharpen, effects, NV12, readback —
/// measured 2.7ms at 720p, 5.3-6.7ms at 1080p and 22-32ms at 4K on this machine, against 8.0-8.5ms,
/// 9.4-11.3ms and 33-38ms for the CPU equivalent <em>excluding</em> the frame download the CPU path also
/// has to pay. So: comfortably inside a 60fps budget at 1080p, and still outside it at 4K. 4K60 with the
/// zoom active is not free here, it is merely closer.
/// </para>
/// <para>
/// The kernels are deliberate ports, not approximations — the same Catmull-Rom basis, the same 1-2-1
/// binomial unsharp mask, the same source-pixel-centre sampling convention, the same BT.709 matrix and
/// 2x2 chroma average as <see cref="Nv12Converter"/> — so GPU and CPU output can be, and are, held
/// against each other pixel for pixel in the kernel test project.
/// </para>
/// <para>
/// Everything here runs on the capture thread, on the device and immediate context that Desktop
/// Duplication and WGC already use: no new thread touches D3D, which is the property that keeps this
/// clear of the use-after-free class of failure this file's neighbours document at length. The live
/// preview cannot therefore read the GPU from its own thread, so <see cref="Process"/> takes an optional
/// second destination and produces the preview's BGRA copy here, only on the frames the preview actually
/// asked for. <see cref="TryCreate"/> returns null instead of throwing if anything is missing, and the
/// caller keeps the CPU path; a GPU that cannot do this means a slower recording, never a failed one.
/// </para>
/// </remarks>
internal sealed class GpuFrameProcessor : IDisposable
{
    /// <summary>Matches MAX_RIPPLES in the effects shader.</summary>
    public const int MaxRipples = 16;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly int _width;
    private readonly int _height;
    private readonly int _chromaWidth;
    private readonly int _chromaHeight;

    private readonly ID3D11VertexShader _fullscreenVs;
    private readonly ID3D11VertexShader _quadVs;
    private readonly ID3D11PixelShader _zoomPs;
    private readonly ID3D11PixelShader _sharpenPs;
    private readonly ID3D11PixelShader _effectsPs;
    private readonly ID3D11PixelShader _quadPs;
    private readonly ID3D11ComputeShader _nv12Cs;

    // The last captured frame, kept so an animation tick — where the desktop has not changed and there
    // is no new texture to be had — can still re-compose. The zoom springs, the ripples and the webcam
    // all advance on a clock, and a still desktop is exactly when they most need to keep moving.
    private readonly ID3D11Texture2D _sourceCache;
    private bool _hasCachedSource;

    private readonly ID3D11Texture2D _texA;
    private readonly ID3D11Texture2D _texB;
    private readonly ID3D11RenderTargetView _rtvA;
    private readonly ID3D11RenderTargetView _rtvB;
    private readonly ID3D11ShaderResourceView _srvA;
    private readonly ID3D11ShaderResourceView _srvB;

    private readonly ID3D11Texture2D _lumaTex;
    private readonly ID3D11Texture2D _chromaTex;
    private readonly ID3D11UnorderedAccessView _lumaUav;
    private readonly ID3D11UnorderedAccessView _chromaUav;
    private readonly ID3D11Texture2D _lumaStaging;
    private readonly ID3D11Texture2D _chromaStaging;
    private readonly ID3D11Texture2D _bgraStaging;

    private readonly ID3D11SamplerState _pointSampler;
    private readonly ID3D11BlendState _alphaBlend;
    private readonly ID3D11BlendState _invertBlend;
    private readonly ID3D11BlendState _opaque;

    private readonly ID3D11Buffer _zoomCb;
    private readonly ID3D11Buffer _sharpenCb;
    private readonly ID3D11Buffer _effectsCb;
    private readonly ID3D11Buffer _quadCb;
    private readonly ID3D11Buffer _nv12Cb;

    private readonly OverlayTexture _cursor = new();
    private readonly OverlayTexture _webcam = new();
    private readonly OverlayTexture _keystroke = new();

    private GpuFrameProcessor(ID3D11Device device, ID3D11DeviceContext context, int width, int height, Resources r)
    {
        _device = device;
        _context = context;
        _width = width;
        _height = height;
        _chromaWidth = width / 2;
        _chromaHeight = height / 2;

        _fullscreenVs = r.FullscreenVs; _quadVs = r.QuadVs;
        _zoomPs = r.ZoomPs; _sharpenPs = r.SharpenPs; _effectsPs = r.EffectsPs; _quadPs = r.QuadPs;
        _nv12Cs = r.Nv12Cs;
        _sourceCache = r.SourceCache; _texA = r.TexA; _texB = r.TexB;
        _rtvA = r.RtvA; _rtvB = r.RtvB; _srvA = r.SrvA; _srvB = r.SrvB;
        _lumaTex = r.LumaTex; _chromaTex = r.ChromaTex;
        _lumaUav = r.LumaUav; _chromaUav = r.ChromaUav;
        _lumaStaging = r.LumaStaging; _chromaStaging = r.ChromaStaging; _bgraStaging = r.BgraStaging;
        _pointSampler = r.PointSampler;
        _alphaBlend = r.AlphaBlend; _invertBlend = r.InvertBlend; _opaque = r.Opaque;
        _zoomCb = r.ZoomCb; _sharpenCb = r.SharpenCb; _effectsCb = r.EffectsCb;
        _quadCb = r.QuadCb; _nv12Cb = r.Nv12Cb;
    }

    /// <summary>Everything TryCreate builds, so the constructor is not thirty parameters wide.</summary>
    private sealed class Resources
    {
        public ID3D11VertexShader FullscreenVs = null!, QuadVs = null!;
        public ID3D11PixelShader ZoomPs = null!, SharpenPs = null!, EffectsPs = null!, QuadPs = null!;
        public ID3D11ComputeShader Nv12Cs = null!;
        public ID3D11Texture2D SourceCache = null!, TexA = null!, TexB = null!;
        public ID3D11RenderTargetView RtvA = null!, RtvB = null!;
        public ID3D11ShaderResourceView SrvA = null!, SrvB = null!;
        public ID3D11Texture2D LumaTex = null!, ChromaTex = null!;
        public ID3D11UnorderedAccessView LumaUav = null!, ChromaUav = null!;
        public ID3D11Texture2D LumaStaging = null!, ChromaStaging = null!, BgraStaging = null!;
        public ID3D11SamplerState PointSampler = null!;
        public ID3D11BlendState AlphaBlend = null!, InvertBlend = null!, Opaque = null!;
        public ID3D11Buffer ZoomCb = null!, SharpenCb = null!, EffectsCb = null!, QuadCb = null!, Nv12Cb = null!;
    }

    /// <summary>
    /// Builds the pipeline against an existing device, or returns null if the GPU, the driver or the
    /// shader compiler will not cooperate — in which case the caller keeps using the CPU kernels.
    /// </summary>
    public static GpuFrameProcessor? TryCreate(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        // Odd dimensions have no 4:2:0 representation, and every capture path here produces even ones;
        // rather than silently cropping a column, hand such a frame to the CPU path, which does not care.
        if (width <= 1 || height <= 1 || width % 2 != 0 || height % 2 != 0) return null;

        // The NV12 pass stores to typed UAVs. Feature level 11.0 guarantees that only for the R32
        // family, so the two formats this needs are checked rather than assumed — on hardware lacking
        // them the stores would silently do nothing and every recording would come out black.
        if (!SupportsTypedUavStore(device, Format.R8_UNorm)) return null;
        if (!SupportsTypedUavStore(device, Format.R8G8_UNorm)) return null;

        var owned = new List<IDisposable>();
        try
        {
            var r = new Resources();

            // Shader Model 5.0 throughout: feature level 11.0, which every GPU that can run Desktop
            // Duplication at a useful framerate has had for well over a decade.
            r.FullscreenVs = Track(owned, CreateVs(device, FrameShaders.FullscreenVs));
            r.QuadVs = Track(owned, CreateVs(device, FrameShaders.QuadVs));
            r.ZoomPs = Track(owned, CreatePs(device, FrameShaders.ZoomPs));
            r.SharpenPs = Track(owned, CreatePs(device, FrameShaders.SharpenPs));
            r.EffectsPs = Track(owned, CreatePs(device, FrameShaders.EffectsPs));
            r.QuadPs = Track(owned, CreatePs(device, FrameShaders.QuadPs));
            r.Nv12Cs = Track(owned, CreateCs(device, FrameShaders.Nv12Cs));

            var rtDesc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            };
            r.SourceCache = Track(owned, device.CreateTexture2D(rtDesc));
            r.TexA = Track(owned, device.CreateTexture2D(rtDesc));
            r.TexB = Track(owned, device.CreateTexture2D(rtDesc));
            r.RtvA = Track(owned, device.CreateRenderTargetView(r.TexA));
            r.RtvB = Track(owned, device.CreateRenderTargetView(r.TexB));
            r.SrvA = Track(owned, device.CreateShaderResourceView(r.TexA));
            r.SrvB = Track(owned, device.CreateShaderResourceView(r.TexB));

            r.LumaTex = Track(owned, device.CreateTexture2D(PlaneDesc(width, height, Format.R8_UNorm)));
            r.ChromaTex = Track(owned, device.CreateTexture2D(PlaneDesc(width / 2, height / 2, Format.R8G8_UNorm)));
            r.LumaUav = Track(owned, device.CreateUnorderedAccessView(r.LumaTex));
            r.ChromaUav = Track(owned, device.CreateUnorderedAccessView(r.ChromaTex));
            r.LumaStaging = Track(owned, device.CreateTexture2D(StagingDesc(width, height, Format.R8_UNorm)));
            r.ChromaStaging = Track(owned, device.CreateTexture2D(StagingDesc(width / 2, height / 2, Format.R8G8_UNorm)));
            r.BgraStaging = Track(owned, device.CreateTexture2D(StagingDesc(width, height, Format.B8G8R8A8_UNorm)));

            // Point sampling, never linear: the kernels do their own filtering and index exact source
            // pixels, so any hardware interpolation underneath them would be a second, unwanted filter.
            r.PointSampler = Track(owned, device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipPoint,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaxLOD = float.MaxValue,
            }));

            r.AlphaBlend = Track(owned, device.CreateBlendState(BlendDescriptionFor(Blend.SourceAlpha, Blend.InverseSourceAlpha)));
            // src * (1 - dst) with the shader emitting white gives 1 - dst: the XOR-invert a few classic
            // Windows cursors need. See FrameShaders.QuadPs.
            r.InvertBlend = Track(owned, device.CreateBlendState(BlendDescriptionFor(Blend.InverseDestinationColor, Blend.Zero)));
            var opaqueDesc = new BlendDescription();
            opaqueDesc.RenderTarget[0] = new RenderTargetBlendDescription { BlendEnable = false, RenderTargetWriteMask = ColorWriteEnable.All };
            r.Opaque = Track(owned, device.CreateBlendState(opaqueDesc));

            r.ZoomCb = Track(owned, CreateDynamicCb(device, 32));
            r.SharpenCb = Track(owned, CreateDynamicCb(device, 16));
            r.EffectsCb = Track(owned, CreateDynamicCb(device, 48 + MaxRipples * 16));
            r.QuadCb = Track(owned, CreateDynamicCb(device, 32));
            r.Nv12Cb = Track(owned, CreateDynamicCb(device, 16));

            return new GpuFrameProcessor(device, context, width, height, r);
        }
        catch
        {
            foreach (var d in owned) { try { d.Dispose(); } catch { /* best effort */ } }
            return null;
        }
    }

    private static bool SupportsTypedUavStore(ID3D11Device device, Format format)
    {
        try
        {
            return (device.CheckFormatSupport(format) & FormatSupport.TypedUnorderedAccessView) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Texture2DDescription PlaneDesc(int w, int h, Format format) => new()
    {
        Width = (uint)w,
        Height = (uint)h,
        MipLevels = 1,
        ArraySize = 1,
        Format = format,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
    };

    private static Texture2DDescription StagingDesc(int w, int h, Format format) => new()
    {
        Width = (uint)w,
        Height = (uint)h,
        MipLevels = 1,
        ArraySize = 1,
        Format = format,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Staging,
        BindFlags = BindFlags.None,
        CPUAccessFlags = CpuAccessFlags.Read,
    };

    private static T Track<T>(List<IDisposable> owned, T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    private static ID3D11VertexShader CreateVs(ID3D11Device device, string source)
    {
        using var blob = CompileOrThrow(source, "vs_5_0");
        return device.CreateVertexShader(blob.AsSpan());
    }

    private static ID3D11PixelShader CreatePs(ID3D11Device device, string source)
    {
        using var blob = CompileOrThrow(source, "ps_5_0");
        return device.CreatePixelShader(blob.AsSpan());
    }

    private static ID3D11ComputeShader CreateCs(ID3D11Device device, string source)
    {
        using var blob = CompileOrThrow(source, "cs_5_0");
        return device.CreateComputeShader(blob.AsSpan());
    }

    private static Blob CompileOrThrow(string source, string profile)
    {
        var result = Compiler.Compile(source, "main", profile, profile, out var blob, out var errors);
        using (errors)
        {
            if (result.Failure || blob is null)
            {
                throw new InvalidOperationException($"Shader compile failed ({profile}): {errors?.AsString()}");
            }
        }
        return blob;
    }

    private static BlendDescription BlendDescriptionFor(Blend source, Blend destination)
    {
        var desc = new BlendDescription();
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = source,
            DestinationBlend = destination,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.Zero,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        return desc;
    }

    private static ID3D11Buffer CreateDynamicCb(ID3D11Device device, int byteWidth) =>
        device.CreateBuffer(new BufferDescription
        {
            ByteWidth = (uint)byteWidth,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
        });

    /// <summary>
    /// A small BGRA bitmap kept resident on the GPU, re-uploaded only when the source array changes.
    /// </summary>
    /// <remarks>
    /// Identity of the backing array is the change signal, because every producer of these caches and
    /// reuses its array: the styled cursors come out of a dictionary, the live system cursor shape is
    /// only rebuilt when DXGI reports the shape changed, and the webcam and keystroke renderers hand back
    /// their own cached overlay. A reference match therefore means the pixels match, without hashing
    /// several hundred kilobytes per frame to establish it.
    /// </remarks>
    private sealed class OverlayTexture : IDisposable
    {
        public ID3D11Texture2D? Texture;
        public ID3D11ShaderResourceView? Srv;
        public byte[]? Source;
        public int Width;
        public int Height;
        public int HotspotX;
        public int HotspotY;

        public bool Ready => Srv is not null && Source is not null;

        public void Clear() => Source = null;

        public void Update(ID3D11Device device, ID3D11DeviceContext context, byte[] bgra, int width, int height)
        {
            if (width <= 0 || height <= 0) { Source = null; return; }
            if (ReferenceEquals(Source, bgra) && Width == width && Height == height) return;

            if (Texture is null || Width != width || Height != height)
            {
                Srv?.Dispose();
                Texture?.Dispose();
                Texture = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.Write,
                });
                Srv = device.CreateShaderResourceView(Texture);
                Width = width;
                Height = height;
            }

            var mapped = context.Map(Texture, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(bgra, y * rowBytes, IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), rowBytes);
                }
            }
            finally
            {
                context.Unmap(Texture, 0);
            }

            Source = bgra;
        }

        public void Dispose()
        {
            Srv?.Dispose();
            Texture?.Dispose();
            Srv = null;
            Texture = null;
        }
    }

    /// <summary>Uploads the cursor bitmap if it changed; pass null to stop drawing one.</summary>
    public void SetCursor(CursorIconBitmap? icon)
    {
        if (icon is not { } value) { _cursor.Clear(); return; }
        _cursor.Update(_device, _context, value.Bgra, value.Width, value.Height);
        _cursor.HotspotX = value.HotspotX;
        _cursor.HotspotY = value.HotspotY;
    }

    /// <summary>Uploads the webcam PiP bitmap if it changed; pass null to stop drawing one.</summary>
    public void SetWebcam(byte[]? bgra, int size)
    {
        if (bgra is null) { _webcam.Clear(); return; }
        _webcam.Update(_device, _context, bgra, size, size);
    }

    /// <summary>Uploads the keystroke toast bitmap if it changed; pass null to stop drawing one.</summary>
    public void SetKeystroke(byte[]? bgra, int width, int height)
    {
        if (bgra is null) { _keystroke.Clear(); return; }
        _keystroke.Update(_device, _context, bgra, width, height);
    }

    /// <summary>
    /// Composites <paramref name="source"/> — the captured frame, still on the GPU — and writes the
    /// finished frame as NV12 into <paramref name="nv12Destination"/>. Pass null for
    /// <paramref name="source"/> on an animation tick to re-compose the last captured frame. When
    /// <paramref name="bgraDestination"/> is non-null the composited frame is also read back as BGRA for
    /// the live preview, which cannot touch the GPU from its own thread. Returns false if anything in
    /// the chain failed, which the caller should treat as "use the CPU path for this frame".
    /// </summary>
    public bool Process(ID3D11Texture2D? source, in GpuFrameParams p, ReadOnlySpan<GpuRipple> ripples,
        byte[] nv12Destination, byte[]? bgraDestination)
    {
        try
        {
            if (source is not null)
            {
                _context.CopyResource(_sourceCache, source);
                _hasCachedSource = true;
            }
            else if (!_hasCachedSource)
            {
                return false; // nothing captured yet, so there is nothing to re-compose
            }

            _context.CopyResource(_texA, _sourceCache);

            var currentRtv = _rtvA;
            var currentSrv = _srvA;
            var current = _texA;
            var otherRtv = _rtvB;
            var otherSrv = _srvB;
            var other = _texB;

            void Swap()
            {
                (current, other) = (other, current);
                (currentRtv, otherRtv) = (otherRtv, currentRtv);
                (currentSrv, otherSrv) = (otherSrv, currentSrv);
            }

            SetCommonState();

            // The cursor goes on before the zoom, so it magnifies along with the content under it.
            if (p.DrawCursor && _cursor.Ready)
            {
                DrawOverlay(_cursor, currentRtv, p.CursorX - _cursor.HotspotX, p.CursorY - _cursor.HotspotY, invertPass: true);
            }

            // Skipped outright at 1x rather than run as an identity resample: the crop is the whole frame
            // then, and a 16-tap pass that cannot change a pixel is pure cost.
            if (p.CropWidth < _width - 0.001 || p.CropHeight < _height - 0.001)
            {
                WriteZoomCb(p.CropX, p.CropY, p.CropWidth, p.CropHeight);
                RunFullscreenPass(_zoomPs, currentSrv, otherRtv);
                Swap();
            }

            if (p.SharpenAmount > 0.001)
            {
                WriteSharpenCb(p.SharpenAmount);
                RunFullscreenPass(_sharpenPs, currentSrv, otherRtv);
                Swap();
            }

            int rippleCount = Math.Min(ripples.Length, MaxRipples);
            if (rippleCount > 0 || p.SpotlightEnabled)
            {
                WriteEffectsCb(p, ripples[..rippleCount]);
                RunFullscreenPass(_effectsPs, currentSrv, otherRtv);
                Swap();
            }

            // Webcam and keystroke sit above the spotlight — a dimmed presenter or a dimmed toast would
            // be nonsensical — and stay a fixed size on screen rather than riding the zoom.
            if (_webcam.Ready)
            {
                const int margin = 24;
                DrawOverlay(_webcam, currentRtv, _width - _webcam.Width - margin, _height - _webcam.Height - margin, invertPass: false);
            }
            if (_keystroke.Ready)
            {
                const int margin = 28;
                DrawOverlay(_keystroke, currentRtv, (_width - _keystroke.Width) / 2, _height - _keystroke.Height - margin, invertPass: false);
            }

            // Leaving a render target bound as an SRV is the classic way to get a silently black frame on
            // the next pass, so unbind before the compute stage rather than trusting it to.
            _context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>());
            _context.PSSetShaderResources(0, [null!]);

            if (bgraDestination is not null)
            {
                _context.CopyResource(_bgraStaging, current);
                if (!ReadPlaneInto(_bgraStaging, bgraDestination, 0, _width * 4, _height)) return false;
            }

            RunNv12Pass(currentSrv);

            _context.CopyResource(_lumaStaging, _lumaTex);
            _context.CopyResource(_chromaStaging, _chromaTex);

            int lumaBytes = _width * _height;
            if (nv12Destination.Length < lumaBytes + _chromaWidth * _chromaHeight * 2) return false;
            if (!ReadPlaneInto(_lumaStaging, nv12Destination, 0, _width, _height)) return false;
            if (!ReadPlaneInto(_chromaStaging, nv12Destination, lumaBytes, _chromaWidth * 2, _chromaHeight)) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetCommonState()
    {
        _context.IASetInputLayout(null);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height, 0f, 1f));
        _context.RSSetScissorRects([]);
        _context.RSSetState(null);
        _context.OMSetDepthStencilState(null);
        _context.PSSetSampler(0, _pointSampler);
    }

    /// <summary>Draws one fullscreen pass. The pass's constant buffer must already be written and bound.</summary>
    private void RunFullscreenPass(ID3D11PixelShader shader, ID3D11ShaderResourceView input, ID3D11RenderTargetView output)
    {
        _context.OMSetRenderTargets(output);
        _context.OMSetBlendState(_opaque);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_fullscreenVs);
        _context.PSSetShader(shader);
        _context.PSSetShaderResource(0, input);
        _context.Draw(3, 0);

        // Unbind immediately: this SRV is about to become a render target on the next pass.
        _context.PSSetShaderResources(0, [null!]);
    }

    private void DrawOverlay(OverlayTexture overlay, ID3D11RenderTargetView target, int x, int y, bool invertPass)
    {
        _context.OMSetRenderTargets(target);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.VSSetShader(_quadVs);
        _context.PSSetShader(_quadPs);
        _context.PSSetShaderResource(0, overlay.Srv!);

        WriteQuadCb(x, y, overlay.Width, overlay.Height, passMode: 0f);
        _context.OMSetBlendState(_alphaBlend);
        _context.Draw(4, 0);

        // Second pass for the XOR-invert pixels, which no single blend state can express alongside
        // ordinary alpha. Only cursors ever contain them.
        if (invertPass)
        {
            WriteQuadCb(x, y, overlay.Width, overlay.Height, passMode: 1f);
            _context.OMSetBlendState(_invertBlend);
            _context.Draw(4, 0);
        }

        _context.PSSetShaderResources(0, [null!]);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    }

    private void RunNv12Pass(ID3D11ShaderResourceView input)
    {
        Span<uint> dims = [(uint)_chromaWidth, (uint)_chromaHeight, (uint)_width, (uint)_height];
        WriteCb(_nv12Cb, MemoryMarshal.Cast<uint, float>(dims));

        _context.CSSetShader(_nv12Cs);
        _context.CSSetConstantBuffer(0, _nv12Cb);
        _context.CSSetShaderResource(0, input);
        _context.CSSetUnorderedAccessView(0, _lumaUav);
        _context.CSSetUnorderedAccessView(1, _chromaUav);

        // 8x8 threads per group, one thread per chroma sample — see FrameShaders.Nv12Cs.
        _context.Dispatch((uint)((_chromaWidth + 7) / 8), (uint)((_chromaHeight + 7) / 8), 1);

        _context.CSSetShaderResources(0, [null!]);
        _context.CSSetUnorderedAccessViews(0, [null!, null!]);
        _context.CSSetShader(null);
    }

    private void WriteZoomCb(double cropX, double cropY, double cropW, double cropH)
    {
        Span<float> data =
        [
            _width, _height,
            (float)cropX, (float)cropY,
            (float)cropW, (float)cropH,
            _width, _height,
        ];
        WriteCb(_zoomCb, data);
        _context.PSSetConstantBuffer(0, _zoomCb);
    }

    private void WriteSharpenCb(double amount)
    {
        Span<float> data = [_width, _height, (float)amount, 0f];
        WriteCb(_sharpenCb, data);
        _context.PSSetConstantBuffer(0, _sharpenCb);
    }

    private void WriteEffectsCb(in GpuFrameParams p, ReadOnlySpan<GpuRipple> ripples)
    {
        Span<float> data = stackalloc float[12 + MaxRipples * 4];
        data.Clear();

        data[0] = _width;
        data[1] = _height;
        data[2] = p.CursorX;
        data[3] = p.CursorY;
        data[4] = p.SpotlightEnabled ? p.SpotlightRadius : 0f;
        data[5] = p.SpotlightFeather;
        data[6] = p.SpotlightDimAlpha;
        data[7] = ripples.Length;
        data[8] = p.RippleThickness;
        // data[9..11] is the shader's RipplePad, already zeroed by Clear().

        for (int i = 0; i < ripples.Length; i++)
        {
            int b = 12 + i * 4;
            data[b + 0] = ripples[i].X;
            data[b + 1] = ripples[i].Y;
            data[b + 2] = ripples[i].Radius;
            data[b + 3] = ripples[i].Opacity;
        }

        WriteCb(_effectsCb, data);
        _context.PSSetConstantBuffer(0, _effectsCb);
    }

    private void WriteQuadCb(float x, float y, float w, float h, float passMode)
    {
        Span<float> data = [x, y, w, h, _width, _height, passMode, 0f];
        WriteCb(_quadCb, data);
        _context.VSSetConstantBuffer(0, _quadCb);
        _context.PSSetConstantBuffer(0, _quadCb);
    }

    private void WriteCb(ID3D11Buffer buffer, ReadOnlySpan<float> values)
    {
        var mapped = _context.Map(buffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                values.CopyTo(new Span<float>((void*)mapped.DataPointer, values.Length));
            }
        }
        finally
        {
            _context.Unmap(buffer, 0);
        }
    }

    /// <summary>
    /// Copies one mapped staging texture into <paramref name="destination"/> at the given byte offset,
    /// row by row — a staging texture's row pitch is rarely the tight width.
    /// </summary>
    private bool ReadPlaneInto(ID3D11Texture2D staging, byte[] destination, int offset, int rowBytes, int rows)
    {
        if (destination.Length < offset + rowBytes * rows) return false;

        var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowPitch = (int)mapped.RowPitch;
            for (int y = 0; y < rows; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * rowPitch), destination, offset + y * rowBytes, rowBytes);
            }
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
        return true;
    }

    public void Dispose()
    {
        _keystroke.Dispose();
        _webcam.Dispose();
        _cursor.Dispose();

        _nv12Cb.Dispose(); _quadCb.Dispose(); _effectsCb.Dispose(); _sharpenCb.Dispose(); _zoomCb.Dispose();
        _opaque.Dispose(); _invertBlend.Dispose(); _alphaBlend.Dispose(); _pointSampler.Dispose();
        _bgraStaging.Dispose(); _chromaStaging.Dispose(); _lumaStaging.Dispose();
        _chromaUav.Dispose(); _lumaUav.Dispose(); _chromaTex.Dispose(); _lumaTex.Dispose();
        _srvB.Dispose(); _srvA.Dispose(); _rtvB.Dispose(); _rtvA.Dispose();
        _texB.Dispose(); _texA.Dispose(); _sourceCache.Dispose();
        _nv12Cs.Dispose(); _quadPs.Dispose(); _effectsPs.Dispose(); _sharpenPs.Dispose();
        _zoomPs.Dispose(); _quadVs.Dispose(); _fullscreenVs.Dispose();
    }
}

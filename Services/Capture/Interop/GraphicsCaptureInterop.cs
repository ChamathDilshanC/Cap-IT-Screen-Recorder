using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace ScreenRecorderApp.Services.Capture.Interop;

/// <summary>
/// The one genuinely unavoidable hand-rolled COM interop surface for Windows.Graphics.Capture: there is
/// no public managed API to create a <see cref="GraphicsCaptureItem"/> for an arbitrary HWND, or to pull
/// the underlying D3D11 texture out of a captured frame's surface — both are exposed only via documented
/// COM interfaces with no WinRT projection of their own.
///
/// Every call here goes through the raw COM vtable via unsafe function pointers rather than a classic
/// <c>[ComImport]</c> interface + <see cref="Marshal.GetObjectForIUnknown"/>/<see cref="Marshal.GetTypedObjectForIUnknown"/>
/// cast. That classic route reliably throws <see cref="InvalidCastException"/> the instant the interface
/// method is actually invoked, in this process: CsWinRT/WinRT.Runtime registers a *global*
/// <see cref="System.Runtime.InteropServices.ComWrappers"/> instance for all WinRT interop
/// (<c>WinRT.ComWrappersSupport</c>), and that redirects the CLR's RCW machinery even for a plain
/// non-WinRT COM interface like these — producing a wrapper that cannot actually dispatch the call. A raw
/// vtable call sidesteps RCW/ComWrappers entirely, which is exactly what CsWinRT's own generated
/// projection code does internally for its own interfaces.
/// </summary>
internal static unsafe class GraphicsCaptureInterop
{
    private static readonly Guid IGraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IDirect3DDxgiInterfaceAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    // The IID CreateForWindow needs to be asked for is the IGraphicsCaptureItem *interface*'s well-known
    // GUID from windows.graphics.capture.h — NOT typeof(GraphicsCaptureItem).GUID, which is the projected
    // runtime class's own GUID and gets E_NOINTERFACE back from the OS's factory implementation.
    private static readonly Guid IGraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    // The real COM IID for ID3D11Texture2D — used to pull the underlying D3D11 texture out of a captured
    // frame's IDirect3DSurface via IDirect3DDxgiInterfaceAccess, below.
    public static readonly Guid Id3D11Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    // .NET (Core)'s P/Invoke marshaler has no built-in string->HSTRING conversion — that automatic
    // [MarshalAs(UnmanagedType.HString)] behavior only ever existed for .NET Framework WinRT apps. On
    // .NET 5+ every HSTRING has to be built and torn down by hand via these two combase.dll exports.
    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out nint hstring);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        nint activatableClassId,
        [In] ref Guid iid,
        out nint factory);

    [DllImport("d3d11.dll", PreserveSig = false)]
    public static extern void CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    /// <summary>
    /// Invokes a COM vtable slot shaped like <c>HRESULT Fn(void* self, void* handle, REFIID riid, void** result)</c>
    /// — the shape both <c>IGraphicsCaptureItemInterop::CreateForWindow/CreateForMonitor</c> and (with
    /// <paramref name="handle"/> unused) <c>IDirect3DDxgiInterfaceAccess::GetInterface</c> need. Slot
    /// indices are 0-based *after* IUnknown's own 3 slots (QueryInterface, AddRef, Release).
    /// </summary>
    private static int InvokeInterfaceFactorySlot(nint self, int slotAfterIUnknown, nint handle, ref Guid riid, out nint result)
    {
        var vtable = *(nint*)self;
        var slotPtr = *(nint*)(vtable + (3 + slotAfterIUnknown) * sizeof(nint));
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)slotPtr;
        fixed (Guid* riidPtr = &riid)
        fixed (nint* resultPtr = &result)
        {
            return fn(self, handle, riidPtr, resultPtr);
        }
    }

    /// <summary>
    /// Same idea as <see cref="InvokeInterfaceFactorySlot"/> but for the narrower
    /// <c>HRESULT Fn(void* self, REFIID riid, void** result)</c> shape (no handle parameter) that
    /// <c>IDirect3DDxgiInterfaceAccess::GetInterface</c> uses — a distinct helper because x64's
    /// register-based calling convention means passing a dummy extra argument doesn't line up harmlessly
    /// the way it might on a stack-based ABI; it shifts every real argument into the wrong register.
    /// </summary>
    private static int InvokeGetInterfaceSlot(nint self, int slotAfterIUnknown, ref Guid riid, out nint result)
    {
        var vtable = *(nint*)self;
        var slotPtr = *(nint*)(vtable + (3 + slotAfterIUnknown) * sizeof(nint));
        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)slotPtr;
        fixed (Guid* riidPtr = &riid)
        fixed (nint* resultPtr = &result)
        {
            return fn(self, riidPtr, resultPtr);
        }
    }

    /// <summary>Creates a GraphicsCaptureItem for a specific top-level window.</summary>
    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(className, className.Length, out var classNameHString);
        try
        {
            var interopIid = IGraphicsCaptureItemInteropIid;
            RoGetActivationFactory(classNameHString, ref interopIid, out var factoryPtr);
            try
            {
                var itemIid = IGraphicsCaptureItemIid;
                // Slot 0 after IUnknown = CreateForWindow (CreateForMonitor is slot 1, unused here).
                var hr = InvokeInterfaceFactorySlot(factoryPtr, 0, hwnd, ref itemIid, out var itemPtr);
                Marshal.ThrowExceptionForHR(hr);
                return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }
        }
        finally
        {
            WindowsDeleteString(classNameHString);
        }
    }

    /// <summary>
    /// Pulls a native interface pointer (queried for <paramref name="iid"/>) out of a CsWinRT-projected
    /// WinRT object — used to reach the real <c>ID3D11Texture2D</c> behind a captured frame's
    /// <c>IDirect3DSurface</c>, via <c>IDirect3DDxgiInterfaceAccess::GetInterface</c>. The returned
    /// pointer carries one reference the caller owns (standard COM QueryInterface convention) — hand it
    /// straight to whatever wrapper (e.g. Vortice's <c>ID3D11Texture2D</c>) takes ownership of it.
    /// </summary>
    public static nint GetInterfaceFromWinRTObject(object winrtObject, Guid iid)
    {
        var unknown = WinRT.MarshalInspectable<object>.FromManaged(winrtObject);
        try
        {
            var accessIid = IDirect3DDxgiInterfaceAccessIid;
            var qiHr = Marshal.QueryInterface(unknown, ref accessIid, out var accessPtr);
            Marshal.ThrowExceptionForHR(qiHr);
            try
            {
                var iidLocal = iid;
                // GetInterface is IDirect3DDxgiInterfaceAccess's only method, so it's slot 0 after IUnknown.
                var hr = InvokeGetInterfaceSlot(accessPtr, 0, ref iidLocal, out var result);
                Marshal.ThrowExceptionForHR(hr);
                return result;
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}

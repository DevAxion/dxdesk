using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DXDesk.Core;

/// <summary>
/// Əsas monitoru DXGI Desktop Duplication ilə tutur (Windows 8+).
/// GDI bəzi Windows 11 qrafik drayverlərində (hibrid GPU, aparat sürətləndirmə)
/// QARA kadr qaytaranda ehtiyat üsul kimi istifadə olunur.
/// Masaüstü dəyişəndə (UAC/kilid ekranı) və ya rejim dəyişəndə dublikasiya
/// itirilir — onda istisna atılır və çağıran tərəf yenidən yaradır.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DxgiCapturer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _staging;
    private static readonly FeatureLevel[] FeatureLevels =
        { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
    private bool _hasFrame;

    public int Width { get; }
    public int Height { get; }

    public DxgiCapturer()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        // Hibrid GPU-larda əsas monitor iGPU-da ola bilər: əsas (0,0) çıxışı olan adapteri tapırıq.
        IDXGIAdapter1? adapter = null;
        IDXGIOutput? output = null;
        for (uint a = 0; factory.EnumAdapters1(a, out var candidate).Success; a++)
        {
            for (uint o = 0; candidate.EnumOutputs(o, out var outp).Success; o++)
            {
                var d = outp.Description;
                if (d.AttachedToDesktop && d.DesktopCoordinates.Left == 0 && d.DesktopCoordinates.Top == 0)
                {
                    adapter = candidate;
                    output = outp;
                    break;
                }
                outp.Dispose();
            }
            if (output is not null) break;
            candidate.Dispose();
        }

        if (adapter is null || output is null)
        {
            throw new InvalidOperationException("DXGI: əsas monitor tapılmadı.");
        }

        try
        {
            D3D11.D3D11CreateDevice(
                adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                FeatureLevels,
                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            _device = device!;
            _context = context!;

            using var output1 = output.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(_device);

            var rect = output.Description.DesktopCoordinates;
            Width = rect.Right - rect.Left;
            Height = rect.Bottom - rect.Top;
        }
        finally
        {
            output.Dispose();
            adapter.Dispose();
        }

        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
        });
    }

    /// <summary>
    /// Yeni kadr varsa onu <paramref name="target"/>-ə (24bpp, eyni ölçü) yazır.
    /// Dəyişiklik yoxdursa əvvəlki məzmun qalır. Dublikasiya itəndə istisna atır.
    /// </summary>
    public void CaptureInto(Bitmap target)
    {
        var result = _duplication.AcquireNextFrame(_hasFrame ? 0u : 500u, out _, out var resource);
        if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
        {
            return; // ekran dəyişməyib — əvvəlki şəkil qalır
        }
        result.CheckError();

        try
        {
            using (var texture = resource!.QueryInterface<ID3D11Texture2D>())
            {
                _context.CopyResource(_staging, texture);
            }

            var map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                CopyBgraToBgr(map.DataPointer, (int)map.RowPitch, target);
                _hasFrame = true;
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
        }
        finally
        {
            resource?.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    private unsafe void CopyBgraToBgr(IntPtr src, int srcPitch, Bitmap target)
    {
        var w = Math.Min(Width, target.Width);
        var h = Math.Min(Height, target.Height);
        var data = target.LockBits(new Rectangle(0, 0, target.Width, target.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < h; y++)
            {
                var s = (byte*)src + y * srcPitch;
                var d = (byte*)data.Scan0 + y * data.Stride;
                for (var x = 0; x < w; x++)
                {
                    d[0] = s[0];
                    d[1] = s[1];
                    d[2] = s[2];
                    s += 4;
                    d += 3;
                }
            }
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        _staging.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}

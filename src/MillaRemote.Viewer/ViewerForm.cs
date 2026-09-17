using System.Drawing.Imaging;
using System.Runtime.Versioning;

using MillaRemote.Core;

namespace MillaRemote.Viewer;

/// <summary>
/// Uzaq ekranı göstərən pəncərə. Kadrlar <see cref="UpdateFrame"/> ilə verilir.
/// Mərhələ 2-də kadrlar lokal ekrandan (loopback) gəlir; Mərhələ 3-də şəbəkədən.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ViewerForm : Form
{
    private readonly PictureBox _picture;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _infoLabel;

    // FPS və bant hesablaması üçün.
    private int _frameCount;
    private long _bytesSinceTick;
    private DateTime _lastTick = DateTime.UtcNow;
    private double _fps;
    private double _kbps;

    public ViewerForm(string title)
    {
        Text = title;
        Width = 1200;
        Height = 750;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;

        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
        };

        _infoLabel = new ToolStripStatusLabel("Gözlənilir...");
        _status = new StatusStrip();
        _status.Items.Add(_infoLabel);

        Controls.Add(_picture);
        Controls.Add(_status);

        // Uzaqdan idarə üçün input tutma (yalnız EnableInput true olanda göndərilir).
        KeyPreview = true;
        _picture.MouseMove += (_, e) => SendMove(e.Location);
        _picture.MouseDown += (_, e) => SendButton(e.Button, down: true, e.Location);
        _picture.MouseUp += (_, e) => SendButton(e.Button, down: false, e.Location);
        _picture.MouseWheel += (_, e) => { if (EnableInput) Raise(InputMessage.MouseWheel((short)e.Delta)); };
        KeyDown += (_, e) => { if (EnableInput) { Raise(InputMessage.Key(true, (ushort)e.KeyValue)); e.SuppressKeyPress = true; } };
        KeyUp += (_, e) => { if (EnableInput) { Raise(InputMessage.Key(false, (ushort)e.KeyValue)); e.SuppressKeyPress = true; } };
    }

    /// <summary>true olanda siçan/klaviatura host-a göndərilir (uzaqdan idarə).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool EnableInput { get; set; }

    /// <summary>Kodlaşdırılmış input mesajı hazır olanda tetiklenir (host-a göndərmək üçün).</summary>
    public event Action<byte[]>? InputProduced;

    private void Raise(byte[] msg) => InputProduced?.Invoke(msg);

    private void SendMove(Point clientPoint)
    {
        if (!EnableInput) return;
        if (TryMapToNormalized(clientPoint, out var nx, out var ny))
            Raise(InputMessage.MouseMove(nx, ny));
    }

    private void SendButton(MouseButtons button, bool down, Point clientPoint)
    {
        if (!EnableInput) return;
        if (TryMapToNormalized(clientPoint, out var nx, out var ny))
            Raise(InputMessage.MouseMove(nx, ny)); // əvvəlcə mövqeyi dəqiqləşdir
        var kind = button switch
        {
            MouseButtons.Right => MouseButtonKind.Right,
            MouseButtons.Middle => MouseButtonKind.Middle,
            _ => MouseButtonKind.Left,
        };
        Raise(InputMessage.MouseButton(down, kind));
    }

    /// <summary>
    /// PictureBox (Zoom) daxilindəki nöqtəni uzaq ekranın 0..65535 normalizə
    /// koordinatlarına çevirir. Şəkil sahəsindən kənardadırsa false qaytarır.
    /// </summary>
    private bool TryMapToNormalized(Point p, out ushort nx, out ushort ny)
    {
        nx = ny = 0;
        var img = _picture.Image;
        if (img is null) return false;

        double cw = _picture.ClientSize.Width, ch = _picture.ClientSize.Height;
        double iw = img.Width, ih = img.Height;
        if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) return false;

        var scale = Math.Min(cw / iw, ch / ih);      // Zoom: en-boy nisbəti saxlanır
        double drawnW = iw * scale, drawnH = ih * scale;
        double offX = (cw - drawnW) / 2.0, offY = (ch - drawnH) / 2.0;

        double ix = (p.X - offX) / scale;
        double iy = (p.Y - offY) / scale;
        if (ix < 0 || iy < 0 || ix > iw || iy > ih) return false; // letterbox kənarı

        nx = (ushort)Math.Clamp(ix / iw * 65535.0, 0, 65535);
        ny = (ushort)Math.Clamp(iy / ih * 65535.0, 0, 65535);
        return true;
    }

    /// <summary>
    /// Yeni JPEG kadrını göstərir. İstənilən thread-dən çağırıla bilər
    /// (şəbəkə kadrları arxa plan thread-lərindən gəlir).
    /// </summary>
    public void UpdateFrame(byte[] jpeg)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try { BeginInvoke(() => UpdateFrame(jpeg)); } catch (InvalidOperationException) { }
            return;
        }

        Image? decoded = null;
        try
        {
            // MemoryStream Bitmap-in ömrü boyu açıq qalmalıdır, ona görə kopyalayırıq.
            using var ms = new MemoryStream(jpeg, writable: false);
            using var loaded = Image.FromStream(ms);
            decoded = new Bitmap(loaded);
        }
        catch
        {
            decoded?.Dispose();
            return; // Zədələnmiş kadr — buraxırıq.
        }

        var old = _picture.Image;
        _picture.Image = decoded;
        old?.Dispose();

        UpdateStats(jpeg.Length, decoded.Width, decoded.Height);
    }

    /// <summary>Hazır bir Bitmap-i göstərir (tile dekoderin kətanı). Kətan təkrar
    /// istifadə olunduğu üçün nüsxə çıxarırıq.</summary>
    public void ShowBitmap(Bitmap canvas, int payloadBytes)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            var w0 = canvas.Width; var h0 = canvas.Height;
            Bitmap copy;
            lock (canvas) { copy = new Bitmap(canvas); }
            try { BeginInvoke(() => SetImage(copy, payloadBytes, w0, h0)); }
            catch (InvalidOperationException) { copy.Dispose(); }
            return;
        }
        SetImage(new Bitmap(canvas), payloadBytes, canvas.Width, canvas.Height);
    }

    private void SetImage(Bitmap img, int payloadBytes, int w, int h)
    {
        var old = _picture.Image;
        _picture.Image = img;
        old?.Dispose();
        UpdateStats(payloadBytes, w, h);
    }

    /// <summary>Status zolağında sərbəst mətn göstərir (bağlantı vəziyyəti üçün).</summary>
    public void ShowStatus(string text)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => ShowStatus(text)); } catch (InvalidOperationException) { }
            return;
        }
        _infoLabel.Text = text;
    }

    private void UpdateStats(int frameBytes, int w, int h)
    {
        _frameCount++;
        _bytesSinceTick += frameBytes;

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;
        if (elapsed >= 1.0)
        {
            _fps = _frameCount / elapsed;
            _kbps = _bytesSinceTick / 1024.0 / elapsed;
            _frameCount = 0;
            _bytesSinceTick = 0;
            _lastTick = now;
        }

        _infoLabel.Text =
            $"Ölçü: {w}x{h}   |   FPS: {_fps:F1}   |   {_kbps:F0} KB/s   |   son kadr: {frameBytes / 1024.0:F1} KB";
    }
}

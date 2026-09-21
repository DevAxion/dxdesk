using System.Diagnostics;
using SRXDesk.Core;

// Mərhələ 1 testi: Core.ScreenCapturer ilə ekranı tutub JPEG kimi saxlayır.
using var capturer = new ScreenCapturer();
Console.WriteLine($"Ekran ölçüsü: {capturer.ScreenSize.Width} x {capturer.ScreenSize.Height}");

var sw = Stopwatch.StartNew();
var jpeg = capturer.CaptureJpeg();
sw.Stop();

var outPath = Path.Combine(AppContext.BaseDirectory, "screenshot.jpg");
File.WriteAllBytes(outPath, jpeg);
Console.WriteLine($"Tutuldu: {jpeg.Length / 1024.0:F1} KB, {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"Fayl: {outPath}");

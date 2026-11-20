using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Connections.Client;

namespace DXDesk.Core;

/// <summary>
/// Relay ilə HTTPS/WSS bağlantısının yoxlanması.
///
/// <c>Relay:CertThumbprint</c> (SHA-256 barmaq izi) təyin olunubsa, YALNIZ həmin sertifikat
/// qəbul edilir (pinning) — self-signed sertifikatı GPO ilə yaymağa ehtiyac yoxdur və
/// başqa sertifikatla özünü relay kimi göstərən server rədd olunur.
/// Təyin olunmayıbsa, Windows-un adi sertifikat yoxlaması işləyir.
/// </summary>
public static class RelayTls
{
    private static string? _pin;

    /// <summary>SHA-256 (64 hex) və ya SHA-1 (40 hex) barmaq izi; ":" və boşluqlar nəzərə alınmır.</summary>
    public static string? PinnedThumbprint
    {
        get => _pin;
        set => _pin = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant();
    }

    /// <summary>Sertifikatı yoxlayır (pin varsa — yalnız pin, yoxdursa — adi yoxlama).</summary>
    public static bool Validate(X509Certificate? certificate, SslPolicyErrors errors)
    {
        var pin = _pin;
        if (pin is null)
        {
            return errors == SslPolicyErrors.None;
        }

        if (certificate is null)
        {
            return false;
        }

        using var cert = new X509Certificate2(certificate);
        var actual = pin.Length == 40
            ? cert.Thumbprint
            : cert.GetCertHashString(HashAlgorithmName.SHA256);
        return string.Equals(actual, pin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SignalR bağlantısına sertifikat yoxlamasını tətbiq edir: <c>.WithUrl(url, RelayTls.Configure)</c>.</summary>
    public static void Configure(HttpConnectionOptions options)
    {
        options.HttpMessageHandlerFactory = handler =>
        {
            if (handler is HttpClientHandler h)
            {
                h.ServerCertificateCustomValidationCallback = (_, cert, _, errors) => Validate(cert, errors);
            }
            return handler;
        };
        options.WebSocketConfiguration = ws =>
            ws.RemoteCertificateValidationCallback = (_, cert, _, errors) => Validate(cert, errors);
    }
}

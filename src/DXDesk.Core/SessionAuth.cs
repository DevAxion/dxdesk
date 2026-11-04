using System.Security.Cryptography;
using System.Text;

namespace DXDesk.Core;

/// <summary>
/// Ekran portu üçün bir dəfəlik sessiya açarı.
/// Relay hər qoşulma sorğusu üçün təsadüfi açar yaradır, onu həm agentə, həm də
/// sorğunu göndərən adminə verir. Viewer TCP-yə qoşulan kimi ilk kadr kimi
/// [magic "DXA1"][açar baytları] göndərir; açar uyğun gəlməsə agent bağlantını kəsir.
/// </summary>
public static class SessionAuth
{
    private static readonly byte[] Magic = "DXA1"u8.ToArray();
    private const int TokenBytes = 32;

    /// <summary>Viewer açarı bu müddətdə göndərməlidir.</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Yeni təsadüfi açar (64 simvol hex).</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>Viewer tərəfi: açarı ilk kadr kimi göndərir.</summary>
    public static Task SendAsync(Stream stream, string token, CancellationToken ct = default) =>
        FrameProtocol.WriteFrameAsync(stream, BuildHello(token), ct);

    /// <summary>Agent tərəfi (sinxron): ilk kadrı oxuyub açarı yoxlayır.</summary>
    public static bool Verify(Stream stream, string expectedToken)
    {
        using var cts = new CancellationTokenSource(HandshakeTimeout);
        try
        {
            return VerifyAsync(stream, expectedToken, cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Agent tərəfi: ilk kadrı <see cref="HandshakeTimeout"/> ərzində oxuyub yoxlayır.</summary>
    public static async Task<bool> VerifyAsync(Stream stream, string expectedToken, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HandshakeTimeout);
        byte[]? hello;
        try
        {
            hello = await FrameProtocol.ReadFrameAsync(stream, timeout.Token, maxBytes: 256);
        }
        catch
        {
            return false;
        }

        if (hello is null || string.IsNullOrEmpty(expectedToken))
        {
            return false;
        }

        var expected = BuildHello(expectedToken);
        return CryptographicOperations.FixedTimeEquals(hello, expected);
    }

    private static byte[] BuildHello(string token)
    {
        var t = Encoding.ASCII.GetBytes(token ?? string.Empty);
        var b = new byte[Magic.Length + t.Length];
        Magic.CopyTo(b, 0);
        t.CopyTo(b, Magic.Length);
        return b;
    }
}

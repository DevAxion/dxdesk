namespace DXDesk.Server;

/// <summary>Relay-dən gələn onlayn kliyent məlumatı (JSON adları relay ilə eyni olmalıdır).</summary>
public sealed record ClientInfo(
    string Hostname,
    string ConnectionId,
    string? IpAddress,
    DateTimeOffset ConnectedAtUtc);

/// <summary><c>RequestConnection</c> hub metodunun nəticəsi.</summary>
public sealed record RequestResult(
    bool Sent,
    string? Error,
    string? Hostname,
    string? IpAddress);

/// <summary>Kliyentin Qəbul/Rədd cavabı.</summary>
public sealed record ConnectionResponseMessage(
    string Hostname,
    bool Accepted,
    string? IpAddress,
    int StreamPort);

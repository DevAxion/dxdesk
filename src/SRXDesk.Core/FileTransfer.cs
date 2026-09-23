using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;

namespace SRXDesk.Core;

/// <summary>
/// Clipboard fayl köçürmə protokolu (mətn deyil, faylların MƏZMUNU ötürülür).
/// Mesaj (lead baytdan sonrakı sub-payload):
///   [1] MANIFEST: [int count]( [int relLen][utf8 rel][long size] )
///   [2] CHUNK:    [int fileIndex][int dataLen][bytes]
///   [3] COMMIT:   (boş) -> qəbul edən staging fayllarını clipboard-a qoyur
///
/// Lead bayt istiqamətə görə: viewer->agent = InputType.File, agent->viewer = KindFile.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileTransfer
{
    public const byte SubManifest = 1;
    public const byte SubChunk = 2;
    public const byte SubCommit = 3;
    private const int ChunkSize = 512 * 1024;

    /// <summary>Clipboard fayl/qovluqlarını mesaj axınına çevirir (lead bayt hər mesajın əvvəlinə).</summary>
    public static IEnumerable<byte[]> Build(string[] topPaths, byte lead)
    {
        var files = Expand(topPaths);

        // MANIFEST
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(lead);
            ms.WriteByte(SubManifest);
            WriteInt(ms, files.Count);
            foreach (var f in files)
            {
                var rb = Encoding.UTF8.GetBytes(f.rel);
                WriteInt(ms, rb.Length);
                ms.Write(rb);
                WriteLong(ms, f.size);
            }
            yield return ms.ToArray();
        }

        // CHUNKS
        for (var i = 0; i < files.Count; i++)
        {
            using var fs = File.OpenRead(files[i].full);
            var buf = new byte[ChunkSize];
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                using var ms = new MemoryStream();
                ms.WriteByte(lead);
                ms.WriteByte(SubChunk);
                WriteInt(ms, i);
                WriteInt(ms, n);
                ms.Write(buf, 0, n);
                yield return ms.ToArray();
            }
        }

        // COMMIT
        yield return new[] { lead, SubCommit };
    }

    /// <summary>Clipboard fayl dəstinin imzası (echo qorunması üçün). İki tərəf eyni hesablayır.</summary>
    public static string Signature(string[] paths)
    {
        var items = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p)) items.Add(new DirectoryInfo(p).Name + ":d");
                else if (File.Exists(p)) items.Add(Path.GetFileName(p) + ":" + new FileInfo(p).Length);
            }
            catch { items.Add(Path.GetFileName(p)); }
        }
        items.Sort(StringComparer.OrdinalIgnoreCase);
        return "F:" + string.Join("|", items);
    }

    private static List<(string rel, string full, long size)> Expand(string[] topPaths)
    {
        var files = new List<(string rel, string full, long size)>();
        foreach (var top in topPaths)
        {
            try
            {
                if (Directory.Exists(top))
                {
                    var baseName = new DirectoryInfo(top.TrimEnd('\\')).Name;
                    foreach (var f in Directory.EnumerateFiles(top, "*", SearchOption.AllDirectories))
                    {
                        var rel = baseName + "\\" + Path.GetRelativePath(top, f);
                        files.Add((rel, f, new FileInfo(f).Length));
                    }
                }
                else if (File.Exists(top))
                {
                    files.Add((Path.GetFileName(top), top, new FileInfo(top).Length));
                }
            }
            catch { /* əlçatmaz element — buraxırıq */ }
        }
        return files;
    }

    internal static void WriteInt(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b);
    }

    internal static void WriteLong(Stream s, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        s.Write(b);
    }
}

/// <summary>
/// Fayl köçürmə mesajlarını qəbul edir: staging qovluğuna yazır, COMMIT-də
/// <see cref="_setClipboard"/> ilə clipboard-a qoyur. Hər gələn istiqamət üçün
/// bir nüsxə saxlanılır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileTransferReceiver : IDisposable
{
    private readonly Action<string[]> _setClipboard;
    private readonly Action<string> _log;

    private string? _stageRoot;
    private List<FileStream>? _streams;
    private List<string>? _topLevel;

    /// <summary>Son qəbul edilən dəstin imzası (echo qorunması üçün).</summary>
    public string? LastSignature { get; private set; }

    public FileTransferReceiver(Action<string[]> setClipboard, Action<string> log)
    {
        _setClipboard = setClipboard;
        _log = log;
    }

    /// <summary>Bir mesajı emal edir (sub[0]=subType).</summary>
    public void Handle(byte[] sub)
    {
        if (sub.Length < 1) return;
        try
        {
            switch (sub[0])
            {
                case FileTransfer.SubManifest: OnManifest(sub); break;
                case FileTransfer.SubChunk: OnChunk(sub); break;
                case FileTransfer.SubCommit: OnCommit(); break;
            }
        }
        catch (Exception ex)
        {
            _log($"Fayl köçürmə xətası: {ex.Message}");
            Cleanup();
        }
    }

    private void OnManifest(byte[] b)
    {
        Cleanup();
        _stageRoot = Path.Combine(Path.GetTempPath(), "SRXDesk", "clip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stageRoot);

        var pos = 1;
        var count = ReadInt(b, ref pos);
        _streams = new List<FileStream>(count);
        var topSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sigItems = new List<string>();

        for (var i = 0; i < count; i++)
        {
            var relLen = ReadInt(b, ref pos);
            var rel = Encoding.UTF8.GetString(b, pos, relLen); pos += relLen;
            var size = ReadLong(b, ref pos);

            var full = Path.GetFullPath(Path.Combine(_stageRoot, rel));
            // Path traversal qoruması
            if (!full.StartsWith(_stageRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Yanlış yol.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            _streams.Add(new FileStream(full, FileMode.Create, FileAccess.Write));

            var firstSeg = rel.Replace('/', '\\').Split('\\')[0];
            topSet.Add(Path.Combine(_stageRoot, firstSeg));
            sigItems.Add((rel.Contains('\\') || rel.Contains('/')) ? firstSeg + ":d" : rel + ":" + size);
        }

        _topLevel = topSet.ToList();
        sigItems.Sort(StringComparer.OrdinalIgnoreCase);
        LastSignature = "F:" + string.Join("|", sigItems);
        _log($"Fayl köçürmə başladı: {count} fayl -> {_stageRoot}");
    }

    private void OnChunk(byte[] b)
    {
        if (_streams is null) return;
        var pos = 1;
        var index = ReadInt(b, ref pos);
        var len = ReadInt(b, ref pos);
        if (index >= 0 && index < _streams.Count)
        {
            _streams[index].Write(b, pos, len);
        }
    }

    private void OnCommit()
    {
        if (_streams is null || _topLevel is null) return;
        foreach (var s in _streams) { try { s.Flush(); s.Dispose(); } catch { } }
        _streams = null;
        _log($"Fayl köçürmə bitdi: {_topLevel.Count} element clipboard-a qoyulur.");
        try { _setClipboard(_topLevel.ToArray()); } catch (Exception ex) { _log($"Clipboard set xətası: {ex.Message}"); }
    }

    private void Cleanup()
    {
        if (_streams is not null)
        {
            foreach (var s in _streams) { try { s.Dispose(); } catch { } }
            _streams = null;
        }
    }

    public void Dispose() => Cleanup();

    private static int ReadInt(byte[] b, ref int pos)
    {
        var v = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    private static long ReadLong(byte[] b, ref int pos)
    {
        var v = BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(pos, 8));
        pos += 8;
        return v;
    }
}

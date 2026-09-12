using System.Security.Cryptography;
using System.Text;

namespace MarkdownEditor.App;

public sealed record LoadedDocument(string Path, string Text, Encoding Encoding, string Fingerprint);
public sealed class FileConflictException() : IOException("Файл изменён другой программой или удалён.");

public sealed class DocumentFiles
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public static bool IsMarkdown(string path) =>
        Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    public async Task<LoadedDocument> ReadAsync(string path, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        if (stream.Length > MaxBytes) throw new IOException("Файл больше 4 МБ. Выберите документ меньшего размера.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > MaxBytes) throw new IOException("Файл больше 4 МБ.");
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        var (encoding, skip) = DetectEncoding(bytes);
        return new(path, encoding.GetString(bytes, skip, bytes.Length - skip), encoding, Hash(bytes));
    }

    public async Task<string> WriteAsync(string path, string text, Encoding encoding,
        string? expectedFingerprint, bool overwriteConflict = false)
    {
        path = Path.GetFullPath(path);
        var content = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        if (content.Length > MaxBytes) throw new IOException("Документ больше 4 МБ.");
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, content);
            // Check immediately before replacement; failed writes never truncate the original.
            if (expectedFingerprint is not null && !overwriteConflict &&
                (!File.Exists(path) || !await MatchesFingerprintAsync(path, expectedFingerprint)))
                throw new FileConflictException();
            File.Move(temp, path, true);
            return Hash(content);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static (Encoding Encoding, int Skip) DetectEncoding(byte[] data)
    {
        if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) return (new UTF32Encoding(false, true, true), 4);
        if (data.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) return (new UTF32Encoding(true, true, true), 4);
        if (data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(true, true), 3);
        if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, true, true), 2);
        if (data.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, true, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static async Task<bool> MatchesFingerprintAsync(string path, string expected)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        if (stream.Length > MaxBytes) return false;
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)) == expected;
    }
}

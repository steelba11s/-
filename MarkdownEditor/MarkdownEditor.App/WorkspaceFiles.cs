namespace MarkdownEditor.App;

public sealed record FileNode(string Name, string FullPath, bool IsDirectory, IReadOnlyList<FileNode> Children);
public sealed record SearchHit(string Path, string FileName, int Line, int Column, int Offset, int Length, string Snippet)
{
    public string Location => $"{FileName}:{Line}:{Column}";
}
public sealed record FolderSearchResult(IReadOnlyList<SearchHit> Hits, int SkippedFiles, bool Truncated);

public static class WorkspaceFiles
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
    };

    public static IReadOnlyList<FileNode> ReadTree(string root)
    {
        var budget = 10000;
        return Visit(root, 0, ref budget);
    }

    private static List<FileNode> Visit(string directory, int depth, ref int budget)
    {
        if (depth > 30 || budget <= 0) return [];
        var nodes = new List<FileNode>();
        foreach (var child in Directory.EnumerateDirectories(directory, "*", Options).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (--budget <= 0) break;
            if (Path.GetFileName(child) is "bin" or "obj" or "node_modules") continue;
            nodes.Add(new(Path.GetFileName(child), child, true, Visit(child, depth + 1, ref budget)));
        }
        foreach (var file in Directory.EnumerateFiles(directory, "*", Options).Where(DocumentFiles.IsMarkdown).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (--budget <= 0) break;
            nodes.Add(new(Path.GetFileName(file), file, false, []));
        }
        return nodes;
    }

    public static IEnumerable<string> EnumerateMarkdown(string root, CancellationToken token)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = queue.Dequeue();
            foreach (var file in Directory.EnumerateFiles(dir, "*", Options))
                if (DocumentFiles.IsMarkdown(file)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(dir, "*", Options))
                if (Path.GetFileName(child) is not ("bin" or "obj" or "node_modules")) queue.Enqueue(child);
        }
    }

    public static List<SearchHit> Find(string text, string query, string path, bool matchCase, int limit = 500)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(query)) return hits;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var offset = 0;
        var scanned = 0;
        var line = 1;
        var lineStart = 0;
        while (offset <= text.Length - query.Length && hits.Count < limit)
        {
            var index = text.IndexOf(query, offset, comparison);
            if (index < 0) break;
            for (; scanned < index; scanned++)
                if (text[scanned] == '\n') { line++; lineStart = scanned + 1; }
            var end = text.IndexOf('\n', index);
            if (end < 0) end = text.Length;
            var start = Math.Max(lineStart, index - 60);
            hits.Add(new(path, Path.GetFileName(path), line, index - lineStart + 1, index, query.Length,
                text[start..Math.Min(end, start + 180)].Trim()));
            offset = index + query.Length;
        }
        return hits;
    }

    public static async Task<FolderSearchResult> SearchFolderAsync(string folder, string query, bool matchCase,
        string? openPath, string openText, CancellationToken token)
    {
        var hits = new List<SearchHit>();
        var skipped = 0;
        var files = new DocumentFiles();
        foreach (var path in EnumerateMarkdown(folder, token))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var text = string.Equals(path, openPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    ? openText : (await files.ReadAsync(path, token)).Text;
                hits.AddRange(Find(text, query, path, matchCase, 501 - hits.Count));
                if (hits.Count > 500) return new(hits.Take(500).ToList(), skipped, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException) { skipped++; }
        }
        return new(hits, skipped, false);
    }
}

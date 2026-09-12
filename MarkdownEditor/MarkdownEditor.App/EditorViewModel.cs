using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;

namespace MarkdownEditor.App;

public enum SaveDecision { Save, Discard, Cancel }

public interface IEditorDialogs
{
    Task<string?> PickFileAsync();
    Task<string?> PickFolderAsync();
    Task<string?> PickSaveAsync(string suggestedName, bool html);
    Task<SaveDecision> ConfirmSaveAsync(string name);
    Task<bool> ConfirmOverwriteAsync(string name);
    Task ShowErrorAsync(string message);
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class EditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEditorDialogs dialogs;
    private readonly DocumentFiles files = new();
    private string text = "";
    private string savedText = "";
    private string? currentPath;
    private string? fingerprint;
    private Encoding encoding = new UTF8Encoding(false, true);
    private string? folderPath;
    private bool busy;
    private string status = "Готово";
    private string query = "";
    private bool matchCase;
    private int searchScope;
    private CancellationTokenSource? searchCancellation;
    private IReadOnlyList<FileNode> tree = [];
    private IReadOnlyList<SearchHit> searchResults = [];
    private string searchSummary = "";
    private readonly AsyncCommand[] commands;

    public EditorViewModel(IEditorDialogs dialogs)
    {
        this.dialogs = dialogs;
        NewCommand = new(() => RunAsync(async () => { if (await MayDiscardAsync()) SetDocument(null); }), () => !IsBusy);
        OpenCommand = new(() => RunAsync(async () =>
        {
            var path = await dialogs.PickFileAsync();
            if (path is not null) await OpenInternalAsync(path);
        }), () => !IsBusy);
        OpenFolderCommand = new(() => RunAsync(async () =>
        {
            var path = await dialogs.PickFolderAsync();
            if (path is not null) await LoadFolderInternalAsync(path);
        }), () => !IsBusy);
        SaveCommand = new(() => RunAsync(async () => { await SaveInternalAsync(false); }), () => !IsBusy);
        SaveAsCommand = new(() => RunAsync(async () => { await SaveInternalAsync(true); }), () => !IsBusy);
        ExportCommand = new(() => RunAsync(ExportInternalAsync), () => !IsBusy);
        RefreshCommand = new(() => RunAsync(async () =>
        {
            if (FolderPath is not null) await LoadFolderInternalAsync(FolderPath);
        }), () => !IsBusy && FolderPath is not null);
        commands = [NewCommand, OpenCommand, OpenFolderCommand, SaveCommand, SaveAsCommand, ExportCommand, RefreshCommand];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? DocumentLoaded;
    public event Action<SearchHit>? NavigateRequested;
    public AsyncCommand NewCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand OpenFolderCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand SaveAsCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public string DocumentText
    {
        get => text;
        set
        {
            if (text == value) return;
            text = value;
            Changed(); Changed(nameof(IsDirty)); Changed(nameof(DisplayName)); Changed(nameof(WindowTitle));
            Changed(nameof(Statistics)); Changed(nameof(SaveState));
            CancelSearch();
        }
    }
    public bool IsDirty => text != savedText;
    public string? CurrentPath => currentPath;
    public string FileName => currentPath is null ? "Без имени.md" : Path.GetFileName(currentPath);
    public string DisplayName => FileName + (IsDirty ? " *" : "");
    public string WindowTitle => $"{DisplayName} | Markdown-редактор";
    public string PathDisplay => currentPath ?? "Новый документ";
    public string SaveState => IsDirty ? "Есть изменения" : currentPath is null ? "Не сохранён" : "Сохранён";
    public string Statistics => $"{text.Length:N0} знаков · {text.Count(c => c == '\n') + 1:N0} строк";
    public string EncodingName => encoding.WebName.ToUpperInvariant();
    public string? FolderPath => folderPath;
    public string FolderName => folderPath is null ? "ФАЙЛЫ" : Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(IsEditable)); foreach (var command in commands) command.Refresh(); } }
    public bool IsEditable => !busy;
    public string Status { get => status; private set { status = value; Changed(); } }
    public IReadOnlyList<FileNode> Tree { get => tree; private set { tree = value; Changed(); } }
    public string Query { get => query; set { if (query == value) return; query = value; Changed(); CancelSearch(); } }
    public bool MatchCase { get => matchCase; set { matchCase = value; Changed(); CancelSearch(); } }
    public int SearchScope { get => searchScope; set { searchScope = value; Changed(); CancelSearch(); } }
    public IReadOnlyList<SearchHit> SearchResults { get => searchResults; private set { searchResults = value; Changed(); } }
    public string SearchSummary { get => searchSummary; private set { searchSummary = value; Changed(); } }

    public Task OpenPathAsync(string path) => RunAsync(() => OpenInternalAsync(path));
    public Task LoadFolderAsync(string path) => RunAsync(() => LoadFolderInternalAsync(path));
    public Task SaveAsync(bool saveAs = false) => RunAsync(async () => { await SaveInternalAsync(saveAs); });
    public Task NewAsync() => RunAsync(async () => { if (await MayDiscardAsync()) SetDocument(null); });
    public Task ExportAsync() => RunAsync(ExportInternalAsync);

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Status = "Операция не выполнена";
            await dialogs.ShowErrorAsync(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task OpenInternalAsync(string path)
    {
        if (SamePath(currentPath, path)) return;
        if (!DocumentFiles.IsMarkdown(path)) throw new IOException("Выберите файл .md или .markdown.");
        if (!await MayDiscardAsync()) return;
        var document = await files.ReadAsync(path);
        SetDocument(document);
        Status = "Файл открыт";
    }

    private void SetDocument(LoadedDocument? document)
    {
        CancelSearch();
        currentPath = document?.Path;
        savedText = document?.Text ?? "";
        text = savedText;
        fingerprint = document?.Fingerprint;
        encoding = document?.Encoding ?? new UTF8Encoding(false, true);
        foreach (var property in new[] { nameof(DocumentText), nameof(CurrentPath), nameof(FileName), nameof(DisplayName), nameof(WindowTitle), nameof(PathDisplay), nameof(IsDirty), nameof(SaveState), nameof(Statistics), nameof(EncodingName) }) Changed(property);
        DocumentLoaded?.Invoke();
    }

    private async Task<bool> MayDiscardAsync()
    {
        if (!IsDirty) return true;
        return await dialogs.ConfirmSaveAsync(FileName) switch
        {
            SaveDecision.Discard => true,
            SaveDecision.Save => await SaveInternalAsync(false),
            _ => false
        };
    }

    public async Task<bool> CanCloseAsync()
    {
        if (IsBusy) return false;
        var result = false;
        await RunAsync(async () => result = await MayDiscardAsync());
        return result;
    }

    private async Task<bool> SaveInternalAsync(bool saveAs)
    {
        var target = currentPath;
        if (saveAs || target is null) target = await dialogs.PickSaveAsync(FileName, false);
        if (target is null) return false;
        if (!DocumentFiles.IsMarkdown(target)) throw new IOException("Имя файла должно заканчиваться на .md или .markdown.");
        var same = SamePath(currentPath, target);
        // Save As may target an existing document; require explicit replacement consent.
        if (!same && File.Exists(target) && !await dialogs.ConfirmOverwriteAsync(Path.GetFileName(target))) return false;
        var snapshot = text;
        string newFingerprint;
        try { newFingerprint = await files.WriteAsync(target, snapshot, encoding, same ? fingerprint : null); }
        catch (FileConflictException)
        {
            if (!await dialogs.ConfirmOverwriteAsync(Path.GetFileName(target))) return false;
            newFingerprint = await files.WriteAsync(target, snapshot, encoding, null, true);
        }
        currentPath = Path.GetFullPath(target);
        fingerprint = newFingerprint;
        savedText = snapshot;
        foreach (var property in new[] { nameof(CurrentPath), nameof(FileName), nameof(DisplayName), nameof(WindowTitle), nameof(PathDisplay), nameof(IsDirty), nameof(SaveState) }) Changed(property);
        Status = "Файл сохранён";
        if (folderPath is not null) await LoadFolderInternalAsync(folderPath);
        return true;
    }

    private async Task LoadFolderInternalAsync(string path)
    {
        var nodes = await Task.Run(() => WorkspaceFiles.ReadTree(path));
        CancelSearch();
        folderPath = Path.GetFullPath(path);
        Tree = nodes;
        Changed(nameof(FolderPath)); Changed(nameof(FolderName));
        RefreshCommand.Refresh();
    }

    private async Task ExportInternalAsync()
    {
        var target = await dialogs.PickSaveAsync(Path.ChangeExtension(FileName, ".html"), true);
        if (target is null) return;
        if (Path.GetExtension(target).ToLowerInvariant() is not (".html" or ".htm")) throw new IOException("Для экспорта выберите расширение .html.");
        if (SamePath(target, currentPath)) throw new IOException("HTML нужно сохранить в отдельный файл.");
        if (File.Exists(target) && !await dialogs.ConfirmOverwriteAsync(Path.GetFileName(target))) return;
        var html = await Task.Run(() => MarkdownDocument.ExportHtml(text, Path.GetFileNameWithoutExtension(FileName), currentPath));
        var temp = target + $".{Guid.NewGuid():N}.tmp";
        try { await File.WriteAllTextAsync(temp, html, new UTF8Encoding(false)); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Status = $"HTML сохранён: {Path.GetFileName(target)}";
    }

    public async Task SearchAsync()
    {
        CancelSearch();
        if (string.IsNullOrEmpty(query)) { SearchSummary = ""; return; }
        if (SearchScope == 1 && folderPath is null) { SearchSummary = "Папка не выбрана"; return; }
        var cts = searchCancellation = new CancellationTokenSource();
        var token = cts.Token;
        var searchText = text;
        var searchQuery = query;
        var searchPath = currentPath;
        var searchFolder = folderPath;
        var caseSensitive = matchCase;
        SearchSummary = "Поиск…";
        try
        {
            FolderSearchResult result;
            if (SearchScope == 1)
                result = await Task.Run(() => WorkspaceFiles.SearchFolderAsync(searchFolder!, searchQuery, caseSensitive, searchPath, searchText, token), token);
            else
            {
                var found = await Task.Run(() => WorkspaceFiles.Find(searchText, searchQuery, searchPath ?? FileName, caseSensitive, 501), token);
                result = new(found.Take(500).ToList(), 0, found.Count > 500);
            }
            if (token.IsCancellationRequested) return;
            SearchResults = result.Hits;
            SearchSummary = $"Совпадений: {result.Hits.Count}{(result.Truncated ? "+" : "")}" +
                (result.SkippedFiles > 0 ? $" · Пропущено файлов: {result.SkippedFiles}" : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!token.IsCancellationRequested) SearchSummary = "Поиск не выполнен: " + ex.Message; }
    }

    public Task NavigateAsync(SearchHit hit) => RunAsync(async () =>
    {
        if (currentPath is null && hit.Path == FileName || SamePath(currentPath, hit.Path))
        { NavigateRequested?.Invoke(hit); return; }
        await OpenInternalAsync(hit.Path);
        if (SamePath(currentPath, hit.Path))
        {
            // Recalculate after opening because a search result can outlive the file's contents.
            var fresh = WorkspaceFiles.Find(text, query, hit.Path, matchCase);
            var nearest = fresh.MinBy(x => Math.Abs(x.Offset - hit.Offset));
            if (nearest is not null) NavigateRequested?.Invoke(nearest);
        }
    });

    private void CancelSearch()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
        SearchResults = [];
    }

    private static bool SamePath(string? a, string? b) => a is not null && b is not null &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() => CancelSearch();
}

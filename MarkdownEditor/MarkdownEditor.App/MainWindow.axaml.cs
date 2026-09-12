using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MarkdownEditor.App;

public partial class MainWindow : Window
{
    private readonly MarkdownPreview preview;
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly IEditorDialogs dialogs;
    private long renderVersion;
    private bool allowClose;
    private bool closingPrompt;
    private bool disposed;
    public EditorViewModel ViewModel { get; }

    public MainWindow() : this(null) { }
    public MainWindow(IEditorDialogs? dialogs)
    {
        InitializeComponent();
        this.dialogs = dialogs ?? new EditorDialogs(this);
        ViewModel = new EditorViewModel(this.dialogs);
        DataContext = ViewModel;
        preview = new MarkdownPreview(PreviewContent, OpenLinkAsync);
        previewTimer.Tick += async (_, _) => { previewTimer.Stop(); await RefreshPreviewAsync(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); if (SearchPanel.IsVisible) await ViewModel.SearchAsync(); };
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.DocumentLoaded += () =>
        {
            Editor.IsUndoEnabled = false;
            Editor.IsUndoEnabled = true;
            Editor.CaretIndex = 0;
            PreviewScroller.Offset = default;
            SchedulePreview();
        };
        ViewModel.NavigateRequested += hit =>
        {
            if (PreviewMode.IsChecked == true) { SplitMode.IsChecked = true; ApplyMode(); }
            Editor.Focus();
            Editor.SelectionStart = Math.Min(hit.Offset, ViewModel.DocumentText.Length);
            Editor.SelectionEnd = Math.Min(hit.Offset + hit.Length, ViewModel.DocumentText.Length);
            Editor.ScrollToLine(hit.Line - 1);
        };
        Editor.PropertyChanged += (_, e) => { if (e.Property == TextBox.CaretIndexProperty) UpdateCaret(); };
        PreviewPane.SizeChanged += (_, _) => SchedulePreview();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            disposed = true; renderVersion++; previewTimer.Stop(); searchTimer.Stop();
            ViewModel.PropertyChanged -= OnViewModelChanged; ViewModel.Dispose(); preview.Dispose();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.DocumentText) or nameof(EditorViewModel.CurrentPath))
        { SchedulePreview(); UpdateCaret(); }
        if (e.PropertyName is nameof(EditorViewModel.Query) or nameof(EditorViewModel.MatchCase) or nameof(EditorViewModel.SearchScope) or nameof(EditorViewModel.DocumentText) or nameof(EditorViewModel.FolderPath))
        { searchTimer.Stop(); searchTimer.Start(); }
    }

    private void SchedulePreview() { renderVersion++; previewTimer.Stop(); previewTimer.Start(); }

    public async Task RefreshPreviewAsync()
    {
        var version = ++renderVersion;
        var text = ViewModel.DocumentText;
        try
        {
            var document = await Task.Run(() => MarkdownDocument.Parse(text));
            if (disposed || version != renderVersion) return;
            preview.Render(document, ViewModel.CurrentPath, PreviewPane.Bounds.Width);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            if (disposed || version != renderVersion) return;
            PreviewContent.Children.Clear();
            PreviewContent.Children.Add(new TextBlock { Text = "Не удалось построить предпросмотр: " + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
    }

    private async void FileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (FileTree.SelectedItem is FileNode { IsDirectory: false } node) await ViewModel.OpenPathAsync(node.FullPath);
        // Clear selection so a cancelled open can be retried with the same file.
        if (FileTree.SelectedItem is FileNode { IsDirectory: false }) FileTree.SelectedItem = null;
    }

    private async void SearchSelected(object? sender, SelectionChangedEventArgs e)
    { if (ResultsList.SelectedItem is SearchHit hit) { await ViewModel.NavigateAsync(hit); ResultsList.SelectedItem = null; } }

    private void FindClick(object? sender, RoutedEventArgs e) => ShowSearch();
    private void ShowSearch()
    {
        SearchPanel.IsVisible = true;
        if (!string.IsNullOrWhiteSpace(Editor.SelectedText) && !Editor.SelectedText.Contains('\n')) ViewModel.Query = Editor.SelectedText;
        SearchBox.Focus(); SearchBox.SelectAll();
        searchTimer.Stop(); searchTimer.Start();
    }
    private void CloseSearchClick(object? sender, RoutedEventArgs e) { SearchPanel.IsVisible = false; Editor.Focus(); }
    private void UndoClick(object? sender, RoutedEventArgs e) { if (ViewModel.IsEditable) Editor.Undo(); }
    private void RedoClick(object? sender, RoutedEventArgs e) { if (ViewModel.IsEditable) Editor.Redo(); }
    private void ExitClick(object? sender, RoutedEventArgs e) => Close();
    private void ModeClick(object? sender, RoutedEventArgs e) { if (DocumentGrid is not null) ApplyMode(); }

    private void ApplyMode()
    {
        var edit = EditorMode.IsChecked == true;
        var read = PreviewMode.IsChecked == true;
        EditorPane.IsVisible = !read;
        PreviewPane.IsVisible = !edit;
        DocumentSplitter.IsVisible = !edit && !read;
        DocumentGrid.ColumnDefinitions = new ColumnDefinitions(read ? "0,0,*" : edit ? "*,0,0" : "*,5,*");
        if (!edit && !read)
        {
            DocumentGrid.ColumnDefinitions[0].MinWidth = 280;
            DocumentGrid.ColumnDefinitions[2].MinWidth = 280;
        }
        SchedulePreview();
    }

    private void FormatClick(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string action }) Format(action); }
    private void Format(string action)
    {
        if (!ViewModel.IsEditable) return;
        if (PreviewMode.IsChecked == true) { SplitMode.IsChecked = true; ApplyMode(); }
        var start = Math.Min(Editor.SelectionStart, Editor.SelectionEnd);
        var end = Math.Max(Editor.SelectionStart, Editor.SelectionEnd);
        var selected = Editor.SelectedText ?? "";
        var (prefix, suffix, placeholder) = action switch
        {
            "bold" => ("**", "**", "текст"), "italic" => ("*", "*", "текст"),
            "code" => ("`", "`", "код"), "link" => ("[", "](https://example.com)", "ссылка"),
            "heading" => ("# ", "", "Заголовок"), "quote" => ("> ", "", "Цитата"),
            _ => ("- ", "", "Пункт")
        };
        if (action is "heading" or "quote" or "list")
        {
            var lineStart = start == 0 ? 0 : ViewModel.DocumentText.LastIndexOf('\n', start - 1) + 1;
            Editor.SelectionStart = start = lineStart;
            Editor.SelectionEnd = end;
            selected = Editor.SelectedText ?? "";
            if (selected.Length > 0) selected = selected.Replace("\n", "\n" + prefix);
        }
        if (selected.Length == 0) selected = placeholder;
        Editor.SelectedText = prefix + selected + suffix;
        Editor.SelectionStart = start + prefix.Length;
        Editor.SelectionEnd = start + prefix.Length + selected.Length;
        Editor.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.F: ShowSearch(); e.Handled = true; break;
                case Key.B: if (Editor.IsFocused) { Format("bold"); e.Handled = true; } break;
                case Key.I: if (Editor.IsFocused) { Format("italic"); e.Handled = true; } break;
            }
        }
        if (e.Key == Key.Escape && SearchPanel.IsVisible) { SearchPanel.IsVisible = false; Editor.Focus(); e.Handled = true; }
    }

    private void UpdateCaret()
    {
        var text = ViewModel.DocumentText;
        var index = Math.Min(Editor.CaretIndex, text.Length);
        var line = 1; var start = 0;
        for (var i = 0; i < index; i++) if (text[i] == '\n') { line++; start = i + 1; }
        CaretStatus.Text = $"Стр. {line}, стлб. {index - start + 1}";
    }

    private async Task OpenLinkAsync(string target)
    {
        if (target.StartsWith('#')) { preview.ScrollToAnchor(target); return; }
        try
        {
            Uri? uri = null;
            if (Uri.TryCreate(target, UriKind.Absolute, out var absolute)) uri = absolute;
            else if (ViewModel.CurrentPath is not null) uri = new Uri(new Uri(ViewModel.CurrentPath), target);
            if (uri is null) return;
            if (uri.IsFile && DocumentFiles.IsMarkdown(uri.LocalPath)) await ViewModel.OpenPathAsync(uri.LocalPath);
            else if (uri.Scheme is "https" or "http" or "mailto") await Launcher.LaunchUriAsync(uri);
            else await dialogs.ShowErrorAsync("Эта ссылка не поддерживается. Можно открыть Markdown-файл или веб-страницу.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        { await dialogs.ShowErrorAsync(ex.Message); }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closingPrompt) return;
        closingPrompt = true;
        try { if (await ViewModel.CanCloseAsync()) { allowClose = true; Close(); } }
        finally { closingPrompt = false; }
    }
}

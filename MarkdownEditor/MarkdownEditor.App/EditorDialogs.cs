using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace MarkdownEditor.App;

public sealed class EditorDialogs(Window owner) : IEditorDialogs
{
    private static readonly FilePickerFileType MarkdownType = new("Markdown") { Patterns = ["*.md", "*.markdown"] };
    private static readonly FilePickerFileType HtmlType = new("HTML") { Patterns = ["*.html", "*.htm"] };

    public async Task<string?> PickFileAsync() => (await owner.StorageProvider.OpenFilePickerAsync(new()
    { Title = "Открыть Markdown", AllowMultiple = false, FileTypeFilter = [MarkdownType] })).FirstOrDefault()?.TryGetLocalPath();

    public async Task<string?> PickFolderAsync() => (await owner.StorageProvider.OpenFolderPickerAsync(new()
    { Title = "Открыть папку", AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();

    public async Task<string?> PickSaveAsync(string suggestedName, bool html) => (await owner.StorageProvider.SaveFilePickerAsync(new()
    {
        Title = html ? "Экспорт в HTML" : "Сохранить Markdown", SuggestedFileName = suggestedName,
        DefaultExtension = html ? "html" : "md", FileTypeChoices = [html ? HtmlType : MarkdownType], ShowOverwritePrompt = false
    }))?.TryGetLocalPath();

    public Task<SaveDecision> ConfirmSaveAsync(string name) => ShowAsync("Несохранённые изменения",
        $"Сохранить изменения в «{name}»?", SaveDecision.Cancel,
        [("Отмена", SaveDecision.Cancel), ("Не сохранять", SaveDecision.Discard), ("Сохранить", SaveDecision.Save)]);

    public Task<bool> ConfirmOverwriteAsync(string name) => ShowAsync("Подтверждение замены",
        $"Файл «{name}» уже существует или изменён на диске. Заменить его текущим содержимым?", false,
        [("Отмена", false), ("Заменить", true)]);

    public async Task ShowErrorAsync(string message) => await ShowAsync("Не удалось выполнить действие", message, false, [("Закрыть", false)]);

    private Task<T> ShowAsync<T>(string title, string text, T cancel, (string Text, T Value)[] choices)
    {
        var dialog = new Window
        {
            Title = title, Width = 470, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (label, value) in choices)
        {
            var button = new Button { Content = label, Padding = new Thickness(14, 7) };
            button.Click += (_, _) => dialog.Close(value);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22), Spacing = 22,
            Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons }
        };
        dialog.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) dialog.Close(cancel); };
        // The title-bar close button must return Cancel rather than the enum's default Save.
        return AwaitResult();
        async Task<T> AwaitResult()
        {
            var result = await dialog.ShowDialog<object?>(owner);
            return result is T value ? value : cancel;
        }
    }
}

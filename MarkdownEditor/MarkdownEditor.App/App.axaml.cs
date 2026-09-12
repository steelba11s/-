using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MarkdownEditor.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                var path = desktop.Args?.FirstOrDefault();
                if (path is not null) await window.ViewModel.OpenPathAsync(path);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace TypingTrainer;

public partial class MainWindow : Window
{
    private readonly AppDataStore _store;
    private readonly TypingSession _session = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<char, Border> _keys = new();
    private readonly List<TypingSessionStats> _statistics = [];
    private TrainerSettings _settings = new();
    private Border? _shiftKey;
    private bool _updatingInput;
    private static readonly IBrush CorrectBrush = Brush.Parse("#167552");
    private static readonly IBrush ErrorBrush = Brush.Parse("#C22E40");
    private static readonly IBrush CurrentBrush = Brush.Parse("#1768BD");

    public MainWindow() : this(new AppDataStore()) { }

    public MainWindow(AppDataStore store)
    {
        _store = store;
        InitializeComponent();
        string? loadError = null;
        try
        {
            _settings = _store.LoadSettings();
            _statistics.AddRange(_store.LoadStatistics());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loadError = "Не удалось загрузить данные: " + ex.Message;
        }

        TextCombo.ItemsSource = _store.LoadDictionaries();
        TextCombo.SelectionChanged += (_, _) => ResetSession();
        TextCombo.SelectedIndex = 0;
        FontSizeSlider.Value = _settings.FontSize;
        ShowKeyboardInput.IsChecked = _settings.ShowKeyboard;
        CaseSensitiveInput.IsChecked = _settings.CaseSensitive;
        FontSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) FontSizeValue.Text = $"{FontSizeSlider.Value:F0}";
        };
        StartButton.Click += (_, _) => StartSession();
        ResetButton.Click += (_, _) => ResetSession();
        InputBox.TextChanged += (_, _) => OnInputChanged();
        SaveSettingsButton.Click += (_, _) => SaveSettings();
        ClearStatisticsButton.Click += (_, _) => ClearConfirmation.IsVisible = true;
        CancelClearButton.Click += (_, _) => ClearConfirmation.IsVisible = false;
        ConfirmClearButton.Click += (_, _) => ClearStatistics();
        OpenNotebookButton.Click += (_, _) => OpenNotebook();
        _timer.Tick += (_, _) => UpdateMetrics();
        Closed += (_, _) => _timer.Stop();
        ApplySettings();
        RefreshStatistics();
        if (loadError != null) StatusText.Text = loadError;
    }

    private void StartSession()
    {
        if (TextCombo.SelectedItem is not WordDictionary text) return;
        _updatingInput = true;
        InputBox.Text = "";
        _updatingInput = false;
        _session.Start(text, _settings.CaseSensitive);
        InputBox.IsReadOnly = false;
        TextCombo.IsEnabled = false;
        CaseSensitiveInput.IsEnabled = false;
        StartButton.Content = "Заново";
        StatusText.Text = text.Name;
        _timer.Start();
        RenderTarget();
        UpdateMetrics();
        InputBox.Focus();
    }

    private void ResetSession()
    {
        _timer.Stop();
        _session.Reset();
        _updatingInput = true;
        InputBox.Text = "";
        _updatingInput = false;
        InputBox.IsReadOnly = true;
        TextCombo.IsEnabled = true;
        CaseSensitiveInput.IsEnabled = true;
        StartButton.Content = "Начать";
        StatusText.Text = "";
        TargetScroller.Offset = default;
        BuildKeyboard();
        RenderTarget();
        UpdateMetrics();
    }

    private void OnInputChanged()
    {
        if (_updatingInput || !_session.IsActive) return;
        var finished = _session.UpdateInput(InputBox.Text ?? "");
        RenderTarget();
        UpdateMetrics();
        if (!finished) return;

        _timer.Stop();
        InputBox.IsReadOnly = true;
        TextCombo.IsEnabled = true;
        CaseSensitiveInput.IsEnabled = true;
        StartButton.Content = "Начать";
        var stats = _session.GetStatistics();
        _statistics.Insert(0, stats);
        if (_statistics.Count > 500) _statistics.RemoveRange(500, _statistics.Count - 500);
        StatusText.Text = $"Готово: {stats.CharactersPerMinute:F0} зн/мин, точность {stats.Accuracy:F1}%.";
        TrySave(() => _store.SaveStatistics(_statistics));
        RefreshStatistics();
    }

    private void UpdateMetrics()
    {
        var stats = _session.GetStatistics();
        var elapsed = _session.Elapsed;
        TimeMetric.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        SpeedMetric.Text = $"{stats.CharactersPerMinute:F0}";
        AccuracyMetric.Text = $"{stats.Accuracy:F0}%";
        ErrorsMetric.Text = stats.Errors.ToString();
    }

    private void RenderTarget()
    {
        var text = _session.Target.Length > 0 ? _session.Target : (TextCombo.SelectedItem as WordDictionary)?.Text ?? "";
        TargetBlock.Inlines!.Clear();
        for (var i = 0; i < text.Length; i++)
        {
            var run = new Run(text[i].ToString());
            if (i < _session.Input.Length)
                run.Foreground = _session.Matches(_session.Input[i], text[i]) ? CorrectBrush : ErrorBrush;
            else if (_session.IsActive && i == _session.Input.Length)
            {
                run.Foreground = CurrentBrush;
                run.TextDecorations = TextDecorations.Underline;
            }
            TargetBlock.Inlines.Add(run);
        }
        HighlightNextKey();
        // Scroll after layout so the caret rectangle reflects wrapping and font changes.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_session.IsActive) return;
            var caret = TargetBlock.TextLayout.HitTestTextPosition(_session.Input.Length);
            TargetBlock.BringIntoView(caret);
        }, DispatcherPriority.Loaded);
    }

    private void SaveSettings()
    {
        var settings = new TrainerSettings
        {
            FontSize = (float)FontSizeSlider.Value,
            ShowKeyboard = ShowKeyboardInput.IsChecked == true,
            CaseSensitive = _session.IsActive ? _settings.CaseSensitive : CaseSensitiveInput.IsChecked == true
        };
        if (!TrySave(() => _store.SaveSettings(settings))) return;
        _settings = settings;
        ApplySettings();
        StatusText.Text = "Настройки сохранены.";
    }

    private void ApplySettings()
    {
        TargetBlock.FontSize = _settings.FontSize;
        InputBox.FontSize = _settings.FontSize;
        FontSizeValue.Text = $"{_settings.FontSize:F0}";
        KeyboardPanel.IsVisible = _settings.ShowKeyboard;
        RenderTarget();
    }

    private void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear();
        _keys.Clear();
        var english = (TextCombo.SelectedItem as WordDictionary)?.Name.StartsWith("Text", StringComparison.Ordinal) == true;
        var rows = english
            ? new[] { "1234567890-", "qwertyuiop[]", "asdfghjkl;'", "zxcvbnm,./" }
            : new[] { "1234567890-", "йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю." };
        foreach (var letters in rows)
        {
            var row = new Grid();
            foreach (var c in letters)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                var key = CreateKey(char.ToUpperInvariant(c).ToString());
                Grid.SetColumn(key, row.Children.Count);
                row.Children.Add(key);
                _keys[c] = key;
            }
            KeyboardPanel.Children.Add(row);
        }
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,3*,*") };
        _shiftKey = CreateKey("Shift");
        var space = CreateKey("Пробел");
        var back = CreateKey("Backspace");
        Grid.SetColumn(space, 1);
        Grid.SetColumn(back, 2);
        bottom.Children.Add(_shiftKey);
        bottom.Children.Add(space);
        bottom.Children.Add(back);
        _keys[' '] = space;
        if (!english) _keys[','] = _keys['.'];
        KeyboardPanel.Children.Add(bottom);
    }

    private static Border CreateKey(string label) => new()
    {
        Height = 32, Margin = new Thickness(2, 0), CornerRadius = new CornerRadius(4),
        Background = Brushes.White, BorderBrush = Brush.Parse("#D8DCE2"), BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = label, FontSize = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        }
    };

    private void HighlightNextKey()
    {
        foreach (var key in _keys.Values) key.Background = Brushes.White;
        if (_shiftKey != null) _shiftKey.Background = Brushes.White;
        if (!_session.IsActive || _session.Input.Length >= _session.Target.Length) return;
        var c = _session.Target[_session.Input.Length];
        if (_keys.TryGetValue(char.ToLowerInvariant(c), out var next)) next.Background = Brush.Parse("#C8EBDD");
        var english = (TextCombo.SelectedItem as WordDictionary)?.Name.StartsWith("Text", StringComparison.Ordinal) == true;
        if (_shiftKey != null && ((_session.CaseSensitive && char.IsUpper(c)) || (!english && c == ',')))
            _shiftKey.Background = Brush.Parse("#C8EBDD");
    }

    private void RefreshStatistics()
    {
        SummaryText.Text = _statistics.Count == 0 ? "Пока нет завершённых попыток." :
            $"Попыток: {_statistics.Count}\nЛучший результат: {_statistics.Max(s => s.CharactersPerMinute):F0} зн/мин\nСредняя точность: {_statistics.Average(s => s.Accuracy):F1}%";
        StatisticsList.ItemsSource = _statistics.OrderByDescending(s => s.Date).Select(s =>
            $"{s.Date:dd.MM.yyyy HH:mm} · {s.DictionaryName}\n{s.CharactersPerMinute:F0} зн/мин · {s.Accuracy:F1}% · ошибок: {s.Errors}").ToArray();
        ClearStatisticsButton.IsEnabled = _statistics.Count > 0;
    }

    private void ClearStatistics()
    {
        if (!TrySave(() => _store.SaveStatistics([]))) return;
        _statistics.Clear();
        ClearConfirmation.IsVisible = false;
        RefreshStatistics();
        StatusText.Text = "Статистика очищена.";
    }

    private bool TrySave(Action save)
    {
        try { save(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Не удалось сохранить данные: " + ex.Message;
            return false;
        }
    }

    private void OpenNotebook()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "typing-trainer-notebook.ipynb");
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Файл notebook не найден.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "Не удалось открыть notebook. Откройте его в VS Code с Polyglot Notebooks. " + ex.Message;
        }
    }
}

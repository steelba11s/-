using System.ComponentModel;
using System.Diagnostics;

namespace TypingTrainer;

public partial class Form1 : Form
{
    private readonly AppDataStore _store = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Dictionary<string, Button> _keyboardButtons = new(StringComparer.OrdinalIgnoreCase);

    private List<WordDictionary> _texts = [];
    private List<TypingSessionStats> _statistics = [];
    private TrainerSettings _settings = new();
    private string _targetText = "";
    private string _activeTextName = "";
    private bool _sessionActive;

    private ComboBox _textCombo = null!;
    private Button _startButton = null!;
    private Label _timerLabel = null!;
    private Label _speedLabel = null!;
    private Label _accuracyLabel = null!;
    private Label _errorsLabel = null!;
    private RichTextBox _targetBox = null!;
    private TextBox _inputBox = null!;
    private TableLayoutPanel _keyboardPanel = null!;
    private NumericUpDown _fontSizeInput = null!;
    private CheckBox _showKeyboardInput = null!;
    private CheckBox _caseSensitiveInput = null!;
    private DataGridView _statsGrid = null!;
    private Label _summaryLabel = null!;

    public Form1()
    {
        InitializeComponent();
        LoadData();
        BuildInterface();
        ApplySettings();
        RefreshTextList();
        RefreshStatistics();

        _timer.Interval = 250;
        _timer.Tick += (_, _) => UpdateLiveStats();
    }

    private void LoadData()
    {
        _texts = _store.LoadDictionaries();
        _settings = _store.LoadSettings();
        _statistics = _store.LoadStatistics();
    }

    private void BuildInterface()
    {
        Text = "Тренажёр набора текста на клавиатуре";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1240, 780);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        Controls.Add(root);

        root.Controls.Add(BuildTrainerPanel(), 0, 0);
        root.Controls.Add(BuildSideTabs(), 1, 0);
    }

    private Control BuildTrainerPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        header.Controls.Add(new Label
        {
            Text = "Текст",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(FontFamily.GenericSansSerif, 12f, FontStyle.Bold)
        }, 0, 0);

        _textCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(FontFamily.GenericSansSerif, 12f)
        };
        header.Controls.Add(_textCombo, 1, 0);

        _startButton = new Button
        {
            Text = "Начать",
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericSansSerif, 11f, FontStyle.Bold)
        };
        _startButton.Click += (_, _) => StartSession();
        header.Controls.Add(_startButton, 2, 0);

        var resetButton = new Button
        {
            Text = "Сброс",
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericSansSerif, 11f)
        };
        resetButton.Click += (_, _) => ResetSession();
        header.Controls.Add(resetButton, 3, 0);
        panel.Controls.Add(header, 0, 0);

        var liveStats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1
        };
        for (var i = 0; i < 4; i++)
        {
            liveStats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        _timerLabel = CreateMetricLabel("Время: 0:00");
        _speedLabel = CreateMetricLabel("Скорость: 0 зн/мин");
        _accuracyLabel = CreateMetricLabel("Точность: 100%");
        _errorsLabel = CreateMetricLabel("Ошибки: 0");
        liveStats.Controls.Add(_timerLabel, 0, 0);
        liveStats.Controls.Add(_speedLabel, 1, 0);
        liveStats.Controls.Add(_accuracyLabel, 2, 0);
        liveStats.Controls.Add(_errorsLabel, 3, 0);
        panel.Controls.Add(liveStats, 0, 1);

        _targetBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            DetectUrls = false,
            HideSelection = false
        };
        panel.Controls.Add(_targetBox, 0, 2);

        _inputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Нажмите «Начать» и набирайте текст здесь..."
        };
        _inputBox.TextChanged += (_, _) => OnInputChanged();
        _inputBox.KeyDown += (_, e) => HighlightPressedKey(e.KeyCode);
        _inputBox.KeyUp += (_, _) => HighlightNextKey();
        panel.Controls.Add(_inputBox, 0, 3);

        _keyboardPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 8, 0, 0)
        };
        for (var i = 0; i < 4; i++)
        {
            _keyboardPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }

        BuildKeyboardRows();
        panel.Controls.Add(_keyboardPanel, 0, 4);
        return panel;
    }

    private Control BuildSideTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.TabPages.Add(BuildStatsTab());
        return tabs;
    }

    private TabPage BuildSettingsTab()
    {
        var tab = new TabPage("Настройки");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(10)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        _fontSizeInput = new NumericUpDown
        {
            Minimum = 10,
            Maximum = 30,
            DecimalPlaces = 0,
            Dock = DockStyle.Fill,
            Value = (decimal)_settings.FontSize
        };
        _showKeyboardInput = new CheckBox
        {
            Text = "Показывать клавиатуру",
            Dock = DockStyle.Fill,
            Checked = _settings.ShowKeyboard
        };
        _caseSensitiveInput = new CheckBox
        {
            Text = "Учитывать регистр",
            Dock = DockStyle.Fill,
            Checked = _settings.CaseSensitive
        };

        AddSettingRow(panel, 0, "Размер шрифта", _fontSizeInput);
        panel.Controls.Add(_showKeyboardInput, 0, 1);
        panel.SetColumnSpan(_showKeyboardInput, 2);
        panel.Controls.Add(_caseSensitiveInput, 0, 2);
        panel.SetColumnSpan(_caseSensitiveInput, 2);

        var saveButton = new Button
        {
            Text = "Применить",
            Dock = DockStyle.Fill,
            Height = 38
        };
        saveButton.Click += (_, _) => SaveSettings();
        panel.Controls.Add(saveButton, 0, 3);
        panel.SetColumnSpan(saveButton, 2);

        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildStatsTab()
    {
        var tab = new TabPage("Статистика");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _summaryLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(FontFamily.GenericSansSerif, 10.5f, FontStyle.Bold)
        };
        panel.Controls.Add(_summaryLabel, 0, 0);

        _statsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false
        };
        panel.Controls.Add(_statsGrid, 0, 1);

        var clearButton = new Button
        {
            Text = "Очистить статистику",
            Dock = DockStyle.Fill
        };
        clearButton.Click += (_, _) => ClearStatistics();
        panel.Controls.Add(clearButton, 0, 2);

        tab.Controls.Add(panel);
        return tab;
    }

    private static Label CreateMetricLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(FontFamily.GenericSansSerif, 10.5f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static void AddSettingRow(TableLayoutPanel panel, int row, string label, Control input)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        panel.Controls.Add(input, 1, row);
    }

    private void BuildKeyboardRows()
    {
        _keyboardPanel.Controls.Clear();
        _keyboardButtons.Clear();

        AddKeyboardRow([("Й/Q", "ЙQ"), ("Ц/W", "ЦW"), ("У/E", "УE"), ("К/R", "КR"), ("Е/T", "ЕT"), ("Н/Y", "НY"), ("Г/U", "ГU"), ("Ш/I", "ШI"), ("Щ/O", "ЩO"), ("З/P", "ЗP"), ("Х", "Х"), ("Ъ", "Ъ")]);
        AddKeyboardRow([("Ф/A", "ФA"), ("Ы/S", "ЫS"), ("В/D", "ВD"), ("А/F", "АF"), ("П/G", "ПG"), ("Р/H", "РH"), ("О/J", "ОJ"), ("Л/K", "ЛK"), ("Д/L", "ДL"), ("Ж", "Ж"), ("Э", "Э")]);
        AddKeyboardRow([("Я/Z", "ЯZ"), ("Ч/X", "ЧX"), ("С/C", "СC"), ("М/V", "МV"), ("И/B", "ИB"), ("Т/N", "ТN"), ("Ь/M", "ЬM"), ("Б", "Б"), ("Ю", "Ю"), (".", "."), (",", ",")]);
        AddKeyboardRow([("Shift", "Shift"), ("Ctrl", "Ctrl"), ("Alt", "Alt"), ("Пробел", " "), ("Enter", "Enter"), ("Backspace", "Backspace")]);
    }

    private void AddKeyboardRow((string Label, string Aliases)[] keys)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = keys.Length,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 2)
        };

        foreach (var key in keys)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / keys.Length));
            var button = new Button
            {
                Text = key.Label,
                Dock = DockStyle.Fill,
                Enabled = false,
                Margin = new Padding(2),
                BackColor = Color.FromArgb(245, 238, 232),
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            row.Controls.Add(button);

            foreach (var alias in key.Aliases)
            {
                _keyboardButtons[alias.ToString()] = button;
            }
            _keyboardButtons[key.Label] = button;
        }

        _keyboardPanel.Controls.Add(row);
    }

    private void RefreshTextList()
    {
        var selectedId = _textCombo.SelectedItem is WordDictionary selected ? selected.Id : Guid.Empty;
        var texts = _texts.OrderBy(text => text.Name).ToList();

        _textCombo.BeginUpdate();
        _textCombo.Items.Clear();
        _textCombo.Items.AddRange(texts.Cast<object>().ToArray());
        _textCombo.EndUpdate();

        if (selectedId != Guid.Empty)
        {
            var index = _textCombo.Items.Cast<WordDictionary>().ToList().FindIndex(item => item.Id == selectedId);
            if (index >= 0)
            {
                _textCombo.SelectedIndex = index;
            }
        }

        if (_textCombo.SelectedIndex < 0 && _textCombo.Items.Count > 0)
        {
            _textCombo.SelectedIndex = 0;
        }
    }

    private void SaveSettings()
    {
        _settings.FontSize = (float)_fontSizeInput.Value;
        _settings.ShowKeyboard = _showKeyboardInput.Checked;
        _settings.CaseSensitive = _caseSensitiveInput.Checked;

        _store.SaveSettings(_settings);
        ApplySettings();
    }

    private void ApplySettings()
    {
        _targetBox.Font = new Font(FontFamily.GenericSansSerif, _settings.FontSize);
        _inputBox.Font = new Font(FontFamily.GenericSansSerif, _settings.FontSize);
        _keyboardPanel.Visible = _settings.ShowKeyboard;

        ApplyThemeRecursive(this, Color.FromArgb(248, 248, 248), Color.White, Color.FromArgb(35, 35, 35));
        _targetBox.BackColor = Color.White;
        _targetBox.ForeColor = Color.FromArgb(35, 35, 35);
        _inputBox.BackColor = Color.White;
        _inputBox.ForeColor = Color.FromArgb(35, 35, 35);
        RenderTargetText();
    }

    private void ApplyThemeRecursive(Control control, Color background, Color surface, Color text)
    {
        control.BackColor = control is TextBoxBase or ComboBox or DataGridView ? surface : background;
        control.ForeColor = text;

        foreach (Control child in control.Controls)
        {
            ApplyThemeRecursive(child, background, surface, text);
        }

        ResetKeyboardHighlight();
    }

    private void StartSession()
    {
        if (_textCombo.SelectedItem is not WordDictionary selectedText || string.IsNullOrWhiteSpace(selectedText.Text))
        {
            MessageBox.Show("Выберите текст.", "Старт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _activeTextName = selectedText.Name;
        _targetText = selectedText.Text;
        _inputBox.Clear();
        _inputBox.ReadOnly = false;
        _sessionActive = true;
        _stopwatch.Restart();
        _timer.Start();
        _startButton.Text = "Заново";
        RenderTargetText();
        UpdateLiveStats();
        _inputBox.Focus();
    }

    private void ResetSession()
    {
        _sessionActive = false;
        _timer.Stop();
        _stopwatch.Reset();
        _targetText = "";
        _activeTextName = "";
        _inputBox.Clear();
        _inputBox.ReadOnly = false;
        _targetBox.Clear();
        _startButton.Text = "Начать";
        UpdateLiveStats();
        ResetKeyboardHighlight();
    }

    private void OnInputChanged()
    {
        if (!_sessionActive)
        {
            return;
        }

        RenderTargetText();
        UpdateLiveStats();

        if (_inputBox.Text.Length >= _targetText.Length)
        {
            FinishSession();
        }
    }

    private void FinishSession()
    {
        if (!_sessionActive)
        {
            return;
        }

        _sessionActive = false;
        _timer.Stop();
        _stopwatch.Stop();
        _inputBox.ReadOnly = true;
        _startButton.Text = "Начать";

        var stats = CalculateStats();
        _statistics.Insert(0, stats);
        _store.SaveStatistics(_statistics);
        RefreshStatistics();

        MessageBox.Show(
            $"Готово!\nСкорость: {stats.WordsPerMinute:F1} зн/мин\nТочность: {stats.Accuracy:F1}%\nОшибки: {stats.Errors}",
            "Результат",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private TypingSessionStats CalculateStats()
    {
        var input = _inputBox.Text;
        var length = Math.Min(input.Length, _targetText.Length);
        var correct = 0;
        var errors = 0;

        for (var i = 0; i < length; i++)
        {
            if (CharsEqual(input[i], _targetText[i]))
            {
                correct++;
            }
            else
            {
                errors++;
            }
        }

        if (input.Length < _targetText.Length)
        {
            errors += _targetText.Length - input.Length;
        }
        else if (input.Length > _targetText.Length)
        {
            errors += input.Length - _targetText.Length;
        }

        var seconds = Math.Max(_stopwatch.Elapsed.TotalSeconds, 1);
        var typedChars = Math.Max(input.Length, 1);
        return new TypingSessionStats
        {
            Date = DateTime.Now,
            DictionaryName = _activeTextName,
            Characters = input.Length,
            CorrectCharacters = correct,
            Errors = errors,
            Seconds = seconds,
            Accuracy = correct * 100d / typedChars,
            WordsPerMinute = correct / seconds * 60d
        };
    }

    private bool CharsEqual(char left, char right)
    {
        return _settings.CaseSensitive
            ? left == right
            : char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
    }

    private void UpdateLiveStats()
    {
        var elapsed = _stopwatch.Elapsed;
        _timerLabel.Text = $"Время: {elapsed.Minutes}:{elapsed.Seconds:00}";

        if (!_sessionActive && _stopwatch.Elapsed == TimeSpan.Zero)
        {
            _speedLabel.Text = "Скорость: 0 зн/мин";
            _accuracyLabel.Text = "Точность: 100%";
            _errorsLabel.Text = "Ошибки: 0";
            return;
        }

        var stats = CalculateStats();
        _speedLabel.Text = $"Скорость: {stats.WordsPerMinute:F0} зн/мин";
        _accuracyLabel.Text = $"Точность: {stats.Accuracy:F0}%";
        _errorsLabel.Text = $"Ошибки: {stats.Errors}";
    }

    private void RenderTargetText()
    {
        if (_targetBox is null)
        {
            return;
        }

        var input = _inputBox?.Text ?? "";
        var baseColor = Color.FromArgb(35, 35, 35);
        var correctColor = Color.FromArgb(20, 140, 72);
        var wrongColor = Color.FromArgb(200, 55, 55);
        var currentColor = Color.FromArgb(255, 236, 150);

        _targetBox.SuspendLayout();
        _targetBox.Clear();
        _targetBox.Font = new Font(FontFamily.GenericSansSerif, _settings.FontSize);

        for (var i = 0; i < _targetText.Length; i++)
        {
            _targetBox.SelectionStart = _targetBox.TextLength;
            _targetBox.SelectionLength = 0;
            _targetBox.SelectionBackColor = i == input.Length ? currentColor : _targetBox.BackColor;

            if (i < input.Length)
            {
                _targetBox.SelectionColor = CharsEqual(input[i], _targetText[i]) ? correctColor : wrongColor;
            }
            else
            {
                _targetBox.SelectionColor = baseColor;
            }

            _targetBox.AppendText(_targetText[i].ToString());
        }

        _targetBox.SelectionStart = 0;
        _targetBox.SelectionLength = 0;
        _targetBox.ResumeLayout();
        HighlightNextKey();
    }

    private void HighlightPressedKey(Keys keyCode)
    {
        ResetKeyboardHighlight();
        var key = keyCode switch
        {
            Keys.Space => " ",
            Keys.Enter => "Enter",
            Keys.Back => "Backspace",
            Keys.ShiftKey => "Shift",
            Keys.ControlKey => "Ctrl",
            Keys.Menu => "Alt",
            _ => ""
        };

        if (!string.IsNullOrEmpty(key) && _keyboardButtons.TryGetValue(key, out var button))
        {
            button.BackColor = Color.FromArgb(128, 189, 255);
        }
    }

    private void HighlightNextKey()
    {
        ResetKeyboardHighlight();
        var inputLength = _inputBox?.Text.Length ?? 0;
        if (inputLength >= _targetText.Length)
        {
            return;
        }

        var key = _targetText[inputLength] == ' '
            ? " "
            : _targetText[inputLength].ToString().ToUpperInvariant();

        if (_keyboardButtons.TryGetValue(key, out var button))
        {
            button.BackColor = Color.FromArgb(179, 232, 196);
        }
    }

    private void ResetKeyboardHighlight()
    {
        foreach (var button in _keyboardButtons.Values.Distinct())
        {
            button.BackColor = Color.FromArgb(245, 238, 232);
            button.ForeColor = Color.FromArgb(50, 50, 50);
        }
    }

    private void RefreshStatistics()
    {
        var total = _statistics.Count;
        var bestSpeed = total == 0 ? 0 : _statistics.Max(item => item.WordsPerMinute);
        var averageAccuracy = total == 0 ? 0 : _statistics.Average(item => item.Accuracy);
        _summaryLabel.Text = $"Сессий: {total}\nЛучший результат: {bestSpeed:F0} зн/мин, средняя точность: {averageAccuracy:F1}%";

        _statsGrid.DataSource = _statistics
            .OrderByDescending(item => item.Date)
            .Select(item => new
            {
                Дата = item.Date.ToString("dd.MM.yyyy HH:mm"),
                Текст = item.DictionaryName,
                Знаки = item.Characters,
                Ошибки = item.Errors,
                Точность = $"{item.Accuracy:F1}%",
                Скорость = $"{item.WordsPerMinute:F0}"
            })
            .ToList();
    }

    private void ClearStatistics()
    {
        if (_statistics.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Очистить всю статистику?",
            "Статистика",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        _statistics.Clear();
        _store.SaveStatistics(_statistics);
        RefreshStatistics();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _timer.Stop();
        base.OnClosing(e);
    }
}

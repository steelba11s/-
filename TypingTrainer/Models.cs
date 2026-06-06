namespace TypingTrainer;

public sealed class WordDictionary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<string> Words { get; set; } = [];
    public string Text { get; set; } = "";

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name) ? "Без названия" : Name;
    }
}

public sealed class TrainerSettings
{
    public float FontSize { get; set; } = 16f;
    public bool ShowKeyboard { get; set; } = true;
    public bool CaseSensitive { get; set; }
}

public sealed class TypingSessionStats
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string DictionaryName { get; set; } = "";
    public int Characters { get; set; }
    public int CorrectCharacters { get; set; }
    public int Errors { get; set; }
    public double Seconds { get; set; }
    public double Accuracy { get; set; }
    public double WordsPerMinute { get; set; }
}

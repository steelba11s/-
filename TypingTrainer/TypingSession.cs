namespace TypingTrainer;

public sealed class TypingSession(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long _startedAt;
    private TimeSpan _finishedElapsed;

    public string Target { get; private set; } = "";
    public string TextName { get; private set; } = "";
    public string Input { get; private set; } = "";
    public bool CaseSensitive { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsComplete { get; private set; }
    public TimeSpan Elapsed => IsActive ? _clock.GetElapsedTime(_startedAt) : _finishedElapsed;

    public void Start(WordDictionary text, bool caseSensitive)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text.Text)) throw new ArgumentException("Текст пуст.", nameof(text));
        Target = text.Text;
        TextName = text.Name;
        Input = "";
        CaseSensitive = caseSensitive;
        _startedAt = _clock.GetTimestamp();
        _finishedElapsed = TimeSpan.Zero;
        IsComplete = false;
        IsActive = true;
    }

    // True only on the transition to completion, preventing duplicate results.
    public bool UpdateInput(string input)
    {
        if (!IsActive) return false;
        Input = input;
        if (Input.Length < Target.Length) return false;
        _finishedElapsed = Elapsed;
        IsActive = false;
        IsComplete = true;
        return true;
    }

    public void Reset()
    {
        IsActive = false;
        IsComplete = false;
        Target = TextName = Input = "";
        _finishedElapsed = TimeSpan.Zero;
    }

    public bool Matches(char left, char right) => CaseSensitive
        ? left == right : char.ToUpperInvariant(left) == char.ToUpperInvariant(right);

    public TypingSessionStats GetStatistics()
    {
        var correct = 0;
        for (var i = 0; i < Math.Min(Input.Length, Target.Length); i++)
            if (Matches(Input[i], Target[i])) correct++;

        var seconds = Elapsed.TotalSeconds;
        return new TypingSessionStats
        {
            Date = _clock.GetLocalNow().DateTime,
            DictionaryName = TextName,
            Characters = Input.Length,
            CorrectCharacters = correct,
            Errors = Input.Length - correct,
            Seconds = seconds,
            Accuracy = Input.Length == 0 ? 100 : correct * 100d / Input.Length,
            WordsPerMinute = correct * 60d / Math.Max(seconds, 1)
        };
    }
}

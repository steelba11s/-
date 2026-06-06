using System.Text.Json;

namespace TypingTrainer;

public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;

    public AppDataStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        _dataDirectory = Path.Combine(root, "TypingTrainer");
        Directory.CreateDirectory(_dataDirectory);
    }

    private string SettingsPath => Path.Combine(_dataDirectory, "settings.json");
    private string StatsPath => Path.Combine(_dataDirectory, "statistics.json");

    public List<WordDictionary> LoadDictionaries()
    {
        return CreateDefaultTexts();
    }

    public TrainerSettings LoadSettings()
    {
        var settings = Load<TrainerSettings>(SettingsPath) ?? new TrainerSettings();
        settings.FontSize = Math.Clamp(settings.FontSize, 10f, 30f);
        return settings;
    }

    public void SaveSettings(TrainerSettings settings)
    {
        settings.FontSize = Math.Clamp(settings.FontSize, 10f, 30f);
        Save(SettingsPath, settings);
    }

    public List<TypingSessionStats> LoadStatistics()
    {
        return Load<List<TypingSessionStats>>(StatsPath) ?? [];
    }

    public void SaveStatistics(List<TypingSessionStats> statistics)
    {
        Save(StatsPath, statistics
            .OrderByDescending(item => item.Date)
            .Take(500)
            .ToList());
    }

    private static T? Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static void Save<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static List<WordDictionary> CreateDefaultTexts()
    {
        return
        [
            CreateText(
                "Текст 1",
                "Байкал считается самым глубоким озером на планете. В его холодной воде живут редкие животные, а зимой прозрачный лед покрывается длинными трещинами и узорами."),
            CreateText(
                "Текст 2",
                "Утром на кухне пахло свежим хлебом и горячим чаем. За окном медленно светлело, во дворе дворник убирал листья, а город только начинал просыпаться."),
            CreateText(
                "Текст 3",
                "В старом лесу стояла маленькая избушка. По вечерам в ее окнах загорался теплый свет, и путники знали, что там можно попросить воды и отдохнуть у печи."),
            CreateText(
                "Текст 4",
                "Пчелы умеют сообщать друг другу, где находится источник нектара. Они двигаются особым танцем, и другие пчелы понимают направление и расстояние до цветов."),
            CreateText(
                "Текст 5",
                "Маленький фонарь висел над калиткой и освещал мокрую дорожку. После дождя воздух стал прохладным, а в лужах отражались окна соседних домов."),
            CreateText(
                "Текст 6",
                "Давным-давно жил мельник, который умел слушать реку. По шуму воды он угадывал погоду, силу ветра и даже то, скоро ли придут гости из соседней деревни."),
            CreateText(
                "Текст 7",
                "Космонавты на орбите встречают рассвет много раз за сутки. Земля под ними кажется огромной картой, где облака медленно закрывают моря, горы и города."),
            CreateText(
                "Text 8",
                "The moon has no weather like Earth. Footprints left by astronauts can remain on its dusty surface for millions of years because there is no wind or rain."),
            CreateText(
                "Text 9",
                "In the small bakery on the corner, the first loaves were ready before sunrise. People stopped by for bread, coffee, and a few quiet words before work."),
            CreateText(
                "Text 10",
                "A young traveler found a silver key near the old bridge. Nobody knew which door it opened, but the village children invented a new story about it every day.")
        ];
    }

    private static WordDictionary CreateText(string name, string text)
    {
        return new WordDictionary
        {
            Id = Guid.NewGuid(),
            Name = name,
            Text = text,
            Words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
        };
    }
}

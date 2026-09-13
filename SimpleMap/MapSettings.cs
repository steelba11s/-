using System.Text.Json;

namespace SimpleMap;

public sealed class MapSettings
{
    public const string UserAgent = "ArturSimpleMap/1.0 (+https://github.com/steelba11s/-)";
    public string TileUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string SearchUrl { get; set; } = "https://photon.komoot.io/api/";

    public static MapSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "map-settings.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<MapSettings>(File.ReadAllText(path)) ?? new()
            : new MapSettings();
        if (!Uri.TryCreate(settings.SearchUrl, UriKind.Absolute, out var search) ||
            search.Scheme is not ("http" or "https") ||
            !Uri.TryCreate(settings.TileUrl, UriKind.Absolute, out var tiles) ||
            tiles.Scheme is not ("http" or "https") ||
            !new[] { "{z}", "{x}", "{y}" }.All(settings.TileUrl.Contains))
            throw new InvalidDataException("Проверьте адреса сервисов в map-settings.json.");
        return settings;
    }
}

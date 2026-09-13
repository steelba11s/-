using System.Net;
using System.Text.Json;

namespace SimpleMap;

public sealed record AddressResult(string Title, string Address, double Longitude, double Latitude, string Kind);

public sealed class AddressSearch : IDisposable
{
    private readonly HttpClient client;
    private readonly string endpoint;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<AddressResult>> cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset lastRequest;

    public AddressSearch(string endpoint, HttpClient? client = null)
    {
        this.endpoint = endpoint;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        this.client.DefaultRequestHeaders.UserAgent.ParseAdd(MapSettings.UserAgent);
        this.client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
    }

    public async Task<IReadOnlyList<AddressResult>> FindAsync(string query, CancellationToken token = default)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        await gate.WaitAsync(token);
        try
        {
            if (cache.TryGetValue(query, out var found)) return found;
            var wait = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - lastRequest);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            lastRequest = DateTimeOffset.UtcNow;
            var separator = endpoint.Contains('?') ? "&" : "?";
            var uri = endpoint + separator + "limit=5&q=" + Uri.EscapeDataString(query);
            using var response = await client.GetAsync(uri, token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new HttpRequestException("Поиск временно ограничен. Попробуйте немного позже.");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(token);
            var results = Parse(json);
            if (cache.Count >= 100) cache.Clear();
            cache[query] = results;
            return results;
        }
        finally { gate.Release(); }
    }

    public static IReadOnlyList<AddressResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = new List<AddressResult>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
            if (coordinates.GetArrayLength() < 2) continue;
            var lon = coordinates[0].GetDouble();
            var lat = coordinates[1].GetDouble();
            if (!double.IsFinite(lon) || !double.IsFinite(lat) || Math.Abs(lon) > 180 || Math.Abs(lat) > 85.05112878) continue;
            var properties = feature.GetProperty("properties");
            string Read(string name) => properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
            var street = string.Join(" ", new[] { Read("street"), Read("housenumber") }.Where(s => s.Length > 0));
            var title = Read("name");
            if (title.Length == 0) title = street.Length > 0 ? street : Read("city");
            if (title.Length == 0) title = "Найденное место";
            var address = string.Join(", ", new[] { street, Read("city"), Read("state"), Read("country") }
                .Where(s => s.Length > 0 && s != title).Distinct());
            results.Add(new(title, address, lon, lat, Read("osm_value")));
        }
        return results;
    }

    public void Dispose() => client.Dispose();
}

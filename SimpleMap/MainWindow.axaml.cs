using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;

namespace SimpleMap;

public partial class MainWindow : Window
{
    private readonly MemoryLayer resultLayer = new() { Name = "Найденный адрес" };
    private readonly CancellationTokenSource lifetime = new();
    private AddressSearch? search;
    private bool searching;
    private bool closed;

    public MainWindow() : this(null, null) { }

    public MainWindow(MapSettings? settings, AddressSearch? addressSearch)
    {
        InitializeComponent();
        settings ??= new MapSettings();
        try
        {
            settings = MapSettings.Load();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            StatusText.Text = "Используются адреса сервисов по умолчанию.";
        }

        // Поиск не зависит от того, удалось ли загрузить карту.
        search = addressSearch ?? new AddressSearch(settings.SearchUrl);
        try
        {
            var cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArturSimpleMap", "tiles");
            var cache = new FileCache(cachePath, "png", TimeSpan.FromDays(7));
            var source = new HttpTileSource(new GlobalSphericalMercator(0, 19), settings.TileUrl,
                name: "OpenStreetMap", persistentCache: cache,
                attribution: new Attribution("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright"),
                configureHttpRequestMessage: request => request.Headers.UserAgent.ParseAdd(MapSettings.UserAgent));
            var map = new Map();
            map.Layers.Add(new TileLayer(source));
            map.Layers.Add(resultLayer);
            map.Navigator.RotationLock = true;
            map.Widgets.Clear();
            MapView.Map = map;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            StatusText.Text = "Карта временно недоступна, но поиск адресов можно использовать.";
        }
        Opened += (_, _) =>
        {
            if (MapView.Map is not null) MoveTo(37.6176, 55.7558, 11);
            Dispatcher.UIThread.Post(() => SearchBox.Focus());
        };
        Closed += (_, _) =>
        {
            closed = true;
            lifetime.Cancel();
            search?.Dispose();
            MapView.Dispose();
        };
    }

    private async void SearchClick(object? sender, RoutedEventArgs e) => await SearchAsync();
    private async void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    public async Task SearchAsync()
    {
        if (searching || search is null || closed) return;
        var query = SearchBox.Text?.Trim() ?? "";
        if (query.Length < 2) { StatusText.Text = "Введите хотя бы два символа адреса."; return; }
        searching = true;
        SearchButton.IsEnabled = SearchBox.IsEnabled = false;
        SearchButton.Content = "Поиск…";
        StatusText.Text = "Ищем адрес…";
        ResultsPanel.IsVisible = false;
        ResultsList.ItemsSource = null;
        resultLayer.Features = [];
        resultLayer.DataHasChanged();
        try
        {
            var results = await search.FindAsync(query, lifetime.Token);
            if (closed) return;
            ResultsList.ItemsSource = results;
            ResultsPanel.IsVisible = results.Count > 0;
            StatusText.Text = results.Count == 0
                ? "Ничего не найдено. Уточните город, улицу или номер дома."
                : $"Найдено мест: {results.Count}. Выберите адрес в списке.";
        }
        catch (OperationCanceledException)
        {
            if (!closed) StatusText.Text = "Сервис не ответил вовремя. Попробуйте ещё раз.";
        }
        catch (HttpRequestException)
        {
            if (!closed) StatusText.Text = "Поиск недоступен. Проверьте интернет или повторите запрос позже.";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            if (!closed) StatusText.Text = "Сервис вернул непонятный ответ. Попробуйте позже.";
        }
        finally
        {
            searching = false;
            if (!closed)
            {
                SearchButton.IsEnabled = SearchBox.IsEnabled = true;
                SearchButton.Content = "Найти";
            }
        }
    }

    private void ResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not AddressResult result) return;
        var (x, y) = SphericalMercator.FromLonLat(result.Longitude, result.Latitude);
        resultLayer.Features = [new PointFeature(new MPoint(x, y))];
        resultLayer.Style = new SymbolStyle
        {
            Fill = new Brush(Color.FromString("#2463D4")),
            Outline = new Pen(Color.White, 3), SymbolScale = 0.8
        };
        resultLayer.DataHasChanged();
        MoveTo(result.Longitude, result.Latitude, result.Kind is "city" or "town" or "village" ? 12 : 16);
        ResultsPanel.IsVisible = false;
        StatusText.Text = string.Join(", ", new[] { result.Title, result.Address }.Where(s => s.Length > 0));
    }

    private void MoveTo(double lon, double lat, int zoom)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        MapView.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 156543.03392804097 / Math.Pow(2, zoom));
    }

    private void ZoomInClick(object? sender, RoutedEventArgs e) => MapView.Map.Navigator.ZoomIn();
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => MapView.Map.Navigator.ZoomOut();
    private void CloseResultsClick(object? sender, RoutedEventArgs e) => ResultsPanel.IsVisible = false;
    private async void AttributionClick(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://www.openstreetmap.org/copyright"));
}

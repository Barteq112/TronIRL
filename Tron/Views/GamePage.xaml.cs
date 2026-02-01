using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Tron.Models;
using Tron.Protos;
using Tron.ViewModels;

namespace Tron.Views;

public partial class GamePage : ContentPage
{
    private GameViewModel _vm;
    private Dictionary<string, PlayerMapObject> _mapObjects = new();
    private DateTime _lastCameraUpdate = DateTime.MinValue;
    private Polygon? _arenaPolygon;

    // Zmieniamy definicjê obiektu gracza - teraz ma Kó³ko (HeadCircle) zamiast Pinezki
    private class PlayerMapObject
    {
        public Circle HeadCircle { get; set; } // Kó³ko zamiast Pina
        public Polyline TailLine { get; set; }
    }

    public GamePage(GameViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;

        _vm.OnLocalLocationChanged += UpdateCameraCenter;
        _vm.OnGameStateUpdated += UpdateEnemiesView;
        _vm.OnMapBordersReceived += DrawRectangularArena;
    }

    private void DrawRectangularArena(double minLat, double maxLat, double minLon, double maxLon)
    {
        // Usuñ star¹ arenê, jeœli istnieje
        if (_arenaPolygon != null && GameMap.MapElements.Contains(_arenaPolygon))
        {
            GameMap.MapElements.Remove(_arenaPolygon);
        }

        // Tworzymy wielok¹t (Prostok¹t)
        _arenaPolygon = new Polygon
        {
            StrokeColor = Colors.Black,      // Czerwona ramka
            StrokeWidth = 6,               // Gruba linia
            FillColor = Color.FromRgba(255, 0, 0, 5), // Lekko czerwone t³o
            Geopath =
            {
                new Location(minLat, minLon), // Lewy Dó³
                new Location(maxLat, minLon), // Lewy Góra
                new Location(maxLat, maxLon), // Prawy Góra
                new Location(minLat, maxLon)  // Prawy Dó³
                // MAUI samo "domknie" kszta³t wracaj¹c do pocz¹tku
            }
        };

        GameMap.MapElements.Add(_arenaPolygon);
    }

    // --- KAMERA ---
    private bool _isFirstCameraMove = true;

    // 2. Podmieñ ca³¹ metodê UpdateCameraCenter na tê:
    private void UpdateCameraCenter(Location myLocation)
    {
        var now = DateTime.Now;
        if ((now - _lastCameraUpdate).TotalMilliseconds < 50) return;
        _lastCameraUpdate = now;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                MapSpan newSpan;

                // --- LOGIKA STARTOWA ---
                if (_isFirstCameraMove)
                {
                    // WYMUSZENIE NA START: Ustawiamy sztywno 50 metrów
                    newSpan = MapSpan.FromCenterAndRadius(myLocation, Distance.FromMeters(20));

                    // Odznaczamy flagê - teraz gracz mo¿e ju¿ sam sterowaæ zoomem
                    _isFirstCameraMove = false;
                }
                else
                {
                    // --- LOGIKA ROZGRYWKI (Respektuje zoom gracza + Limity) ---
                    var currentRegion = GameMap.VisibleRegion;

                    if (currentRegion != null)
                    {
                        double latDeg = currentRegion.LatitudeDegrees;
                        double lonDeg = currentRegion.LongitudeDegrees;

                        // LIMIT ODDALANIA (Max Zoom Out) - zapobiega ucieczce do Szwecji
                        if (latDeg > 0.02) latDeg = 0.02;
                        if (lonDeg > 0.02) lonDeg = 0.02;

                        // LIMIT PRZYBLI¯ANIA (Max Zoom In)
                        if (latDeg < 0.0002) latDeg = 0.0002;
                        if (lonDeg < 0.0002) lonDeg = 0.0002;

                        // U¿ywamy starego zoomu (skorygowanego), ale NOWEGO œrodka (myLocation)
                        newSpan = new MapSpan(myLocation, latDeg, lonDeg);
                    }
                    else
                    {
                        // Zabezpieczenie, gdyby region by³ null
                        newSpan = MapSpan.FromCenterAndRadius(myLocation, Distance.FromMeters(50));
                    }
                }

                // Aplikujemy zmianê
                GameMap.MoveToRegion(newSpan);
            }
            catch { }
        });
    }

    // --- RYSOWANIE (POPRAWIONE) ---
    // 1. ZARZ¥DZANIE WIDOKIEM (Lobby vs Gra)
    // 1. G£ÓWNA PÊTLA AKTUALIZACJI WIDOKU
    private void UpdateEnemiesView(List<PlayerState> players, bool isRunning)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // 1. CZYSZCZENIE: Jeœli jest Lobby, usuñ œlady
            if (!isRunning)
            {
                // Przechodzimy po WSZYSTKICH elementach mapy
                // U¿ywamy .ToList(), ¿eby nie modyfikowaæ kolekcji w trakcie iteracji
                foreach (var element in GameMap.MapElements.ToList())
                {
                    // Jeœli element to Linia (Polyline) -> Wyczyœæ j¹
                    if (element is Polyline polyline)
                    {
                        if (polyline.Geopath.Count > 0)
                            polyline.Geopath.Clear();
                    }
                }
            }

            var activeNames = new HashSet<string>();
            foreach (var player in players)
            {
                // Filtr b³êdów GPS (0,0)
                if (Math.Abs(player.Latitude) < 0.0001 && Math.Abs(player.Longitude) < 0.0001) continue;

                activeNames.Add(player.Name);

                // Przekazujemy status gry dalej
                UpdateSinglePlayer(player, isRunning);
            }

            CleanupLeftPlayers(activeNames);
        });
    }

    // 2. AKTUALIZACJA KONKRETNEGO GRACZA
    // ZMIANA: Dodano parametr 'bool isRunning'
    private void UpdateSinglePlayer(PlayerState player, bool isRunning)
    {
        Color serverColor = Colors.Lime;
        try { if (!string.IsNullOrEmpty(player.Color)) serverColor = Color.FromArgb(player.Color); } catch { }

        if (!_mapObjects.TryGetValue(player.Name, out var mapObj))
        {
            // --- TWORZENIE (INIT) ---
            var circle = new Circle
            {
                Center = new Location(player.Latitude, player.Longitude),
                Radius = Distance.FromMeters(0.6),
                StrokeColor = Colors.White,
                StrokeWidth = 1,
                FillColor = serverColor
            };

            var polyline = new Polyline
            {
                StrokeColor = serverColor,
                StrokeWidth = 4,
                // WA¯NE: Nie dodajemy tu punktu od razu w XAML-style initialization!
                // Zostawiamy Geopath puste na start.
                Geopath = { }
            };

            // Jeœli gra TRWA, to dodajemy ten pierwszy punkt startowy.
            // Jeœli jest LOBBY, linia zostaje pusta (niewidoczna).
            if (isRunning)
            {
                polyline.Geopath.Add(new Location(player.Latitude, player.Longitude));
            }

            GameMap.MapElements.Add(polyline);
            GameMap.MapElements.Add(circle);

            mapObj = new PlayerMapObject { HeadCircle = circle, TailLine = polyline };
            _mapObjects.Add(player.Name, mapObj);
        }

        // --- AKTUALIZACJA (UPDATE) ---

        // 1. Zawsze aktualizujemy pozycjê kropki
        mapObj.HeadCircle.Center = new Location(player.Latitude, player.Longitude);

        // 2. Naprawa kolorów (Reset po œmierci)
        if (mapObj.HeadCircle.FillColor.ToHex() != serverColor.ToHex())
        {
            mapObj.HeadCircle.FillColor = serverColor;
            mapObj.TailLine.StrokeColor = serverColor;
        }

        // --- BLOKADA RYSOWANIA W LOBBY ---
        // Jeœli gra nie dzia³a, wychodzimy st¹d.
        if (!isRunning)
        {
            return;
        }

        // 3. Rysowanie linii (Tylko gdy gra trwa)
        var newPoint = new Location(player.Latitude, player.Longitude);
        var lastPoint = mapObj.TailLine.Geopath.LastOrDefault();

        if (lastPoint == null || Location.CalculateDistance(lastPoint, newPoint, DistanceUnits.Kilometers) > 0.0005)
        {
            mapObj.TailLine.Geopath.Add(newPoint);
        }
    }

    private void CleanupLeftPlayers(HashSet<string> currentActiveNames)
    {
        var toRemove = _mapObjects.Keys.Where(k => !currentActiveNames.Contains(k)).ToList();
        foreach (var name in toRemove)
        {
            var obj = _mapObjects[name];
            // Usuwamy Circle i Polyline
            GameMap.MapElements.Remove(obj.HeadCircle);
            GameMap.MapElements.Remove(obj.TailLine);
            _mapObjects.Remove(name);
        }
    }

    // ... Reszta (ClearTrails, OnDisappearing) bez zmian ...
    private void ClearTrails()
    {
        GameMap.MapElements.Clear();
        _mapObjects.Clear();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopSensors();
        _vm.OnLocalLocationChanged -= UpdateCameraCenter;
        _vm.OnGameStateUpdated -= UpdateEnemiesView;
        ClearTrails();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.StartGame();
    }
}
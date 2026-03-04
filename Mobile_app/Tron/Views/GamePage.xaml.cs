using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Tron.Models;
using Tron.Protos;
using Tron.ViewModels;

namespace Tron.Views;

public partial class GamePage : ContentPage
{
    // atrybuty 
    private GameViewModel _vm;
    private Dictionary<string, PlayerMapObject> _mapObjects = new();
    private DateTime _lastCameraUpdate = DateTime.MinValue;
    private Polygon? _arenaPolygon;


    // Klasa pomocnicza do przechowywania elementów mapy dla pojedynczego gracza
    private class PlayerMapObject
    {
        public Circle HeadCircle { get; set; } 
        public Polyline TailLine { get; set; }
    }

    // konstruktor, inicjalizacja widoku i powi¹zanie z ViewModelem
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
        if (_arenaPolygon != null && GameMap.MapElements.Contains(_arenaPolygon))
        {
            GameMap.MapElements.Remove(_arenaPolygon);
        }

        _arenaPolygon = new Polygon
        {
            StrokeColor = Colors.Black,      
            StrokeWidth = 6,               
            FillColor = Color.FromRgba(255, 0, 0, 5), 
            Geopath =
            {
                new Location(minLat, minLon), 
                new Location(maxLat, minLon), 
                new Location(maxLat, maxLon), 
                new Location(minLat, maxLon)  
            }
        };

        GameMap.MapElements.Add(_arenaPolygon);
    }

    //czy kamera powinna siê przesun¹æ na start gry
    private bool _isFirstCameraMove = true;


    // metoda aktualizuj¹ca pozycjê kamery na mapie
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

                if (_isFirstCameraMove)
                {
                    newSpan = MapSpan.FromCenterAndRadius(myLocation, Distance.FromMeters(20));

                    _isFirstCameraMove = false;
                }
                else
                {
                    var currentRegion = GameMap.VisibleRegion;

                    if (currentRegion != null)
                    {
                        double latDeg = currentRegion.LatitudeDegrees;
                        double lonDeg = currentRegion.LongitudeDegrees;

                        if (latDeg > 0.02) latDeg = 0.02;
                        if (lonDeg > 0.02) lonDeg = 0.02;

                        if (latDeg < 0.0002) latDeg = 0.0002;
                        if (lonDeg < 0.0002) lonDeg = 0.0002;

                        newSpan = new MapSpan(myLocation, latDeg, lonDeg);
                    }
                    else
                    {
                        newSpan = MapSpan.FromCenterAndRadius(myLocation, Distance.FromMeters(50));
                    }
                }

                GameMap.MoveToRegion(newSpan);
            }
            catch { }
        });
    }

    //metoda aktualizuj¹ca widok przeciwników na mapie
    private void UpdateEnemiesView(List<PlayerState> players, bool isRunning)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // czyszczenie linii, jeœli gra nie jest uruchomiona
            if (!isRunning)
            {

                foreach (var element in GameMap.MapElements.ToList())
                {
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
                if (Math.Abs(player.Latitude) < 0.0001 && Math.Abs(player.Longitude) < 0.0001) continue;

                activeNames.Add(player.Name);

                UpdateSinglePlayer(player, isRunning);
            }

            CleanupLeftPlayers(activeNames);
        });
    }

    // metoda rysuj¹ca pojedynczego gracza na mapie oraz jego trasê
    private void UpdateSinglePlayer(PlayerState player, bool isRunning)
    {
        Color serverColor = Colors.Lime;
        try { if (!string.IsNullOrEmpty(player.Color)) serverColor = Color.FromArgb(player.Color); } catch { }

        if (!_mapObjects.TryGetValue(player.Name, out var mapObj))
        {
  
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
                Geopath = { }
            };


            if (isRunning)
            {
                polyline.Geopath.Add(new Location(player.Latitude, player.Longitude));
            }

            GameMap.MapElements.Add(polyline);
            GameMap.MapElements.Add(circle);

            mapObj = new PlayerMapObject { HeadCircle = circle, TailLine = polyline };
            _mapObjects.Add(player.Name, mapObj);
        }


        mapObj.HeadCircle.Center = new Location(player.Latitude, player.Longitude);

        if (mapObj.HeadCircle.FillColor.ToHex() != serverColor.ToHex())
        {
            mapObj.HeadCircle.FillColor = serverColor;
            mapObj.TailLine.StrokeColor = serverColor;
        }


        if (!isRunning)
        {
            return;
        }

        var newPoint = new Location(player.Latitude, player.Longitude);
        var lastPoint = mapObj.TailLine.Geopath.LastOrDefault();

        if (lastPoint == null || Location.CalculateDistance(lastPoint, newPoint, DistanceUnits.Kilometers) > 0.0005)
        {
            mapObj.TailLine.Geopath.Add(newPoint);
        }
    }


    // metoda usuwaj¹ca graczy, którzy opuœcili grê
    private void CleanupLeftPlayers(HashSet<string> currentActiveNames)
    {
        var toRemove = _mapObjects.Keys.Where(k => !currentActiveNames.Contains(k)).ToList();
        foreach (var name in toRemove)
        {
            var obj = _mapObjects[name];
            GameMap.MapElements.Remove(obj.HeadCircle);
            GameMap.MapElements.Remove(obj.TailLine);
            _mapObjects.Remove(name);
        }
    }

    // usuwanie wszystkich œladów z mapy
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
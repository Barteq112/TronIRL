using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Net.Client;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using System.Net.Http;
using Tron.Models;
using Tron.Protos;
using System.Numerics;

namespace Tron.ViewModels
{
    [QueryProperty(nameof(GameId), "GameId")]
    [QueryProperty(nameof(PlayerId), "PlayerId")]
    [QueryProperty(nameof(PlayerName), "PlayerName")]
    [QueryProperty(nameof(ServerIp), "ServerIp")]
    public partial class GameViewModel : ObservableObject
    {
        [ObservableProperty] string gameId = string.Empty;
        [ObservableProperty] string playerId = string.Empty;
        [ObservableProperty] string playerName = string.Empty;
        [ObservableProperty] string serverIp = "10.0.2.2";

        [ObservableProperty] double currentSpeed;
        [ObservableProperty] double mapRotation;
        [ObservableProperty] string statusMessage = "Inicjalizacja...";

        private GrpcChannel? _channel;
        private TronGameService.TronGameServiceClient? _client;
        private GameSessionHandler? _sessionHandler;

        private bool _isRunning;
        private Vector3 _currentAcceleration;
        private Location? _sharedLocation = null;

        public Action<Location>? OnLocalLocationChanged;
        public Action<List<PlayerState>, bool>? OnGameStateUpdated;
        public Action<double, double, double, double>? OnMapBordersReceived;

        public async Task StartGame()
        {
            StatusMessage = "Startowanie...";

            // 1. Sensory (GPS w trybie ciągłym + pomocnicze)
            await StartSensors();

            // 2. Sieć
            SetupGrpcClient();

            if (_client != null)
            {
                // --- ETAP 1: Próba pobrania konfiguracji mapy (opcjonalne) ---
                try
                {
                    var joinRequest = new JoinRequest { GameId = GameId, PlayerName = PlayerName };

                    // WAŻNE: Dodaj Deadline (np. 3 sekundy). 
                    // Inaczej, jak serwer nie odpowie, gra zawiśnie tutaj na wieki.
                    var response = await _client.JoinGameAsync(joinRequest, deadline: DateTime.UtcNow.AddSeconds(3));

                    if (response.Success)
                    {
                        PlayerId = response.PlayerId;

                        // WYSYŁAMY GRANICE DO WIDOKU
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            OnMapBordersReceived?.Invoke(
                                response.MinLatitude,
                                response.MaxLatitude,
                                response.MinLongitude,
                                response.MaxLongitude
                            );
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Logujemy błąd, ale NIE ZATRZYMUJEMY gry. 
                    // Może serwer jest stary i nie obsługuje map, ale grać się da.
                    System.Diagnostics.Debug.WriteLine($"Join Error: {ex.Message}");
                }

                // --- ETAP 2: URUCHOMIENIE LOGIKI GRY (TEGO BRAKOWAŁO!) ---
                // To musi się wykonać ZAWSZE, niezależnie czy JoinGame się udał, czy rzucił błąd.

                _sessionHandler = new GameSessionHandler(_client, GameId, PlayerName);
                _isRunning = true;

                // Uruchamiamy wątki w tle
                _ = Task.Run(GpsForceLoop);  // Wymuszanie GPS (Kamera)
                _ = Task.Run(NetworkLoop);   // Komunikacja z serwerem (Pozycje wrogów)

                StatusMessage = "Gra uruchomiona";
            }
        }

        public async Task StartSensors()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                // --- TRIK: KEEP-ALIVE ---
                // Włączamy nasłuchiwanie tylko po to, żeby Android nie uśpił anteny GPS.
                // Dzięki temu GetLocationAsync w pętli będzie działać błyskawicznie.
                var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(100));

                // Możemy podpiąć event, ale główną pracę wykona GpsForceLoop
                Geolocation.Default.LocationChanged += (s, e) => UpdateLocationLogic(e.Location);
                await Geolocation.Default.StartListeningForegroundAsync(request);

                // Akcelerometr i Kompas
                if (Accelerometer.Default.IsSupported)
                {
                    Accelerometer.Default.ReadingChanged += (s, e) => _currentAcceleration = e.Reading.Acceleration;
                    Accelerometer.Default.Start(SensorSpeed.UI);
                }
                if (Magnetometer.Default.IsSupported)
                {
                    Magnetometer.Default.ReadingChanged += (s, e) => {
                        var v = e.Reading.MagneticField;
                        MapRotation = -(Math.Atan2(v.Y, v.X) * (180 / Math.PI)) + 90;
                    };
                    Magnetometer.Default.Start(SensorSpeed.UI);
                }
            }
            catch { }
        }

        // --- WĄTEK WYMUSZAJĄCY GPS (Force Loop) ---
        private async Task GpsForceLoop()
        {
            while (_isRunning)
            {
                try
                {
                    // "Daj mi pozycję TERAZ"
                    // Timeout 100ms - skoro nasłuchiwanie działa w tle, to powinno zwrócić wynik z RAMu w 1-5ms.
                    // To omija filtr "5 metrów", bo GetLocationAsync zwraca surowe dane.
                    var req = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(100));

                    var location = await Geolocation.Default.GetLocationAsync(req);

                    if (location != null)
                    {
                        UpdateLocationLogic(location);
                    }
                }
                catch { /* Ignorujemy timeouty */ }

                // Pytamy bardzo często (co 50ms)
                await Task.Delay(50);
            }
        }

        // Wspólna metoda aktualizacji (wołana i przez Loop i przez Event)
        private void UpdateLocationLogic(Location? location)
        {
            if (location == null) return;

            // 1. Zapisz do zmiennej dla sieci
            _sharedLocation = location;

            // 2. Aktualizuj Kamerę (NATYCHMIAST)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnLocalLocationChanged?.Invoke(location);
                if (location.Speed.HasValue) CurrentSpeed = location.Speed.Value * 3.6;
            });
        }

        // --- WĄTEK SIECIOWY ---

        private async Task NetworkLoop()
        {
            StatusMessage = "Łączenie z grą...";

            while (_isRunning && _sessionHandler != null)
            {
                var locationToSend = _sharedLocation;

                if (locationToSend != null)
                {
                    try
                    {
                        // Pobieramy dane z Handlera. Tutaj 'isRunning' jest prawdziwe (z serwera).
                        var (lines, players, isRunning) = await _sessionHandler.ProcessTickAsync(locationToSend, _currentAcceleration);

                        // --- TUTAJ JEST BŁĄD ---
                        // BYŁO: OnGameStateUpdated?.Invoke(players, true);  <-- To kłamie widokowi, że gra zawsze trwa!

                        // MA BYĆ:
                        OnGameStateUpdated?.Invoke(players, isRunning); // <-- Przekazujemy prawdę!

                        MainThread.BeginInvokeOnMainThread(() =>
                            StatusMessage = isRunning ? "GRA TRWA" : $"LOBBY ({players.Count})");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Network Lag: {ex.Message}");
                    }
                }

                await Task.Delay(50);
            }
        }

        public void StopSensors()
        {
            _isRunning = false;
            try
            {
                Geolocation.Default.StopListeningForeground();
                if (Accelerometer.Default.IsMonitoring) Accelerometer.Default.Stop();
                if (Magnetometer.Default.IsMonitoring) Magnetometer.Default.Stop();
            }
            catch { }
        }

        private void SetupGrpcClient()
        {
            try
            {
                var httpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true } };
                var address = $"http://{ServerIp}:50051";
                _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = httpHandler });
                _client = new TronGameService.TronGameServiceClient(_channel);
            }
            catch { }
        }

        [RelayCommand]
        async Task LeaveGame()
        {
            _isRunning = false;
            try { await _client?.LeaveGameAsync(new LeaveRequest { GameId = GameId, PlayerId = PlayerId, PlayerName = PlayerName }); } catch { }
            StopSensors();
            await Shell.Current.GoToAsync("..");
        }
    }
}
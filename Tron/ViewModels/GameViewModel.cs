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

        // metoda uruchamiająca grę, łączy się z serwerem i inicjalizuje wątki( GPS i sieciowy)
        public async Task StartGame()
        {
            StatusMessage = "Startowanie...";

  
            await StartSensors();

            SetupGrpcClient();

            if (_client != null)
            {

                try
                {
                    var joinRequest = new JoinRequest { GameId = GameId, PlayerName = PlayerName };


                    var response = await _client.JoinGameAsync(joinRequest, deadline: DateTime.UtcNow.AddSeconds(3));

                    if (response.Success)
                    {
                        PlayerId = response.PlayerId;

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

                    System.Diagnostics.Debug.WriteLine($"Join Error: {ex.Message}");
                }



                _sessionHandler = new GameSessionHandler(_client, GameId, PlayerName);
                _isRunning = true;


                _ = Task.Run(GpsForceLoop);  
                _ = Task.Run(NetworkLoop);   

                StatusMessage = "Gra uruchomiona";
            }
        }

        // metoda uruchamiająca sensory
        public async Task StartSensors()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted) await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            
                var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(100));

                
                Geolocation.Default.LocationChanged += (s, e) => UpdateLocationLogic(e.Location);
                await Geolocation.Default.StartListeningForegroundAsync(request);

                
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

        //wątek GPS - wymusza ciągłe pobieranie lokalizacji
        private async Task GpsForceLoop()
        {
            while (_isRunning)
            {
                try
                {
                    
                    var req = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(100));

                    var location = await Geolocation.Default.GetLocationAsync(req);

                    if (location != null)
                    {
                        UpdateLocationLogic(location);
                    }
                }
                catch {  }

                
                await Task.Delay(50);
            }
        }

        // metoda aktualizująca lokalizację
        private void UpdateLocationLogic(Location? location)
        {
            if (location == null) return;

            _sharedLocation = location;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnLocalLocationChanged?.Invoke(location);
                if (location.Speed.HasValue) CurrentSpeed = location.Speed.Value * 3.6;
            });
        }

        //Wątek sieciowy - odpowiada za komunikację z serwerem gry, wywołuję sessionHandler

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
                        
                        var (lines, players, isRunning) = await _sessionHandler.ProcessTickAsync(locationToSend, _currentAcceleration);

                        OnGameStateUpdated?.Invoke(players, isRunning); 

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
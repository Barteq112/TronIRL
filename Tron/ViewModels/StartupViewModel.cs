using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Net.Client;
using Microsoft.Maui.Devices; // Do wykrywania emulatora
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Tron.Protos;
using Tron.Views;

namespace Tron.ViewModels
{
    public partial class StartupViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentServerIp = "10.0.2.2";

        [ObservableProperty]
        private string statusMessage = "Kliknij szukaj, aby znaleźć serwer.";

        [ObservableProperty]
        private bool isSearching;

        [ObservableProperty]
        private bool isGameListVisible;

        [ObservableProperty]
        private bool canRefresh; // Czy pokazać przycisk odśwież

        public ObservableCollection<GameInfo> Games { get; } = new();

        [RelayCommand]
        async Task SearchServer()
        {
            IsSearching = true;
            IsGameListVisible = false;
            CanRefresh = false;
            Games.Clear();
            StatusMessage = "Szukam serwera...";

            // Szukamy adresu (UDP lub Emulator)
            string serverIp = await FindServerIpAsync();

            if (string.IsNullOrEmpty(serverIp))
            {
                StatusMessage = "Nie znaleziono serwera.";
                IsSearching = false;
                return;
            }

            // Znaleziono! Zapisujemy IP i pobieramy listę
            _currentServerIp = serverIp;
            CanRefresh = true;

            await FetchGamesList();

            IsSearching = false;
        }

        [RelayCommand]
        async Task RefreshGames()
        {
            if (string.IsNullOrEmpty(_currentServerIp)) return;

            IsSearching = true;
            StatusMessage = "Odświeżam listę...";

            await FetchGamesList();

            IsSearching = false;
            StatusMessage = "Lista odświeżona.";
        }

        private async Task FetchGamesList()
        {
            try
            {
                Games.Clear();
                var channel = GrpcChannel.ForAddress($"http://{_currentServerIp}:50051");
                var client = new TronGameService.TronGameServiceClient(channel);

                var reply = await client.ListGamesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5));

                foreach (var game in reply.Games)
                {
                    Games.Add(game);
                }

                IsGameListVisible = true;
                StatusMessage = $"Połączono z: {_currentServerIp}. Wybierz grę.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Błąd pobierania: {ex.Message}";
                IsGameListVisible = false;
            }
        }

        [RelayCommand]
        async Task JoinGame(GameInfo game)
        {
            if (game == null) return;

            string playerName = await Shell.Current.DisplayPromptAsync("Dołączanie",
                $"Podaj nick:", accept: "Graj", cancel: "Anuluj");

            if (string.IsNullOrWhiteSpace(playerName)) return;

            StatusMessage = "Pobieram GPS i sprawdzam strefę...";
            IsSearching = true; // Pokaż kręciołek

            try
            {
                // 1. Pobierz aktualną lokalizację gracza
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(10)));

                if (location == null)
                {
                    await Shell.Current.DisplayAlert("Błąd", "Nie udało się pobrać lokalizacji. Włącz GPS.", "OK");
                    IsSearching = false;
                    return;
                }

                // 2. Przygotuj klienta
                var channel = GrpcChannel.ForAddress($"http://{_currentServerIp}:50051");
                var client = new TronGameService.TronGameServiceClient(channel);

                // 3. Wyślij request ze współrzędnymi
                var request = new JoinRequest
                {
                    GameId = game.Id,
                    PlayerName = playerName,
                    CurrentLatitude = location.Latitude,
                    CurrentLongitude = location.Longitude
                };

                var response = await client.JoinGameAsync(request);


                if (response.Success)
                {
                    // Przekazujemy dane do gry
                    var navParams = new Dictionary<string, object>
            {
                { "GameId", game.Id },
                { "PlayerId", response.PlayerId },
                { "PlayerName", playerName },
                // Przekazujemy też granice gry, żeby GamePage wiedział jak narysować mapę
                { "MinLat", game.MinLatitude },
                { "MaxLat", game.MaxLatitude },
                { "MinLon", game.MinLongitude },
                { "MaxLon", game.MaxLongitude },
                { "ServerIp", _currentServerIp }
            };
                    await Shell.Current.GoToAsync(nameof(GamePage), navParams);
                }
                else
                {
                    // Komunikat z serwera np. "Jesteś poza obszarem gry!"
                    await Shell.Current.DisplayAlert("Odmowa dostępu", response.Message, "OK");
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Błąd", ex.Message, "OK");
            }
            finally
            {
                IsSearching = false;
                StatusMessage = "Gotowy.";
            }
        }

        private async Task<string?> FindServerIpAsync()
        {
            try
            {
                if (DeviceInfo.DeviceType == DeviceType.Virtual)
                {
                    return "10.0.2.2";
                }

                using var udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;
                udpClient.Client.ReceiveTimeout = 2000;

                var requestData = Encoding.UTF8.GetBytes("TRON_DISCOVERY");
                var endPoint = new IPEndPoint(IPAddress.Broadcast, 50051);

                await udpClient.SendAsync(requestData, requestData.Length, endPoint);

                // POPRAWKA: Jawnie oznaczamy typy jako nullable (byte[]? i IPEndPoint?)
                var result = await Task.Run(() =>
                {
                    try
                    {
                        IPEndPoint? remoteEp = null;
                        byte[]? data = udpClient.Receive(ref remoteEp);
                        return (Data: data, Endpoint: remoteEp);
                    }
                    catch
                    {
                        // Zwracamy null tuple w razie błędu
                        return (Data: (byte[]?)null, Endpoint: (IPEndPoint?)null);
                    }
                });

                if (result.Data != null && result.Endpoint != null)
                {
                    string msg = Encoding.UTF8.GetString(result.Data);
                    if (msg == "TRON_HERE")
                    {
                        return result.Endpoint.Address.ToString();
                    }
                }
            }
            catch { /* Ignoruj błędy UDP */ }

            

            return null;
        }
    }
}
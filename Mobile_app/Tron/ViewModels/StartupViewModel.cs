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
        // atrybuty, które będą powiązane z widokiem (StartupPage)

        [ObservableProperty]
        private string _currentServerIp = "10.0.2.2";

        [ObservableProperty]
        private string statusMessage = "Kliknij szukaj, aby znaleźć serwer.";

        [ObservableProperty]
        private bool isSearching;

        [ObservableProperty]
        private bool isGameListVisible;

        [ObservableProperty]
        private bool canRefresh; 

        public ObservableCollection<GameInfo> Games { get; } = new();


        // metoda do wyszukiwania serwera w sieci lokalnej
        [RelayCommand]
        async Task SearchServer()
        {
            IsSearching = true;
            IsGameListVisible = false;
            CanRefresh = false;
            Games.Clear();
            StatusMessage = "Szukam serwera...";

            string serverIp = await FindServerIpAsync();

            if (string.IsNullOrEmpty(serverIp))
            {
                StatusMessage = "Nie znaleziono serwera.";
                IsSearching = false;
                return;
            }

            _currentServerIp = serverIp;
            CanRefresh = true;

            await FetchGamesList();

            IsSearching = false;
        }


        // metoda do odświeżania listy gier na znalezionym serwerze
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

        // metoda do pobierania listy gier z serwera
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

        // metoda do dołączania do wybranej gry

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
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(10)));

                if (location == null)
                {
                    await Shell.Current.DisplayAlert("Błąd", "Nie udało się pobrać lokalizacji. Włącz GPS.", "OK");
                    IsSearching = false;
                    return;
                }

                var channel = GrpcChannel.ForAddress($"http://{_currentServerIp}:50051");
                var client = new TronGameService.TronGameServiceClient(channel);


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
                    var navParams = new Dictionary<string, object>
            {
                { "GameId", game.Id },
                { "PlayerId", response.PlayerId },
                { "PlayerName", playerName },
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

        // metoda do wykrywania adresu IP serwera w sieci lokalnej za pomocą UDP broadcast, wysyłając wiadomość "TRON_DISCOVERY" i oczekując na odpowiedź "TRON_HERE"
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
            catch {}

            

            return null;
        }
    }
}
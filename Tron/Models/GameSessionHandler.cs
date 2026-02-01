using Microsoft.Maui.Devices.Sensors; // Do Location
using Microsoft.Maui.Maps; // Jeśli używasz typów mapy
using Tron.Protos; // Do gRPC
using System.Numerics;

namespace Tron.Models
{
    // Prosta struktura pomocnicza
    public record DrawInstruction(string PlayerName, Location Start, Location End, string Color);

    public class GameSessionHandler
    {
        private readonly TronGameService.TronGameServiceClient _client;
        private readonly string _gameId;
        private readonly string _playerName;

        // Pamięć podręczna: Gdzie byli gracze w poprzedniej klatce?
        private Dictionary<string, Location> _previousPositions = new();

        public GameSessionHandler(TronGameService.TronGameServiceClient client, string gameId, string playerName)
        {
            _client = client;
            _gameId = gameId;
            _playerName = playerName;
        }

        public async Task<(List<DrawInstruction> LinesToDraw, List<PlayerState> AllPlayers, bool IsRunning)> ProcessTickAsync(Location myLocation, Vector3 acceleration)
        {
            try
            {
                var request = new PositionRequest
                {
                    GameId = _gameId,
                    PlayerName = _playerName,
                    Latitude = myLocation.Latitude,
                    Longitude = myLocation.Longitude,
                    AccelX = acceleration.X,
                    AccelY = acceleration.Y,
                    AccelZ = acceleration.Z
                };

                // Timeout 2s
                var response = await _client.UpdatePositionAsync(request, deadline: DateTime.UtcNow.AddSeconds(2));

                // 1. Tworzymy listę, którą uzupełnimy
                var linesToDraw = new List<DrawInstruction>();

                if (response.IsGameRunning)
                {
                    foreach (var player in response.Players)
                    {
                        var newLoc = new Location(player.Latitude, player.Longitude);

                        if (_previousPositions.TryGetValue(player.Name, out var oldLoc))
                        {
                            // 2. OBLICZAMY DYSTANS
                            // Rysuj kreskę tylko przy ruchu > 1m (filtrujemy szum GPS)
                            if (Location.CalculateDistance(oldLoc, newLoc, DistanceUnits.Kilometers) > 0.001)
                            {
                                // 3. DODAJEMY LINIE DO LISTY (To było pominięte!)
                                linesToDraw.Add(new DrawInstruction(player.Name, oldLoc, newLoc, player.Color));

                                // Aktualizujemy pozycję
                                _previousPositions[player.Name] = newLoc;
                            }
                        }
                        else
                        {
                            // Pierwsze wykrycie gracza - zapisujemy pozycję startową
                            _previousPositions[player.Name] = newLoc;
                        }
                    }
                }
                else
                {
                    // Gra nie trwa -> czyścimy historię, żeby nie łączyć linii po restarcie
                    _previousPositions.Clear();
                }

                // 4. ZWRACAMY WYPEŁNIONĄ LISTĘ 'linesToDraw' (A nie nową pustą!)
                return (linesToDraw, response.Players.ToList(), response.IsGameRunning);
            }
            catch (Exception)
            {
                // W przypadku błędu zwracamy false i puste listy
                return (new List<DrawInstruction>(), new List<PlayerState>(), false);
            }
        }
    }
}
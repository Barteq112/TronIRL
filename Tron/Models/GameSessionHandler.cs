using Microsoft.Maui.Devices.Sensors; 
using Microsoft.Maui.Maps; 
using Tron.Protos; 
using System.Numerics;

namespace Tron.Models
{
    // struktura która przechowuje instrukcje rysowania linii
    public record DrawInstruction(string PlayerName, Location Start, Location End, string Color);

    public class GameSessionHandler
    {

        // klasy obsługująca sesję gry
        private readonly TronGameService.TronGameServiceClient _client;
        private readonly string _gameId;
        private readonly string _playerName;

        // struktura przechowująca poprzednie pozycje graczy
        private Dictionary<string, Location> _previousPositions = new();

        public GameSessionHandler(TronGameService.TronGameServiceClient client, string gameId, string playerName)
        {
            _client = client;
            _gameId = gameId;
            _playerName = playerName;
        }

        // Metoda która wysyła aktualną pozycję i przyspieszenie, a następnie odbiera stan gry i przetwarza je na instrukcje rysowania
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


                var linesToDraw = new List<DrawInstruction>();

                if (response.IsGameRunning)
                {
                    foreach (var player in response.Players)
                    {
                        var newLoc = new Location(player.Latitude, player.Longitude);

                        if (_previousPositions.TryGetValue(player.Name, out var oldLoc))
                        {
      
                            if (Location.CalculateDistance(oldLoc, newLoc, DistanceUnits.Kilometers) > 0.001)
                            {
  
                                linesToDraw.Add(new DrawInstruction(player.Name, oldLoc, newLoc, player.Color));
                                _previousPositions[player.Name] = newLoc;
                            }
                        }
                        else
                        {
                            _previousPositions[player.Name] = newLoc;
                        }
                    }
                }
                else
                {
                    _previousPositions.Clear();
                }

                return (linesToDraw, response.Players.ToList(), response.IsGameRunning);
            }
            catch (Exception)
            {
                return (new List<DrawInstruction>(), new List<PlayerState>(), false);
            }
        }
    }
}
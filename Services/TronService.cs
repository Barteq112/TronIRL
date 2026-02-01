using Grpc.Core;
using Tron.Server.Protos;             // Tu są Twoje wygenerowane klasy (PositionRequest, itp.)

namespace Tron.Server.Services
{
    // Dziedziczymy po klasie z namespace Tron.Server.Protos
    public class TronService : Tron.Server.Protos.TronGameService.TronGameServiceBase
    {
        private readonly GameManager _gameManager;
        private readonly ILogger<TronService> _logger;

        public TronService(GameManager gameManager, ILogger<TronService> logger)
        {
            _gameManager = gameManager;
            _logger = logger;
        }

        // Zwróć uwagę na typy: Empty, GameListResponse
        public override Task<GameListResponse> ListGames(Empty request, ServerCallContext context)
        {
            var response = new GameListResponse();
            // Mapowanie Info z ServerGame na GameInfo (Protobuf)
            response.Games.AddRange(_gameManager.Games.Values.Select(x => x.Info));
            return Task.FromResult(response);
        }

        // Zwróć uwagę: JoinGameRequest (zgodnie z nowym proto)
        public override Task<JoinResponse> JoinGame(JoinRequest request, ServerCallContext context)
        {
            // 1. Sprawdzamy czy gra istnieje
            if (!_gameManager.Games.TryGetValue(request.GameId, out var game))
            {
                return Task.FromResult(new JoinResponse { Success = false, Message = "Gra nie istnieje." });
            }

            // 2. Sprawdzamy czy jest miejsce (opcjonalnie)
            if (game.Info.CurrentPlayers >= game.Info.MaxPlayers)
            {
                // Jeśli gracza nie ma na liście, a jest pełno -> błąd
                if (!game.Players.ContainsKey(request.PlayerName))
                {
                    return Task.FromResult(new JoinResponse { Success = false, Message = "Gra jest pełna." });
                }
            }

            // 3. Dodajemy/Aktualizujemy gracza w logicznej warstwie gry
            // Wywołujemy "pusty" update, żeby GameManager utworzył gracza i przydzielił mu kolor
            _gameManager.UpdatePlayerState(request.GameId, request.PlayerName, 0, 0, 0, 0, 0, 0);

            // 4. Pobieramy dane tego gracza (żeby poznać jego przydzielony kolor)
            if (game.Players.TryGetValue(request.PlayerName, out var playerInfo))
            {
                // 5. ZWRACAMY ROZSZERZONĄ ODPOWIEDŹ
                return Task.FromResult(new JoinResponse
                {
                    Success = true,
                    Message = "Dołączono do gry",
                    PlayerId = request.PlayerName,

                    // --- NOWE POLA ---
                    PlayerColor = playerInfo.Color, // Kolor wylosowany przez GameManager

                    MinLatitude = game.Info.MinLatitude,
                    MaxLatitude = game.Info.MaxLatitude,
                    MinLongitude = game.Info.MinLongitude,
                    MaxLongitude = game.Info.MaxLongitude
                });
            }
            else
            {
                return Task.FromResult(new JoinResponse { Success = false, Message = "Błąd przy tworzeniu gracza." });
            }
        }

        public override Task<LeaveResponse> LeaveGame(LeaveRequest request, ServerCallContext context)
        {
            if (_gameManager.Games.TryGetValue(request.GameId, out var serverGame))
            {
                bool removedName = serverGame.Info.PlayerNames.Remove(request.PlayerName);
                serverGame.Players.TryRemove(request.PlayerName, out _);

                if (removedName)
                {
                    serverGame.Info.CurrentPlayers = serverGame.Info.PlayerNames.Count;
                    return Task.FromResult(new LeaveResponse { Success = true, Message = "Wylogowano" });
                }
                return Task.FromResult(new LeaveResponse { Success = false, Message = "Gracza nie było na liście" });
            }
            return Task.FromResult(new LeaveResponse { Success = false, Message = "Gra nie istnieje" });
        }

        public override Task<GameUpdateResponse> UpdatePosition(PositionRequest request, ServerCallContext context)
        {
            // 1. Najpierw aktualizujemy pozycję tego gracza (to co już miałeś)
            _gameManager.UpdatePlayerState(
                request.GameId,
                request.PlayerName,
                request.Latitude,
                request.Longitude,
                request.Accuracy,
                request.AccelX,
                request.AccelY,
                request.AccelZ
            );

            // 2. Pobieramy stan wszystkich graczy w tej grze
            var playersList = _gameManager.GetPlayersState(request.GameId);
            var isRunning = _gameManager.IsGameRunning(request.GameId);

            // 3. Wysyłamy odpowiedź do telefonu
            var response = new GameUpdateResponse
            {
                IsGameRunning = isRunning
            };
            response.Players.AddRange(playersList);

            return Task.FromResult(response);
        }
    }


    
}
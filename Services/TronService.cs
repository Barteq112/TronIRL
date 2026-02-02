using Grpc.Core;
using Tron.Server.Protos;

namespace Tron.Server.Services
{

    // klasa implementująca serwis gRPC
    public class TronService : Tron.Server.Protos.TronGameService.TronGameServiceBase
    {
        private readonly GameManager _gameManager;
        private readonly ILogger<TronService> _logger;

        public TronService(GameManager gameManager, ILogger<TronService> logger)
        {
            _gameManager = gameManager;
            _logger = logger;
        }


        public override Task<GameListResponse> ListGames(Empty request, ServerCallContext context)
        {
            var response = new GameListResponse();
            response.Games.AddRange(_gameManager.Games.Values.Select(x => x.Info));
            return Task.FromResult(response);
        }

        // dołączanie gracza do gry
        public override Task<JoinResponse> JoinGame(JoinRequest request, ServerCallContext context)
        {
            if (!_gameManager.Games.TryGetValue(request.GameId, out var game))
            {
                return Task.FromResult(new JoinResponse { Success = false, Message = "Gra nie istnieje." });
            }

            if (game.Info.CurrentPlayers >= game.Info.MaxPlayers)
            {
                if (!game.Players.ContainsKey(request.PlayerName))
                {
                    return Task.FromResult(new JoinResponse { Success = false, Message = "Gra jest pełna." });
                }
            }

            _gameManager.UpdatePlayerState(request.GameId, request.PlayerName, 0, 0, 0, 0, 0, 0);

            if (game.Players.TryGetValue(request.PlayerName, out var playerInfo))
            {

                return Task.FromResult(new JoinResponse
                {
                    Success = true,
                    Message = "Dołączono do gry",
                    PlayerId = request.PlayerName,

                    PlayerColor = playerInfo.Color, 

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


        // opuszczanie gry przez gracza
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


        // aktualizacja pozycji gracza
        public override Task<GameUpdateResponse> UpdatePosition(PositionRequest request, ServerCallContext context)
        {
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

            var playersList = _gameManager.GetPlayersState(request.GameId);
            var isRunning = _gameManager.IsGameRunning(request.GameId);

            var response = new GameUpdateResponse
            {
                IsGameRunning = isRunning
            };
            response.Players.AddRange(playersList);

            return Task.FromResult(response);
        }
    }


    
}
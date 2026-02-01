using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tron.Server.Services;

namespace Tron.Server.Components.Pages
{
    public partial class Games 
    {
        [Inject] public GameManager GameManager { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        private IJSObjectReference? module;

        // Aktualnie wybrana gra (wrapper ServerGame)
        private ServerGame? selectedGame;

        // Timer do odświeżania listy graczy na żywo
        private System.Threading.Timer? timer;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/mapLogic.js?v=31132");
            }
        }

        protected override void OnInitialized()
        {
            timer = new System.Threading.Timer(async (_) =>
            {
                await InvokeAsync(async () =>
                {
                    StateHasChanged(); // Odświeża HTML (tabelki)
                    
                    // Odświeża Mapę (pozycje graczy i linie)
                    if (module != null && selectedGame != null)
                    {
                        try
                        {
                            await module.InvokeVoidAsync("updateGameView", selectedGame);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Błąd odświeżania mapy: {ex.Message}");
                        }
                    }
                });
            }, null, 1000, 500);
        }

        // Metoda wywoływana po kliknięciu w grę na liście
        private async Task SelectGame(string gameId)
        {
            if (GameManager.Games.TryGetValue(gameId, out var game))
            {
                selectedGame = game;

                StateHasChanged();
                await Task.Delay(50);

                // Wywołujemy nową funkcję JS do wyświetlenia mapy
                if (module != null)
                {
                    await module.InvokeVoidAsync("viewGameOnMap",
                        game.Info.MinLatitude,
                        game.Info.MaxLatitude,
                        game.Info.MinLongitude,
                        game.Info.MaxLongitude
                    );
                }
            }
        }

        private void RemovePlayer(string playerName)
        {
            if (selectedGame != null)
            {
                selectedGame.Info.PlayerNames.Remove(playerName);
                selectedGame.Info.CurrentPlayers = selectedGame.Info.PlayerNames.Count;

                selectedGame.Players.TryRemove(playerName, out _);
            }
        }

        private void DeleteGame()
        {
            if (selectedGame != null)
            {
                GameManager.Games.TryRemove(selectedGame.Info.Id, out _);
                selectedGame = null; // Czyścimy widok szczegółów
            }
        }

        private void StartGame()
        {
            if (selectedGame != null)
            {
                GameManager.StartGame(selectedGame.Info.Id);
            }
        }

        private void StopGame()
        {
            if (selectedGame != null)
            {
                GameManager.StopGame(selectedGame.Info.Id);
            }
        }

        private void AddBot()
        {
            if (selectedGame != null)
            {
                GameManager.AddBot(selectedGame.Info.Id);
                // Odświeżamy widok, żeby bot pojawił się na liście
                StateHasChanged();
            }
        }

    }


}
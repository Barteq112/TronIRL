using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;
using Tron.Server.Services;

namespace Tron.Server.Components.Pages // Upewnij się, że namespace pasuje do folderu!
{
    // Słowo kluczowe "partial" jest tutaj kluczowe - łączy ten plik z plikiem .razor
    public partial class AddGame : IAsyncDisposable
    {
        // Wstrzykiwanie zależności w pliku .cs robimy przez [Inject]
        // ALBO zostawiamy @inject w pliku .razor (wtedy tu nie musimy ich deklarować, bo są w drugiej połówce klasy)
        // Dla czystości C# często deklaruje się je tutaj:
        [Inject] public GameManager GameManager { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        private IJSObjectReference? module;
        private DotNetObjectReference<AddGame>? dotNetHelper;
        private NewGameFormModel newGameModel = new();

        // Zmienne do przechowywania rzeczywistych granic
        private double _minLat, _maxLat, _minLon, _maxLon;
        private bool _isInternalUpdate = false;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/mapLogic.js");
                dotNetHelper = DotNetObjectReference.Create(this);
                await module.InvokeVoidAsync("initMap", dotNetHelper, newGameModel.CenterLat, newGameModel.CenterLon);
            }
        }

        [JSInvokable]
        public void UpdateCoordinates(double minLat, double maxLat, double minLon, double maxLon)
        {
            _minLat = minLat; _maxLat = maxLat;
            _minLon = minLon; _maxLon = maxLon;

            _isInternalUpdate = true;

            newGameModel.CenterLat = Math.Round((minLat + maxLat) / 2.0, 4);
            newGameModel.CenterLon = Math.Round((minLon + maxLon) / 2.0, 4);
            newGameModel.WidthMeters = Math.Round(CalculateDistance(minLat, minLon, minLat, maxLon), 2);
            newGameModel.HeightMeters = Math.Round(CalculateDistance(minLat, minLon, maxLat, minLon), 2);

            _isInternalUpdate = false;
            StateHasChanged();
        }

        private async Task SyncMapFromInputs()
        {
            if (_isInternalUpdate) return;
            if (module is null) return;

            await module.InvokeVoidAsync("updateRectangle",
                newGameModel.CenterLat,
                newGameModel.CenterLon,
                newGameModel.WidthMeters,
                newGameModel.HeightMeters
            );
        }

        private void HandleAddGame()
        {
            double latOffset = (newGameModel.HeightMeters / 2.0) / 111132.0;
            double metersPerDegLon = 40075000.0 * Math.Cos(newGameModel.CenterLat * Math.PI / 180.0) / 360.0;
            double lonOffset = (newGameModel.WidthMeters / 2.0) / metersPerDegLon;

            _minLat = newGameModel.CenterLat - latOffset;
            _maxLat = newGameModel.CenterLat + latOffset;
            _minLon = newGameModel.CenterLon - lonOffset;
            _maxLon = newGameModel.CenterLon + lonOffset;

            string newId = Guid.NewGuid().ToString().Substring(0, 8);

            GameManager.CreateGame(
                newId,
                newGameModel.Name,
                newGameModel.MaxPlayers,
                _minLat, _maxLat,
                _minLon, _maxLon,
                newGameModel.WidthMeters,
                newGameModel.HeightMeters
            );

            newGameModel.Name = "Kolejna Arena";
        }


        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3;
            var φ1 = lat1 * Math.PI / 180;
            var φ2 = lat2 * Math.PI / 180;
            var Δφ = (lat2 - lat1) * Math.PI / 180;
            var Δλ = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) +
                    Math.Cos(φ1) * Math.Cos(φ2) *
                    Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        public async ValueTask DisposeAsync()
        {
            dotNetHelper?.Dispose();
            if (module is not null) await module.DisposeAsync();
        }

        // Klasa modelu formularza wewnątrz partial class
        public class NewGameFormModel
        {
            [Required] public string Name { get; set; } = "Moja Arena";
            [Range(2, 100)] public int MaxPlayers { get; set; } = 4;
            public double CenterLat { get; set; } = 52.2319;
            public double CenterLon { get; set; } = 21.0067;
            public double WidthMeters { get; set; } = 500;
            public double HeightMeters { get; set; } = 500;
        }
    }
}
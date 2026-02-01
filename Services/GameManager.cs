using System.Collections.Concurrent;
using Tron.Server.Protos;

namespace Tron.Server.Services
{
    // --- MODELE POMOCNICZE ---

    public class PlayerInfo
    {
        public string Name { get; set; } = "";
        public bool IsAlive { get; set; } = true;
        public string Color { get; set; } = "#FF0000";

        //DLA BOTA
        public bool IsBot { get; set; } = false;
        public int DirX { get; set; } = 0; 
        public int DirY { get; set; } = 0;
       

        // Pozycja w siatce (Grid)
        public int LastGridX { get; set; }
        public int LastGridY { get; set; }

        // Surowy GPS (tylko do obliczeń)
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }

        // Historia ścieżki - TERAZ TRZYMA IDEALNE KRATKI, NIE GPS
        public List<Coordinate> Trail { get; set; } = new();
    }

    // --- MATEMATYKA ---
    public static class MapUtils
    {
        private const double MetersPerLatDeg = 111132.0;
        private const double EarthCircumference = 40075000.0;

        // GPS -> SIATKA (int)
        public static (int x, int y) GpsToGrid(double lat, double lon, double centerLat, double centerLon, double cellSize = 1.0)
        {
            double latDiff = lat - centerLat;
            double lonDiff = lon - centerLon;
            double yMeters = latDiff * MetersPerLatDeg;
            double metersPerLonDeg = EarthCircumference * Math.Cos(centerLat * Math.PI / 180.0) / 360.0;
            double xMeters = lonDiff * metersPerLonDeg;
            return ((int)Math.Round(xMeters / cellSize), (int)Math.Round(yMeters / cellSize));
        }

        // SIATKA -> GPS (double) - NOWOŚĆ DO RYSOWANIA
        public static (double lat, double lon) GridToGps(int x, int y, double centerLat, double centerLon, double cellSize = 1.0)
        {
            double xMeters = x * cellSize;
            double yMeters = y * cellSize;

            double latDiff = yMeters / MetersPerLatDeg;
            double metersPerLonDeg = EarthCircumference * Math.Cos(centerLat * Math.PI / 180.0) / 360.0;
            double lonDiff = xMeters / metersPerLonDeg;

            return (centerLat + latDiff, centerLon + lonDiff);
        }
    }

    public class Coordinate { public double Lat { get; set; } public double Lon { get; set; } }

    public class ServerGame
    {
        public GameInfo Info { get; set; } = new();
        public double WidthMeters { get; set; }
        public double HeightMeters { get; set; }
        public bool IsRunning { get; set; } = false;

        public double CenterLat => (Info.MinLatitude + Info.MaxLatitude) / 2.0;
        public double CenterLon => (Info.MinLongitude + Info.MaxLongitude) / 2.0;

        public ConcurrentDictionary<string, PlayerInfo> Players { get; set; } = new();

        // Zbiór zajętych pól "X:Y"
        public HashSet<string> OccupiedCells { get; set; } = new();
    }

    // --- MANAGER ---
    // ... (sekcja using i PlayerInfo bez zmian) ...

    public class GameManager
    {
        public ConcurrentDictionary<string, ServerGame> Games { get; } = new();
        private readonly string[] _colors = { "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#00FFFF", "#FF00FF", "#FFA500", "#800080", "#FFC0CB" };
        private readonly Random _rnd = new();

        // Timer do sterowania botami
        private CancellationTokenSource _botsCts = new();

        public GameManager()
        {
            // ... (Twoje create game) ...
            CreateGame("1", "Arena Polska", 4, 49.0, 55.0, 14.0, 24.0, 600000, 600000);
            CreateGame("2", "Mały Park", 10, 52.220, 52.240, 21.000, 21.020, 1000, 2000);
            CreateGame("3", "Test", 10, 52.193986, 52.194154, 20.844205, 20.844686, 30, 19);

            // Uruchamiamy pętlę botów w tle
            _ = RunBotLoopAsync(_botsCts.Token);
        }

        // --- LOGIKA AI BOTÓW ---
        private async Task RunBotLoopAsync(CancellationToken token)
        {
            // Boty ruszają się co 400ms (dość wolno, ok. 2.5 metra na sekundę w siatce)
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
            while (await timer.WaitForNextTickAsync(token))
            {
                foreach (var game in Games.Values)
                {
                    if (!game.IsRunning) continue;

                    foreach (var player in game.Players.Values)
                    {
                        if (player.IsBot && player.IsAlive)
                        {
                            MoveBot(game, player);
                        }
                    }
                }
            }
        }

        private void MoveBot(ServerGame game, PlayerInfo bot)
        {
            // OBLICZAMY GRANICE MAPY (dla AI)
            int limitX = (int)(game.WidthMeters / 2.0);
            int limitY = (int)(game.HeightMeters / 2.0);

            // Gdzie chcę iść?
            int nextX = bot.LastGridX + bot.DirX;
            int nextY = bot.LastGridY + bot.DirY;
            string key = $"{nextX}:{nextY}";

            // 1. CZY PROSTO JEST ŚCIANA (LINIA)?
            bool isWall = game.OccupiedCells.Contains(key);

            // 2. CZY PROSTO JEST KONIEC MAPY?
            bool isOutOfBounds = Math.Abs(nextX) >= limitX || Math.Abs(nextY) >= limitY;

            bool isBlocked = isWall || isOutOfBounds;
            bool randomTurn = _rnd.Next(100) < 5; // 5% szansy na losowy skręt

            // JEŚLI ZABLOKOWANE LUB LOSOWY SKRĘT -> SZUKAMY WYJŚCIA
            if (isBlocked || randomTurn)
            {
                var possibleDirs = new List<(int x, int y)>();
                var dirs = new (int x, int y)[] { (0, 1), (0, -1), (1, 0), (-1, 0) };

                foreach (var d in dirs)
                {
                    if (d.x == -bot.DirX && d.y == -bot.DirY) continue; // Nie cofaj

                    // Sprawdzamy potencjalny kierunek
                    int checkX = bot.LastGridX + d.x;
                    int checkY = bot.LastGridY + d.y;
                    string checkKey = $"{checkX}:{checkY}";

                    // Warunki bezpiecznego kierunku:
                    // 1. Nie ma tam linii
                    // 2. Nie jest to poza mapą
                    bool dirWall = game.OccupiedCells.Contains(checkKey);
                    bool dirBound = Math.Abs(checkX) >= limitX || Math.Abs(checkY) >= limitY;

                    if (!dirWall && !dirBound)
                    {
                        possibleDirs.Add(d);
                    }
                }

                if (possibleDirs.Count > 0)
                {
                    var chosen = possibleDirs[_rnd.Next(possibleDirs.Count)];
                    bot.DirX = chosen.x; bot.DirY = chosen.y;

                    // Aktualizujemy cel na podstawie nowego kierunku
                    nextX = bot.LastGridX + bot.DirX;
                    nextY = bot.LastGridY + bot.DirY;
                }
            }

            // Wykonaj ruch (teraz już prawdopodobnie bezpieczny)
            bool collision = MoveAndCheckCollision(game, bot, bot.LastGridX, bot.LastGridY, nextX, nextY);

            if (collision)
            {
                KillPlayer(bot);
            }
            else
            {
                bot.LastGridX = nextX;
                bot.LastGridY = nextY;
                var gps = MapUtils.GridToGps(nextX, nextY, game.CenterLat, game.CenterLon);
                bot.Latitude = gps.lat;
                bot.Longitude = gps.lon;
            }
        }

        public void AddBot(string gameId)
        {
            if (Games.TryGetValue(gameId, out var game))
            {
                string botName = $"Bot_{_rnd.Next(100, 999)}";

                
                // Obliczamy połowę wymiaru gry (promień od środka)
                double halfWidth = game.WidthMeters / 2.0;
                double halfHeight = game.HeightMeters / 2.0;

                // Margines bezpieczeństwa 90% (żeby nie rodzić się na ścianie)
                int safeX = (int)(halfWidth * 0.9);
                int safeY = (int)(halfHeight * 0.9);

                // Zabezpieczenie dla bardzo małych map
                if (safeX < 2) safeX = 2;
                if (safeY < 2) safeY = 2;

                // Losujemy pozycję wewnątrz bezpiecznego obszaru
                int startX = _rnd.Next(-safeX, safeX);
                int startY = _rnd.Next(-safeY, safeY);

                // Losujemy kierunek startowy
                int dx = 0, dy = 0;
                if (_rnd.Next(2) == 0) dx = _rnd.Next(2) == 0 ? 1 : -1;
                else dy = _rnd.Next(2) == 0 ? 1 : -1;

                var startGps = MapUtils.GridToGps(startX, startY, game.CenterLat, game.CenterLon);

                var bot = new PlayerInfo
                {
                    Name = botName,
                    Color = _colors[_rnd.Next(_colors.Length)],
                    IsAlive = true,
                    IsBot = true,
                    DirX = dx,
                    DirY = dy,
                    LastGridX = startX,
                    LastGridY = startY,
                    Latitude = startGps.lat,
                    Longitude = startGps.lon
                };

                // Dodajemy punkt startowy
                bot.Trail.Add(new Coordinate { Lat = startGps.lat, Lon = startGps.lon });

                game.Players[botName] = bot;
                game.Info.PlayerNames.Add(botName);
                game.Info.CurrentPlayers++;

                // Jeśli gra trwa, od razu zajmujemy pole
                if (game.IsRunning)
                {
                    game.OccupiedCells.Add($"{startX}:{startY}");
                }
            }
        }

        // ... (Reszta metod: CreateGame, StartGame, UpdatePlayerState, MoveAndCheckCollision - BEZ ZMIAN) ...
        // Pamiętaj, żeby zostawić MoveAndCheckCollision, KillPlayer, GetPlayersState itp. z poprzedniej wersji!

        // TYLKO DLA KOMPLETNOŚCI WKLEJAM TE METODY, ŻEBYŚ NIE SKASOWAŁ ICH PRZYPADKIEM:
        public void CreateGame(string id, string name, int maxPlayers, double minLat, double maxLat, double minLon, double maxLon, double w, double h)
        {
            var game = new ServerGame
            {
                WidthMeters = w,
                HeightMeters = h,
                Info = new GameInfo { Id = id, Name = name, MaxPlayers = maxPlayers, MinLatitude = minLat, MaxLatitude = maxLat, MinLongitude = minLon, MaxLongitude = maxLon }
            };
            Games.TryAdd(id, game);
        }
        public void ClearPlayers(string gameId)
        {
            if (Games.TryGetValue(gameId, out var g))
            {
                g.Info.PlayerNames.Clear();
                g.Info.CurrentPlayers = 0;
                g.Players.Clear();
                g.OccupiedCells.Clear();
            }
        }

        public void StartGame(string gameId)
        {
            if (Games.TryGetValue(gameId, out var game))
            {
                game.IsRunning = true;

                // Czyścimy całą mapę (zajęte pola)
                game.OccupiedCells.Clear();

                foreach (var p in game.Players.Values)
                {
                    p.IsAlive = true;
                    if (p.Color == "#000000") p.Color = _colors[_rnd.Next(_colors.Length)];

                    p.Trail.Clear();

                    // --- POPRAWKA: RESET POZYCJI STARTOWEJ ---
                    if (!p.IsBot)
                    {
                        // Jeśli to człowiek, bierzemy jego AKTUALNY GPS i ustawiamy jako punkt startowy siatki.
                        // Dzięki temu nie pociągnie linii od miejsca, gdzie stał 5 minut temu.
                        var (gx, gy) = MapUtils.GpsToGrid(p.Latitude, p.Longitude, game.CenterLat, game.CenterLon);
                        p.LastGridX = gx;
                        p.LastGridY = gy;
                    }
                    // Dla bota zostawiamy LastGridX/Y bez zmian (bo on stoi tam, gdzie go zespawnowało)

                    // Zapisujemy ten punkt jako STARTOWY (kropka, nie linia)
                    var snapped = MapUtils.GridToGps(p.LastGridX, p.LastGridY, game.CenterLat, game.CenterLon);
                    p.Trail.Add(new Coordinate { Lat = snapped.lat, Lon = snapped.lon });

                    // Zajmujemy pole startowe (żeby nikt na nas nie wszedł)
                    game.OccupiedCells.Add($"{p.LastGridX}:{p.LastGridY}");
                }
            }
        }

        public void StopGame(string gameId)
        {
            if (Games.TryGetValue(gameId, out var game))
            {
                game.IsRunning = false;

                // 1. Czyścimy wizualne linie graczy
                foreach (var p in game.Players.Values)
                {
                    p.Trail.Clear();
                }

                // 2. WAŻNE: Czyścimy pamięć kolizji (OccupiedCells)
                // To usuwa wszystkie "ściany" z pamięci serwera.
                game.OccupiedCells.Clear();
            }
        }

        public void UpdatePlayerState(string gameId, string playerName, double rawLat, double rawLon, double accuracy, double ax, double ay, double az)
        {
            // ... (Twój kod z poprzedniej odpowiedzi) ...
            // (Wklej tu UpdatePlayerState z poprzedniego kodu)
            if (!Games.TryGetValue(gameId, out var game)) return;

            if (!game.Players.TryGetValue(playerName, out var player))
            {
                player = new PlayerInfo { Name = playerName, Color = _colors[_rnd.Next(_colors.Length)], IsAlive = true };
                game.Players[playerName] = player;
                if (!game.Info.PlayerNames.Contains(playerName))
                {
                    game.Info.PlayerNames.Add(playerName);
                    game.Info.CurrentPlayers = game.Info.PlayerNames.Count;
                }
            }
            if (!player.IsAlive) return;

            player.Latitude = rawLat;
            player.Longitude = rawLon;

            player.AccelX = ax; player.AccelY = ay; player.AccelZ = az;
 

            if (game.IsRunning)
            {
                var (newGridX, newGridY) = MapUtils.GpsToGrid(player.Latitude, player.Longitude, game.CenterLat, game.CenterLon);
                if (player.Trail.Count == 0) { /* ... */ } // (Skopiuj z poprzedniej odpowiedzi)

                if (newGridX != player.LastGridX || newGridY != player.LastGridY)
                {
                    bool crash = MoveAndCheckCollision(game, player, player.LastGridX, player.LastGridY, newGridX, newGridY);
                    if (crash) KillPlayer(player);
                    else { player.LastGridX = newGridX; player.LastGridY = newGridY; }
                }
            }
        }

        private bool MoveAndCheckCollision(ServerGame game, PlayerInfo player, int x0, int y0, int x1, int y1)
        {
            // OBLICZAMY GRANICE MAPY (w metrach/kratkach)
            // Zakładamy, że środek to (0,0). Więc granica to +/- połowa szerokości.
            int limitX = (int)(game.WidthMeters / 2.0);
            int limitY = (int)(game.HeightMeters / 2.0);

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = (x0 < x1) ? 1 : -1;
            int sy = (y0 < y1) ? 1 : -1;
            int err = dx - dy;
            int cx = x0; int cy = y0;

            while (true)
            {
                // Pomijamy punkt startowy (żeby nie zabić się stojąc w miejscu)
                if (cx != x0 || cy != y0)
                {
                    // 1. SPRAWDZENIE GRANIC MAPY (NOWOŚĆ)
                    if (Math.Abs(cx) > limitX || Math.Abs(cy) > limitY)
                    {
                        return true; // ŚMIERĆ: Uderzenie w bandę!
                    }

                    // 2. SPRAWDZENIE INNYCH LINII (OccupiedCells)
                    string key = $"{cx}:{cy}";
                    if (game.OccupiedCells.Contains(key))
                    {
                        return true; // ŚMIERĆ: Uderzenie w ślad!
                    }

                    // Jeśli czysto -> zajmujemy pole
                    game.OccupiedCells.Add(key);

                    // Rysujemy ślad
                    var snapped = MapUtils.GridToGps(cx, cy, game.CenterLat, game.CenterLon);
                    player.Trail.Add(new Coordinate { Lat = snapped.lat, Lon = snapped.lon });
                }

                if (cx == x1 && cy == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; cx += sx; }
                if (e2 < dx) { err += dx; cy += sy; }
            }
            return false;
        }

        private void KillPlayer(PlayerInfo player)
        {
            player.IsAlive = false;
            player.Color = "#000000";
        }

        public List<PlayerState> GetPlayersState(string gameId)
        {
            // ... (Kod z poprzedniej odpowiedzi) ...
            if (Games.TryGetValue(gameId, out var game))
            {
                var result = new List<PlayerState>();
                foreach (var p in game.Players.Values)
                {
                    var ps = new PlayerState
                    {
                        Name = p.Name,
                        Latitude = p.Latitude,
                        Longitude = p.Longitude,
                        Color = p.Color
                    };
                    // Visual trick
                    var fullTrail = new List<Coordinate>(p.Trail);
                    if (p.IsAlive && game.IsRunning) fullTrail.Add(new Coordinate { Lat = p.Latitude, Lon = p.Longitude });

                    result.Add(ps);
                }
                return result;
            }
            return new List<PlayerState>();
        }

        public bool IsGameRunning(string gameId)
        {
            return Games.TryGetValue(gameId, out var game) && game.IsRunning;
        }
    }


}
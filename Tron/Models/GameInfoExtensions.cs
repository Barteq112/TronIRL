namespace Tron.Protos
{
    //Przerobienie klasy ¿eby dodaæ w³aœciwoœci wyœwietlania
    public partial class GameInfo
    {
        public string PlayersDisplay
        {
            get
            {
                if (PlayerNames == null || PlayerNames.Count == 0)
                    return "Brak graczy";

                return "Gracze: " + string.Join(", ", PlayerNames);
            }
        }

        public string CountDisplay => $"{CurrentPlayers} / {MaxPlayers}";
    }
}
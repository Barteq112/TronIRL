using Tron.Views;

namespace Tron
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(GamePage), typeof(GamePage));
        }
    }
}

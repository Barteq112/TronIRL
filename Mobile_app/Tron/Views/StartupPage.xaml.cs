using Tron.ViewModels;

namespace Tron.Views;

public partial class StartupPage : ContentPage
{
    public StartupPage(StartupViewModel vm)
    {
        InitializeComponent();

        BindingContext = vm;
    }
}
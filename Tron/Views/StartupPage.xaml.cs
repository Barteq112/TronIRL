using Tron.ViewModels;

namespace Tron.Views;

public partial class StartupPage : ContentPage
{
    // Konstruktor przyjmuje ViewModel (to jest to Wstrzykiwanie Zale¿noœci)
    public StartupPage(StartupViewModel vm)
    {
        InitializeComponent();

        // KLUCZOWE: Tutaj mówimy stronie: "Twoje komendy s¹ w tym ViewModelu"
        BindingContext = vm;
    }
}
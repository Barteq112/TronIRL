using CommunityToolkit.Mvvm; 
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Tron.ViewModels;
using Tron.Views;

namespace Tron;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps() 
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Rejestracja widoków i widok-modeli
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<StartupViewModel>();

        builder.Services.AddTransient<GamePage>();
        builder.Services.AddTransient<GameViewModel>();

        return builder.Build();
    }
}
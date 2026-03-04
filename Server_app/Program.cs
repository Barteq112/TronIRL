using Microsoft.AspNetCore.Server.Kestrel.Core;
using Tron.Server.Components;
using Tron.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// --- KONFIGURACJA PORTÓW (Analogicznie do Pythona) ---
builder.WebHost.ConfigureKestrel(options =>
{
    // WEJŒCIE 1: Dla strony WWW (Blazor)
    // Nas³uchuje na standardowym porcie HTTP (np. 5000)
    // Dziêki temu wchodzisz przez przegl¹darkê na http://localhost:5000
    options.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // WEJŒCIE 2: Dla gRPC (Telefon) - To jest Twój "insecure socket"
    // Port 50051, wymuszone HTTP/2, BEZ SZYFROWANIA (SSL)
    options.ListenAnyIP(50051, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

// Reszta serwisu bez zmian...
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddGrpc();
builder.Services.AddSingleton<GameManager>();
builder.Services.AddHostedService<UdpDiscoveryService>(); // Twój serwis UDP

var app = builder.Build();

// Wy³¹czamy wymuszanie HTTPS, ¿eby nie psu³o nam gRPC na 50051
// app.UseHttpsRedirection(); 

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGrpcService<TronService>(); // To zadzia³a na porcie 50051

app.Run();
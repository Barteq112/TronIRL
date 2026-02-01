using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tron.Server.Services
{
    public class UdpDiscoveryService : BackgroundService
    {
        private readonly ILogger<UdpDiscoveryService> _logger;
        private const int Port = 50051; 

        public UdpDiscoveryService(ILogger<UdpDiscoveryService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var udpClient = new UdpClient(Port);
            _logger.LogInformation($"Nasłuchiwanie Discovery UDP na porcie {Port}...");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Czekamy na pakiet (asynchronicznie)
                    var result = await udpClient.ReceiveAsync(stoppingToken);
                    var message = Encoding.UTF8.GetString(result.Buffer);

                    if (message == "TRON_DISCOVERY")
                    {
                        _logger.LogInformation($"Otrzymano TRON_DISCOVERY od {result.RemoteEndPoint}");

                        // Odsyłamy odpowiedź
                        var responseData = Encoding.UTF8.GetBytes("TRON_HERE");
                        await udpClient.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normalne zamknięcie serwera
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd serwera UDP");
            }
        }
    }
}
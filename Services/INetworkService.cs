using TestService.Models;

namespace TestService.Services
{
    public interface INetworkService : IDisposable
    {
        event EventHandler<MessageData>? MessageReceived;
        event EventHandler<string>? ConnectionStatusChanged;

        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
        Task SendMessageAsync(string hexMessage);
        bool IsConnected { get; }
        void UpdateConfiguration(ConnectionConfig config);
    }
}

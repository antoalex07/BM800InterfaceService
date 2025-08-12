using TestService.Models; // Ensure this is present

namespace TestService.Services
{
    public interface INetworkService : IDisposable
    {
        // Event signature updated to reflect MessageData changes
        event EventHandler<MessageData>? MessageReceived;
        event EventHandler<string>? ConnectionStatusChanged;
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
        Task SendMessageAsync(string hexMessage);
        bool IsConnected { get; }
        void UpdateConfiguration(ConnectionConfig config);
    }
}
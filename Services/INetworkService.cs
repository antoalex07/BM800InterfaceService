using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

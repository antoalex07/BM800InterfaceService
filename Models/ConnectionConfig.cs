using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestService.Models
{
    public class ConnectionConfig
    {
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string ConnectionMethod { get; set; } = "client";
        public int ReconnectIntervalSeconds { get; set; } = 30;
        public int ConnectionTimeoutSeconds { get; set; } = 10;
        public int ReceiveTimeoutSeconds { get; set; } = 30;
        public int SendTimeoutSeconds { get; set; } = 10;
        public bool EnableKeepAlive { get; set; } = true;
        public int KeepAliveIntervalSeconds { get; set; } = 60;
        public int MaxReconnectAttempts { get; set; } = -1; // -1 for infinite
        public string LogLevel { get; set; } = "Information";
    }
}

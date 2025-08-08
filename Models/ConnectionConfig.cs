namespace TestService.Models
{
    public class ConnectionConfig
    {
        // Communication Type
        public string CommunicationType { get; set; } = "TCP"; // "TCP" or "SERIAL"

        // TCP/IP Settings
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;
        public string ConnectionMethod { get; set; } = "client"; // "client" or "server" (for TCP only)

        // Serial RS232 Settings
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public string Parity { get; set; } = "None"; // None, Odd, Even, Mark, Space
        public int DataBits { get; set; } = 8;
        public string StopBits { get; set; } = "One"; // None, One, Two, OnePointFive
        public string Handshake { get; set; } = "None"; // None, XOnXOff, RequestToSend, RequestToSendXOnXOff
        public bool DtrEnable { get; set; } = false;
        public bool RtsEnable { get; set; } = false;

        // Common Settings
        public int ReconnectIntervalSeconds { get; set; } = 30;
        public int ConnectionTimeoutSeconds { get; set; } = 10;
        public int ReceiveTimeoutSeconds { get; set; } = 30;
        public int SendTimeoutSeconds { get; set; } = 10;
        public bool EnableKeepAlive { get; set; } = true;
        public int KeepAliveIntervalSeconds { get; set; } = 60;
        public int MaxReconnectAttempts { get; set; } = -1; // -1 for infinite
        public string LogLevel { get; set; } = "Information";

        // Message Settings
        public string MessageDelimiter { get; set; } = ""; // For serial communication, optional message delimiter
        public int ReadBufferSize { get; set; } = 4096;
        public int WriteBufferSize { get; set; } = 2048;

        public CommunicationDirection Direction { get; set; } = CommunicationDirection.Bidirectional;

    }
}

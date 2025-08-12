using System.Net;
using System.Net.Sockets;
using System.Text; // Add this for Encoding.ASCII or Encoding.UTF8
using TestService.Handlers;
using TestService.Models;

namespace TestService.Services
{
    public class NetworkService : INetworkService
    {
        private readonly ILogger<NetworkService> _logger;
        // private readonly MessageHandler _messageHandler; // Remove or comment if not used elsewhere in this class
        private ConnectionConfig _config;
        private TcpListener? _tcpListener;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _connectionTask;
        private Task? _receiveTask;
        private Task? _keepAliveTask;
        private bool _isRunning;
        private int _reconnectAttempts;
        private readonly object _connectionLock = new object();

        public event EventHandler<MessageData>? MessageReceived;
        public event EventHandler<string>? ConnectionStatusChanged;
        public bool IsConnected { get; private set; }

        public NetworkService(ILogger<NetworkService> logger, ConfigurationService configService)
        {
            _logger = logger;
            // _config = configService.GetConfiguration(); // Move this inside the lock or keep as is, but ensure it's loaded
            // _messageHandler = new MessageHandler(logger); // Remove or comment if not used
            // Load config safely
            _config = new ConnectionConfig(); // Initialize with default
            try
            {
                _config = configService.GetConfiguration(); // Get the actual config
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting initial configuration in NetworkService, using default.");
            }
            configService.ConfigurationChanged += OnConfigurationChanged;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting NetworkService with method: {Method}", _config.ConnectionMethod);

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            if (_config.ConnectionMethod.ToLower() == "server")
            {
                _connectionTask = Task.Run(() => StartServerAsync(_cancellationTokenSource.Token), cancellationToken);
            }
            else
            {
                _connectionTask = Task.Run(() => StartClientAsync(_cancellationTokenSource.Token), cancellationToken);
            }

            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _logger.LogInformation("Stopping NetworkService");

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            await CleanupConnectionAsync();

            if (_connectionTask != null)
            {
                await _connectionTask;
            }
        }

        private async Task StartServerAsync(CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting TCP server on {IP}:{Port}", _config.IpAddress, _config.Port);

                    _tcpListener = new TcpListener(IPAddress.Parse(_config.IpAddress), _config.Port);
                    _tcpListener.Start();

                    OnConnectionStatusChanged("Server started, waiting for connections");

                    while (_isRunning && !cancellationToken.IsCancellationRequested)
                    {
                        var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                        _logger.LogInformation("Client connected from {RemoteEndPoint}", tcpClient.Client.RemoteEndPoint);

                        await HandleClientConnectionAsync(tcpClient, cancellationToken);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Server error occurred");
                    OnConnectionStatusChanged($"Server error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(_config.ReconnectIntervalSeconds), cancellationToken);
                }
                finally
                {
                    _tcpListener?.Stop();
                }
            }
        }

        private async Task StartClientAsync(CancellationToken cancellationToken)
        {
            _reconnectAttempts = 0;

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                if (_config.MaxReconnectAttempts > 0 && _reconnectAttempts >= _config.MaxReconnectAttempts)
                {
                    _logger.LogError("Maximum reconnection attempts ({MaxAttempts}) reached", _config.MaxReconnectAttempts);
                    break;
                }

                try
                {
                    _reconnectAttempts++;
                    _logger.LogInformation("Attempting to connect to {IP}:{Port} (Attempt {Attempt})",
                        _config.IpAddress, _config.Port, _reconnectAttempts);

                    _tcpClient = new TcpClient();
                    _tcpClient.ReceiveTimeout = _config.ReceiveTimeoutSeconds * 1000;
                    _tcpClient.SendTimeout = _config.SendTimeoutSeconds * 1000;

                    var connectTask = _tcpClient.ConnectAsync(_config.IpAddress, _config.Port);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_config.ConnectionTimeoutSeconds), cancellationToken);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        throw new TimeoutException("Connection attempt timed out");
                    }

                    if (_tcpClient.Connected)
                    {
                        await HandleClientConnectionAsync(_tcpClient, cancellationToken);
                        _reconnectAttempts = 0; // Reset on successful connection
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Client connection error (Attempt {Attempt})", _reconnectAttempts);
                    OnConnectionStatusChanged($"Connection failed: {ex.Message}");

                    _tcpClient?.Close();

                    if (_isRunning)
                    {
                        _logger.LogInformation("Waiting {Seconds} seconds before next connection attempt", _config.ReconnectIntervalSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(_config.ReconnectIntervalSeconds), cancellationToken);
                    }
                }
            }
        }

        private async Task HandleClientConnectionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                lock (_connectionLock)
                {
                    _tcpClient = client;
                    _networkStream = client.GetStream();
                    IsConnected = true;
                }

                OnConnectionStatusChanged("Connected");
                _logger.LogInformation("Connected to {RemoteEndPoint}", client.Client.RemoteEndPoint);

                if (_config.EnableKeepAlive)
                {
                    _keepAliveTask = Task.Run(() => KeepAliveAsync(cancellationToken), cancellationToken);
                }

                _receiveTask = Task.Run(() => ReceiveMessagesAsync(cancellationToken), cancellationToken);

                // Wait for disconnection or cancellation
                while (IsConnected && !cancellationToken.IsCancellationRequested && client.Connected)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client connection");
            }
            finally
            {
                await CleanupConnectionAsync();
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                while (IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    if (_networkStream?.DataAvailable == true)
                    {
                        int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead > 0)
                        {
                            var receivedData = new byte[bytesRead];
                            Array.Copy(buffer, receivedData, bytesRead);
                            var hexData = Convert.ToHexString(receivedData);

                            // --- Modified Section: Convert Hex to ASCII directly ---
                            string asciiContent = Encoding.UTF8.GetString(receivedData); // Or Encoding.ASCII if appropriate and known
                            _logger.LogInformation("Received message (HEX): {HexData}", hexData);
                            _logger.LogInformation("Received message (ASCII): {AsciiContent}", asciiContent);
                            // --- End Modified Section ---

                            var messageData = new MessageData
                            {
                                HexData = hexData,
                                Direction = "Received",
                                Timestamp = DateTime.Now,
                                // --- Modified Section: Assign to AsciiContent ---
                                AsciiContent = asciiContent
                                // --- End Modified Section ---
                                // XmlContent = _messageHandler.ProcessHexMessage(hexData) // Remove this line
                            };

                            MessageReceived?.Invoke(this, messageData);
                        }
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error receiving messages");
                await CleanupConnectionAsync();
            }
        }

        private async Task KeepAliveAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.KeepAliveIntervalSeconds), cancellationToken);

                    if (IsConnected)
                    {
                        // Send a simple keep-alive ping (you can customize this)
                        await SendMessageAsync("FFFE"); // Example keep-alive hex message
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keep-alive error");
            }
        }

        public async Task SendMessageAsync(string hexMessage)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new InvalidOperationException("Not connected");
            }
            try
            {
                var bytes = Convert.FromHexString(hexMessage);
                await _networkStream.WriteAsync(bytes, 0, bytes.Length);
                await _networkStream.FlushAsync();
                _logger.LogInformation("Sent message (HEX): {HexData}", hexMessage);

                // --- Modified Section: Convert Hex to ASCII for logging/sending event ---
                string asciiContent = Encoding.UTF8.GetString(bytes); // Or Encoding.ASCII
                _logger.LogInformation("Sent message (ASCII): {AsciiContent}", asciiContent);

                var messageData = new MessageData
                {
                    HexData = hexMessage,
                    Direction = "Sent",
                    Timestamp = DateTime.Now,
                    // --- Modified Section: Assign to AsciiContent ---
                    AsciiContent = asciiContent
                    // --- End Modified Section ---
                    // XmlContent = _messageHandler.ProcessHexMessage(hexMessage) // Remove this line
                };
                // --- End Modified Section ---
                MessageReceived?.Invoke(this, messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message: {HexData}", hexMessage);
                throw;
            }
        }                 

        private async Task CleanupConnectionAsync()
        {
            lock (_connectionLock)
            {
                IsConnected = false;
            }

            OnConnectionStatusChanged("Disconnected");

            _keepAliveTask?.Dispose();
            _receiveTask?.Dispose();

            _networkStream?.Close();
            _tcpClient?.Close();

            _networkStream = null;
            _tcpClient = null;

            await Task.CompletedTask;
        }

        public void UpdateConfiguration(ConnectionConfig config)
        {
            _config = config;
            _logger.LogInformation("Configuration updated");
        }

        private void OnConfigurationChanged(object? sender, ConnectionConfig newConfig)
        {
            UpdateConfiguration(newConfig);

            // Restart connection with new configuration if currently running
            if (_isRunning)
            {
                Task.Run(async () =>
                {
                    await StopAsync();
                    await StartAsync(CancellationToken.None);
                });
            }
        }

        private void OnConnectionStatusChanged(string status)
        {
            ConnectionStatusChanged?.Invoke(this, status);
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource?.Dispose();
        }
    }
}

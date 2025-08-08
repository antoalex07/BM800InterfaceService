using System.IO.Ports;
using System.Text;
using TestService.Handlers;
using TestService.Models;

namespace TestService.Services
{
    public class SerialService : INetworkService
    {
        private readonly ILogger<SerialService> _logger;
        private readonly MessageHandler _messageHandler;

        private ConnectionConfig _config;
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        private Task? _keepAliveTask;
        private Task? _monitorTask;

        private bool _isRunning;
        private int _reconnectAttempts;
        private readonly object _connectionLock = new object();

        public event EventHandler<MessageData>? MessageReceived;
        public event EventHandler<string>? ConnectionStatusChanged;

        public bool IsConnected { get; private set; }

        // Define the actual message start and end markers
        private const string MessageStartMarker = "<!--:Begin:Msg:";
        private const string MessageEndMarker = "<!--:End:Msg:";

        public SerialService(ILogger<SerialService> logger, ConfigurationService configService)
        {
            _logger = logger;
            _config = configService.GetConfiguration();
            _messageHandler = new MessageHandler(logger);

            configService.ConfigurationChanged += OnConfigurationChanged;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting SerialService on port: {PortName}", _config.PortName);

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _monitorTask = Task.Run(() => MonitorSerialConnectionAsync(_cancellationTokenSource.Token), cancellationToken);

            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _logger.LogInformation("Stopping SerialService");

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            await CleanupConnectionAsync();

            if (_monitorTask != null)
            {
                await _monitorTask;
            }
        }

        private async Task MonitorSerialConnectionAsync(CancellationToken cancellationToken)
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
                    if (!IsConnected)
                    {
                        _reconnectAttempts++;
                        _logger.LogInformation("Attempting to open serial port {PortName} (Attempt {Attempt})",
                            _config.PortName, _reconnectAttempts);

                        await OpenSerialPortAsync();

                        if (IsConnected)
                        {
                            _reconnectAttempts = 0; // Reset on successful connection
                            _logger.LogInformation("Serial port {PortName} opened successfully", _config.PortName);

                            // Start receiving data
                            _receiveTask = Task.Run(() => ReceiveMessagesAsync(cancellationToken), cancellationToken);

                            if (_config.EnableKeepAlive)
                            {
                                _keepAliveTask = Task.Run(() => KeepAliveAsync(cancellationToken), cancellationToken);
                            }
                        }
                    }

                    // Monitor connection status
                    if (IsConnected && (_serialPort == null || !_serialPort.IsOpen))
                    {
                        _logger.LogWarning("Serial port connection lost");
                        await CleanupConnectionAsync();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Serial connection error (Attempt {Attempt})", _reconnectAttempts);
                    OnConnectionStatusChanged($"Connection failed: {ex.Message}");

                    await CleanupConnectionAsync();

                    if (_isRunning)
                    {
                        _logger.LogInformation("Waiting {Seconds} seconds before next connection attempt", _config.ReconnectIntervalSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(_config.ReconnectIntervalSeconds), cancellationToken);
                    }
                }
            }
        }

        private async Task OpenSerialPortAsync()
        {
            try
            {
                // Check if port exists
                var availablePorts = SerialPort.GetPortNames();
                if (!availablePorts.Contains(_config.PortName))
                {
                    throw new InvalidOperationException($"Serial port {_config.PortName} is not available. Available ports: {string.Join(", ", availablePorts)}");
                }

                _serialPort = new SerialPort()
                {
                    PortName = _config.PortName,
                    BaudRate = _config.BaudRate,
                    DataBits = _config.DataBits,
                    Parity = ParseParity(_config.Parity),
                    StopBits = ParseStopBits(_config.StopBits),
                    Handshake = ParseHandshake(_config.Handshake),
                    DtrEnable = _config.DtrEnable,
                    RtsEnable = _config.RtsEnable,
                    ReadTimeout = _config.ReceiveTimeoutSeconds * 1000,
                    WriteTimeout = _config.SendTimeoutSeconds * 1000,
                    ReadBufferSize = _config.ReadBufferSize,
                    WriteBufferSize = _config.WriteBufferSize
                };

                // Open the port
                _serialPort.Open();

                lock (_connectionLock)
                {
                    IsConnected = true;
                }

                OnConnectionStatusChanged("Connected");
                await Task.CompletedTask;
            }
            catch (Exception)
            {
                _serialPort?.Dispose();
                _serialPort = null;
                throw;
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            if(_config.Direction == CommunicationDirection.Output)
            {
                _logger.LogWarning("ReceiveMessagesAsync called but service is configured for output only. Exiting receive task.");
                return;
            }

            // Use StringBuilder for efficient string concatenation while buffering
            var buffer = new StringBuilder();
            try
            {
                while (IsConnected && !cancellationToken.IsCancellationRequested && _serialPort?.IsOpen == true)
                {
                    try
                    {
                        if (_serialPort.BytesToRead > 0)
                        {
                            // Read available data. ReadExisting is okay for buffering approach.
                            // Consider ReadLine if line endings are strictly message part separators,
                            // but buffering with markers is more robust here.
                            var data = _serialPort.ReadExisting();
                            buffer.Append(data);

                            // Process the buffer to check for complete messages
                            await ProcessReceivedData(buffer, cancellationToken);
                        }
                        else
                        {
                            await Task.Delay(50, cancellationToken); // Small delay to prevent busy-waiting
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Read timeout is normal with SerialPort, continue
                        continue;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError(ex, "Serial port operation error during receive");
                        break; // Exit the receive loop on operational errors
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error receiving serial messages");
                await CleanupConnectionAsync();
            }
        }

        private async Task ProcessReceivedData(StringBuilder buffer, CancellationToken cancellationToken)
        {
            try 
            { 
                // Keep processing as long as there are complete messages in the buffer
                bool messageFound;
                do
                {
                    messageFound = false;
                    string currentBufferContent = buffer.ToString();

                    // Find the start index of a potential message
                    int startIndex = currentBufferContent.IndexOf(MessageStartMarker, StringComparison.Ordinal);
                    if (startIndex != -1)
                    {
                        // Find the end index *after* the start marker
                        // We need to find the *end marker* that corresponds to this start
                        int endIndex = currentBufferContent.IndexOf(MessageEndMarker, startIndex + MessageStartMarker.Length, StringComparison.Ordinal);

                        if (endIndex != -1)
                        {
                            // Find the actual end of the end tag (assuming it ends with -->)
                            int endOfEndTag = currentBufferContent.IndexOf("-->", endIndex + MessageEndMarker.Length, StringComparison.Ordinal);
                            if (endOfEndTag != -1)
                            {
                                // We have a complete message
                                endOfEndTag += 3; // Include the "-->" in the message
                                string completeMessage = currentBufferContent.Substring(startIndex, endOfEndTag - startIndex);

                                // Remove the processed message from the buffer
                                buffer.Remove(0, endOfEndTag); // Remove from start up to the end of the processed message

                                // Process the extracted complete message
                                await ProcessCompleteMessage(completeMessage);
                                messageFound = true; // Loop again to check for another message
                            }
                            // else: End tag found but --> not found yet, wait for more data
                        }
                        // else: Start tag found but end tag not found yet, wait for more data
                    }
                    // else: No start tag found at the beginning of the buffer, wait for more data or check if buffer starts with partial tag
                    
                    // Optional: Add a check for buffer overflow or malformed data if buffer grows too large without markers

                } while (messageFound && buffer.Length > 0); // Continue if a message was found and buffer still has content
                 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received serial data buffer");
                // Depending on error severity, you might want to clear the buffer or handle differently
                // buffer.Clear(); // Example: Clear on error to prevent getting stuck
            }
        }

        private async Task ProcessCompleteMessage(string completeMessageString)
        {
            try
            {
                // Convert the complete message string to hex as expected by MessageHandler/Logging
                var hexData = Convert.ToHexString(Encoding.UTF8.GetBytes(completeMessageString));

                _logger.LogInformation("Received complete serial message (based on markers)");

                var messageData = new MessageData
                {
                    HexData = hexData,
                    Direction = "Received",
                    Timestamp = DateTime.Now,
                    // Pass the actual complete message string for XML processing
                    XmlContent = _messageHandler.ProcessHexMessage(hexData) // MessageHandler expects hex
                };

                // Trigger the event with the complete message data
                MessageReceived?.Invoke(this, messageData);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing complete serial message: {Message}", completeMessageString);
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

            if (_config.Direction == CommunicationDirection.Input)
            {
                _logger.LogWarning("Attempted to send message on an Input-only connection.");
                throw new InvalidOperationException("Sending messages is not allowed on an Input-only connection.");
            }

            if (!IsConnected || _serialPort == null || !_serialPort.IsOpen)
            {
                throw new InvalidOperationException("Serial port not connected");
            }

            try
            {
                var bytes = Convert.FromHexString(hexMessage);
                var stringData = Encoding.UTF8.GetString(bytes);

                // Add delimiter if configured
                if (!string.IsNullOrEmpty(_config.MessageDelimiter))
                {
                    stringData += _config.MessageDelimiter;
                }

                _serialPort.Write(stringData);

                _logger.LogInformation("Sent serial message: {HexData}", hexMessage);

                var messageData = new MessageData
                {
                    HexData = hexMessage,
                    Direction = "Sent",
                    Timestamp = DateTime.Now,
                    XmlContent = _messageHandler.ProcessHexMessage(hexMessage)
                };

                MessageReceived?.Invoke(this, messageData);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending serial message: {HexData}", hexMessage);
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

            try
            {
                _serialPort?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing serial port");
            }

            _serialPort?.Dispose();
            _serialPort = null;

            await Task.CompletedTask;
        }

        public void UpdateConfiguration(ConnectionConfig config)
        {
            _config = config;
            _logger.LogInformation("Serial configuration updated");
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

        // Helper methods to parse enum values from strings
        private static Parity ParseParity(string parity)
        {
            return parity.ToUpper() switch
            {
                "NONE" => Parity.None,
                "ODD" => Parity.Odd,
                "EVEN" => Parity.Even,
                "MARK" => Parity.Mark,
                "SPACE" => Parity.Space,
                _ => Parity.None
            };
        }

        private static StopBits ParseStopBits(string stopBits)
        {
            return stopBits.ToUpper() switch
            {
                "NONE" => StopBits.None,
                "ONE" => StopBits.One,
                "TWO" => StopBits.Two,
                "ONEPOINTFIVE" => StopBits.OnePointFive,
                _ => StopBits.One
            };
        }

        private static Handshake ParseHandshake(string handshake)
        {
            return handshake.ToUpper() switch
            {
                "NONE" => Handshake.None,
                "XONXOFF" => Handshake.XOnXOff,
                "REQUESTTOSEND" => Handshake.RequestToSend,
                "REQUESTTOSENDXONXOFF" => Handshake.RequestToSendXOnXOff,
                _ => Handshake.None
            };
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource?.Dispose();
        }
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestService.Models;
using TestService.Services;

namespace TestService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly INetworkService _networkService;
        private readonly ConfigurationService _configService;

        public Worker(ILogger<Worker> logger, INetworkService networkService, ConfigurationService configService)
        {
            _logger = logger;
            _networkService = networkService;
            _configService = configService;

            // Subscribe to network events
            _networkService.MessageReceived += OnMessageReceived;
            _networkService.ConnectionStatusChanged += OnConnectionStatusChanged;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker service started at: {time}", DateTimeOffset.Now);

            try
            {
                // Start the network service
                await _networkService.StartAsync(stoppingToken);

                // Main service loop
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Log periodic status
                        var config = _configService.GetConfiguration();
                        _logger.LogInformation("Service running - Mode: {Mode}, Connected: {Connected}, Time: {Time}",
                            config.ConnectionMethod,
                            _networkService.IsConnected,
                            DateTimeOffset.Now);

                        // Example: Send periodic test message if connected (optional)
                        if (_networkService.IsConnected && ShouldSendTestMessage())
                        {
                            await SendTestMessage();
                        }

                        // Wait for the next iteration
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error in worker main loop");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in worker service");
                throw;
            }
            finally
            {
                _logger.LogInformation("Worker service stopping...");
                await _networkService.StopAsync();
            }
        }

        private void OnMessageReceived(object? sender, MessageData messageData)
        {
            _logger.LogInformation("Message {Direction}: {HexData} at {Timestamp}",
                messageData.Direction,
                messageData.HexData,
                messageData.Timestamp);

            if (!string.IsNullOrEmpty(messageData.XmlContent))
            {
                _logger.LogInformation("XML Content:\n{XmlContent}", messageData.XmlContent);
            }

            // Log to file for persistence
            LogMessageToFile(messageData);

            // Process the message based on your business logic
            ProcessReceivedMessage(messageData);
        }

        private void OnConnectionStatusChanged(object? sender, string status)
        {
            _logger.LogInformation("Connection status changed: {Status}", status);

            // Log to file
            File.AppendAllText("C:\\Temp\\connection-status.txt",
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {status}\n");
        }

        private bool ShouldSendTestMessage()
        {
            // Implement your logic to determine when to send test messages
            // For example, every 5 minutes
            return DateTime.Now.Minute % 5 == 0 && DateTime.Now.Second < 30;
        }

        private async Task SendTestMessage()
        {
            try
            {
                // Example test XML message converted to hex
                var testXml = "<TestMessage><Timestamp>" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</Timestamp><Data>Test from service</Data></TestMessage>";
                var testHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(testXml));

                await _networkService.SendMessageAsync(testHex);
                _logger.LogInformation("Test message sent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test message");
            }
        }

        private void ProcessReceivedMessage(MessageData messageData)
        {
            try
            {
                // Implement your message processing logic here
                // This could include:
                // - Parsing specific XML structures
                // - Triggering business logic based on message content
                // - Storing data in database
                // - Sending responses back to the machine

                if (messageData.Direction == "Received" && !string.IsNullOrEmpty(messageData.XmlContent))
                {
                    // Example: Parse XML and respond
                    if (messageData.XmlContent.Contains("TestMessage"))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var responseXml = "<Response><Status>OK</Status><ReceivedAt>" +
                                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</ReceivedAt></Response>";
                                var responseHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(responseXml));

                                await Task.Delay(1000); // Small delay before response
                                await _networkService.SendMessageAsync(responseHex);

                                _logger.LogInformation("Response sent for test message");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error sending response message");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received message: {HexData}", messageData.HexData);
            }
        }

        private void LogMessageToFile(MessageData messageData)
        {
            try
            {
                var logEntry = $"{messageData.Timestamp:yyyy-MM-dd HH:mm:ss} [{messageData.Direction}] {messageData.HexData}";
                if (!string.IsNullOrEmpty(messageData.XmlContent))
                {
                    logEntry += $" | XML: {messageData.XmlContent.Replace("\n", " ").Replace("\r", "")}";
                }
                logEntry += "\n";

                File.AppendAllText("C:\\Temp\\message-log.txt", logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging message to file");
            }
        }

        public override void Dispose()
        {
            _networkService?.Dispose();
            base.Dispose();
        }
    }
}

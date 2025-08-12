using TestService.Models;
using TestService.Services;
using System.Text.RegularExpressions; // For parsing XML-like segments

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
                    // --- New Logic for GLUQUANT HBA1C ---
                    // Check if the message content looks like the GLUQUANT format
                    // A simple check is the presence of <SEND> tags
                    if (messageData.XmlContent.Contains("<SEND>") && messageData.XmlContent.Contains("</SEND>"))
                    {
                        _logger.LogInformation("Detected GLUQUANT HBA1C message format.");

                        // Parse the message content
                        var analyzerResult = ParseAnalyzerMessage(messageData.XmlContent);

                        if (analyzerResult != null)
                        {
                            _logger.LogInformation("Parsed Analyzer Result: HbA1c={HbA1c}, HbA1ab={HbA1ab}, HbF={HbF}, HbLa1c={HbLa1c}, HbA1={HbA1}, HbA0={HbA0}",
                                analyzerResult.HbA1c, analyzerResult.HbA1ab, analyzerResult.HbF, analyzerResult.HbLa1c, analyzerResult.HbA1, analyzerResult.HbA0);

                            // --- TODO: Add Database Logic Here ---
                            // Example placeholder for database insertion:
                            // await InsertAnalyzerResultIntoDatabaseAsync(analyzerResult, messageData.Timestamp);
                            // You would need to implement InsertAnalyzerResultIntoDatabaseAsync based on your DB technology (Entity Framework, Dapper, etc.)

                            // Example: Log the extracted data to a separate file
                            LogAnalyzerResultToFile(analyzerResult, messageData.Timestamp);

                        }
                        else
                        {
                            _logger.LogWarning("Failed to parse GLUQUANT HBA1C message content.");
                        }
                    }

                    //if (messageData.Direction == "Received" && !string.IsNullOrEmpty(messageData.XmlContent))
                    //{
                    //    // Example: Parse XML and respond
                    //    if (messageData.XmlContent.Contains("TestMessage"))
                    //    {
                    //        _ = Task.Run(async () =>
                    //        {
                    //            try
                    //            {
                    //                var responseXml = "<Response><Status>OK</Status><ReceivedAt>" +
                    //                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</ReceivedAt></Response>";
                    //                var responseHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(responseXml));

                    //                await Task.Delay(1000); // Small delay before response
                    //                await _networkService.SendMessageAsync(responseHex);

                    //                _logger.LogInformation("Response sent for test message");
                    //            }
                    //            catch (Exception ex)
                    //            {
                    //                _logger.LogError(ex, "Error sending response message");
                    //            }
                    //        });
                    //    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received message: {HexData}", messageData.HexData);
            }
        }

        /// <summary>
        /// Parses the raw XML/Text content of a message expected to be in the GLUQUANT HBA1C format.
        /// </summary>
        /// <param name="messageContent">The full message content string.</param>
        /// <returns>An AnalyzerResult object populated with data, or null if parsing fails.</returns>
        private AnalyzerResult? ParseAnalyzerMessage(string messageContent)
        {
            try
            {
                var result = new AnalyzerResult();

                // --- Extract <R> section content ---
                // Use regex to find the content between <R> and </R> tags
                // RegexOptions.Singleline allows . to match newline characters within the data
                var rSectionMatch = Regex.Match(messageContent, @"<R>(.*?)</R>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (!rSectionMatch.Success)
                {
                    _logger.LogWarning("Could not find <R> section in message content.");
                    return null; // Indicate parsing failure
                }

                // Get the content inside the <R> tags
                string rContent = rSectionMatch.Groups[1].Value.Trim(); // Trim whitespace/newlines

                // --- Parse lines within <R> section ---
                // Split the content by newlines to get individual data lines
                // StringSplitOptions.RemoveEmptyEntries handles potential empty lines
                string[] lines = rContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    // Each line is expected to be "Key|Value"
                    string[] parts = line.Split('|');
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim(); // Trim whitespace
                        string value = parts[1].Trim();

                        // Map the key-value pair to the corresponding property in AnalyzerResult
                        switch (key)
                        {
                            case "HbA1ab":
                                result.HbA1ab = value;
                                break;
                            case "HbF":
                                result.HbF = value;
                                break;
                            case "HbLa1c":
                                result.HbLa1c = value;
                                break;
                            case "HbA1c":
                                result.HbA1c = value;
                                break;
                            case "HbA1": // Based on the example, even if not in description list
                                result.HbA1 = value;
                                break;
                            case "HbA0":
                                result.HbA0 = value;
                                break;
                            default:
                                _logger.LogDebug("Unknown key '{Key}' found in <R> section with value '{Value}'.", key, value);
                                // Optionally store unknown keys in a dictionary if needed
                                // result.AdditionalData[key] = value;
                                break;
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Skipping line in <R> section that does not conform to 'Key|Value' format: '{Line}'", line);
                    }
                }

                return result; // Return the populated object
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while parsing analyzer message content.");
                return null; // Indicate parsing failure due to exception
            }
        }

        // --- Optional: Add a method to log the parsed result to a file ---
        private void LogAnalyzerResultToFile(AnalyzerResult result, DateTime timestamp)
        {
            try
            {
                var logEntry = $"{timestamp:yyyy-MM-dd HH:mm:ss}," +
                               $"HbA1c={result.HbA1c}," +
                               $"HbA1ab={result.HbA1ab}," +
                               $"HbF={result.HbF}," +
                               $"HbLa1c={result.HbLa1c}," +
                               $"HbA1={result.HbA1}," +
                               $"HbA0={result.HbA0}" +
                               Environment.NewLine;

                File.AppendAllText("C:\\Temp\\analyzer-results.csv", logEntry); // Consider using Path.Combine for better path handling
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging analyzer result to file.");
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

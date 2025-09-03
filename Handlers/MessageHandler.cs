using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TestService.Handlers
{
    public class ResultNode
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Low { get; set; }
        public string High { get; set; }
    }

    public class MessageHandler
    {
        private readonly ILogger _logger;

        // Regex pattern for extracting parameters with optional v, l, h values
        private static readonly Regex ParameterRegex = new Regex(
            @"<p><n>(?<n>.*?)</n>(?:<v>(?<v>.*?)</v>)?(?:<l>(?<l>.*?)</l>)?(?:<h>(?<h>.*?)</h>)?</p>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public MessageHandler(ILogger logger)
        {
            _logger = logger;
        }

        public string? ProcessHexMessage(string hexMessage)
        {
            try
            {
                // Convert hex to bytes
                var bytes = Convert.FromHexString(hexMessage);
                // Convert bytes directly to string (assuming UTF-8 encoding)
                var messageString = Encoding.UTF8.GetString(bytes);

                // Extract and log parameters if the message contains XML data
                var parameters = ExtractParameters(messageString);
                if (parameters.Count > 0)
                {
                    LogExtractedParameters(parameters);
                }

                // Return the raw string content as-is
                return messageString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing hex message: {HexMessage}", hexMessage);
                return null; // Return null on conversion error
            }
        }

        /// <summary>
        /// Extracts parameters from XML content using regex pattern
        /// </summary>
        /// <param name="xmlContent">The XML content to parse</param>
        /// <returns>List of ResultNode objects containing parameter data</returns>
        public List<ResultNode> ExtractParameters(string xmlContent)
        {
            var results = new List<ResultNode>();

            try
            {
                var matches = ParameterRegex.Matches(xmlContent);

                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        results.Add(new ResultNode
                        {
                            Name = match.Groups["n"].Value,
                            Value = match.Groups["v"].Success ? match.Groups["v"].Value : null,
                            Low = match.Groups["l"].Success ? match.Groups["l"].Value : null,
                            High = match.Groups["h"].Success ? match.Groups["h"].Value : null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting parameters from XML content");
            }

            return results;
        }

        /// <summary>
        /// Logs all extracted parameters in a structured format
        /// </summary>
        /// <param name="parameters">List of extracted parameters</param>
        private void LogExtractedParameters(List<ResultNode> parameters)
        {
            try
            {
                _logger.LogInformation("=== EXTRACTED PARAMETERS ===");
                _logger.LogInformation("Total Parameters Found: {Count}", parameters.Count);
                _logger.LogInformation("================================");

                foreach (var param in parameters)
                {
                    var logMessage = new StringBuilder();
                    logMessage.Append($"Name: {param.Name}");

                    if (!string.IsNullOrEmpty(param.Value))
                        logMessage.Append($" | Value: {param.Value}");

                    if (!string.IsNullOrEmpty(param.Low))
                        logMessage.Append($" | Low: {param.Low}");

                    if (!string.IsNullOrEmpty(param.High))
                        logMessage.Append($" | High: {param.High}");

                    _logger.LogInformation("{ParameterInfo}", logMessage.ToString());
                }

                _logger.LogInformation("=== END PARAMETERS ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging extracted parameters");
            }
        }

        /// <summary>
        /// Gets a specific parameter by name from the extracted parameters
        /// </summary>
        /// <param name="parameters">List of parameters</param>
        /// <param name="parameterName">Name of the parameter to find</param>
        /// <returns>ResultNode if found, null otherwise</returns>
        public static ResultNode GetParameter(List<ResultNode> parameters, string parameterName)
        {
            return parameters?.FirstOrDefault(p => p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets parameters as a dictionary for easier access
        /// </summary>
        /// <param name="parameters">List of parameters</param>
        /// <returns>Dictionary with parameter names as keys</returns>
        public static Dictionary<string, ResultNode> GetParametersDictionary(List<ResultNode> parameters)
        {
            return parameters?.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, ResultNode>();
        }

        public string ConvertStringToHex(string input)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                return Convert.ToHexString(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting string to hex: {Input}", input);
                throw;
            }
        }

        public string? ConvertHexToString(string hexInput)
        {
            try
            {
                var bytes = Convert.FromHexString(hexInput);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting hex to string: {HexInput}", hexInput);
                return null;
            }
        }

        // Utility method to format hex with spaces (moved from MessageProcessor)
        public string FormatHexWithSpaces(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
                return string.Empty;

            // Remove spaces and validate
            hexString = hexString.Replace(" ", "").Replace("-", "");

            if (!IsValidHexString(hexString))
                return hexString;

            // Add space every two characters
            var formatted = new StringBuilder();
            for (int i = 0; i < hexString.Length; i += 2)
            {
                if (i > 0)
                    formatted.Append(" ");
                formatted.Append(hexString.Substring(i, Math.Min(2, hexString.Length - i)));
            }

            return formatted.ToString();
        }

        // Utility method to validate hex strings
        public bool IsValidHexString(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
                return false;

            if (hexString.Length % 2 != 0)
                return false;

            return hexString.All(c => "0123456789ABCDEFabcdef".Contains(c));
        }
    }
}
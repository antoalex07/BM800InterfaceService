using System.Text;
using System.Xml;

namespace TestService.Handlers
{
    public class MessageHandler
    {
        private readonly ILogger _logger;

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

                // Convert bytes to string (assuming UTF-8 encoding)
                var messageString = Encoding.UTF8.GetString(bytes);

                // Try to parse as XML
                if (IsValidXml(messageString))
                {
                    return FormatXml(messageString);
                }

                // If not valid XML, return the raw string
                _logger.LogDebug("Message is not valid XML: {Message}", messageString);
                return messageString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing hex message: {HexMessage}", hexMessage);
                return null;
            }
        }

        private bool IsValidXml(string xmlString)
        {
            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlString);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string FormatXml(string xmlString)
        {
            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlString);

                using var stringWriter = new StringWriter();
                using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\n"
                });

                xmlDoc.Save(xmlWriter);
                return stringWriter.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting XML");
                return xmlString;
            }
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
    }
}

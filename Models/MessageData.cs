namespace TestService.Models
{
    public class MessageData
    {
        public string HexData { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Direction { get; set; } = string.Empty; // "Sent" or "Received"
        // Renamed from XmlContent to AsciiContent
        public string? AsciiContent { get; set; }
        public string? XmlContent { get; set; }
    }
}
namespace TestService.Models
{
    public class MessageData
    {
        public string HexData { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Direction { get; set; } = string.Empty; // "Sent" or "Received"
        public string? XmlContent { get; set; }
    }
}

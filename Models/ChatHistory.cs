using diplom.Models;

namespace diplom.Models
{
    public class ChatHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public string UserMessage { get; set; } = string.Empty;
        public string BotResponse { get; set; } = string.Empty;
        public string? RetrievedContext { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
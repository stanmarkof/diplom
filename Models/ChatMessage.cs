using diplom.Models;

namespace diplom.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int SenderId { get; set; }
        public User Sender { get; set; } = null!;
        public int RecipientId { get; set; }
        public User Recipient { get; set; } = null!;
        public string Message { get; set; } = string.Empty;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }
}
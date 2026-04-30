using diplom.Models;

namespace diplom.Models
{
    public class ChatDialog
    {
        public int Id { get; set; }
        public int User1Id { get; set; }
        public User User1 { get; set; } = null!;
        public int User2Id { get; set; }
        public User User2 { get; set; } = null!;
        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    }
}
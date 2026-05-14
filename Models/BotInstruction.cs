namespace diplom.Models
{
    public class BotInstruction
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string[] Keywords { get; set; } = Array.Empty<string>();
        public string Answer { get; set; } = string.Empty;
        public string Role { get; set; } = "All";
        public string Embedding { get; set; } = string.Empty;  // JSON массива float
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
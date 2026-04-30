using diplom.Models;

namespace diplom.Models
{
    public class RagQueryLog
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public string Query { get; set; } = string.Empty;
        public string RetrievedChunks { get; set; } = string.Empty;
        public string OllamaResponse { get; set; } = string.Empty;
        public double ResponseTimeMs { get; set; }
        public DateTime QueryTime { get; set; } = DateTime.UtcNow;
    }
}
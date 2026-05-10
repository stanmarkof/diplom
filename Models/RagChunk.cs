namespace diplom.Models
{
    public class RagChunk
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int? DisciplineId { get; set; }  // Добавлено
        public int? MaterialId { get; set; }     // Добавлено
        public int ChunkIndex { get; set; }
        public string Text { get; set; } = string.Empty;  // Переименовано с Content на Text
        public string Embedding { get; set; } = string.Empty;
        public int CharStart { get; set; }
        public int CharEnd { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public RagDocument Document { get; set; } = null!;
    }
}
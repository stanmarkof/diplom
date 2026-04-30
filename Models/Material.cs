using diplom.Models;

namespace diplom.Models
{
    public class Material
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;      // Текстовое содержимое
        public string? FilePath { get; set; }                     // Путь к файлу
        public string? FileType { get; set; }                     // pdf, docx, txt, md
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public int DisciplineId { get; set; }
        public Discipline Discipline { get; set; } = null!;

        // Связь с RAG (один материал → один RAG документ)
        public RagDocument? RagDocument { get; set; }
    }
}
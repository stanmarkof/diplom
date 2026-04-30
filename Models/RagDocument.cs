using diplom.Models;

namespace  diplom.Models
{
    public enum AccessLevel
    {
        Public = 0,
        Discipline = 1,
        LecturerOnly = 2,
        AdminOnly = 3
    }

    public class RagDocument
    {
        public int Id { get; set; }
        public int? MaterialId { get; set; }
        public Material? Material { get; set; }
        public int DisciplineId { get; set; }
        public Discipline Discipline { get; set; } = null!;
        public string Title { get; set; } = string.Empty;
        public string SourceType { get; set; } = "Material";
        public AccessLevel AccessLevel { get; set; } = AccessLevel.Discipline;
        public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }

        public ICollection<RagChunk> RagChunks { get; set; } = new List<RagChunk>();
    }
}
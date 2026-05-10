using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace diplom.Models
{
    public class Material
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string? FilePath { get; set; }

        public string? FileType { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public bool IsIndexed { get; set; } = false;

        public bool IsVisible { get; set; } = true; // Видимость для студентов

        public int DisciplineId { get; set; }

        [ForeignKey("DisciplineId")]
        public Discipline Discipline { get; set; } = null!;

        public int? SectionId { get; set; }

        [ForeignKey("SectionId")]
        public Section? Section { get; set; }

        public RagDocument? RagDocument { get; set; }
    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace diplom.Models
{
    public class Test
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int DurationMinutes { get; set; }
        public int MaxScore { get; set; }
        public DateTime? Deadline { get; set; }
        public bool IsPublished { get; set; } = false;
        public bool IsVisible { get; set; } = true; // Добавлено: видимость для студентов

        public int DisciplineId { get; set; }
        public Discipline Discipline { get; set; } = null!;
        public int? SectionId { get; set; }
        public Section? Section { get; set; }

        public ICollection<Question> Questions { get; set; } = new List<Question>();
        public ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
    }
}
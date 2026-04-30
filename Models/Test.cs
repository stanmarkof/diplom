using diplom.Models;

namespace diplom.Models
{
    public class Test
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int DurationMinutes { get; set; }  // Продолжительность
        public int MaxScore { get; set; }
        public DateTime? Deadline { get; set; }   // 👈 ЖИВОЙ ДЕДЛАЙН (берётся отсюда)
        public bool IsPublished { get; set; } = false;

        public int DisciplineId { get; set; }
        public Discipline Discipline { get; set; } = null!;

        public ICollection<Question> Questions { get; set; } = new List<Question>();
        public ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
    }
}
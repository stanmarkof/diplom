using diplom.Models;

namespace diplom.Models
{
    public class TestResult
    {
        public int Id { get; set; }
        public int Score { get; set; }
        public int MaxScore { get; set; }
        public string? AnswersJson { get; set; }   // Сохранённые ответы студента
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;

        public int TestId { get; set; }
        public Test Test { get; set; } = null!;
    }
}
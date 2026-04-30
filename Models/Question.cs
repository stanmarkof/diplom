using diplom.Models;

namespace diplom.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? OptionsJson { get; set; }  // JSON для вариантов ответов
        public string CorrectAnswer { get; set; } = string.Empty;
        public int Points { get; set; } = 1;

        public int TestId { get; set; }
        public Test Test { get; set; } = null!;
    }
}
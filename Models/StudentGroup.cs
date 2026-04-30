namespace diplom.Models
{
    public class StudentGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;  // "ИСТ-122"
        public int YearOfAdmission { get; set; }
        public int CourseId { get; set; }

        public Course Course { get; set; } = null!;
        public ICollection<Student> Students { get; set; } = new List<Student>();

        // Дисциплины, открытые для этой группы
        public ICollection<Discipline> Disciplines { get; set; } = new List<Discipline>();
    }
}
namespace diplom.Models
{
    public class Course
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;  // "09.03.02"
        public string Name { get; set; } = string.Empty;  // "Информационные системы"
        public int DurationYears { get; set; } = 4;

        // Группы этого направления
        public ICollection<StudentGroup> StudentGroups { get; set; } = new List<StudentGroup>();

        // Дисциплины этого направления (учебный план)
        public ICollection<Discipline> Disciplines { get; set; } = new List<Discipline>();
    }
}
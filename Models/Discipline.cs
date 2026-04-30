namespace diplom.Models
{
    public class Discipline
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Semester { get; set; }
        public int CourseId { get; set; }
        public int CourseNumber { get; set; }  // 1, 2, 3, 4 курс обучения

        public Course Course { get; set; } = null!;

        public ICollection<Material> Materials { get; set; } = new List<Material>();
        public ICollection<Test> Tests { get; set; } = new List<Test>();
        public ICollection<StudentGroup> OpenGroups { get; set; } = new List<StudentGroup>();

        // Связь с преподавателями (многие-ко-многим)
        public ICollection<DisciplineLecturer> DisciplineLecturers { get; set; } = new List<DisciplineLecturer>();
    }
}
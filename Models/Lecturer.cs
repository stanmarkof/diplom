namespace diplom.Models
{
    public class Lecturer : User
    {
        public string? Department { get; set; }
        public string? Position { get; set; }

        // Связь с дисциплинами (многие-ко-многим)
        public ICollection<DisciplineLecturer> DisciplineLecturers { get; set; } = new List<DisciplineLecturer>();
    }
}
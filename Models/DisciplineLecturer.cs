namespace diplom.Models
{
    public class DisciplineLecturer
    {
        public int Id { get; set; }
        public int DisciplineId { get; set; }
        public int LecturerId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        public Discipline Discipline { get; set; } = null!;
        public Lecturer Lecturer { get; set; } = null!;
    }
}
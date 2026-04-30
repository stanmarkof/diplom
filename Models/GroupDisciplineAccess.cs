namespace diplom.Models
{
    public class GroupDisciplineAccess
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int DisciplineId { get; set; }
        public bool IsOpen { get; set; } = true;  // Открыта ли дисциплина для группы
        public DateTime? OpenedAt { get; set; }   // Дата открытия
        public DateTime? ClosedAt { get; set; }   // Дата закрытия (если закрыли)

        public StudentGroup Group { get; set; } = null!;
        public Discipline Discipline { get; set; } = null!;
    }
}
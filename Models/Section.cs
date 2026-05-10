namespace diplom.Models
{
    public class Section
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Order { get; set; } // Порядок отображения
        public int DisciplineId { get; set; }

        public Discipline Discipline { get; set; } = null!;

        // Материалы и тесты в этом разделе
        public ICollection<Material> Materials { get; set; } = new List<Material>();
        public ICollection<Test> Tests { get; set; } = new List<Test>();
    }
}
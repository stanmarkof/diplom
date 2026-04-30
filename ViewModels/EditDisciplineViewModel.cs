using System.ComponentModel.DataAnnotations;

namespace diplom.ViewModels
{
    public class EditDisciplineViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Введите название дисциплины")]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Выберите направление")]
        public int CourseId { get; set; }

        [Required(ErrorMessage = "Введите номер курса")]
        [Range(1, 6, ErrorMessage = "Курс должен быть от 1 до 6")]
        public int CourseNumber { get; set; }

        [Required(ErrorMessage = "Введите номер семестра")]
        [Range(1, 12, ErrorMessage = "Семестр должен быть от 1 до 12")]
        public int Semester { get; set; }

        public List<GroupAccessViewModel> Groups { get; set; } = new List<GroupAccessViewModel>();
    }

    public class GroupAccessViewModel
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public int StudentCount { get; set; }
        public bool IsOpen { get; set; }
    }
}
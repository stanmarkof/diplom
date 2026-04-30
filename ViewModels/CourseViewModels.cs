using System.ComponentModel.DataAnnotations;

namespace diplom.ViewModels
{
    // ==================== VIEW MODELS ДЛЯ КУРСОВ И ГРУПП ====================

    public class CourseGroupViewModel
    {
        public int CourseId { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public List<GroupAssignmentViewModel> Groups { get; set; } = new List<GroupAssignmentViewModel>();
    }

    public class GroupAssignmentViewModel
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public bool IsAssigned { get; set; }
    }
}
namespace diplom.ViewModels
{
    public class UserRoleViewModel
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
        public string? NewRole { get; set; }
        public int? GroupId { get; set; }
        public string? GroupName { get; set; }
    }
}
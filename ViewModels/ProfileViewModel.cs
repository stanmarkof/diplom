namespace diplom.ViewModels
{
    public class ProfileViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string? Role { get; set; }
        public string? GroupName { get; set; }
        public bool HasGroup { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
    }
}
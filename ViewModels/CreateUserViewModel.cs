using System.ComponentModel.DataAnnotations;

namespace diplom.ViewModels
{
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Введите фамилию")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите имя")]
        public string FirstName { get; set; } = string.Empty;

        public string? MiddleName { get; set; }

        [Required(ErrorMessage = "Введите логин")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите email")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите пароль")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        public string? Role { get; set; }  // Добавьте эту строку!

        public string? Department { get; set; }
        public string? Position { get; set; }
    }
}
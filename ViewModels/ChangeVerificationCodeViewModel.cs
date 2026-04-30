using System.ComponentModel.DataAnnotations;

namespace diplom.ViewModels
{
    public class ChangeVerificationCodeViewModel
    {
        public string CurrentVerificationCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите новый код")]
        [Display(Name = "Новый код верификации")]
        public string NewVerificationCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Подтвердите новый код")]
        [Display(Name = "Подтверждение нового кода")]
        [Compare("NewVerificationCode", ErrorMessage = "Коды не совпадают")]
        public string ConfirmNewVerificationCode { get; set; } = string.Empty;
    }
}
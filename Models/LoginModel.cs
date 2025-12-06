using System.ComponentModel.DataAnnotations;

namespace KerioControlWeb.Models
{
    public class LoginModel
    {
        [Required(ErrorMessage = "Поле обязательно для заполнения")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Поле обязательно для заполнения")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Поле обязательно для заполнения")]
        public string IpAddress { get; set; } = string.Empty;
    }
}
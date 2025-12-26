using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class Account
    {
        [Key]
        public int AccID { get; set; }

        [Required]
        public string AccName { get; set; } = string.Empty; // Tên hiển thị (Vd: Nguyễn Văn A)

        public string Username { get; set; } = string.Empty; // Tên đăng nhập

        public string AccPass { get; set; } = string.Empty; // Mật khẩu

        public string AccRole { get; set; } = "Staff"; // Admin hoặc Staff
    }
}
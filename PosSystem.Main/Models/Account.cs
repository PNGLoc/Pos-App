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

        // --- THÊM MỚI: QUYỀN HẠN CHI TIẾT ---
        public bool CanMoveTable { get; set; } = false;    // Quyền chuyển bàn
        public bool CanPayment { get; set; } = false;      // Quyền thanh toán
        public bool CanCancelItem { get; set; } = false;   // Quyền huỷ món
    }
}
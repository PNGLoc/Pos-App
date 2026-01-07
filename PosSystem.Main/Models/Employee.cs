using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class Employee
    {
        [Key]
        public int EmpID { get; set; }

        public required string FullName { get; set; }
        public string? Position { get; set; } // Vị trí: Phục vụ, Bếp, Bảo vệ...
        // Mã thẻ RFID dùng để chấm công
        public string? CardNumber { get; set; }

        public bool IsActive { get; set; } = true; // Để ẩn nhân viên đã nghỉ việc
    }
}
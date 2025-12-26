using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class Category
    {
        [Key]
        public int CategoryID { get; set; }

        [Required]
        public string CategoryName { get; set; } = string.Empty; // Vd: Cà phê, Trà sữa, Món chính

        public int OrderIndex { get; set; } = 0; // Để sắp xếp thứ tự hiển thị
    }
}
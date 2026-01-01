using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    public class Dish
    {
        [Key]
        public int DishID { get; set; }

        [Required]
        public string DishName { get; set; } = string.Empty;

        public decimal Price { get; set; } = 0;

        public string Unit { get; set; } = "Cốc"; // Cốc, Dĩa, Phần...

        public string ImagePath { get; set; } = "default.png"; // Đường dẫn ảnh (lưu trong wwwroot/images)

        public string DishStatus { get; set; } = "Active";

        // Liên kết Category
        public int CategoryID { get; set; }
        [ForeignKey("CategoryID")]
        public virtual Category? Category { get; set; }
    }
}
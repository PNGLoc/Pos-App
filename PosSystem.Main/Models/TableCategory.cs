using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class TableCategory
    {
        [Key]
        public int CategoryID { get; set; }

        // Thứ tự hiển thị khu vực/loại bàn (dùng khi lọc "Tất cả")
        public int DisplayOrder { get; set; } = 0;

        [Required]
        public string CategoryName { get; set; } = string.Empty; // Vd: Bàn thường, Bàn VIP, Mang về...

        public string Description { get; set; } = "";

        // Navigation property
        public ICollection<Table> Tables { get; set; } = new List<Table>();
    }
}

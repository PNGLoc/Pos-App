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

        // Màu viền hiển thị cho card bàn (hex: #RRGGBB)
        public string BorderColorHex { get; set; } = "#D0D0D0";

        // Font Awesome class cho icon hiển thị ở mobile (vd: "fas fa-chair")
        public string IconClass { get; set; } = "fas fa-chair";

        // Navigation property
        public ICollection<Table> Tables { get; set; } = new List<Table>();
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    public class OrderDetail
    {
        [Key]
        public long OrderDetailID { get; set; }

        public int Quantity { get; set; } = 1;

        public decimal UnitPrice { get; set; } // Giá gốc lúc bán

        // Giảm giá trên từng món (Vd: Món này đang sale 50%)
        public decimal DiscountRate { get; set; } = 0; // % giảm
        public decimal TotalAmount { get; set; } = 0; // Thành tiền = (SL * Giá) * (1 - Discount)

        public string Note { get; set; } = "";

        // Quan trọng cho IN ẤN BẾP
        // New: Mới gọi (Cần in) -> Sent: Đã gửi bếp -> Done: Đã ra món -> Cancel: Đã hủy
        public string ItemStatus { get; set; } = "New";

        // --- LIÊN KẾT ---
        public long OrderID { get; set; }
        [ForeignKey("OrderID")]
        public virtual Order? Order { get; set; }

        public int DishID { get; set; }
        [ForeignKey("DishID")]
        public virtual Dish? Dish { get; set; }
    }
}
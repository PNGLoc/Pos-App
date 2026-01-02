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
        public DateTime ItemOrderTime { get; set; } = DateTime.Now;
        // Quan trọng cho IN ẤN BẾP
        // New: Mới gọi (Cần in) -> Sent: Đã gửi bếp -> Done: Đã ra món -> Cancel: Đã hủy
        public string ItemStatus { get; set; } = "New";
        // --- MỚI: Số thứ tự đợt gọi bếp (0 = Chưa gọi, 1 = Đợt 1, 2 = Đợt 2...) ---
        public int KitchenBatch { get; set; } = 0;

        // --- MỚI: Số lượng ĐÃ BÁO BẾP ---
        // Vd: Quantity=3, Printed=2 => Cần báo thêm 1
        // Vd: Quantity=1, Printed=2 => Cần báo hủy 1
        public int PrintedQuantity { get; set; } = 0;
        // --- LIÊN KẾT ---
        public long OrderID { get; set; }
        [ForeignKey("OrderID")]
        public virtual Order? Order { get; set; }

        public int DishID { get; set; }
        [ForeignKey("DishID")]
        public virtual Dish? Dish { get; set; }
    }
}
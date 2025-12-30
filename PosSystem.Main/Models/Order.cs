using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System;

namespace PosSystem.Main.Models
{
    public class Order
    {
        [Key]
        public long OrderID { get; set; }

        public DateTime OrderTime { get; set; } = DateTime.Now;
        public DateTime? CheckoutTime { get; set; } // Thời điểm thanh toán xong
        public DateTime? FirstSentTime { get; set; } // Thời điểm gửi bếp lần đầu tiên

        public string OrderStatus { get; set; } = "Pending"; // Pending (Đang phục vụ), Paid (Đã thanh toán), Cancelled

        // --- TÍNH TIỀN ---
        public decimal SubTotal { get; set; } = 0; // Tổng tiền hàng (chưa giảm giá)

        // Giảm giá tổng bill
        public decimal DiscountPercent { get; set; } = 0; // Giảm theo % (Vd: 10%)
        public decimal DiscountAmount { get; set; } = 0; // Giảm theo tiền mặt (Vd: 50.000đ)

        public decimal TaxAmount { get; set; } = 0; // Thuế VAT (nếu có)

        public decimal FinalAmount { get; set; } = 0; // Khách cần trả = SubTotal - Discount + Tax

        // --- THANH TOÁN ---
        public string PaymentMethod { get; set; } = "Cash"; // Cash, Transfer (QR Code)

        // --- LIÊN KẾT ---
        public int? TableID { get; set; }
        [ForeignKey("TableID")]
        public virtual Table? Table { get; set; }

        public int? AccID { get; set; } // Người tạo đơn
        [ForeignKey("AccID")]
        public virtual Account? Account { get; set; }

        public virtual List<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    }
}
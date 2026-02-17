namespace PosSystem.Main.Server.Dtos
{
    public class CheckoutRequest
    {
        public long OrderID { get; set; }

        // Giảm giá (Vd: 10%)
        public decimal DiscountPercent { get; set; } = 0;

        // Hoặc giảm tiền mặt (Vd: 50.000)
        public decimal DiscountAmount { get; set; } = 0;

        // Phương thức: "Cash", "Transfer" (QR)
        public string PaymentMethod { get; set; } = "Cash";

        // [NEW] Mobile có thể chọn không in bill khi thanh toán
        public bool ShouldPrintBill { get; set; } = true;
    }
}
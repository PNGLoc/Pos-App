using System;
using PosSystem.Main.Models;

namespace PosSystem.Main.Helpers
{
    public static class PrintContentHelper
    {
        public static string ReplacePlaceholders(string content, Order order, int batchNumber = 0)
        {
            if (string.IsNullOrEmpty(content)) return "";

            string res = content;
            DateTime now = DateTime.Now;

            // --- 1. THÔNG TIN CƠ BẢN ---
            res = res.Replace("{Table}", order.Table?.TableName ?? "Mang về");
            res = res.Replace("{TableType}", order.Table?.TableType ?? "");
            res = res.Replace("{OrderId}", order.OrderID.ToString());

            // --- 2. THÔNG TIN NHÂN VIÊN ---
            // Cần đảm bảo query Order có Include Account
            res = res.Replace("{Staff}", order.Account?.AccName ?? "Admin");

            // --- 3. THỜI GIAN ---
            res = res.Replace("{PrintTime}", now.ToString("HH:mm")); // Giờ in phiếu
            res = res.Replace("{PrintDate}", now.ToString("dd/MM/yyyy"));

            res = res.Replace("{CheckInTime}", order.OrderTime.ToString("HH:mm")); // Giờ khách vào
            res = res.Replace("{CheckInDate}", order.OrderTime.ToString("dd/MM/yyyy"));

            // Tính thời gian ngồi (Duration)
            TimeSpan duration = now - order.OrderTime;
            string durationStr = $"{(int)duration.TotalHours}h {duration.Minutes}p";
            res = res.Replace("{Duration}", durationStr);

            // --- 4. TÀI CHÍNH (Chỉ dùng cho Bill) ---
            res = res.Replace("{SubTotal}", order.SubTotal.ToString("N0"));
            res = res.Replace("{Discount}", order.DiscountAmount > 0
                ? order.DiscountAmount.ToString("N0")
                : (order.DiscountPercent > 0 ? $"{order.DiscountPercent}%" : "0"));
            res = res.Replace("{Tax}", order.TaxAmount.ToString("N0"));
            res = res.Replace("{Total}", order.FinalAmount.ToString("N0"));
            res = res.Replace("{PaymentMethod}", order.PaymentMethod == "Transfer" ? "Chuyển khoản" : "Tiền mặt");

            // --- 5. BẾP ---
            res = res.Replace("{Batch}", batchNumber.ToString());

            return res;
        }
    }
}
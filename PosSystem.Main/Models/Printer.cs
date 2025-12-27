using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class Printer
    {
        [Key]
        public int PrinterID { get; set; }

        [Required]
        public string PrinterName { get; set; } = "Máy in mới"; // Tên gợi nhớ (Vd: Máy Bếp 1)

        // Loại kết nối: "LAN" (Wifi/Dây) hoặc "USB" (Driver Windows)
        public string ConnectionType { get; set; } = "LAN";

        // Nếu LAN -> Lưu IP (vd: 192.168.1.200). Nếu USB -> Lưu tên máy in trong Control Panel
        public string ConnectionString { get; set; } = "";

        // Chức năng chính: "Cashier" (In bill), "Kitchen" (Bếp), "Bar" (Pha chế)
        // Lưu ý: Một máy có thể vừa là Bếp vừa là Bar (tùy cấu hình logic sau này), 
        // nhưng ta cần 1 cờ để biết máy nào in Bill tính tiền.
        public bool IsBillPrinter { get; set; } = false;

        public int PaperSize { get; set; } = 80; // 80mm hoặc 58mm
        public bool IsActive { get; set; } = true;
    }
}
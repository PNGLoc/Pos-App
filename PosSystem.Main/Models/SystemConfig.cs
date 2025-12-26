using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class SystemConfig
    {
        [Key]
        public int ConfigID { get; set; }

        public string ShopName { get; set; } = "POS Quán Ăn";
        public string ShopAddress { get; set; } = "Địa chỉ quán...";
        public string ShopPhone { get; set; } = "0909...";
        // public string WifiPassword { get; set; } = "Wifipass...";

        // --- CẤU HÌNH QR ---
        // Nếu dùng ảnh có sẵn thì lưu tên file vào đây (Vd: qr_static.png)
        public string QrImagePath { get; set; } = "";
        /*
        // Nếu muốn tự sinh QR động thì dùng các trường này (như đã làm ở bước trước)
        public string BankBin { get; set; } = ""; 
        public string BankAccountNo { get; set; } = ""; 
        public string BankAccountName { get; set; } = ""; 
        */
    }
}
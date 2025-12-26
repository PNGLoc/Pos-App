using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class PrintTemplate
    {
        [Key]
        public int TemplateID { get; set; }

        [Required]
        public string TemplateName { get; set; } = "Mẫu chuẩn";

        // Loại mẫu in: "Bill" (Hóa đơn), "Kitchen" (Báo bếp), "Bar" (Pha chế)
        public string TemplateType { get; set; } = "Bill";

        // Kích thước khổ giấy: 80mm, 58mm
        public int PaperSize { get; set; } = 80;

        // --- TRÁI TIM CỦA TÙY CHỈNH ---
        // Chúng ta sẽ lưu cấu trúc in dưới dạng JSON String.
        // Ví dụ: 
        // {
        //   "Header": [ { "type": "logo", "align": "center" }, { "type": "text", "content": "{ShopName}", "size": 16, "bold": true } ],
        //   "Body": { "showDiscount": true, "showIndex": false },
        //   "Footer": [ { "type": "qr", "size": 200 } ]
        // }
        public string LayoutConfig { get; set; } = "";

        // Cờ đánh dấu mẫu đang được sử dụng
        public bool IsActive { get; set; } = false;
    }
}
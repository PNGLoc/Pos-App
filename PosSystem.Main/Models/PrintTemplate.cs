using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class PrintTemplate
    {
        [Key]
        public int TemplateID { get; set; }

        public string TemplateName { get; set; } = "Mẫu chuẩn";

        // Loại mẫu: "Bill" (Hóa đơn), "Kitchen" (Bếp)
        public string TemplateType { get; set; } = "Bill";

        // Khổ giấy: 80, 58
        public int PaperSize { get; set; } = 80;

        // --- QUAN TRỌNG: CHỨA TOÀN BỘ CẤU TRÚC JSON ---
        // Ví dụ: [{"ElementType":"Logo"}, {"ElementType":"Text", "Content":"{ShopName}"}...]
        public string TemplateContentJson { get; set; } = "";

        public bool IsActive { get; set; } = true;
    }
}
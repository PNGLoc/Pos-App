namespace PosSystem.Main.Models
{
    // Class này KHÔNG khai báo DbSet trong DbContext
    public class PrintElement
    {
        public string ElementType { get; set; } = "Text"; // Text, Logo, Separator, OrderDetails, Total, QRCode
        public string Content { get; set; } = "";
        public string Align { get; set; } = "Center";
        public int FontSize { get; set; } = 14;
        public bool IsBold { get; set; } = false;
        public bool IsVisible { get; set; } = true;
        public int ImageHeight { get; set; } = 300; // Độ lớn ảnh (Logo, QRCode) - mặc định 300px
    }
}
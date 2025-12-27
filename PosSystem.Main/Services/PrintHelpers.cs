using System.Text;
using System.Text.RegularExpressions;

namespace PosSystem.Main.Services
{
    // 1. Xử lý Tiếng Việt
    public static class VietnameseHelper
    {
        public static string RemoveSign4VietnameseString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            string[] ArrPlus = new string[] { "aAeEoOuUiIdDyY", "áàạảãâấầậẩẫăắằặẳẵ", "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ", "éèẹẻẽêếềệểễ", "ÉÈẸẺẼÊẾỀỆỂỄ", "óòọỏõôốồộổỗơớờợởỡ", "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ", "úùụủũưứừựửữ", "ÚÙỤỦŨƯỨỪỰỬỮ", "íìịỉĩ", "ÍÌỊỈĨ", "đ", "Đ", "ýỳỵỷỹ", "ÝỲỴỶỸ" };
            for (int i = 1; i < ArrPlus.Length; i++)
            {
                for (int j = 0; j < ArrPlus[i].Length; j++)
                    str = str.Replace(ArrPlus[i][j], ArrPlus[0][i - 1]);
            }
            return str;
        }
    }

    // 2. Mã lệnh ESC/POS
    public static class EscPos
    {
        public static byte[] Init = { 0x1B, 0x40 }; // Khởi tạo
        public static byte[] CutPaper = { 0x1D, 0x56, 0x42, 0x00 }; // Cắt giấy

        // Căn lề
        public static byte[] AlignLeft = { 0x1B, 0x61, 0 };
        public static byte[] AlignCenter = { 0x1B, 0x61, 1 };
        public static byte[] AlignRight = { 0x1B, 0x61, 2 };

        // In đậm
        public static byte[] BoldOn = { 0x1B, 0x45, 1 };
        public static byte[] BoldOff = { 0x1B, 0x45, 0 };

        // Cỡ chữ (Phóng to)
        public static byte[] SizeNormal = { 0x1D, 0x21, 0x00 };
        public static byte[] SizeDoubleHeight = { 0x1D, 0x21, 0x01 };
        public static byte[] SizeDoubleWidth = { 0x1D, 0x21, 0x10 };
        public static byte[] SizeBig = { 0x1D, 0x21, 0x11 };
    }
}
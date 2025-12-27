using System;
using System.Collections.Generic;
using System.Drawing; // System.Drawing.Common
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PosSystem.Main.Services
{
    public static class EscPosImageHelper
    {
        // 1. Chuyển WPF Visual (Giao diện) thành Bitmap (Ảnh)
        public static Bitmap RenderVisualToBitmap(UIElement visual, int width)
        {
            // Tính toán lại kích thước giao diện trước khi chụp
            visual.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            visual.Arrange(new Rect(new System.Windows.Point(0, 0), visual.DesiredSize));
            visual.UpdateLayout();

            int height = (int)visual.DesiredSize.Height;

            // Render ra RenderTargetBitmap
            RenderTargetBitmap rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            // Chuyển sang MemoryStream để tạo đối tượng Bitmap
            MemoryStream stream = new MemoryStream();
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(stream);

            return new Bitmap(stream);
        }

        // 2. Chuyển Bitmap thành Bytes ESC/POS (Lệnh GS v 0)
        public static byte[] ConvertBitmapToEscPosBytes(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            // Chiều rộng phải chia hết cho 8 (vì 1 byte = 8 bit)
            int resizeWidth = (width + 7) / 8 * 8;

            List<byte> data = new List<byte>();

            // Header lệnh in ảnh (GS v 0)
            data.AddRange(new byte[] { 0x1D, 0x76, 0x30, 0x00 });

            // xL, xH (Bytes chiều ngang)
            int bytesWidth = resizeWidth / 8;
            data.Add((byte)(bytesWidth % 256));
            data.Add((byte)(bytesWidth / 256));

            // yL, yH (Dots chiều dọc)
            data.Add((byte)(height % 256));
            data.Add((byte)(height / 256));

            // Quét từng pixel để chuyển thành Đen/Trắng
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < bytesWidth; x++)
                {
                    byte b = 0;
                    for (int k = 0; k < 8; k++) // Gom 8 pixel thành 1 byte
                    {
                        int posX = x * 8 + k;
                        if (posX < width)
                        {
                            // Lấy màu pixel
                            System.Drawing.Color c = bitmap.GetPixel(posX, y);
                            // Thuật toán: Nếu độ sáng < 128 (màu tối) -> Đen (bit 1)
                            if (c.R * 0.3 + c.G * 0.59 + c.B * 0.11 < 128)
                            {
                                b |= (byte)(1 << (7 - k));
                            }
                        }
                    }
                    data.Add(b);
                }
            }
            return data.ToArray();
        }
    }
}
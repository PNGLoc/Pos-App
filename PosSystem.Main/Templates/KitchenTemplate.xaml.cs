using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PosSystem.Main.Models;
using PosSystem.Main.Helpers;

namespace PosSystem.Main.Templates
{
    // SỬA TÊN CLASS Ở ĐÂY: Phải là KitchenTemplate, không phải BillTemplate
    public partial class KitchenTemplate : UserControl
    {
        public KitchenTemplate()
        {
            InitializeComponent();
        }

        // Hàm SetData nhận cấu hình layout từ DB
        public void SetData(Order order, int batchNumber, List<PrintElement> layoutElements)
        {
            RootPanel.Children.Clear();

            // Nếu chưa có cấu hình layout, chạy mặc định
            if (layoutElements == null || layoutElements.Count == 0)
            {
                RenderDefault(order, batchNumber);
                return;
            }

            // VẼ THEO CẤU HÌNH (Dynamic Layout)
            foreach (var el in layoutElements)
            {
                if (!el.IsVisible) continue;

                switch (el.ElementType)
                {
                    case "Text":
                    case "KitchenOrderInfo":
                    case "TableNumberBig":
                    case "BatchNumber":
                        string final = PrintContentHelper.ReplacePlaceholders(el.Content, order, batchNumber);
                        AddTextBlock(final, el);
                        break;
                }
            }
        }

        private void AddTextBlock(string text, PrintElement style)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = style.FontSize > 0 ? style.FontSize : 14,
                FontWeight = style.IsBold ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (style.Align == "Center") tb.TextAlignment = TextAlignment.Center;
            else if (style.Align == "Right") tb.TextAlignment = TextAlignment.Right;
            else tb.TextAlignment = TextAlignment.Left;

            RootPanel.Children.Add(tb);
        }

        private void AddSeparator()
        {
            var line = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Margin = new Thickness(0, 5, 0, 5),
                SnapsToDevicePixels = true
            };
            RootPanel.Children.Add(line);
        }

        private void RenderKitchenDetails(Order order, int fontSize)
        {
            var items = order.OrderDetails.ToList();
            foreach (var d in items)
            {
                // Logic HỦY / THÊM
                string txt = d.Quantity < 0 ? $"[HỦY] {Math.Abs(d.Quantity)} x {d.Dish?.DishName}" : $"{d.Quantity} x {d.Dish?.DishName}";
                var brush = d.Quantity < 0 ? Brushes.Red : Brushes.Black;

                var tb = new TextBlock
                {
                    Text = txt,
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = brush
                };
                RootPanel.Children.Add(tb);

                // Ghi chú
                if (!string.IsNullOrEmpty(d.Note))
                {
                    var note = new TextBlock { Text = $"   📝 {d.Note}", FontStyle = FontStyles.Italic, FontSize = fontSize - 6, FontWeight = FontWeights.SemiBold };
                    RootPanel.Children.Add(note);
                }

                // Kẻ ngăn cách mờ giữa các món
                var sep = new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    Margin = new Thickness(0, 5, 0, 5)
                };
                RootPanel.Children.Add(sep);
            }
        }
        private string ReplacePlaceholders(string content, Order order, int batchNumber)
        {
            if (string.IsNullOrEmpty(content)) return "";
            string res = content;
            res = res.Replace("{Table}", order.Table?.TableName ?? "Mang về");
            res = res.Replace("{Date}", DateTime.Now.ToString("dd/MM/yyyy"));
            res = res.Replace("{Time}", DateTime.Now.ToString("HH:mm"));
            res = res.Replace("{Batch}", batchNumber.ToString()); // Bếp có thêm biến đợt
            return res;
        }
        private void RenderDefault(Order order, int batchNumber)
        {
            AddTextBlock($"ĐỢT: {batchNumber}", new PrintElement { IsBold = true, FontSize = 20, Align = "Center" });
            AddSeparator();
            AddTextBlock($"Bàn: {order.Table?.TableName}", new PrintElement { IsBold = true, FontSize = 24 });
            AddSeparator();
            RenderKitchenDetails(order, 24);
        }
    }
}
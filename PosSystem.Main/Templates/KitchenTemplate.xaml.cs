using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PosSystem.Main.Helpers; // Dùng Helper thay thế biến số
using PosSystem.Main.Models;

namespace PosSystem.Main.Templates
{
    public partial class KitchenTemplate : UserControl
    {
        public KitchenTemplate()
        {
            InitializeComponent();
        }

        public void SetData(Order order, int batchNumber, List<PrintElement> layoutElements)
        {
            RootPanel.Children.Clear();

            // Nếu không có layout, dùng mặc định
            if (layoutElements == null || layoutElements.Count == 0) return;

            foreach (var el in layoutElements)
            {
                if (!el.IsVisible) continue;

                switch (el.ElementType)
                {
                    case "Text":
                    case "KitchenOrderInfo":
                    case "TableNumberBig":
                    case "BatchNumber":
                        // Dùng Helper thay thế {Table}, {Batch}...
                        string finalContent = PrintContentHelper.ReplacePlaceholders(el.Content, order, batchNumber);
                        AddTextBlock(finalContent, el);
                        break;

                    case "Separator":
                        AddSeparator();
                        break;

                    case "KitchenOrderDetails":
                        // QUAN TRỌNG: Truyền el.Content (chứa cấu hình NoteSize) vào hàm
                        RenderKitchenDetails(order, el.FontSize, el.Content);
                        break;
                }
            }
        }

        private void RenderKitchenDetails(Order order, int fontSize, string config)
        {
            // 1. Parse cấu hình (Cỡ chữ Note...)
            bool showNote = !config.Contains("ShowNote=False");
            int noteSize = Math.Max(18, fontSize - 4); // Mặc định Note bếp to hơn Bill chút cho dễ đọc

            if (!string.IsNullOrEmpty(config))
            {
                var parts = config.Split(';');
                foreach (var p in parts)
                {
                    if (p.StartsWith("NoteSize=") && int.TryParse(p.Split('=')[1], out int s))
                        noteSize = s;
                }
            }

            // 2. Vẽ danh sách món
            var items = order.OrderDetails.ToList();
            if (items.Count == 0) return;

            foreach (var d in items)
            {
                // Logic hiển thị HỦY MÓN / THÊM MÓN
                string txt = d.Quantity < 0
                    ? $"[HỦY] {Math.Abs(d.Quantity)} x {d.Dish?.DishName}"
                    : $"{d.Quantity} x {d.Dish?.DishName}";

                var brush = d.Quantity < 0 ? Brushes.Red : Brushes.Black; // Hủy màu đỏ (nếu in màu), hoặc Đen

                // Tên món (In đậm, To)
                var tb = new TextBlock
                {
                    Text = txt,
                    FontSize = fontSize > 0 ? fontSize : 24,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = brush,
                    Margin = new Thickness(0, 5, 0, 2)
                };
                RootPanel.Children.Add(tb);

                // Ghi chú (Nghiêng, Nhỏ hơn chút)
                if (showNote && !string.IsNullOrEmpty(d.Note))
                {
                    var note = new TextBlock
                    {
                        Text = $"   📝 {d.Note}",
                        FontStyle = FontStyles.Italic,
                        FontSize = noteSize,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    RootPanel.Children.Add(note);
                }

                // Đường kẻ mờ ngăn cách giữa các món cho dễ nhìn
                var sep = new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    Margin = new Thickness(0, 5, 0, 5),
                    SnapsToDevicePixels = true,
                    Opacity = 0.5
                };
                RootPanel.Children.Add(sep);
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
                Height = 2, // Kẻ bếp dày hơn chút
                Stroke = Brushes.Black,
                StrokeThickness = 2,
                Margin = new Thickness(0, 5, 0, 5),
                SnapsToDevicePixels = true
            };
            RootPanel.Children.Add(line);
        }
    }
}
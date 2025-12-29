using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PosSystem.Main.Helpers; // Đảm bảo đã có file Helper này
using PosSystem.Main.Models;

namespace PosSystem.Main.Templates
{
    public partial class BillTemplate : UserControl
    {
        public BillTemplate()
        {
            InitializeComponent();
        }

        public void SetData(Order order, List<PrintElement> layoutElements)
        {
            RootPanel.Children.Clear();

            // Nếu không có layout, chạy mặc định (tùy chọn)
            if (layoutElements == null || layoutElements.Count == 0) return;

            foreach (var el in layoutElements)
            {
                if (!el.IsVisible) continue;

                switch (el.ElementType)
                {
                    case "Text":
                    case "OrderInfo":
                    case "TableNumberBig":
                        // Dùng Helper để thay thế biến số {Table}, {Staff}...
                        string finalContent = PrintContentHelper.ReplacePlaceholders(el.Content, order);
                        AddTextBlock(finalContent, el);
                        break;

                    case "Separator":
                        AddSeparator();
                        break;

                    case "Logo":
                    case "QRCode":
                        AddImage(el.Content, el.Align);
                        break;

                    case "OrderDetails":
                        // Truyền el.Content (chứa cấu hình ShowNote, NoteSize) vào hàm
                        RenderOrderDetails(order, el.FontSize, el.Content);
                        break;

                    case "Total":
                        // Truyền el (chứa cấu hình ShowSub, SubSize) vào hàm
                        RenderTotal(order, el);
                        break;
                }
            }
        }

        // --- HÀM VẼ DANH SÁCH MÓN (Đã sửa lỗi mainFontSize) ---
        // --- SỬA HÀM NÀY ĐỂ TĂNG KHOẢNG CÁCH CỘT ---
        // --- HÀM VẼ DANH SÁCH MÓN (CẬP NHẬT: ItemSize + Kẻ dọc) ---
        private void RenderOrderDetails(Order order, int fontSize, string config)
        {
            // 1. Parse cấu hình
            bool showNote = !config.Contains("ShowNote=False");
            int noteSize = Math.Max(10, fontSize - 2);
            int itemSize = fontSize; // Mặc định lấy theo fontSize chung

            if (!string.IsNullOrEmpty(config))
            {
                var parts = config.Split(';');
                foreach (var p in parts)
                {
                    if (p.StartsWith("NoteSize=") && int.TryParse(p.Split('=')[1], out int s)) noteSize = s;
                    if (p.StartsWith("ItemSize=") && int.TryParse(p.Split('=')[1], out int i)) itemSize = i;
                }
            }

            // 2. Định nghĩa cấu trúc cột chung (5 cột: Tên | Kẻ | SL | Kẻ | Tiền)
            // Width tham khảo: Tên(*), Kẻ(10px), SL(40px), Kẻ(10px), Tiền(Auto)
            Action<Grid> setupColumns = (g) =>
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0. Tên
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });                   // 1. Kẻ
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 35 });       // 2. SL
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });                   // 3. Kẻ
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 4. Tiền
            };

            // 3. Vẽ Header
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            setupColumns(headerGrid);

            var lblName = new TextBlock { Text = "Món", FontWeight = FontWeights.Bold, FontSize = itemSize };
            var lblQty = new TextBlock { Text = "SL", FontWeight = FontWeights.Bold, FontSize = itemSize, HorizontalAlignment = HorizontalAlignment.Center };
            var lblTotal = new TextBlock { Text = "Tiền", FontWeight = FontWeights.Bold, FontSize = itemSize, HorizontalAlignment = HorizontalAlignment.Right };

            Grid.SetColumn(lblName, 0);
            Grid.SetColumn(lblQty, 2);
            Grid.SetColumn(lblTotal, 4);

            headerGrid.Children.Add(lblName);
            headerGrid.Children.Add(lblQty);
            headerGrid.Children.Add(lblTotal);

            // Vẽ đường kẻ dọc cho Header (tùy chọn, ở đây tôi chỉ kẻ ngang phân cách)
            RootPanel.Children.Add(headerGrid);

            // Đường kẻ ngang đậm phân cách Header
            RootPanel.Children.Add(new System.Windows.Shapes.Rectangle { Height = 2, Fill = Brushes.Black, Margin = new Thickness(0, 0, 0, 5) });

            // 4. Vẽ từng dòng món ăn
            foreach (var d in order.OrderDetails)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                setupColumns(row);

                // Nội dung
                var txtName = new TextBlock { Text = d.Dish?.DishName, TextWrapping = TextWrapping.Wrap, FontSize = itemSize };
                var txtQty = new TextBlock { Text = d.Quantity.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.Bold, FontSize = itemSize };
                var txtPrice = new TextBlock { Text = d.TotalAmount.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right, FontSize = itemSize };

                // Đường kẻ dọc mờ (Màu xám nhạt hoặc đen nét đứt)
                var vLine1 = new System.Windows.Shapes.Rectangle { Width = 1, Fill = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };
                var vLine2 = new System.Windows.Shapes.Rectangle { Width = 1, Fill = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };

                // Gán cột
                Grid.SetColumn(txtName, 0);
                Grid.SetColumn(vLine1, 1);
                Grid.SetColumn(txtQty, 2);
                Grid.SetColumn(vLine2, 3);
                Grid.SetColumn(txtPrice, 4);

                row.Children.Add(txtName);
                row.Children.Add(vLine1);
                row.Children.Add(txtQty);
                row.Children.Add(vLine2);
                row.Children.Add(txtPrice);

                RootPanel.Children.Add(row);

                // Note (Ghi chú)
                if (showNote && !string.IsNullOrEmpty(d.Note))
                {
                    var txtNote = new TextBlock
                    {
                        Text = $"({d.Note})",
                        FontStyle = FontStyles.Italic,
                        FontSize = noteSize,
                        Foreground = Brushes.Black,
                        Margin = new Thickness(10, 0, 0, 2) // Thụt đầu dòng
                    };
                    RootPanel.Children.Add(txtNote);
                }

                // Kẻ mờ ngăn cách từng món (tùy chọn, nếu rối mắt có thể bỏ)
                RootPanel.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Fill = Brushes.Black,
                    Opacity = 0.2,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }

            // Đường kẻ ngang kết thúc list
            AddSeparator();
        }
        // --- HÀM VẼ TỔNG TIỀN (Hỗ trợ Tạm tính, Thuế, Cỡ chữ riêng) ---
        private void RenderTotal(Order order, PrintElement el)
        {
            // Parse cấu hình
            bool showSub = el.Content.Contains("ShowSub=True");
            bool showDisc = el.Content.Contains("ShowDisc=True");

            // Lấy cỡ chữ phụ (cho Tạm tính, Thuế)
            int subSize = Math.Max(12, el.FontSize - 2);
            var parts = el.Content.Split(';');
            foreach (var p in parts)
            {
                if (p.StartsWith("SubSize=") && int.TryParse(p.Split('=')[1], out int s))
                    subSize = s;
            }

            // 1. TẠM TÍNH
            if (showSub)
            {
                AddRowTotal("Tạm tính:", order.SubTotal.ToString("N0"), subSize);
            }

            // 2. GIẢM GIÁ / THUẾ
            if (showDisc)
            {
                if (order.DiscountAmount > 0 || order.DiscountPercent > 0)
                {
                    string discText = order.DiscountAmount > 0 ? $"-{order.DiscountAmount:N0}" : $"-{order.DiscountPercent}%";
                    AddRowTotal("Giảm giá:", discText, subSize);
                }
                if (order.TaxAmount > 0)
                {
                    AddRowTotal("Thuế (VAT):", order.TaxAmount.ToString("N0"), subSize);
                }
            }

            // 3. TỔNG CỘNG (Luôn hiện & Dùng cỡ chữ chính el.FontSize)
            var dock = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var lbl = new TextBlock { Text = "TỔNG CỘNG:", FontWeight = FontWeights.Bold, FontSize = el.FontSize };
            // Giá tiền tổng cộng làm to hơn chữ label 1 chút cho nổi
            var val = new TextBlock { Text = order.FinalAmount.ToString("N0"), FontWeight = FontWeights.Bold, FontSize = el.FontSize + 4, HorizontalAlignment = HorizontalAlignment.Right };

            DockPanel.SetDock(lbl, Dock.Left);
            dock.Children.Add(lbl);
            dock.Children.Add(val);
            RootPanel.Children.Add(dock);
        }

        // --- CÁC HÀM HỖ TRỢ ---
        private void AddRowTotal(string label, string value, int fontSize)
        {
            var dock = new DockPanel();
            var lbl = new TextBlock { Text = label, FontSize = fontSize };
            var val = new TextBlock { Text = value, FontSize = fontSize, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(lbl, Dock.Left);
            dock.Children.Add(lbl);
            dock.Children.Add(val);
            RootPanel.Children.Add(dock);
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
                StrokeDashArray = new DoubleCollection { 4, 2 }, // Nét đứt
                Margin = new Thickness(0, 5, 0, 5),
                SnapsToDevicePixels = true
            };
            RootPanel.Children.Add(line);
        }

        private void AddImage(string fileName, string align)
        {
            try
            {
                string path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Images", fileName);
                if (File.Exists(path))
                {
                    var img = new Image { Source = new BitmapImage(new Uri(path)), Height = 100, Stretch = Stretch.Uniform };

                    if (align == "Center") img.HorizontalAlignment = HorizontalAlignment.Center;
                    else if (align == "Right") img.HorizontalAlignment = HorizontalAlignment.Right;
                    else img.HorizontalAlignment = HorizontalAlignment.Left;

                    RootPanel.Children.Add(img);
                }
            }
            catch { }
        }
    }
}
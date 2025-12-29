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

        public void SetData(Order order, List<PrintElement> layoutElements, string paymentMethod = "Cash")
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
                        AddImage(el.Content, el.Align, el.ImageHeight);
                        break;

                    case "QRCode":
                        // Chỉ hiển thị QR code khi phương thức thanh toán là Transfer (QR)
                        if (paymentMethod == "Transfer")
                        {
                            AddImage(el.Content, el.Align, el.ImageHeight);
                        }
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

        // --- HÀM VẼ DANH SÁCH MÓN (CẬP NHẬT: HeaderSize, ItemSize, ColumnSpacing) ---
        private void RenderOrderDetails(Order order, int fontSize, string config)
        {
            // 1. Parse cấu hình
            bool showNote = !config.Contains("ShowNote=False");
            int noteSize = Math.Max(10, fontSize - 2);
            int itemSize = fontSize; // Mặc định lấy theo fontSize chung
            int headerSize = fontSize; // Mặc định lấy theo fontSize chung
            int columnSpacing = 10; // Mặc định 10px

            if (!string.IsNullOrEmpty(config))
            {
                var parts = config.Split(';');
                foreach (var p in parts)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var kv = p.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                    {
                        if (kv[0] == "NoteSize" && int.TryParse(kv[1], out int s)) noteSize = s;
                        else if (kv[0] == "ItemSize" && int.TryParse(kv[1], out int i)) itemSize = i;
                        else if (kv[0] == "HeaderSize" && int.TryParse(kv[1], out int h)) headerSize = h;
                        else if (kv[0] == "ColumnSpacing" && int.TryParse(kv[1], out int c)) columnSpacing = c;
                    }
                }
            }

            // 2. Định nghĩa cấu trúc cột (7 cột: Tên | Spacing | Kẻ | SL | Kẻ | Spacing | Tiền)
            // ColumnSpacing dùng để di chuyển cột SL sang trái/phải, không ảnh hưởng width của cột kẻ
            // Cột kẻ luôn có width cố định 5px để đường kẻ | | hiển thị rõ
            const int lineColumnWidth = 5; // Width cố định cho cột kẻ
            const int qtyColumnWidth = 40; // Width cố định cho cột số lượng
            const int priceColumnWidth = 120; // Width cố định cho cột tiền (đủ để hiển thị 1.000.000)

            Action<Grid> setupColumns = (g) =>
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0. Tên
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnSpacing) });        // 1. Spacing trái
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lineColumnWidth) });      // 2. Kẻ trái - width cố định
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(qtyColumnWidth) });       // 3. SL - width cố định
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lineColumnWidth) });      // 4. Kẻ phải - width cố định
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnSpacing) });        // 5. Spacing phải
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(priceColumnWidth) });     // 6. Tiền - width cố định 120px
            };

            // 3. Vẽ Header
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            setupColumns(headerGrid);

            var lblName = new TextBlock { Text = "Món", FontWeight = FontWeights.Bold, FontSize = headerSize };
            var lblQty = new TextBlock { Text = "SL", FontWeight = FontWeights.Bold, FontSize = headerSize, HorizontalAlignment = HorizontalAlignment.Center };
            var lblTotal = new TextBlock { Text = "Tiền", FontWeight = FontWeights.Bold, FontSize = headerSize, HorizontalAlignment = HorizontalAlignment.Right };

            // Đường kẻ dọc cho Header (để align với data rows) - luôn hiển thị rõ
            var headerVLine1 = new System.Windows.Shapes.Rectangle
            {
                Width = 1,
                Fill = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var headerVLine2 = new System.Windows.Shapes.Rectangle
            {
                Width = 1,
                Fill = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            Grid.SetColumn(lblName, 0);
            Grid.SetColumn(headerVLine1, 2); // Kẻ trái ở cột 2
            Grid.SetColumn(lblQty, 3);       // SL ở cột 3
            Grid.SetColumn(headerVLine2, 4); // Kẻ phải ở cột 4
            Grid.SetColumn(lblTotal, 6);     // Tiền ở cột 6

            headerGrid.Children.Add(lblName);
            headerGrid.Children.Add(headerVLine1);
            headerGrid.Children.Add(lblQty);
            headerGrid.Children.Add(headerVLine2);
            headerGrid.Children.Add(lblTotal);

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
                var txtQty = new TextBlock { Text = d.Quantity.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontSize = itemSize };
                var txtPrice = new TextBlock { Text = d.TotalAmount.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right, FontSize = itemSize };

                // Đường kẻ dọc - luôn hiển thị rõ để bao quanh cột số lượng
                var vLine1 = new System.Windows.Shapes.Rectangle
                {
                    Width = 1,
                    Fill = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                var vLine2 = new System.Windows.Shapes.Rectangle
                {
                    Width = 1,
                    Fill = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                // Gán cột
                Grid.SetColumn(txtName, 0);
                Grid.SetColumn(vLine1, 2);   // Kẻ trái ở cột 2
                Grid.SetColumn(txtQty, 3);   // SL ở cột 3
                Grid.SetColumn(vLine2, 4);   // Kẻ phải ở cột 4
                Grid.SetColumn(txtPrice, 6); // Tiền ở cột 6

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

            // Lấy cỡ chữ phụ (cho Tạm tính, Thuế) và cỡ chữ header Tổng cộng
            int subSize = Math.Max(12, el.FontSize - 2);
            int totalHeaderSize = el.FontSize; // Mặc định dùng FontSize
            if (!string.IsNullOrEmpty(el.Content))
            {
                var parts = el.Content.Split(';');
                foreach (var p in parts)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var kv = p.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                    {
                        if (kv[0] == "SubSize" && int.TryParse(kv[1], out int s))
                            subSize = s;
                        else if (kv[0] == "TotalHeaderSize" && int.TryParse(kv[1], out int t))
                            totalHeaderSize = t;
                    }
                }
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

            // 3. TỔNG CỘNG (Dùng cỡ chữ totalHeaderSize)
            var dock = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var lbl = new TextBlock { Text = "TỔNG CỘNG:", FontWeight = FontWeights.Bold, FontSize = totalHeaderSize };
            // Giá tiền tổng cộng làm to hơn chữ label 1 chút cho nổi
            var val = new TextBlock { Text = order.FinalAmount.ToString("N0"), FontWeight = FontWeights.Bold, FontSize = totalHeaderSize + 4, HorizontalAlignment = HorizontalAlignment.Right };

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

        private void AddImage(string fileName, string align, int imageHeight = 300)
        {
            try
            {
                string path = fileName;

                // Nếu là đường dẫn tuyệt đối, sử dụng trực tiếp
                if (Path.IsPathRooted(fileName) && File.Exists(fileName))
                {
                    path = fileName;
                }
                // Nếu là tên file, tìm trong thư mục Images
                else
                {
                    path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Images", fileName);
                }

                if (File.Exists(path))
                {
                    // Tạo BitmapImage với UriKind.Absolute
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var img = new Image
                    {
                        Source = bitmap,
                        Height = imageHeight,
                        Stretch = Stretch.Uniform  // Giữ nguyên aspect ratio, width tự động
                    };

                    // Wrap ảnh trong Border để cố định kích thước
                    var border = new System.Windows.Controls.Border
                    {
                        Child = img,
                        HorizontalAlignment = align == "Center" ? HorizontalAlignment.Center :
                                             (align == "Right" ? HorizontalAlignment.Right : HorizontalAlignment.Left),
                        Margin = new Thickness(0, 10, 0, 10)
                    };

                    RootPanel.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddImage Error] {ex.Message}");
            }
        }
    }
}
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PosSystem.Main.Models;
using System.Text.RegularExpressions;
using PosSystem.Main.Helpers;

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
            RootPanel.Children.Clear(); // Xóa sạch giao diện cũ


            // VẼ THEO LAYOUT CẤU HÌNH
            foreach (var el in layoutElements)
            {
                if (!el.IsVisible) continue;

                switch (el.ElementType)
                {
                    case "Text":
                    case "OrderInfo":      // Gộp chung xử lý như Text
                    case "TableNumberBig": // Gộp chung
                                           // Dùng Helper để thay thế biến số
                        string finalContent = PrintContentHelper.ReplacePlaceholders(el.Content, order);
                        AddTextBlock(finalContent, el);
                        break;

                    case "Separator": AddSeparator(); break;
                    case "Logo": AddImage(el.Content, el.Align); break;

                    case "OrderDetails":
                        RenderOrderDetails(order, el.FontSize, el.Content); // Truyền el.Content vào
                        break;
                    case "Total": RenderTotal(order, el); break;
                }
            }
        }
        // --- HÀM MỚI: THAY THẾ BIẾN SỐ ---
        private string ReplacePlaceholders(string content, Order order)
        {
            if (string.IsNullOrEmpty(content)) return "";

            string res = content;
            res = res.Replace("{Table}", order.Table?.TableName ?? "Mang về");
            res = res.Replace("{Date}", order.OrderTime.ToString("dd/MM/yyyy"));
            res = res.Replace("{Time}", order.OrderTime.ToString("HH:mm"));
            res = res.Replace("{Staff}", "Admin"); // Hoặc lấy từ order.Account.FullName
            res = res.Replace("{OrderId}", order.OrderID.ToString());

            return res;
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

            // Xử lý căn lề
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

        private void AddImage(string fileName, string align)
        {
            // Logic load ảnh (giữ nguyên logic cũ hoặc copy từ LayoutDesigner)
            try
            {
                string path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Images", fileName);
                if (File.Exists(path))
                {
                    var img = new Image { Source = new BitmapImage(new Uri(path)), Height = 100, Stretch = Stretch.Uniform };
                    // Căn lề ảnh
                    if (align == "Center") img.HorizontalAlignment = HorizontalAlignment.Center;
                    else if (align == "Right") img.HorizontalAlignment = HorizontalAlignment.Right;
                    else img.HorizontalAlignment = HorizontalAlignment.Left;

                    RootPanel.Children.Add(img);
                }
            }
            catch { }
        }

        private void RenderOrderDetails(Order order, int fontSize, string config)
        {
            // Mặc định hiện note nếu không có cấu hình "ShowNote=False"
            bool showNote = !config.Contains("ShowNote=False");
            // Header bảng món
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Tên
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // SL
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Tiền

            var lblName = new TextBlock { Text = "Món", FontWeight = FontWeights.Bold, FontSize = fontSize };
            var lblQty = new TextBlock { Text = "SL", FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 10, 0), FontSize = fontSize };
            var lblTotal = new TextBlock { Text = "Tiền", FontWeight = FontWeights.Bold, FontSize = fontSize };

            Grid.SetColumn(lblName, 0);
            Grid.SetColumn(lblQty, 1);
            Grid.SetColumn(lblTotal, 2);

            headerGrid.Children.Add(lblName);
            headerGrid.Children.Add(lblQty);
            headerGrid.Children.Add(lblTotal);
            RootPanel.Children.Add(headerGrid);
            AddSeparator();

            // List món
            foreach (var d in order.OrderDetails)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txtName = new TextBlock { Text = d.Dish?.DishName, TextWrapping = TextWrapping.Wrap, FontSize = fontSize };
                var txtQty = new TextBlock { Text = d.Quantity.ToString(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), FontSize = fontSize };
                var txtPrice = new TextBlock { Text = d.TotalAmount.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right, FontSize = fontSize };

                Grid.SetColumn(txtName, 0);
                Grid.SetColumn(txtQty, 1);
                Grid.SetColumn(txtPrice, 2);

                row.Children.Add(txtName);
                row.Children.Add(txtQty);
                row.Children.Add(txtPrice);
                RootPanel.Children.Add(row);

                // CHỈ IN GHI CHÚ NẾU ĐƯỢC BẬT
                if (showNote && !string.IsNullOrEmpty(d.Note))
                {
                    var txtNote = new TextBlock { Text = $"  ({d.Note})", FontStyle = FontStyles.Italic, FontSize = fontSize - 2, Foreground = Brushes.Black };
                    RootPanel.Children.Add(txtNote);
                }
            }
            AddSeparator();
        }

        private void RenderTotal(Order order, PrintElement el)
        {
            bool showSub = el.Content.Contains("ShowSub=True");
            bool showDisc = el.Content.Contains("ShowDisc=True");

            // 1. TẠM TÍNH
            if (showSub)
            {
                AddRowTotal("Tạm tính:", order.SubTotal.ToString("N0"), el.FontSize);
            }

            // 2. GIẢM GIÁ / THUẾ
            if (showDisc)
            {
                if (order.DiscountAmount > 0 || order.DiscountPercent > 0)
                {
                    string discText = order.DiscountAmount > 0 ? $"-{order.DiscountAmount:N0}" : $"-{order.DiscountPercent}%";
                    AddRowTotal("Giảm giá:", discText, el.FontSize);
                }
                if (order.TaxAmount > 0)
                {
                    AddRowTotal("Thuế (VAT):", order.TaxAmount.ToString("N0"), el.FontSize);
                }
            }

            // 3. TỔNG CỘNG (Luôn hiện & To hơn)
            var dock = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var lbl = new TextBlock { Text = "TỔNG CỘNG:", FontWeight = FontWeights.Bold, FontSize = el.FontSize + 2 };
            var val = new TextBlock { Text = order.FinalAmount.ToString("N0"), FontWeight = FontWeights.Bold, FontSize = el.FontSize + 6, HorizontalAlignment = HorizontalAlignment.Right };

            DockPanel.SetDock(lbl, Dock.Left);
            dock.Children.Add(lbl);
            dock.Children.Add(val);
            RootPanel.Children.Add(dock);
        }

        // Hàm phụ để add dòng nhỏ
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
        /*
                private void RenderDefault(Order order)
                {
                    // (Code hardcode cũ để fallback)
                    AddTextBlock("HÓA ĐƠN", new PrintElement { Align = "Center", IsBold = true, FontSize = 20 });
                    AddSeparator();
                    RenderOrderDetails(order, 14);
                    AddSeparator();
                    RenderTotal(order, new PrintElement { FontSize = 16 });
                }
                */
    }
}
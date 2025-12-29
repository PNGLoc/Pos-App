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
            // 1. Parse cấu hình
            bool showNote = !config.Contains("ShowNote=False");
            int noteSize = Math.Max(10, fontSize - 2);
            int itemSize = fontSize > 0 ? fontSize : 14;
            int headerSize = fontSize > 0 ? fontSize : 14;
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
                        if (kv[0] == "NoteSize" && int.TryParse(kv[1], out int s))
                            noteSize = s;
                        else if (kv[0] == "ItemSize" && int.TryParse(kv[1], out int i))
                            itemSize = i;
                        else if (kv[0] == "HeaderSize" && int.TryParse(kv[1], out int h))
                            headerSize = h;
                        else if (kv[0] == "ColumnSpacing" && int.TryParse(kv[1], out int c))
                            columnSpacing = c;
                    }
                }
            }

            // 2. Định nghĩa cấu trúc cột (5 cột: Tên | Kẻ | SL | Kẻ | Padding)
            Action<Grid> setupColumns = (g) =>
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0. Tên
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnSpacing) });        // 1. Kẻ
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 35 });       // 2. SL
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnSpacing) });        // 3. Kẻ
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 4. Padding (không dùng nhưng giữ cấu trúc)
            };

            // 3. Vẽ Header (tùy chọn)
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            setupColumns(headerGrid);

            var lblName = new TextBlock { Text = "Món", FontWeight = FontWeights.Bold, FontSize = headerSize };
            var lblQty = new TextBlock { Text = "SL", FontWeight = FontWeights.Bold, FontSize = headerSize, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(lblName, 0);
            Grid.SetColumn(lblQty, 2);
            headerGrid.Children.Add(lblName);
            headerGrid.Children.Add(lblQty);
            RootPanel.Children.Add(headerGrid);

            // Đường kẻ ngang đậm phân cách Header
            RootPanel.Children.Add(new System.Windows.Shapes.Rectangle { Height = 2, Fill = Brushes.Black, Margin = new Thickness(0, 0, 0, 5) });

            // 4. Vẽ danh sách món
            var items = order.OrderDetails.ToList();
            if (items.Count == 0) return;

            foreach (var d in items)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                setupColumns(row);

                // Logic hiển thị HỦY MÓN / THÊM MÓN
                string dishName = d.Dish?.DishName ?? "";
                bool isCancel = d.Quantity < 0;
                int absQuantity = Math.Abs(d.Quantity);
                
                var brush = isCancel ? Brushes.Red : Brushes.Black;

                // Tên món
                string nameText = isCancel ? $"[HỦY] {dishName}" : dishName;
                var txtName = new TextBlock
                {
                    Text = nameText,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = itemSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = brush
                };

                // Số lượng (hiển thị ở cột phải)
                var txtQty = new TextBlock
                {
                    Text = absQuantity.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = itemSize,
                    Foreground = brush
                };

                // Đường kẻ dọc mờ
                var vLine1 = new System.Windows.Shapes.Rectangle { Width = 1, Fill = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };
                var vLine2 = new System.Windows.Shapes.Rectangle { Width = 1, Fill = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center };

                // Gán cột
                Grid.SetColumn(txtName, 0);
                Grid.SetColumn(vLine1, 1);
                Grid.SetColumn(txtQty, 2);
                Grid.SetColumn(vLine2, 3);

                row.Children.Add(txtName);
                row.Children.Add(vLine1);
                row.Children.Add(txtQty);
                row.Children.Add(vLine2);
                RootPanel.Children.Add(row);

                // Ghi chú (Nghiêng, Nhỏ hơn chút)
                if (showNote && !string.IsNullOrEmpty(d.Note))
                {
                    var note = new TextBlock
                    {
                        Text = $"   ({d.Note})",
                        FontStyle = FontStyles.Italic,
                        FontSize = noteSize,
                        Margin = new Thickness(10, 0, 0, 2)
                    };
                    RootPanel.Children.Add(note);
                }

                // Đường kẻ mờ ngăn cách giữa các món
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
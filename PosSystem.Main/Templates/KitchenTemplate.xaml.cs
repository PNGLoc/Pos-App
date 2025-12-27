using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Templates
{
    public partial class KitchenTemplate : UserControl
    {
        public KitchenTemplate()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Render phiếu bếp từ order data và template layout
        /// </summary>
        public void SetData(Order order, int batchNumber = 0)
        {
            if (order == null)
            {
                return;
            }

            RootPanel.Children.Clear();

            using var db = new AppDbContext();

            // Lấy template kitchen đang active
            var template = db.PrintTemplates
                .FirstOrDefault(t => t.TemplateType == "Kitchen" && t.IsActive);

            if (template == null || string.IsNullOrEmpty(template.TemplateContentJson))
            {
                return;
            }

            // Deserialize layout từ JSON
            List<PrintElement>? layout;
            try
            {
                layout = JsonSerializer.Deserialize<List<PrintElement>>(template.TemplateContentJson);
            }
            catch
            {
                return;
            }

            if (layout == null || layout.Count == 0)
            {
                return;
            }

            // Render từng element trong layout
            foreach (var element in layout)
            {
                if (!element.IsVisible)
                {
                    continue;
                }

                RenderElement(element, order, batchNumber);
            }
        }

        /// <summary>
        /// Render một element dựa trên type
        /// </summary>
        private void RenderElement(PrintElement element, Order order, int batchNumber)
        {
            switch (element.ElementType)
            {
                case "Text":
                    RenderText(element);
                    break;
                case "Logo":
                    RenderImage(element, 200);
                    break;
                case "QRCode":
                    RenderImage(element, 250);
                    break;
                case "Separator":
                    RenderSeparator();
                    break;
                case "KitchenOrderInfo":
                    RenderKitchenOrderInfo(order, batchNumber);
                    break;
                case "KitchenOrderDetails":
                    RenderKitchenOrderDetails(order, batchNumber);
                    break;
                case "BatchNumber":
                    RenderBatchNumber(batchNumber);
                    break;
            }
        }

        /// <summary>
        /// Render text element
        /// </summary>
        private void RenderText(PrintElement element)
        {
            var textBlock = new TextBlock
            {
                Text = element.Content ?? string.Empty,
                FontSize = element.FontSize,
                FontWeight = element.IsBold ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };

            SetAlignment(textBlock, element.Align);
            RootPanel.Children.Add(textBlock);
        }

        /// <summary>
        /// Render image element (Logo hoặc QR Code)
        /// </summary>
        private void RenderImage(PrintElement element, double width)
        {
            if (string.IsNullOrEmpty(element.Content))
            {
                return;
            }

            string fullPath = Path.Combine(AppContext.BaseDirectory, "Images", element.Content);
            if (!File.Exists(fullPath))
            {
                return;
            }

            try
            {
                var image = new Image
                {
                    Width = width,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(fullPath);
                bitmap.EndInit();
                image.Source = bitmap;

                SetAlignment(image, element.Align);
                RootPanel.Children.Add(image);
            }
            catch
            {
                // Ignore image load errors
            }
        }

        /// <summary>
        /// Render separator line
        /// </summary>
        private void RenderSeparator()
        {
            var border = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 10, 0, 10),
                Height = 1,
                SnapsToDevicePixels = true
            };
            RootPanel.Children.Add(border);
        }

        /// <summary>
        /// Render thông tin đơn hàng bếp (bàn, số phiếu, giờ, đợt)
        /// </summary>
        private void RenderKitchenOrderInfo(Order order, int batchNumber)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string leftText = "Bàn:\nSố phiếu:\nGiờ:";
            if (batchNumber > 0)
            {
                leftText += "\nĐợt:";
            }

            string rightText = $"{order.Table?.TableName ?? "Mang về"}\n" +
                              $"#{order.OrderID}\n" +
                              $"{DateTime.Now:HH:mm}";
            if (batchNumber > 0)
            {
                rightText += $"\n{batchNumber}";
            }

            var labelBlock = new TextBlock
            {
                Text = leftText,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 18
            };

            var valueBlock = new TextBlock
            {
                Text = rightText,
                FontSize = 18
            };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            RootPanel.Children.Add(grid);
        }

        /// <summary>
        /// Render chi tiết các món trong đơn (chỉ món của đợt này)
        /// </summary>
        private void RenderKitchenOrderDetails(Order order, int batchNumber)
        {
            // Lấy danh sách món cần in (Đã được xử lý ở PrintService)
            var itemsToPrint = order.OrderDetails.ToList();

            if (itemsToPrint == null || itemsToPrint.Count == 0) return;

            foreach (var detail in itemsToPrint)
            {
                var stackPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 10) }; // Tăng Margin dưới chút cho thoáng

                // 1. XỬ LÝ TÊN MÓN & SỐ LƯỢNG
                string quantityText = "";
                string dishNameText = detail.Dish?.DishName ?? "Unknown";
                Brush textColor = Brushes.Black;

                if (detail.Quantity < 0)
                {
                    // Trường hợp HỦY MÓN
                    quantityText = $"[HỦY] {Math.Abs(detail.Quantity)}";
                    textColor = Brushes.Red;
                }
                else
                {
                    // Trường hợp THÊM MÓN
                    quantityText = $"{detail.Quantity}";
                    textColor = Brushes.Black;
                }

                var dishBlock = new TextBlock
                {
                    Text = $"{quantityText} x {dishNameText}",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = textColor
                };
                stackPanel.Children.Add(dishBlock);

                // 2. XỬ LÝ GHI CHÚ (THÊM PHẦN NÀY)
                if (!string.IsNullOrEmpty(detail.Note))
                {
                    var noteBlock = new TextBlock
                    {
                        Text = $"{detail.Note}", // Thêm icon hoặc dấu ngoặc
                        FontSize = 18,                 // Font nhỏ hơn tên món
                        FontStyle = FontStyles.Italic, // In nghiêng
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 2, 0, 0),
                        Foreground = Brushes.Black     // Màu đen cho dễ đọc
                    };
                    stackPanel.Children.Add(noteBlock);
                }

                RootPanel.Children.Add(stackPanel);

                // Thêm đường kẻ mờ ngăn cách giữa các món (tùy chọn)
                var separator = new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 2 }, // 4 điểm tô, 2 điểm hở (tạo nét đứt)
                    Margin = new Thickness(0, 0, 0, 5),
                    SnapsToDevicePixels = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                RootPanel.Children.Add(separator);
            }
        }
        /// <summary>
        /// Render số đợt
        /// </summary>
        private void RenderBatchNumber(int batchNumber)
        {
            if (batchNumber > 0)
            {
                var batchBlock = new TextBlock
                {
                    Text = $"ĐỢT {batchNumber}",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 5)
                };
                RootPanel.Children.Add(batchBlock);
            }
        }

        /// <summary>
        /// Set alignment cho FrameworkElement
        /// </summary>
        private void SetAlignment(FrameworkElement element, string align)
        {
            switch (align)
            {
                case "Center":
                    element.HorizontalAlignment = HorizontalAlignment.Center;
                    if (element is TextBlock textBlock)
                    {
                        textBlock.TextAlignment = TextAlignment.Center;
                    }
                    break;
                case "Right":
                    element.HorizontalAlignment = HorizontalAlignment.Right;
                    if (element is TextBlock textBlockRight)
                    {
                        textBlockRight.TextAlignment = TextAlignment.Right;
                    }
                    break;
                default:
                    element.HorizontalAlignment = HorizontalAlignment.Left;
                    if (element is TextBlock textBlockLeft)
                    {
                        textBlockLeft.TextAlignment = TextAlignment.Left;
                    }
                    break;
            }
        }
    }
}


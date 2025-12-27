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
    public partial class BillTemplate : UserControl
    {
        public BillTemplate()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Render hóa đơn từ order data và template layout
        /// </summary>
        public void SetData(Order order)
        {
            if (order == null)
            {
                return;
            }

            RootPanel.Children.Clear();

            using var db = new AppDbContext();
            
            // Lấy template bill đang active
            var template = db.PrintTemplates
                .FirstOrDefault(t => t.TemplateType == "Bill" && t.IsActive);
            
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

                RenderElement(element, order);
            }
        }

        /// <summary>
        /// Render một element dựa trên type
        /// </summary>
        private void RenderElement(PrintElement element, Order order)
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
                case "OrderInfo":
                    RenderOrderInfo(order);
                    break;
                case "OrderDetails":
                    RenderOrderDetails(order);
                    break;
                case "Total":
                    RenderTotal(order);
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
        /// Render thông tin đơn hàng (số phiếu, ngày, thu ngân)
        /// </summary>
        private void RenderOrderInfo(Order order)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string leftText = "Số phiếu:\nNgày:\nThu ngân:";
            string rightText = $"#{order.OrderID}\n" +
                              $"{(order.CheckoutTime ?? order.OrderTime):dd/MM/yyyy HH:mm}\n" +
                              $"{order.Account?.AccName ?? "N/A"}";

            var labelBlock = new TextBlock
            {
                Text = leftText,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var valueBlock = new TextBlock { Text = rightText };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);
            
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            RootPanel.Children.Add(grid);
        }

        /// <summary>
        /// Render chi tiết các món trong đơn
        /// </summary>
        private void RenderOrderDetails(Order order)
        {
            if (order.OrderDetails == null || order.OrderDetails.Count == 0)
            {
                return;
            }

            // Header
            var header = CreateDetailsHeader();
            RootPanel.Children.Add(header);

            // Items
            foreach (var detail in order.OrderDetails)
            {
                var row = CreateDetailRow(detail);
                RootPanel.Children.Add(row);
            }
        }

        /// <summary>
        /// Tạo header cho bảng chi tiết
        /// </summary>
        private Grid CreateDetailsHeader()
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var dishHeader = new TextBlock
            {
                Text = "Món",
                FontWeight = FontWeights.Bold,
                FontSize = 18
            };
            grid.Children.Add(dishHeader);

            var qtyHeader = new TextBlock
            {
                Text = "SL",
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                FontSize = 18
            };
            Grid.SetColumn(qtyHeader, 1);
            grid.Children.Add(qtyHeader);

            var totalHeader = new TextBlock
            {
                Text = "T.Tiền",
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right,
                FontSize = 18
            };
            Grid.SetColumn(totalHeader, 2);
            grid.Children.Add(totalHeader);

            return grid;
        }

        /// <summary>
        /// Tạo một dòng chi tiết món
        /// </summary>
        private Grid CreateDetailRow(OrderDetail detail)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var dishName = new TextBlock
            {
                Text = detail.Dish?.DishName ?? "N/A",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 18
            };
            grid.Children.Add(dishName);

            var quantity = new TextBlock
            {
                Text = detail.Quantity.ToString(),
                TextAlignment = TextAlignment.Center,
                FontSize = 18
            };
            Grid.SetColumn(quantity, 1);
            grid.Children.Add(quantity);

            var totalAmount = new TextBlock
            {
                Text = detail.TotalAmount.ToString("N0"),
                TextAlignment = TextAlignment.Right,
                FontSize = 18
            };
            Grid.SetColumn(totalAmount, 2);
            grid.Children.Add(totalAmount);

            return grid;
        }

        /// <summary>
        /// Render tổng tiền thanh toán
        /// </summary>
        private void RenderTotal(Order order)
        {
            var totalBlock = new TextBlock
            {
                Text = $"THANH TOÁN: {order.FinalAmount:N0}",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            RootPanel.Children.Add(totalBlock);
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
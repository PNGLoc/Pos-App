using System;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;

namespace PosSystem.Main
{
    public partial class PaymentWindow : Window
    {
        private int _orderId;
        private int _tableId;
        public bool IsPaidSuccess { get; private set; } = false; // Để MainWindow biết mà reload

        public PaymentWindow(int orderId)
        {
            InitializeComponent();
            _orderId = orderId;
            LoadOrderData();
        }

        private void LoadOrderData()
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders.Include(o => o.Table).FirstOrDefault(o => o.OrderID == _orderId);
                if (order != null)
                {
                    _tableId = order.TableID ?? 0;
                    lblOrderInfo.Text = $"Bàn: {order.Table?.TableName} - Đơn: #{order.OrderID}";

                    txtSubTotal.Text = order.SubTotal.ToString("N0") + "đ";

                    decimal discountVal = order.SubTotal - order.FinalAmount;
                    txtDiscount.Text = $"-{discountVal:N0}đ";

                    txtFinal.Text = order.FinalAmount.ToString("N0") + "đ";
                }
                else
                {
                    MessageBox.Show("Không tìm thấy đơn hàng!");
                    this.Close();
                }
            }
        }

        // 1. IN TẠM TÍNH (Chưa chốt đơn)
        private void BtnPrintCheck_Click(object sender, RoutedEventArgs e)
        {
            // Cập nhật PaymentMethod theo lựa chọn hiện tại
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.OrderID == _orderId);
                if (order != null)
                {
                    order.PaymentMethod = radCash.IsChecked == true ? "Cash" : "Transfer";
                    db.SaveChanges();
                }
            }

            // In bill với phương thức thanh toán vừa lưu
            PrintService.PrintBill(_orderId);
            ShowToast("✅ Đã gửi lệnh in tạm tính!");
        }

        private async void ShowToast(string message)
        {
            var border = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)),
                CornerRadius = new System.Windows.CornerRadius(5),
                Padding = new System.Windows.Thickness(20, 10, 20, 10),
                Margin = new System.Windows.Thickness(20)
            };

            var text = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                FontWeight = System.Windows.FontWeights.Bold
            };

            border.Child = text;

            // Thêm vào window (cuối cùng)
            var rootGrid = this.Content as System.Windows.Controls.Grid;
            if (rootGrid != null)
            {
                rootGrid.Children.Add(border);
                System.Windows.Controls.Grid.SetRow(border, 0);
                System.Windows.Controls.Grid.SetColumn(border, 0);
                System.Windows.Controls.Grid.SetColumnSpan(border, 100);
                System.Windows.Controls.Grid.SetRowSpan(border, 100);

                border.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                border.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

                await System.Threading.Tasks.Task.Delay(1500);
                rootGrid.Children.Remove(border);
            }
        }

        // 2. THANH TOÁN & ĐÓNG BÀN
        private void BtnPay_Click(object sender, RoutedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.OrderID == _orderId);
                if (order == null) return;

                // Cập nhật thông tin thanh toán
                order.OrderStatus = "Paid";
                order.CheckoutTime = DateTime.Now;
                // Lưu phương thức thanh toán: Cash hoặc Transfer (QR)
                order.PaymentMethod = radCash.IsChecked == true ? "Cash" : "Transfer";

                // Giải phóng bàn
                var table = db.Tables.Find(_tableId);
                if (table != null)
                {
                    table.TableStatus = "Empty";
                }

                db.SaveChanges();

                // In hóa đơn chính thức , nhưng chuyển qua mainwindow in luôn rồi
                // PrintService.PrintBill(_orderId);

                IsPaidSuccess = true;
                this.Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
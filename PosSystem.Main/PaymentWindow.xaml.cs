using System;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using Microsoft.AspNetCore.SignalR;
using PosSystem.Main.Server.Hubs;
using Microsoft.Extensions.DependencyInjection;
namespace PosSystem.Main
{
    public partial class PaymentWindow : Window
    {
        private int _orderId;
        private int _tableId;
        public bool IsPaidSuccess { get; private set; } = false;
        public bool IsProvisionalSuccess { get; private set; } = false; // [NEW] Flag for Provisional Print
        public bool ShouldPrint { get; private set; } = true;

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
                var order = db.Orders.Include(o => o.Table).Include(o => o.OrderDetails).FirstOrDefault(o => o.OrderID == _orderId);
                if (order != null)
                {
                    _tableId = order.TableID ?? 0;
                    lblOrderInfo.Text = $"Bàn: {order.Table?.TableName} - Đơn: #{order.OrderID}";

                    // 1. Tính Tổng Tiền Gốc (Chưa trừ giảm giá món)
                    // Lưu ý: Cần Include OrderDetails khi query
                    decimal originalTotal = order.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);
                    txtOriginalTotal.Text = originalTotal.ToString("N0") + "đ";

                    // 2. Tính Giảm Giá Món (Original - SubTotal)
                    // SubTotal là tổng tiền sau khi đã trừ giảm giá món (nhưng chưa trừ giảm bill)
                    decimal itemDiscount = originalTotal - order.SubTotal;
                    if (itemDiscount > 0)
                    {
                        txtItemDiscount.Text = $"-{itemDiscount:N0}đ";
                        pnlItemDiscount.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        pnlItemDiscount.Visibility = Visibility.Collapsed;
                    }

                    // 3. Tính Giảm Giá Bill (SubTotal - Final)
                    decimal billDiscount = order.SubTotal - order.FinalAmount;
                    if (billDiscount > 0)
                    {
                         txtBillDiscount.Text = $"-{billDiscount:N0}đ";
                         pnlBillDiscount.Visibility = Visibility.Visible;
                    }
                    else
                    {
                         pnlBillDiscount.Visibility = Visibility.Collapsed;
                    }

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
        private async void BtnPrintCheck_Click(object sender, RoutedEventArgs e)
        {
            // Cập nhật PaymentMethod theo lựa chọn hiện tại
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.OrderID == _orderId);
                if (order != null)
                {
                    order.PaymentMethod = radCash.IsChecked == true ? "Cash" : "Transfer";
                    
                    // [FIX] Cập nhật trạng thái đã in tạm tính
                    order.IsPreCalculated = true;
                    
                    db.SaveChanges();
                }
            }

            // In bill với phương thức thanh toán vừa lưu, chế độ Tạm Tính
            PrintService.PrintBill(_orderId, isProvisional: true);
            
            // [FIX] Set flag and Close
            IsProvisionalSuccess = true;
            this.Close();

            // Notify others
            if (App.WebHost != null)
            {
                var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                if (hubContext != null)
                {
                    await hubContext.Clients.All.SendAsync("TableUpdated", _tableId);
                }
            }
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
        private async void BtnPay_Click(object sender, RoutedEventArgs e)
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
                // --- [THÊM ĐOẠN SIGNALR NÀY] ---
                // Bắn tín hiệu cho Web/Mobile biết bàn này đã thay đổi (đã trống)
                if (App.WebHost != null)
                {
                    var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                    if (hubContext != null)
                    {
                        // _tableId là biến có sẵn trong class của bạn
                        await hubContext.Clients.All.SendAsync("TableUpdated", _tableId);
                    }
                }
                // -------------------------------

                IsPaidSuccess = true;
                ShouldPrint = chkNoPrint.IsChecked != true;
                this.Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
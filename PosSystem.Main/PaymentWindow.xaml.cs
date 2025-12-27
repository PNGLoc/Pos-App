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
            // Gọi hàm in bill (Hàm này đã viết ở bước trước, dùng Template ảnh)
            PrintService.PrintBill(_orderId);
            MessageBox.Show("Đã gửi lệnh in tạm tính!");
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
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;

namespace PosSystem.Main.Pages
{
    public partial class OrderHistoryPage : Page
    {
        public OrderHistoryPage()
        {
            InitializeComponent();
            dpFrom.SelectedDate = DateTime.Today;
            dpTo.SelectedDate = DateTime.Today;
            LoadData();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => LoadData();

        private void LoadData()
        {
            DateTime from = dpFrom.SelectedDate ?? DateTime.MinValue;
            DateTime to = dpTo.SelectedDate?.AddDays(1).AddTicks(-1) ?? DateTime.MaxValue;

            // Lấy giá trị lọc PTTT
            string ptttFilter = "";
            if (cboPaymentMethod.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                ptttFilter = item.Tag.ToString() ?? "";
            }

            using (var db = new AppDbContext())
            {
                // Query cơ bản: Đơn đã Paid hoặc Cancelled (Không lấy Pending)
                var query = db.Orders.Include(o => o.Table)
                    .Where(o => o.OrderTime >= from && o.OrderTime <= to && o.OrderStatus != "Pending");

                // Áp dụng lọc PTTT nếu có
                if (!string.IsNullOrEmpty(ptttFilter))
                {
                    query = query.Where(o => o.PaymentMethod == ptttFilter);
                }

                var list = query
                    .OrderByDescending(o => o.OrderTime)
                    .Select(o => new
                    {
                        o.OrderID,
                        TableName = o.Table != null ? o.Table.TableName : "Mang về",
                        o.OrderTime,
                        o.FinalAmount,
                        o.OrderStatus,
                        StatusDisplay = o.OrderStatus == "Paid" ? "Đã TT" : "Đã Hủy",
                        // Hiển thị PTTT tiếng Việt
                        PaymentMethodDisplay = o.PaymentMethod == "Transfer" ? "Chuyển khoản" : "Tiền mặt"
                    })
                    .ToList();

                dgOrders.ItemsSource = list;

                // Tính tổng chỉ với đơn đã thanh toán
                lblTotalRevenue.Text = list.Where(x => x.OrderStatus == "Paid").Sum(x => x.FinalAmount).ToString("N0") + "đ";
            }
        }

        private void BtnDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long id)
            {
                new OrderDetailWindow(id).ShowDialog();
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long id)
            {
                if (MessageBox.Show("Xóa vĩnh viễn đơn hàng này?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var order = db.Orders.Include(o => o.OrderDetails).FirstOrDefault(o => o.OrderID == id);
                        if (order != null)
                        {
                            db.OrderDetails.RemoveRange(order.OrderDetails);
                            db.Orders.Remove(order);
                            db.SaveChanges();
                            LoadData();
                        }
                    }
                }
            }
        }

        // --- CHỨC NĂNG XÓA TẤT CẢ (CÓ XÁC THỰC) ---
        private void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
        {
            // Tạo cửa sổ nhập liệu nhanh (Input Dialog)
            var inputWindow = new Window
            {
                Title = "Xác nhận xóa nguy hiểm",
                Width = 350,
                Height = 230,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock { Text = "Hành động này sẽ XÓA TOÀN BỘ lịch sử đơn hàng.\nKhông thể khôi phục! Dữ liệu doanh thu sẽ mất hết.", Foreground = System.Windows.Media.Brushes.Red, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = "Gõ chữ 'ok' bên dưới để xác nhận:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) });

            var txtInput = new TextBox();
            stack.Children.Add(txtInput);

            var btnConfirm = new Button { Content = "XÁC NHẬN XÓA", Background = System.Windows.Media.Brushes.Red, Foreground = System.Windows.Media.Brushes.White, Height = 30, Margin = new Thickness(0, 15, 0, 0) };
            stack.Children.Add(btnConfirm);

            inputWindow.Content = stack;

            // Xử lý sự kiện click
            bool isConfirmed = false;
            btnConfirm.Click += (s, args) =>
            {
                if (txtInput.Text.Trim().ToLower() == "ok")
                {
                    isConfirmed = true;
                    inputWindow.Close();
                }
                else
                {
                    MessageBox.Show("Mã xác nhận không đúng!");
                }
            };

            inputWindow.ShowDialog();

            // Nếu đã xác nhận đúng
            if (isConfirmed)
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        // Xóa tất cả đơn Paid hoặc Cancelled (Giữ lại Pending đang ăn)
                        var oldOrders = db.Orders.Include(o => o.OrderDetails).Where(o => o.OrderStatus != "Pending").ToList();

                        foreach (var order in oldOrders)
                        {
                            db.OrderDetails.RemoveRange(order.OrderDetails);
                        }
                        db.Orders.RemoveRange(oldOrders);

                        db.SaveChanges();
                        MessageBox.Show($"Đã xóa sạch {oldOrders.Count} đơn hàng lịch sử!", "Thành công");
                        LoadData();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi xóa: " + ex.Message);
                }
            }
        }
    }
}
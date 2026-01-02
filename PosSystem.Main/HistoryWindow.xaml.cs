using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input; // Để dùng MouseLeftButtonDown
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using Microsoft.AspNetCore.SignalR.Client; // Để dùng SignalR

namespace PosSystem.Main
{
    // ViewModel cho TAB 1: Danh sách Hóa đơn
    public class OrderViewModel
    {
        public long OrderID { get; set; }
        public DateTime OrderTime { get; set; }
        public DateTime? CheckoutTime { get; set; }   // Giờ ra (Có thể null nếu chưa thanh toán)
        public string TimeDisplay
        {
            get
            {
                string start = OrderTime.ToString("HH:mm");
                string end = CheckoutTime.HasValue ? CheckoutTime.Value.ToString("HH:mm") : "...";
                return $"{start} - {end}";
            }
        }
        public string TableName { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public string StaffName { get; set; } = "";
        public string OrderStatus { get; set; } = "";

        public string StatusDisplay => OrderStatus == "Paid" ? "Đã thanh toán" : "Đang phục vụ";
        // Màu trạng thái Bill: Xanh lá (Đã trả), Cam (Đang ngồi)
        public SolidColorBrush StatusColor => OrderStatus == "Paid"
            ? new SolidColorBrush(Color.FromRgb(40, 167, 69))
            : new SolidColorBrush(Color.FromRgb(253, 126, 20));
    }

    // ViewModel cho TAB 2: Chi tiết gọi món
    public class ItemLogViewModel
    {
        public DateTime LogTime { get; set; }
        public string TimeDisplay => LogTime.ToString("HH:mm");
        public string TableName { get; set; } = "";
        public string DishName { get; set; } = "";
        public string Note { get; set; } = "";
        public int Quantity { get; set; }
        public string StaffName { get; set; } = "";
        public string ItemStatus { get; set; } = "";

        public string StatusDisplay => ItemStatus == "Sent" ? "Đã gửi bếp" : (ItemStatus == "Done" ? "Đã ra món" : "Mới gọi");

        // Màu nền Badge
        public SolidColorBrush StatusBg => ItemStatus == "Sent"
            ? new SolidColorBrush(Color.FromRgb(207, 226, 255))  // Xanh nhạt
            : (ItemStatus == "Done" ? new SolidColorBrush(Color.FromRgb(209, 231, 221)) : new SolidColorBrush(Color.FromRgb(255, 243, 205))); // Xanh lá nhạt / Vàng nhạt

        // Màu chữ Badge
        public SolidColorBrush StatusFore => ItemStatus == "Sent"
            ? new SolidColorBrush(Color.FromRgb(13, 110, 253))   // Xanh đậm
            : (ItemStatus == "Done" ? new SolidColorBrush(Color.FromRgb(15, 81, 50)) : new SolidColorBrush(Color.FromRgb(102, 77, 3))); // Xanh lá đậm / Nâu vàng
    }

    public partial class HistoryWindow : Window
    {
        private HubConnection _connection;

        public HistoryWindow()
        {
            InitializeComponent();
            dpDate.SelectedDate = DateTime.Now;

            LoadAllData();
            SetupRealtime(); // Kích hoạt lắng nghe Realtime
        }

        // --- CẤU HÌNH REAL-TIME (SIGNALR) ---
        private async void SetupRealtime()
        {
            try
            {
                // Tạo kết nối riêng để lắng nghe
                _connection = new HubConnectionBuilder()
                    .WithUrl("http://localhost:5000/posHub") // Đảm bảo đúng port Server của bạn
                    .WithAutomaticReconnect()
                    .Build();

                // Khi nhận tín hiệu "TableUpdated" từ bất kỳ đâu -> Reload dữ liệu ngay
                _connection.On<int>("TableUpdated", (tableId) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Reload data mà không cần bấm nút
                        LoadAllData();
                    });
                });

                await _connection.StartAsync();
            }
            catch (Exception ex)
            {
                // Có thể log lỗi nếu cần, nhưng modal không nên hiện popup lỗi kết nối gây phiền
                Console.WriteLine("History Realtime Error: " + ex.Message);
            }
        }

        // Ngắt kết nối khi đóng Modal để tránh rò rỉ bộ nhớ
        protected override async void OnClosed(EventArgs e)
        {
            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
            }
            base.OnClosed(e);
        }

        private void LoadAllData()
        {
            if (dpDate.SelectedDate == null) return;
            DateTime selectedDate = dpDate.SelectedDate.Value.Date;

            using (var db = new AppDbContext())
            {
                // 1. TAB 1: DANH SÁCH BILL (CHỈ LẤY ĐÃ THANH TOÁN)
                var orders = db.Orders
                    .Include(o => o.Table)
                    .Include(o => o.Account)
                    .Where(o => o.OrderTime.Date == selectedDate
                             && o.OrderStatus == "Paid") // <--- [MỚI] Chỉ lấy đơn đã thanh toán
                    .OrderByDescending(o => o.OrderTime)
                    .Select(o => new OrderViewModel
                    {
                        OrderID = o.OrderID,
                        OrderTime = o.OrderTime,
                        CheckoutTime = o.CheckoutTime,
                        TableName = o.Table != null ? o.Table.TableName : "Mang về",
                        TotalAmount = o.FinalAmount,
                        StaffName = o.Account != null ? o.Account.AccName : "Admin",
                        OrderStatus = o.OrderStatus
                    })
                    .ToList();
                dgOrders.ItemsSource = orders;

                // 2. TAB 2: CHI TIẾT GỌI MÓN (SỬA LỖI KHÔNG CẬP NHẬT)
                var items = db.OrderDetails
                    .Include(d => d.Dish)
                    .Include(d => d.Order).ThenInclude(o => o.Table)
                    .Include(d => d.Order).ThenInclude(o => o.Account)
     // Lấy theo ngày của ItemOrderTime thay vì OrderTime
     .Where(d => d.ItemOrderTime.Date == selectedDate && d.Quantity > 0)

      // Sắp xếp: Mới nhất lên đầu (Dựa vào giờ gọi món thực tế)
      .OrderByDescending(d => d.ItemOrderTime)

                    .Select(d => new ItemLogViewModel
                    {
                        // Vẫn hiển thị giờ của hóa đơn (vì DB chưa có cột giờ gọi món riêng)
                        // Nhưng nhờ xếp theo ID nên món mới thêm lúc 22:30 sẽ nằm trên cùng
                        LogTime = d.ItemOrderTime,
                        TableName = d.Order.Table != null ? d.Order.Table.TableName : "Unknown",
                        DishName = d.Dish != null ? d.Dish.DishName : "Unknown",
                        Note = d.Note ?? "",
                        Quantity = d.Quantity,
                        StaffName = d.Order.Account != null ? d.Order.Account.AccName : "Admin",
                        ItemStatus = d.ItemStatus
                    })
                    .ToList();
                dgItems.ItemsSource = items;
            }
        }
        // [MỚI] Xử lý nút In lại
        private void BtnReprint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long orderId)
            {
                if (MessageBox.Show($"Bạn có muốn in lại hóa đơn #{orderId}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    // Gọi PrintService đã có sẵn
                    PrintService.PrintBill(orderId);
                }
            }
        }
        private void BtnViewDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long orderId)
            {
                using (var db = new AppDbContext())
                {
                    // Lấy thông tin đơn hàng
                    var order = db.Orders
                        .Include(o => o.Table)
                        .FirstOrDefault(o => o.OrderID == orderId);

                    if (order == null) return;

                    // Lấy danh sách món, gom nhóm để hiển thị đẹp
                    var details = db.OrderDetails
                        .Include(d => d.Dish)
                        .Where(d => d.OrderID == orderId && d.Quantity > 0)
                        .ToList() // Tải về bộ nhớ trước
                        .Select(d => new OrderDetailDisplayModel
                        {
                            DishName = d.Dish != null ? d.Dish.DishName : "Unknown",
                            UnitPrice = d.UnitPrice,
                            Quantity = d.Quantity,
                            TotalAmount = d.TotalAmount,
                            Note = d.Note ?? ""
                        })
                        .ToList();

                    // Cập nhật UI Popup
                    lblPopupTitle.Text = $"CHI TIẾT HÓA ĐƠN #{order.OrderID}";
                    string tableName = order.Table != null ? order.Table.TableName : "Mang về";
                    string time = (order.CheckoutTime ?? order.OrderTime).ToString("HH:mm dd/MM/yyyy");
                    lblPopupInfo.Text = $"{tableName}  |  {time}";

                    lblPopupTotal.Text = order.FinalAmount.ToString("N0") + "đ";

                    dgPopupDetails.ItemsSource = details;

                    // Hiện Popup
                    pnlOrderDetailPopup.Visibility = Visibility.Visible;
                }
            }
        }

        // [MỚI] Đóng Popup
        private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
        {
            pnlOrderDetailPopup.Visibility = Visibility.Collapsed;
            dgPopupDetails.ItemsSource = null;
        }


        private void DpDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => LoadAllData();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        // Hàm cho phép kéo thả cửa sổ khi không có thanh tiêu đề
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }
    }
    public class OrderDetailDisplayModel
    {
        public string DishName { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal TotalAmount { get; set; }
        public string Note { get; set; } = "";
    }
}
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore; // Quan trọng để dùng Include
using PosSystem.Main.Database;
using System.Collections.Generic;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class DashboardPage : Page
    {
        private readonly AppDbContext _context;

        public DashboardPage()
        {
            InitializeComponent();
            _context = new AppDbContext();
            Loaded += DashboardPage_Loaded;
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDashboardData();
        }

        private void LoadDashboardData()
        {
            try
            {
                var today = DateTime.Today;
                txtDate.Text = $"Cập nhật lúc: {DateTime.Now:HH:mm} - {DateTime.Now:dd/MM/yyyy}";

                // 1. Lấy dữ liệu Order hôm nay
                var ordersToday = _context.Orders
                                         .Where(o => o.OrderTime >= today)
                                         .Include(o => o.OrderDetails) // Kèm chi tiết để tính tổng món
                                         .ToList();

                // Tính toán thống kê
                var paidOrders = ordersToday.Where(o => o.OrderStatus == "Paid").ToList();

                decimal revenue = paidOrders.Sum(o => o.FinalAmount);
                int orderCount = ordersToday.Count;

                // Món đã bán (Loại bỏ món bị hủy status Cancel hoặc đơn Cancelled)
                int soldItems = ordersToday
                    .Where(o => o.OrderStatus != "Cancelled")
                    .SelectMany(o => o.OrderDetails)
                    .Where(d => d.ItemStatus != "Cancel")
                    .Sum(d => d.Quantity);

                // Số bàn đang có khách
                int activeTables = _context.Tables.Count(t => t.TableStatus == "Occupied");

                // Hiển thị lên UI
                txtRevenue.Text = string.Format("{0:N0} đ", revenue);
                txtOrderCount.Text = orderCount.ToString();
                txtActiveTables.Text = activeTables.ToString();
                txtSoldItems.Text = soldItems.ToString();

                // 2. Load Top món bán chạy & Thống kê nhóm
                LoadAnalytics(today);

                // 3. Load đơn hàng mới
                LoadRecentOrders(today);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi Dashboard: " + ex.Message);
            }
        }

        private void LoadAnalytics(DateTime date)
        {
            // Lấy chi tiết đơn hàng (Kèm Dish và Category để lấy tên)
            var details = _context.OrderDetails
                .Include(d => d.Order)
                .Include(d => d.Dish)
                .ThenInclude(dish => dish.Category) // Lấy thêm Category từ Dish
                .Where(d => d.Order.OrderTime >= date &&
                            d.Order.OrderStatus != "Cancelled" &&
                            d.ItemStatus != "Cancel")
                .ToList(); // Tải về RAM để xử lý GroupBy cho dễ

            // --- A. TOP MÓN BÁN CHẠY ---
            var topDishes = details
                .GroupBy(d => d.Dish != null ? d.Dish.DishName : "Không xác định") // Fix lỗi DishName
                .Select(g => new
                {
                    Name = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.Quantity)
                .Take(5)
                .ToList();

            if (topDishes.Any())
            {
                double maxQty = topDishes.Max(x => x.Quantity);
                lvTopProducts.ItemsSource = topDishes.Select(x => new BarChartModel
                {
                    Name = x.Name,
                    QuantityStr = x.Quantity + " ly",
                    BarWidth = (x.Quantity / maxQty) * 150 // Scale theo UI
                });
            }

            // --- B. DOANH THU THEO DANH MỤC (New) ---
            var categoryStats = details
                .GroupBy(d => d.Dish != null && d.Dish.Category != null ? d.Dish.Category.CategoryName : "Khác")
                .Select(g => new
                {
                    Name = g.Key,
                    Total = g.Sum(x => x.TotalAmount)
                })
                .OrderByDescending(x => x.Total)
                .Take(5)
                .ToList();

            if (categoryStats.Any())
            {
                decimal maxTotal = categoryStats.Max(x => x.Total);
                if (maxTotal == 0) maxTotal = 1;

                lvCategories.ItemsSource = categoryStats.Select(x => new BarChartModel
                {
                    Name = x.Name,
                    TotalAmountStr = string.Format("{0:N0}", x.Total),
                    // Ép kiểu double để tính toán thanh bar
                    BarWidth = ((double)x.Total / (double)maxTotal) * 150
                });
            }
        }

        private void LoadRecentOrders(DateTime date)
        {
            var recentOrders = _context.Orders
                .Include(o => o.Table) // Kèm Table để lấy TableName
                .Where(o => o.OrderTime >= date)
                .OrderByDescending(o => o.OrderTime)
                .Take(8)
                .Select(o => new
                {
                    TableName = o.Table != null ? o.Table.TableName : "Mang về",
                    Time = o.OrderTime.ToString("HH:mm"),
                    Total = string.Format("{0:N0}", o.FinalAmount),
                    Status = o.OrderStatus == "Paid" ? "Đã trả" : (o.OrderStatus == "Cancelled" ? "Đã hủy" : "Chờ"),
                    StatusColor = o.OrderStatus == "Paid" ? "#27AE60" : (o.OrderStatus == "Cancelled" ? "#E74C3C" : "#F39C12")
                })
                .ToList();

            dgRecentOrders.ItemsSource = recentOrders;
        }

        // ViewModel dùng chung cho biểu đồ
        public class BarChartModel
        {
            public string Name { get; set; }
            public string QuantityStr { get; set; }
            public string TotalAmountStr { get; set; }
            public double BarWidth { get; set; }
        }
    }
}
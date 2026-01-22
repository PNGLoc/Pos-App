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
            // Default: Today
            BtnToday_Click(null, null);
        }

        // --- BUTTON EVENTS ---
        private void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            var start = DateTime.Today;
            var end = DateTime.Today.AddDays(1).AddTicks(-1);
            LoadDashboardData(start, end, "KẾT QUẢ KINH DOANH HÔM NAY");
        }

        private void BtnYesterday_Click(object sender, RoutedEventArgs e)
        {
            var start = DateTime.Today.AddDays(-1);
            var end = DateTime.Today.AddTicks(-1);
            LoadDashboardData(start, end, "KẾT QUẢ KINH DOANH HÔM QUA");
        }

        private void BtnWeek_Click(object sender, RoutedEventArgs e)
        {
            // Monday of current week
            var today = DateTime.Today;
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var start = today.AddDays(-1 * diff).Date;
            var end = DateTime.Now; // Until now
            LoadDashboardData(start, end, "KẾT QUẢ KINH DOANH TUẦN NÀY");
        }

        private void BtnMonth_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);
            var end = DateTime.Now;
            LoadDashboardData(start, end, "KẾT QUẢ KINH DOANH THÁNG NÀY");
        }

        private void BtnYear_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, 1, 1);
            var end = DateTime.Now;
            LoadDashboardData(start, end, "KẾT QUẢ KINH DOANH NĂM NAY");
        }

        private void LoadDashboardData(DateTime start, DateTime end, string title)
        {
            try
            {
                lblDashboardTitle.Text = title;
                txtDate.Text = $"Từ: {start:dd/MM/yyyy HH:mm} - Đến: {end:dd/MM/yyyy HH:mm}";

                // 1. Lấy dữ liệu Order trong khoảng thời gian
                var ordersInRange = _context.Orders
                                         .Where(o => o.OrderTime >= start && o.OrderTime <= end)
                                         .Include(o => o.OrderDetails) // Kèm chi tiết để tính tổng món
                                         .ToList();

                // Tính toán thống kê
                var paidOrders = ordersInRange.Where(o => o.OrderStatus == "Paid").ToList();

                decimal revenue = paidOrders.Sum(o => o.FinalAmount);
                int orderCount = ordersInRange.Count;

                // Món đã bán (Loại bỏ món bị hủy status Cancel hoặc đơn Cancelled)
                int soldItems = ordersInRange
                    .Where(o => o.OrderStatus != "Cancelled")
                    .SelectMany(o => o.OrderDetails)
                    .Where(d => d.ItemStatus != "Cancel")
                    .Sum(d => d.Quantity);

                // Số bàn đang có khách (Realtime status, not history)
                int activeTables = _context.Tables.Count(t => t.TableStatus == "Occupied");

                // [NEW] Tính chi phí
                decimal totalExpense = _context.Expenses
                    .Where(e => e.ExpenseDate >= start && e.ExpenseDate <= end)
                    .Sum(e => e.Amount);
                
                decimal netRevenue = revenue - totalExpense;

                // Hiển thị lên UI
                txtRevenue.Text = string.Format("{0:N0}", revenue); // Doanh thu tổng
                txtExpense.Text = string.Format("{0:N0}", totalExpense); // Chi phí
                txtNetRevenue.Text = string.Format("{0:N0}", netRevenue); // Thực thu

                txtOrderCount.Text = orderCount.ToString();
                txtActiveTables.Text = activeTables.ToString();

                // 2. Load Top món bán chạy & Thống kê nhóm
                LoadAnalytics(start, end);

                // 3. Load đơn hàng mới
                LoadRecentOrders(start, end);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi Dashboard: " + ex.Message);
            }
        }

        private void LoadAnalytics(DateTime start, DateTime end)
        {
            // Lấy chi tiết đơn hàng (Kèm Dish và Category để lấy tên)
            var details = _context.OrderDetails
                .Include(d => d.Order)
                .Include(d => d.Dish)
                .ThenInclude(dish => dish.Category) // Lấy thêm Category từ Dish
                .Where(d => d.Order.OrderTime >= start && d.Order.OrderTime <= end &&
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
                    BarWidth = (x.Quantity / (maxQty == 0 ? 1 : maxQty)) * 150 // Scale theo UI
                });
            }
            else
            {
                lvTopProducts.ItemsSource = null;
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
            else
            {
                lvCategories.ItemsSource = null;
            }
        }

        private void LoadRecentOrders(DateTime start, DateTime end)
        {
            var recentOrders = _context.Orders
                .Include(o => o.Table) // Kèm Table để lấy TableName
                .Where(o => o.OrderTime >= start && o.OrderTime <= end)
                .OrderByDescending(o => o.OrderTime)
                .Take(20) // Tăng lên 20 vì khoảng thời gian dài hơn
                .Select(o => new
                {
                    TableName = o.Table != null ? o.Table.TableName : "Mang về",
                    Time = o.OrderTime.ToString("dd/MM HH:mm"), // Thêm ngày vào hiển thị
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
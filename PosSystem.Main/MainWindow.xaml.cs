using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using System.Threading.Tasks;
using System.Globalization;
// [THÊM] Namespace quan trọng
using Microsoft.AspNetCore.SignalR.Client; // Cho Client (Lắng nghe)
using Microsoft.AspNetCore.SignalR;        // Cho Server (Gửi đi)
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Server.Hubs;
namespace PosSystem.Main
{
    // ViewModels
    public class TableViewModel
    {
        public int TableID { get; set; }
        public required string TableName { get; set; }
        public required string TableStatus { get; set; }
        public string TimeDisplay { get; set; } = "";
        public string StatusDisplay => TableStatus == "Occupied" ? "Có khách" : "Trống";
        public SolidColorBrush ColorBrush => TableStatus == "Occupied" ? new SolidColorBrush(Color.FromRgb(220, 53, 69)) : new SolidColorBrush(Color.FromRgb(40, 167, 69));
    }

    public class CategoryViewModel { public int CategoryID { get; set; } public string CategoryName { get; set; } = ""; }

    // View Model cho Món ăn trong menu (Đơn giản hóa vì bỏ checkbox)
    public class DishViewModel
    {
        public int DishID { get; set; }
        public string DishName { get; set; } = "";
        public decimal Price { get; set; }
        public int CategoryID { get; set; }
    }

    public partial class MainWindow : Window
    {
        private HubConnection _connection = default!;
        private int _selectedTableId = 0;
        private List<Dish> _allDishes = new List<Dish>();
        private List<DishViewModel> _dishViewModels = new List<DishViewModel>();
        private DispatcherTimer _tableTimeTimer = new DispatcherTimer();
        private DispatcherTimer _tableListUpdateTimer = new DispatcherTimer();
        private DateTime? _currentOrderTime = null;
        private string _tableTypeFilter = "All";  // Track current filter

        // Split mode variables
        private bool _isSplitMode = false;
        private Dictionary<long, int> _splitQuantities = new Dictionary<long, int>();  // OrderDetailID -> Qty to split
        private bool _isWaitingForTargetTable = false;  // True when waiting for user to click target table
        private Dictionary<long, int> _pendingSplitItems = new Dictionary<long, int>();  // Items to split when table selected

        // Move table mode variables
        private bool _isWaitingForMoveTargetTable = false;  // True when waiting for user to click target table for move

        public MainWindow()
        {
            InitializeComponent();
            if (UserSession.IsLoggedIn) lblStaffName.Text = UserSession.AccName;
            if (UserSession.IsLoggedIn && UserSession.AccRole == "Admin") btnBackToAdmin.Visibility = Visibility.Visible;

            // Setup timer to update table time every second
            _tableTimeTimer.Interval = TimeSpan.FromSeconds(1);
            _tableTimeTimer.Tick += TableTimeTimer_Tick;

            // Setup timer to refresh table list every second (for displaying elapsed times)
            _tableListUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            _tableListUpdateTimer.Tick += (s, e) => LoadTables();
            _tableListUpdateTimer.Start();

            // Reset buttons on startup
            btnCheckout.IsEnabled = false;
            btnSendKitchen.IsEnabled = false;
            btnSendKitchen.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));  // Màu xám
            btnSendKitchen.Content = "👨‍🍳 GỬI BẾP (In Đợt Mới)";
            btnSplitTable.Visibility = Visibility.Collapsed;
            btnMoveTable.Visibility = Visibility.Collapsed;
            lblSubTotal.Text = "0đ";
            lblTotal.Text = "0đ";
            pnlDiscount.Visibility = Visibility.Collapsed;

            LoadTables();
            LoadMenu();
            SetupRealtime();
        }

        private void BtnFilterTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filterType)
            {
                _tableTypeFilter = filterType;

                // Update button colors
                btnFilterAll.Background = new SolidColorBrush(filterType == "All" ? Color.FromRgb(0, 123, 255) : Color.FromRgb(108, 117, 125));
                btnFilterDineIn.Background = new SolidColorBrush(filterType == "DineIn" ? Color.FromRgb(0, 123, 255) : Color.FromRgb(108, 117, 125));
                btnFilterTakeAway.Background = new SolidColorBrush(filterType == "TakeAway" ? Color.FromRgb(0, 123, 255) : Color.FromRgb(108, 117, 125));
                btnFilterPickup.Background = new SolidColorBrush(filterType == "Pickup" ? Color.FromRgb(0, 123, 255) : Color.FromRgb(108, 117, 125));
                btnFilterDelivery.Background = new SolidColorBrush(filterType == "Delivery" ? Color.FromRgb(0, 123, 255) : Color.FromRgb(108, 117, 125));

                // Reload tables with new filter
                LoadTables();
            }
        }

        // --- 1. CHUYỂN ĐỔI VIEW ---
        private void lstTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTables.SelectedItem is TableViewModel selected)
            {
                // If waiting for target table in split mode, transfer items instead of opening menu
                if (_isWaitingForTargetTable && _pendingSplitItems.Count > 0)
                {
                    int targetTableId = selected.TableID;
                    lstTables.SelectedItem = null;  // Deselect to reset
                    ExecuteSplitTransfer(targetTableId);
                    return;
                }

                // If waiting for target table in move mode, move entire order instead of opening menu
                if (_isWaitingForMoveTargetTable)
                {
                    int targetTableId = selected.TableID;
                    lstTables.SelectedItem = null;  // Deselect to reset
                    ExecuteMoveTable(targetTableId);
                    return;
                }

                _selectedTableId = selected.TableID;
                lblSelectedTable.Text = selected.TableName;

                pnlTableList.Visibility = Visibility.Collapsed;
                pnlMenu.Visibility = Visibility.Visible;

                // Show split and move buttons when selecting a table
                btnSplitTable.Visibility = Visibility.Visible;
                btnMoveTable.Visibility = Visibility.Visible;

                // Stop timer when entering a table (will start only when sending kitchen)
                _tableTimeTimer.Stop();
                _currentOrderTime = null;
                lblTableTime.Text = "";

                // Get order time (but don't start timer - wait for first kitchen send)
                using (var db = new AppDbContext())
                {
                    var order = db.Orders.FirstOrDefault(o => o.TableID == selected.TableID && o.OrderStatus == "Pending");
                    if (order != null && order.FirstSentTime.HasValue)
                    {
                        // Order has been sent to kitchen - start timer from FirstSentTime
                        _currentOrderTime = order.FirstSentTime;
                        _tableTimeTimer.Start();
                    }
                }

                LoadOrderDetails(selected.TableID);
            }
        }

        private void BtnBackToTables_Click(object sender, RoutedEventArgs e)
        {
            _selectedTableId = 0;
            _currentOrderTime = null;
            lblSelectedTable.Text = "Chưa chọn bàn";
            lblTableTime.Text = "";
            lstOrderDetails.ItemsSource = null;

            _tableTimeTimer.Stop();
            pnlMenu.Visibility = Visibility.Collapsed;
            pnlTableList.Visibility = Visibility.Visible;

            // Hide split and move buttons when returning to table list
            btnSplitTable.Visibility = Visibility.Collapsed;
            btnMoveTable.Visibility = Visibility.Collapsed;

            // Reset split mode when returning to table list
            _isSplitMode = false;
            _splitQuantities.Clear();
            _isWaitingForTargetTable = false;
            _pendingSplitItems.Clear();
            btnTransferSplit.Visibility = Visibility.Collapsed;
            btnDiscountBill.Visibility = Visibility.Visible;
            colSplitQuantity.Visibility = Visibility.Collapsed;

            // Reset move mode when returning to table list
            _isWaitingForMoveTargetTable = false;

            // Reset buttons và labels
            btnCheckout.IsEnabled = false;
            btnSendKitchen.IsEnabled = false;
            btnSendKitchen.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));  // Màu xám
            lblSubTotal.Text = "0đ";
            lblTotal.Text = "0đ";
            pnlDiscount.Visibility = Visibility.Collapsed;

            LoadTables();
            lstTables.SelectedItem = null;
        }

        // Helper method to switch to a specific table
        private void SelectAndLoadTable(int tableId)
        {
            _selectedTableId = tableId;

            using (var db = new AppDbContext())
            {
                var table = db.Tables.FirstOrDefault(t => t.TableID == tableId);
                if (table != null)
                {
                    lblSelectedTable.Text = table.TableName;
                }
            }

            pnlTableList.Visibility = Visibility.Collapsed;
            pnlMenu.Visibility = Visibility.Visible;
            btnSplitTable.Visibility = Visibility.Visible;
            btnMoveTable.Visibility = Visibility.Visible;

            // Stop timer when entering a table
            _tableTimeTimer.Stop();
            _currentOrderTime = null;
            lblTableTime.Text = "";

            // Check if order has been sent to kitchen
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");
                if (order != null && order.FirstSentTime.HasValue)
                {
                    _currentOrderTime = order.FirstSentTime;
                    _tableTimeTimer.Start();
                    // Manually trigger timer tick to show time immediately
                    TableTimeTimer_Tick(null, null);
                }
            }

            LoadOrderDetails(tableId);

            // Force UI refresh with proper rebinding
            Dispatcher.Invoke(() =>
            {
                // Rebind to force UI update
                var source = lstOrderDetails.ItemsSource;
                lstOrderDetails.ItemsSource = null;
                System.Threading.Thread.Sleep(10);
                lstOrderDetails.ItemsSource = source;
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        // Recalculate order totals based on order details
        private void RecalculateOrderTotals(Order order)
        {
            if (order == null) return;

            decimal subTotal = order.OrderDetails.Where(d => d.Quantity > 0).Sum(d => d.Quantity * d.UnitPrice);
            order.SubTotal = subTotal;

            decimal discountValue = (order.DiscountPercent > 0) ? subTotal * (order.DiscountPercent / 100) : order.DiscountAmount;
            order.FinalAmount = subTotal - discountValue;
        }

        // --- 2. LOAD DATA ---
        private void LoadTables()
        {
            using (var db = new AppDbContext())
            {
                var tables = db.Tables.Include(t => t.Orders).ThenInclude(o => o.OrderDetails).ToList();

                // Apply filter
                if (_tableTypeFilter != "All")
                {
                    tables = tables.Where(t => t.TableType == _tableTypeFilter).ToList();
                }

                lstTables.ItemsSource = tables.Select(t =>
                {
                    var vm = new TableViewModel
                    {
                        TableID = t.TableID,
                        TableName = t.TableName,
                        TableStatus = t.TableStatus,
                        TimeDisplay = ""
                    };

                    // Calculate time for occupied tables with pending orders that have been sent to kitchen
                    if (t.TableStatus == "Occupied" && t.Orders.Any())
                    {
                        var order = t.Orders.FirstOrDefault(o => o.OrderStatus == "Pending");
                        // Only show time if FirstSentTime has value (order has been sent to kitchen)
                        if (order != null && order.FirstSentTime.HasValue)
                        {
                            var elapsed = DateTime.Now - order.FirstSentTime.Value;
                            if (elapsed.TotalMinutes < 1)
                                vm.TimeDisplay = $"{(int)elapsed.TotalSeconds}s";
                            else if (elapsed.TotalHours < 1)
                                vm.TimeDisplay = $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                            else
                                vm.TimeDisplay = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
                        }
                    }

                    return vm;
                }).ToList();
            }
        }

        // --- 1. CẬP NHẬT HIỂN THỊ: Món SL=0 sẽ hiện rõ "CHỜ HỦY" ---
        private void LoadOrderDetails(int tableId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order != null)
                {
                    // Xử lý đồng hồ đếm giờ (Giữ nguyên)
                    if (order.FirstSentTime.HasValue)
                    {
                        _currentOrderTime = order.FirstSentTime;
                        if (!_tableTimeTimer.IsEnabled) _tableTimeTimer.Start();
                        TableTimeTimer_Tick(null, null);
                    }
                    else
                    {
                        _currentOrderTime = null;
                        _tableTimeTimer.Stop();
                        lblTableTime.Text = "";
                    }

                    // --- [LOGIC GỘP MÓN CHUẨN F&B] ---
                    var viewModels = order.OrderDetails
                        .GroupBy(d => new
                        {
                            d.DishID,                   // 1. Cùng món
                            d.ItemStatus,               // 2. Cùng trạng thái (New/Sent) -> Món mới không được gộp vào món cũ
                            Note = (d.Note ?? "").Trim() // 3. QUAN TRỌNG: Cùng ghi chú mới gộp (Chuẩn hóa bỏ dấu cách)
                        })
                        .Select(g => new OrderDetailViewModel
                        {
                            OrderDetailID = g.First().OrderDetailID,
                            DishName = g.First().Dish != null ? g.First().Dish.DishName : "Unknown",
                            UnitPrice = g.First().UnitPrice,
                            DiscountRate = g.First().DiscountRate,

                            // Lấy Status và Note từ Key của nhóm để đảm bảo chính xác
                            ItemStatus = g.Key.ItemStatus,
                            Note = g.Key.Note,

                            // Cộng dồn số lượng và tiền
                            Quantity = g.Sum(x => x.Quantity),
                            TotalAmount = g.Sum(x => x.TotalAmount),

                            // Logic hiển thị
                            BatchDisplay = g.Sum(x => x.PrintedQuantity) == 0 ? "⏳" : (g.Max(x => x.KitchenBatch) > 0 ? $"Đợt {g.Max(x => x.KitchenBatch)}" : "---"),

                            StatusDisplay = g.Sum(x => x.Quantity) == 0 ? "❌ CHỜ HỦY" :
                                            (g.Key.ItemStatus == "Sent" ? "✓ Đã gửi" :
                                            (g.Sum(x => x.Quantity) != g.Sum(x => x.PrintedQuantity) ? "Mới" : "OK")),

                            RowColor = g.Sum(x => x.Quantity) == 0 ? "#FFCCCC" :
                                       (g.Key.ItemStatus == "Sent" ? "#D4EDDA" :       // Xanh nhạt (Đã gửi)
                                       (g.Key.ItemStatus == "New" ? "#FFF3CD" : "White")), // Vàng (Mới)

                            IsInSplitMode = false
                        })
                        .OrderBy(vm => vm.ItemStatus == "New" ? 0 : 1) // Mẹo: Đưa món MỚI lên đầu (hoặc xuống cuối) để dễ thấy
                        .ThenBy(vm => vm.DishName)
                        .ToList();

                    lstOrderDetails.ItemsSource = viewModels;

                    // --- Tính tổng tiền (Code cũ giữ nguyên) ---
                    RecalculateOrderTotals(order);
                    lblSubTotal.Text = order.SubTotal.ToString("N0") + "đ";
                    decimal discountValue = (order.DiscountPercent > 0) ? order.SubTotal * (order.DiscountPercent / 100) : order.DiscountAmount;

                    if (discountValue > 0)
                    {
                        lblDiscount.Text = $"-{discountValue:N0}đ";
                        pnlDiscount.Visibility = Visibility.Visible;
                    }
                    else pnlDiscount.Visibility = Visibility.Collapsed;

                    lblTotal.Text = order.FinalAmount.ToString("N0") + "đ";

                    // Logic nút bấm
                    bool hasChanges = order.OrderDetails.Any(d => d.Quantity != d.PrintedQuantity);
                    bool hasValidItems = order.OrderDetails.Any(d => d.Quantity > 0);

                    btnCheckout.IsEnabled = hasValidItems;
                    btnSendKitchen.IsEnabled = hasChanges;
                    btnSendKitchen.Content = hasChanges ? "🔔 GỬI BẾP (Cập nhật)" : "👨‍🍳 GỬI BẾP";
                    btnSendKitchen.Background = hasChanges ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FD7E14")
                                                           : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#6C757D");
                }
                else
                {
                    // Reset giao diện khi bàn trống
                    lstOrderDetails.ItemsSource = null;
                    lblTotal.Text = "0đ";
                    lblSubTotal.Text = "0đ";
                    pnlDiscount.Visibility = Visibility.Collapsed;
                    btnCheckout.IsEnabled = false;
                    btnSendKitchen.IsEnabled = false;
                    _currentOrderTime = null;
                    lblTableTime.Text = "";
                    _tableTimeTimer.Stop();
                }
            }
        }

        private void LoadMenu()
        {
            using (var db = new AppDbContext())
            {
                var cats = db.Categories.OrderBy(c => c.OrderIndex).ToList();
                var catViewModels = new List<CategoryViewModel> { new CategoryViewModel { CategoryID = 0, CategoryName = "TẤT CẢ" } };
                catViewModels.AddRange(cats.Select(c => new CategoryViewModel { CategoryID = c.CategoryID, CategoryName = c.CategoryName }));

                lstCategories.ItemsSource = catViewModels;
                _allDishes = db.Dishes.Where(d => d.DishStatus == "Active").ToList();

                _dishViewModels = _allDishes.Select(d => new DishViewModel
                {
                    DishID = d.DishID,
                    DishName = d.DishName,
                    Price = Services.PriceService.GetCurrentPrice(d.DishID),
                    CategoryID = d.CategoryID
                }).ToList();

                UpdateDishListDisplay();
                lstCategories.SelectedIndex = 0;
            }
        }

        private void UpdateDishListDisplay()
        {
            var filtered = _dishViewModels;

            // Filter by category
            if (lstCategories.SelectedItem is CategoryViewModel selected && selected.CategoryID != 0)
            {
                filtered = filtered.Where(d => d.CategoryID == selected.CategoryID).ToList();
            }

            // Filter by search
            string searchText = txtDishSearch?.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(d => MatchDishSearch(d.DishName, searchText)).ToList();
            }

            lstDishes.ItemsSource = filtered;
        }

        private string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Map Vietnamese characters to non-accented versions
            var replacements = new Dictionary<char, char>
            {
                // Lowercase a
                {'à', 'a'}, {'á', 'a'}, {'ả', 'a'}, {'ã', 'a'}, {'ạ', 'a'},
                {'ă', 'a'}, {'ằ', 'a'}, {'ắ', 'a'}, {'ẳ', 'a'}, {'ẵ', 'a'}, {'ặ', 'a'},
                {'â', 'a'}, {'ầ', 'a'}, {'ấ', 'a'}, {'ẩ', 'a'}, {'ẫ', 'a'}, {'ậ', 'a'},
                // Lowercase d
                {'đ', 'd'},
                // Lowercase e
                {'è', 'e'}, {'é', 'e'}, {'ẻ', 'e'}, {'ẽ', 'e'}, {'ẹ', 'e'},
                {'ê', 'e'}, {'ề', 'e'}, {'ế', 'e'}, {'ể', 'e'}, {'ễ', 'e'}, {'ệ', 'e'},
                // Lowercase i
                {'ì', 'i'}, {'í', 'i'}, {'ỉ', 'i'}, {'ĩ', 'i'}, {'ị', 'i'},
                // Lowercase o
                {'ò', 'o'}, {'ó', 'o'}, {'ỏ', 'o'}, {'õ', 'o'}, {'ọ', 'o'},
                {'ô', 'o'}, {'ồ', 'o'}, {'ố', 'o'}, {'ổ', 'o'}, {'ỗ', 'o'}, {'ộ', 'o'},
                {'ơ', 'o'}, {'ờ', 'o'}, {'ớ', 'o'}, {'ở', 'o'}, {'ỡ', 'o'}, {'ợ', 'o'},
                // Lowercase u
                {'ù', 'u'}, {'ú', 'u'}, {'ủ', 'u'}, {'ũ', 'u'}, {'ụ', 'u'},
                {'ư', 'u'}, {'ừ', 'u'}, {'ứ', 'u'}, {'ử', 'u'}, {'ữ', 'u'}, {'ự', 'u'},
                // Lowercase y
                {'ỳ', 'y'}, {'ý', 'y'}, {'ỷ', 'y'}, {'ỹ', 'y'}, {'ỵ', 'y'},
                // Uppercase A
                {'À', 'A'}, {'Á', 'A'}, {'Ả', 'A'}, {'Ã', 'A'}, {'Ạ', 'A'},
                {'Ă', 'A'}, {'Ằ', 'A'}, {'Ắ', 'A'}, {'Ẳ', 'A'}, {'Ẵ', 'A'}, {'Ặ', 'A'},
                {'Â', 'A'}, {'Ầ', 'A'}, {'Ấ', 'A'}, {'Ẩ', 'A'}, {'Ẫ', 'A'}, {'Ậ', 'A'},
                // Uppercase D
                {'Đ', 'D'},
                // Uppercase E
                {'È', 'E'}, {'É', 'E'}, {'Ẻ', 'E'}, {'Ẽ', 'E'}, {'Ẹ', 'E'},
                {'Ê', 'E'}, {'Ề', 'E'}, {'Ế', 'E'}, {'Ể', 'E'}, {'Ễ', 'E'}, {'Ệ', 'E'},
                // Uppercase I
                {'Ì', 'I'}, {'Í', 'I'}, {'Ỉ', 'I'}, {'Ĩ', 'I'}, {'Ị', 'I'},
                // Uppercase O
                {'Ò', 'O'}, {'Ó', 'O'}, {'Ỏ', 'O'}, {'Õ', 'O'}, {'Ọ', 'O'},
                {'Ô', 'O'}, {'Ồ', 'O'}, {'Ố', 'O'}, {'Ổ', 'O'}, {'Ỗ', 'O'}, {'Ộ', 'O'},
                {'Ơ', 'O'}, {'Ờ', 'O'}, {'Ớ', 'O'}, {'Ở', 'O'}, {'Ỡ', 'O'}, {'Ợ', 'O'},
                // Uppercase U
                {'Ù', 'U'}, {'Ú', 'U'}, {'Ủ', 'U'}, {'Ũ', 'U'}, {'Ụ', 'U'},
                {'Ư', 'U'}, {'Ừ', 'U'}, {'Ứ', 'U'}, {'Ử', 'U'}, {'Ữ', 'U'}, {'Ự', 'U'},
                // Uppercase Y
                {'Ỳ', 'Y'}, {'Ý', 'Y'}, {'Ỷ', 'Y'}, {'Ỹ', 'Y'}, {'Ỵ', 'Y'}
            };

            var result = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (replacements.ContainsKey(c))
                    result.Append(replacements[c]);
                else
                    result.Append(c);
            }
            return result.ToString();
        }

        private bool MatchDishSearch(string dishName, string searchText)
        {
            string normalized = RemoveDiacritics(dishName).ToLower();
            string normalizedSearch = RemoveDiacritics(searchText).ToLower();

            // Full name match (handles diacritical marks)
            if (normalized.Contains(normalizedSearch))
                return true;

            // First letter abbreviation match (e.g., "mctc" for "mỳ cay thập cẩm")
            var words = dishName.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string firstLetters = string.Concat(words.Select(w => RemoveDiacritics(w.Substring(0, 1)).ToLower()));
            if (firstLetters.Contains(normalizedSearch))
                return true;

            // Partial first letter match (e.g., "tc" for "thập cẩm" in "mỳ cay thập cẩm")
            foreach (var word in words)
            {
                if (RemoveDiacritics(word).ToLower().StartsWith(normalizedSearch))
                    return true;
            }

            return false;
        }

        private void lstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDishListDisplay();

        private void TxtDishSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDishListDisplay();

        // --- 3. THAO TÁC NHANH TRÊN MÓN ĂN ---

        // A. Nhấn vào món -> Thêm ngay
        private void Dish_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is int dishId)
            {
                AddDishToOrder(_selectedTableId, dishId);
                // Reset search after selecting dish
                txtDishSearch.Clear();
            }
        }

        private void AddDishToOrder(int tableId, int dishId)
        {
            using (var db = new AppDbContext())
            {
                // 1. Lấy đơn hàng Pending (kèm chi tiết)
                var order = db.Orders.Include(o => o.OrderDetails)
                                     .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                // 2. Nếu chưa có thì tạo mới
                if (order == null)
                {
                    int? currentAccId = UserSession.AccID > 0 ? UserSession.AccID : (db.Accounts.FirstOrDefault()?.AccID);

                    order = new Order
                    {
                        TableID = tableId,
                        AccID = currentAccId,
                        OrderTime = DateTime.Now, // Nhớ set thời gian
                        OrderStatus = "Pending",
                        PaymentMethod = "Cash",
                        OrderDetails = new List<OrderDetail>()
                    };
                    db.Orders.Add(order);

                    var table = db.Tables.Find(tableId);
                    if (table != null) table.TableStatus = "Occupied";
                }

                // Cập nhật biến thời gian để hiển thị đồng hồ (nếu cần)
                if (_selectedTableId == tableId)
                {
                    _currentOrderTime = order.FirstSentTime ?? order.OrderTime;
                }

                // === [SỬA LẠI QUAN TRỌNG] ===
                // Logic đúng: Chỉ gộp nếu cùng món + không ghi chú + VÀ CHƯA GỬI BẾP (Status == "New")
                var existingDetail = order.OrderDetails
                    .FirstOrDefault(d => d.DishID == dishId
                                      && d.ItemStatus == "New"    // <--- QUAN TRỌNG NHẤT
                                      && (d.Note == null || d.Note == ""));

                // Lấy thông tin món để lấy giá
                // Lưu ý: Nên lấy từ DB để chắc chắn giá đúng, hoặc lấy từ cache _allDishes nếu tin tưởng
                var dishInfo = db.Dishes.Find(dishId);
                if (dishInfo == null) return;

                if (existingDetail != null)
                {
                    // TÌM THẤY món đang treo (New) -> Cộng dồn
                    existingDetail.Quantity++;
                    existingDetail.TotalAmount = existingDetail.Quantity * existingDetail.UnitPrice;
                    // Không cần chỉnh sửa Status vì nó vốn dĩ đã là "New"
                }
                else
                {
                    // KHÔNG TÌM THẤY (hoặc món cũ đã Sent, hoặc có Note) -> TẠO DÒNG MỚI
                    // Lúc này dù bàn đã có món đó nhưng đã gửi bếp rồi, ta vẫn tạo dòng mới để tách biệt
                    order.OrderDetails.Add(new OrderDetail
                    {
                        DishID = dishId,
                        Quantity = 1,
                        UnitPrice = dishInfo.Price,
                        ItemStatus = "New",
                        PrintedQuantity = 0,
                        TotalAmount = dishInfo.Price,
                        Note = ""
                    });
                }

                // Tính lại tổng tiền ngay tại đây để lưu luôn (tối ưu hơn gọi RecalculateOrder riêng)
                order.SubTotal = order.OrderDetails.Sum(d => d.TotalAmount);
                order.FinalAmount = order.SubTotal; // Chưa tính giảm giá bill

                // 3. Lưu Database
                db.SaveChanges();

                // 4. Cập nhật giao diện
                if (_selectedTableId == tableId) LoadOrderDetails(tableId);

                // Hiện thông báo
                ShowToast($"Đã chọn: {dishInfo.DishName}");

                // 5. Bắn SignalR
                NotifyTableUpdated(tableId);
            }
        }
        // --- 2. NÚT CỘNG (+) (CHỈ CỘNG SỐ) ---
        private void BtnIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long detailId)
            {
                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail == null) return;

                    detail.Quantity++;
                    detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);

                    if (detail.ItemStatus == "Sent") detail.ItemStatus = "Modified";

                    db.SaveChanges();
                    RecalculateOrder(db, detail.OrderID);
                    LoadOrderDetails(_selectedTableId);

                    // ⭐ Notify mobile via SignalR
                    NotifyTableUpdated(_selectedTableId);
                }
            }
        }

        // --- 3. NÚT TRỪ (-) (GIẢM SỐ, VỀ 0 CŨNG KHÔNG XÓA NGAY) ---
        private void BtnDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long detailId)
            {
                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail == null) return;

                    long currentOrderId = detail.OrderID; // Lưu lại ID đơn hàng để kiểm tra sau

                    // 1. GIẢM SỐ LƯỢNG (Nếu đang > 0)
                    if (detail.Quantity > 0)
                    {
                        detail.Quantity--;
                        detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);

                        if (detail.ItemStatus == "Sent") detail.ItemStatus = "Modified";
                    }

                    // 2. LOGIC XÓA
                    bool isRemoved = false;

                    // Nếu món Mới (chưa in) về 0 -> XÓA
                    if (detail.Quantity == 0 && detail.PrintedQuantity == 0)
                    {
                        db.OrderDetails.Remove(detail);
                        isRemoved = true;
                    }
                    // Nếu món Cũ (đã in) -> Giữ lại số 0 để báo hủy (Không xóa dòng này ngay)

                    db.SaveChanges();

                    // 3. QUAN TRỌNG: KIỂM TRA XEM ĐƠN HÀNG CÒN MÓN NÀO KHÔNG?
                    // Chỉ kiểm tra nếu vừa có hành động xóa dòng
                    if (isRemoved)
                    {
                        // Kiểm tra xem trong Order này còn dòng nào không?
                        bool hasAnyItem = db.OrderDetails.Any(d => d.OrderID == currentOrderId);

                        if (!hasAnyItem)
                        {
                            // === ĐƠN TRỐNG RỖNG -> HỦY ĐƠN & TRẢ BÀN ===
                            var order = db.Orders.Find(currentOrderId);
                            if (order != null)
                            {
                                // 1. Trả trạng thái bàn về "Empty"
                                var table = db.Tables.Find(order.TableID);
                                if (table != null) table.TableStatus = "Empty";

                                // 2. Xóa Order rỗng
                                db.Orders.Remove(order);
                                db.SaveChanges();

                                // 3. Cập nhật giao diện
                                LoadTables(); // Load lại màu bàn (xanh)
                                LoadOrderDetails(_selectedTableId); // Reset cột phải
                                return; // Kết thúc luôn
                            }
                        }
                    }

                    // Nếu đơn vẫn còn món -> Tính lại tiền bình thường
                    RecalculateOrder(db, detail.OrderID);
                    LoadOrderDetails(_selectedTableId);

                    // ⭐ Notify mobile via SignalR
                    NotifyTableUpdated(_selectedTableId);
                }
            }
        }

        // --- NHẬP TRỰC TIẾP SỐ LƯỢNG ---
        private void TxtQuantity_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBlock txtQuantity &&
                txtQuantity.Parent is StackPanel stackPanel)
            {
                // Tìm TextBlock hiện tại và TextBox tương ứng
                var textBox = stackPanel.Children.OfType<TextBox>().FirstOrDefault();
                if (textBox != null)
                {
                    txtQuantity.Visibility = Visibility.Collapsed;
                    textBox.Visibility = Visibility.Visible;
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }
        }

        private void QuantityInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Chỉ cho phép nhập số
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void TxtQuantityInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Return || e.Key == System.Windows.Input.Key.Enter)
            {
                // Enter: Lưu thay đổi
                if (sender is TextBox textBox)
                {
                    SaveQuantityChange(textBox);
                }
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                // Escape: Hủy bỏ
                if (sender is TextBox textBox)
                {
                    CancelQuantityEdit(textBox);
                }
                e.Handled = true;
            }
        }

        private void TxtQuantityInput_LostFocus(object sender, RoutedEventArgs e)
        {
            // Khi mất focus: Lưu thay đổi
            if (sender is TextBox textBox)
            {
                SaveQuantityChange(textBox);
            }
        }

        private void SaveQuantityChange(TextBox textBox)
        {
            if (textBox == null || !(textBox.Tag is long detailId)) return;

            if (!int.TryParse(textBox.Text, out int newQuantity) || newQuantity < 0)
            {
                newQuantity = 0;
            }

            using (var db = new AppDbContext())
            {
                var detail = db.OrderDetails.Find(detailId);
                if (detail == null) return;

                long currentOrderId = detail.OrderID;
                int oldQuantity = detail.Quantity;
                detail.Quantity = newQuantity;
                detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);

                if (newQuantity != oldQuantity && detail.ItemStatus == "Sent")
                {
                    detail.ItemStatus = "Modified";
                }

                bool isRemoved = false;

                // Nếu về 0: Xóa hoặc giữ lại để báo hủy
                if (detail.Quantity == 0 && detail.PrintedQuantity == 0)
                {
                    db.OrderDetails.Remove(detail);
                    isRemoved = true;
                }

                db.SaveChanges();

                // Kiểm tra xem có xóa dòng hay không
                if (isRemoved)
                {
                    bool hasAnyItem = db.OrderDetails.Any(d => d.OrderID == currentOrderId);
                    if (!hasAnyItem)
                    {
                        var order = db.Orders.Find(currentOrderId);
                        if (order != null)
                        {
                            var table = db.Tables.Find(order.TableID);
                            if (table != null) table.TableStatus = "Empty";
                            db.Orders.Remove(order);
                            db.SaveChanges();
                            LoadTables();
                            LoadOrderDetails(_selectedTableId);
                            return;
                        }
                    }
                }

                RecalculateOrder(db, detail.OrderID);
                LoadOrderDetails(_selectedTableId);
                ShowToast($"Đã cập nhật số lượng");
            }
        }

        private void CancelQuantityEdit(TextBox textBox)
        {
            if (textBox == null || !(textBox.Parent is StackPanel stackPanel)) return;

            // Tìm TextBlock tương ứng
            var textBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock != null)
            {
                textBox.Visibility = Visibility.Collapsed;
                textBlock.Visibility = Visibility.Visible;
            }
        }

        // --- C. NÚT SỬA (✎) -> MỞ DISCOUNT WINDOW ---
        private void BtnEditItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long detailId)
            {
                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail == null) return;

                    // Xác định xem đang giảm theo % hay tiền để hiển thị lên Dialog
                    bool isPercent = detail.DiscountRate > 0;
                    decimal currentVal = 0;

                    if (isPercent)
                        currentVal = detail.DiscountRate;
                    else
                        // Nếu giảm theo tiền, ta phải tính ngược lại: Giá gốc - Giá thực bán cho 1 đơn vị
                        // (Ở đây ta giả định DiscountWindow sẽ trả về Giá Mới hoặc % Giảm)
                        currentVal = detail.UnitPrice;

                    // Mở Dialog Discount (Mode: isEditItem = true để đổi tiêu đề thành "Giá mới")
                    // Constructor: (giá trị hiện tại, mode %, mode Sửa Món)
                    var dialog = new DiscountWindow(currentVal, isPercentMode: isPercent, isEditItem: true);

                    if (dialog.ShowDialog() == true)
                    {
                        if (dialog.IsPercentage)
                        {
                            // GIẢM THEO %
                            detail.DiscountRate = dialog.ResultValue; // Vd: 10%
                        }
                        else
                        {
                            // GIẢM THEO GIÁ TIỀN MỚI (Set Price)
                            decimal newPrice = dialog.ResultValue; // Vd: Bán 20k (Gốc 25k)

                            // Cập nhật lại DiscountRate dựa trên giá mới để hệ thống thống nhất
                            if (detail.UnitPrice > 0)
                                detail.DiscountRate = ((detail.UnitPrice - newPrice) / detail.UnitPrice) * 100;
                            else
                                detail.DiscountRate = 0;
                        }

                        // Tính lại Thành tiền
                        detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);

                        db.SaveChanges();
                        RecalculateOrder(db, detail.OrderID);
                        LoadOrderDetails(_selectedTableId);

                        ShowToast("Đã cập nhật giá món!");
                    }
                }
            }
        }

        // --- 2. CẬP NHẬT NÚT GỬI BẾP: Tự động dọn dẹp đơn rỗng ---
        private void BtnSendKitchen_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            Task.Run(() =>
            {
                using (var db = new AppDbContext())
                {
                    var order = db.Orders
                        .Include(o => o.Table)
                        .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                        .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                    if (order == null) return;

                    // Lấy các món có thay đổi
                    var changedItems = order.OrderDetails.Where(d => d.Quantity != d.PrintedQuantity).ToList();
                    if (!changedItems.Any()) return;

                    int currentMaxBatch = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 0;
                    bool isFirstSend = (currentMaxBatch == 0); // Lần gửi đầu tiên
                    int nextBatch = currentMaxBatch + 1;

                    // --- 1. TẠO DANH SÁCH IN (ẢO) TRƯỚC KHI SỬA DB ---
                    var itemsToPrint = new List<OrderDetail>();

                    foreach (var item in changedItems)
                    {
                        int diff = item.Quantity - item.PrintedQuantity;
                        if (diff == 0) continue;

                        // Tạo món ảo để in
                        var printItem = new OrderDetail
                        {
                            Dish = item.Dish,          // Giữ thông tin món (để lấy Tên, PrinterID)
                            Quantity = diff,           // Số lượng thay đổi (Dương = Thêm, Âm = Hủy)
                            Note = item.Note,
                            KitchenBatch = nextBatch   // Gán đợt mới
                        };
                        itemsToPrint.Add(printItem);
                    }

                    // --- 2. CẬP NHẬT DATABASE ---
                    // Set FirstSentTime on first send
                    if (isFirstSend)
                    {
                        order.FirstSentTime = DateTime.Now;
                    }

                    foreach (var item in changedItems)
                    {
                        // Cập nhật số lượng đã in
                        if (item.Quantity > item.PrintedQuantity) item.KitchenBatch = nextBatch;
                        item.PrintedQuantity = item.Quantity;

                        if (item.Quantity == 0)
                        {
                            db.OrderDetails.Remove(item); // Xóa món SL=0
                        }
                        else
                        {
                            item.ItemStatus = "Sent";
                        }
                    }
                    db.SaveChanges(); // Lưu thay đổi (lúc này món hủy sẽ mất khỏi DB)

                    // Start timer on first send
                    if (isFirstSend)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _currentOrderTime = order.OrderTime;
                            if (!_tableTimeTimer.IsEnabled)
                            {
                                _tableTimeTimer.Start();
                            }
                        });
                    }
                    if (itemsToPrint.Any())
                    {
                        // Gọi hàm PrintKitchen mới (đã sửa ở Bước 2)
                        Services.PrintService.PrintKitchen(order, itemsToPrint, nextBatch);
                    }

                    // ⭐ Notify mobile via SignalR
                    Dispatcher.Invoke(() => NotifyTableUpdated(_selectedTableId));

                    // --- 4. KIỂM TRA ĐƠN RỖNG ---
                    bool isOrderEmpty = !db.OrderDetails.Any(d => d.OrderID == order.OrderID);
                    if (isOrderEmpty)
                    {
                        db.Orders.Remove(order);
                        var table = db.Tables.Find(order.TableID);
                        if (table != null) table.TableStatus = "Empty";
                        db.SaveChanges();

                        Dispatcher.Invoke(() =>
                        {
                            LoadTables();
                            LoadOrderDetails(_selectedTableId);
                            ShowToast("✅ Đã hủy món & Trả bàn trống");
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LoadOrderDetails(_selectedTableId);
                            ShowToast($"✅ Đã gửi Đợt {nextBatch}!");
                        });
                    }
                }
            });
        }
        // --- CÁC HÀM HỖ TRỢ KHÁC (GIỮ NGUYÊN) ---
        private void RecalculateOrder(AppDbContext db, long orderId)
        {
            var order = db.Orders.Include(o => o.OrderDetails).FirstOrDefault(o => o.OrderID == orderId);
            if (order == null) return;
            order.SubTotal = order.OrderDetails.Where(d => d.ItemStatus != "Cancelled").Sum(d => d.TotalAmount);
            decimal discount = (order.DiscountPercent > 0) ? order.SubTotal * order.DiscountPercent / 100 : order.DiscountAmount;
            order.FinalAmount = order.SubTotal - discount;
            if (order.FinalAmount < 0) order.FinalAmount = 0;
            db.SaveChanges();
        }

        public async void ShowToast(string message, int durationMs = 1500)
        {
            lblToastMessage.Text = message;
            bdToast.Visibility = Visibility.Visible;
            await Task.Delay(durationMs);
            bdToast.Visibility = Visibility.Collapsed;
        }

        private void ShowToastPersistent(string message)
        {
            lblToastMessage.Text = message;
            bdToast.Visibility = Visibility.Visible;
        }

        private void HideToast()
        {
            bdToast.Visibility = Visibility.Collapsed;
        }



        private void BtnDiscountBill_Click(object sender, RoutedEventArgs e)
        {
            // (Giữ nguyên logic cũ của bạn)
            if (_selectedTableId == 0) return;
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");
                if (order == null) return;

                bool isPercent = order.DiscountPercent > 0;
                decimal currentVal = isPercent ? order.DiscountPercent : order.DiscountAmount;
                var dialog = new DiscountWindow(currentVal, isPercentMode: isPercent, isEditItem: false);

                if (dialog.ShowDialog() == true)
                {
                    if (dialog.IsPercentage) { order.DiscountPercent = dialog.ResultValue; order.DiscountAmount = 0; }
                    else { order.DiscountAmount = dialog.ResultValue; order.DiscountPercent = 0; }
                    db.SaveChanges();
                    RecalculateOrder(db, order.OrderID);
                    LoadOrderDetails(_selectedTableId);
                }
            }
        }

        private void BtnBackToAdmin_Click(object sender, RoutedEventArgs e)
        {
            AdminWindow admin = new AdminWindow(); admin.Show(); this.Close();
        }

        // --- 6. SIGNALR & CHECKOUT & IN BẾP (MAIN) ---
        private async void SetupRealtime()
        {
            _connection = new HubConnectionBuilder().WithUrl("http://localhost:5000/posHub").WithAutomaticReconnect().Build();
            _connection.On<int>("TableUpdated", (id) => Dispatcher.Invoke(() => { LoadTables(); if (_selectedTableId == id) LoadOrderDetails(id); }));
            try { await _connection.StartAsync(); } catch { }
        }

        // ⭐ Helper: Gửi sự kiện cập nhật bàn cho mobile (via SignalR)
        // [SỬA] Hàm gửi thông báo: Dùng Server Host trực tiếp (Tin cậy hơn)
        private async void NotifyTableUpdated(int tableId)
        {
            try
            {
                // Cách 1 (Tối ưu): Dùng Hub của Server đang chạy trên App
                if (App.WebHost != null)
                {
                    var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                    if (hubContext != null)
                    {
                        await hubContext.Clients.All.SendAsync("TableUpdated", tableId);
                        return;
                    }
                }

                // Cách 2 (Fallback): Dùng Client connection nếu không lấy được Server Hub
                if (_connection != null && _connection.State == HubConnectionState.Connected)
                {
                    await _connection.SendAsync("TableUpdated", tableId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR send error: {ex.Message}");
            }
        }

        private void btnCheckout_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;
            int orderId = 0;
            decimal finalAmount = 0;

            using (var db = new AppDbContext())
            {
                var order = db.Orders.Include(o => o.OrderDetails)
                                     .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order != null)
                {
                    orderId = (int)order.OrderID;
                    finalAmount = order.FinalAmount;

                    bool hasValidItems = order.OrderDetails.Any(d => d.Quantity > 0);
                    if (!hasValidItems)
                    {
                        MessageBox.Show("Đơn hàng đang trống!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (finalAmount <= 0)
                    {
                        if (MessageBox.Show("Thanh toán 0đ để đóng bàn?", "Xác nhận", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    }
                }
            }

            if (orderId == 0) return;

            // Mở cửa sổ thanh toán
            var payWindow = new PaymentWindow(orderId);
            payWindow.ShowDialog();

            if (payWindow.IsPaidSuccess)
            {
                // --- SỬA: Kiểm tra ToggleButton (tglPrintBill) ---
                if (tglPrintBill.IsChecked == true)
                {
                    Services.PrintService.PrintBill(orderId);
                    ShowToast("🖨 Đã in hóa đơn & Thanh toán xong!");
                }
                else
                {
                    ShowToast("💰 Thanh toán thành công (Không in)");
                }

                // Reset table time
                _currentOrderTime = null;
                lblTableTime.Text = "";
                _tableTimeTimer.Stop();

                LoadTables();
                LoadOrderDetails(_selectedTableId);
            }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWin = new HistoryWindow();
            historyWin.ShowDialog();
        }
        private void TxtNote_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && txt.Tag is long detailId)
            {
                string newNote = txt.Text.Trim(); // Lấy nội dung mới

                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail != null)
                    {
                        // Chỉ lưu nếu nội dung thay đổi
                        string oldNote = detail.Note ?? "";
                        if (oldNote != newNote)
                        {
                            detail.Note = newNote;

                            // Nếu món đã gửi bếp mà sửa ghi chú -> Cần đánh dấu để in lại
                            if (detail.ItemStatus == "Sent") detail.ItemStatus = "Modified";

                            db.SaveChanges();

                            // Lưu ý: Không cần reload lại toàn bộ bảng để tránh bị mất focus hoặc giật
                            // Chỉ cần cập nhật trạng thái nút Gửi bếp nếu cần
                            btnSendKitchen.IsEnabled = true;
                            btnSendKitchen.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FD7E14");
                            btnSendKitchen.Content = "🔔 GỬI BẾP (Cập nhật)";
                        }
                    }
                }
            }
        }

        private void BtnMoveTable_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            using (var db = new AppDbContext())
            {
                var currentOrder = db.Orders
                    .Include(o => o.Table)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (currentOrder == null)
                {
                    ShowToast("❌ Không có đơn hàng để chuyển!", 2000);
                    return;
                }

                // Check if there are any available tables
                var availableTables = db.Tables.Where(t => t.TableID != _selectedTableId).ToList();
                if (!availableTables.Any())
                {
                    ShowToast("❌ Không có bàn khác để chuyển!", 2000);
                    return;
                }
            }

            // Enter move mode
            _isWaitingForMoveTargetTable = true;

            ShowToastPersistent("📍 Chọn bàn đích để chuyển...");

            // Switch back to table list view
            pnlMenu.Visibility = Visibility.Collapsed;
            pnlTableList.Visibility = Visibility.Visible;
            _tableTimeTimer.Stop();
        }

        private void ExecuteMoveTable(int targetTableId)
        {
            if (targetTableId == _selectedTableId)
            {
                ShowToast("❌ Vui lòng chọn bàn khác!", 2000);
                _isWaitingForMoveTargetTable = false;
                return;
            }

            using (var db = new AppDbContext())
            {
                var sourceOrder = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .Include(o => o.Table)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                var targetOrder = db.Orders
                    .FirstOrDefault(o => o.TableID == targetTableId && o.OrderStatus == "Pending");

                if (sourceOrder != null)
                {
                    // Lưu tên bàn cũ trước khi cập nhật
                    string oldTableName = sourceOrder.Table?.TableName ?? $"Bàn {_selectedTableId}";

                    // If target table already has an order, merge them
                    if (targetOrder != null)
                    {
                        // Move all order details from source to target
                        foreach (var detail in sourceOrder.OrderDetails)
                        {
                            detail.OrderID = targetOrder.OrderID;
                        }
                    }
                    else
                    {
                        // Move entire order to target table
                        sourceOrder.TableID = targetTableId;
                    }

                    // Update source table status to empty
                    var sourceTable = db.Tables.FirstOrDefault(t => t.TableID == _selectedTableId);
                    if (sourceTable != null)
                    {
                        sourceTable.TableStatus = "Empty";
                    }

                    // Update target table status to occupied
                    var targetTable = db.Tables.FirstOrDefault(t => t.TableID == targetTableId);
                    if (targetTable != null)
                    {
                        targetTable.TableStatus = "Occupied";
                    }

                    db.SaveChanges();

                    // Recalculate totals
                    if (targetOrder != null)
                    {
                        RecalculateOrder(db, targetOrder.OrderID);
                    }
                    else
                    {
                        RecalculateOrder(db, sourceOrder.OrderID);
                    }

                    // Lấy tên bàn mới
                    var newTableInfo = db.Tables.FirstOrDefault(t => t.TableID == targetTableId);
                    string newTableName = newTableInfo?.TableName ?? $"Bàn {targetTableId}";

                    // In thông báo chuyển bàn cho các máy in tương ứng
                    var orderToNotify = targetOrder ?? sourceOrder;
                    PrintService.PrintMoveTableNotification(orderToNotify, oldTableName, newTableName);

                    Dispatcher.Invoke(() =>
                    {
                        _isWaitingForMoveTargetTable = false;
                        HideToast();

                        LoadTables();
                        SelectAndLoadTable(targetTableId);

                        ShowToast("✅ Chuyển bàn thành công!", 2000);
                    });
                }
            }
        }

        private void BtnSplitTable_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            _isSplitMode = !_isSplitMode;
            _splitQuantities.Clear();

            if (_isSplitMode)
            {
                // Enter split mode
                btnTransferSplit.Visibility = Visibility.Visible;
                btnDiscountBill.Visibility = Visibility.Collapsed;
                colSplitQuantity.Visibility = Visibility.Visible;  // Show split column
                LoadOrderDetailsInSplitMode();
            }
            else
            {
                // Exit split mode
                btnTransferSplit.Visibility = Visibility.Collapsed;
                btnDiscountBill.Visibility = Visibility.Visible;
                colSplitQuantity.Visibility = Visibility.Collapsed;  // Hide split column
                LoadOrderDetails(_selectedTableId);
            }
        }

        private void HideQuantityControls()
        {
            // Hàm này không còn cần thiết - sử dụng binding thay thế
        }

        private void ShowQuantityControls()
        {
            // Hàm này không còn cần thiết - sử dụng binding thay thế
        }

        private void UpdateQuantityControlsVisibility(System.Windows.Controls.DataGridRow row, Visibility visibility)
        {
            // Hàm này không còn cần thiết - sử dụng binding thay thế
        }

        private void LoadOrderDetailsInSplitMode()
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order != null)
                {
                    var viewModels = order.OrderDetails.OrderBy(d => d.OrderDetailID).Select(d => new OrderDetailViewModel
                    {
                        OrderDetailID = d.OrderDetailID,
                        DishName = d.Dish != null ? d.Dish.DishName : "Unknown",
                        Quantity = d.Quantity,
                        TotalAmount = d.TotalAmount,
                        DiscountRate = d.DiscountRate,
                        ItemStatus = d.ItemStatus,
                        Note = d.Note ?? "",
                        BatchDisplay = d.PrintedQuantity == 0 ? "⏳" : (d.KitchenBatch > 0 ? $"Đợt {d.KitchenBatch}" : "---"),
                        // Kiểm tra ItemStatus từ mobile: Nếu 'Sent' thì đã gửi từ điện thoại
                        StatusDisplay = d.Quantity == 0 ? "❌ CHỜ HỦY" :
                                       (d.ItemStatus == "Sent" ? "✓ Từ Mobile" :
                                       (d.Quantity != d.PrintedQuantity ? "Cần Gửi" : "OK")),
                        RowColor = d.Quantity == 0 ? "#FFCCCC" :
                                  (d.ItemStatus == "Sent" ? "#D4EDDA" :  // Green cho items từ mobile
                                  (d.Quantity != d.PrintedQuantity ? "#FFF3CD" : "White")),  // Yellow cho items chưa gửi
                        SplitQuantity = 0,
                        IsInSplitMode = true  // Set để ẩn nút +/-
                    }).ToList();

                    lstOrderDetails.ItemsSource = viewModels;
                }
            }
        }

        private void NumericOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void TxtSplitQty_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && txt.Tag is long orderDetailId)
            {
                if (int.TryParse(txt.Text, out int splitQty))
                {
                    // Find the corresponding order detail to validate
                    if (lstOrderDetails.ItemsSource is System.Collections.IEnumerable items)
                    {
                        foreach (var item in items)
                        {
                            if (item is OrderDetailViewModel vm && vm.OrderDetailID == orderDetailId)
                            {
                                if (splitQty > vm.Quantity)
                                {
                                    ShowToast("❌ Số lượng tách không vượt quá hiện có!", 2000);
                                    txt.Text = "0";
                                    vm.SplitQuantity = 0;
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void BtnTransferSplit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            // Collect all items with split quantity > 0
            var itemsToTransfer = new Dictionary<long, int>();
            if (lstOrderDetails.ItemsSource is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is OrderDetailViewModel vm && vm.SplitQuantity > 0)
                    {
                        itemsToTransfer[vm.OrderDetailID] = vm.SplitQuantity;
                    }
                }
            }

            if (itemsToTransfer.Count == 0)
            {
                ShowToast("❌ Vui lòng chọn ít nhất một món để tách!", 2000);
                return;
            }

            // Validate quantities
            using (var db = new AppDbContext())
            {
                foreach (var kvp in itemsToTransfer)
                {
                    var detail = db.OrderDetails.FirstOrDefault(d => d.OrderDetailID == kvp.Key);
                    if (detail != null && kvp.Value > detail.Quantity)
                    {
                        ShowToast("❌ Số lượng tách vượt quá hiện có!", 2000);
                        return;
                    }
                }
            }

            // Set waiting mode and show persistent popup to select destination table
            _isWaitingForTargetTable = true;
            _pendingSplitItems = itemsToTransfer;

            ShowToastPersistent("📍 Chọn bàn đích để tách...");

            // Switch back to table list view
            pnlMenu.Visibility = Visibility.Collapsed;
            pnlTableList.Visibility = Visibility.Visible;
            _tableTimeTimer.Stop();
        }

        private void ExecuteSplitTransfer(int targetTableId)
        {
            if (targetTableId == _selectedTableId)
            {
                ShowToast("❌ Vui lòng chọn bàn khác!", 2000);
                _isWaitingForTargetTable = false;
                _pendingSplitItems.Clear();
                return;
            }

            using (var db = new AppDbContext())
            {
                var sourceOrder = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                var targetOrder = db.Orders
                    .Include(o => o.OrderDetails)
                    .FirstOrDefault(o => o.TableID == targetTableId && o.OrderStatus == "Pending");

                if (targetOrder == null)
                {
                    targetOrder = new Order
                    {
                        TableID = targetTableId,
                        OrderTime = DateTime.Now,
                        OrderStatus = "Pending",
                        PaymentMethod = "Cash",
                        FirstSentTime = sourceOrder?.FirstSentTime
                    };
                    db.Orders.Add(targetOrder);
                    db.SaveChanges();
                }

                if (sourceOrder != null)
                {
                    // Transfer selected items
                    foreach (var kvp in _pendingSplitItems)
                    {
                        var detail = sourceOrder.OrderDetails.FirstOrDefault(d => d.OrderDetailID == kvp.Key);
                        if (detail != null)
                        {
                            if (kvp.Value == detail.Quantity)
                            {
                                // Move entire item
                                detail.OrderID = targetOrder.OrderID;
                            }
                            else
                            {
                                // Split item: create new item for target table
                                decimal dishPrice = detail.Dish?.Price ?? 0;

                                // Calculate how many of the split items were already printed
                                int printedToSplit = Math.Min(kvp.Value, detail.PrintedQuantity);

                                var newDetail = new OrderDetail
                                {
                                    OrderID = targetOrder.OrderID,
                                    DishID = detail.DishID,
                                    Quantity = kvp.Value,
                                    PrintedQuantity = printedToSplit,  // Split the printed quantity
                                    KitchenBatch = detail.KitchenBatch,
                                    TotalAmount = dishPrice * kvp.Value,
                                    ItemStatus = detail.ItemStatus,  // Inherit status from original
                                    Note = detail.Note
                                };
                                db.OrderDetails.Add(newDetail);

                                // Reduce original item
                                detail.Quantity -= kvp.Value;
                                detail.PrintedQuantity -= printedToSplit;  // Also reduce printed quantity
                                detail.TotalAmount = dishPrice * detail.Quantity;
                            }
                        }
                    }

                    // Update target table status
                    var targetTable = db.Tables.FirstOrDefault(t => t.TableID == targetTableId);
                    if (targetTable != null)
                    {
                        targetTable.TableStatus = "Occupied";
                    }

                    db.SaveChanges();

                    // Reload source order to get updated OrderDetails
                    db.Entry(sourceOrder).Reload();

                    // Check if source order still has items with quantity > 0
                    bool sourceOrderHasItems = sourceOrder.OrderDetails.Any(d => d.Quantity > 0);

                    // If source order has no items left, delete it and mark table as empty
                    if (!sourceOrderHasItems)
                    {
                        // Delete source order
                        db.Orders.Remove(sourceOrder);

                        var sourceTable = db.Tables.FirstOrDefault(t => t.TableID == _selectedTableId);
                        if (sourceTable != null)
                        {
                            sourceTable.TableStatus = "Empty";
                        }

                        db.SaveChanges();
                    }
                    else
                    {
                        // Recalculate totals for source order if it still has items
                        RecalculateOrder(db, sourceOrder.OrderID);
                    }

                    // Recalculate totals for target order
                    RecalculateOrder(db, targetOrder.OrderID);

                    Dispatcher.Invoke(() =>
                    {
                        _isWaitingForTargetTable = false;
                        _pendingSplitItems.Clear();
                        HideToast();

                        // Reset split mode UI when transfer completes
                        _isSplitMode = false;
                        _splitQuantities.Clear();
                        btnTransferSplit.Visibility = Visibility.Collapsed;
                        btnDiscountBill.Visibility = Visibility.Visible;
                        colSplitQuantity.Visibility = Visibility.Collapsed;

                        LoadTables();
                        SelectAndLoadTable(targetTableId);

                        ShowToast("✅ Tách bàn thành công!", 2000);
                    });
                }
            }
        }

        // Timer handler to update table time display
        private void TableTimeTimer_Tick(object sender, EventArgs e)
        {
            if (_currentOrderTime.HasValue)
            {
                var elapsed = DateTime.Now - _currentOrderTime.Value;
                if (elapsed.TotalMinutes < 1)
                    lblTableTime.Text = $"{(int)elapsed.TotalSeconds}s";
                else if (elapsed.TotalHours < 1)
                    lblTableTime.Text = $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                else
                    lblTableTime.Text = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
            }
        }
    }
    // Class dùng để hiển thị lên DataGrid và hỗ trợ sửa Ghi chú
    public class OrderDetailViewModel
    {
        public long OrderDetailID { get; set; }
        public string DishName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DiscountRate { get; set; }
        public string ItemStatus { get; set; } = "";

        // Ghi chú (Cho phép sửa đổi)
        public string Note { get; set; } = "";

        // Các thuộc tính hiển thị
        public bool HasDiscount => DiscountRate > 0;
        public string DiscountDisplay => HasDiscount ? $"Giảm {DiscountRate:0.#}%" : "";
        public string BatchDisplay { get; set; } = "";
        public string StatusDisplay { get; set; } = "";
        public string RowColor { get; set; } = "White";

        // Split mode property - để kiểm soát visibility nút +/-
        public bool IsInSplitMode { get; set; } = false;

        // Split mode properties
        public int SplitQuantity { get; set; } = 0;  // Quantity to split in split mode
    }

    // Extension methods để tìm visual children
    public static class VisualTreeHelper_Extensions
    {
        public static T FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T childOfType)
                    return childOfType;
                var result = child.FindVisualChild<T>();
                if (result != null)
                    return result;
            }
            return null;
        }

        public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T childOfType)
                    yield return childOfType;
                foreach (var descendant in child.FindVisualChildren<T>())
                    yield return descendant;
            }
        }
    }

    // Converter để convert boolean thành visibility (Inverse)
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Nếu true (split mode) -> Collapsed (ẩn nút)
                // Nếu false (normal mode) -> Visible (hiện nút)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return false;
        }
    }

    // Converter để hiển thị placeholder khi text rỗng
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                // Nếu text rỗng hoặc null → Hiện placeholder (Visible)
                // Nếu có text → Ẩn placeholder (Collapsed)
                return string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel; // [NEW]
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using System.Threading.Tasks;
using System.Globalization;
using System.Text;
// [THÊM] Namespace quan trọng
using Microsoft.AspNetCore.SignalR;        // Cho Server (Gửi đi)
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Server.Hubs;
using System.Threading;
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
        public bool IsGrayedOut { get; set; } = false; // [NEW] Gray out source table
        public SolidColorBrush ColorBrush => IsGrayedOut ? new SolidColorBrush(Colors.Gray) : (TableStatus == "Occupied" ? new SolidColorBrush(Color.FromRgb(220, 53, 69)) : new SolidColorBrush(Color.FromRgb(40, 167, 69)));
        public bool IsRequestingPayment { get; set; } = false;
        public bool HasProvisionalBill { get; set; } = false; // [NEW]
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

    public class ReprintItemViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public List<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        public string DisplayText { get; set; }

        public int TotalQuantity => OrderDetails.Sum(d => d.Quantity);

        private int _selectedQuantity;
        public int SelectedQuantity
        {
            get => _selectedQuantity;
            set
            {
                if (_selectedQuantity != value)
                {
                    _selectedQuantity = value;
                    OnPropertyChanged(nameof(SelectedQuantity));
                    OnPropertyChanged(nameof(QuantityDisplay));
                }
            }
        }

        public string QuantityDisplay => $"{SelectedQuantity}/{TotalQuantity}";
        public string Note => OrderDetails.FirstOrDefault()?.Note ?? "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    // If selected -> Default to Max, else 0? 
                    // Let's keep SelectedQuantity sticky or reset? 
                    // Logic: If checking, ensure at least 1 is selected (Max).
                    if (value && SelectedQuantity == 0) SelectedQuantity = TotalQuantity;

                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
    }

    public partial class MainWindow : Window
    {
        private HubConnection _connection = default!;
        private int _selectedTableId = 0;
        private int? _selectedCategoryId = null; // Filter by CategoryID
        private string _tableTypeFilter = "All"; // Legacy or backup

        // List categories for Filter Bar
        public List<TableCategory> FilterCategories { get; set; } = new List<TableCategory>();

        // Notification List for UI (persisted in DB)
        public ObservableCollection<string> NotificationList { get; } = new ObservableCollection<string>();

        private readonly SemaphoreSlim _activityLogLock = new SemaphoreSlim(1, 1);

        private List<Dish> _allDishes = new List<Dish>();
        private List<DishViewModel> _dishViewModels = new List<DishViewModel>();
        private DispatcherTimer _tableTimeTimer = new DispatcherTimer();
        private DispatcherTimer _tableListUpdateTimer = new DispatcherTimer();
        private DateTime? _currentOrderTime = null;
        private HashSet<int> _tablesRequestingPayment = new HashSet<int>();

        private bool _isWaitingForTargetTable = false;  // True when waiting for user to click target table
        private Dictionary<long, int> _pendingSplitItems = new Dictionary<long, int>();  // Items to split when table selected

        // Move table mode variables
        private bool _isWaitingForMoveTargetTable = false;  // True when waiting for user to click target table for move
        private const int MIN_SECONDS_WAIT = 10; // Thời gian chờ giữa Check-in và Check-out chấm công
        public MainWindow()
        {
            InitializeComponent();

            // Load Categories for Filter
            using (var db = new AppDbContext())
            {
                FilterCategories = db.TableCategories.ToList();
            }
            this.DataContext = this; // Bind to self

            LoadActivityLogFromDb();

            LoadTables();
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
            btnDiscountBill.Visibility = Visibility.Collapsed; // [FIX] Hide initially

            LoadTables();
            LoadMenu();
            SetupRealtime();
        }

        private void LoadActivityLogFromDb()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var latest = db.ActivityLogs
                        .OrderByDescending(x => x.Id)
                        .Take(200)
                        .Select(x => new { x.CreatedAt, x.Message })
                        .ToList();

                    NotificationList.Clear();
                    foreach (var row in latest)
                    {
                        var ts = row.CreatedAt == default ? DateTime.Now : row.CreatedAt;
                        NotificationList.Add($"[{ts:HH:mm:ss}] {row.Message}");
                    }
                }
            }
            catch
            {
                // Ignore (DB may not be ready yet); realtime will still function.
            }
        }

        private void AppendActivityLog(string message)
        {
            // Fire-and-forget to avoid blocking UI thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await _activityLogLock.WaitAsync();
                    try
                    {
                        using (var db = new AppDbContext())
                        {
                            db.ActivityLogs.Add(new ActivityLogEntry
                            {
                                CreatedAt = DateTime.Now,
                                Message = message
                            });
                            await db.SaveChangesAsync();

                            // Keep only 200 newest in DB
                            try
                            {
                                await db.Database.ExecuteSqlRawAsync(@"
                                    DELETE FROM ""ActivityLogs""
                                    WHERE ""Id"" NOT IN (
                                        SELECT ""Id"" FROM ""ActivityLogs"" ORDER BY ""Id"" DESC LIMIT 200
                                    );
                                ");
                            }
                            catch { }
                        }
                    }
                    finally
                    {
                        _activityLogLock.Release();
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        NotificationList.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                        while (NotificationList.Count > 200) NotificationList.RemoveAt(NotificationList.Count - 1);
                    });
                }
                catch { }
            });
        }


        // --- 1. CHUYỂN ĐỔI VIEW ---
        private void lstTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTables.SelectedItem is TableViewModel selected)
            {
                // [NEW] Prevent selecting grayed-out tables (Source table in split mode)
                if (selected.IsGrayedOut)
                {
                    lstTables.SelectedItem = null;
                    return;
                }

                // If waiting for target table in split mode, transfer items instead of opening menu
                if (_isWaitingForTargetTable && _pendingSplitItems.Count > 0)
                {
                    int targetTableId = selected.TableID;
                    lstTables.SelectedItem = null;  // Deselect to reset
                    ExecuteSplitTransfer(targetTableId);
                    return;
                }
                // [FIX] Clear "Request Payment" flag in DB when selecting table
                if (selected.IsRequestingPayment)
                {
                    using (var db = new AppDbContext())
                    {
                        var order = db.Orders.FirstOrDefault(o => o.TableID == selected.TableID && o.OrderStatus == "Pending");
                        if (order != null && order.IsRequestingPayment)
                        {
                            order.IsRequestingPayment = false;
                            db.SaveChanges();

                            // Send SignalR to update other clients
                            if (_connection.State == HubConnectionState.Connected)
                            {
                                _connection.SendAsync("NotifyTableUpdated", selected.TableID);
                            }
                        }
                    }
                    // Refresh UI immediately
                    LoadTables();
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
                btnSplitTable.Visibility = Visibility.Visible;
                btnMoveTable.Visibility = Visibility.Visible;
                btnReprintKitchen.Visibility = Visibility.Visible;
                btnDiscountBill.Visibility = Visibility.Visible; // [FIX] Show when table selected

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
            btnReprintKitchen.Visibility = Visibility.Collapsed;

            // Reset split mode when returning to table list
            _isWaitingForTargetTable = false;
            _pendingSplitItems.Clear();
            _pendingSplitItems.Clear();
            btnDiscountBill.Visibility = Visibility.Collapsed; // [FIX] Hide when returning to table list

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
            btnMoveTable.Visibility = Visibility.Visible;
            btnReprintKitchen.Visibility = Visibility.Visible;
            btnDiscountBill.Visibility = Visibility.Visible; // [FIX] Show when table loaded

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
                if (_selectedCategoryId.HasValue)
                {
                    tables = tables.Where(t => t.CategoryID == _selectedCategoryId.Value).ToList();
                }
                else if (_tableTypeFilter != "All") // Backwards compatibility for old buttons if any exist
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
                        TimeDisplay = "",
                        // [NEW] Gray out if this is the source table and we are waiting for target (Split or Move)
                        IsGrayedOut = ((_isWaitingForTargetTable || _isWaitingForMoveTargetTable) && t.TableID == _selectedTableId)
                    };

                    // Calculate time for occupied tables with pending orders that have been sent to kitchen
                    if (t.TableStatus == "Occupied" && t.Orders.Any())
                    {
                        var order = t.Orders.FirstOrDefault(o => o.OrderStatus == "Pending");
                        // Only show time if FirstSentTime has value (order has been sent to kitchen)
                        if (order != null)
                        {
                            if (order.FirstSentTime.HasValue)
                            {
                                var elapsed = DateTime.Now - order.FirstSentTime.Value;
                                if (elapsed.TotalMinutes < 1)
                                    vm.TimeDisplay = $"{(int)elapsed.TotalSeconds}s";
                                else if (elapsed.TotalHours < 1)
                                    vm.TimeDisplay = $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                                else
                                    vm.TimeDisplay = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
                            }
                            // [NEW] Check provisional bill
                            if (order.IsPreCalculated) vm.HasProvisionalBill = true;
                            // [NEW] Check request payment (Persisted)
                            if (order.IsRequestingPayment) vm.IsRequestingPayment = true;
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
                    // (Giữ nguyên đoạn xử lý Timer...)
                    if (order.FirstSentTime.HasValue) { /*Code cũ...*/ } else { /*Code cũ...*/ }

                    var viewModels = order.OrderDetails
                        .GroupBy(d => new
                        {
                            d.DishID,
                            // [FIX] Group Sent and Modified together. New items stay separate.
                            GroupStatus = (d.ItemStatus == "New") ? "New" : "SentGroup",
                            Note = (d.Note ?? "").Trim()
                        })
                        .Select(g => new OrderDetailViewModel
                        {
                            // [FIX] Use First ID as representative. Handlers will look up siblings.
                            OrderDetailID = g.First().OrderDetailID,
                            DishName = g.First().Dish != null ? g.First().Dish.DishName : "Unknown",
                            UnitPrice = g.First().UnitPrice,
                            DiscountRate = g.First().DiscountRate,
                            // Status for logic: If any is New, it's New. Else check if modified.
                            ItemStatus = g.Key.GroupStatus == "New" ? "New" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "Modified" : "Sent"),
                            Note = g.Key.Note,
                            Quantity = g.Sum(x => x.Quantity),
                            TotalAmount = g.Sum(x => x.TotalAmount),

                            KitchenBatch = g.Max(x => x.KitchenBatch),

                            BatchDisplay = g.Sum(x => x.PrintedQuantity) == 0 ? "⏳" : (g.Max(x => x.KitchenBatch) > 0 ? $"Đợt {g.Max(x => x.KitchenBatch)}" : "---"),

                            // [FIX] Status Display Logic
                            StatusDisplay = g.Sum(x => x.Quantity) == 0 ? "❌ CHỜ HỦY" :
                                            (g.Key.GroupStatus == "New" ? "Mới" :
                                            (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "⚠️ Sửa đổi" : "✓ Đã gửi")),

                            // [FIX] Row Color Logic
                            RowColor = g.Sum(x => x.Quantity) == 0 ? "#FFCCCC" : // Red for Cancel All
                                       (g.Key.GroupStatus == "New" ? "#FFF3CD" : // Yellow for New
                                       (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "#FFF3CD" : "#D4EDDA")) // Yellow for Modified, Green for Sent
                        })
                        // --- [SỬA ĐỔI QUAN TRỌNG: LOGIC SẮP XẾP] ---
                        .OrderByDescending(vm => vm.ItemStatus == "New")
                        .ThenByDescending(vm => vm.KitchenBatch)
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
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = Char.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd')
                .Replace('Đ', 'D');
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

        // --- ASYNC VERSIONS FOR HIGH PERFORMANCE ---
        private async Task LoadTablesAsync()
        {
            try
            {
                // 1. Fetch Data in Background
                var viewModels = await Task.Run(() =>
                {
                    using (var db = new AppDbContext())
                    {
                        var tables = db.Tables.Include(t => t.Orders).ThenInclude(o => o.OrderDetails).ToList();

                        // Use local copies of filters to avoid threading issues
                        // Note: If you have dynamic filters, capture them before Task.Run or use Dispatcher if they are UI elements (but here they are simple fields)
                        int? catId = _selectedCategoryId;
                        string typeFilter = _tableTypeFilter;

                        // Apply filter
                        if (catId.HasValue)
                        {
                            tables = tables.Where(t => t.CategoryID == catId.Value).ToList();
                        }
                        else if (typeFilter != "All")
                        {
                            tables = tables.Where(t => t.TableType == typeFilter).ToList();
                        }

                        return tables.Select(t =>
                        {
                            var vm = new TableViewModel
                            {
                                TableID = t.TableID,
                                TableName = t.TableName,
                                TableStatus = t.TableStatus,
                                TimeDisplay = "",
                                IsGrayedOut = false // Simplified safely
                            };

                            // Calculate time
                            if (t.TableStatus == "Occupied" && t.Orders.Any())
                            {
                                var order = t.Orders.FirstOrDefault(o => o.OrderStatus == "Pending");
                                if (order != null)
                                {
                                    if (order.FirstSentTime.HasValue)
                                    {
                                        var elapsed = DateTime.Now - order.FirstSentTime.Value;
                                        if (elapsed.TotalMinutes < 1) vm.TimeDisplay = $"{(int)elapsed.TotalSeconds}s";
                                        else if (elapsed.TotalHours < 1) vm.TimeDisplay = $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                                        else vm.TimeDisplay = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
                                    }
                                    if (order.IsPreCalculated) vm.HasProvisionalBill = true;
                                    if (order.IsRequestingPayment) vm.IsRequestingPayment = true;
                                }
                            }
                            return vm;
                        }).ToList();
                    }
                });

                // 2. Update UI on Dispatcher
                if (viewModels != null)
                {
                    // Re-apply any UI-specific state like selection gray-out
                    if (_isWaitingForTargetTable || _isWaitingForMoveTargetTable)
                    {
                        var selected = viewModels.FirstOrDefault(t => t.TableID == _selectedTableId);
                        if (selected != null) selected.IsGrayedOut = true;
                    }
                    lstTables.ItemsSource = viewModels;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Async LoadTables Error: " + ex.Message);
            }
        }

        private async Task LoadOrderDetailsAsync(int tableId)
        {
            try
            {
                // 1. Fetch Logic in Background
                var result = await Task.Run(() =>
                {
                    using (var db = new AppDbContext())
                    {
                        var order = db.Orders
                            .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                            .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                        if (order == null) return null;

                        // Calculate totals
                        decimal subTotal = order.OrderDetails.Where(d => d.Quantity > 0).Sum(d => d.Quantity * d.UnitPrice);
                        decimal discountVal = (order.DiscountPercent > 0) ? subTotal * (order.DiscountPercent / 100) : order.DiscountAmount;
                        decimal final = subTotal - discountVal;

                        // Create View Models
                        var vms = order.OrderDetails
                            .GroupBy(d => new { d.DishID, GroupStatus = (d.ItemStatus == "New" ? "New" : "SentGroup"), Note = (d.Note ?? "").Trim() })
                            .Select(g => new OrderDetailViewModel
                            {
                                OrderDetailID = g.First().OrderDetailID,
                                DishName = g.First().Dish != null ? g.First().Dish.DishName : "Unknown",
                                UnitPrice = g.First().UnitPrice,
                                DiscountRate = g.First().DiscountRate,
                                ItemStatus = g.Key.GroupStatus == "New" ? "New" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "Modified" : "Sent"),
                                Note = g.Key.Note,
                                Quantity = g.Sum(x => x.Quantity),
                                TotalAmount = g.Sum(x => x.TotalAmount),
                                KitchenBatch = g.Max(x => x.KitchenBatch),
                                BatchDisplay = g.Sum(x => x.PrintedQuantity) == 0 ? "⏳" : (g.Max(x => x.KitchenBatch) > 0 ? $"Đợt {g.Max(x => x.KitchenBatch)}" : "---"),
                                StatusDisplay = g.Sum(x => x.Quantity) == 0 ? "❌ CHỜ HỦY" : (g.Key.GroupStatus == "New" ? "Mới" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "⚠️ Sửa đổi" : "✓ Đã gửi")),
                                RowColor = g.Sum(x => x.Quantity) == 0 ? "#FFCCCC" : (g.Key.GroupStatus == "New" ? "#FFF3CD" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "#FFF3CD" : "#D4EDDA"))
                            })
                            .OrderByDescending(vm => vm.ItemStatus == "New")
                            .ThenByDescending(vm => vm.KitchenBatch)
                            .ThenBy(vm => vm.DishName)
                            .ToList();

                        return new
                        {
                            ViewModels = vms,
                            HasChanges = order.OrderDetails.Any(d => d.Quantity != d.PrintedQuantity),
                            HasValidItems = order.OrderDetails.Any(d => d.Quantity > 0),
                            Order = new { order.SubTotal, order.FinalAmount, order.DiscountPercent, order.DiscountAmount, order.OrderTime, order.FirstSentTime }
                        };
                    }
                });

                // 2. Update UI
                // [FIX] Race Condition: Check if user is still on this table
                if (tableId != _selectedTableId) return;

                if (result != null)
                {
                    lstOrderDetails.ItemsSource = result.ViewModels;

                    // Update Labels
                    lblSubTotal.Text = result.Order.SubTotal.ToString("N0") + "đ";
                    lblTotal.Text = result.Order.FinalAmount.ToString("N0") + "đ";

                    decimal dVal = (result.Order.DiscountPercent > 0) ? result.Order.SubTotal * (result.Order.DiscountPercent / 100) : result.Order.DiscountAmount;
                    if (dVal > 0)
                    {
                        lblDiscount.Text = $"-{dVal:N0}đ";
                        pnlDiscount.Visibility = Visibility.Visible;
                    }
                    else pnlDiscount.Visibility = Visibility.Collapsed;

                    // Update Buttons
                    btnCheckout.IsEnabled = result.HasValidItems;
                    btnSendKitchen.IsEnabled = result.HasChanges;
                    btnSendKitchen.Content = result.HasChanges ? "🔔 GỬI BẾP (Cập nhật)" : "👨‍🍳 GỬI BẾP";
                    btnSendKitchen.Background = result.HasChanges ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FD7E14") : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#6C757D");

                    // Timer logic if needed (Assuming timer handles itself or only stopped when leaving table)
                    if (result.Order.FirstSentTime.HasValue)
                    {
                        _currentOrderTime = result.Order.FirstSentTime;
                        if (!_tableTimeTimer.IsEnabled) _tableTimeTimer.Start();
                    }
                }
                else
                {
                    // Empty table logic
                    lstOrderDetails.ItemsSource = null;
                    lblTotal.Text = "0đ";
                    lblSubTotal.Text = "0đ";
                    pnlDiscount.Visibility = Visibility.Collapsed;
                    btnCheckout.IsEnabled = false;
                    btnSendKitchen.IsEnabled = false;
                }
            }
            catch { }
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
                // 1. Lấy đơn hàng
                var order = db.Orders.Include(o => o.OrderDetails)
                                     .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order == null)
                {
                    // ... (Giữ nguyên logic tạo order mới của bạn) ...
                    int? currentAccId = UserSession.AccID > 0 ? UserSession.AccID : (db.Accounts.FirstOrDefault()?.AccID);
                    order = new Order
                    {
                        TableID = tableId,
                        AccID = currentAccId,
                        OrderTime = DateTime.Now,
                        OrderStatus = "Pending",
                        PaymentMethod = "Cash",
                        OrderDetails = new List<OrderDetail>()
                    };
                    db.Orders.Add(order);
                    var table = db.Tables.Find(tableId);
                    if (table != null) table.TableStatus = "Occupied";
                }

                // 2. Tìm dòng để gộp: PHẢI LÀ MÓN MỚI (New) VÀ KHÔNG GHI CHÚ
                var existingDetail = order.OrderDetails
                    .FirstOrDefault(d => d.DishID == dishId
                                      && d.ItemStatus == "New" // <--- QUAN TRỌNG: Chỉ gộp vào món chưa gửi
                                      && (string.IsNullOrEmpty(d.Note)));

                var dishInfo = db.Dishes.Find(dishId);
                if (dishInfo == null) return;

                // [FIX] Lấy giá hiện tại (áp dụng rule giá nếu có)
                decimal currentPrice = Services.PriceService.GetCurrentPrice(dishId);

                if (existingDetail != null)
                {
                    // TÌM THẤY món New -> Cộng dồn
                    // Cập nhật lại giá mới nhất cho dòng đang chờ (nếu giá có thay đổi)
                    existingDetail.UnitPrice = currentPrice;
                    existingDetail.Quantity++;
                    existingDetail.TotalAmount = existingDetail.Quantity * existingDetail.UnitPrice;
                }
                else
                {
                    // KHÔNG TÌM THẤY (hoặc chỉ có món Sent) -> TẠO DÒNG MỚI
                    order.OrderDetails.Add(new OrderDetail
                    {
                        DishID = dishId,
                        Quantity = 1,
                        UnitPrice = currentPrice,
                        ItemStatus = "New", // Luôn là New
                        PrintedQuantity = 0,
                        TotalAmount = currentPrice,
                        Note = "",
                        ItemOrderTime = DateTime.Now
                    });
                }

                // Tính tổng tiền
                order.SubTotal = order.OrderDetails.Sum(d => d.TotalAmount);
                order.FinalAmount = order.SubTotal;

                db.SaveChanges();

                if (_selectedTableId == tableId) LoadOrderDetails(tableId);
                ShowToast($"Đã chọn: {dishInfo.DishName}");
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

                    // [FIX] Group Logic: Nếu click vào nhóm Sent/Modified
                    if (detail.ItemStatus == "Sent" || detail.ItemStatus == "Modified")
                    {
                        // Tìm xem trong nhóm (Cùng Dish + Note + Đã gửi) có món nào đang bị giảm (Qty < Printed) không?
                        // Nếu có -> Tăng nó lên (Undo Cancel)
                        // Nếu không -> Gọi AddDishToOrder để thêm mới

                        var note = (detail.Note ?? "").Trim();
                        var candidate = db.OrderDetails
                            .Where(d => d.OrderID == detail.OrderID
                                     && d.DishID == detail.DishID
                                     && (d.Note ?? "").Trim() == note
                                     && (d.ItemStatus == "Sent" || d.ItemStatus == "Modified")
                                     && d.Quantity < d.PrintedQuantity) // ĐK: Đang bị giảm
                            .OrderByDescending(d => d.KitchenBatch) // Ưu tiên đợt mới nhất
                            .FirstOrDefault();

                        if (candidate != null)
                        {
                            // Restore Quantity
                            candidate.Quantity++;
                            candidate.TotalAmount = candidate.Quantity * candidate.UnitPrice * (1 - candidate.DiscountRate / 100);

                            // Restore Status if full
                            if (candidate.ItemStatus == "Modified" && candidate.Quantity == candidate.PrintedQuantity)
                            {
                                candidate.ItemStatus = "Sent";
                            }
                            db.SaveChanges();
                            RecalculateOrder(db, candidate.OrderID);
                            LoadOrderDetails(_selectedTableId);
                            NotifyTableUpdated(_selectedTableId);
                        }
                        else
                        {
                            // Full hết rồi -> Thêm món mới
                            AddDishToOrder(_selectedTableId, detail.DishID);
                        }
                    }
                    else
                    {
                        // Món đang chờ (New) -> Tăng số lượng bình thường
                        detail.Quantity++;
                        detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);
                        db.SaveChanges();
                        RecalculateOrder(db, detail.OrderID);
                        LoadOrderDetails(_selectedTableId);
                        NotifyTableUpdated(_selectedTableId);
                    }
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
                    // Lấy detail gốc để biết Context (Order, Dish, Note)
                    var originalDetail = db.OrderDetails.Include(d => d.Dish).FirstOrDefault(d => d.OrderDetailID == detailId);
                    if (originalDetail == null) return;

                    OrderDetail targetDetail = originalDetail;

                    // [FIX] Group Logic: Nếu Click vào nhóm Sent/Modified
                    // Cần tìm món nào trong nhóm CÒN SỐ LƯỢNG để trừ
                    if (originalDetail.ItemStatus == "Sent" || originalDetail.ItemStatus == "Modified")
                    {
                        var note = (originalDetail.Note ?? "").Trim();
                        // Ưu tiên trừ món Batch cao nhất trước (Mới gọi nhất)
                        var candidate = db.OrderDetails
                            .Where(d => d.OrderID == originalDetail.OrderID
                                     && d.DishID == originalDetail.DishID
                                     && (d.Note ?? "").Trim() == note
                                     && (d.ItemStatus == "Sent" || d.ItemStatus == "Modified")
                                     && d.Quantity > 0)
                            .OrderByDescending(d => d.KitchenBatch)
                            .FirstOrDefault();

                        // Nếu tìm thấy ứng viên tốt hơn, đổi target
                        if (candidate != null) targetDetail = candidate;
                    }

                    long currentOrderId = targetDetail.OrderID;

                    // 1. GIẢM SỐ LƯỢNG (Nếu đang > 0)
                    if (targetDetail.Quantity > 0)
                    {
                        // [NEW] Log if Item was Sent
                        if (targetDetail.ItemStatus == "Sent")
                        {
                            var log = new CancelledLog
                            {
                                TableID = db.Orders.Where(o => o.OrderID == currentOrderId).Select(o => o.TableID).FirstOrDefault(),
                                OrderID = targetDetail.OrderID,
                                DishName = targetDetail.Dish?.DishName ?? "Unknown",
                                Quantity = 1, // Decrease 1
                                Amount = targetDetail.UnitPrice,
                                DeletedBy = UserSession.AccName ?? "Admin",
                                CancelTime = DateTime.Now
                            };
                            db.CancelledLogs.Add(log);

                            targetDetail.ItemStatus = "Modified";
                        }

                        targetDetail.Quantity--;
                        targetDetail.TotalAmount = targetDetail.Quantity * targetDetail.UnitPrice * (1 - targetDetail.DiscountRate / 100);
                    }

                    // 2. LOGIC XÓA
                    bool isRemoved = false;

                    // Nếu món Mới (chưa in) về 0 -> XÓA
                    if (targetDetail.Quantity == 0 && targetDetail.PrintedQuantity == 0)
                    {
                        db.OrderDetails.Remove(targetDetail);
                        isRemoved = true;
                    }
                    // Nếu món Cũ (đã in) -> Giữ lại số 0 để báo hủy (Không xóa dòng này ngay)

                    db.SaveChanges();

                    // 3. QUAN TRỌNG: KIỂM TRA XEM ĐƠN HÀNG CÒN MÓN NÀO KHÔNG?
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
                                NotifyTableUpdated(order.TableID ?? _selectedTableId);
                                return;
                            }
                        }
                    }

                    RecalculateOrder(db, targetDetail.OrderID);
                    LoadOrderDetails(_selectedTableId);
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
                // [FIX] Load Dish for Log Name
                var detail = db.OrderDetails.Include(d => d.Dish).FirstOrDefault(d => d.OrderDetailID == detailId);
                if (detail == null) return;

                // [FIX] DETECT GROUP SIBLINGS & MERGE
                // Find other items in the same group (Same Order, Dish, Note, and Status Group)
                bool isTargetNew = detail.ItemStatus == "New";
                string targetNote = (detail.Note ?? "").Trim();

                var siblings = db.OrderDetails
                    .Where(d => d.OrderID == detail.OrderID
                             && d.DishID == detail.DishID
                             && d.OrderDetailID != detail.OrderDetailID)
                    .ToList(); // Client-side filtering for Note & Status to be safe

                var siblingsToMerge = siblings.Where(d =>
                    ((d.Note ?? "").Trim() == targetNote) &&
                    (isTargetNew ? (d.ItemStatus == "New") : (d.ItemStatus != "New"))
                ).ToList();

                // Merge logic
                if (siblingsToMerge.Any())
                {
                    foreach (var s in siblingsToMerge)
                    {
                        detail.Quantity += s.Quantity;
                        detail.PrintedQuantity += s.PrintedQuantity;
                        // If Sent/Modified, maybe keep max Batch?
                        if (s.KitchenBatch > detail.KitchenBatch) detail.KitchenBatch = s.KitchenBatch;

                        db.OrderDetails.Remove(s);
                    }
                    // Save merge first? No, we will modify detail further.
                }

                long currentOrderId = detail.OrderID;
                int oldQuantity = detail.Quantity; // This is now the "Group Total" before edit

                // NOW apply the new target quantity
                detail.Quantity = newQuantity;
                detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);

                if (newQuantity != oldQuantity && detail.ItemStatus != "New")
                {
                    detail.ItemStatus = "Modified";

                    // [NEW] Nếu giảm số lượng món đã gửi bếp -> Ghi log hủy
                    if (newQuantity < oldQuantity)
                    {
                        int cancelQty = oldQuantity - newQuantity;
                        var log = new CancelledLog
                        {
                            TableID = db.Orders.Where(o => o.OrderID == detail.OrderID).Select(o => o.TableID).FirstOrDefault(),
                            OrderID = detail.OrderID,
                            DishName = detail.Dish?.DishName ?? "Unknown",
                            Quantity = cancelQty,
                            Amount = cancelQty * detail.UnitPrice,
                            // Reason removed
                            DeletedBy = UserSession.AccName ?? "Admin",
                            CancelTime = DateTime.Now
                        };
                        db.CancelledLogs.Add(log);
                    }
                }

                bool isRemoved = false;

                // Nếu về 0: Xóa hoặc giữ lại để báo hủy
                // Note: If PrintedQuantity > 0, we keep it to print cancellation ticket
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
                            NotifyTableUpdated(order.TableID ?? _selectedTableId);
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
                        .Include(o => o.Account) // [FIX] Include Account to get Staff Name
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
                            DishID = item.DishID,      // [FIX] Gán DishID để không bị gộp sai
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
                        // [FIX] Truyền thêm tên người bấm (Sender)
                        string senderName = UserSession.AccName ?? "Admin";
                        Services.PrintService.PrintKitchen(order, itemsToPrint, nextBatch, senderName);
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
                // [MODIFIED] Pass MaxLimit = SubTotal
                var dialog = new DiscountWindow(currentVal, isPercentMode: isPercent, isEditItem: false, maxLimit: order.SubTotal);

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

        // --- REALTIME UPDATE COALESCING (ANTI-LAG) ---
        // Khi mobile spam update (tăng số lượng liên tục), SignalR sẽ bắn rất nhiều "TableUpdated".
        // Nếu mỗi event đều trigger load + update UI ngay, UI thread sẽ bị queue và trông như "đơ".
        // Các biến dưới đây giúp debounce + chạy tuần tự, luôn cập nhật trạng thái mới nhất.
        private int _pendingRealtimeTableId = -1;
        private int _realtimeRefreshRunning = 0;
        private CancellationTokenSource? _realtimeDebounceCts;

        private Task InvokeOnUiAsync(Func<Task> action)
        {
            if (Dispatcher.CheckAccess()) return action();
            return Dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        private void QueueRealtimeTableUpdate(int tableId)
        {
            // Keep only the latest tableId; LoadTables will refresh all statuses anyway.
            Interlocked.Exchange(ref _pendingRealtimeTableId, tableId);

            // Debounce bursts (spam + button hold)
            try { _realtimeDebounceCts?.Cancel(); } catch { }
            var cts = new CancellationTokenSource();
            _realtimeDebounceCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, cts.Token);
                    await ProcessRealtimeTableUpdatesAsync();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Realtime debounce error: {ex.Message}");
                }
            });
        }

        private async Task ProcessRealtimeTableUpdatesAsync()
        {
            // Ensure single refresh pipeline at a time
            if (Interlocked.CompareExchange(ref _realtimeRefreshRunning, 1, 0) != 0) return;

            try
            {
                while (true)
                {
                    int tableId = Interlocked.Exchange(ref _pendingRealtimeTableId, -1);
                    if (tableId <= 0) break;

                    // Always refresh table grid; refresh details only if it is the selected table.
                    int selected = _selectedTableId;
                    await InvokeOnUiAsync(async () =>
                    {
                        if (selected == tableId)
                        {
                            await LoadOrderDetailsAsync(tableId);
                        }
                        await LoadTablesAsync();
                    });

                    // If another event arrived during refresh, loop and pick latest.
                }
            }
            finally
            {
                Interlocked.Exchange(ref _realtimeRefreshRunning, 0);
                // If a new update arrived right after we released, kick again.
                if (Volatile.Read(ref _pendingRealtimeTableId) > 0)
                {
                    QueueRealtimeTableUpdate(Volatile.Read(ref _pendingRealtimeTableId));
                }
            }
        }

        // --- 6. SIGNALR & CHECKOUT & IN BẾP (MAIN) ---
        private HubConnection BuildRealtimeConnection(bool forceWebSockets)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/posHub", options =>
                {
                    if (forceWebSockets)
                    {
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                        options.SkipNegotiation = true;
                    }
                    else
                    {
                        // Fallback to normal negotiation (allows SSE/LongPolling if WS is blocked)
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                                             Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents |
                                             Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                        options.SkipNegotiation = false;
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            connection.On<int>("TableRequestPayment", (tableId) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _tablesRequestingPayment.Add(tableId);
                    // Thay vì LoadTables sync (dễ lag), coalesce vào pipeline async
                    QueueRealtimeTableUpdate(tableId);
                    ShowToast($"🔔 Bàn {tableId} yêu cầu thanh toán!", 3000);
                });
            });

            connection.On<int>("TableUpdated", (id) =>
            {
                QueueRealtimeTableUpdate(id);
            });

            // [NEW] Listen for Order Notifications
            connection.On<string>("ReceiveOrderNotification", (msg) =>
            {
                AppendActivityLog(msg);
            });

            return connection;
        }

        private async void SetupRealtime()
        {
            _connection = BuildRealtimeConnection(forceWebSockets: true);

            try
            {
                await _connection.StartAsync();
            }
            catch (Exception ex)
            {
                // If WS is blocked on some networks/devices, fall back to negotiated transport.
                Console.WriteLine($"SignalR WS start failed, falling back: {ex.Message}");
                try
                {
                    await _connection.DisposeAsync();
                }
                catch { }

                _connection = BuildRealtimeConnection(forceWebSockets: false);
                try { await _connection.StartAsync(); } catch { }
            }
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
                        // [MODIFIED] Removed confirmation dialog for 0đ bill
                        // if (MessageBox.Show("Thanh toán 0đ để đóng bàn?", "Xác nhận", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    }
                }
            }

            if (orderId == 0) return;

            // Mở cửa sổ thanh toán
            var payWindow = new PaymentWindow(orderId);
            payWindow.ShowDialog();

            if (payWindow.IsPaidSuccess)
            {
                // --- SỬA: Kiểm tra ToggleButton (tglPrintBill) VÀ lựa chọn từ PaymentWindow ---
                if (tglPrintBill.IsChecked == true && payWindow.ShouldPrint)
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
            else if (payWindow.IsProvisionalSuccess)
            {
                LoadTables(); // Update icon
                ShowToast("🧾 Đã in tạm tính thành công!");
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
            btnCancelMove.Visibility = Visibility.Visible; // [NEW] Show Cancel button

            LoadTables(); // [NEW] Refresh to gray out source table
            _tableTimeTimer.Stop();
        }

        private void BtnCancelMove_Click(object sender, RoutedEventArgs e)
        {
            _isWaitingForMoveTargetTable = false;
            btnCancelMove.Visibility = Visibility.Collapsed;
            HideToast();

            // Return to Menu / Order Screen for the current table
            SelectAndLoadTable(_selectedTableId);
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
                        btnCancelMove.Visibility = Visibility.Collapsed; // [NEW] Hide Cancel button

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

            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order == null || !order.OrderDetails.Any())
                {
                    ShowToast("❌ Không có món để tách!");
                    return;
                }

                var items = order.OrderDetails
                    .Where(d => d.Quantity > 0)
                    .GroupBy(d => new { d.DishID, Note = (d.Note ?? "").Trim() })
                    .Select(g => new ReprintItemViewModel
                    {
                        OrderDetails = g.ToList(),
                        DisplayText = g.First().Dish?.DishName ?? "Unknown",
                        IsSelected = false,
                        SelectedQuantity = 0 // Initially 0 for Split
                    })
                    .ToList();

                lstSplitItems.ItemsSource = items;
                btnSelectAllSplit.Content = "Chọn tất cả";
                pnlSplitPopup.Visibility = Visibility.Visible;
            }
        }


        private void BtnReprint_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order == null || !order.OrderDetails.Any())
                {
                    ShowToast("❌ Không có món nào để in lại!");
                    return;
                }

                // Load items to popup
                var items = order.OrderDetails
                    .Where(d => d.Quantity > 0)
                    .GroupBy(d => new { d.DishID, Note = (d.Note ?? "").Trim() })
                    .Select(g => new ReprintItemViewModel
                    {
                        OrderDetails = g.ToList(),
                        DisplayText = g.First().Dish?.DishName ?? "Unknown",
                        IsSelected = false,
                        SelectedQuantity = g.Sum(d => d.Quantity) // Default to Max
                    })
                    .ToList();

                lstReprintItems.ItemsSource = items;
                btnSelectAllReprint.Content = "Chọn tất cả";
                pnlReprintPopup.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseReprintPopup_Click(object sender, RoutedEventArgs e)
        {
            pnlReprintPopup.Visibility = Visibility.Collapsed;
        }

        private void PnlReprintPopup_OutsideClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            pnlReprintPopup.Visibility = Visibility.Collapsed;
        }

        private void BtnSelectAllReprint_Click(object sender, RoutedEventArgs e)
        {
            if (lstReprintItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                // Check if currently all selected
                bool allSelected = items.All(i => i.IsSelected);

                // Toggle: If all selected -> Deselect all. Otherwise -> Select all.
                bool newValue = !allSelected;

                foreach (var item in items) item.IsSelected = newValue;
                lstReprintItems.Items.Refresh();

                // Update button text if needed, but for now purely logic
                if (sender is Button btn)
                {
                    btn.Content = newValue ? "Bỏ chọn tất cả" : "Chọn tất cả";
                }
            }
        }

        private void BtnConfirmReprint_Click(object sender, RoutedEventArgs e)
        {
            if (lstReprintItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                var selected = items.Where(i => i.IsSelected).SelectMany(i => i.OrderDetails).ToList();
                if (!selected.Any())
                {
                    ShowToast("⚠️ Vui lòng chọn ít nhất 1 món!");
                    return;
                }

                using (var db = new AppDbContext())
                {
                    var order = db.Orders
                       .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                       .Include(o => o.Table)
                       .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                    if (order == null) return;

                    // Filter selected items from THIS order context
                    // Logic: For each selected group, pick TOP N items
                    var itemsToPrint = new List<OrderDetail>();

                    foreach (var vm in items.Where(x => x.IsSelected && x.SelectedQuantity > 0))
                    {
                        // Get IDs of the items we want to print
                        var idsToPrint = vm.OrderDetails.Take(vm.SelectedQuantity).Select(d => d.OrderDetailID).ToList();

                        // Fetch the actual entities from the current 'order' context (which has Category loaded)
                        var details = order.OrderDetails.Where(d => idsToPrint.Contains(d.OrderDetailID)).ToList();
                        itemsToPrint.AddRange(details);
                    }

                    if (itemsToPrint.Any())
                    {
                        int maxBatch = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 1;
                        string senderName = UserSession.AccName ?? "Admin";
                        Services.PrintService.PrintKitchen(order, itemsToPrint, maxBatch, senderName + " (IN LẠI)");

                        ShowToast($"✅ Đã gửi lệnh in lại {itemsToPrint.Count} món!");
                        pnlReprintPopup.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void BtnIncreaseReprint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ReprintItemViewModel vm)
            {
                if (vm.SelectedQuantity < vm.TotalQuantity)
                {
                    vm.SelectedQuantity++;
                    if (!vm.IsSelected) vm.IsSelected = true;
                }
            }
        }

        private void BtnDecreaseReprint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ReprintItemViewModel vm)
            {
                if (vm.SelectedQuantity > 1)
                {
                    vm.SelectedQuantity--;
                }
            }
        }

        private void ReprintItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ReprintItemViewModel vm)
            {
                // Toggle selection
                vm.IsSelected = !vm.IsSelected;

                // Note: IsSelected setter in ViewModel handles the default quantity logic (defaults to max if 0).
            }
        }

        private void BtnCloseSplitPopup_Click(object sender, RoutedEventArgs e)
        {
            pnlSplitPopup.Visibility = Visibility.Collapsed;
        }

        private void PnlSplitPopup_OutsideClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            pnlSplitPopup.Visibility = Visibility.Collapsed;
        }

        private void BtnSelectAllSplit_Click(object sender, RoutedEventArgs e)
        {
            if (lstSplitItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                bool allSelected = items.All(i => i.IsSelected);
                bool newValue = !allSelected;

                foreach (var item in items) item.IsSelected = newValue;
                lstSplitItems.Items.Refresh();

                if (sender is Button btn)
                {
                    btn.Content = newValue ? "Bỏ chọn tất cả" : "Chọn tất cả";
                }
            }
        }

        private void BtnConfirmSplit_Click(object sender, RoutedEventArgs e)
        {
            if (lstSplitItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                var selectedItems = items.Where(x => x.IsSelected && x.SelectedQuantity > 0).ToList();
                if (!selectedItems.Any())
                {
                    ShowToast("❌ Vui lòng chọn món để tách!", 2000);
                    return;
                }

                // Prepare transfer dictionary
                var itemsToTransfer = new Dictionary<long, int>();

                foreach (var vm in selectedItems)
                {
                    int quantityRemainingToSplit = vm.SelectedQuantity;

                    // Iterate through the underlying OrderDetails in this group
                    foreach (var detail in vm.OrderDetails)
                    {
                        if (quantityRemainingToSplit <= 0) break;

                        int take = Math.Min(detail.Quantity, quantityRemainingToSplit);
                        itemsToTransfer[detail.OrderDetailID] = take;
                        quantityRemainingToSplit -= take;
                    }
                }

                if (itemsToTransfer.Count == 0) return;

                // Set waiting mode and show persistent popup to select destination table
                _isWaitingForTargetTable = true;
                _pendingSplitItems = itemsToTransfer;

                ShowToastPersistent("📍 Chọn bàn đích để tách...");

                // Switch back to table list view
                pnlSplitPopup.Visibility = Visibility.Collapsed;
                pnlMenu.Visibility = Visibility.Collapsed;
                pnlTableList.Visibility = Visibility.Visible;
                btnCancelSplit.Visibility = Visibility.Visible; // [NEW] Show Cancel button

                LoadTables(); // [NEW] Refresh to gray out source table
                _tableTimeTimer.Stop();
            }
        }

        private void BtnCancelSplit_Click(object sender, RoutedEventArgs e)
        {
            _isWaitingForTargetTable = false;
            _pendingSplitItems.Clear();
            btnCancelSplit.Visibility = Visibility.Collapsed;
            HideToast();

            // Return to Menu / Order Screen for the current table
            SelectAndLoadTable(_selectedTableId);
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

                        btnDiscountBill.Visibility = Visibility.Visible;
                        btnCancelSplit.Visibility = Visibility.Collapsed; // [NEW] Hide Cancel button

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
        private void BtnTimeKeeping_Click(object sender, RoutedEventArgs e)
        {
            // Tạo cửa sổ từ file riêng
            var tkWindow = new TimeKeepingWindow();

            // Gán Owner là MainWindow để nó hiện chính giữa cửa sổ chính
            tkWindow.Owner = this;

            // QUAN TRỌNG: Dùng ShowDialog() để biến nó thành MODAL
            // (Người dùng không thể bấm vào MainWindow khi cửa sổ này đang mở)
            tkWindow.ShowDialog();

            // Sau khi đóng modal, focus lại màn hình chính để bán hàng tiếp
            this.Focus();
        }
        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn đăng xuất?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Xóa session
                UserSession.AccID = 0;
                UserSession.AccName = "";
                UserSession.AccRole = "";

                // Mở lại màn hình Login
                LoginWindow login = new LoginWindow();
                login.Show();

                // Đóng màn hình hiện tại
                this.Close();
            }
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            _selectedCategoryId = null;
            if (btnFilterAll != null)
                btnFilterAll.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007BFF"));
            LoadTables();
        }

        private void BtnFilterCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int catId)
            {
                _selectedCategoryId = catId;
                if (btnFilterAll != null)
                    btnFilterAll.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C757D"));
                LoadTables();
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
        public int KitchenBatch { get; set; }
        // Ghi chú (Cho phép sửa đổi)
        public string Note { get; set; } = "";

        // Các thuộc tính hiển thị
        public bool HasDiscount => DiscountRate > 0;
        public string DiscountDisplay => HasDiscount ? $"Giảm {DiscountRate:0.#}%" : "";
        public string BatchDisplay { get; set; } = "";
        public string StatusDisplay { get; set; } = "";
        public string RowColor { get; set; } = "White";
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

    public class NotEmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                return !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
} // End of namespace
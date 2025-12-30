using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using System.Threading.Tasks;

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

            LoadTables();
            LoadMenu();
            SetupRealtime();
        }

        // --- 1. CHUYỂN ĐỔI VIEW ---
        private void lstTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTables.SelectedItem is TableViewModel selected)
            {
                _selectedTableId = selected.TableID;
                lblSelectedTable.Text = selected.TableName;

                pnlTableList.Visibility = Visibility.Collapsed;
                pnlMenu.Visibility = Visibility.Visible;

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
            LoadTables();
            lstTables.SelectedItem = null;
        }

        // --- 2. LOAD DATA ---
        private void LoadTables()
        {
            using (var db = new AppDbContext())
            {
                var tables = db.Tables.Include(t => t.Orders).ThenInclude(o => o.OrderDetails).ToList();
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
                    // Update order time - use FirstSentTime if available (order sent to kitchen), otherwise null
                    if (order.FirstSentTime.HasValue)
                    {
                        _currentOrderTime = order.FirstSentTime;
                        if (!_tableTimeTimer.IsEnabled)
                        {
                            _tableTimeTimer.Start();
                        }
                    }
                    else
                    {
                        _currentOrderTime = null;
                        _tableTimeTimer.Stop();
                        lblTableTime.Text = "";
                    }

                    // SỬA ĐOẠN NÀY: Dùng OrderDetailViewModel thay vì new { ... }
                    var viewModels = order.OrderDetails.OrderBy(d => d.OrderDetailID).Select(d => new OrderDetailViewModel
                    {
                        OrderDetailID = d.OrderDetailID,
                        DishName = d.Dish != null ? d.Dish.DishName : "Unknown",
                        Quantity = d.Quantity,
                        TotalAmount = d.TotalAmount,
                        DiscountRate = d.DiscountRate,
                        ItemStatus = d.ItemStatus,
                        Note = d.Note ?? "", // Lấy ghi chú, nếu null thì thành rỗng

                        BatchDisplay = d.PrintedQuantity == 0 ? "⏳" : (d.KitchenBatch > 0 ? $"Đợt {d.KitchenBatch}" : "---"),
                        StatusDisplay = d.Quantity == 0 ? "❌ CHỜ HỦY" : (d.Quantity != d.PrintedQuantity ? "Cần Gửi" : "OK"),
                        RowColor = d.Quantity == 0 ? "#FFCCCC" : (d.Quantity != d.PrintedQuantity ? "#FFF3CD" : "White")
                    }).ToList();

                    lstOrderDetails.ItemsSource = viewModels; // Gán list mới

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

                    // Nút Checkout: Chỉ sáng khi có ít nhất 1 món SL > 0
                    bool hasValidItems = order.OrderDetails.Any(d => d.Quantity > 0);
                    btnCheckout.IsEnabled = hasValidItems;

                    // Nút Gửi bếp
                    btnSendKitchen.IsEnabled = hasChanges;
                    btnSendKitchen.Content = hasChanges ? "🔔 GỬI BẾP (Cập nhật)" : "👨‍🍳 GỬI BẾP";
                    btnSendKitchen.Background = hasChanges ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FD7E14")
                                                           : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#6C757D");
                }
                else
                {
                    // Reset khi không có đơn
                    lstOrderDetails.ItemsSource = null;
                    lblTotal.Text = "0đ";
                    lblSubTotal.Text = "0đ";
                    pnlDiscount.Visibility = Visibility.Collapsed;
                    btnCheckout.IsEnabled = false;
                    btnSendKitchen.IsEnabled = false;
                    btnSendKitchen.Content = "👨‍🍳 GỬI BẾP";

                    // Reset table time
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
                    Price = d.Price,
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
                var order = db.Orders.Include(o => o.OrderDetails)
                                     .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order == null)
                {
                    int? currentAccId = UserSession.AccID > 0 ? UserSession.AccID : (db.Accounts.FirstOrDefault()?.AccID);
                    order = new Order { TableID = tableId, AccID = currentAccId, OrderDetails = new List<OrderDetail>() };
                    db.Orders.Add(order);
                    var table = db.Tables.Find(tableId);
                    if (table != null) table.TableStatus = "Occupied";
                }

                // Update order time khi có order (mới tạo hoặc đã có)
                if (_selectedTableId == tableId)
                {
                    _currentOrderTime = order.OrderTime;
                }

                // === SỬA LẠI ĐOẠN NÀY ===
                // Chỉ tìm dòng nào CÙNG MÓN và KHÔNG CÓ GHI CHÚ
                var existingDetail = order.OrderDetails
                    .FirstOrDefault(d => d.DishID == dishId && (string.IsNullOrEmpty(d.Note)));

                var dishInfo = _allDishes.FirstOrDefault(d => d.DishID == dishId);
                if (dishInfo == null) return;

                if (existingDetail != null)
                {
                    // Tìm thấy món y hệt (không note) -> Cộng dồn
                    existingDetail.Quantity++;
                    existingDetail.TotalAmount = existingDetail.Quantity * existingDetail.UnitPrice * (1 - existingDetail.DiscountRate / 100);
                    if (existingDetail.ItemStatus == "Sent") existingDetail.ItemStatus = "Modified";
                }
                else
                {
                    // Không tìm thấy (tức là các dòng món này hiện tại đều ĐANG CÓ ghi chú) -> TẠO DÒNG MỚI
                    order.OrderDetails.Add(new OrderDetail
                    {
                        DishID = dishId,
                        Quantity = 1,
                        UnitPrice = dishInfo.Price,
                        ItemStatus = "New",
                        PrintedQuantity = 0,
                        TotalAmount = dishInfo.Price,
                        Note = "" // Món mới mặc định Note rỗng
                    });
                }

                db.SaveChanges();
                RecalculateOrder(db, order.OrderID);
                if (_selectedTableId == tableId) LoadOrderDetails(tableId);
                ShowToast($"Đã chọn: {dishInfo.DishName}");
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
                }
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

        private async void ShowToast(string message)
        {
            lblToastMessage.Text = message;
            bdToast.Visibility = Visibility.Visible;
            await Task.Delay(1500);
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

        private void TableTimeTimer_Tick(object sender, EventArgs e)
        {
            if (_currentOrderTime.HasValue)
            {
                var elapsed = DateTime.Now - _currentOrderTime.Value;
                string timeStr = "";

                if (elapsed.TotalMinutes < 1)
                {
                    timeStr = $"{(int)elapsed.TotalSeconds}s";
                }
                else if (elapsed.TotalHours < 1)
                {
                    timeStr = $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
                }
                else
                {
                    timeStr = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
                }

                lblTableTime.Text = timeStr;
            }
        }

    }
    // Class dùng để hiển thị lên DataGrid và hỗ trợ sửa Ghi chú
    public class OrderDetailViewModel
    {
        public long OrderDetailID { get; set; }
        public string DishName { get; set; } = "";
        public int Quantity { get; set; }
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
    }
}
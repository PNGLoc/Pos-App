using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        public MainWindow()
        {
            InitializeComponent();
            if (UserSession.IsLoggedIn) lblStaffName.Text = UserSession.AccName;
            if (UserSession.IsLoggedIn && UserSession.AccRole == "Admin") btnBackToAdmin.Visibility = Visibility.Visible;

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

                LoadOrderDetails(selected.TableID);
            }
        }

        private void BtnBackToTables_Click(object sender, RoutedEventArgs e)
        {
            _selectedTableId = 0;
            lblSelectedTable.Text = "Chưa chọn bàn";
            lstOrderDetails.ItemsSource = null;

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
                var tables = db.Tables.ToList();
                lstTables.ItemsSource = tables.Select(t => new TableViewModel
                {
                    TableID = t.TableID,
                    TableName = t.TableName,
                    TableStatus = t.TableStatus
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
                    lstOrderDetails.ItemsSource = order.OrderDetails.OrderBy(d => d.OrderDetailID).Select(d => new
                    {
                        d.OrderDetailID,
                        d.Dish!.DishName,
                        d.Quantity,
                        d.TotalAmount,
                        d.DiscountRate,
                        HasDiscount = d.DiscountRate > 0,
                        DiscountDisplay = $"Giảm {d.DiscountRate:0.#}%",
                        d.ItemStatus,

                        // HIỂN THỊ ĐỢT
                        BatchDisplay = d.PrintedQuantity == 0 ? "⏳" : (d.KitchenBatch > 0 ? $"Đợt {d.KitchenBatch}" : "---"),

                        // LOGIC HIỂN THỊ TRẠNG THÁI MỚI
                        // Nếu SL = 0 -> Hiện chữ "CHỜ HỦY" đỏ rực
                        StatusDisplay = d.Quantity == 0 ? "❌ CHỜ HỦY" :
                                       (d.Quantity != d.PrintedQuantity ? "Cần Gửi" : "OK"),

                        // Tô màu nền: Đỏ nhạt nếu chờ hủy, Vàng nhạt nếu có sửa đổi
                        RowColor = d.Quantity == 0 ? "#FFCCCC" :
                                  (d.Quantity != d.PrintedQuantity ? "#FFF3CD" : "White")
                    }).ToList();

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
            if (lstCategories.SelectedItem is CategoryViewModel selected)
            {
                var filtered = selected.CategoryID == 0 ? _dishViewModels : _dishViewModels.Where(d => d.CategoryID == selected.CategoryID).ToList();
                lstDishes.ItemsSource = filtered;
            }
        }
        private void lstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDishListDisplay();

        // --- 3. THAO TÁC NHANH TRÊN MÓN ĂN ---

        // A. Nhấn vào món -> Thêm ngay
        private void Dish_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is int dishId)
            {
                AddDishToOrder(_selectedTableId, dishId);
            }
        }

        private void AddDishToOrder(int tableId, int dishId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders.Include(o => o.OrderDetails).FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order == null)
                {
                    // Tạo Order mới nếu chưa có
                    int? currentAccId = UserSession.AccID > 0 ? UserSession.AccID : (db.Accounts.FirstOrDefault()?.AccID);
                    order = new Order { TableID = tableId, AccID = currentAccId, OrderDetails = new List<OrderDetail>() };
                    db.Orders.Add(order);
                    var table = db.Tables.Find(tableId);
                    if (table != null) table.TableStatus = "Occupied";
                }

                var existingDetail = order.OrderDetails.FirstOrDefault(d => d.DishID == dishId);
                var dishInfo = _allDishes.FirstOrDefault(d => d.DishID == dishId);
                if (dishInfo == null) return;

                if (existingDetail != null)
                {
                    // Chỉ tăng số lượng, KHÔNG IN GÌ CẢ
                    existingDetail.Quantity++;
                    existingDetail.TotalAmount = existingDetail.Quantity * existingDetail.UnitPrice * (1 - existingDetail.DiscountRate / 100);

                    // Nếu món đã từng gửi bếp, đổi màu trạng thái để biết đang có thay đổi chờ gửi
                    if (existingDetail.ItemStatus == "Sent") existingDetail.ItemStatus = "Modified";
                }
                else
                {
                    // Thêm món mới
                    order.OrderDetails.Add(new OrderDetail
                    {
                        DishID = dishId,
                        Quantity = 1,
                        UnitPrice = dishInfo.Price,
                        ItemStatus = "New",
                        PrintedQuantity = 0, // Chưa in cái nào
                        TotalAmount = dishInfo.Price
                    });
                }

                db.SaveChanges();
                RecalculateOrder(db, order.OrderID);
                if (_selectedTableId == tableId) LoadOrderDetails(tableId);

                // Chỉ hiện thông báo nhỏ
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

                    var changedItems = order.OrderDetails.Where(d => d.Quantity != d.PrintedQuantity).ToList();
                    if (!changedItems.Any()) return;

                    // 1. TÍNH BATCH & GOM NHÓM IN (Giữ nguyên logic cũ)
                    int currentMaxBatch = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 0;
                    int nextBatch = currentMaxBatch + 1;

                    var printerGroups = changedItems
                        .Where(d => d.Dish?.Category?.PrinterID != null)
                        .GroupBy(d => d.Dish.Category.PrinterID)
                        .ToList();

                    foreach (var group in printerGroups)
                    {
                        if (group.Key == null) continue;
                        var printer = db.Printers.Find(group.Key.Value);
                        if (printer == null || !printer.IsActive) continue;

                        var printData = group.Select(item => new
                        {
                            DishName = item.Dish.DishName,
                            Diff = item.Quantity - item.PrintedQuantity,
                            Note = item.Note
                        }).ToList();

                        Services.PrintService.PrintKitchenUpdates(printer, order.Table.TableName, nextBatch, printData);
                    }

                    // 2. CẬP NHẬT DATABASE
                    foreach (var item in changedItems)
                    {
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
                    db.SaveChanges();

                    // 3. --- LOGIC MỚI: KIỂM TRA ĐƠN RỖNG ---
                    // Sau khi xóa các món SL=0, kiểm tra xem đơn hàng còn món nào không?
                    bool isOrderEmpty = !db.OrderDetails.Any(d => d.OrderID == order.OrderID);

                    if (isOrderEmpty)
                    {
                        // Nếu đơn trống -> Xóa luôn Order & Trả bàn
                        db.Orders.Remove(order);
                        var table = db.Tables.Find(order.TableID);
                        if (table != null) table.TableStatus = "Empty";
                        db.SaveChanges();

                        Dispatcher.Invoke(() =>
                        {
                            LoadTables();           // Reload sơ đồ bàn (về màu xanh)
                            LoadOrderDetails(_selectedTableId); // Clear màn hình order
                            ShowToast("✅ Đã hủy món & Trả bàn trống");
                        });
                    }
                    else
                    {
                        // Nếu vẫn còn món -> Chỉ reload Order
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

                LoadTables();
                LoadOrderDetails(_selectedTableId);
            }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWin = new HistoryWindow();
            historyWin.ShowDialog();
        }

    }
}
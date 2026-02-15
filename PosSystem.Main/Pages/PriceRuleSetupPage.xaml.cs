using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Server.Hubs;

namespace PosSystem.Main.Pages
{
    public partial class PriceRuleSetupPage : UserControl
    {
        private sealed class CategoryFilterOption
        {
            public int CategoryID { get; set; }
            public string CategoryName { get; set; } = "";
        }

        private string? _editingRuleType = null;
        private List<RuleDetailViewModel>? _allRuleDetails = null; // [NEW] Cache for filtering
        private bool _isLoadingData = true;

        private DispatcherTimer? _toastTimer;

        public PriceRuleSetupPage()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            _isLoadingData = true;

            using (var db = new AppDbContext())
            {
                // 1. Load Rule Types
                var ruleTypes = db.PriceRuleTypes
                    .OrderByDescending(r => r.CreatedDate)
                    .ToList();

                var ruleTypeViewModels = new List<RuleTypeViewModel>();
                foreach (var rt in ruleTypes)
                {
                    var productCount = db.DishPriceRules.Count(p => p.RuleType == rt.RuleType);
                    ruleTypeViewModels.Add(new RuleTypeViewModel
                    {
                        RuleType = rt.RuleType,
                        ProductCount = productCount,
                        IsActive = rt.IsActive
                    });
                }

                dgRuleTypes.ItemsSource = ruleTypeViewModels;

                // 2. Load Combo Active Rule
                var availableRules = ruleTypes.Select(r => r.RuleType).ToList();
                availableRules.Insert(0, "(Giá gốc)");
                cboActiveRule.ItemsSource = availableRules;

                // 3. Set Active Rule in Combo
                var activeSetting = db.GlobalSettings.FirstOrDefault(g => g.Key == "activePriceRule");
                if (activeSetting != null && !string.IsNullOrEmpty(activeSetting.Value))
                    cboActiveRule.SelectedItem = activeSetting.Value;
                else
                    cboActiveRule.SelectedIndex = 0;
            }

            _isLoadingData = false;
        }

        private void CboActiveRule_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingData) return;
            if (cboActiveRule.SelectedIndex < 0) return;

            // [MODIFIED] Check if any table is occupied
            using (var db = new AppDbContext())
            {
                if (db.Tables.Any(t => t.TableStatus == "Occupied")) // or != "Empty"
                {
                    MessageBox.Show("Không thể thay đổi bảng giá khi còn bàn đang phục vụ!\nVui lòng thanh toán tất cả các bàn trước.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Revert selection logic
                    _isLoadingData = true;
                    string currentRule = db.GlobalSettings.FirstOrDefault(g => g.Key == "activePriceRule")?.Value ?? "";
                    if (string.IsNullOrEmpty(currentRule)) cboActiveRule.SelectedIndex = 0; // (Giá gốc)
                    else cboActiveRule.SelectedItem = currentRule;
                    _isLoadingData = false;

                    return;
                }
            }

            string selectedRule = cboActiveRule.SelectedItem as string ?? "";

            if (selectedRule == "(Giá gốc)")
                PriceService.SetActivePriceRule("");
            else
                PriceService.SetActivePriceRule(selectedRule);

            UpdateSentPricesForPendingOrders();
            NotifyMenuUpdated();
            ShowNotification($"Đã áp dụng: {selectedRule}");
        }

        private void UpdateSentPricesForPendingOrders()
        {
            using (var db = new AppDbContext())
            {
                var orders = db.Orders
                    .Include(o => o.OrderDetails)
                    .Where(o => o.OrderStatus == "Pending" && o.OrderDetails.Any(d => d.ItemStatus == "Sent" || d.ItemStatus == "New"))
                    .ToList();

                if (orders.Count == 0) return;

                var updatedTables = new HashSet<int>();

                foreach (var order in orders)
                {
                    var hasChanges = false;
                    foreach (var detail in order.OrderDetails.Where(d => d.ItemStatus == "Sent" || d.ItemStatus == "New"))
                    {
                        var newPrice = PriceService.GetCurrentPrice(detail.DishID, db);
                        if (detail.UnitPrice != newPrice)
                        {
                            detail.UnitPrice = newPrice;
                            detail.TotalAmount = detail.Quantity * newPrice;
                            hasChanges = true;
                        }
                    }

                    if (hasChanges)
                    {
                        var subtotal = order.OrderDetails.Sum(d => d.TotalAmount);
                        order.SubTotal = subtotal;
                        if (order.DiscountPercent > 0)
                        {
                            order.FinalAmount = subtotal - (subtotal * (order.DiscountPercent / 100m));
                        }
                        else if (order.DiscountAmount > 0)
                        {
                            order.FinalAmount = subtotal - order.DiscountAmount;
                        }
                        else
                        {
                            order.FinalAmount = subtotal;
                        }

                        if (order.TableID.HasValue)
                        {
                            updatedTables.Add(order.TableID.Value);
                        }
                    }
                }

                db.SaveChanges();

                NotifyTablesUpdated(updatedTables);
            }
        }

        private async void NotifyTablesUpdated(IEnumerable<int> tableIds)
        {
            try
            {
                if (App.WebHost == null) return;
                var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                if (hubContext == null) return;

                foreach (var tableId in tableIds)
                {
                    await hubContext.Clients.All.SendAsync("TableUpdated", tableId);
                }
            }
            catch
            {
                // Best-effort notification only.
            }
        }

        private async void NotifyMenuUpdated()
        {
            try
            {
                if (App.WebHost == null) return;
                var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                if (hubContext == null) return;
                await hubContext.Clients.All.SendAsync("MenuUpdated");
            }
            catch
            {
                // Best-effort notification only.
            }
        }

        private void dgRuleTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingData) return;

            if (dgRuleTypes.SelectedItem is RuleTypeViewModel vm)
            {
                if (string.IsNullOrWhiteSpace(vm.RuleType)) return;
                if (vm.RuleType == _editingRuleType) return;
                LoadRuleDetails(vm.RuleType);
            }
        }

        // --- ADD MODAL HANDLERS ---

        private void BtnShowAddForm_Click(object sender, RoutedEventArgs e)
        {
            txtRuleType.Clear();
            chkRuleTypeActive.IsChecked = true;
            modalAddRule.Visibility = Visibility.Visible;
            txtRuleType.Focus();
        }

        private void BtnCancelAddForm_Click(object sender, RoutedEventArgs e)
        {
            modalAddRule.Visibility = Visibility.Collapsed;
        }

        private void BtnAddRuleType_Click(object sender, RoutedEventArgs e)
        {
            string ruleName = txtRuleType.Text.Trim();
            if (string.IsNullOrEmpty(ruleName))
            {
                ShowNotification("Vui lòng nhập tên loại giá.");
                txtRuleType.Focus();
                return;
            }

            using (var db = new AppDbContext())
            {
                // [MODIFIED] Case insensitive check
                if (db.PriceRuleTypes.ToList().Any(p => p.RuleType.ToLower() == ruleName.ToLower()))
                {
                    ShowNotification("Loại giá này đã tồn tại.");
                    txtRuleType.Focus();
                    txtRuleType.SelectAll();
                    return;
                }

                var newRuleType = new PriceRuleType
                {
                    RuleType = ruleName,
                    IsActive = chkRuleTypeActive.IsChecked ?? true,
                    CreatedDate = DateTime.Now
                };
                db.PriceRuleTypes.Add(newRuleType);
                db.SaveChanges();

                ShowNotification("Thêm loại giá thành công!");
                modalAddRule.Visibility = Visibility.Collapsed;
                LoadData();
            }
        }

        private void BtnDeleteRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RuleTypeViewModel vm)
            {
                if (MessageBox.Show($"Xóa loại giá '{vm.RuleType}' và tất cả giá đã thiết lập?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var rules = db.DishPriceRules.Where(p => p.RuleType == vm.RuleType).ToList();
                        db.DishPriceRules.RemoveRange(rules);

                        var ruleType = db.PriceRuleTypes.FirstOrDefault(r => r.RuleType == vm.RuleType);
                        if (ruleType != null)
                            db.PriceRuleTypes.Remove(ruleType);

                        db.SaveChanges();
                        LoadData();
                        ShowNotification("Đã xóa thành công!");
                    }
                }
            }
        }

        // --- DETAIL PANEL HANDLERS ---

        private void BtnEditRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RuleTypeViewModel vm)
            {
                LoadRuleDetails(vm.RuleType);
            }
        }

        private void LoadRuleDetails(string ruleType)
        {
            using (var db = new AppDbContext())
            {
                lblSelectedRuleType.Text = ruleType;
                _editingRuleType = ruleType;

                // [MODIFIED] Include Category
                var dishes = db.Dishes.Include(d => d.Category).Where(d => d.DishStatus == "Active").ToList();
                var rules = db.DishPriceRules.Where(p => p.RuleType == ruleType).ToList();

                var detailsViewModels = dishes.Select(d => new RuleDetailViewModel
                {
                    DishID = d.DishID,
                    DishName = d.DishName,
                    CategoryName = d.Category?.CategoryName ?? "---", // [NEW]
                    Unit = d.Unit,
                    CategoryID = d.CategoryID, // [NEW]
                    BasePrice = d.Price, // Giá gốc
                    // [MODIFIED] Init NewPrice and OriginalPrice
                    OriginalPrice = rules.FirstOrDefault(p => p.DishID == d.DishID)?.Price ?? d.Price,
                    NewPrice = rules.FirstOrDefault(p => p.DishID == d.DishID)?.Price ?? d.Price
                }).ToList();

                _allRuleDetails = detailsViewModels; // [NEW] Store full list

                // Load Categories for Filter
                var cats = db.Categories
                    .OrderBy(c => c.OrderIndex)
                    .Select(c => new CategoryFilterOption { CategoryID = c.CategoryID, CategoryName = c.CategoryName })
                    .ToList();
                var catList = new List<CategoryFilterOption> { new CategoryFilterOption { CategoryID = 0, CategoryName = "Tất cả danh mục" } };
                catList.AddRange(cats);
                cboDetailCategory.ItemsSource = catList;
                cboDetailCategory.SelectedValuePath = "CategoryID";
                cboDetailCategory.DisplayMemberPath = "CategoryName";
                cboDetailCategory.SelectedIndex = 0;
                txtDetailSearch.Clear();

                UpdateDetailFilter(); // Initial Load

                // Split view: show details panel + hide placeholder
                if (DetailPanel != null) DetailPanel.Visibility = Visibility.Visible;
                if (pnlDetailPlaceholder != null) pnlDetailPlaceholder.Visibility = Visibility.Collapsed;

                UpdateDetailStats();
            }
        }

        private void BtnSaveRuleDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_editingRuleType == null) return;

            var allDetails = _allRuleDetails;
            if (allDetails == null || allDetails.Count == 0) return;

            using (var db = new AppDbContext())
            {
                // Save should persist ALL items of this rule type, not only the currently filtered view.
                foreach (var detail in allDetails)
                {
                    // Chỉ lưu nếu giá KHÁC giá gốc (optional optimisation)

                    var existingRule = db.DishPriceRules
                        .FirstOrDefault(p => p.DishID == detail.DishID && p.RuleType == _editingRuleType);

                    if (existingRule != null)
                    {
                        existingRule.Price = detail.NewPrice;
                    }
                    else
                    {
                        // Nếu chưa có rule, tạo mới
                        var newRule = new DishPriceRule
                        {
                            DishID = detail.DishID,
                            RuleName = $"{_editingRuleType} - {detail.DishName}",
                            RuleType = _editingRuleType,
                            Price = detail.NewPrice,
                            IsActive = true,
                            CreatedDate = DateTime.Now
                        };
                        db.DishPriceRules.Add(newRule);
                    }
                }

                db.SaveChanges();

                // Update OriginalPrice to clear highlights
                foreach (var detail in allDetails)
                {
                    detail.CommitChange();
                }

                ShowNotification("Đã lưu giá mới thành công.");

                UpdateDetailStats();
                LoadData();

                var activeRule = db.GlobalSettings.FirstOrDefault(g => g.Key == "activePriceRule")?.Value ?? "";
                if (!string.IsNullOrEmpty(activeRule) && string.Equals(activeRule, _editingRuleType, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateSentPricesForPendingOrders();
                    NotifyMenuUpdated();
                }
            }
        }

        private void BtnBackToRuleList_Click(object sender, RoutedEventArgs e)
        {
            _editingRuleType = null;
            _allRuleDetails = null;

            if (lblSelectedRuleType != null) lblSelectedRuleType.Text = "(chưa chọn)";
            if (dgRuleDetails != null) dgRuleDetails.ItemsSource = null;
            if (DetailPanel != null) DetailPanel.Visibility = Visibility.Collapsed;
            if (pnlDetailPlaceholder != null) pnlDetailPlaceholder.Visibility = Visibility.Visible;
            if (txtDetailStats != null) txtDetailStats.Text = "";
        }

        // --- FILTER LOGIC ---
        private void UpdateDetailFilter()
        {
            if (_allRuleDetails == null) return;

            var filtered = _allRuleDetails.AsEnumerable();

            // Filter by Category
            if (cboDetailCategory.SelectedValue is int catId && catId != 0)
            {
                filtered = filtered.Where(d => d.CategoryID == catId);
            }

            // Filter by Search Text
            string text = txtDetailSearch.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(text))
            {
                filtered = filtered.Where(d => MatchDishSearch(d.DishName, text));
            }

            dgRuleDetails.ItemsSource = filtered.ToList();

            UpdateDetailStats();
        }

        private void UpdateDetailStats()
        {
            if (txtDetailStats == null) return;
            if (_allRuleDetails == null)
            {
                txtDetailStats.Text = string.Empty;
                return;
            }

            int changed = _allRuleDetails.Count(d => d.NewPrice != d.OriginalPrice);

            int visible = 0;
            if (dgRuleDetails?.ItemsSource is IEnumerable<RuleDetailViewModel> visibleItems)
            {
                // Avoid expensive Count() on some enumerables
                visible = visibleItems is ICollection<RuleDetailViewModel> c ? c.Count : visibleItems.Count();
            }

            txtDetailStats.Text = $"• Hiển thị: {visible} • Đã chỉnh: {changed}";

            // Update the "apply" hint to clarify scope for +/- adjustments
            if (txtApplyHint != null)
            {
                var catName = "Tất cả";
                if (cboDetailCategory?.SelectedItem is CategoryFilterOption cat && cat.CategoryID != 0)
                {
                    catName = cat.CategoryName;
                }
                else if (cboDetailCategory?.SelectedItem is CategoryFilterOption all)
                {
                    catName = all.CategoryName;
                }

                var hasSearch = !string.IsNullOrWhiteSpace(txtDetailSearch?.Text);
                var searchPart = hasSearch ? " + tìm kiếm" : string.Empty;
                txtApplyHint.Text = $"Áp dụng cho: {catName}{searchPart} ({visible} món đang hiển thị)";
            }
        }

        private void CboDetailCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetailFilter();
        private void TxtDetailSearch_TextChanged(object sender, TextChangedEventArgs e) => UpdateDetailFilter();

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Digits only
            e.Handled = Regex.IsMatch(e.Text, "[^0-9]+");
        }

        private void BtnGroupIncrease_Click(object sender, RoutedEventArgs e)
        {
            ApplyGroupDelta(isIncrease: true);
        }

        private void BtnGroupDecrease_Click(object sender, RoutedEventArgs e)
        {
            ApplyGroupDelta(isIncrease: false);
        }

        private void BtnQuickPlus1k_Click(object sender, RoutedEventArgs e) => ApplyGroupDelta(isIncrease: true, deltaKOverride: 1);
        private void BtnQuickPlus5k_Click(object sender, RoutedEventArgs e) => ApplyGroupDelta(isIncrease: true, deltaKOverride: 5);
        private void BtnQuickPlus10k_Click(object sender, RoutedEventArgs e) => ApplyGroupDelta(isIncrease: true, deltaKOverride: 10);

        private void ApplyGroupDelta(bool isIncrease, int? deltaKOverride = null)
        {
            if (_editingRuleType == null) return;
            if (_allRuleDetails == null) return;

            int deltaK;
            if (deltaKOverride.HasValue)
            {
                deltaK = deltaKOverride.Value;
                if (txtGroupDeltaK != null) txtGroupDeltaK.Text = deltaK.ToString();
            }
            else if (!int.TryParse((txtGroupDeltaK.Text ?? string.Empty).Trim(), out deltaK) || deltaK <= 0)
            {
                ShowNotification("Vui lòng nhập số nghìn hợp lệ (ví dụ: 5 = 5.000đ).");
                txtGroupDeltaK.Focus();
                txtGroupDeltaK.SelectAll();
                return;
            }

            decimal delta = deltaK * 1000m;

            // Apply to the CURRENTLY VISIBLE list (after filter/search), which matches user expectation.
            var list = (dgRuleDetails?.ItemsSource as IEnumerable<RuleDetailViewModel>)?.ToList()
                       ?? _allRuleDetails.ToList();

            if (list.Count == 0)
            {
                ShowNotification("Không có món nào trong danh sách đang hiển thị.");
                return;
            }

            // Confirm if applying to many items (avoid accidental bulk edits)
            if (list.Count >= 50)
            {
                var msg = isIncrease
                    ? $"Tăng {deltaK:N0} nghìn cho {list.Count} món đang hiển thị?"
                    : $"Giảm {deltaK:N0} nghìn cho {list.Count} món đang hiển thị?";
                if (MessageBox.Show(msg, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            foreach (var item in list)
            {
                var newPrice = isIncrease ? (item.NewPrice + delta) : (item.NewPrice - delta);
                if (newPrice < 0) newPrice = 0;
                item.NewPrice = newPrice;
            }

            UpdateDetailFilter();

            var actionText = isIncrease ? "Tăng" : "Giảm";
            ShowNotification($"{actionText} {deltaK:N0} nghìn cho {list.Count} món đang hiển thị. Nhớ bấm 'Lưu' để lưu.");
        }

        private bool MatchDishSearch(string dishName, string searchText)
        {
            string normalized = RemoveDiacritics(dishName).ToLower();
            string normalizedSearch = RemoveDiacritics(searchText).ToLower();

            // Full name match (handles diacritical marks)
            if (normalized.Contains(normalizedSearch))
                return true;

            // First letter abbreviation match (e.g., "mctc" for "mỳ cay thập cẩm")
            var words = dishName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string firstLetters = string.Concat(words.Select(w => RemoveDiacritics(w.Substring(0, 1)).ToLower()));
            if (firstLetters.Contains(normalizedSearch))
                return true;

            // Partial first letter match (e.g., "tc" for "thập cẩm" in "mỳ cay thập cẩm")
            foreach (var word in words)
            {
                if (RemoveDiacritics(word).ToLower().StartsWith(normalizedSearch)) return true;
            }

            return false;
        }

        private string RemoveDiacritics(string text)
        {
            // Simple helper or copy from MainWindow
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
        }

        // --- UTILS ---

        private void ShowNotification(string message)
        {
            // Inline toast in this page (non-blocking)
            if (txtToast == null || pnlToast == null)
            {
                MessageBox.Show(message, "Thông báo");
                return;
            }

            txtToast.Text = message;
            pnlToast.Visibility = Visibility.Visible;

            _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _toastTimer.Stop();
            _toastTimer.Tick -= ToastTimer_Tick;
            _toastTimer.Tick += ToastTimer_Tick;
            _toastTimer.Start();
        }

        private void ToastTimer_Tick(object? sender, EventArgs e)
        {
            _toastTimer?.Stop();
            if (pnlToast != null) pnlToast.Visibility = Visibility.Collapsed;
        }
    }

    public class RuleTypeViewModel
    {
        public string RuleType { get; set; } = "";
        public int ProductCount { get; set; }
        public bool IsActive { get; set; }
    }

    public class RuleDetailViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public int DishID { get; set; }
        public string CategoryName { get; set; } = "";
        public string DishName { get; set; } = "";
        public string Unit { get; set; } = "";
        public int CategoryID { get; set; }
        public decimal BasePrice { get; set; }

        // [NEW] Logic for tracking changes
        public decimal OriginalPrice { get; set; }

        private decimal _newPrice;
        public decimal NewPrice
        {
            get => _newPrice;
            set
            {
                if (_newPrice != value)
                {
                    _newPrice = value;
                    OnPropertyChanged(nameof(NewPrice));
                    OnPropertyChanged(nameof(RowBackground));
                }
            }
        }

        public string RowBackground => (NewPrice != OriginalPrice) ? "#FFF3CD" : "White"; // Yellowish if changed

        public void CommitChange()
        {
            OriginalPrice = NewPrice;
            OnPropertyChanged(nameof(RowBackground));
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}

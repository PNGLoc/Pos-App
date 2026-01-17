using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using Microsoft.EntityFrameworkCore;

namespace PosSystem.Main.Pages
{
    public partial class PriceRuleSetupPage : UserControl
    {
        private string? _editingRuleType = null;
        private List<RuleTypeViewModel>? _originalRuleTypes = null;
        private List<RuleDetailViewModel>? _allRuleDetails = null; // [NEW] Cache for filtering
        private bool _isLoadingData = true;

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

            ShowNotification($"Đã áp dụng: {selectedRule}");
        }

        private void dgRuleTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optional: Handle row selection
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
                MessageBox.Show("Vui lòng nhập tên loại giá!");
                return;
            }

            using (var db = new AppDbContext())
            {
                // [MODIFIED] Case insensitive check
                if (db.PriceRuleTypes.ToList().Any(p => p.RuleType.ToLower() == ruleName.ToLower()))
                {
                    MessageBox.Show("Loại giá này đã tồn tại!");
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
                var cats = db.Categories.OrderBy(c => c.OrderIndex).Select(c => new { c.CategoryID, c.CategoryName }).ToList();
                var catList = new List<dynamic> { new { CategoryID = 0, CategoryName = "Tất cả danh mục" } };
                catList.AddRange(cats);
                cboDetailCategory.ItemsSource = catList;
                cboDetailCategory.SelectedValuePath = "CategoryID";
                cboDetailCategory.DisplayMemberPath = "CategoryName";
                cboDetailCategory.SelectedIndex = 0;
                txtDetailSearch.Clear();

                UpdateDetailFilter(); // Initial Load
                DetailPanel.Visibility = Visibility.Visible;
            }
        }

        private void BtnSaveRuleDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_editingRuleType == null) return;

            using (var db = new AppDbContext())
            {
                var detailsData = dgRuleDetails.ItemsSource as List<RuleDetailViewModel>;
                if (detailsData == null) return;

                foreach (var detail in detailsData)
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
                ShowNotification("Lưu giá mới thành công!");
                
                // [MODIFIED] Update OriginalPrice to clear highlights
                foreach (var detail in detailsData)
                {
                    detail.CommitChange();
                }

                // DetailPanel.Visibility = Visibility.Collapsed; // User wants to keep open
                _allRuleDetails = null; // Clear cache
                LoadData();
            }
        }

        private void BtnBackToRuleList_Click(object sender, RoutedEventArgs e)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            _allRuleDetails = null; // Clear memory
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
        }

        private void CboDetailCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetailFilter();
        private void TxtDetailSearch_TextChanged(object sender, TextChangedEventArgs e) => UpdateDetailFilter();

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
            // Nếu MainWindow có hàm ShowToast thì gọi, không thì MessageBox
            var window = Window.GetWindow(this);
            if (window != null && window.GetType().Name == "MainWindow") // Hard check name because generic MainWindow cast might fail if namespace differs
            {
                // Reflection call if specific type is not accessible
               // For now just MessageBox or try cast if namespace known
               // Assuming logic from previous:
               // MessageBox.Show(message); 
            }
            
             MessageBox.Show(message, "Thông báo");
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

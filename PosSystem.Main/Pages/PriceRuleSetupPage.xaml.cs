using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;

namespace PosSystem.Main.Pages
{
    public partial class PriceRuleSetupPage : UserControl
    {
        private string? _editingRuleType = null;
        private List<RuleTypeViewModel>? _originalRuleTypes = null;
        private bool _isLoadingData = true; // Prevent SelectionChanged from firing during initial load

        public PriceRuleSetupPage()
        {
            InitializeComponent();
            LoadData();
            _isLoadingData = false; // Mark loading complete

            // Hook DataGrid events for auto-save
            dgRuleTypes.CellEditEnding += DgRuleTypes_CellEditEnding;
        }

        private void DgRuleTypes_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Auto-save when RuleType cell edit ends
            if (e.Column.Header?.ToString() == "Loại Giá")
            {
                var vm = e.Row.Item as RuleTypeViewModel;
                if (vm != null)
                {
                    // Delay the save to allow the binding to update
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SaveRuleTypeChange(vm);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void SaveRuleTypeChange(RuleTypeViewModel vm)
        {
            // Find the index of this item in the DataGrid
            var currentItems = dgRuleTypes.ItemsSource as List<RuleTypeViewModel>;
            int itemIndex = currentItems?.IndexOf(vm) ?? -1;

            if (itemIndex < 0 || _originalRuleTypes == null || itemIndex >= _originalRuleTypes.Count)
            {
                return;
            }

            // Get the original by index
            var original = _originalRuleTypes[itemIndex];
            var oldRuleType = original.RuleType;
            var newRuleType = vm.RuleType?.Trim() ?? "";

            // Validate
            if (string.IsNullOrWhiteSpace(newRuleType) || oldRuleType == newRuleType)
            {
                return;
            }

            using (var db = new AppDbContext())
            {
                // Check if new name already exists
                if (db.PriceRuleTypes.Any(r => r.RuleType == newRuleType))
                {
                    ShowNotification("Loại giá này đã tồn tại!");
                    LoadData(); // Reload to revert
                    return;
                }

                // Update all DishPriceRules
                var rules = db.DishPriceRules.Where(p => p.RuleType == oldRuleType).ToList();
                foreach (var rule in rules)
                {
                    rule.RuleType = newRuleType;
                }

                // Update PriceRuleType
                var ruleType = db.PriceRuleTypes.FirstOrDefault(r => r.RuleType == oldRuleType);
                if (ruleType != null)
                {
                    ruleType.RuleType = newRuleType;
                }

                try
                {
                    db.SaveChanges();
                    ShowNotification("Lưu thay đổi thành công!");
                    LoadData(); // Reload from database to ensure data consistency
                }
                catch (Exception ex)
                {
                    ShowNotification($"Lỗi khi lưu: {ex.Message}");
                    LoadData(); // Reload to revert on error
                }
            }
        }

        private void LoadData()
        {
            _isLoadingData = true; // Prevent SelectionChanged notification

            using (var db = new AppDbContext())
            {
                // Load danh sách rule types từ bảng PriceRuleType
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

                // Lưu copy để detect thay đổi - tạo deep copy
                _originalRuleTypes = ruleTypeViewModels.Select(r => new RuleTypeViewModel
                {
                    RuleType = r.RuleType,
                    ProductCount = r.ProductCount,
                    IsActive = r.IsActive
                }).ToList();

                // Load danh sách rule types cho combo áp dụng
                var availableRules = ruleTypes.Select(r => r.RuleType).ToList();
                availableRules.Insert(0, "(Giá gốc)");
                cboActiveRule.ItemsSource = availableRules;

                // Load rule đang hoạt động
                var activeSetting = db.GlobalSettings
                    .FirstOrDefault(g => g.Key == "activePriceRule");

                if (activeSetting != null && !string.IsNullOrEmpty(activeSetting.Value))
                    cboActiveRule.SelectedItem = activeSetting.Value;
                else
                    cboActiveRule.SelectedIndex = 0;
            }

            _isLoadingData = false; // Allow SelectionChanged after load
        }

        // === TAB 1: Quản lý Loại Giá ===

        private void CboActiveRule_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip notification during initial load
            if (_isLoadingData) return;

            if (cboActiveRule.SelectedIndex < 0) return;

            string selectedRule = cboActiveRule.SelectedItem as string ?? "";

            if (selectedRule == "(Giá gốc)")
                PriceService.SetActivePriceRule("");
            else
                PriceService.SetActivePriceRule(selectedRule);

            // Hiển thị popup thông báo
            ShowNotification($"Đã áp dụng: {selectedRule}");
        }

        private void dgRuleTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Detect thay đổi checkbox IsActive
            CheckForChanges();
        }

        private void CheckForChanges()
        {
            // Không cần detect thay đổi nữa - đã đơn giản hóa
        }

        private void BtnShowAddForm_Click(object sender, RoutedEventArgs e)
        {
            AddRulePanel.Visibility = Visibility.Visible;
            ClearRuleTypeForm();
        }

        private void BtnCancelAddForm_Click(object sender, RoutedEventArgs e)
        {
            AddRulePanel.Visibility = Visibility.Collapsed;
            ClearRuleTypeForm();
        }

        private void BtnSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            var currentData = dgRuleTypes.ItemsSource as List<RuleTypeViewModel>;
            if (currentData == null) return;

            using (var db = new AppDbContext())
            {
                foreach (var current in currentData)
                {
                    var ruleType = db.PriceRuleTypes.FirstOrDefault(r => r.RuleType == current.RuleType);
                    if (ruleType != null)
                    {
                        ruleType.IsActive = current.IsActive;
                    }
                }
                db.SaveChanges();

                ShowNotification("Lưu thay đổi thành công!");

                // Cập nhật bản copy
                _originalRuleTypes = currentData.Select(r => new RuleTypeViewModel
                {
                    RuleType = r.RuleType,
                    ProductCount = r.ProductCount,
                    IsActive = r.IsActive
                }).ToList();
            }
        }

        private void BtnAddRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtRuleType.Text))
            {
                ShowNotification("Vui lòng nhập loại giá!");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (db.PriceRuleTypes.Any(p => p.RuleType == txtRuleType.Text))
                {
                    ShowNotification("Loại giá này đã tồn tại!");
                    return;
                }

                var newRuleType = new PriceRuleType
                {
                    RuleType = txtRuleType.Text,
                    IsActive = chkRuleTypeActive.IsChecked ?? true,
                    CreatedDate = DateTime.Now
                };
                db.PriceRuleTypes.Add(newRuleType);
                db.SaveChanges();

                ShowNotification("Thêm loại giá thành công!");
                ClearRuleTypeForm();
                AddRulePanel.Visibility = Visibility.Collapsed;
                LoadData();
            }
        }

        private void BtnDeleteRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RuleTypeViewModel vm)
            {
                if (ShowConfirmation($"Xóa loại giá '{vm.RuleType}' và tất cả rules liên quan?"))
                {
                    using (var db = new AppDbContext())
                    {
                        // Xóa tất cả DishPriceRule của loại giá này
                        var rules = db.DishPriceRules.Where(p => p.RuleType == vm.RuleType).ToList();
                        db.DishPriceRules.RemoveRange(rules);

                        // Xóa loại giá
                        var ruleType = db.PriceRuleTypes.FirstOrDefault(r => r.RuleType == vm.RuleType);
                        if (ruleType != null)
                            db.PriceRuleTypes.Remove(ruleType);

                        db.SaveChanges();

                        ShowNotification("Xóa thành công!");
                        LoadData();
                    }
                }
            }
        }

        private void BtnEditRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RuleTypeViewModel vm)
            {
                // Load chi tiết loại giá sang tab 2
                LoadRuleDetails(vm.RuleType);
            }
        }

        private void ClearRuleTypeForm()
        {
            _editingRuleType = null;
            txtRuleType.Clear();
            chkRuleTypeActive.IsChecked = true;
        }

        // === TAB 2: Chi Tiết Loại Giá ===

        private void LoadRuleDetails(string ruleType)
        {
            using (var db = new AppDbContext())
            {
                lblSelectedRuleType.Text = ruleType;
                _editingRuleType = ruleType;

                // Load tất cả sản phẩm
                var dishes = db.Dishes.Where(d => d.DishStatus == "Active").ToList();

                var detailsViewModels = dishes.Select(d => new RuleDetailViewModel
                {
                    DishID = d.DishID,
                    DishName = d.DishName,
                    BasePrice = d.Price,
                    NewPrice = db.DishPriceRules
                        .Where(p => p.DishID == d.DishID && p.RuleType == ruleType)
                        .Select(p => (decimal?)p.Price)
                        .FirstOrDefault() ?? d.Price
                }).ToList();

                dgRuleDetails.ItemsSource = detailsViewModels;

                // Hiển thị overlay
                DetailOverlay.Visibility = Visibility.Visible;
                DetailPanel.Visibility = Visibility.Visible;
            }
        }

        private void BtnSaveRuleDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_editingRuleType == null)
                return;

            using (var db = new AppDbContext())
            {
                var detailsData = dgRuleDetails.ItemsSource as List<RuleDetailViewModel>
                    ?? new List<RuleDetailViewModel>();

                foreach (var detail in detailsData)
                {
                    var existingRule = db.DishPriceRules
                        .FirstOrDefault(p => p.DishID == detail.DishID && p.RuleType == _editingRuleType);

                    if (existingRule != null)
                    {
                        existingRule.Price = detail.NewPrice;
                    }
                    else
                    {
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

                ShowNotification("Lưu chi tiết loại giá thành công!");

                // Ẩn overlay và quay về trang chính
                DetailOverlay.Visibility = Visibility.Collapsed;
                DetailPanel.Visibility = Visibility.Collapsed;

                LoadData();
            }
        }

        private void BtnBackToRuleList_Click(object sender, RoutedEventArgs e)
        {
            DetailOverlay.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
        }

        // Toast Notification Methods
        private void ShowNotification(string message)
        {
            var window = Window.GetWindow(this);
            if (window is MainWindow mainWindow)
            {
                mainWindow.ShowToast(message);
            }
            else
            {
                MessageBox.Show(message);
            }
        }

        private bool ShowConfirmation(string message)
        {
            return MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }
    }

    // ViewModels
    public class RuleTypeViewModel
    {
        public string RuleType { get; set; } = "";
        public int ProductCount { get; set; }
        public bool IsActive { get; set; }
    }

    public class RuleDetailViewModel
    {
        public int DishID { get; set; }
        public string DishName { get; set; } = "";
        public decimal BasePrice { get; set; }
        public decimal NewPrice { get; set; }
    }
}


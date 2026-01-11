using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;

namespace PosSystem.Main.Pages
{
    public partial class PriceRuleSetupPage : UserControl
    {
        private string? _editingRuleType = null;
        private List<RuleTypeViewModel>? _originalRuleTypes = null;
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
                if (db.PriceRuleTypes.Any(p => p.RuleType == ruleName))
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

                var dishes = db.Dishes.Where(d => d.DishStatus == "Active").ToList();
                var rules = db.DishPriceRules.Where(p => p.RuleType == ruleType).ToList();

                var detailsViewModels = dishes.Select(d => new RuleDetailViewModel
                {
                    DishID = d.DishID,
                    DishName = d.DishName,
                    Unit = d.Unit,
                    BasePrice = d.Price, // Giá gốc
                    NewPrice = rules.FirstOrDefault(p => p.DishID == d.DishID)?.Price ?? d.Price // Giá mới (nếu có) hoặc mặc định bằng giá cũ
                }).ToList();

                dgRuleDetails.ItemsSource = detailsViewModels;
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
                DetailPanel.Visibility = Visibility.Collapsed;
                LoadData();
            }
        }

        private void BtnBackToRuleList_Click(object sender, RoutedEventArgs e)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
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

    public class RuleDetailViewModel
    {
        public int DishID { get; set; }
        public string DishName { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal BasePrice { get; set; }
        public decimal NewPrice { get; set; }
    }
}

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

        public PriceRuleSetupPage() 
        { 
            InitializeComponent(); 
            LoadData(); 
        }

        private void LoadData()
        {
            using (var db = new AppDbContext())
            {
                // Load danh sách rule types từ bảng PriceRuleType
                var ruleTypes = db.PriceRuleTypes
                    .OrderByDescending(r => r.CreatedDate)
                    .ToList();

                var ruleTypeViewModels = ruleTypes.Select(rt => new RuleTypeViewModel
                {
                    RuleType = rt.RuleType,
                    ProductCount = db.DishPriceRules.Count(p => p.RuleType == rt.RuleType),
                    IsActive = rt.IsActive
                }).ToList();

                dgRuleTypes.ItemsSource = ruleTypeViewModels;

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
        }

        // === TAB 1: Quản lý Loại Giá ===

        private void dgRuleTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgRuleTypes.SelectedItem is RuleTypeViewModel vm)
            {
                txtRuleType.Text = vm.RuleType;
                chkRuleTypeActive.IsChecked = vm.IsActive;
                _editingRuleType = vm.RuleType;
            }
        }

        private void BtnAddRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtRuleType.Text))
            {
                MessageBox.Show("Vui lòng nhập loại giá!");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (db.PriceRuleTypes.Any(p => p.RuleType == txtRuleType.Text))
                {
                    MessageBox.Show("Loại giá này đã tồn tại!");
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

                MessageBox.Show("Thêm loại giá thành công!");
                ClearRuleTypeForm();
                LoadData();
            }
        }

        private void BtnUpdateRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (_editingRuleType == null)
            {
                MessageBox.Show("Vui lòng chọn loại giá để sửa!");
                return;
            }

            using (var db = new AppDbContext())
            {
                var ruleType = db.PriceRuleTypes.FirstOrDefault(r => r.RuleType == _editingRuleType);
                if (ruleType != null)
                {
                    ruleType.IsActive = chkRuleTypeActive.IsChecked ?? true;
                    db.SaveChanges();
                    MessageBox.Show("Cập nhật loại giá thành công!");
                    ClearRuleTypeForm();
                    LoadData();
                }
            }
        }

        private void BtnDeleteRuleType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RuleTypeViewModel vm)
            {
                if (MessageBox.Show($"Xóa loại giá '{vm.RuleType}' và tất cả rules liên quan?", 
                    "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var rules = db.DishPriceRules.Where(p => p.RuleType == vm.RuleType).ToList();
                        db.DishPriceRules.RemoveRange(rules);
                        db.SaveChanges();

                        MessageBox.Show("Xóa thành công!");
                        ClearRuleTypeForm();
                        LoadData();
                    }
                }
            }
        }

        private void BtnDeleteRuleTypeForm_Click(object sender, RoutedEventArgs e)
        {
            if (_editingRuleType == null)
            {
                MessageBox.Show("Vui lòng chọn loại giá để xóa!");
                return;
            }

            if (MessageBox.Show($"Xóa loại giá '{_editingRuleType}' và tất cả rules liên quan?", 
                "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    // Xóa tất cả DishPriceRule của loại giá này
                    var rules = db.DishPriceRules.Where(p => p.RuleType == _editingRuleType).ToList();
                    db.DishPriceRules.RemoveRange(rules);

                    // Xóa loại giá
                    var ruleType = db.PriceRuleTypes.FirstOrDefault(r => r.RuleType == _editingRuleType);
                    if (ruleType != null)
                        db.PriceRuleTypes.Remove(ruleType);

                    db.SaveChanges();

                    MessageBox.Show("Xóa thành công!");
                    ClearRuleTypeForm();
                    LoadData();
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

        private void BtnSaveActiveRule_Click(object sender, RoutedEventArgs e)
        {
            string selectedRule = cboActiveRule.SelectedItem as string ?? "";

            if (selectedRule == "(Giá gốc)")
                PriceService.SetActivePriceRule("");
            else
                PriceService.SetActivePriceRule(selectedRule);

            MessageBox.Show($"Đã áp dụng: {selectedRule}");
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

                // Switch to tab 2
                if (this.Parent is TabControl tab)
                {
                    tab.SelectedIndex = 1;
                }
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
                MessageBox.Show("Lưu chi tiết loại giá thành công!");
                LoadData();
            }
        }

        private void BtnBackToRuleList_Click(object sender, RoutedEventArgs e)
        {
            if (this.Parent is TabControl tab)
            {
                tab.SelectedIndex = 0;
            }
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


using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore; // For DbUpdateException
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Server.Hubs;

namespace PosSystem.Main.Pages
{
    public partial class TableCategorySetupPage : UserControl
    {
        private TableCategory? _selected = null;
        private const string KeyShowTableIcons = "showTableCardIcons";
        private readonly List<IconOption> _iconOptions = new List<IconOption>
        {
            new IconOption("Bàn thường", "fas fa-chair", char.ConvertFromUtf32(0xf6c0)),
            new IconOption("Mang về", "fas fa-shopping-bag", char.ConvertFromUtf32(0xf290)),
            new IconOption("Khách lấy", "fas fa-walking", char.ConvertFromUtf32(0xf554)),
            new IconOption("Ship", "fas fa-motorcycle", char.ConvertFromUtf32(0xf21c)),
            new IconOption("VIP", "fas fa-crown", char.ConvertFromUtf32(0xf521)),
            new IconOption("Gia đình", "fas fa-users", char.ConvertFromUtf32(0xf0c0)),
            new IconOption("Hẹn giờ", "fas fa-clock", char.ConvertFromUtf32(0xf017))
        };

        public TableCategorySetupPage()
        {
            InitializeComponent();
            InitIconPicker();
            LoadData();
            LoadTableIconSetting();
        }

        private void NotifyTablesUpdated()
        {
            try
            {
                if (App.WebHost == null) return;
                var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                if (hubContext != null)
                {
                    _ = hubContext.Clients.All.SendAsync("TableUpdated", -1);
                }
            }
            catch { }
        }

        private void InitIconPicker()
        {
            cmbIconClass.ItemsSource = _iconOptions;
            cmbIconClass.SelectedValuePath = nameof(IconOption.Class);
            cmbIconClass.SelectedValue = "fas fa-chair";
        }

        void LoadData()
        {
            try
            {
                using (var db = new AppDbContext())
                    dgCategories.ItemsSource = db.TableCategories
                        .OrderBy(c => c.DisplayOrder)
                        .ThenBy(c => c.CategoryID)
                        .ToList();
            }
            catch { }
        }

        private void LoadTableIconSetting()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var setting = db.GlobalSettings.FirstOrDefault(s => s.Key == KeyShowTableIcons);
                    chkShowTableIcons.IsChecked = setting == null || setting.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                chkShowTableIcons.IsChecked = true;
            }
        }

        private void SaveTableIconSetting(bool enabled)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    UpsertSetting(db, KeyShowTableIcons, enabled.ToString().ToLower());
                    db.SaveChanges();
                }

                MainWindow.ApplyTableIconSettingsToOpenWindows();
            }
            catch { }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _selected = null;
            txtName.Text = "";
            txtDisplayOrder.Text = "";
            txtDesc.Text = "";
            txtBorderColor.Text = "#D0D0D0";
            SelectIconByClass("fas fa-chair");
            UpdateBorderColorPreview();

            // Default display order = max + 1
            try
            {
                using (var db = new AppDbContext())
                {
                    var next = (db.TableCategories.Select(c => (int?)c.DisplayOrder).Max() ?? 0) + 1;
                    txtDisplayOrder.Text = next.ToString();
                }
            }
            catch
            {
                txtDisplayOrder.Text = "1";
            }

            lblModalTitle.Text = "THÊM LOẠI BÀN";
            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TableCategory cat)
            {
                _selected = cat;
                txtName.Text = cat.CategoryName;
                txtDisplayOrder.Text = cat.DisplayOrder.ToString();
                txtDesc.Text = cat.Description;
                txtBorderColor.Text = string.IsNullOrWhiteSpace(cat.BorderColorHex) ? "#D0D0D0" : cat.BorderColorHex;
                SelectIconByClass(cat.IconClass);
                UpdateBorderColorPreview();

                lblModalTitle.Text = "SỬA LOẠI BÀN";
                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TableCategory cat)
            {
                if (MessageBox.Show($"Bạn có chắc muốn xóa loại bàn '{cat.CategoryName}'?\nTất cả bàn và đơn hàng liên quan sẽ bị xóa.", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.TableCategories.Find(cat.CategoryID);
                        if (item != null)
                        {
                            try
                            {
                                var tableIds = db.Tables
                                    .Where(t => t.CategoryID == item.CategoryID)
                                    .Select(t => t.TableID)
                                    .ToList();

                                if (tableIds.Count > 0)
                                {
                                    var orders = db.Orders
                                        .Include(o => o.OrderDetails)
                                        .Where(o => o.TableID.HasValue && tableIds.Contains(o.TableID.Value))
                                        .ToList();

                                    var orderDetails = orders.SelectMany(o => o.OrderDetails).ToList();
                                    if (orderDetails.Count > 0)
                                        db.OrderDetails.RemoveRange(orderDetails);

                                    if (orders.Count > 0)
                                        db.Orders.RemoveRange(orders);

                                    var cancelLogs = db.CancelledLogs
                                        .Where(c => c.TableID.HasValue && tableIds.Contains(c.TableID.Value))
                                        .ToList();
                                    if (cancelLogs.Count > 0)
                                        db.CancelledLogs.RemoveRange(cancelLogs);

                                    var tables = db.Tables.Where(t => tableIds.Contains(t.TableID)).ToList();
                                    if (tables.Count > 0)
                                        db.Tables.RemoveRange(tables);
                                }

                                db.TableCategories.Remove(item);
                                db.SaveChanges();
                                LoadData();
                                NotifyTablesUpdated();
                            }
                            catch (DbUpdateException)
                            {
                                MessageBox.Show("Không thể xóa loại bàn do còn dữ liệu liên quan.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                }
            }
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        private void ChkShowTableIcons_Checked(object sender, RoutedEventArgs e)
        {
            SaveTableIconSetting(true);
        }

        private void ChkShowTableIcons_Unchecked(object sender, RoutedEventArgs e)
        {
            SaveTableIconSetting(false);
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên loại bàn!", "Thiếu thông tin");
                return;
            }

            if (!int.TryParse(txtDisplayOrder.Text?.Trim(), out var displayOrder))
            {
                MessageBox.Show("Thứ tự hiển thị phải là số nguyên!", "Sai định dạng");
                return;
            }

            using (var db = new AppDbContext())
            {
                var borderColor = NormalizeHex(txtBorderColor.Text?.Trim());
                var iconClass = GetSelectedIconClass();
                if (_selected == null)
                {
                    // Add new
                    // Validate Duplicate
                    if (db.TableCategories.ToList().Any(c => c.CategoryName.ToLower() == txtName.Text.ToLower()))
                    {
                        MessageBox.Show("Tên loại bàn đã tồn tại!");
                        return;
                    }

                    var newCat = new TableCategory
                    {
                        CategoryName = txtName.Text,
                        Description = txtDesc.Text,
                        DisplayOrder = displayOrder,
                        BorderColorHex = borderColor,
                        IconClass = iconClass
                    };
                    db.TableCategories.Add(newCat);
                }
                else
                {
                    // Update
                    var item = db.TableCategories.Find(_selected.CategoryID);
                    if (item != null)
                    {
                        // Validate Duplicate
                        if (item.CategoryName.ToLower() != txtName.Text.ToLower())
                        {
                            if (db.TableCategories.ToList().Any(c => c.CategoryName.ToLower() == txtName.Text.ToLower()))
                            {
                                MessageBox.Show("Tên loại bàn đã tồn tại!");
                                return;
                            }
                        }

                        item.CategoryName = txtName.Text;
                        item.Description = txtDesc.Text;
                        item.DisplayOrder = displayOrder;
                        item.BorderColorHex = borderColor;
                        item.IconClass = iconClass;
                    }
                }
                db.SaveChanges();
            }
            modalOverlay.Visibility = Visibility.Collapsed;
            LoadData();
            NotifyTablesUpdated();
        }

        private void TxtBorderColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBorderColorPreview();
        }

        private void UpdateBorderColorPreview()
        {
            var hex = NormalizeHex(txtBorderColor.Text?.Trim());
            try
            {
                var brush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex);
                previewBorderColor.BorderBrush = brush;
            }
            catch
            {
                previewBorderColor.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 208, 208));
            }
        }

        private string NormalizeHex(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "#D0D0D0";

            var hex = input.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;

            // Basic validation: #RRGGBB
            if (hex.Length != 7)
                return "#D0D0D0";

            for (int i = 1; i < hex.Length; i++)
            {
                var c = hex[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return "#D0D0D0";
            }

            return hex.ToUpperInvariant();
        }

        private string GetSelectedIconClass()
        {
            if (cmbIconClass.SelectedItem is IconOption opt)
                return opt.Class;
            return "fas fa-chair";
        }

        private void SelectIconByClass(string? iconClass)
        {
            var cls = string.IsNullOrWhiteSpace(iconClass) ? "fas fa-chair" : iconClass.Trim();
            var match = _iconOptions.FirstOrDefault(o => o.Class.Equals(cls, StringComparison.OrdinalIgnoreCase));
            cmbIconClass.SelectedItem = match ?? _iconOptions.FirstOrDefault();
        }

        private static void UpsertSetting(AppDbContext db, string key, string value)
        {
            var setting = db.GlobalSettings.FirstOrDefault(s => s.Key == key);
            if (setting == null)
            {
                setting = new GlobalSetting
                {
                    Key = key,
                    Value = value,
                    Description = "Table card icon setting",
                    ModifiedDate = DateTime.Now
                };
                db.GlobalSettings.Add(setting);
            }
            else
            {
                setting.Value = value;
                setting.ModifiedDate = DateTime.Now;
            }
        }

        private sealed class IconOption
        {
            public IconOption(string name, string @class, string glyph)
            {
                Name = name;
                Class = @class;
                Glyph = glyph;
            }

            public string Name { get; }
            public string Class { get; }
            public string Glyph { get; }
        }
    }
}

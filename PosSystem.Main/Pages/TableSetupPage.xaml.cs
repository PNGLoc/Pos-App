using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore; // For Include
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class TableSetupPage : UserControl
    {
        private Table? _selected = null;
        public List<TableCategory> Categories { get; set; } = new List<TableCategory>();

        public TableSetupPage()
        {
            InitializeComponent();
            this.DataContext = this; // Set DataContext for Binding
            LoadData();
        }

        void LoadData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Load Categories for ComboBox
                    Categories = db.TableCategories
                        .OrderBy(c => c.DisplayOrder)
                        .ThenBy(c => c.CategoryID)
                        .ToList();
                    cboType.ItemsSource = Categories; // Refresh ItemsSource

                    // Load Tables with Category info
                    dgTables.ItemsSource = db.Tables.Include(t => t.Category).OrderBy(t => t.TableID).ToList();
                }
            }
            catch { }
        }

        // --- CÁC HÀM XỬ LÝ MODAL ---

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Mở form thêm mới
            _selected = null;
            txtName.Text = "";
            if (Categories.Count > 0) cboType.SelectedIndex = 0;

            lblModalTitle.Text = "THÊM BÀN MỚI";
            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            // Mở form sửa từ nút trên dòng
            if (sender is Button btn && btn.Tag is Table t)
            {
                _selected = t;
                txtName.Text = t.TableName;

                // Select Category in ComboBox
                if (t.CategoryID.HasValue)
                {
                    cboType.SelectedValue = t.CategoryID.Value;
                }
                else
                {
                    cboType.SelectedIndex = -1;
                }

                lblModalTitle.Text = "SỬA THÔNG TIN BÀN";
                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Table t)
            {
                if (MessageBox.Show($"Bạn có chắc muốn xóa bàn '{t.TableName}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Tables.Find(t.TableID);
                        if (item != null)
                        {
                            db.Tables.Remove(item);
                            db.SaveChanges();
                            LoadData();
                        }
                    }
                }
            }
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên bàn!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? selectedCatId = (int?)cboType.SelectedValue;
            string catName = "DineIn"; // Default fallback

            // Get Category Name for backward compatibility
            if (cboType.SelectedItem is TableCategory cat)
            {
                // Map category name to old TableType string if needed, or just use CategoryName
                // For now, let's keep TableType simple or map it based on known categories
                // Simple mapping strategy: Keep TableType = "DineIn" by default or derived from CategoryName if possible.
                // Actually, let's just save the CategoryName as TableType for now to see it in debugging, 
                // but rely on CategoryID for logic.
                // Better approach: If Category Name matches "Mang Về" -> "TakeAway", etc.

                if (cat.CategoryName.Contains("Mang")) catName = "TakeAway";
                else if (cat.CategoryName.Contains("Ship")) catName = "Delivery";
                else if (cat.CategoryName.Contains("Khách")) catName = "Pickup";
                else catName = "DineIn";
            }

            using (var db = new AppDbContext())
            {
                if (_selected == null)
                {
                    // Thêm mới
                    // Validate
                    if (db.Tables.ToList().Any(tbl => tbl.TableName.ToLower() == txtName.Text.ToLower()))
                    {
                        MessageBox.Show("Tên bàn đã tồn tại!");
                        return;
                    }

                    db.Tables.Add(new Table
                    {
                        TableName = txtName.Text,
                        CategoryID = selectedCatId,
                        TableType = catName, // Legacy support
                        TableStatus = "Empty"
                    });
                }
                else
                {
                    // Cập nhật
                    var t = db.Tables.Find(_selected.TableID);
                    if (t != null)
                    {
                        // Validate
                        if (t.TableName.ToLower() != txtName.Text.ToLower())
                        {
                            if (db.Tables.ToList().Any(tbl => tbl.TableName.ToLower() == txtName.Text.ToLower()))
                            {
                                MessageBox.Show("Tên bàn đã tồn tại!");
                                return;
                            }
                        }

                        t.TableName = txtName.Text;
                        t.CategoryID = selectedCatId;
                        t.TableType = catName; // Legacy support
                    }
                }
                db.SaveChanges();
            }

            // Đóng modal và tải lại dữ liệu
            modalOverlay.Visibility = Visibility.Collapsed;
            LoadData();
        }
    }
}
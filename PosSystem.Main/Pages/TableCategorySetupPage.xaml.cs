using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class TableCategorySetupPage : UserControl
    {
        private TableCategory? _selected = null;
        public TableCategorySetupPage() { InitializeComponent(); LoadData(); }

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

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _selected = null;
            txtName.Text = "";
            txtDisplayOrder.Text = "";
            txtDesc.Text = "";

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

                lblModalTitle.Text = "SỬA LOẠI BÀN";
                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TableCategory cat)
            {
                if (MessageBox.Show($"Bạn có chắc muốn xóa loại bàn '{cat.CategoryName}'?\nLưu ý: Các bàn thuộc loại này có thể bị ảnh hưởng.", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.TableCategories.Find(cat.CategoryID);
                        if (item != null)
                        {
                            // Kiểm tra ràng buộc nếu cần (Optional)
                            db.TableCategories.Remove(item);
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
                        DisplayOrder = displayOrder
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
                    }
                }
                db.SaveChanges();
            }
            modalOverlay.Visibility = Visibility.Collapsed;
            LoadData();
        }
    }
}

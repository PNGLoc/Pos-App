using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32; // Để dùng OpenFileDialog
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;

namespace PosSystem.Main.Pages
{
    public partial class MenuSetupPage : UserControl
    {
        // Biến lưu trạng thái
        private Category? _selectedCat;
        private Dish? _selectedDish;
        private string _currentImgPath = "default.png"; // Ảnh mặc định

        public MenuSetupPage()
        {
            InitializeComponent();
            LoadCats();
            LoadDishes();
        }

        // ==========================================
        // PHẦN 1: QUẢN LÝ DANH MỤC (CATEGORY)
        // ==========================================

        void LoadCats()
        {
            using (var db = new AppDbContext())
            {
                var list = db.Categories.OrderBy(c => c.OrderIndex).ToList();
                dgCats.ItemsSource = list;

                // Load danh sách máy in vào ComboBox
                cboPrinters.ItemsSource = db.Printers.Where(p => p.IsActive).ToList();

                // Cập nhật luôn ComboBox chọn nhóm bên Tab Món ăn
                cboDishCat.ItemsSource = list;
            }
        }

        private void dgCats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgCats.SelectedItem is Category c)
            {
                _selectedCat = c;
                txtCatName.Text = c.CategoryName;
                txtCatIndex.Text = c.OrderIndex.ToString();
                cboPrinters.SelectedValue = c.PrinterID;
            }
        }

        private void BtnAddCat_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtCatName.Text)) return;

            using (var db = new AppDbContext())
            {
                var cat = new Category
                {
                    CategoryName = txtCatName.Text,
                    OrderIndex = int.TryParse(txtCatIndex.Text, out int idx) ? idx : 0,
                    PrinterID = (int?)cboPrinters.SelectedValue
                };
                db.Categories.Add(cat);
                db.SaveChanges();
                LoadCats();
                ClearCatForm();
            }
        }

        private void BtnUpdateCat_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCat == null) return;
            using (var db = new AppDbContext())
            {
                var cat = db.Categories.Find(_selectedCat.CategoryID);
                if (cat != null)
                {
                    cat.CategoryName = txtCatName.Text;
                    cat.OrderIndex = int.TryParse(txtCatIndex.Text, out int idx) ? idx : 0;
                    cat.PrinterID = (int?)cboPrinters.SelectedValue;

                    db.SaveChanges();
                    LoadCats();
                    ClearCatForm();
                }
            }
        }

        private void BtnDeleteCat_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCat == null) return;

            if (MessageBox.Show("Xóa danh mục này sẽ xóa luôn các món bên trong. Tiếp tục?", "Cảnh báo", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var cat = db.Categories.Find(_selectedCat.CategoryID);
                    if (cat != null)
                    {
                        db.Categories.Remove(cat);
                        db.SaveChanges();
                        LoadCats();
                        LoadDishes(); // Reload cả món vì món bị xóa theo
                        ClearCatForm();
                    }
                }
            }
        }

        void ClearCatForm()
        {
            _selectedCat = null;
            txtCatName.Text = "";
            txtCatIndex.Text = "0";
            cboPrinters.SelectedValue = null;
            dgCats.SelectedItem = null;
        }


        // ==========================================
        // PHẦN 2: QUẢN LÝ MÓN ĂN (DISH)
        // ==========================================

        void LoadDishes()
        {
            using (var db = new AppDbContext())
            {
                // Include Category để hiển thị tên nhóm trong DataGrid
                dgDishes.ItemsSource = db.Dishes.Include(d => d.Category).ToList();
            }
        }

        private void dgDishes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgDishes.SelectedItem is Dish d)
            {
                _selectedDish = d;
                txtDishName.Text = d.DishName;
                txtPrice.Text = d.Price.ToString("0");
                txtUnit.Text = d.Unit;

                cboDishCat.SelectedValue = d.CategoryID;

                chkActive.IsChecked = d.DishStatus == "Active";

                // Load ảnh
                _currentImgPath = d.ImagePath;
                LoadImageToPreview(_currentImgPath);
            }
        }

        // --- XỬ LÝ UPLOAD ẢNH ---
        private void BtnUploadImg_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string destFolder = Path.Combine(AppContext.BaseDirectory, "Images");
                    if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                    // Tạo tên file ngẫu nhiên để tránh trùng
                    string ext = Path.GetExtension(dlg.FileName);
                    string newName = $"dish_{DateTime.Now.Ticks}{ext}";
                    string destPath = Path.Combine(destFolder, newName);

                    File.Copy(dlg.FileName, destPath, true);

                    _currentImgPath = newName;
                    LoadImageToPreview(newName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi upload: " + ex.Message);
                }
            }
        }

        void LoadImageToPreview(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Images", fileName);
                if (File.Exists(path))
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // Để không bị lock file
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    imgPreview.Source = bmp;
                }
                else
                {
                    imgPreview.Source = null;
                }
            }
            catch { imgPreview.Source = null; }
        }

        // --- CRUD MÓN ĂN ---

        private void BtnAddDish_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDishName.Text) || cboDishCat.SelectedValue == null)
            {
                MessageBox.Show("Vui lòng nhập tên món và chọn nhóm!");
                return;
            }

            using (var db = new AppDbContext())
            {
                var dish = new Dish
                {
                    DishName = txtDishName.Text,
                    Price = decimal.TryParse(txtPrice.Text, out decimal p) ? p : 0,
                    Unit = txtUnit.Text,
                    CategoryID = (int)cboDishCat.SelectedValue,
                    DishStatus = chkActive.IsChecked == true ? "Active" : "Inactive",
                    ImagePath = _currentImgPath
                };

                db.Dishes.Add(dish);
                db.SaveChanges();
                LoadDishes();
                ClearDishForm();
                MessageBox.Show("Đã thêm món mới!");
            }
        }

        private void BtnUpdateDish_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDish == null) return;
            using (var db = new AppDbContext())
            {
                var dish = db.Dishes.Find(_selectedDish.DishID);
                if (dish != null)
                {
                    dish.DishName = txtDishName.Text;
                    dish.Price = decimal.TryParse(txtPrice.Text, out decimal p) ? p : 0;
                    dish.Unit = txtUnit.Text;
                    dish.CategoryID = (int)cboDishCat.SelectedValue;
                    dish.DishStatus = chkActive.IsChecked == true ? "Active" : "Inactive";
                    dish.ImagePath = _currentImgPath;

                    db.SaveChanges();
                    LoadDishes();
                    ClearDishForm();
                    MessageBox.Show("Cập nhật món thành công!");
                }
            }
        }

        private void BtnDeleteDish_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDish == null) return;
            if (MessageBox.Show("Xóa món này?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var dish = db.Dishes.Find(_selectedDish.DishID);
                    if (dish != null)
                    {
                        db.Dishes.Remove(dish);
                        db.SaveChanges();
                        LoadDishes();
                        ClearDishForm();
                    }
                }
            }
        }

        private void BtnClearDish_Click(object sender, RoutedEventArgs e)
        {
            ClearDishForm();
        }

        void ClearDishForm()
        {
            _selectedDish = null;
            txtDishName.Text = "";
            txtPrice.Text = "0";
            txtUnit.Text = "";
            cboDishCat.SelectedIndex = -1;
            chkActive.IsChecked = true;
            _currentImgPath = "default.png";
            imgPreview.Source = null;
            dgDishes.SelectedItem = null;
        }
        // ==========================================
        // PHẦN 4: IMPORT/EXPORT EXCEL
        // ==========================================

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"DanhSachMon_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportDishesToExcel(saveDialog.FileName);
                    var window = Window.GetWindow(this);
                    if (window is MainWindow mainWindow)
                    {
                        mainWindow.ShowToast($"✅ Xuất Excel thành công: {Path.GetFileName(saveDialog.FileName)}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var (importedCount, errors) = ExcelService.ImportDishesFromExcel(openDialog.FileName);

                    if (importedCount > 0)
                    {
                        LoadDishes(); // Reload the dish list
                    }

                    // Show result message
                    string message = $"✅ Nhập thành công: {importedCount} món\n";
                    if (errors.Count > 0)
                    {
                        message += $"\n⚠️ {errors.Count} lỗi:\n";
                        message += string.Join("\n", errors.Take(10)); // Show first 10 errors
                        if (errors.Count > 10)
                        {
                            message += $"\n... và {errors.Count - 10} lỗi khác";
                        }
                    }

                    MessageBox.Show(message, "Kết quả nhập Excel", MessageBoxButton.OK,
                        errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi nhập Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
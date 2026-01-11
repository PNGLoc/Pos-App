using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;

namespace PosSystem.Main.Pages
{
    public partial class MenuSetupPage : UserControl
    {
        private Category? _selectedCat;
        private Dish? _selectedDish;
        private string _currentImgPath = "default.png";
        private System.Collections.Generic.List<Dish> _allDishes = new();

        public MenuSetupPage()
        {
            InitializeComponent();
            LoadCats();
            LoadDishes();
        }

        // ==========================================
        // 1. QUẢN LÝ DANH MỤC (CATEGORY)
        // ==========================================

        void LoadCats()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var list = db.Categories.Include(c => c.Printer).OrderBy(c => c.OrderIndex).ToList();
                    dgCats.ItemsSource = list;

                    // Load Data for ComboBoxes (printers & category selection for dishes)
                    var printers = db.Printers.Where(p => p.IsActive).ToList();
                    cboPrinters.ItemsSource = printers;
                    cboDishCat.ItemsSource = list;
                }
            }
            catch { }
        }

        private void BtnAddCat_Click(object sender, RoutedEventArgs e)
        {
            // Open Modal for Category
            _selectedCat = null;
            txtCatName.Text = "";
            txtCatIndex.Text = "0";
            cboPrinters.SelectedIndex = -1;

            ShowModal(isCategory: true);
            txtCatName.Focus();
        }

        private void BtnEditCatRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Category cat)
            {
                _selectedCat = cat;
                txtCatName.Text = cat.CategoryName;
                txtCatIndex.Text = cat.OrderIndex.ToString();
                cboPrinters.SelectedValue = cat.PrinterID;

                ShowModal(isCategory: true);
                txtCatName.Focus();
            }
        }

        private void BtnDeleteCatRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Category cat)
            {
                if (MessageBox.Show($"Xóa danh mục '{cat.CategoryName}' sẽ xóa TẤT CẢ món ăn thuộc danh mục này.\nBạn có chắc chắn không?", "Cảnh báo xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Categories.Find(cat.CategoryID);
                        if (item != null)
                        {
                            db.Categories.Remove(item);
                            db.SaveChanges();
                            LoadCats();
                            LoadDishes(); // Refresh dishes too
                        }
                    }
                }
            }
        }

        private void BtnSaveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtCatName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên danh mục!", "Thiếu thông tin");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (_selectedCat == null)
                {
                    // Add
                    var cat = new Category
                    {
                        CategoryName = txtCatName.Text,
                        OrderIndex = int.TryParse(txtCatIndex.Text, out int idx) ? idx : 0,
                        PrinterID = (int?)cboPrinters.SelectedValue
                    };
                    db.Categories.Add(cat);
                }
                else
                {
                    // Update
                    var item = db.Categories.Find(_selectedCat.CategoryID);
                    if (item != null)
                    {
                        item.CategoryName = txtCatName.Text;
                        item.OrderIndex = int.TryParse(txtCatIndex.Text, out int idx) ? idx : 0;
                        item.PrinterID = (int?)cboPrinters.SelectedValue;
                    }
                }
                db.SaveChanges();
            }

            CloseModal();
            LoadCats();
        }

        // ==========================================
        // 2. QUẢN LÝ MÓN ĂN (DISH)
        // ==========================================

        void LoadDishes()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    _allDishes = db.Dishes.Include(d => d.Category).ThenInclude(c => c.Printer).OrderBy(d => d.Category.OrderIndex).ThenBy(d => d.DishName).ToList();
                    dgDishes.ItemsSource = _allDishes;
                }
            }
            catch { }
        }

        private void TxtSearchDish_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allDishes == null) return;
            string keyword = txtSearchDish.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                dgDishes.ItemsSource = _allDishes;
            }
            else
            {
                string kwNoSign = RemoveDiacritics(keyword).ToLower();
                dgDishes.ItemsSource = _allDishes.Where(d => 
                    IsMatch(d.DishName, kwNoSign) || 
                    (d.Category != null && IsMatch(d.Category.CategoryName, kwNoSign))
                ).ToList();
            }
        }

        private bool IsMatch(string source, string keywordNoSign)
        {
            if (string.IsNullOrEmpty(source)) return false;
            string sourceNoSign = RemoveDiacritics(source).ToLower();
            
            // 1. Match contains (e.g. "ca phe" in "Cà phê sữa")
            if (sourceNoSign.Contains(keywordNoSign)) return true;

            // 2. Match initials (e.g. "cps" matches "Cà phê sữa")
            // Simple initials: First letter of each word
            string initials = string.Concat(sourceNoSign.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s[0]));
            if (initials.Contains(keywordNoSign)) return true;

            return false;
        }

        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();

            foreach (char c in normalizedString)
            {
                System.Globalization.UnicodeCategory unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace("đ", "d").Replace("Đ", "D");
        }

        private void BtnAddDish_Click(object sender, RoutedEventArgs e)
        {
            // Open Modal for Dish
            _selectedDish = null;
            txtDishName.Text = "";
            txtPrice.Text = "0";
            txtUnit.Text = "Phần";
            cboDishCat.SelectedIndex = 0;
            chkActive.IsChecked = true;
            _currentImgPath = "default.png";
            imgPreview.Source = null;

            ShowModal(isCategory: false);
            txtDishName.Focus();
        }

        private void BtnEditDishRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Dish d)
            {
                _selectedDish = d;
                txtDishName.Text = d.DishName;
                txtPrice.Text = d.Price.ToString("0");
                txtUnit.Text = d.Unit;
                cboDishCat.SelectedValue = d.CategoryID;
                chkActive.IsChecked = d.DishStatus == "Active";
                _currentImgPath = d.ImagePath;
                LoadImageToPreview(_currentImgPath);

                ShowModal(isCategory: false);
                txtDishName.Focus();
            }
        }

        private void BtnDeleteDishRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Dish d)
            {
                if (MessageBox.Show($"Xóa món '{d.DishName}'?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Dishes.Find(d.DishID);
                        if (item != null)
                        {
                            db.Dishes.Remove(item);
                            db.SaveChanges();
                            LoadDishes();
                        }
                    }
                }
            }
        }

        private void BtnSaveDish_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDishName.Text) || cboDishCat.SelectedValue == null)
            {
                MessageBox.Show("Vui lòng nhập tên món và chọn danh mục!");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (_selectedDish == null)
                {
                    // Add
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
                }
                else
                {
                    // Update
                    var item = db.Dishes.Find(_selectedDish.DishID);
                    if (item != null)
                    {
                        item.DishName = txtDishName.Text;
                        item.Price = decimal.TryParse(txtPrice.Text, out decimal p) ? p : 0;
                        item.Unit = txtUnit.Text;
                        item.CategoryID = (int)cboDishCat.SelectedValue;
                        item.DishStatus = chkActive.IsChecked == true ? "Active" : "Inactive";
                        item.ImagePath = _currentImgPath;
                    }
                }
                db.SaveChanges();
            }

            CloseModal();
            LoadDishes();
        }

        // ==========================================
        // 3. COMMON / MODAL / UTILS
        // ==========================================

        private void ShowModal(bool isCategory)
        {
            modalOverlay.Visibility = Visibility.Visible;
            if (isCategory)
            {
                panelCategoryForm.Visibility = Visibility.Visible;
                panelDishForm.Visibility = Visibility.Collapsed;
            }
            else
            {
                panelCategoryForm.Visibility = Visibility.Collapsed;
                panelDishForm.Visibility = Visibility.Visible;
            }
        }

        private void CloseModal()
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            CloseModal();
        }

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
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
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

        // ==========================================
        // 4. IMPORT / EXPORT EXCEL
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
                        mainWindow.ShowToast($"Xuất Excel thành công: {Path.GetFileName(saveDialog.FileName)}");
                    }
                    MessageBox.Show("Xuất file thành công!");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi: {ex.Message}");
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
                    if (importedCount > 0) LoadDishes();

                    string msg = $"Nhập thành công {importedCount} món.";
                    if (errors.Count > 0)
                    {
                        msg += $"\nCó {errors.Count} lỗi (xem chi tiết?)";
                    }
                    MessageBox.Show(msg);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi: {ex.Message}");
                }
            }
        }
    }
}
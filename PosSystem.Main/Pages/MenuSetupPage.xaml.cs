using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using PosSystem.Main.Helpers;
using PosSystem.Main.Server.Hubs;

namespace PosSystem.Main.Pages
{
    public partial class MenuSetupPage : UserControl
    {
        private Category? _selectedCat;
        private Dish? _selectedDish;
        private string _currentImgPath = "default.png";
        private System.Collections.Generic.List<Dish> _allDishes = new();

        private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("vi-VN");
        private bool _isFormattingPrice;

        public MenuSetupPage()
        {
            InitializeComponent();
            LoadCats();
            LoadDishes();

            DataObject.AddPastingHandler(txtPrice, TxtPrice_Pasting);
        }

        private void NotifyMenuUpdated()
        {
            try
            {
                if (App.WebHost == null) return;
                var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                if (hubContext != null)
                {
                    _ = hubContext.Clients.All.SendAsync("MenuUpdated");
                }
            }
            catch { }
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

                    // [NEW] Load for Filter ComboBox
                    var filterList = new System.Collections.Generic.List<Category>();
                    filterList.Add(new Category { CategoryID = 0, CategoryName = "--- Tất cả danh mục ---" });
                    filterList.AddRange(list);
                    cboFilterCategory.ItemsSource = filterList;
                    cboFilterCategory.SelectedIndex = 0; // Select "All" by default
                }
            }
            catch { }
        }

        private void BtnAddCat_Click(object sender, RoutedEventArgs e)
        {
            // Open Modal for Category
            _selectedCat = null;
            txtCatName.Text = "";

            // [MODIFIED] Auto-increment Order Index
            try
            {
                using (var db = new AppDbContext())
                {
                    var maxIdx = db.Categories.Any() ? db.Categories.Max(c => c.OrderIndex) : 0;
                    txtCatIndex.Text = (maxIdx + 1).ToString();
                }
            }
            catch
            {
                txtCatIndex.Text = "1";
            }

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
                            NotifyMenuUpdated();
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
                    // Validate Duplicate (Case Insensitive)
                    if (db.Categories.ToList().Any(c => c.CategoryName.ToLower() == txtCatName.Text.ToLower()))
                    {
                        MessageBox.Show("Tên danh mục đã tồn tại!");
                        return;
                    }

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
                        // Validate Duplicate (Case Insensitive)
                        if (item.CategoryName.ToLower() != txtCatName.Text.ToLower())
                        {
                            if (db.Categories.ToList().Any(c => c.CategoryName.ToLower() == txtCatName.Text.ToLower()))
                            {
                                MessageBox.Show("Tên danh mục đã tồn tại!");
                                return;
                            }
                        }

                        item.CategoryName = txtCatName.Text;
                        item.OrderIndex = int.TryParse(txtCatIndex.Text, out int idx) ? idx : 0;
                        item.PrinterID = (int?)cboPrinters.SelectedValue;
                    }
                }
                db.SaveChanges();
            }

            CloseModal();
            LoadCats();
            NotifyMenuUpdated();
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
                    FilterDishes();
                }
            }
            catch { }
        }

        private void TxtSearchDish_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterDishes();
        }

        private void CboFilterCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterDishes();
        }

        private void FilterDishes()
        {
            if (_allDishes == null) return;

            string keyword = txtSearchDish.Text?.Trim() ?? "";
            string kwNoSign = RemoveDiacritics(keyword).ToLower();
            int selectedCatId = (int?)cboFilterCategory.SelectedValue ?? 0;

            var filtered = _allDishes.AsEnumerable();

            // Filter by Category
            if (selectedCatId > 0)
            {
                filtered = filtered.Where(d => d.CategoryID == selectedCatId);
            }

            // Filter by Keyword
            if (!string.IsNullOrEmpty(keyword))
            {
                filtered = filtered.Where(d =>
                    IsMatch(d.DishName, kwNoSign) ||
                    (d.Category != null && IsMatch(d.Category.CategoryName, kwNoSign))
                );
            }

            dgDishes.ItemsSource = filtered.ToList();
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
                txtPrice.Text = d.Price.ToString("N0", PriceCulture);
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
                            NotifyMenuUpdated();
                        }
                    }
                }
            }
        }

        private void BtnDeleteAllDishes_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn xóa TẤT CẢ món ăn?\nHành động này không thể hoàn tác!", "Cảnh báo dữ liệu", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        db.Dishes.RemoveRange(db.Dishes);
                        db.SaveChanges();
                    }
                    LoadDishes();
                    NotifyMenuUpdated();
                    MessageBox.Show("Đã xóa toàn bộ món ăn thành công.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xóa dữ liệu: {ex.Message}");
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

            var parsedPrice = ParsePriceFromText(txtPrice.Text);

            using (var db = new AppDbContext())
            {
                if (_selectedDish == null)
                {
                    // Add
                    // Validate Duplicate Name (Case Insensitive)
                    if (db.Dishes.ToList().Any(d => d.DishName.ToLower() == txtDishName.Text.ToLower()))
                    {
                        MessageBox.Show("Tên món ăn đã tồn tại!");
                        return;
                    }

                    var dish = new Dish
                    {
                        DishName = txtDishName.Text,
                        Price = parsedPrice,
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
                        // Validate Duplicate Name (Case Insensitive)
                        if (item.DishName.ToLower() != txtDishName.Text.ToLower())
                        {
                            if (db.Dishes.ToList().Any(d => d.DishName.ToLower() == txtDishName.Text.ToLower()))
                            {
                                MessageBox.Show("Tên món ăn đã tồn tại!");
                                return;
                            }
                        }

                        item.DishName = txtDishName.Text;
                        item.Price = parsedPrice;
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
            NotifyMenuUpdated();
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
                    AppPaths.EnsureInitialized();
                    string destFolder = AppPaths.ImagesDir;

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

        private void TxtPrice_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void TxtPrice_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
            {
                e.CancelCommand();
                return;
            }

            var pasteText = e.SourceDataObject.GetData(DataFormats.Text, true) as string;
            if (string.IsNullOrEmpty(pasteText)) return;

            // Allow pasting formatted text like "1.000"; it will be normalized by TextChanged.
            if (pasteText.Any(ch => !char.IsDigit(ch) && ch != '.' && ch != ',' && !char.IsWhiteSpace(ch)))
            {
                e.CancelCommand();
            }
        }

        private void TxtPrice_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isFormattingPrice) return;
            if (sender is not TextBox tb) return;

            var oldText = tb.Text ?? string.Empty;
            var caretIndex = tb.CaretIndex;

            var digitsBeforeCaret = oldText.Take(Math.Max(0, Math.Min(caretIndex, oldText.Length))).Count(char.IsDigit);
            var digits = new string(oldText.Where(char.IsDigit).ToArray());

            if (digits.Length == 0)
            {
                return;
            }

            if (!decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return;
            }

            var formatted = value.ToString("N0", PriceCulture);
            if (string.Equals(formatted, oldText, StringComparison.Ordinal))
            {
                return;
            }

            _isFormattingPrice = true;
            tb.Text = formatted;

            tb.CaretIndex = GetCaretIndexByDigitCount(formatted, digitsBeforeCaret);
            _isFormattingPrice = false;
        }

        private void TxtPrice_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var digits = new string((tb.Text ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                tb.Text = "0";
                return;
            }

            if (decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                tb.Text = value.ToString("N0", PriceCulture);
            }
        }

        private static int GetCaretIndexByDigitCount(string formattedText, int digitsBeforeCaret)
        {
            if (digitsBeforeCaret <= 0) return 0;

            var digitsSeen = 0;
            for (var i = 0; i < formattedText.Length; i++)
            {
                if (char.IsDigit(formattedText[i]))
                {
                    digitsSeen++;
                    if (digitsSeen >= digitsBeforeCaret)
                    {
                        return i + 1;
                    }
                }
            }

            return formattedText.Length;
        }

        private static decimal ParsePriceFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return 0;

            return decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        void LoadImageToPreview(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            try
            {
                AppPaths.EnsureInitialized();
                string path = Path.Combine(AppPaths.ImagesDir, fileName);
                if (!File.Exists(path))
                {
                    // legacy plural folder fallback
                    path = Path.Combine(AppPaths.ImagesDirLegacyPlural, fileName);
                }

                if (!File.Exists(path))
                {
                    // legacy fallback
                    path = Path.Combine(AppContext.BaseDirectory, "Images", fileName);
                }

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
                    var (addedCount, unchangedCount, errors) = ExcelService.ImportDishesFromExcel(openDialog.FileName);
                    if (addedCount > 0)
                    {
                        LoadDishes();
                        LoadCats(); // [NEW] Refresh categories because import might have created new ones
                        NotifyMenuUpdated();
                    }

                    string msg = $"Import xong: {addedCount} món mới – {unchangedCount} món không thay đổi";
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
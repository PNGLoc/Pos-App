using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using System.IO;
using System.Windows.Media;
using System.ComponentModel; // Cần cho Real-time update
using System.Runtime.CompilerServices;

namespace PosSystem.Main.Pages
{
    // ViewModel hỗ trợ cập nhật tức thì (Real-time)
    public class PrintElementViewModel : PrintElement, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Ghi đè các thuộc tính để báo hiệu thay đổi
        public new string ElementType
        {
            get => base.ElementType;
            set { if (base.ElementType != value) { base.ElementType = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayPreview)); OnPropertyChanged(nameof(IsTextVisible)); OnPropertyChanged(nameof(ElementTypeDisplay)); } }
        }

        public new string Content
        {
            get => base.Content;
            set { if (base.Content != value) { base.Content = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayPreview)); } }
        }

        public new int FontSize
        {
            get => base.FontSize;
            set { if (base.FontSize != value) { base.FontSize = value; OnPropertyChanged(); } }
        }

        public new string Align
        {
            get => base.Align;
            set
            {
                if (base.Align != value)
                {
                    base.Align = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TextAlignmentDisplay));
                    OnPropertyChanged(nameof(HorizontalAlignDisplay));
                }
            }
        }

        public new bool IsBold
        {
            get => base.IsBold;
            set { if (base.IsBold != value) { base.IsBold = value; OnPropertyChanged(); OnPropertyChanged(nameof(FontWeightDisplay)); } }
        }

        public new bool IsVisible
        {
            get => base.IsVisible;
            set { if (base.IsVisible != value) { base.IsVisible = value; OnPropertyChanged(); } }
        }

        // Logic hiển thị
        public Visibility IsTextVisible => (ElementType != "Separator" && ElementType != "Logo" && ElementType != "QRCode") ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSeparatorVisible => ElementType == "Separator" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsImageVisible => (ElementType == "Logo" || ElementType == "QRCode") ? Visibility.Visible : Visibility.Collapsed;

        public FontWeight FontWeightDisplay => IsBold ? FontWeights.Bold : FontWeights.Normal;

        public TextAlignment TextAlignmentDisplay
        {
            get
            {
                if (Align == "Center") return TextAlignment.Center;
                if (Align == "Right") return TextAlignment.Right;
                return TextAlignment.Left;
            }
        }

        public HorizontalAlignment HorizontalAlignDisplay
        {
            get
            {
                if (Align == "Center") return HorizontalAlignment.Center;
                if (Align == "Right") return HorizontalAlignment.Right;
                return HorizontalAlignment.Left;
            }
        }

        public string ElementTypeDisplay
        {
            get
            {
                switch (ElementType)
                {
                    case "OrderDetails": return "[DANH SÁCH MÓN]";
                    case "KitchenOrderDetails": return "[BẾP: MÓN]";
                    case "Total": return "[TỔNG TIỀN]";
                    case "BatchNumber": return "[SỐ ĐỢT]";
                    case "Text": return "[VĂN BẢN]";
                    case "Separator": return "[KẺ NGANG]";
                    case "Logo": return "[LOGO]";
                    default: return ElementType;
                }
            }
        }

        // --- QUAN TRỌNG: DỮ LIỆU MẪU ĐỂ KHÔNG BỊ TRẮNG ---
        public string DisplayPreview
        {
            get
            {
                if (ElementType == "Text")
                {
                    string s = Content ?? "";
                    return s.Replace("{Table}", "10").Replace("{Staff}", "Admin")
                            .Replace("{CheckInTime}", "09:30").Replace("{PrintTime}", DateTime.Now.ToString("HH:mm"))
                            .Replace("{Duration}", "45p").Replace("{Batch}", "1").Replace("{OrderId}", "1023");
                }

                // Dữ liệu mẫu cho List Món
                if (ElementType == "OrderDetails" || ElementType == "KitchenOrderDetails")
                {
                    string s = "Cà phê sữa đá          2        50,000";
                    if (ShowNote) s += "\n   (Ít ngọt)";
                    s += "\nSinh tố bơ             1        40,000\n----------------------------------------";
                    return s;
                }

                // Dữ liệu mẫu cho Tổng tiền
                if (ElementType == "Total")
                {
                    string s = "";
                    if (ShowSubTotal) s += "Tạm tính:                 90,000\n";
                    if (ShowDiscount) s += "Giảm giá:                      0\n";
                    s += "TỔNG CỘNG:                90,000";
                    return s;
                }

                if (ElementType == "BatchNumber") return "ĐỢT: 1";
                return Content; // Fallback
            }
        }

        // Helpers đọc cấu hình
        private string GetConfig(string key)
        {
            if (string.IsNullOrEmpty(Content)) return "";
            var parts = Content.Split(';');
            foreach (var p in parts) { var kv = p.Split('='); if (kv.Length == 2 && kv[0] == key) return kv[1]; }
            return "";
        }
        public bool ShowNote => GetConfig("ShowNote") != "False";
        public bool ShowSubTotal => GetConfig("ShowSub") == "True";
        public bool ShowDiscount => GetConfig("ShowDisc") == "True";
        public int NoteFontSize { get { if (int.TryParse(GetConfig("NoteSize"), out int s)) return s; return Math.Max(10, FontSize - 2); } }
        public int SubFontSize { get { if (int.TryParse(GetConfig("SubSize"), out int s)) return s; return Math.Max(12, FontSize - 2); } }
        public int ItemFontSize { get { if (int.TryParse(GetConfig("ItemSize"), out int s)) return s; return 14; } }
        public PrintElementViewModel(PrintElement origin)
        {
            // Copy dữ liệu từ Model sang ViewModel
            this.ElementType = origin.ElementType;
            this.Content = origin.Content;
            this.FontSize = origin.FontSize;
            this.IsBold = origin.IsBold;
            this.Align = origin.Align;
            this.IsVisible = origin.IsVisible;
        }

        public PrintElement ToModel()
        {
            return new PrintElement { ElementType = this.ElementType, Content = this.Content, FontSize = this.FontSize, IsBold = this.IsBold, Align = this.Align, IsVisible = this.IsVisible };
        }
    }

    public partial class LayoutDesignerPage : UserControl
    {
        public static readonly DependencyProperty TemplateTypeProperty =
            DependencyProperty.Register(nameof(TemplateType), typeof(string), typeof(LayoutDesignerPage),
                new PropertyMetadata("Bill", OnTemplateTypeChanged));

        public string TemplateType
        {
            get => (string)GetValue(TemplateTypeProperty);
            set => SetValue(TemplateTypeProperty, value);
        }

        private List<PrintElementViewModel> _elements = new List<PrintElementViewModel>();
        private PrintElementViewModel? _selectedElement;
        private bool _isInternalUpdate = false;

        public LayoutDesignerPage()
        {
            InitializeComponent();
            this.Loaded += LayoutDesignerPage_Loaded;
        }

        private void LayoutDesignerPage_Loaded(object sender, RoutedEventArgs e) => LoadLayout();

        private static void OnTemplateTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LayoutDesignerPage page && page.IsLoaded) page.LoadLayout();
        }

        private void LoadLayout()
        {
            string templateType = TemplateType ?? "Bill";
            List<PrintElement> rawElements;

            using (var db = new AppDbContext())
            {
                var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == templateType && t.IsActive);
                if (template != null && !string.IsNullOrEmpty(template.TemplateContentJson))
                {
                    try { rawElements = JsonSerializer.Deserialize<List<PrintElement>>(template.TemplateContentJson) ?? CreateDefaultLayout(); }
                    catch { rawElements = CreateDefaultLayout(); }
                }
                else rawElements = CreateDefaultLayout();
            }

            _elements = rawElements.Select(e => new PrintElementViewModel(e)).ToList();
            if (txtTitle != null) txtTitle.Text = TemplateType == "Kitchen" ? "THIẾT KẾ PHIẾU BẾP" : "THIẾT KẾ HÓA ĐƠN";
            FilterToolbox();
            RefreshList(-1);
        }

        private void FilterToolbox()
        {
            if (pnlToolbox == null) return;
            foreach (var child in pnlToolbox.Children)
            {
                if (child is Button btn && btn.Tag is string tag)
                {
                    bool isKitchenItem = tag.Contains("Kitchen") || tag.Contains("Batch");
                    bool isBillItem = tag == "OrderDetails" || tag == "Total";

                    if (TemplateType == "Kitchen") btn.Visibility = isBillItem ? Visibility.Collapsed : Visibility.Visible;
                    else btn.Visibility = isKitchenItem ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        private List<PrintElement> CreateDefaultLayout()
        {
            if (TemplateType == "Kitchen")
            {
                return new List<PrintElement>
                {
                    new PrintElement { ElementType = "Text", Content = "PHIẾU BẾP - ĐỢT {Batch}", FontSize = 20, IsBold = true, Align = "Center" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "KitchenOrderDetails" }
                };
            }
            else
            {
                return new List<PrintElement>
                {
                    new PrintElement { ElementType = "Text", Content = "TÊN QUÁN CAFE", FontSize = 24, IsBold = true, Align = "Center" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "OrderDetails" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "Total" }
                };
            }
        }

        private void RefreshList(int selectIndex)
        {
            if (lstElements == null) return;
            lstElements.ItemsSource = null;
            lstElements.ItemsSource = _elements;
            if (selectIndex >= 0 && selectIndex < _elements.Count) lstElements.SelectedIndex = selectIndex;
        }

        private void BtnToolbox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                var newEl = new PrintElement { ElementType = "Text", IsVisible = true, Align = "Left", FontSize = 14, Content = "Nội dung..." };

                switch (tag)
                {
                    case "Text": newEl.Content = "Nhập văn bản..."; break;
                    case "Separator": newEl.ElementType = "Separator"; newEl.Content = "----------------"; break;

                    case "TextTable": newEl.Content = "Bàn: {Table}"; newEl.FontSize = 16; newEl.IsBold = true; break;
                    case "TextStaff": newEl.Content = "NV: {Staff}"; newEl.FontSize = 12; break;
                    case "TextTime": newEl.Content = "Vào: {CheckInTime} | In: {PrintTime}"; newEl.Align = "Center"; newEl.FontSize = 12; break;
                    case "TextDuration": newEl.Content = "Thời gian: {Duration}"; newEl.Align = "Center"; newEl.FontSize = 12; break;

                    case "BatchNumber": newEl.Content = "ĐỢT GỌI: {Batch}"; newEl.FontSize = 18; newEl.IsBold = true; newEl.Align = "Center"; break;

                    case "Logo":
                    case "QRCode":
                    case "OrderDetails":
                    case "KitchenOrderDetails":
                    case "Total":
                        newEl.ElementType = tag;
                        newEl.Content = tag; // Gán content để DisplayPreview hoạt động
                        break;
                }

                _elements.Add(new PrintElementViewModel(newEl));
                RefreshList(_elements.Count - 1);
            }
        }

        private void LstElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstElements.SelectedItem is PrintElementViewModel el)
            {
                _selectedElement = el;
                _isInternalUpdate = true;
                if (pnlProperties != null) pnlProperties.IsEnabled = true;
                if (lblSelectedType != null) lblSelectedType.Text = el.ElementTypeDisplay;

                if (txtContent != null) txtContent.Text = el.Content;
                if (cboAlign != null) cboAlign.SelectedIndex = el.Align == "Left" ? 0 : (el.Align == "Right" ? 2 : 1);
                if (txtFontSize != null) txtFontSize.Text = el.FontSize.ToString();
                if (chkBold != null) chkBold.IsChecked = el.IsBold;
                if (chkVisible != null) chkVisible.IsChecked = el.IsVisible;

                if (pnlTextProp != null) pnlTextProp.Visibility = Visibility.Collapsed;
                if (pnlImageProp != null) pnlImageProp.Visibility = Visibility.Collapsed;
                if (pnlOptionProp != null) pnlOptionProp.Visibility = Visibility.Collapsed;

                if (el.ElementType == "Text")
                {
                    if (pnlTextProp != null) pnlTextProp.Visibility = Visibility.Visible;
                    if (txtContent != null) txtContent.IsEnabled = true;
                }
                else if (el.ElementType == "Logo" || el.ElementType == "QRCode")
                {
                    if (pnlImageProp != null) pnlImageProp.Visibility = Visibility.Visible;
                    LoadImagePreview(el.Content);
                }
                else
                {
                    // Các khối dữ liệu
                    if (pnlOptionProp != null)
                    {
                        if (el.ElementType == "OrderDetails" || el.ElementType == "KitchenOrderDetails")
                        {
                            pnlOptionProp.Visibility = Visibility.Visible;
                            if (optOrderDetails != null) optOrderDetails.Visibility = Visibility.Visible;
                            if (optTotal != null) optTotal.Visibility = Visibility.Collapsed;
                            if (txtItemSize != null) txtItemSize.Text = el.ItemFontSize.ToString();
                            if (chkShowNote != null) chkShowNote.IsChecked = el.ShowNote;
                            if (txtNoteSize != null) txtNoteSize.Text = el.NoteFontSize.ToString();
                        }
                        else if (el.ElementType == "Total")
                        {
                            pnlOptionProp.Visibility = Visibility.Visible;
                            if (optOrderDetails != null) optOrderDetails.Visibility = Visibility.Collapsed;
                            if (optTotal != null) optTotal.Visibility = Visibility.Visible;
                            if (chkShowSubTotal != null) chkShowSubTotal.IsChecked = el.ShowSubTotal;
                            if (chkShowDiscount != null) chkShowDiscount.IsChecked = el.ShowDiscount;
                            if (txtSubSize != null) txtSubSize.Text = el.SubFontSize.ToString();
                        }
                    }
                }
                _isInternalUpdate = false;
            }
            else
            {
                _selectedElement = null;
                if (pnlProperties != null) pnlProperties.IsEnabled = false;
                if (lblSelectedType != null) lblSelectedType.Text = "None";
            }
        }

        private void UpdateModelFromUI()
        {
            if (_isInternalUpdate || _selectedElement == null) return;

            // ViewModel tự động Notify, không cần Refresh() list nữa
            if (txtContent != null) _selectedElement.Content = txtContent.Text;
            if (cboAlign != null) _selectedElement.Align = cboAlign.SelectedIndex == 0 ? "Left" : (cboAlign.SelectedIndex == 2 ? "Right" : "Center");
            if (txtFontSize != null && int.TryParse(txtFontSize.Text, out int size)) _selectedElement.FontSize = size;
            if (chkBold != null) _selectedElement.IsBold = chkBold.IsChecked == true;
            if (chkVisible != null) _selectedElement.IsVisible = chkVisible.IsChecked == true;

            // Lưu cấu hình vào Content
            List<string> configs = new List<string>();
            if (_selectedElement.ElementType == "OrderDetails" || _selectedElement.ElementType == "KitchenOrderDetails")
            {
                if (txtItemSize != null && int.TryParse(txtItemSize.Text, out int iSize)) configs.Add($"ItemSize={iSize}");
                if (chkShowNote != null && chkShowNote.IsChecked == false) configs.Add("ShowNote=False");
                if (txtNoteSize != null && int.TryParse(txtNoteSize.Text, out int nSize)) configs.Add($"NoteSize={nSize}");
                _selectedElement.Content = string.Join(";", configs);
            }
            else if (_selectedElement.ElementType == "Total")
            {
                if (chkShowSubTotal != null && chkShowSubTotal.IsChecked == true) configs.Add("ShowSub=True");
                if (chkShowDiscount != null && chkShowDiscount.IsChecked == true) configs.Add("ShowDisc=True");
                if (txtSubSize != null && int.TryParse(txtSubSize.Text, out int sSize)) configs.Add($"SubSize={sSize}");
                _selectedElement.Content = string.Join(";", configs);
            }
        }

        // Các hàm xử lý sự kiện khác (BtnDelete, BtnUp, BtnDown, UploadImage, LoadImage, Save)
        // Giữ nguyên như cũ...
        private void TxtContent_TextChanged(object sender, TextChangedEventArgs e) => UpdateModelFromUI();
        private void Prop_Changed(object sender, RoutedEventArgs e) => UpdateModelFromUI();
        private void Prop_Changed(object sender, SelectionChangedEventArgs e) => UpdateModelFromUI();

        private void BtnDelete_Click(object sender, RoutedEventArgs e) { if (_selectedElement != null) { int idx = lstElements.SelectedIndex; _elements.Remove(_selectedElement); RefreshList(idx >= _elements.Count ? _elements.Count - 1 : idx); } }
        private void BtnUp_Click(object sender, RoutedEventArgs e) { int idx = lstElements.SelectedIndex; if (idx > 0) { var item = _elements[idx]; _elements.RemoveAt(idx); _elements.Insert(idx - 1, item); RefreshList(idx - 1); } }
        private void BtnDown_Click(object sender, RoutedEventArgs e) { int idx = lstElements.SelectedIndex; if (idx >= 0 && idx < _elements.Count - 1) { var item = _elements[idx]; _elements.RemoveAt(idx); _elements.Insert(idx + 1, item); RefreshList(idx + 1); } }
        private void BtnUploadImage_Click(object sender, RoutedEventArgs e) { if (_selectedElement == null) return; OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp" }; if (dlg.ShowDialog() == true) { try { string destFolder = Path.Combine(AppContext.BaseDirectory, "Images"); if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder); string ext = Path.GetExtension(dlg.FileName); string newName = $"img_{DateTime.Now.Ticks}{ext}"; File.Copy(dlg.FileName, Path.Combine(destFolder, newName), true); _selectedElement.Content = newName; LoadImagePreview(newName); UpdateModelFromUI(); } catch (Exception ex) { MessageBox.Show("Lỗi: " + ex.Message); } } }
        void LoadImagePreview(string fileName) { try { string path = Path.Combine(AppContext.BaseDirectory, "Images", fileName); if (File.Exists(path)) imgPreview.Source = new BitmapImage(new Uri(path)); else imgPreview.Source = null; } catch { if (imgPreview != null) imgPreview.Source = null; } }
        private void BtnSave_Click(object sender, RoutedEventArgs e) { string templateType = TemplateType ?? "Bill"; using (var db = new AppDbContext()) { var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == templateType && t.IsActive); if (template == null) { template = new PrintTemplate { TemplateName = templateType, TemplateType = templateType, IsActive = true }; db.PrintTemplates.Add(template); } var rawList = _elements.Select(vm => vm.ToModel()).ToList(); template.TemplateContentJson = JsonSerializer.Serialize(rawList); db.SaveChanges(); MessageBox.Show("✅ Đã lưu cấu trúc thành công!"); } }
    }
}
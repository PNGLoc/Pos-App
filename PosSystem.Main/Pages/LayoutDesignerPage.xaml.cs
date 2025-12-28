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

namespace PosSystem.Main.Pages
{
    public class PrintElementViewModel : PrintElement
    {
        // 1. LOGIC ẨN HIỆN TRÊN CANVAS
        // Coi OrderDetails, Total... là Text để hiển thị dữ liệu mẫu (Preview)
        public bool ShowNote => !Content.Contains("ShowNote=False"); // Mặc định là True (Hiện note)

        public bool ShowSubTotal => Content.Contains("ShowSub=True");
        public bool ShowDiscount => Content.Contains("ShowDisc=True");
        public Visibility IsTextVisible => (ElementType != "Separator" && ElementType != "Logo" && ElementType != "QRCode") ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSeparatorVisible => ElementType == "Separator" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsImageVisible => (ElementType == "Logo" || ElementType == "QRCode") ? Visibility.Visible : Visibility.Collapsed;

        // Ẩn hoàn toàn hộp vàng cũ (vì giờ đã có dữ liệu mẫu đẹp hơn)
        public Visibility IsDynamicDataVisible => Visibility.Collapsed;

        // 2. LOGIC HIỂN THỊ FONT & CĂN LỀ
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

        // --- SỬA LỖI CS1061: THÊM LẠI THUỘC TÍNH NÀY ---
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
                    case "QRCode": return "[QR CODE]";
                    default: return ElementType;
                }
            }
        }

        // 3. TẠO DỮ LIỆU MẪU (PREVIEW DATA)
        public string DisplayPreview
        {
            get
            {
                if (ElementType == "Text")
                {
                    string s = Content ?? "";
                    s = s.Replace("{Table}", "10")
                         .Replace("{Staff}", "Admin")
                         .Replace("{CheckInTime}", "09:30")
                         .Replace("{PrintTime}", DateTime.Now.ToString("HH:mm"))
                         .Replace("{PrintDate}", DateTime.Now.ToString("dd/MM/yyyy"))
                         .Replace("{Duration}", "45p")
                         .Replace("{Batch}", "1")
                         .Replace("{TableType}", "Tại quán")
                         .Replace("{OrderId}", "10023");
                    return s;
                }

                if (ElementType == "OrderDetails" || ElementType == "KitchenOrderDetails")
                {
                    return "Cà phê sữa đá        2      50,000\n   (Ít ngọt)\nSinh tố bơ           1      40,000\n----------------------------------";
                }

                if (ElementType == "Total")
                {
                    return "Tạm tính:               90,000\nGiảm giá:                    0\nTỔNG CỘNG:              90,000";
                }

                if (ElementType == "BatchNumber") return "ĐỢT: 1";

                return Content; // Mặc định cho Logo, Separator
            }
        }

        public PrintElementViewModel(PrintElement origin)
        {
            this.ElementType = origin.ElementType;
            this.Content = origin.Content;
            this.FontSize = origin.FontSize;
            this.IsBold = origin.IsBold;
            this.Align = origin.Align;
            this.IsVisible = origin.IsVisible;
        }

        public PrintElement ToModel()
        {
            return new PrintElement
            {
                ElementType = this.ElementType,
                Content = this.Content,
                FontSize = this.FontSize,
                IsBold = this.IsBold,
                Align = this.Align,
                IsVisible = this.IsVisible
            };
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
                else
                {
                    rawElements = CreateDefaultLayout();
                }
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

                    if (TemplateType == "Kitchen")
                    {
                        if (isBillItem) btn.Visibility = Visibility.Collapsed;
                        else btn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        if (isKitchenItem) btn.Visibility = Visibility.Collapsed;
                        else btn.Visibility = Visibility.Visible;
                    }
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
                    new PrintElement { ElementType = "Text", Content = "{Table}", FontSize = 50, IsBold = true, Align = "Center" },
                    new PrintElement { ElementType = "Text", Content = "Vào: {CheckInTime} | In: {PrintTime}", FontSize = 12, Align = "Center" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "KitchenOrderDetails" }
                };
            }
            else
            {
                return new List<PrintElement>
                {
                    new PrintElement { ElementType = "Text", Content = "TÊN QUÁN CAFE", FontSize = 24, IsBold = true, Align = "Center" },
                    new PrintElement { ElementType = "Text", Content = "ĐC: 123 Đường ABC, TP.XYZ", FontSize = 12, Align = "Center" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "Text", Content = "Bàn: {Table}   #{OrderId}", FontSize = 14, IsBold = true },
                    new PrintElement { ElementType = "Text", Content = "NV: {Staff}   Ngày: {PrintDate}", FontSize = 12 },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "OrderDetails" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "Total" },
                    new PrintElement { ElementType = "Text", Content = "Xin cảm ơn quý khách!", FontSize = 14, Align = "Center" }
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

                    // Nút Số bàn Khổng Lồ (Vẫn xử lý như Text nhưng font to)
                    case "TableNumberBig":
                        newEl.Content = "{Table}";
                        newEl.FontSize = 60;
                        newEl.IsBold = true;
                        newEl.Align = "Center";
                        break;

                    case "Logo":
                    case "QRCode":
                    case "OrderDetails":
                    case "KitchenOrderDetails":
                    case "Total":
                        newEl.ElementType = tag;
                        newEl.Content = tag;
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

                // Hiển thị tên loại phần tử (đã fix lỗi CS1061)
                if (lblSelectedType != null) lblSelectedType.Text = el.ElementTypeDisplay;

                // Load các thuộc tính chung
                if (txtContent != null) txtContent.Text = el.Content;
                if (cboAlign != null) cboAlign.SelectedIndex = el.Align == "Left" ? 0 : (el.Align == "Right" ? 2 : 1);
                if (txtFontSize != null) txtFontSize.Text = el.FontSize.ToString();
                if (chkBold != null) chkBold.IsChecked = el.IsBold;
                if (chkVisible != null) chkVisible.IsChecked = el.IsVisible;

                // Reset trạng thái hiển thị các Panel
                if (pnlTextProp != null) pnlTextProp.Visibility = Visibility.Collapsed;
                if (pnlImageProp != null) pnlImageProp.Visibility = Visibility.Collapsed;
                if (pnlOptionProp != null) pnlOptionProp.Visibility = Visibility.Collapsed;

                // --- XỬ LÝ HIỂN THỊ THEO TỪNG LOẠI ---
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
                    // Các khối dữ liệu (OrderDetails, Total...) -> Ẩn ô nhập Text, Hiện Option
                    if (pnlTextProp != null) pnlTextProp.Visibility = Visibility.Collapsed;

                    // Logic hiển thị Panel Tùy chọn nâng cao
                    if (pnlOptionProp != null)
                    {
                        if (el.ElementType == "OrderDetails" || el.ElementType == "KitchenOrderDetails")
                        {
                            pnlOptionProp.Visibility = Visibility.Visible;

                            // Hiện nhóm Note, Ẩn nhóm Tiền
                            if (optOrderDetails != null) optOrderDetails.Visibility = Visibility.Visible;
                            if (optTotal != null) optTotal.Visibility = Visibility.Collapsed;

                            // Load giá trị từ Model lên Checkbox
                            if (chkShowNote != null) chkShowNote.IsChecked = el.ShowNote;
                        }
                        else if (el.ElementType == "Total")
                        {
                            pnlOptionProp.Visibility = Visibility.Visible;

                            // Ẩn nhóm Note, Hiện nhóm Tiền
                            if (optOrderDetails != null) optOrderDetails.Visibility = Visibility.Collapsed;
                            if (optTotal != null) optTotal.Visibility = Visibility.Visible;

                            // Load giá trị từ Model lên Checkbox
                            if (chkShowSubTotal != null) chkShowSubTotal.IsChecked = el.ShowSubTotal;
                            if (chkShowDiscount != null) chkShowDiscount.IsChecked = el.ShowDiscount;
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

            // Cập nhật các thuộc tính cơ bản
            if (cboAlign != null) _selectedElement.Align = cboAlign.SelectedIndex == 0 ? "Left" : (cboAlign.SelectedIndex == 2 ? "Right" : "Center");
            if (txtFontSize != null && int.TryParse(txtFontSize.Text, out int size)) _selectedElement.FontSize = size;
            if (chkBold != null) _selectedElement.IsBold = chkBold.IsChecked == true;
            if (chkVisible != null) _selectedElement.IsVisible = chkVisible.IsChecked == true;

            // --- CẬP NHẬT CONTENT (NỘI DUNG HOẶC CẤU HÌNH) ---
            if (_selectedElement.ElementType == "Text")
            {
                // Với Text: Content là nội dung văn bản
                if (txtContent != null) _selectedElement.Content = txtContent.Text;
            }
            else if (_selectedElement.ElementType == "OrderDetails" || _selectedElement.ElementType == "KitchenOrderDetails")
            {
                // Với List món: Content lưu cấu hình ShowNote
                // Nếu bỏ tick Note -> Lưu "ShowNote=False", ngược lại lưu rỗng
                if (chkShowNote != null)
                    _selectedElement.Content = (chkShowNote.IsChecked == false) ? "ShowNote=False" : "";
            }
            else if (_selectedElement.ElementType == "Total")
            {
                // Với Total: Content lưu cấu hình ShowSubTotal, ShowDiscount
                List<string> configs = new List<string>();

                if (chkShowSubTotal != null && chkShowSubTotal.IsChecked == true)
                    configs.Add("ShowSub=True");

                if (chkShowDiscount != null && chkShowDiscount.IsChecked == true)
                    configs.Add("ShowDisc=True");

                _selectedElement.Content = string.Join(";", configs);
            }

            // Refresh lại ListBox để thấy Preview thay đổi ngay lập tức
            if (lstElements != null) lstElements.Items.Refresh();
        }

        private void Prop_Changed(object sender, RoutedEventArgs e) => UpdateModelFromUI();
        private void Prop_Changed(object sender, SelectionChangedEventArgs e) => UpdateModelFromUI();
        private void TxtContent_TextChanged(object sender, TextChangedEventArgs e) => UpdateModelFromUI();

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement != null)
            {
                int idx = lstElements.SelectedIndex;
                _elements.Remove(_selectedElement);
                RefreshList(idx >= _elements.Count ? _elements.Count - 1 : idx);
            }
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = lstElements.SelectedIndex;
            if (idx > 0)
            {
                var item = _elements[idx];
                _elements.RemoveAt(idx);
                _elements.Insert(idx - 1, item);
                RefreshList(idx - 1);
            }
        }

        private void BtnDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = lstElements.SelectedIndex;
            if (idx >= 0 && idx < _elements.Count - 1)
            {
                var item = _elements[idx];
                _elements.RemoveAt(idx);
                _elements.Insert(idx + 1, item);
                RefreshList(idx + 1);
            }
        }

        private void BtnUploadImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null) return;
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string destFolder = Path.Combine(AppContext.BaseDirectory, "Images");
                    if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);
                    string ext = Path.GetExtension(dlg.FileName);
                    string newName = $"img_{DateTime.Now.Ticks}{ext}";
                    File.Copy(dlg.FileName, Path.Combine(destFolder, newName), true);

                    _selectedElement.Content = newName;
                    LoadImagePreview(newName);
                    UpdateModelFromUI();
                }
                catch (Exception ex) { MessageBox.Show("Lỗi: " + ex.Message); }
            }
        }

        void LoadImagePreview(string fileName)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Images", fileName);
                if (File.Exists(path)) imgPreview.Source = new BitmapImage(new Uri(path));
                else imgPreview.Source = null;
            }
            catch { if (imgPreview != null) imgPreview.Source = null; }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string templateType = TemplateType ?? "Bill";
            using (var db = new AppDbContext())
            {
                var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == templateType && t.IsActive);
                if (template == null)
                {
                    template = new PrintTemplate { TemplateName = templateType, TemplateType = templateType, IsActive = true };
                    db.PrintTemplates.Add(template);
                }
                var rawList = _elements.Select(vm => vm.ToModel()).ToList();
                template.TemplateContentJson = JsonSerializer.Serialize(rawList);
                db.SaveChanges();
                MessageBox.Show("✅ Đã lưu cấu trúc thành công!");
            }
        }
    }
}
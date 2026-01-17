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
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PosSystem.Main.Pages
{
    public class PrintElementViewModel : PrintElement, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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

        // [NEW] Props for QR
        public new string QRTextTop { get => base.QRTextTop; set { if(base.QRTextTop != value) { base.QRTextTop = value; OnPropertyChanged(); } } }
        public new string QRTextBottom { get => base.QRTextBottom; set { if(base.QRTextBottom != value) { base.QRTextBottom = value; OnPropertyChanged(); } } }
        public new int QRTextTopFontSize { get => base.QRTextTopFontSize; set { if(base.QRTextTopFontSize != value) { base.QRTextTopFontSize = value; OnPropertyChanged(); } } }
        public new int QRTextBottomFontSize { get => base.QRTextBottomFontSize; set { if(base.QRTextBottomFontSize != value) { base.QRTextBottomFontSize = value; OnPropertyChanged(); } } }

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

        // Logic display
        public Visibility IsTextVisible => (ElementType != "Separator" && ElementType != "SeparatorDashed" && ElementType != "Logo" && ElementType != "QRCode") ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSeparatorVisible => ElementType == "Separator" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsDashedSeparatorVisible => ElementType == "SeparatorDashed" ? Visibility.Visible : Visibility.Collapsed;
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
                    case "SeparatorDashed": return "[KẺ NÉT ĐỨT]";
                    case "Logo": return "[LOGO]";
                    case "QRCode": return "[MÃ QR]";
                    default: return ElementType;
                }
            }
        }

        public string DisplayPreview
        {
            get
            {
                if (ElementType == "Text")
                {
                    string s = Content ?? "";
                    return s.Replace("{Table}", "10").Replace("{Staff}", "Admin")
                            .Replace("{CheckInTime}", "09:30").Replace("{PrintTime}", DateTime.Now.ToString("HH:mm"))
                            .Replace("{Duration}", "45p").Replace("{Batch}", "1").Replace("{OrderId}", "1023")
                            .Replace("{Sender}", "Nhân viên A");
                }

                if (ElementType == "OrderDetails" || ElementType == "KitchenOrderDetails")
                {
                    string s = "Cà phê sữa đá          2        50,000";
                    if (ShowNote) s += "\n   (Ít ngọt)";
                    s += "\nSinh tố bơ             1        40,000\n----------------------------------------";
                    return s;
                }

                if (ElementType == "Total")
                {
                    string s = "";
                    if (ShowSubTotal) s += "Tạm tính:                 90,000\n";
                    if (ShowDiscount) s += "Giảm giá:                      0\n";
                    s += "TỔNG CỘNG:                90,000";
                    return s;
                }

                if (ElementType == "BatchNumber") return "ĐỢT: 1";
                return Content;
            }
        }

        // Helpers config parsing
        private string GetConfig(string key)
        {
            if (string.IsNullOrEmpty(Content)) return "";
            var parts = Content.Split(';');
            foreach (var p in parts) { var kv = p.Split('='); if (kv.Length == 2 && kv[0] == key) return kv[1]; }
            return "";
        }
        public bool ShowNote => GetConfig("ShowNote") != "False";
        public bool ShowItemSeparator => GetConfig("ItemSep") == "True"; // NEW
        public string SeparatorStyle => GetConfig("SepStyle") == "Dashed" ? "Dashed" : "Solid"; // NEW

        public bool ShowSubTotal => GetConfig("ShowSub") == "True";
        public bool ShowDiscount => GetConfig("ShowDisc") == "True";
        public int NoteFontSize { get { if (int.TryParse(GetConfig("NoteSize"), out int s)) return s; return Math.Max(10, FontSize - 2); } }
        public int SubFontSize { get { if (int.TryParse(GetConfig("SubSize"), out int s)) return s; return Math.Max(12, FontSize - 2); } }
        public int ItemFontSize { get { if (int.TryParse(GetConfig("ItemSize"), out int s)) return s; return FontSize; } }
        public int HeaderFontSize { get { if (int.TryParse(GetConfig("HeaderSize"), out int s)) return s; return FontSize; } }
        public int TotalHeaderFontSize { get { if (int.TryParse(GetConfig("TotalHeaderSize"), out int s)) return s; return FontSize; } }
        public int ColumnSpacing { get { if (int.TryParse(GetConfig("ColumnSpacing"), out int s)) return s; return 10; } }

        // [FIX] Map QR Props in Constructor
        public PrintElementViewModel(PrintElement origin)
        {
            this.ElementType = origin.ElementType;
            this.Content = origin.Content;
            this.FontSize = origin.FontSize;
            this.IsBold = origin.IsBold;
            this.Align = origin.Align;
            this.IsVisible = origin.IsVisible;
            this.ImageHeight = origin.ImageHeight;
            this.QRTextTop = origin.QRTextTop; // [FIX]
            this.QRTextBottom = origin.QRTextBottom; // [FIX]
            this.QRTextTopFontSize = origin.QRTextTopFontSize;
            this.QRTextBottomFontSize = origin.QRTextBottomFontSize;
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
                IsVisible = this.IsVisible,
                ImageHeight = this.ImageHeight,
                QRTextTop = this.QRTextTop,     
                QRTextBottom = this.QRTextBottom,
                QRTextTopFontSize = this.QRTextTopFontSize,
                QRTextBottomFontSize = this.QRTextBottomFontSize
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
                else rawElements = CreateDefaultLayout();
            }

            _elements = rawElements.Select(e => new PrintElementViewModel(e)).ToList();
            if (txtTitle != null) txtTitle.Text = TemplateType == "Kitchen" ? "THIẾT KẾ PHIẾU BẾP" : "THIẾT KẾ HÓA ĐƠN";
            FilterToolbox();
            RefreshList(-1);
        }

        private void FilterToolbox()
        {
            // Logic ẩn hiện các nút Toolbox dựa trên loại mẫu in (Bill/Kitchen)
            if (grpKitchen == null || grpBill == null) return;

            if (TemplateType == "Kitchen")
            {
                grpKitchen.Visibility = Visibility.Visible;
                grpBill.Visibility = Visibility.Collapsed;
            }
            else // Bill
            {
                grpKitchen.Visibility = Visibility.Collapsed;
                grpBill.Visibility = Visibility.Visible;
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
                    new PrintElement { ElementType = "Total" },
                    new PrintElement { ElementType = "Text", Content = "Cảm ơn quý khách!", FontSize = 12, Align = "Center" }
                };
            }
        }

        private void RefreshList(int selectIndex)
        {
            if (lstElements == null) return;
            lstElements.ItemsSource = null;
            lstElements.ItemsSource = _elements;
            if (selectIndex >= 0 && selectIndex < _elements.Count) 
            {
                lstElements.SelectedIndex = selectIndex;
                lstElements.ScrollIntoView(lstElements.SelectedItem);
            }
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
                    case "SeparatorDashed": newEl.ElementType = "SeparatorDashed"; newEl.Content = "- - - - - - - -"; break;

                    case "TextTable": newEl.Content = "Bàn: {Table}"; newEl.FontSize = 16; newEl.IsBold = true; break;
                    case "TextStaff": newEl.Content = "Thu ngân: {Staff}"; newEl.FontSize = 12; break;
                    case "TextSender": newEl.Content = "Người gửi: {Sender}"; newEl.FontSize = 12; break;
                    case "TextOrderId": newEl.Content = "Mã đơn: #{OrderId}"; newEl.FontSize = 12; break;
                    case "TextTime": newEl.Content = "Giờ đến: {CheckInTime} | Giờ in: {PrintTime}"; newEl.Align = "Center"; newEl.FontSize = 12; break;
                    case "TextPrintTime": newEl.Content = "Giờ in: {PrintTime}"; newEl.Align = "Center"; newEl.FontSize = 12; break;
                    case "TextDuration": newEl.Content = "Thời gian ngồi: {Duration}"; newEl.Align = "Center"; newEl.FontSize = 12; break;

                    case "BatchNumber": newEl.Content = "ĐỢT GỌI: {Batch}"; newEl.FontSize = 18; newEl.IsBold = true; newEl.Align = "Center"; break;

                    case "Logo":
                    case "QRCode":
                        newEl.ElementType = tag;
                        newEl.Content = tag;
                        newEl.Align = "Center";
                        newEl.ImageHeight = 200;
                        break;
                    case "OrderDetails":
                    case "KitchenOrderDetails":
                    case "Total":
                        newEl.ElementType = tag;
                        newEl.Content = "";
                        newEl.FontSize = 12; // Default font size
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

                // Reset các panel
                // Reset các panel
                if (pnlTextProp != null) pnlTextProp.Visibility = Visibility.Collapsed;
                if (pnlAlignProp != null) pnlAlignProp.Visibility = Visibility.Collapsed;
                if (pnlImageProp != null) pnlImageProp.Visibility = Visibility.Collapsed;
                if (pnlOptionProp != null) pnlOptionProp.Visibility = Visibility.Collapsed;

                // Bind dữ liệu chung
                if (chkVisible != null) chkVisible.IsChecked = el.IsVisible;

                if (el.ElementType == "Text")
                {
                    if (pnlAlignProp != null) 
                    {
                        pnlAlignProp.Visibility = Visibility.Visible;
                        if (cboAlign != null) cboAlign.SelectedIndex = el.Align == "Left" ? 0 : (el.Align == "Right" ? 2 : 1);
                    }
                    if (pnlTextProp != null) pnlTextProp.Visibility = Visibility.Visible;
                    if (txtContent != null) { txtContent.Text = el.Content; txtContent.IsEnabled = true; }
                    if (txtFontSize != null) txtFontSize.Text = el.FontSize.ToString();
                    if (chkBold != null) chkBold.IsChecked = el.IsBold;
                }
                else if (el.ElementType == "Logo" || el.ElementType == "QRCode")
                {
                    if (pnlImageProp != null) pnlImageProp.Visibility = Visibility.Visible;
                    LoadImagePreview(el.Content);
                    if (txtImageHeight != null) txtImageHeight.Text = el.ImageHeight.ToString();
                    
                    if (pnlAlignProp != null) 
                    {
                        pnlAlignProp.Visibility = Visibility.Visible;
                        if (cboAlign != null) cboAlign.SelectedIndex = el.Align == "Left" ? 0 : (el.Align == "Right" ? 2 : 1);
                    }

                    // [NEW] Bind QR Text
                    if (el.ElementType == "QRCode")
                    {
                        if (pnlQrText != null) pnlQrText.Visibility = Visibility.Visible;
                        if (txtQrTop != null) txtQrTop.Text = el.QRTextTop;
                        if (txtQrBottom != null) txtQrBottom.Text = el.QRTextBottom;
                        if (txtQrTopSize != null) txtQrTopSize.Text = el.QRTextTopFontSize.ToString(); // [NEW]
                        if (txtQrBottomSize != null) txtQrBottomSize.Text = el.QRTextBottomFontSize.ToString(); // [NEW]
                    }
                    else
                    {
                         if (pnlQrText != null) pnlQrText.Visibility = Visibility.Collapsed;
                    }
                }
                else if (el.ElementType == "Separator" || el.ElementType == "SeparatorDashed")
                {
                     // Separator ko có property gì mấy
                }
                else 
                {
                    // Các khối dữ liệu (List món, Total, Batch)
                    if (pnlOptionProp != null)
                    {
                         pnlOptionProp.Visibility = Visibility.Visible;

                         if (el.ElementType == "OrderDetails" || el.ElementType == "KitchenOrderDetails")
                         {
                             if (optOrderDetails != null) optOrderDetails.Visibility = Visibility.Visible;
                             if (optTotal != null) optTotal.Visibility = Visibility.Collapsed;

                             if (txtHeaderSize != null) txtHeaderSize.Text = el.HeaderFontSize.ToString();
                             if (txtItemSize != null) txtItemSize.Text = el.ItemFontSize.ToString();
                             if (chkShowNote != null) chkShowNote.IsChecked = el.ShowNote;
                             if (txtNoteSize != null) txtNoteSize.Text = el.NoteFontSize.ToString();
                             if (txtColumnSpacing != null) txtColumnSpacing.Text = el.ColumnSpacing.ToString();
                             if (chkOrderDetailsBold != null) chkOrderDetailsBold.IsChecked = el.IsBold;
                             
                             // [NEW] Bind Separator options
                             if (chkItemSep != null) chkItemSep.IsChecked = el.ShowItemSeparator;
                             if (cboItemSepStyle != null) cboItemSepStyle.SelectedIndex = (el.SeparatorStyle == "Dashed") ? 1 : 0;
                         }
                         else if (el.ElementType == "Total")
                         {
                             if (optOrderDetails != null) optOrderDetails.Visibility = Visibility.Collapsed;
                             if (optTotal != null) optTotal.Visibility = Visibility.Visible;

                             if (chkShowSubTotal != null) chkShowSubTotal.IsChecked = el.ShowSubTotal;
                             if (chkShowDiscount != null) chkShowDiscount.IsChecked = el.ShowDiscount;
                             if (chkTotalBold != null) chkTotalBold.IsChecked = el.IsBold;
                             if (txtTotalHeaderSize != null) txtTotalHeaderSize.Text = el.TotalHeaderFontSize.ToString();
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

            // Cập nhật các thuộc tính chung
            if (chkVisible != null) _selectedElement.IsVisible = chkVisible.IsChecked == true;
            
            if (_selectedElement.ElementType == "Text")
            {
                if (txtContent != null) _selectedElement.Content = txtContent.Text;
                if (cboAlign != null) _selectedElement.Align = cboAlign.SelectedIndex == 0 ? "Left" : (cboAlign.SelectedIndex == 2 ? "Right" : "Center");
                if (txtFontSize != null && int.TryParse(txtFontSize.Text, out int size)) _selectedElement.FontSize = size;
                if (chkBold != null) _selectedElement.IsBold = chkBold.IsChecked == true;
            }
            else if (_selectedElement.ElementType == "Logo" || _selectedElement.ElementType == "QRCode")
            {
                if (cboAlign != null) _selectedElement.Align = cboAlign.SelectedIndex == 0 ? "Left" : (cboAlign.SelectedIndex == 2 ? "Right" : "Center");
                if (txtImageHeight != null && int.TryParse(txtImageHeight.Text, out int h)) _selectedElement.ImageHeight = h;
                
                if (_selectedElement.ElementType == "QRCode")
                {
                     if (txtQrTop != null) _selectedElement.QRTextTop = txtQrTop.Text;
                     if (txtQrBottom != null) _selectedElement.QRTextBottom = txtQrBottom.Text;
                     if (txtQrTopSize != null && int.TryParse(txtQrTopSize.Text, out int tSize)) _selectedElement.QRTextTopFontSize = tSize;
                     if (txtQrBottomSize != null && int.TryParse(txtQrBottomSize.Text, out int bSize)) _selectedElement.QRTextBottomFontSize = bSize;
                }
            }
            else if (_selectedElement.ElementType == "OrderDetails" || _selectedElement.ElementType == "KitchenOrderDetails")
            {
                List<string> configs = new List<string>();
                if (txtHeaderSize != null && int.TryParse(txtHeaderSize.Text, out int hSize)) configs.Add($"HeaderSize={hSize}");
                if (txtItemSize != null && int.TryParse(txtItemSize.Text, out int iSize)) configs.Add($"ItemSize={iSize}");
                if (chkShowNote != null && chkShowNote.IsChecked == false) configs.Add("ShowNote=False");
                if (txtNoteSize != null && int.TryParse(txtNoteSize.Text, out int nSize)) configs.Add($"NoteSize={nSize}");
                if (txtColumnSpacing != null && int.TryParse(txtColumnSpacing.Text, out int cSpacing)) configs.Add($"ColumnSpacing={cSpacing}");
                
                // [NEW] Save Separator options
                if (chkItemSep != null && chkItemSep.IsChecked == true) configs.Add("ItemSep=True");
                if (cboItemSepStyle != null && cboItemSepStyle.SelectedIndex == 1) configs.Add("SepStyle=Dashed");

                _selectedElement.Content = string.Join(";", configs);
                if (chkOrderDetailsBold != null) _selectedElement.IsBold = chkOrderDetailsBold.IsChecked == true;
            }
            else if (_selectedElement.ElementType == "Total")
            {
                List<string> configs = new List<string>();
                if (chkShowSubTotal != null && chkShowSubTotal.IsChecked == true) configs.Add("ShowSub=True");
                if (chkShowDiscount != null && chkShowDiscount.IsChecked == true) configs.Add("ShowDisc=True");
                if (txtTotalHeaderSize != null && int.TryParse(txtTotalHeaderSize.Text, out int tHSize)) configs.Add($"TotalHeaderSize={tHSize}");
                if (txtSubSize != null && int.TryParse(txtSubSize.Text, out int sSize)) configs.Add($"SubSize={sSize}");
                
                _selectedElement.Content = string.Join(";", configs);
                if (chkTotalBold != null) _selectedElement.IsBold = chkTotalBold.IsChecked == true;
            }
        }

        private void TxtContent_TextChanged(object sender, TextChangedEventArgs e) => UpdateModelFromUI();
        private void Prop_Changed(object sender, RoutedEventArgs e) => UpdateModelFromUI();
        private void Prop_Changed(object sender, SelectionChangedEventArgs e) => UpdateModelFromUI();

        private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

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
                    _selectedElement.Content = dlg.FileName;
                    LoadImagePreview(dlg.FileName);
                    UpdateModelFromUI();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi: " + ex.Message);
                }
            }
        }

        void LoadImagePreview(string filePath)
        {
            try
            {
                string path = filePath;
                if (!Path.IsPathRooted(filePath))
                {
                    path = Path.Combine(AppContext.BaseDirectory, "Images", filePath);
                }

                if (File.Exists(path))
                    imgPreview.Source = new BitmapImage(new Uri(path));
                else
                    imgPreview.Source = null;
            }
            catch
            {
                if (imgPreview != null) imgPreview.Source = null;
            }
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
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

        private List<PrintElement> _elements = new List<PrintElement>();
        private PrintElement? _selectedElement;
        private bool _isInternalUpdate = false;

        public LayoutDesignerPage()
        {
            InitializeComponent();
            this.Loaded += LayoutDesignerPage_Loaded;
        }

        private void LayoutDesignerPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLayout();
        }

        private static void OnTemplateTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LayoutDesignerPage page && page.IsLoaded)
            {
                page.LoadLayout();
            }
        }

        private void LoadLayout()
        {
            string templateType = TemplateType ?? "Bill";
            
            using (var db = new AppDbContext())
            {
                var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == templateType && t.IsActive);

                if (template != null && !string.IsNullOrEmpty(template.TemplateContentJson))
                {
                    try
                    {
                        _elements = JsonSerializer.Deserialize<List<PrintElement>>(template.TemplateContentJson)
                                    ?? CreateDefaultLayout();
                    }
                    catch
                    {
                        _elements = CreateDefaultLayout();
                    }
                }
                else
                {
                    _elements = CreateDefaultLayout();
                }

                RefreshList(-1);
                UpdateUITitle();
                UpdateElementTypeFilter();
            }
        }

        private void UpdateUITitle()
        {
            string title = TemplateType == "Kitchen" ? "CẤU TRÚC PHIẾU BẾP" : "CẤU TRÚC HÓA ĐƠN";
            if (txtTitle != null)
            {
                txtTitle.Text = title;
            }
        }

        private void UpdateElementTypeFilter()
        {
            if (cboType == null) return;
            
            // Filter element types based on TemplateType
            if (TemplateType == "Kitchen")
            {
                // For kitchen, hide bill-specific items
                foreach (ComboBoxItem item in cboType.Items)
                {
                    string content = item.Content?.ToString() ?? "";
                    // Hide OrderInfo, OrderDetails, Total for kitchen (show KitchenOrderInfo, KitchenOrderDetails instead)
                    if (content.Contains("OrderInfo (Thông tin bàn)") && !content.Contains("Kitchen"))
                    {
                        item.Visibility = Visibility.Collapsed;
                    }
                    else if (content.Contains("OrderDetails (List món)") && !content.Contains("Kitchen"))
                    {
                        item.Visibility = Visibility.Collapsed;
                    }
                    else if (content.Contains("Total"))
                    {
                        item.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        item.Visibility = Visibility.Visible;
                    }
                }
            }
            else
            {
                // For bill, hide kitchen-specific items
                foreach (ComboBoxItem item in cboType.Items)
                {
                    string content = item.Content?.ToString() ?? "";
                    if (content.Contains("Kitchen") || content.Contains("BatchNumber"))
                    {
                        item.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        item.Visibility = Visibility.Visible;
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
                    new PrintElement { ElementType = "Text", Content = "PHIẾU CHẾ BIẾN", FontSize = 24, IsBold = true, Align = "Center" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "KitchenOrderInfo" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "KitchenOrderDetails" }
                };
            }
            else
            {
                return new List<PrintElement>
                {
                    new PrintElement { ElementType = "Text", Content = "TÊN QUÁN CỦA BẠN", FontSize = 24, IsBold = true, Align = "Center" },
                    new PrintElement { ElementType = "Text", Content = "ĐC: Địa chỉ quán...", FontSize = 14, Align = "Center" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "OrderInfo" },
                    new PrintElement { ElementType = "OrderDetails" },
                    new PrintElement { ElementType = "Separator" },
                    new PrintElement { ElementType = "Total" },
                    new PrintElement { ElementType = "Text", Content = "Xin cảm ơn quý khách!", FontSize = 14, Align = "Center" }
                };
            }
        }

        private void RefreshList(int selectIndex)
        {
            lstElements.ItemsSource = null;
            lstElements.ItemsSource = _elements;

            if (selectIndex >= 0 && selectIndex < _elements.Count)
            {
                lstElements.SelectedIndex = selectIndex;
            }
        }

        private void LstElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstElements.SelectedItem is PrintElement el)
            {
                _selectedElement = el;
                _isInternalUpdate = true;

                pnlProperties.IsEnabled = true;

                foreach (ComboBoxItem item in cboType.Items)
                {
                    if (item.Content?.ToString()?.StartsWith(el.ElementType) == true)
                    {
                        cboType.SelectedItem = item;
                        break;
                    }
                }

                cboAlign.SelectedIndex = el.Align == "Left" ? 0 : (el.Align == "Right" ? 2 : 1);

                txtFontSize.Text = el.FontSize.ToString();
                chkBold.IsChecked = el.IsBold;
                chkVisible.IsChecked = el.IsVisible;

                if (el.ElementType == "Text")
                {
                    pnlTextProp.Visibility = Visibility.Visible;
                    pnlImageProp.Visibility = Visibility.Collapsed;
                    txtContent.Text = el.Content;
                }
                else if (el.ElementType == "Logo" || el.ElementType == "QRCode")
                {
                    pnlTextProp.Visibility = Visibility.Collapsed;
                    pnlImageProp.Visibility = Visibility.Visible;

                    lblImgPath.Text = el.Content;
                    LoadImagePreview(el.Content);
                }
                else
                {
                    pnlTextProp.Visibility = Visibility.Collapsed;
                    pnlImageProp.Visibility = Visibility.Collapsed;
                }

                _isInternalUpdate = false;
            }
            else
            {
                _selectedElement = null;
                pnlProperties.IsEnabled = false;
            }
        }

        private void CboType_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateElementTypeFilter();
        }

        private void CboType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInternalUpdate || _selectedElement == null) return;

            var selectedItem = cboType.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            string raw = selectedItem.Content?.ToString() ?? "Text";
            string newType = raw.Split(' ')[0];

            _selectedElement.ElementType = newType;

            if (newType == "Text" && (_selectedElement.Content.Contains(".png") || _selectedElement.Content.Contains(".jpg")))
            {
                _selectedElement.Content = "Văn bản mới";
            }

            LstElements_SelectionChanged(null, null);
            lstElements.Items.Refresh();
        }

        private void TxtContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInternalUpdate || _selectedElement == null) return;
            _selectedElement.Content = txtContent.Text;
        }

        private void Prop_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModelFromUI();
        }

        private void UpdateModelFromUI()
        {
            if (_isInternalUpdate || _selectedElement == null) return;

            _selectedElement.Align = cboAlign.SelectedIndex == 0 ? "Left" : (cboAlign.SelectedIndex == 2 ? "Right" : "Center");

            if (int.TryParse(txtFontSize.Text, out int size)) _selectedElement.FontSize = size;

            _selectedElement.IsBold = chkBold.IsChecked == true;
            _selectedElement.IsVisible = chkVisible.IsChecked == true;

            lstElements.Items.Refresh();
        }

        private void BtnUploadImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null) return;

            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Chọn ảnh Logo hoặc QR Code"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string destFolder = Path.Combine(AppContext.BaseDirectory, "Images");
                    if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                    string ext = Path.GetExtension(dlg.FileName);
                    string newName = $"img_{DateTime.Now.Ticks}{ext}";
                    string destPath = Path.Combine(destFolder, newName);

                    File.Copy(dlg.FileName, destPath, true);

                    _selectedElement.Content = newName;
                    lblImgPath.Text = newName;

                    LoadImagePreview(newName);

                    lstElements.Items.Refresh();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi upload ảnh: " + ex.Message);
                }
            }
        }

        void LoadImagePreview(string fileName)
        {
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
            catch
            {
                imgPreview.Source = null;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _elements.Add(new PrintElement { ElementType = "Text", Content = "Dòng mới", FontSize = 14 });
            RefreshList(_elements.Count - 1);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement != null)
            {
                _elements.Remove(_selectedElement);
                RefreshList(-1);
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

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            UpdateModelFromUI();

            string templateType = TemplateType ?? "Bill";
            string successMessage = templateType == "Kitchen" 
                ? "✅ Đã lưu cấu trúc phiếu bếp thành công!" 
                : "✅ Đã lưu cấu trúc hóa đơn thành công!";

            using (var db = new AppDbContext())
            {
                var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == templateType && t.IsActive);

                if (template == null)
                {
                    template = new PrintTemplate
                    {
                        TemplateName = templateType == "Kitchen" ? "Mẫu Bếp" : "Mẫu Hóa Đơn",
                        TemplateType = templateType,
                        TemplateContentJson = "",
                        IsActive = true
                    };
                    db.PrintTemplates.Add(template);
                }

                template.TemplateContentJson = JsonSerializer.Serialize(_elements);

                db.SaveChanges();
                MessageBox.Show(successMessage);
            }
        }
    }
}
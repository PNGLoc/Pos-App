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
        private List<PrintElement> _elements = new List<PrintElement>();
        private PrintElement? _selectedElement;
        private bool _isInternalUpdate = false;

        public LayoutDesignerPage()
        {
            InitializeComponent();
            LoadLayout();
        }

        private void LoadLayout()
        {
            using (var db = new AppDbContext())
            {
                var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == "Bill" && t.IsActive);

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
            }
        }

        private List<PrintElement> CreateDefaultLayout()
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

            using (var db = new AppDbContext())
            {
                var template = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == "Bill" && t.IsActive);

                if (template == null)
                {
                    template = new PrintTemplate
                    {
                        TemplateName = "Mẫu Tùy Chỉnh",
                        TemplateType = "Bill",
                        TemplateContentJson = "",
                        IsActive = true
                    };
                    db.PrintTemplates.Add(template);
                }

                template.TemplateContentJson = JsonSerializer.Serialize(_elements);

                db.SaveChanges();
                MessageBox.Show("✅ Đã lưu cấu trúc hóa đơn thành công!");
            }
        }
    }
}